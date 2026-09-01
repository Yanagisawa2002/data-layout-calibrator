using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.TransformExport
{
    public struct TransformSoAStorage : IDisposable
    {
        public NativeArray<float3> Positions;
        public NativeArray<quaternion> Rotations;
        public NativeArray<float3> Scales;
        public NativeArray<int> EntityIds;
        public NativeArray<int> Flags;

        public int Count => Positions.IsCreated ? Positions.Length : 0;

        public static TransformSoAStorage Allocate(int count, Allocator allocator)
        {
            return new TransformSoAStorage
            {
                Positions = NewArray<float3>(count, allocator),
                Rotations = NewArray<quaternion>(count, allocator),
                Scales = NewArray<float3>(count, allocator),
                EntityIds = NewArray<int>(count, allocator),
                Flags = NewArray<int>(count, allocator),
            };
        }

        public void Dispose()
        {
            DisposeIfCreated(ref Positions);
            DisposeIfCreated(ref Rotations);
            DisposeIfCreated(ref Scales);
            DisposeIfCreated(ref EntityIds);
            DisposeIfCreated(ref Flags);
        }

        private static NativeArray<T> NewArray<T>(int count, Allocator allocator)
            where T : struct
        {
            return new NativeArray<T>(count, allocator, NativeArrayOptions.UninitializedMemory);
        }

        private static void DisposeIfCreated<T>(ref NativeArray<T> array)
            where T : struct
        {
            if (array.IsCreated)
                array.Dispose();
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct TransformSoAIngressJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<TransformRecord> Source;
        [WriteOnly] public NativeArray<float3> Positions;
        [WriteOnly] public NativeArray<quaternion> Rotations;
        [WriteOnly] public NativeArray<float3> Scales;
        [WriteOnly] public NativeArray<int> EntityIds;
        [WriteOnly] public NativeArray<int> Flags;

        public void Execute(int index)
        {
            TransformRecord record = Source[index];
            Positions[index] = record.Position;
            Rotations[index] = record.Rotation;
            Scales[index] = record.Scale;
            EntityIds[index] = record.EntityId;
            Flags[index] = record.Flags;
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct TransformAoSExportJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<TransformRecord> Records;
        [WriteOnly] public NativeArray<TransformExportRecord> Output;

        public void Execute(int index)
        {
            TransformRecord record = Records[index];
            Output[index] = new TransformExportRecord
            {
                LocalToWorld = float4x4.TRS(record.Position, record.Rotation, record.Scale),
                EntityId = record.EntityId,
                Flags = record.Flags,
            };
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct TransformSoAExportJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<quaternion> Rotations;
        [ReadOnly] public NativeArray<float3> Scales;
        [ReadOnly] public NativeArray<int> EntityIds;
        [ReadOnly] public NativeArray<int> Flags;
        [WriteOnly] public NativeArray<TransformExportRecord> Output;

        public void Execute(int index)
        {
            Output[index] = new TransformExportRecord
            {
                LocalToWorld = float4x4.TRS(Positions[index], Rotations[index], Scales[index]),
                EntityId = EntityIds[index],
                Flags = Flags[index],
            };
        }
    }
}
