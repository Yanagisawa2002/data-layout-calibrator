using Unity.Collections;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate
{
    public static class ParticleDataSet
    {
        public const uint CalibrationSeed = 0x00C0FFEEu;
        public const uint HoldoutSeed = 0x0BADC0DEu;

        public static NativeArray<ParticleRecord> Create(
            int count,
            uint seed,
            Allocator allocator)
        {
            var records = new NativeArray<ParticleRecord>(
                count,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            for (int index = 0; index < count; index++)
            {
                uint state = Mix(seed ^ ((uint)index * 0x9E3779B9u));
                float px = SignedUnit(ref state) * 500.0f;
                float py = SignedUnit(ref state) * 100.0f;
                float pz = SignedUnit(ref state) * 500.0f;
                float vx = SignedUnit(ref state) * 8.0f;
                float vy = SignedUnit(ref state) * 3.0f;
                float vz = SignedUnit(ref state) * 8.0f;
                float lifetime = 0.01f + Unit(ref state) * 9.99f;
                int category = (int)(state & 3u);

                records[index] = new ParticleRecord
                {
                    Position = new float3(px, py, pz),
                    Velocity = new float3(vx, vy, vz),
                    Rotation = quaternion.identity,
                    Lifetime = lifetime,
                    Category = category,
                };
            }

            return records;
        }

        private static float SignedUnit(ref uint state) =>
            Unit(ref state) * 2.0f - 1.0f;

        private static float Unit(ref uint state)
        {
            state = Mix(state + 0x6D2B79F5u);
            return (state & 0x00FFFFFFu) * (1.0f / 16777216.0f);
        }

        private static uint Mix(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }
    }
}
