using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate
{
    // Compatibility facades own generated storage. NativeArray properties are borrowed views;
    // callers must complete jobs before disposal and must never dispose copied views.
    public struct ParticleAoSStorage : IDisposable
    {
        public ParticleRecordGeneratedAoSStorage Generated;
        public NativeArray<ParticleRecord> Records { get => Generated.Records; set => Generated.Records = value; }
        public int Count => Generated.Count;
        public static ParticleAoSStorage FromRecords(NativeArray<ParticleRecord> source, Allocator allocator) =>
            new ParticleAoSStorage { Generated = ParticleRecordGeneratedAoSStorage.FromRecords(source, allocator) };
        public ParticleRecord ReadRecord(int index) => Generated.ReadRecord(index);
        public void Dispose() => Generated.Dispose();
    }

    public struct ParticleSoAStorage : IDisposable
    {
        public ParticleRecordGeneratedSoAStorage Generated;
        public NativeArray<float3> Positions { get => Generated.Field_Position; set => Generated.Field_Position = value; }
        public NativeArray<float3> Velocities { get => Generated.Field_Velocity; set => Generated.Field_Velocity = value; }
        public NativeArray<quaternion> Rotations { get => Generated.Field_Rotation; set => Generated.Field_Rotation = value; }
        public NativeArray<float> Lifetimes { get => Generated.Field_Lifetime; set => Generated.Field_Lifetime = value; }
        public NativeArray<int> Categories { get => Generated.Field_Category; set => Generated.Field_Category = value; }
        public int Count => Generated.Count;
        public static ParticleSoAStorage FromRecords(NativeArray<ParticleRecord> source, Allocator allocator) =>
            new ParticleSoAStorage { Generated = ParticleRecordGeneratedSoAStorage.FromRecords(source, allocator) };
        public ParticleRecord ReadRecord(int index) => Generated.ReadRecord(index);
        public void Dispose() => Generated.Dispose();
    }

    public struct ParticleAoSoA8Storage : IDisposable
    {
        public ParticleRecordGeneratedPackedAoSoA8Storage Generated;
        public NativeArray<ParticleRecordGeneratedPackedAoSoA8Block> HotBlocks { get => Generated.HotBlocks; set => Generated.HotBlocks = value; }
        public NativeArray<quaternion> Rotations { get => Generated.Cold_Rotation; set => Generated.Cold_Rotation = value; }
        public NativeArray<int> Categories { get => Generated.Cold_Category; set => Generated.Cold_Category = value; }
        public int Count => Generated.Count;
        public const int BlockWidth = ParticleRecordGeneratedPackedAoSoA8Storage.BlockWidth;
        public int BlockCount => Generated.BlockCount;
        public static ParticleAoSoA8Storage FromRecords(NativeArray<ParticleRecord> source, Allocator allocator) =>
            new ParticleAoSoA8Storage { Generated = ParticleRecordGeneratedPackedAoSoA8Storage.FromRecords(source, allocator) };
        public ParticleRecord ReadRecord(int index) => Generated.ReadRecord(index);
        public void Dispose() => Generated.Dispose();
    }

}
