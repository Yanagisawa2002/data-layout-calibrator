using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutAutotuner
{
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSStepJob : IJobParallelFor
    {
        public NativeArray<ParticleRecord> Records;
        public float DeltaTime;

        public void Execute(int index)
        {
            float3 acceleration = new float3(
                ParticleStepContract.AccelerationX,
                ParticleStepContract.AccelerationY,
                ParticleStepContract.AccelerationZ);
            ParticleRecord record = Records[index];
            record.Velocity =
                record.Velocity * ParticleStepContract.VelocityDamping +
                acceleration * DeltaTime;
            record.Position += record.Velocity * DeltaTime;
            record.Lifetime -= DeltaTime;
            if (record.Lifetime <= 0.0f)
            {
                record.Lifetime += ParticleStepContract.RespawnLifetimeSeconds;
                record.Position *= ParticleStepContract.RespawnPositionScale;
            }

            Records[index] = record;
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleSoAStepJob : IJobParallelFor
    {
        public NativeArray<float3> Positions;
        public NativeArray<float3> Velocities;
        public NativeArray<float> Lifetimes;
        public float DeltaTime;

        public void Execute(int index)
        {
            float3 acceleration = new float3(
                ParticleStepContract.AccelerationX,
                ParticleStepContract.AccelerationY,
                ParticleStepContract.AccelerationZ);
            float3 velocity =
                Velocities[index] * ParticleStepContract.VelocityDamping +
                acceleration * DeltaTime;
            float3 position = Positions[index] + velocity * DeltaTime;
            float lifetime = Lifetimes[index] - DeltaTime;
            if (lifetime <= 0.0f)
            {
                lifetime += ParticleStepContract.RespawnLifetimeSeconds;
                position *= ParticleStepContract.RespawnPositionScale;
            }

            Velocities[index] = velocity;
            Positions[index] = position;
            Lifetimes[index] = lifetime;
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA8StepJob : IJobParallelFor
    {
        public NativeArray<ParticleAoSoA8Block> Blocks;
        public float DeltaTime;

        public void Execute(int index)
        {
            ParticleAoSoA8Block block = Blocks[index];
            float damping = ParticleStepContract.VelocityDamping;
            float3 acceleration = new float3(
                ParticleStepContract.AccelerationX,
                ParticleStepContract.AccelerationY,
                ParticleStepContract.AccelerationZ);

            block.VelocityX0 = block.VelocityX0 * damping + acceleration.x * DeltaTime;
            block.VelocityX1 = block.VelocityX1 * damping + acceleration.x * DeltaTime;
            block.VelocityY0 = block.VelocityY0 * damping + acceleration.y * DeltaTime;
            block.VelocityY1 = block.VelocityY1 * damping + acceleration.y * DeltaTime;
            block.VelocityZ0 = block.VelocityZ0 * damping + acceleration.z * DeltaTime;
            block.VelocityZ1 = block.VelocityZ1 * damping + acceleration.z * DeltaTime;

            block.PositionX0 += block.VelocityX0 * DeltaTime;
            block.PositionX1 += block.VelocityX1 * DeltaTime;
            block.PositionY0 += block.VelocityY0 * DeltaTime;
            block.PositionY1 += block.VelocityY1 * DeltaTime;
            block.PositionZ0 += block.VelocityZ0 * DeltaTime;
            block.PositionZ1 += block.VelocityZ1 * DeltaTime;

            block.Lifetime0 -= DeltaTime;
            block.Lifetime1 -= DeltaTime;
            bool4 expired0 = block.Lifetime0 <= 0.0f;
            bool4 expired1 = block.Lifetime1 <= 0.0f;
            block.Lifetime0 = math.select(
                block.Lifetime0,
                block.Lifetime0 + ParticleStepContract.RespawnLifetimeSeconds,
                expired0);
            block.Lifetime1 = math.select(
                block.Lifetime1,
                block.Lifetime1 + ParticleStepContract.RespawnLifetimeSeconds,
                expired1);

            float positionScale = ParticleStepContract.RespawnPositionScale;
            block.PositionX0 = math.select(block.PositionX0, block.PositionX0 * positionScale, expired0);
            block.PositionX1 = math.select(block.PositionX1, block.PositionX1 * positionScale, expired1);
            block.PositionY0 = math.select(block.PositionY0, block.PositionY0 * positionScale, expired0);
            block.PositionY1 = math.select(block.PositionY1, block.PositionY1 * positionScale, expired1);
            block.PositionZ0 = math.select(block.PositionZ0, block.PositionZ0 * positionScale, expired0);
            block.PositionZ1 = math.select(block.PositionZ1, block.PositionZ1 * positionScale, expired1);

            Blocks[index] = block;
        }
    }

    public static class ParticleJobScheduler
    {
        public static JobHandle Schedule(
            ref ParticleAoSStorage storage,
            int logicalBatchSize,
            float deltaTime,
            JobHandle dependency = default)
        {
            var job = new ParticleAoSStepJob
            {
                Records = storage.Records,
                DeltaTime = deltaTime,
            };
            return job.Schedule(
                storage.Count,
                math.max(1, logicalBatchSize),
                dependency);
        }

        public static JobHandle Schedule(
            ref ParticleSoAStorage storage,
            int logicalBatchSize,
            float deltaTime,
            JobHandle dependency = default)
        {
            var job = new ParticleSoAStepJob
            {
                Positions = storage.Positions,
                Velocities = storage.Velocities,
                Lifetimes = storage.Lifetimes,
                DeltaTime = deltaTime,
            };
            return job.Schedule(
                storage.Count,
                math.max(1, logicalBatchSize),
                dependency);
        }

        public static JobHandle Schedule(
            ref ParticleAoSoA8Storage storage,
            int logicalBatchSize,
            float deltaTime,
            JobHandle dependency = default)
        {
            var job = new ParticleAoSoA8StepJob
            {
                Blocks = storage.HotBlocks,
                DeltaTime = deltaTime,
            };
            int blockBatchSize = math.max(
                1,
                logicalBatchSize / ParticleAoSoA8Storage.BlockWidth);
            return job.Schedule(storage.BlockCount, blockBatchSize, dependency);
        }
    }
}
