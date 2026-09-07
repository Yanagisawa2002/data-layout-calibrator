using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate
{
    public sealed partial class ParticleLayoutDomain
    {
        /// <summary>Actual layout-owned cold reads/writes; its dispatch is part of resident time.</summary>
        public JobHandle ScheduleColdFields(JobHandle dependency = default)
        {
            ThrowIfDisposed();
            switch (Layout)
            {
                case LayoutKind.AoS:
                    return new ParticleColdAoSJob { Records = _aos.Records }
                        .Schedule(Count, LogicalBatchSize, dependency);
                case LayoutKind.SoA:
                    return ScheduleColdArrays(_soa.Rotations, _soa.Categories, dependency);
                case LayoutKind.AoSoA8:
                    return ScheduleColdArrays(_aosoa8.Rotations, _aosoa8.Categories, dependency);
                case LayoutKind.AoSoA4:
                    return ScheduleColdArrays(_aosoa4.Cold_Rotation, _aosoa4.Cold_Category, dependency);
                case LayoutKind.AoSoA16:
                    return ScheduleColdArrays(_aosoa16.Cold_Rotation, _aosoa16.Cold_Category, dependency);
                case LayoutKind.AoSPadded64:
                    return new ParticleColdPaddedJob { Records = _padded64.Records }
                        .Schedule(Count, LogicalBatchSize, dependency);
                default: throw new NotSupportedException("Cold access is not implemented for " + Layout);
            }
        }

        private JobHandle ScheduleColdArrays(NativeArray<quaternion> rotations,
            NativeArray<int> categories, JobHandle dependency) =>
            new ParticleColdSplitJob { Rotations = rotations, Categories = categories }
                .Schedule(Count, LogicalBatchSize, dependency);
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    internal struct ParticleColdAoSJob : IJobParallelFor
    {
        public NativeArray<ParticleRecord> Records;
        public void Execute(int index)
        {
            ParticleRecord record = Records[index];
            record.Rotation.value += new float4(0.0001f, -0.0001f, 0.0002f, -0.0002f);
            record.Category = unchecked(record.Category + 1);
            Records[index] = record;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    internal struct ParticleColdPaddedJob : IJobParallelFor
    {
        public NativeArray<ParticleRecordGeneratedPadded64Record> Records;
        public void Execute(int index)
        {
            ParticleRecordGeneratedPadded64Record record = Records[index];
            record.Value.Rotation.value += new float4(0.0001f, -0.0001f, 0.0002f, -0.0002f);
            record.Value.Category = unchecked(record.Value.Category + 1);
            Records[index] = record;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    internal struct ParticleColdSplitJob : IJobParallelFor
    {
        public NativeArray<quaternion> Rotations;
        public NativeArray<int> Categories;
        public void Execute(int index)
        {
            quaternion rotation = Rotations[index];
            rotation.value += new float4(0.0001f, -0.0001f, 0.0002f, -0.0002f);
            Rotations[index] = rotation;
            Categories[index] = unchecked(Categories[index] + 1);
        }
    }
}
