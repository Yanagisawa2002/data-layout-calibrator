using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutAutotuner
{
    public struct ParticleAoSStorage : IDisposable
    {
        public NativeArray<ParticleRecord> Records;

        public int Count => Records.IsCreated ? Records.Length : 0;

        public static ParticleAoSStorage FromRecords(
            NativeArray<ParticleRecord> source,
            Allocator allocator)
        {
            var storage = new ParticleAoSStorage
            {
                Records = new NativeArray<ParticleRecord>(
                    source.Length,
                    allocator,
                    NativeArrayOptions.UninitializedMemory),
            };
            storage.Records.CopyFrom(source);
            return storage;
        }

        public ParticleRecord ReadRecord(int index) => Records[index];

        public void Dispose()
        {
            if (Records.IsCreated)
                Records.Dispose();
        }
    }

    public struct ParticleSoAStorage : IDisposable
    {
        public NativeArray<float3> Positions;
        public NativeArray<float3> Velocities;
        public NativeArray<quaternion> Rotations;
        public NativeArray<float> Lifetimes;
        public NativeArray<int> Categories;

        public int Count => Positions.IsCreated ? Positions.Length : 0;

        public static ParticleSoAStorage FromRecords(
            NativeArray<ParticleRecord> source,
            Allocator allocator)
        {
            int count = source.Length;
            var storage = new ParticleSoAStorage
            {
                Positions = NewArray<float3>(count, allocator),
                Velocities = NewArray<float3>(count, allocator),
                Rotations = NewArray<quaternion>(count, allocator),
                Lifetimes = NewArray<float>(count, allocator),
                Categories = NewArray<int>(count, allocator),
            };

            for (int index = 0; index < count; index++)
            {
                ParticleRecord record = source[index];
                storage.Positions[index] = record.Position;
                storage.Velocities[index] = record.Velocity;
                storage.Rotations[index] = record.Rotation;
                storage.Lifetimes[index] = record.Lifetime;
                storage.Categories[index] = record.Category;
            }

            return storage;
        }

        public ParticleRecord ReadRecord(int index) => new ParticleRecord
        {
            Position = Positions[index],
            Velocity = Velocities[index],
            Rotation = Rotations[index],
            Lifetime = Lifetimes[index],
            Category = Categories[index],
        };

        public void Dispose()
        {
            DisposeIfCreated(ref Positions);
            DisposeIfCreated(ref Velocities);
            DisposeIfCreated(ref Rotations);
            DisposeIfCreated(ref Lifetimes);
            DisposeIfCreated(ref Categories);
        }

        private static NativeArray<T> NewArray<T>(int count, Allocator allocator)
            where T : struct =>
            new NativeArray<T>(
                count,
                allocator,
                NativeArrayOptions.UninitializedMemory);

        private static void DisposeIfCreated<T>(ref NativeArray<T> array)
            where T : struct
        {
            if (array.IsCreated)
                array.Dispose();
        }
    }

    public struct ParticleAoSoA8Block
    {
        public float4 PositionX0;
        public float4 PositionX1;
        public float4 PositionY0;
        public float4 PositionY1;
        public float4 PositionZ0;
        public float4 PositionZ1;

        public float4 VelocityX0;
        public float4 VelocityX1;
        public float4 VelocityY0;
        public float4 VelocityY1;
        public float4 VelocityZ0;
        public float4 VelocityZ1;

        public float4 Lifetime0;
        public float4 Lifetime1;
    }

    public struct ParticleAoSoA8Storage : IDisposable
    {
        public const int BlockWidth = 8;

        public NativeArray<ParticleAoSoA8Block> HotBlocks;
        public NativeArray<quaternion> Rotations;
        public NativeArray<int> Categories;
        public int Count;

        public int BlockCount => HotBlocks.IsCreated ? HotBlocks.Length : 0;

        public static ParticleAoSoA8Storage FromRecords(
            NativeArray<ParticleRecord> source,
            Allocator allocator)
        {
            int count = source.Length;
            int blockCount = (count + BlockWidth - 1) / BlockWidth;
            var storage = new ParticleAoSoA8Storage
            {
                Count = count,
                HotBlocks = new NativeArray<ParticleAoSoA8Block>(
                    blockCount,
                    allocator,
                    NativeArrayOptions.ClearMemory),
                Rotations = new NativeArray<quaternion>(
                    count,
                    allocator,
                    NativeArrayOptions.UninitializedMemory),
                Categories = new NativeArray<int>(
                    count,
                    allocator,
                    NativeArrayOptions.UninitializedMemory),
            };

            for (int index = 0; index < count; index++)
            {
                ParticleRecord record = source[index];
                int blockIndex = index / BlockWidth;
                int lane = index % BlockWidth;
                ParticleAoSoA8Block block = storage.HotBlocks[blockIndex];
                SetLane(ref block.PositionX0, ref block.PositionX1, lane, record.Position.x);
                SetLane(ref block.PositionY0, ref block.PositionY1, lane, record.Position.y);
                SetLane(ref block.PositionZ0, ref block.PositionZ1, lane, record.Position.z);
                SetLane(ref block.VelocityX0, ref block.VelocityX1, lane, record.Velocity.x);
                SetLane(ref block.VelocityY0, ref block.VelocityY1, lane, record.Velocity.y);
                SetLane(ref block.VelocityZ0, ref block.VelocityZ1, lane, record.Velocity.z);
                SetLane(ref block.Lifetime0, ref block.Lifetime1, lane, record.Lifetime);
                storage.HotBlocks[blockIndex] = block;
                storage.Rotations[index] = record.Rotation;
                storage.Categories[index] = record.Category;
            }

            return storage;
        }

        public ParticleRecord ReadRecord(int index)
        {
            int blockIndex = index / BlockWidth;
            int lane = index % BlockWidth;
            ParticleAoSoA8Block block = HotBlocks[blockIndex];
            return new ParticleRecord
            {
                Position = new float3(
                    GetLane(block.PositionX0, block.PositionX1, lane),
                    GetLane(block.PositionY0, block.PositionY1, lane),
                    GetLane(block.PositionZ0, block.PositionZ1, lane)),
                Velocity = new float3(
                    GetLane(block.VelocityX0, block.VelocityX1, lane),
                    GetLane(block.VelocityY0, block.VelocityY1, lane),
                    GetLane(block.VelocityZ0, block.VelocityZ1, lane)),
                Rotation = Rotations[index],
                Lifetime = GetLane(block.Lifetime0, block.Lifetime1, lane),
                Category = Categories[index],
            };
        }

        public void Dispose()
        {
            if (HotBlocks.IsCreated)
                HotBlocks.Dispose();
            if (Rotations.IsCreated)
                Rotations.Dispose();
            if (Categories.IsCreated)
                Categories.Dispose();
        }

        private static void SetLane(
            ref float4 low,
            ref float4 high,
            int lane,
            float value)
        {
            switch (lane)
            {
                case 0: low.x = value; break;
                case 1: low.y = value; break;
                case 2: low.z = value; break;
                case 3: low.w = value; break;
                case 4: high.x = value; break;
                case 5: high.y = value; break;
                case 6: high.z = value; break;
                default: high.w = value; break;
            }
        }

        private static float GetLane(float4 low, float4 high, int lane)
        {
            switch (lane)
            {
                case 0: return low.x;
                case 1: return low.y;
                case 2: return low.z;
                case 3: return low.w;
                case 4: return high.x;
                case 5: return high.y;
                case 6: return high.z;
                default: return high.w;
            }
        }
    }
}

