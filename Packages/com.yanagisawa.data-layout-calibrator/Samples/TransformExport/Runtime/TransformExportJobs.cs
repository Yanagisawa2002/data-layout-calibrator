using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.TransformExport
{
    public struct TransformSoAStorage : IDisposable
    {
        public TransformRecordGeneratedSoAStorage Generated;
        public NativeArray<float3> Positions { get => Generated.Field_Position; set => Generated.Field_Position = value; }
        public NativeArray<quaternion> Rotations { get => Generated.Field_Rotation; set => Generated.Field_Rotation = value; }
        public NativeArray<float3> Scales { get => Generated.Field_Scale; set => Generated.Field_Scale = value; }
        public NativeArray<int> EntityIds { get => Generated.Field_EntityId; set => Generated.Field_EntityId = value; }
        public NativeArray<int> Flags { get => Generated.Field_Flags; set => Generated.Field_Flags = value; }
        public int Count => Generated.Count;
        public static TransformSoAStorage Allocate(int count, Allocator allocator) =>
            new TransformSoAStorage { Generated = TransformRecordGeneratedSoAStorage.Allocate(count, allocator) };
        public void Dispose() => Generated.Dispose();
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
            var storage = new TransformRecordGeneratedSoAStorage
            {
                Field_Position = Positions, Field_Rotation = Rotations, Field_Scale = Scales,
                Field_EntityId = EntityIds, Field_Flags = Flags,
            };
            storage.WriteRecord(index, Source[index]);
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
