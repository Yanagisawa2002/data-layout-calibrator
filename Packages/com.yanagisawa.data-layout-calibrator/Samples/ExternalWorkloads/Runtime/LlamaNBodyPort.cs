// Copyright 2024 Bernhard Manfred Gruber
// SPDX-License-Identifier: MPL-2.0
// Modified: C# / Burst port with separate source/velocity storage and four target
// lanes. Based on the pinned examples/nbody_code_comp/nbody-AoS-baseline.cpp.
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ExternalWorkloads
{
    public struct LlamaParticle { public float3 Position, Velocity; public float Mass; }
    public struct LlamaSource4 { public float4 X, Y, Z, Mass; }
    public struct LlamaVelocity4 { public float4 X, Y, Z; }

    public static class LlamaNBodyContract
    {
        public const string UpstreamCommit = "086e66e7565f677d6b3aff88542e28c7dd6d8228";
        public const string EvidenceStatus = "Unmeasured";
        public const int DefaultCount = 64 * 1024, DefaultSteps = 5;
        public const float TimeStep = 0.0001f, Eps2 = 0.01f;

        // Retain the upstream squared-component force, including negative masses,
        // self interaction and ascending j order. This is not a corrected gravity law.
        public static void Interact(ref LlamaParticle target, LlamaParticle source)
        {
            float3 d = target.Position - source.Position;
            d *= d;
            float distance = ((Eps2 + d.x) + d.y) + d.z;
            float sixth = (distance * distance) * distance;
            float scale = (source.Mass * TimeStep) * (1f / math.sqrt(sixth));
            target.Velocity += d * scale;
        }
    }

    public struct LlamaNBodyPackedStorage : IDisposable
    {
        public NativeArray<LlamaSource4> Sources;
        public NativeArray<LlamaVelocity4> Velocities;
        public int Count;

        public static LlamaNBodyPackedStorage FromRecords(NativeArray<LlamaParticle> source, Allocator allocator)
        {
            if (!source.IsCreated) throw new ArgumentException("Source is not created.", nameof(source));
            var storage = new LlamaNBodyPackedStorage();
            try
            {
                storage.Count = source.Length;
                int blocks = source.Length / 4 + (source.Length % 4 == 0 ? 0 : 1);
                storage.Sources = new NativeArray<LlamaSource4>(blocks, allocator);
                storage.Velocities = new NativeArray<LlamaVelocity4>(blocks, allocator);
                for (int b = 0; b < blocks; b++)
                {
                    LlamaSource4 p = default; LlamaVelocity4 v = default;
                    for (int lane = 0; lane < math.min(4, source.Length - b * 4); lane++)
                    {
                        var r = source[b * 4 + lane];
                        p.X[lane] = r.Position.x; p.Y[lane] = r.Position.y; p.Z[lane] = r.Position.z; p.Mass[lane] = r.Mass;
                        v.X[lane] = r.Velocity.x; v.Y[lane] = r.Velocity.y; v.Z[lane] = r.Velocity.z;
                    }
                    storage.Sources[b] = p; storage.Velocities[b] = v;
                }
                return storage;
            }
            catch { storage.Dispose(); throw; }
        }

        public JobHandle ScheduleStep(int logicalBatchSize, JobHandle dependency = default)
        {
            if (!Sources.IsCreated) throw new ObjectDisposedException(nameof(LlamaNBodyPackedStorage));
            if (logicalBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(logicalBatchSize));
            int batch = Math.Max(1, logicalBatchSize / 4);
            var update = new LlamaNBodyUpdate4Job { Sources = Sources, Velocities = Velocities, LogicalCount = Count }
                .Schedule(Sources.Length, batch, dependency);
            // Required global update -> move barrier: all interactions see the old positions.
            return new LlamaNBodyMove4Job { Sources = Sources, Velocities = Velocities }.Schedule(Sources.Length, batch, update);
        }

        public void Export(NativeArray<LlamaParticle> destination)
        {
            if (!Sources.IsCreated) throw new ObjectDisposedException(nameof(LlamaNBodyPackedStorage));
            if (!destination.IsCreated || destination.Length != Count) throw new ArgumentException("Canonical count mismatch.", nameof(destination));
            for (int b = 0; b < Sources.Length; b++)
            {
                var p = Sources[b]; var v = Velocities[b];
                for (int lane = 0; lane < math.min(4, Count - b * 4); lane++)
                    destination[b * 4 + lane] = new LlamaParticle
                    { Position = new float3(p.X[lane], p.Y[lane], p.Z[lane]), Velocity = new float3(v.X[lane], v.Y[lane], v.Z[lane]), Mass = p.Mass[lane] };
            }
        }

        public void Dispose()
        {
            if (Sources.IsCreated) Sources.Dispose();
            if (Velocities.IsCreated) Velocities.Dispose();
            Sources = default; Velocities = default; Count = 0;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict)]
    public struct LlamaNBodyUpdate4Job : IJobParallelFor
    {
        // Arbitrary source blocks are read; only this target's velocity block is
        // written. The source snapshot is immutable until the dependent move job.
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<LlamaSource4> Sources;
        public NativeArray<LlamaVelocity4> Velocities;
        public int LogicalCount;
        public void Execute(int targetBlock)
        {
            var p = Sources[targetBlock]; var v = Velocities[targetBlock];
            for (int block = 0; block < Sources.Length; block++)
            {
                // Source block loaded once, then broadcast each valid source lane
                // to four independent targets. No horizontal reassociation/reduction.
                var other = Sources[block];
                int lanes = math.min(4, LogicalCount - block * 4);
                for (int lane = 0; lane < lanes; lane++)
                {
                    float4 x = p.X - other.X[lane], y = p.Y - other.Y[lane], z = p.Z - other.Z[lane];
                    x *= x; y *= y; z *= z;
                    float4 distance = ((LlamaNBodyContract.Eps2 + x) + y) + z;
                    float4 sixth = (distance * distance) * distance;
                    float4 scale = (other.Mass[lane] * LlamaNBodyContract.TimeStep) * (1f / math.sqrt(sixth));
                    v.X += x * scale; v.Y += y * scale; v.Z += z * scale;
                }
            }
            Velocities[targetBlock] = v;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict)]
    public struct LlamaNBodyMove4Job : IJobParallelFor
    {
        public NativeArray<LlamaSource4> Sources;
        [ReadOnly] public NativeArray<LlamaVelocity4> Velocities;
        public void Execute(int i)
        {
            var p = Sources[i]; var v = Velocities[i];
            p.X += v.X * LlamaNBodyContract.TimeStep; p.Y += v.Y * LlamaNBodyContract.TimeStep; p.Z += v.Z * LlamaNBodyContract.TimeStep;
            Sources[i] = p;
        }
    }
}
