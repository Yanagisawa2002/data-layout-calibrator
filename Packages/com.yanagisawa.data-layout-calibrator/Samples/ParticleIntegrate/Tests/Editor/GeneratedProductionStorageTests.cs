using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate.Tests
{
    public sealed class GeneratedProductionStorageTests
    {
        [Test]
        public void PackedEightRetainsHistoricalShapeAndPaddedStrideIsExplicit()
        {
            Assert.That(UnsafeUtility.SizeOf<ParticleRecordGeneratedPackedAoSoA8Block>(), Is.EqualTo(224));
            Assert.That(Marshal.OffsetOf<ParticleRecordGeneratedPackedAoSoA8Block>("PositionX1").ToInt32(), Is.EqualTo(16));
            Assert.That(Marshal.OffsetOf<ParticleRecordGeneratedPackedAoSoA8Block>("PositionY0").ToInt32(), Is.EqualTo(32));
            Assert.That(Marshal.OffsetOf<ParticleRecordGeneratedPackedAoSoA8Block>("Lifetime1").ToInt32(), Is.EqualTo(208));
            Assert.That(UnsafeUtility.SizeOf<ParticleRecord>(), Is.EqualTo(48));
            Assert.That(UnsafeUtility.SizeOf<ParticleRecordGeneratedPadded64Record>(), Is.EqualTo(64));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(4099)]
        public void Packed4PreservesAllFieldsAndDisposesOwner(int count)
        {
            using (var source = new NativeArray<ParticleRecord>(count, Allocator.TempJob))
            using (var output = new NativeArray<ParticleRecord>(count, Allocator.TempJob))
            {
                var writableSource = source;
                for (int i = 0; i < count; i++) writableSource[i] = new ParticleRecord
                { Position = new float3(i, -i, i + 0.25f), Velocity = new float3(1, 2, 3),
                   Rotation = quaternion.identity, Lifetime = 4, Category = -i - 17 };
                var storage = ParticleRecordGeneratedPackedAoSoA4Storage.FromRecords(source, Allocator.TempJob);
                try
                {
                    storage.Export(output);
                    for (int i = 0; i < count; i++)
                        Assert.That(ParticleStateValidation.ApproximatelyEqual(source[i], output[i], 0, out string reason), Is.True, reason);
                    Assert.That(storage.BlockCount, Is.EqualTo(count / 4 + (count % 4 == 0 ? 0 : 1)));
                    if (count % 4 != 0)
                    {
                        var tail = storage.HotBlocks[storage.BlockCount - 1];
                        for (int lane = count % 4; lane < 4; lane++)
                            Assert.That(ParticleRecordGeneratedPackedAoSoA4Storage.Read_Lifetime(tail, lane), Is.Zero);
                    }
                    using (var wrong = new NativeArray<ParticleRecord>(count + 1, Allocator.TempJob))
                        Assert.Throws<ArgumentException>(() => storage.Ingress(wrong));
                    for (int block = 0; block < storage.BlockCount; block++) storage.IngressBlock(block, source);
                    storage.Export(output);
                    for (int i = 0; i < count; i++) Assert.That(output[i].Category, Is.EqualTo(source[i].Category));
                }
                finally { storage.Dispose(); }
                storage.Dispose();
                Assert.That(storage.IsCreated, Is.False);
                Assert.That(storage.Count, Is.Zero);
                Assert.Throws<InvalidOperationException>(() => storage.Export(output));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => ParticleRecordGeneratedPackedAoSoA4Storage.Allocate(-1, Allocator.TempJob));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(7)] [TestCase(8)] [TestCase(9)] [TestCase(4099)]
        public void Packed8PreservesAllFieldsAndDisposesOwner(int count)
        {
            using (var source = new NativeArray<ParticleRecord>(count, Allocator.TempJob))
            using (var output = new NativeArray<ParticleRecord>(count, Allocator.TempJob))
            {
                var writableSource = source;
                for (int i = 0; i < count; i++) writableSource[i] = new ParticleRecord
                { Position = new float3(i, -i, i + 0.25f), Velocity = new float3(1, 2, 3),
                   Rotation = quaternion.identity, Lifetime = 4, Category = -i - 17 };
                var storage = ParticleRecordGeneratedPackedAoSoA8Storage.FromRecords(source, Allocator.TempJob);
                try
                {
                    storage.Export(output);
                    for (int i = 0; i < count; i++)
                        Assert.That(ParticleStateValidation.ApproximatelyEqual(source[i], output[i], 0, out string reason), Is.True, reason);
                    Assert.That(storage.BlockCount, Is.EqualTo(count / 8 + (count % 8 == 0 ? 0 : 1)));
                    if (count % 8 != 0)
                    {
                        var tail = storage.HotBlocks[storage.BlockCount - 1];
                        for (int lane = count % 8; lane < 8; lane++)
                            Assert.That(ParticleRecordGeneratedPackedAoSoA8Storage.Read_Lifetime(tail, lane), Is.Zero);
                    }
                    using (var wrong = new NativeArray<ParticleRecord>(count + 1, Allocator.TempJob))
                        Assert.Throws<ArgumentException>(() => storage.Ingress(wrong));
                    for (int block = 0; block < storage.BlockCount; block++) storage.IngressBlock(block, source);
                    storage.Export(output);
                    for (int i = 0; i < count; i++) Assert.That(output[i].Category, Is.EqualTo(source[i].Category));
                }
                finally { storage.Dispose(); }
                storage.Dispose();
                Assert.That(storage.IsCreated, Is.False);
                Assert.That(storage.Count, Is.Zero);
                Assert.Throws<InvalidOperationException>(() => storage.Export(output));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => ParticleRecordGeneratedPackedAoSoA8Storage.Allocate(-1, Allocator.TempJob));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(15)] [TestCase(16)] [TestCase(17)] [TestCase(4099)]
        public void Packed16PreservesAllFieldsAndDisposesOwner(int count)
        {
            using (var source = new NativeArray<ParticleRecord>(count, Allocator.TempJob))
            using (var output = new NativeArray<ParticleRecord>(count, Allocator.TempJob))
            {
                var writableSource = source;
                for (int i = 0; i < count; i++) writableSource[i] = new ParticleRecord
                { Position = new float3(i, -i, i + 0.25f), Velocity = new float3(1, 2, 3),
                   Rotation = quaternion.identity, Lifetime = 4, Category = -i - 17 };
                var storage = ParticleRecordGeneratedPackedAoSoA16Storage.FromRecords(source, Allocator.TempJob);
                try
                {
                    storage.Export(output);
                    for (int i = 0; i < count; i++)
                        Assert.That(ParticleStateValidation.ApproximatelyEqual(source[i], output[i], 0, out string reason), Is.True, reason);
                    Assert.That(storage.BlockCount, Is.EqualTo(count / 16 + (count % 16 == 0 ? 0 : 1)));
                    if (count % 16 != 0)
                    {
                        var tail = storage.HotBlocks[storage.BlockCount - 1];
                        for (int lane = count % 16; lane < 16; lane++)
                            Assert.That(ParticleRecordGeneratedPackedAoSoA16Storage.Read_Lifetime(tail, lane), Is.Zero);
                    }
                    using (var wrong = new NativeArray<ParticleRecord>(count + 1, Allocator.TempJob))
                        Assert.Throws<ArgumentException>(() => storage.Ingress(wrong));
                    for (int block = 0; block < storage.BlockCount; block++) storage.IngressBlock(block, source);
                    storage.Export(output);
                    for (int i = 0; i < count; i++) Assert.That(output[i].Category, Is.EqualTo(source[i].Category));
                }
                finally { storage.Dispose(); }
                storage.Dispose();
                Assert.That(storage.IsCreated, Is.False);
                Assert.That(storage.Count, Is.Zero);
                Assert.Throws<InvalidOperationException>(() => storage.Export(output));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => ParticleRecordGeneratedPackedAoSoA16Storage.Allocate(-1, Allocator.TempJob));
        }
    }
}
