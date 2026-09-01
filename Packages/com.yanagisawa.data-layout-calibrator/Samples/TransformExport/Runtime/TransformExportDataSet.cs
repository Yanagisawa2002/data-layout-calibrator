using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.TransformExport
{
    public static class TransformExportDataSet
    {
        public const uint CalibrationSeed = 0x73A9C5D1u;
        public const uint HoldoutSeed = 0xC19F42B7u;

        public static NativeArray<TransformRecord> Create(
            int count,
            uint seed,
            Allocator allocator)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (seed == 0u)
                throw new ArgumentOutOfRangeException(nameof(seed), "Unity.Mathematics.Random requires a non-zero seed.");

            var records = new NativeArray<TransformRecord>(
                count,
                allocator,
                NativeArrayOptions.UninitializedMemory);
            var random = new Unity.Mathematics.Random(seed);
            for (int index = 0; index < count; index++)
            {
                float3 euler = random.NextFloat3(new float3(-math.PI), new float3(math.PI));
                records[index] = new TransformRecord
                {
                    Position = random.NextFloat3(new float3(-2000f), new float3(2000f)),
                    Rotation = quaternion.EulerXYZ(euler),
                    Scale = random.NextFloat3(new float3(0.25f), new float3(4f)),
                    EntityId = index,
                    Flags = random.NextInt(0, 16),
                };
            }

            return records;
        }
    }
}
