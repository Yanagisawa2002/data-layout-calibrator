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
            ParticleRecord record = Source[index];
            Positions[index] = record.Position;
            Velocities[index] = record.Velocity;
            Rotations[index] = record.Rotation;
            Lifetimes[index] = record.Lifetime;
            Categories[index] = record.Category;
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
            Destination[index] = new ParticleRecord
            {
                Position = Positions[index],
                Velocity = Velocities[index],
                Rotation = Rotations[index],
                Lifetime = Lifetimes[index],
                Category = Categories[index],
            };
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
            ParticleAoSoA8Block block = default;
            int first = blockIndex * ParticleAoSoA8Storage.BlockWidth;
            int count = math.min(ParticleAoSoA8Storage.BlockWidth, LogicalCount - first);
            for (int lane = 0; lane < count; lane++)
            {
                int index = first + lane;
                ParticleRecord record = Source[index];
                ParticleAoSoA8Storage.SetLane(ref block.PositionX0, ref block.PositionX1, lane, record.Position.x);
                ParticleAoSoA8Storage.SetLane(ref block.PositionY0, ref block.PositionY1, lane, record.Position.y);
                ParticleAoSoA8Storage.SetLane(ref block.PositionZ0, ref block.PositionZ1, lane, record.Position.z);
                ParticleAoSoA8Storage.SetLane(ref block.VelocityX0, ref block.VelocityX1, lane, record.Velocity.x);
                ParticleAoSoA8Storage.SetLane(ref block.VelocityY0, ref block.VelocityY1, lane, record.Velocity.y);
                ParticleAoSoA8Storage.SetLane(ref block.VelocityZ0, ref block.VelocityZ1, lane, record.Velocity.z);
                ParticleAoSoA8Storage.SetLane(ref block.Lifetime0, ref block.Lifetime1, lane, record.Lifetime);
                Rotations[index] = record.Rotation;
                Categories[index] = record.Category;
            }

            HotBlocks[blockIndex] = block;
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
            int blockIndex = index / ParticleAoSoA8Storage.BlockWidth;
            int lane = index % ParticleAoSoA8Storage.BlockWidth;
            ParticleAoSoA8Block block = HotBlocks[blockIndex];
            Destination[index] = new ParticleRecord
            {
                Position = new float3(
                    ParticleAoSoA8Storage.GetLane(block.PositionX0, block.PositionX1, lane),
                    ParticleAoSoA8Storage.GetLane(block.PositionY0, block.PositionY1, lane),
                    ParticleAoSoA8Storage.GetLane(block.PositionZ0, block.PositionZ1, lane)),
                Velocity = new float3(
                    ParticleAoSoA8Storage.GetLane(block.VelocityX0, block.VelocityX1, lane),
                    ParticleAoSoA8Storage.GetLane(block.VelocityY0, block.VelocityY1, lane),
                    ParticleAoSoA8Storage.GetLane(block.VelocityZ0, block.VelocityZ1, lane)),
                Rotation = Rotations[index],
                Lifetime = ParticleAoSoA8Storage.GetLane(block.Lifetime0, block.Lifetime1, lane),
                Category = Categories[index],
            };
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
