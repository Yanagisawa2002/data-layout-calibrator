using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.TransformExport
{
    // Four independent transforms per float4, rather than one float4 quaternion.
    // All fields are consumed by export; EntityId/Flags remain integer payloads.
    public struct TransformTrs4Block
    {
        public float4 PX, PY, PZ, QX, QY, QZ, QW, SX, SY, SZ;
        public int4 EntityIds, Flags;
    }

    public struct TransformPacked4Storage : IDisposable
    {
        public NativeArray<TransformTrs4Block> Blocks;
        public int Count;
        public const string EvidenceStatus = "Unmeasured";

        public static TransformPacked4Storage FromRecords(NativeArray<TransformRecord> source, Allocator allocator)
        {
            if (!source.IsCreated) throw new ArgumentException("Source is not created.", nameof(source));
            var storage = new TransformPacked4Storage();
            try
            {
                storage.Count = source.Length;
                storage.Blocks = new NativeArray<TransformTrs4Block>(source.Length / 4 + (source.Length % 4 == 0 ? 0 : 1), allocator);
                var ingress = new TransformPacked4IngressJob { Source = source, Blocks = storage.Blocks };
                for (int i = 0; i < storage.Blocks.Length; i++) ingress.Execute(i);
                return storage;
            }
            catch { storage.Dispose(); throw; }
        }

        public JobHandle ScheduleExport(NativeArray<TransformExportRecord> output, int logicalBatchSize, JobHandle dependency = default)
        {
            if (!Blocks.IsCreated) throw new ObjectDisposedException(nameof(TransformPacked4Storage));
            if (!output.IsCreated || output.Length != Count) throw new ArgumentException("Canonical count mismatch.", nameof(output));
            if (logicalBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(logicalBatchSize));
            return new TransformPacked4ExportJob { Blocks = Blocks, Output = output, LogicalCount = Count }
                .Schedule(Blocks.Length, Math.Max(1, logicalBatchSize / 4), dependency);
        }

        public void Dispose()
        {
            if (Blocks.IsCreated) Blocks.Dispose();
            Blocks = default; Count = 0;
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct TransformPacked4IngressJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<TransformRecord> Source;
        [WriteOnly] public NativeArray<TransformTrs4Block> Blocks;
        public void Execute(int blockIndex)
        {
            TransformTrs4Block b = default;
            int first = blockIndex * 4;
            int lanes = math.min(4, Source.Length - first);
            for (int lane = 0; lane < lanes; lane++)
            {
                var r = Source[first + lane];
                b.PX[lane] = r.Position.x; b.PY[lane] = r.Position.y; b.PZ[lane] = r.Position.z;
                b.QX[lane] = r.Rotation.value.x; b.QY[lane] = r.Rotation.value.y;
                b.QZ[lane] = r.Rotation.value.z; b.QW[lane] = r.Rotation.value.w;
                b.SX[lane] = r.Scale.x; b.SY[lane] = r.Scale.y; b.SZ[lane] = r.Scale.z;
                b.EntityIds[lane] = r.EntityId; b.Flags[lane] = r.Flags;
            }
            Blocks[blockIndex] = b;
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct TransformPacked4ExportJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<TransformTrs4Block> Blocks;
        // Block b owns only [4*b, min(4*b+4, LogicalCount)); ranges are disjoint.
        [NativeDisableParallelForRestriction, WriteOnly] public NativeArray<TransformExportRecord> Output;
        public int LogicalCount;
        public void Execute(int blockIndex)
        {
            var b = Blocks[blockIndex];
            float4 x2 = b.QX + b.QX, y2 = b.QY + b.QY, z2 = b.QZ + b.QZ;
            float4 xx = x2 * b.QX, yy = y2 * b.QY, zz = z2 * b.QZ;
            float4 xy = x2 * b.QY, xz = x2 * b.QZ, yz = y2 * b.QZ;
            float4 wx = x2 * b.QW, wy = y2 * b.QW, wz = z2 * b.QW;
            float4 c0x = (1f - yy - zz) * b.SX, c0y = (xy + wz) * b.SX, c0z = (xz - wy) * b.SX;
            float4 c1x = (xy - wz) * b.SY, c1y = (1f - xx - zz) * b.SY, c1z = (yz + wx) * b.SY;
            float4 c2x = (xz + wy) * b.SZ, c2y = (yz - wx) * b.SZ, c2z = (1f - xx - yy) * b.SZ;
            int first = blockIndex * 4;
            int lanes = math.min(4, LogicalCount - first);
            for (int lane = 0; lane < lanes; lane++)
                Output[first + lane] = new TransformExportRecord
                {
                    LocalToWorld = new float4x4(new float4(c0x[lane], c0y[lane], c0z[lane], 0f),
                        new float4(c1x[lane], c1y[lane], c1z[lane], 0f),
                        new float4(c2x[lane], c2y[lane], c2z[lane], 0f),
                        new float4(b.PX[lane], b.PY[lane], b.PZ[lane], 1f)),
                    EntityId = b.EntityIds[lane], Flags = b.Flags[lane],
                };
        }
    }
}
