using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate
{
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA4IngressJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ParticleRecord> Source;
        [WriteOnly] public NativeArray<ParticleRecordGeneratedPackedAoSoA4Block> HotBlocks;
        [NativeDisableParallelForRestriction, WriteOnly]
        public NativeArray<quaternion> Rotations;
        [NativeDisableParallelForRestriction, WriteOnly]
        public NativeArray<int> Categories;
        public int LogicalCount;

        public void Execute(int blockIndex)
        {
            var storage = new ParticleRecordGeneratedPackedAoSoA4Storage
            {
                HotBlocks = HotBlocks, Cold_Rotation = Rotations, Cold_Category = Categories,
                Count = LogicalCount,
            };
            storage.IngressBlock(blockIndex, Source);
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA4ExportJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ParticleRecordGeneratedPackedAoSoA4Block> HotBlocks;
        [ReadOnly] public NativeArray<quaternion> Rotations;
        [ReadOnly] public NativeArray<int> Categories;
        [WriteOnly] public NativeArray<ParticleRecord> Destination;

        public void Execute(int index)
        {
            var storage = new ParticleRecordGeneratedPackedAoSoA4Storage
            {
                HotBlocks = HotBlocks, Cold_Rotation = Rotations, Cold_Category = Categories,
            };
            Destination[index] = storage.ReadRecord(index);
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA16IngressJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ParticleRecord> Source;
        [WriteOnly] public NativeArray<ParticleRecordGeneratedPackedAoSoA16Block> HotBlocks;
        [NativeDisableParallelForRestriction, WriteOnly]
        public NativeArray<quaternion> Rotations;
        [NativeDisableParallelForRestriction, WriteOnly]
        public NativeArray<int> Categories;
        public int LogicalCount;

        public void Execute(int blockIndex)
        {
            var storage = new ParticleRecordGeneratedPackedAoSoA16Storage
            {
                HotBlocks = HotBlocks, Cold_Rotation = Rotations, Cold_Category = Categories,
                Count = LogicalCount,
            };
            storage.IngressBlock(blockIndex, Source);
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticleAoSoA16ExportJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ParticleRecordGeneratedPackedAoSoA16Block> HotBlocks;
        [ReadOnly] public NativeArray<quaternion> Rotations;
        [ReadOnly] public NativeArray<int> Categories;
        [WriteOnly] public NativeArray<ParticleRecord> Destination;

        public void Execute(int index)
        {
            var storage = new ParticleRecordGeneratedPackedAoSoA16Storage
            {
                HotBlocks = HotBlocks, Cold_Rotation = Rotations, Cold_Category = Categories,
            };
            Destination[index] = storage.ReadRecord(index);
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticlePadded64IngressJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ParticleRecord> Source;
        [WriteOnly] public NativeArray<ParticleRecordGeneratedPadded64Record> Records;
        public void Execute(int index)
        {
            var storage = new ParticleRecordGeneratedPadded64Storage { Records = Records };
            storage.WriteRecord(index, Source[index]);
        }
    }
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ParticlePadded64ExportJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ParticleRecordGeneratedPadded64Record> Records;
        [WriteOnly] public NativeArray<ParticleRecord> Destination;
        public void Execute(int index)
        {
            var storage = new ParticleRecordGeneratedPadded64Storage { Records = Records };
            Destination[index] = storage.ReadRecord(index);
        }
    }
}
