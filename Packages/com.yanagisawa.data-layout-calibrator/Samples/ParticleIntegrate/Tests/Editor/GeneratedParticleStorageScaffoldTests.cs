using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate.Tests
{
    public sealed class GeneratedParticleStorageScaffoldTests
    {
        [Test]
        public void GeneratedAoSSoAAndAoSoA8CodecsRoundTripCanonicalRecords()
        {
            NativeArray<ParticleRecord> source = default;
            NativeArray<ParticleRecord> destination = default;
            ParticleRecordGeneratedAoSStorage aos = default;
            ParticleRecordGeneratedSoAStorage soa = default;
            ParticleRecordGeneratedAoSoA8Storage aosoa = default;
            try
            {
                source = ParticleDataSet.Create(13, ParticleDataSet.CalibrationSeed, Allocator.TempJob);
                destination = new NativeArray<ParticleRecord>(
                    source.Length,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                aos = ParticleRecordGeneratedAoSStorage.FromRecords(source, Allocator.TempJob);
                soa = ParticleRecordGeneratedSoAStorage.FromRecords(source, Allocator.TempJob);
                aosoa = ParticleRecordGeneratedAoSoA8Storage.FromRecords(source, Allocator.TempJob);

                AssertRoundTrip(source, destination, ref aos);
                AssertRoundTrip(source, destination, ref soa);
                AssertRoundTrip(source, destination, ref aosoa);
                Assert.That(aosoa.BlockCount, Is.EqualTo(2));
            }
            finally
            {
                aosoa.Dispose();
                soa.Dispose();
                aos.Dispose();
                if (destination.IsCreated)
                    destination.Dispose();
                if (source.IsCreated)
                    source.Dispose();
            }
        }

        [Test]
        public void GeneratedSchemaAndParityMapExposeExplicitStableMetadata()
        {
            Assert.That(ParticleRecordGeneratedDataLayoutSchema.SchemaId, Is.EqualTo("particle-record"));
            Assert.That(ParticleRecordGeneratedDataLayoutSchema.SchemaVersion, Is.EqualTo(1));
            Assert.That(ParticleRecordGeneratedDataLayoutSchema.SchemaHashSha256, Does.Match("^[0-9A-F]{64}$"));
            Assert.That(ParticleRecordGeneratedParityFieldMap.FieldCount, Is.EqualTo(5));
            Assert.That(ParticleRecordGeneratedParityFieldMap.GetFieldName(0), Is.EqualTo("Position"));
            Assert.That(
                ParticleRecordGeneratedParityFieldMap.GetTemperature(2),
                Is.EqualTo(DataLayoutFieldTemperature.Cold));
        }

        private static void AssertRoundTrip(
            NativeArray<ParticleRecord> source,
            NativeArray<ParticleRecord> destination,
            ref ParticleRecordGeneratedAoSStorage storage)
        {
            ParticleRecordGeneratedDataLayoutCodec.Export(ref storage, destination);
            AssertEqual(source, destination);
        }

        private static void AssertRoundTrip(
            NativeArray<ParticleRecord> source,
            NativeArray<ParticleRecord> destination,
            ref ParticleRecordGeneratedSoAStorage storage)
        {
            ParticleRecordGeneratedDataLayoutCodec.Export(ref storage, destination);
            AssertEqual(source, destination);
        }

        private static void AssertRoundTrip(
            NativeArray<ParticleRecord> source,
            NativeArray<ParticleRecord> destination,
            ref ParticleRecordGeneratedAoSoA8Storage storage)
        {
            ParticleRecordGeneratedDataLayoutCodec.Export(ref storage, destination);
            AssertEqual(source, destination);
        }

        private static void AssertEqual(
            NativeArray<ParticleRecord> expected,
            NativeArray<ParticleRecord> actual)
        {
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.That(math.all(actual[index].Position == expected[index].Position), Is.True, $"Position {index}");
                Assert.That(math.all(actual[index].Velocity == expected[index].Velocity), Is.True, $"Velocity {index}");
                Assert.That(math.all(actual[index].Rotation.value == expected[index].Rotation.value), Is.True, $"Rotation {index}");
                Assert.That(actual[index].Lifetime, Is.EqualTo(expected[index].Lifetime), $"Lifetime {index}");
                Assert.That(actual[index].Category, Is.EqualTo(expected[index].Category), $"Category {index}");
            }
        }
    }
}
