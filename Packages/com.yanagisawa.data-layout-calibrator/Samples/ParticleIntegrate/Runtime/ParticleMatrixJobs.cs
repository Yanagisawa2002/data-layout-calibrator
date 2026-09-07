using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate
{
    // One scheduled block, scalar source operations per lane. Burst may auto-vectorize.
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA4ScalarBranchedStepJob : IJobParallelFor
    {
        public NativeArray<ParticleRecordGeneratedPackedAoSoA4Block> Blocks;
        public float DeltaTime;
        public void Execute(int index)
        {
            var block = Blocks[index];
            for (int lane = 0; lane < 4; lane++)
            {
                float vX = block.VelocityX0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
                float pX = block.PositionX0[lane] + vX * DeltaTime;
                float vY = block.VelocityY0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
                float pY = block.PositionY0[lane] + vY * DeltaTime;
                float vZ = block.VelocityZ0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
                float pZ = block.PositionZ0[lane] + vZ * DeltaTime;
                float lifetime = block.Lifetime0[lane] - DeltaTime;
                if (lifetime <= 0f)
                {
                    lifetime += ParticleStepContract.RespawnLifetimeSeconds;
                    pX *= ParticleStepContract.RespawnPositionScale;
                    pY *= ParticleStepContract.RespawnPositionScale;
                    pZ *= ParticleStepContract.RespawnPositionScale;
                }
                block.VelocityX0[lane] = vX;
                block.PositionX0[lane] = pX;
                block.VelocityY0[lane] = vY;
                block.PositionY0[lane] = pY;
                block.VelocityZ0[lane] = vZ;
                block.PositionZ0[lane] = pZ;
                block.Lifetime0[lane] = lifetime;
            }
            Blocks[index] = block;
        }
    }

    // One scheduled block, scalar source operations per lane. Burst may auto-vectorize.
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA4ScalarBranchlessStepJob : IJobParallelFor
    {
        public NativeArray<ParticleRecordGeneratedPackedAoSoA4Block> Blocks;
        public float DeltaTime;
        public void Execute(int index)
        {
            var block = Blocks[index];
            for (int lane = 0; lane < 4; lane++)
            {
                float vX = block.VelocityX0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
                float pX = block.PositionX0[lane] + vX * DeltaTime;
                float vY = block.VelocityY0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
                float pY = block.PositionY0[lane] + vY * DeltaTime;
                float vZ = block.VelocityZ0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
                float pZ = block.PositionZ0[lane] + vZ * DeltaTime;
                float lifetime = block.Lifetime0[lane] - DeltaTime;
                bool expired = lifetime <= 0f;
                lifetime = math.select(lifetime, lifetime + ParticleStepContract.RespawnLifetimeSeconds, expired);
                pX = math.select(pX, pX * ParticleStepContract.RespawnPositionScale, expired);
                pY = math.select(pY, pY * ParticleStepContract.RespawnPositionScale, expired);
                pZ = math.select(pZ, pZ * ParticleStepContract.RespawnPositionScale, expired);
                block.VelocityX0[lane] = vX;
                block.PositionX0[lane] = pX;
                block.VelocityY0[lane] = vY;
                block.PositionY0[lane] = pY;
                block.VelocityZ0[lane] = vZ;
                block.PositionZ0[lane] = pZ;
                block.Lifetime0[lane] = lifetime;
            }
            Blocks[index] = block;
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA4StepJob : IJobParallelFor
    {
        public NativeArray<ParticleRecordGeneratedPackedAoSoA4Block> Blocks;
        public float DeltaTime;
        public void Execute(int index)
        {
            var block = Blocks[index];
            block.VelocityX0 = block.VelocityX0 * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
            block.PositionX0 += block.VelocityX0 * DeltaTime;
            block.VelocityY0 = block.VelocityY0 * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
            block.PositionY0 += block.VelocityY0 * DeltaTime;
            block.VelocityZ0 = block.VelocityZ0 * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
            block.PositionZ0 += block.VelocityZ0 * DeltaTime;
            block.Lifetime0 -= DeltaTime;
            bool4 expired0 = block.Lifetime0 <= 0f;
            block.Lifetime0 = math.select(block.Lifetime0, block.Lifetime0 + ParticleStepContract.RespawnLifetimeSeconds, expired0);
            block.PositionX0 = math.select(block.PositionX0, block.PositionX0 * ParticleStepContract.RespawnPositionScale, expired0);
            block.PositionY0 = math.select(block.PositionY0, block.PositionY0 * ParticleStepContract.RespawnPositionScale, expired0);
            block.PositionZ0 = math.select(block.PositionZ0, block.PositionZ0 * ParticleStepContract.RespawnPositionScale, expired0);
            Blocks[index] = block;
        }
    }

    // One scheduled block, scalar source operations per lane. Burst may auto-vectorize.
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA8ScalarBranchedStepJob : IJobParallelFor
    {
        public NativeArray<ParticleRecordGeneratedPackedAoSoA8Block> Blocks;
        public float DeltaTime;
        public void Execute(int index)
        {
            var block = Blocks[index];
            for (int lane = 0; lane < 4; lane++)
            {
                float vX = block.VelocityX0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
                float pX = block.PositionX0[lane] + vX * DeltaTime;
                float vY = block.VelocityY0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
                float pY = block.PositionY0[lane] + vY * DeltaTime;
                float vZ = block.VelocityZ0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
                float pZ = block.PositionZ0[lane] + vZ * DeltaTime;
                float lifetime = block.Lifetime0[lane] - DeltaTime;
                if (lifetime <= 0f)
                {
                    lifetime += ParticleStepContract.RespawnLifetimeSeconds;
                    pX *= ParticleStepContract.RespawnPositionScale;
                    pY *= ParticleStepContract.RespawnPositionScale;
                    pZ *= ParticleStepContract.RespawnPositionScale;
                }
                block.VelocityX0[lane] = vX;
                block.PositionX0[lane] = pX;
                block.VelocityY0[lane] = vY;
                block.PositionY0[lane] = pY;
                block.VelocityZ0[lane] = vZ;
                block.PositionZ0[lane] = pZ;
                block.Lifetime0[lane] = lifetime;
            }
            for (int lane = 0; lane < 4; lane++)
            {
                float vX = block.VelocityX1[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
                float pX = block.PositionX1[lane] + vX * DeltaTime;
                float vY = block.VelocityY1[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
                float pY = block.PositionY1[lane] + vY * DeltaTime;
                float vZ = block.VelocityZ1[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
                float pZ = block.PositionZ1[lane] + vZ * DeltaTime;
                float lifetime = block.Lifetime1[lane] - DeltaTime;
                if (lifetime <= 0f)
                {
                    lifetime += ParticleStepContract.RespawnLifetimeSeconds;
                    pX *= ParticleStepContract.RespawnPositionScale;
                    pY *= ParticleStepContract.RespawnPositionScale;
                    pZ *= ParticleStepContract.RespawnPositionScale;
                }
                block.VelocityX1[lane] = vX;
                block.PositionX1[lane] = pX;
                block.VelocityY1[lane] = vY;
                block.PositionY1[lane] = pY;
                block.VelocityZ1[lane] = vZ;
                block.PositionZ1[lane] = pZ;
                block.Lifetime1[lane] = lifetime;
            }
            Blocks[index] = block;
        }
    }

    // One scheduled block, scalar source operations per lane. Burst may auto-vectorize.
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA8ScalarBranchlessStepJob : IJobParallelFor
    {
        public NativeArray<ParticleRecordGeneratedPackedAoSoA8Block> Blocks;
        public float DeltaTime;
        public void Execute(int index)
        {
            var block = Blocks[index];
            for (int lane = 0; lane < 4; lane++)
            {
                float vX = block.VelocityX0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
                float pX = block.PositionX0[lane] + vX * DeltaTime;
                float vY = block.VelocityY0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
                float pY = block.PositionY0[lane] + vY * DeltaTime;
                float vZ = block.VelocityZ0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
                float pZ = block.PositionZ0[lane] + vZ * DeltaTime;
                float lifetime = block.Lifetime0[lane] - DeltaTime;
                bool expired = lifetime <= 0f;
                lifetime = math.select(lifetime, lifetime + ParticleStepContract.RespawnLifetimeSeconds, expired);
                pX = math.select(pX, pX * ParticleStepContract.RespawnPositionScale, expired);
                pY = math.select(pY, pY * ParticleStepContract.RespawnPositionScale, expired);
                pZ = math.select(pZ, pZ * ParticleStepContract.RespawnPositionScale, expired);
                block.VelocityX0[lane] = vX;
                block.PositionX0[lane] = pX;
                block.VelocityY0[lane] = vY;
                block.PositionY0[lane] = pY;
                block.VelocityZ0[lane] = vZ;
                block.PositionZ0[lane] = pZ;
                block.Lifetime0[lane] = lifetime;
            }
            for (int lane = 0; lane < 4; lane++)
            {
                float vX = block.VelocityX1[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
                float pX = block.PositionX1[lane] + vX * DeltaTime;
                float vY = block.VelocityY1[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
                float pY = block.PositionY1[lane] + vY * DeltaTime;
                float vZ = block.VelocityZ1[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
                float pZ = block.PositionZ1[lane] + vZ * DeltaTime;
                float lifetime = block.Lifetime1[lane] - DeltaTime;
                bool expired = lifetime <= 0f;
                lifetime = math.select(lifetime, lifetime + ParticleStepContract.RespawnLifetimeSeconds, expired);
                pX = math.select(pX, pX * ParticleStepContract.RespawnPositionScale, expired);
                pY = math.select(pY, pY * ParticleStepContract.RespawnPositionScale, expired);
                pZ = math.select(pZ, pZ * ParticleStepContract.RespawnPositionScale, expired);
                block.VelocityX1[lane] = vX;
                block.PositionX1[lane] = pX;
                block.VelocityY1[lane] = vY;
                block.PositionY1[lane] = pY;
                block.VelocityZ1[lane] = vZ;
                block.PositionZ1[lane] = pZ;
                block.Lifetime1[lane] = lifetime;
            }
            Blocks[index] = block;
        }
    }

    // One scheduled block, scalar source operations per lane. Burst may auto-vectorize.
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA16ScalarBranchedStepJob : IJobParallelFor
    {
        public NativeArray<ParticleRecordGeneratedPackedAoSoA16Block> Blocks;
        public float DeltaTime;
        public void Execute(int index)
        {
            var block = Blocks[index];
            for (int lane = 0; lane < 4; lane++)
            {
                float vX = block.VelocityX0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
                float pX = block.PositionX0[lane] + vX * DeltaTime;
                float vY = block.VelocityY0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
                float pY = block.PositionY0[lane] + vY * DeltaTime;
                float vZ = block.VelocityZ0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
                float pZ = block.PositionZ0[lane] + vZ * DeltaTime;
                float lifetime = block.Lifetime0[lane] - DeltaTime;
                if (lifetime <= 0f)
                {
                    lifetime += ParticleStepContract.RespawnLifetimeSeconds;
                    pX *= ParticleStepContract.RespawnPositionScale;
                    pY *= ParticleStepContract.RespawnPositionScale;
                    pZ *= ParticleStepContract.RespawnPositionScale;
                }
                block.VelocityX0[lane] = vX;
                block.PositionX0[lane] = pX;
                block.VelocityY0[lane] = vY;
                block.PositionY0[lane] = pY;
                block.VelocityZ0[lane] = vZ;
                block.PositionZ0[lane] = pZ;
                block.Lifetime0[lane] = lifetime;
            }
            for (int lane = 0; lane < 4; lane++)
            {
                float vX = block.VelocityX1[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
                float pX = block.PositionX1[lane] + vX * DeltaTime;
                float vY = block.VelocityY1[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
                float pY = block.PositionY1[lane] + vY * DeltaTime;
                float vZ = block.VelocityZ1[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
                float pZ = block.PositionZ1[lane] + vZ * DeltaTime;
                float lifetime = block.Lifetime1[lane] - DeltaTime;
                if (lifetime <= 0f)
                {
                    lifetime += ParticleStepContract.RespawnLifetimeSeconds;
                    pX *= ParticleStepContract.RespawnPositionScale;
                    pY *= ParticleStepContract.RespawnPositionScale;
                    pZ *= ParticleStepContract.RespawnPositionScale;
                }
                block.VelocityX1[lane] = vX;
                block.PositionX1[lane] = pX;
                block.VelocityY1[lane] = vY;
                block.PositionY1[lane] = pY;
                block.VelocityZ1[lane] = vZ;
                block.PositionZ1[lane] = pZ;
                block.Lifetime1[lane] = lifetime;
            }
            for (int lane = 0; lane < 4; lane++)
            {
                float vX = block.VelocityX2[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
                float pX = block.PositionX2[lane] + vX * DeltaTime;
                float vY = block.VelocityY2[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
                float pY = block.PositionY2[lane] + vY * DeltaTime;
                float vZ = block.VelocityZ2[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
                float pZ = block.PositionZ2[lane] + vZ * DeltaTime;
                float lifetime = block.Lifetime2[lane] - DeltaTime;
                if (lifetime <= 0f)
                {
                    lifetime += ParticleStepContract.RespawnLifetimeSeconds;
                    pX *= ParticleStepContract.RespawnPositionScale;
                    pY *= ParticleStepContract.RespawnPositionScale;
                    pZ *= ParticleStepContract.RespawnPositionScale;
                }
                block.VelocityX2[lane] = vX;
                block.PositionX2[lane] = pX;
                block.VelocityY2[lane] = vY;
                block.PositionY2[lane] = pY;
                block.VelocityZ2[lane] = vZ;
                block.PositionZ2[lane] = pZ;
                block.Lifetime2[lane] = lifetime;
            }
            for (int lane = 0; lane < 4; lane++)
            {
                float vX = block.VelocityX3[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
                float pX = block.PositionX3[lane] + vX * DeltaTime;
                float vY = block.VelocityY3[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
                float pY = block.PositionY3[lane] + vY * DeltaTime;
                float vZ = block.VelocityZ3[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
                float pZ = block.PositionZ3[lane] + vZ * DeltaTime;
                float lifetime = block.Lifetime3[lane] - DeltaTime;
                if (lifetime <= 0f)
                {
                    lifetime += ParticleStepContract.RespawnLifetimeSeconds;
                    pX *= ParticleStepContract.RespawnPositionScale;
                    pY *= ParticleStepContract.RespawnPositionScale;
                    pZ *= ParticleStepContract.RespawnPositionScale;
                }
                block.VelocityX3[lane] = vX;
                block.PositionX3[lane] = pX;
                block.VelocityY3[lane] = vY;
                block.PositionY3[lane] = pY;
                block.VelocityZ3[lane] = vZ;
                block.PositionZ3[lane] = pZ;
                block.Lifetime3[lane] = lifetime;
            }
            Blocks[index] = block;
        }
    }

    // One scheduled block, scalar source operations per lane. Burst may auto-vectorize.
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA16ScalarBranchlessStepJob : IJobParallelFor
    {
        public NativeArray<ParticleRecordGeneratedPackedAoSoA16Block> Blocks;
        public float DeltaTime;
        public void Execute(int index)
        {
            var block = Blocks[index];
            for (int lane = 0; lane < 4; lane++)
            {
                float vX = block.VelocityX0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
                float pX = block.PositionX0[lane] + vX * DeltaTime;
                float vY = block.VelocityY0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
                float pY = block.PositionY0[lane] + vY * DeltaTime;
                float vZ = block.VelocityZ0[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
                float pZ = block.PositionZ0[lane] + vZ * DeltaTime;
                float lifetime = block.Lifetime0[lane] - DeltaTime;
                bool expired = lifetime <= 0f;
                lifetime = math.select(lifetime, lifetime + ParticleStepContract.RespawnLifetimeSeconds, expired);
                pX = math.select(pX, pX * ParticleStepContract.RespawnPositionScale, expired);
                pY = math.select(pY, pY * ParticleStepContract.RespawnPositionScale, expired);
                pZ = math.select(pZ, pZ * ParticleStepContract.RespawnPositionScale, expired);
                block.VelocityX0[lane] = vX;
                block.PositionX0[lane] = pX;
                block.VelocityY0[lane] = vY;
                block.PositionY0[lane] = pY;
                block.VelocityZ0[lane] = vZ;
                block.PositionZ0[lane] = pZ;
                block.Lifetime0[lane] = lifetime;
            }
            for (int lane = 0; lane < 4; lane++)
            {
                float vX = block.VelocityX1[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
                float pX = block.PositionX1[lane] + vX * DeltaTime;
                float vY = block.VelocityY1[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
                float pY = block.PositionY1[lane] + vY * DeltaTime;
                float vZ = block.VelocityZ1[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
                float pZ = block.PositionZ1[lane] + vZ * DeltaTime;
                float lifetime = block.Lifetime1[lane] - DeltaTime;
                bool expired = lifetime <= 0f;
                lifetime = math.select(lifetime, lifetime + ParticleStepContract.RespawnLifetimeSeconds, expired);
                pX = math.select(pX, pX * ParticleStepContract.RespawnPositionScale, expired);
                pY = math.select(pY, pY * ParticleStepContract.RespawnPositionScale, expired);
                pZ = math.select(pZ, pZ * ParticleStepContract.RespawnPositionScale, expired);
                block.VelocityX1[lane] = vX;
                block.PositionX1[lane] = pX;
                block.VelocityY1[lane] = vY;
                block.PositionY1[lane] = pY;
                block.VelocityZ1[lane] = vZ;
                block.PositionZ1[lane] = pZ;
                block.Lifetime1[lane] = lifetime;
            }
            for (int lane = 0; lane < 4; lane++)
            {
                float vX = block.VelocityX2[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
                float pX = block.PositionX2[lane] + vX * DeltaTime;
                float vY = block.VelocityY2[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
                float pY = block.PositionY2[lane] + vY * DeltaTime;
                float vZ = block.VelocityZ2[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
                float pZ = block.PositionZ2[lane] + vZ * DeltaTime;
                float lifetime = block.Lifetime2[lane] - DeltaTime;
                bool expired = lifetime <= 0f;
                lifetime = math.select(lifetime, lifetime + ParticleStepContract.RespawnLifetimeSeconds, expired);
                pX = math.select(pX, pX * ParticleStepContract.RespawnPositionScale, expired);
                pY = math.select(pY, pY * ParticleStepContract.RespawnPositionScale, expired);
                pZ = math.select(pZ, pZ * ParticleStepContract.RespawnPositionScale, expired);
                block.VelocityX2[lane] = vX;
                block.PositionX2[lane] = pX;
                block.VelocityY2[lane] = vY;
                block.PositionY2[lane] = pY;
                block.VelocityZ2[lane] = vZ;
                block.PositionZ2[lane] = pZ;
                block.Lifetime2[lane] = lifetime;
            }
            for (int lane = 0; lane < 4; lane++)
            {
                float vX = block.VelocityX3[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
                float pX = block.PositionX3[lane] + vX * DeltaTime;
                float vY = block.VelocityY3[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
                float pY = block.PositionY3[lane] + vY * DeltaTime;
                float vZ = block.VelocityZ3[lane] * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
                float pZ = block.PositionZ3[lane] + vZ * DeltaTime;
                float lifetime = block.Lifetime3[lane] - DeltaTime;
                bool expired = lifetime <= 0f;
                lifetime = math.select(lifetime, lifetime + ParticleStepContract.RespawnLifetimeSeconds, expired);
                pX = math.select(pX, pX * ParticleStepContract.RespawnPositionScale, expired);
                pY = math.select(pY, pY * ParticleStepContract.RespawnPositionScale, expired);
                pZ = math.select(pZ, pZ * ParticleStepContract.RespawnPositionScale, expired);
                block.VelocityX3[lane] = vX;
                block.PositionX3[lane] = pX;
                block.VelocityY3[lane] = vY;
                block.PositionY3[lane] = pY;
                block.VelocityZ3[lane] = vZ;
                block.PositionZ3[lane] = pZ;
                block.Lifetime3[lane] = lifetime;
            }
            Blocks[index] = block;
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA16StepJob : IJobParallelFor
    {
        public NativeArray<ParticleRecordGeneratedPackedAoSoA16Block> Blocks;
        public float DeltaTime;
        public void Execute(int index)
        {
            var block = Blocks[index];
            block.VelocityX0 = block.VelocityX0 * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
            block.PositionX0 += block.VelocityX0 * DeltaTime;
            block.VelocityY0 = block.VelocityY0 * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
            block.PositionY0 += block.VelocityY0 * DeltaTime;
            block.VelocityZ0 = block.VelocityZ0 * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
            block.PositionZ0 += block.VelocityZ0 * DeltaTime;
            block.Lifetime0 -= DeltaTime;
            bool4 expired0 = block.Lifetime0 <= 0f;
            block.Lifetime0 = math.select(block.Lifetime0, block.Lifetime0 + ParticleStepContract.RespawnLifetimeSeconds, expired0);
            block.PositionX0 = math.select(block.PositionX0, block.PositionX0 * ParticleStepContract.RespawnPositionScale, expired0);
            block.PositionY0 = math.select(block.PositionY0, block.PositionY0 * ParticleStepContract.RespawnPositionScale, expired0);
            block.PositionZ0 = math.select(block.PositionZ0, block.PositionZ0 * ParticleStepContract.RespawnPositionScale, expired0);
            block.VelocityX1 = block.VelocityX1 * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
            block.PositionX1 += block.VelocityX1 * DeltaTime;
            block.VelocityY1 = block.VelocityY1 * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
            block.PositionY1 += block.VelocityY1 * DeltaTime;
            block.VelocityZ1 = block.VelocityZ1 * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
            block.PositionZ1 += block.VelocityZ1 * DeltaTime;
            block.Lifetime1 -= DeltaTime;
            bool4 expired1 = block.Lifetime1 <= 0f;
            block.Lifetime1 = math.select(block.Lifetime1, block.Lifetime1 + ParticleStepContract.RespawnLifetimeSeconds, expired1);
            block.PositionX1 = math.select(block.PositionX1, block.PositionX1 * ParticleStepContract.RespawnPositionScale, expired1);
            block.PositionY1 = math.select(block.PositionY1, block.PositionY1 * ParticleStepContract.RespawnPositionScale, expired1);
            block.PositionZ1 = math.select(block.PositionZ1, block.PositionZ1 * ParticleStepContract.RespawnPositionScale, expired1);
            block.VelocityX2 = block.VelocityX2 * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
            block.PositionX2 += block.VelocityX2 * DeltaTime;
            block.VelocityY2 = block.VelocityY2 * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
            block.PositionY2 += block.VelocityY2 * DeltaTime;
            block.VelocityZ2 = block.VelocityZ2 * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
            block.PositionZ2 += block.VelocityZ2 * DeltaTime;
            block.Lifetime2 -= DeltaTime;
            bool4 expired2 = block.Lifetime2 <= 0f;
            block.Lifetime2 = math.select(block.Lifetime2, block.Lifetime2 + ParticleStepContract.RespawnLifetimeSeconds, expired2);
            block.PositionX2 = math.select(block.PositionX2, block.PositionX2 * ParticleStepContract.RespawnPositionScale, expired2);
            block.PositionY2 = math.select(block.PositionY2, block.PositionY2 * ParticleStepContract.RespawnPositionScale, expired2);
            block.PositionZ2 = math.select(block.PositionZ2, block.PositionZ2 * ParticleStepContract.RespawnPositionScale, expired2);
            block.VelocityX3 = block.VelocityX3 * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationX * DeltaTime;
            block.PositionX3 += block.VelocityX3 * DeltaTime;
            block.VelocityY3 = block.VelocityY3 * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationY * DeltaTime;
            block.PositionY3 += block.VelocityY3 * DeltaTime;
            block.VelocityZ3 = block.VelocityZ3 * ParticleStepContract.VelocityDamping + ParticleStepContract.AccelerationZ * DeltaTime;
            block.PositionZ3 += block.VelocityZ3 * DeltaTime;
            block.Lifetime3 -= DeltaTime;
            bool4 expired3 = block.Lifetime3 <= 0f;
            block.Lifetime3 = math.select(block.Lifetime3, block.Lifetime3 + ParticleStepContract.RespawnLifetimeSeconds, expired3);
            block.PositionX3 = math.select(block.PositionX3, block.PositionX3 * ParticleStepContract.RespawnPositionScale, expired3);
            block.PositionY3 = math.select(block.PositionY3, block.PositionY3 * ParticleStepContract.RespawnPositionScale, expired3);
            block.PositionZ3 = math.select(block.PositionZ3, block.PositionZ3 * ParticleStepContract.RespawnPositionScale, expired3);
            Blocks[index] = block;
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticlePadded64BranchedStepJob : IJobParallelFor
    {
        public NativeArray<ParticleRecordGeneratedPadded64Record> Records;
        public float DeltaTime;

        public void Execute(int index)
        {
            float3 acceleration = new float3(
                ParticleStepContract.AccelerationX,
                ParticleStepContract.AccelerationY,
                ParticleStepContract.AccelerationZ);
            var padded = Records[index];
            ParticleRecord record = padded.Value;
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

            padded.Value = record;
            Records[index] = padded;
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticlePadded64BranchlessStepJob : IJobParallelFor
    {
        public NativeArray<ParticleRecordGeneratedPadded64Record> Records;
        public float DeltaTime;

        public void Execute(int index)
        {
            float3 acceleration = new float3(
                ParticleStepContract.AccelerationX,
                ParticleStepContract.AccelerationY,
                ParticleStepContract.AccelerationZ);
            var padded = Records[index];
            ParticleRecord record = padded.Value;
            record.Velocity =
                record.Velocity * ParticleStepContract.VelocityDamping +
                acceleration * DeltaTime;
            float3 integratedPosition = record.Position + record.Velocity * DeltaTime;
            float integratedLifetime = record.Lifetime - DeltaTime;
            bool expired = integratedLifetime <= 0.0f;
            record.Lifetime = math.select(
                integratedLifetime,
                integratedLifetime + ParticleStepContract.RespawnLifetimeSeconds,
                expired);
            record.Position = math.select(
                integratedPosition,
                integratedPosition * ParticleStepContract.RespawnPositionScale,
                expired);
            padded.Value = record;
            Records[index] = padded;
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleSoABranchlessStepJob : IJobParallelFor
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
            bool expired = lifetime <= 0f;
            lifetime = math.select(lifetime, lifetime + ParticleStepContract.RespawnLifetimeSeconds, expired);
            position = math.select(position, position * ParticleStepContract.RespawnPositionScale, expired);

            Velocities[index] = velocity;
            Positions[index] = position;
            Lifetimes[index] = lifetime;
        }
    }
}
