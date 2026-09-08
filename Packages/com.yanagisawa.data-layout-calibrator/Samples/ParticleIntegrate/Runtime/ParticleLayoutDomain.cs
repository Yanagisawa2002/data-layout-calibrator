using ParticleAoSoA8Block = Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate.ParticleRecordGeneratedPackedAoSoA8Block;
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate
{
    internal enum ParticleKernelKind
    {
        ScalarBranched = 0,
        ScalarBranchless = 1,
        PackedBranchless8 = 2,
        PackedBranchless4 = 3,
        PackedBranchless16 = 4,
    }

    /// <summary>
    /// Owns one persistent representation of the particle workload. Layout selection
    /// remains managed; every hot schedule site targets a concrete Burst job.
    /// </summary>
    public sealed partial class ParticleLayoutDomain : IDisposable
    {
        private ParticleAoSStorage _aos;
        private ParticleSoAStorage _soa;
        private ParticleAoSoA8Storage _aosoa8;
        private ParticleRecordGeneratedPackedAoSoA4Storage _aosoa4;
        private ParticleRecordGeneratedPackedAoSoA16Storage _aosoa16;
        private ParticleRecordGeneratedPadded64Storage _padded64;
        private bool _disposed;

        private ParticleLayoutDomain(
            LayoutKind layout,
            ParticleKernelKind kernel,
            int logicalBatchSize)
        {
            Layout = layout;
            Kernel = kernel;
            LogicalBatchSize = Math.Max(1, logicalBatchSize);
        }

        public LayoutKind Layout { get; }

        internal ParticleKernelKind Kernel { get; }

        /// <summary>
        /// Batch size expressed in logical records, not physical AoSoA blocks.
        /// </summary>
        public int LogicalBatchSize { get; }

        public int Count
        {
            get
            {
                ThrowIfDisposed();
                switch (Layout)
                {
                    case LayoutKind.AoSPadded64: return _padded64.Count;
                    case LayoutKind.AoS: return _aos.Count;
                    case LayoutKind.SoA: return _soa.Count;
                    case LayoutKind.AoSoA4: return _aosoa4.Count;
                    case LayoutKind.AoSoA16: return _aosoa16.Count;
                    case LayoutKind.AoSoA8: return _aosoa8.Count;
                    default: throw new ArgumentOutOfRangeException();
                }
            }
        }

        public long ResidentBytes
        {
            get
            {
                ThrowIfDisposed();
                switch (Layout)
                {
                    case LayoutKind.AoSPadded64: return (long)_padded64.Count * UnsafeUtility.SizeOf<ParticleRecordGeneratedPadded64Record>();
                    case LayoutKind.AoS:
                        return (long)_aos.Count * UnsafeUtility.SizeOf<ParticleRecord>();
                    case LayoutKind.SoA:
                        return (long)_soa.Count *
                               ((UnsafeUtility.SizeOf<float3>() * 2) +
                                UnsafeUtility.SizeOf<quaternion>() +
                                UnsafeUtility.SizeOf<float>() +
                                UnsafeUtility.SizeOf<int>());
                    case LayoutKind.AoSoA4:
                        return (long)_aosoa4.BlockCount * UnsafeUtility.SizeOf<ParticleRecordGeneratedPackedAoSoA4Block>() + (long)_aosoa4.Count * 20;
                    case LayoutKind.AoSoA16:
                        return (long)_aosoa16.BlockCount * UnsafeUtility.SizeOf<ParticleRecordGeneratedPackedAoSoA16Block>() + (long)_aosoa16.Count * 20;
                    case LayoutKind.AoSoA8:
                        return ((long)_aosoa8.BlockCount * UnsafeUtility.SizeOf<ParticleAoSoA8Block>()) +
                               ((long)_aosoa8.Count *
                                (UnsafeUtility.SizeOf<quaternion>() + UnsafeUtility.SizeOf<int>()));
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public static ParticleLayoutDomain Create(
            LayoutKind layout,
            int logicalBatchSize,
            NativeArray<ParticleRecord> source,
            Allocator allocator = Allocator.Persistent)
        {
            return Create(
                layout,
                DefaultKernelForLayout(layout),
                logicalBatchSize,
                source,
                allocator);
        }

        internal static ParticleLayoutDomain Create(
            LayoutKind layout,
            ParticleKernelKind kernel,
            int logicalBatchSize,
            NativeArray<ParticleRecord> source,
            Allocator allocator = Allocator.Persistent)
        {
            if (!source.IsCreated)
                throw new ArgumentException("Source records are not created.", nameof(source));

            ValidateKernel(layout, kernel);

            var domain = new ParticleLayoutDomain(layout, kernel, logicalBatchSize);
            try
            {
                switch (layout)
                {
                    case LayoutKind.AoSPadded64:
                        domain._padded64 = ParticleRecordGeneratedPadded64Storage.FromRecords(source, allocator);
                        break;
                    case LayoutKind.AoS:
                        domain._aos = ParticleAoSStorage.FromRecords(source, allocator);
                        break;
                    case LayoutKind.SoA:
                        domain._soa = ParticleSoAStorage.FromRecords(source, allocator);
                        break;
                    case LayoutKind.AoSoA4:
                        domain._aosoa4 = ParticleRecordGeneratedPackedAoSoA4Storage.FromRecords(source, allocator);
                        break;
                    case LayoutKind.AoSoA16:
                        domain._aosoa16 = ParticleRecordGeneratedPackedAoSoA16Storage.FromRecords(source, allocator);
                        break;
                    case LayoutKind.AoSoA8:
                        domain._aosoa8 = ParticleAoSoA8Storage.FromRecords(source, allocator);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(layout), layout, null);
                }

                return domain;
            }
            catch
            {
                domain.Dispose();
                throw;
            }
        }

        public JobHandle Schedule(float deltaTime, JobHandle dependency = default)
        {
            ThrowIfDisposed();
            switch (Layout)
            {
                case LayoutKind.AoS:
                    if (Kernel == ParticleKernelKind.ScalarBranched)
                    {
                        return ParticleJobScheduler.Schedule(
                            ref _aos,
                            LogicalBatchSize,
                            deltaTime,
                            dependency);
                    }
                    return ParticleJobScheduler.ScheduleBranchless(
                        ref _aos,
                        LogicalBatchSize,
                        deltaTime,
                        dependency);
                case LayoutKind.SoA:
                    if (Kernel == ParticleKernelKind.ScalarBranchless)
                        return new ParticleSoABranchlessStepJob { Positions = _soa.Positions, Velocities = _soa.Velocities,
                            Lifetimes = _soa.Lifetimes, DeltaTime = deltaTime }.Schedule(_soa.Count, LogicalBatchSize, dependency);
                    return ParticleJobScheduler.Schedule(ref _soa, LogicalBatchSize, deltaTime, dependency);
                case LayoutKind.AoSPadded64:
                    if (Kernel == ParticleKernelKind.ScalarBranched)
                        return new ParticlePadded64BranchedStepJob { Records = _padded64.Records, DeltaTime = deltaTime }.Schedule(Count, LogicalBatchSize, dependency);
                    return new ParticlePadded64BranchlessStepJob { Records = _padded64.Records, DeltaTime = deltaTime }.Schedule(Count, LogicalBatchSize, dependency);
                case LayoutKind.AoSoA4:
                    if (Kernel == ParticleKernelKind.ScalarBranched)
                        return new ParticleAoSoA4ScalarBranchedStepJob { Blocks = _aosoa4.HotBlocks, DeltaTime = deltaTime }.Schedule(_aosoa4.BlockCount, Math.Max(1, LogicalBatchSize / 4), dependency);
                    if (Kernel == ParticleKernelKind.ScalarBranchless)
                        return new ParticleAoSoA4ScalarBranchlessStepJob { Blocks = _aosoa4.HotBlocks, DeltaTime = deltaTime }.Schedule(_aosoa4.BlockCount, Math.Max(1, LogicalBatchSize / 4), dependency);
                    return new ParticleAoSoA4StepJob { Blocks = _aosoa4.HotBlocks, DeltaTime = deltaTime }.Schedule(_aosoa4.BlockCount, Math.Max(1, LogicalBatchSize / 4), dependency);
                case LayoutKind.AoSoA8:
                    if (Kernel == ParticleKernelKind.ScalarBranched)
                        return new ParticleAoSoA8ScalarBranchedStepJob { Blocks = _aosoa8.HotBlocks, DeltaTime = deltaTime }.Schedule(_aosoa8.BlockCount, Math.Max(1, LogicalBatchSize / 8), dependency);
                    if (Kernel == ParticleKernelKind.ScalarBranchless)
                        return new ParticleAoSoA8ScalarBranchlessStepJob { Blocks = _aosoa8.HotBlocks, DeltaTime = deltaTime }.Schedule(_aosoa8.BlockCount, Math.Max(1, LogicalBatchSize / 8), dependency);
                    return new ParticleAoSoA8StepJob { Blocks = _aosoa8.HotBlocks, DeltaTime = deltaTime }.Schedule(_aosoa8.BlockCount, Math.Max(1, LogicalBatchSize / 8), dependency);
                case LayoutKind.AoSoA16:
                    if (Kernel == ParticleKernelKind.ScalarBranched)
                        return new ParticleAoSoA16ScalarBranchedStepJob { Blocks = _aosoa16.HotBlocks, DeltaTime = deltaTime }.Schedule(_aosoa16.BlockCount, Math.Max(1, LogicalBatchSize / 16), dependency);
                    if (Kernel == ParticleKernelKind.ScalarBranchless)
                        return new ParticleAoSoA16ScalarBranchlessStepJob { Blocks = _aosoa16.HotBlocks, DeltaTime = deltaTime }.Schedule(_aosoa16.BlockCount, Math.Max(1, LogicalBatchSize / 16), dependency);
                    return new ParticleAoSoA16StepJob { Blocks = _aosoa16.HotBlocks, DeltaTime = deltaTime }.Schedule(_aosoa16.BlockCount, Math.Max(1, LogicalBatchSize / 16), dependency);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void Ingress(NativeArray<ParticleRecord> source)
        {
            ThrowIfDisposed();
            if (!source.IsCreated || source.Length != Count)
                throw new ArgumentException("Source must be created with the logical record count.", nameof(source));

            switch (Layout)
            {
                case LayoutKind.AoS:
                    _aos.Records.CopyFrom(source);
                    return;
                case LayoutKind.SoA:
                    ParticleBoundaryJobScheduler.ScheduleIngress(source, ref _soa).Complete();
                    return;
                case LayoutKind.AoSoA4:
                    new ParticleAoSoA4IngressJob { HotBlocks = _aosoa4.HotBlocks, Rotations = _aosoa4.Cold_Rotation, Categories = _aosoa4.Cold_Category, Source = source, LogicalCount = Count }.Schedule(_aosoa4.BlockCount, 32).Complete();
                    return;
                case LayoutKind.AoSoA16:
                    new ParticleAoSoA16IngressJob { HotBlocks = _aosoa16.HotBlocks, Rotations = _aosoa16.Cold_Rotation, Categories = _aosoa16.Cold_Category, Source = source, LogicalCount = Count }.Schedule(_aosoa16.BlockCount, 8).Complete();
                    return;
                case LayoutKind.AoSPadded64:
                    new ParticlePadded64IngressJob { Records = _padded64.Records, Source = source }.Schedule(Count, 128).Complete();
                    return;
                case LayoutKind.AoSoA8:
                    ParticleBoundaryJobScheduler.ScheduleIngress(source, ref _aosoa8).Complete();
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void Export(NativeArray<ParticleRecord> destination)
        {
            ThrowIfDisposed();
            if (!destination.IsCreated || destination.Length != Count)
                throw new ArgumentException("Destination must be created with the logical record count.", nameof(destination));

            switch (Layout)
            {
                case LayoutKind.AoS:
                    destination.CopyFrom(_aos.Records);
                    return;
                case LayoutKind.SoA:
                    ParticleBoundaryJobScheduler.ScheduleExport(ref _soa, destination).Complete();
                    return;
                case LayoutKind.AoSoA4:
                    new ParticleAoSoA4ExportJob { HotBlocks = _aosoa4.HotBlocks, Rotations = _aosoa4.Cold_Rotation, Categories = _aosoa4.Cold_Category, Destination = destination }.Schedule(Count, 128).Complete();
                    return;
                case LayoutKind.AoSoA16:
                    new ParticleAoSoA16ExportJob { HotBlocks = _aosoa16.HotBlocks, Rotations = _aosoa16.Cold_Rotation, Categories = _aosoa16.Cold_Category, Destination = destination }.Schedule(Count, 128).Complete();
                    return;
                case LayoutKind.AoSPadded64:
                    new ParticlePadded64ExportJob { Records = _padded64.Records, Destination = destination }.Schedule(Count, 128).Complete();
                    return;
                case LayoutKind.AoSoA8:
                    ParticleBoundaryJobScheduler.ScheduleExport(ref _aosoa8, destination).Complete();
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public ParticleRecord ReadRecord(int index)
        {
            ThrowIfDisposed();
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            switch (Layout)
            {
                case LayoutKind.AoSPadded64: return _padded64.ReadRecord(index);
                case LayoutKind.AoS: return _aos.ReadRecord(index);
                case LayoutKind.SoA: return _soa.ReadRecord(index);
                case LayoutKind.AoSoA4: return _aosoa4.ReadRecord(index);
                case LayoutKind.AoSoA16: return _aosoa16.ReadRecord(index);
                case LayoutKind.AoSoA8: return _aosoa8.ReadRecord(index);
                default: throw new ArgumentOutOfRangeException();
            }
        }

        public void CopyTo(NativeArray<ParticleRecord> destination)
        {
            Export(destination);
        }

        public ulong ComputeQuantizedHash()
        {
            ThrowIfDisposed();
            ulong hash = ParticleStateValidation.BeginHash();
            for (int index = 0; index < Count; index++)
                hash = ParticleStateValidation.AppendRecordHash(hash, ReadRecord(index));
            return hash;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _aosoa4.Dispose();
            _aosoa16.Dispose();
            _padded64.Dispose();
            _aos.Dispose();
            _soa.Dispose();
            _aosoa8.Dispose();
            _disposed = true;
        }

        private static ParticleKernelKind DefaultKernelForLayout(LayoutKind layout)
        {
            switch (layout)
            {
                case LayoutKind.AoSPadded64:
                case LayoutKind.AoSoA4:
                case LayoutKind.AoSoA16:
                case LayoutKind.AoS:
                case LayoutKind.SoA:
                    return ParticleKernelKind.ScalarBranched;
                case LayoutKind.AoSoA8:
                    return ParticleKernelKind.PackedBranchless8;
                default:
                    throw new ArgumentOutOfRangeException(nameof(layout), layout, null);
            }
        }

        private static void ValidateKernel(LayoutKind layout, ParticleKernelKind kernel)
        {
            bool valid = Enum.IsDefined(typeof(LayoutKind), layout) &&
                (kernel == ParticleKernelKind.ScalarBranched || kernel == ParticleKernelKind.ScalarBranchless ||
                 (layout == LayoutKind.AoSoA4 && kernel == ParticleKernelKind.PackedBranchless4) ||
                 (layout == LayoutKind.AoSoA8 && kernel == ParticleKernelKind.PackedBranchless8) ||
                 (layout == LayoutKind.AoSoA16 && kernel == ParticleKernelKind.PackedBranchless16));
            if (!valid)
            {
                throw new ArgumentException(
                    $"Kernel {kernel} is not implemented for layout {layout}.",
                    nameof(kernel));
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ParticleLayoutDomain));
        }
    }
}
