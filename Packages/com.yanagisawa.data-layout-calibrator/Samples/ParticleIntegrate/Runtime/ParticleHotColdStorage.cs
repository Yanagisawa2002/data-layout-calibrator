using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate
{
    // Opt-in experimental storage. The step's NativeArray contains no Rotation or
    // Category, so neither field can be read or written by either step job.
    public struct ParticleHotRecord
    {
        public float3 Position;
        public float3 Velocity;
        public float Lifetime;
    }

    public struct ParticleHotColdStorage : IDisposable
    {
        public NativeArray<ParticleHotRecord> Hot;
        public NativeArray<quaternion> Rotations;
        public NativeArray<int> Categories;
        public int Count => Hot.IsCreated ? Hot.Length : 0;
        public const string EvidenceStatus = "Unmeasured";

        public static ParticleHotColdStorage FromRecords(NativeArray<ParticleRecord> source, Allocator allocator)
        {
            if (!source.IsCreated) throw new ArgumentException("Source is not created.", nameof(source));
            var storage = new ParticleHotColdStorage();
            try
            {
                storage.Hot = new NativeArray<ParticleHotRecord>(source.Length, allocator, NativeArrayOptions.UninitializedMemory);
                storage.Rotations = new NativeArray<quaternion>(source.Length, allocator, NativeArrayOptions.UninitializedMemory);
                storage.Categories = new NativeArray<int>(source.Length, allocator, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < source.Length; i++) storage.WriteRecord(i, source[i]);
                return storage;
            }
            catch { storage.Dispose(); throw; }
        }

        public void WriteRecord(int index, ParticleRecord record)
        {
            Hot[index] = new ParticleHotRecord { Position = record.Position, Velocity = record.Velocity, Lifetime = record.Lifetime };
            Rotations[index] = record.Rotation;
            Categories[index] = record.Category;
        }

        public ParticleRecord ReadRecord(int index)
        {
            var hot = Hot[index];
            return new ParticleRecord { Position = hot.Position, Velocity = hot.Velocity, Lifetime = hot.Lifetime,
                Rotation = Rotations[index], Category = Categories[index] };
        }

        // A schedule always represents exactly one canonical step. Call Complete
        // per frame, or pass each returned handle as the next step's dependency.
        public JobHandle ScheduleStep(float deltaTime, int logicalBatchSize, bool branchless, JobHandle dependency = default)
        {
            if (!Hot.IsCreated) throw new ObjectDisposedException(nameof(ParticleHotColdStorage));
            if (logicalBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(logicalBatchSize));
            if (branchless)
                return new ParticleHotColdBranchlessStepJob { Hot = Hot, DeltaTime = deltaTime }.Schedule(Count, logicalBatchSize, dependency);
            return new ParticleHotColdBranchedStepJob { Hot = Hot, DeltaTime = deltaTime }.Schedule(Count, logicalBatchSize, dependency);
        }

        public void Export(NativeArray<ParticleRecord> destination)
        {
            if (!Hot.IsCreated) throw new ObjectDisposedException(nameof(ParticleHotColdStorage));
            if (!destination.IsCreated || destination.Length != Count) throw new ArgumentException("Canonical count mismatch.", nameof(destination));
            for (int i = 0; i < Count; i++) destination[i] = ReadRecord(i);
        }

        public void Dispose()
        {
            if (Hot.IsCreated) Hot.Dispose();
            if (Rotations.IsCreated) Rotations.Dispose();
            if (Categories.IsCreated) Categories.Dispose();
            Hot = default; Rotations = default; Categories = default;
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleHotColdBranchedStepJob : IJobParallelFor
    {
        public NativeArray<ParticleHotRecord> Hot;
        public float DeltaTime;
        public void Execute(int index)
        {
            var r = Hot[index];
            r.Velocity = r.Velocity * ParticleStepContract.VelocityDamping +
                new float3(ParticleStepContract.AccelerationX, ParticleStepContract.AccelerationY, ParticleStepContract.AccelerationZ) * DeltaTime;
            r.Position += r.Velocity * DeltaTime;
            r.Lifetime -= DeltaTime;
            if (r.Lifetime <= 0f)
            {
                r.Lifetime += ParticleStepContract.RespawnLifetimeSeconds;
                r.Position *= ParticleStepContract.RespawnPositionScale;
            }
            Hot[index] = r;
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleHotColdBranchlessStepJob : IJobParallelFor
    {
        public NativeArray<ParticleHotRecord> Hot;
        public float DeltaTime;
        public void Execute(int index)
        {
            var r = Hot[index];
            r.Velocity = r.Velocity * ParticleStepContract.VelocityDamping +
                new float3(ParticleStepContract.AccelerationX, ParticleStepContract.AccelerationY, ParticleStepContract.AccelerationZ) * DeltaTime;
            float3 position = r.Position + r.Velocity * DeltaTime;
            float lifetime = r.Lifetime - DeltaTime;
            bool expired = lifetime <= 0f;
            r.Lifetime = math.select(lifetime, lifetime + ParticleStepContract.RespawnLifetimeSeconds, expired);
            r.Position = math.select(position, position * ParticleStepContract.RespawnPositionScale, expired);
            Hot[index] = r;
        }
    }
}
