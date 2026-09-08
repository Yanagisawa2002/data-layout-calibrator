using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.TransformExport.Tests
{
    public sealed class GeneratedTransformStorageScaffoldTests
    {
        [Test]
        public void GeneratedAoSSoAAndAoSoA4CodecsRoundTripCanonicalRecords()
        {
            NativeArray<TransformRecord> source = default;
            NativeArray<TransformRecord> destination = default;
            TransformRecordGeneratedAoSStorage aos = default;
            TransformRecordGeneratedSoAStorage soa = default;
            TransformRecordGeneratedAoSoA4Storage aosoa = default;
            try
            {
                source = TransformExportDataSet.Create(
                    11,
                    TransformExportDataSet.CalibrationSeed,
                    Allocator.TempJob);
                destination = new NativeArray<TransformRecord>(
                    source.Length,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                aos = TransformRecordGeneratedAoSStorage.FromRecords(source, Allocator.TempJob);
                soa = TransformRecordGeneratedSoAStorage.FromRecords(source, Allocator.TempJob);
                aosoa = TransformRecordGeneratedAoSoA4Storage.FromRecords(source, Allocator.TempJob);

                TransformRecordGeneratedDataLayoutCodec.Export(ref aos, destination);
                AssertEqual(source, destination);
                TransformRecordGeneratedDataLayoutCodec.Export(ref soa, destination);
                AssertEqual(source, destination);
                TransformRecordGeneratedDataLayoutCodec.Export(ref aosoa, destination);
                AssertEqual(source, destination);
                Assert.That(aosoa.BlockCount, Is.EqualTo(3));
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
        public void GeneratedSchemaHasIndependentWorkloadIdentityAndFieldMap()
        {
            Assert.That(TransformRecordGeneratedDataLayoutSchema.SchemaId, Is.EqualTo("transform-record"));
            Assert.That(TransformRecordGeneratedDataLayoutSchema.AoSoABlockSize, Is.EqualTo(4));
            Assert.That(TransformRecordGeneratedParityFieldMap.FieldCount, Is.EqualTo(5));
            Assert.That(TransformRecordGeneratedParityFieldMap.GetFieldName(3), Is.EqualTo("EntityId"));
        }

        private static void AssertEqual(
            NativeArray<TransformRecord> expected,
            NativeArray<TransformRecord> actual)
        {
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.That(math.all(actual[index].Position == expected[index].Position), Is.True, $"Position {index}");
                Assert.That(math.all(actual[index].Rotation.value == expected[index].Rotation.value), Is.True, $"Rotation {index}");
                Assert.That(math.all(actual[index].Scale == expected[index].Scale), Is.True, $"Scale {index}");
                Assert.That(actual[index].EntityId, Is.EqualTo(expected[index].EntityId), $"EntityId {index}");
                Assert.That(actual[index].Flags, Is.EqualTo(expected[index].Flags), $"Flags {index}");
            }
        }
    }
}
