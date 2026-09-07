using ParticleAoSoA8Block = Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate.ParticleRecordGeneratedPackedAoSoA8Block;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate
{
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleSoAIngressJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ParticleRecord> Source;
        [WriteOnly] public NativeArray<float3> Positions;
        [WriteOnly] public NativeArray<float3> Velocities;
        [WriteOnly] public NativeArray<quaternion> Rotations;
        [WriteOnly] public NativeArray<float> Lifetimes;
        [WriteOnly] public NativeArray<int> Categories;

        public void Execute(int index)
        {
            var storage = new ParticleRecordGeneratedSoAStorage
            {
                Field_Position = Positions, Field_Velocity = Velocities,
                Field_Rotation = Rotations, Field_Lifetime = Lifetimes, Field_Category = Categories,
            };
            storage.WriteRecord(index, Source[index]);
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleSoAExportJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<float3> Velocities;
        [ReadOnly] public NativeArray<quaternion> Rotations;
        [ReadOnly] public NativeArray<float> Lifetimes;
        [ReadOnly] public NativeArray<int> Categories;
        [WriteOnly] public NativeArray<ParticleRecord> Destination;

        public void Execute(int index)
        {
            var storage = new ParticleRecordGeneratedSoAStorage
            {
                Field_Position = Positions, Field_Velocity = Velocities,
                Field_Rotation = Rotations, Field_Lifetime = Lifetimes, Field_Category = Categories,
            };
            Destination[index] = storage.ReadRecord(index);
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA8IngressJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ParticleRecord> Source;
        [WriteOnly] public NativeArray<ParticleAoSoA8Block> HotBlocks;
        [NativeDisableParallelForRestriction, WriteOnly]
        public NativeArray<quaternion> Rotations;
        [NativeDisableParallelForRestriction, WriteOnly]
        public NativeArray<int> Categories;
        public int LogicalCount;

        public void Execute(int blockIndex)
        {
            var storage = new ParticleRecordGeneratedPackedAoSoA8Storage
            {
                HotBlocks = HotBlocks, Cold_Rotation = Rotations, Cold_Category = Categories,
                Count = LogicalCount,
            };
            storage.IngressBlock(blockIndex, Source);
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA8ExportJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ParticleAoSoA8Block> HotBlocks;
        [ReadOnly] public NativeArray<quaternion> Rotations;
        [ReadOnly] public NativeArray<int> Categories;
        [WriteOnly] public NativeArray<ParticleRecord> Destination;

        public void Execute(int index)
        {
            var storage = new ParticleRecordGeneratedPackedAoSoA8Storage
            {
                HotBlocks = HotBlocks, Cold_Rotation = Rotations, Cold_Category = Categories,
            };
            Destination[index] = storage.ReadRecord(index);
        }
    }

    public static class ParticleBoundaryJobScheduler
    {
        private const int DefaultBatchSize = 128;

        public static JobHandle ScheduleIngress(
            NativeArray<ParticleRecord> source,
            ref ParticleSoAStorage destination,
            JobHandle dependency = default)
        {
            return new ParticleSoAIngressJob
            {
                Source = source,
                Positions = destination.Positions,
                Velocities = destination.Velocities,
                Rotations = destination.Rotations,
                Lifetimes = destination.Lifetimes,
                Categories = destination.Categories,
            }.Schedule(source.Length, DefaultBatchSize, dependency);
        }

        public static JobHandle ScheduleExport(
            ref ParticleSoAStorage source,
            NativeArray<ParticleRecord> destination,
            JobHandle dependency = default)
        {
            return new ParticleSoAExportJob
            {
                Positions = source.Positions,
                Velocities = source.Velocities,
                Rotations = source.Rotations,
                Lifetimes = source.Lifetimes,
                Categories = source.Categories,
                Destination = destination,
            }.Schedule(destination.Length, DefaultBatchSize, dependency);
        }

        public static JobHandle ScheduleIngress(
            NativeArray<ParticleRecord> source,
            ref ParticleAoSoA8Storage destination,
            JobHandle dependency = default)
        {
            return new ParticleAoSoA8IngressJob
            {
                Source = source,
                HotBlocks = destination.HotBlocks,
                Rotations = destination.Rotations,
                Categories = destination.Categories,
                LogicalCount = destination.Count,
            }.Schedule(destination.BlockCount, 16, dependency);
        }

        public static JobHandle ScheduleExport(
            ref ParticleAoSoA8Storage source,
            NativeArray<ParticleRecord> destination,
            JobHandle dependency = default)
        {
            return new ParticleAoSoA8ExportJob
            {
                HotBlocks = source.HotBlocks,
                Rotations = source.Rotations,
                Categories = source.Categories,
                Destination = destination,
            }.Schedule(destination.Length, DefaultBatchSize, dependency);
        }
    }
}
