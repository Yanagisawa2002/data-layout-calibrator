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
    }

    /// <summary>
    /// Owns one persistent representation of the particle workload. Layout selection
    /// remains managed; every hot schedule site targets a concrete Burst job.
    /// </summary>
    public sealed class ParticleLayoutDomain : IDisposable
    {
        private ParticleAoSStorage _aos;
        private ParticleSoAStorage _soa;
        private ParticleAoSoA8Storage _aosoa8;
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
                    case LayoutKind.AoS: return _aos.Count;
                    case LayoutKind.SoA: return _soa.Count;
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
                    case LayoutKind.AoS:
                        return (long)_aos.Count * UnsafeUtility.SizeOf<ParticleRecord>();
                    case LayoutKind.SoA:
                        return (long)_soa.Count *
                               ((UnsafeUtility.SizeOf<float3>() * 2) +
                                UnsafeUtility.SizeOf<quaternion>() +
                                UnsafeUtility.SizeOf<float>() +
                                UnsafeUtility.SizeOf<int>());
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
                    case LayoutKind.AoS:
                        domain._aos = ParticleAoSStorage.FromRecords(source, allocator);
                        break;
                    case LayoutKind.SoA:
                        domain._soa = ParticleSoAStorage.FromRecords(source, allocator);
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
                    return ParticleJobScheduler.Schedule(
                        ref _soa,
                        LogicalBatchSize,
                        deltaTime,
                        dependency);
                case LayoutKind.AoSoA8:
                    return ParticleJobScheduler.Schedule(
                        ref _aosoa8,
                        LogicalBatchSize,
                        deltaTime,
                        dependency);
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
                case LayoutKind.AoS: return _aos.ReadRecord(index);
                case LayoutKind.SoA: return _soa.ReadRecord(index);
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
            switch (Layout)
            {
                case LayoutKind.AoS: return ParticleStateValidation.ComputeHash(ref _aos);
                case LayoutKind.SoA: return ParticleStateValidation.ComputeHash(ref _soa);
                case LayoutKind.AoSoA8: return ParticleStateValidation.ComputeHash(ref _aosoa8);
                default: throw new ArgumentOutOfRangeException();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _aos.Dispose();
            _soa.Dispose();
            _aosoa8.Dispose();
            _disposed = true;
        }

        private static ParticleKernelKind DefaultKernelForLayout(LayoutKind layout)
        {
            switch (layout)
            {
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
            bool valid =
                (layout == LayoutKind.AoS &&
                 (kernel == ParticleKernelKind.ScalarBranched ||
                  kernel == ParticleKernelKind.ScalarBranchless)) ||
                (layout == LayoutKind.SoA && kernel == ParticleKernelKind.ScalarBranched) ||
                (layout == LayoutKind.AoSoA8 && kernel == ParticleKernelKind.PackedBranchless8);
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
