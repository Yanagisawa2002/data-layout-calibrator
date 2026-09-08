using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate
{
    // Opt-in boundary factor. Every job owns [block * width, min(count, (block+1) * width)).
    // The unrestricted output array is safe because these ranges cannot overlap.
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA4BlockExportJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ParticleRecordGeneratedPackedAoSoA4Block> HotBlocks;
        [ReadOnly] public NativeArray<quaternion> Rotations;
        [ReadOnly] public NativeArray<int> Categories;
        [NativeDisableParallelForRestriction, WriteOnly] public NativeArray<ParticleRecord> Destination;
        public int LogicalCount;
        public void Execute(int blockIndex)
        {
            var storage = new ParticleRecordGeneratedPackedAoSoA4Storage
            { HotBlocks = HotBlocks, Cold_Rotation = Rotations, Cold_Category = Categories, Count = LogicalCount };
            storage.ExportBlock(blockIndex, Destination);
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA8BlockExportJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ParticleRecordGeneratedPackedAoSoA8Block> HotBlocks;
        [ReadOnly] public NativeArray<quaternion> Rotations;
        [ReadOnly] public NativeArray<int> Categories;
        [NativeDisableParallelForRestriction, WriteOnly] public NativeArray<ParticleRecord> Destination;
        public int LogicalCount;
        public void Execute(int blockIndex)
        {
            var storage = new ParticleRecordGeneratedPackedAoSoA8Storage
            { HotBlocks = HotBlocks, Cold_Rotation = Rotations, Cold_Category = Categories, Count = LogicalCount };
            storage.ExportBlock(blockIndex, Destination);
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA16BlockExportJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ParticleRecordGeneratedPackedAoSoA16Block> HotBlocks;
        [ReadOnly] public NativeArray<quaternion> Rotations;
        [ReadOnly] public NativeArray<int> Categories;
        [NativeDisableParallelForRestriction, WriteOnly] public NativeArray<ParticleRecord> Destination;
        public int LogicalCount;
        public void Execute(int blockIndex)
        {
            var storage = new ParticleRecordGeneratedPackedAoSoA16Storage
            { HotBlocks = HotBlocks, Cold_Rotation = Rotations, Cold_Category = Categories, Count = LogicalCount };
            storage.ExportBlock(blockIndex, Destination);
        }
    }

    public static class ParticleBlockExportScheduler
    {
        public static JobHandle Schedule(ref ParticleRecordGeneratedPackedAoSoA4Storage source,
            NativeArray<ParticleRecord> destination, int logicalBatchSize, JobHandle dependency = default)
        {
            if (!source.IsCreated) throw new ArgumentException("Source is not created.", nameof(source));
            if (!destination.IsCreated || destination.Length != source.Count)
                throw new ArgumentException("Canonical count mismatch.", nameof(destination));
            if (logicalBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(logicalBatchSize));
            return new ParticleAoSoA4BlockExportJob { HotBlocks = source.HotBlocks, Rotations = source.Cold_Rotation,
                Categories = source.Cold_Category, Destination = destination, LogicalCount = source.Count }
                .Schedule(source.BlockCount, Math.Max(1, logicalBatchSize / 4), dependency);
        }
        public static JobHandle Schedule(ref ParticleRecordGeneratedPackedAoSoA8Storage source,
            NativeArray<ParticleRecord> destination, int logicalBatchSize, JobHandle dependency = default)
        {
            if (!source.IsCreated) throw new ArgumentException("Source is not created.", nameof(source));
            if (!destination.IsCreated || destination.Length != source.Count)
                throw new ArgumentException("Canonical count mismatch.", nameof(destination));
            if (logicalBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(logicalBatchSize));
            return new ParticleAoSoA8BlockExportJob { HotBlocks = source.HotBlocks, Rotations = source.Cold_Rotation,
                Categories = source.Cold_Category, Destination = destination, LogicalCount = source.Count }
                .Schedule(source.BlockCount, Math.Max(1, logicalBatchSize / 8), dependency);
        }
        public static JobHandle Schedule(ref ParticleRecordGeneratedPackedAoSoA16Storage source,
            NativeArray<ParticleRecord> destination, int logicalBatchSize, JobHandle dependency = default)
        {
            if (!source.IsCreated) throw new ArgumentException("Source is not created.", nameof(source));
            if (!destination.IsCreated || destination.Length != source.Count)
                throw new ArgumentException("Canonical count mismatch.", nameof(destination));
            if (logicalBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(logicalBatchSize));
            return new ParticleAoSoA16BlockExportJob { HotBlocks = source.HotBlocks, Rotations = source.Cold_Rotation,
                Categories = source.Cold_Category, Destination = destination, LogicalCount = source.Count }
                .Schedule(source.BlockCount, Math.Max(1, logicalBatchSize / 16), dependency);
        }
    }
}
