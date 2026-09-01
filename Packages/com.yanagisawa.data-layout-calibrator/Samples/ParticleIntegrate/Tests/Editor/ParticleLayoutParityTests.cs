using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate.Tests
{
    public sealed class ParticleLayoutParityTests
    {
        private const float DeltaTime = 1.0f / 60.0f;
        private const float FieldTolerance = 1e-5f;
        private const int LogicalBatchSize = 64;

        [TestCase(1, 1)]
        [TestCase(1, 97)]
        [TestCase(7, 1)]
        [TestCase(7, 97)]
        [TestCase(8, 1)]
        [TestCase(8, 97)]
        [TestCase(9, 1)]
        [TestCase(9, 97)]
        [TestCase(4099, 1)]
        [TestCase(4099, 97)]
        public void StepJobs_KeepAllLayoutsEquivalent(
            int count,
            int stepCount)
        {
            NativeArray<ParticleRecord> source = default;
            ParticleAoSStorage aos = default;
            ParticleSoAStorage soa = default;
            ParticleAoSoA8Storage aosoa8 = default;

            try
            {
                source = ParticleDataSet.Create(
                    count,
                    ParticleDataSet.CalibrationSeed,
                    Allocator.TempJob);
                aos = ParticleAoSStorage.FromRecords(source, Allocator.TempJob);
                soa = ParticleSoAStorage.FromRecords(source, Allocator.TempJob);
                aosoa8 = ParticleAoSoA8Storage.FromRecords(source, Allocator.TempJob);

                for (int step = 0; step < stepCount; step++)
                {
                    ParticleJobScheduler
                        .Schedule(ref aos, LogicalBatchSize, DeltaTime)
                        .Complete();
                    ParticleJobScheduler
                        .Schedule(ref soa, LogicalBatchSize, DeltaTime)
                        .Complete();
                    ParticleJobScheduler
                        .Schedule(ref aosoa8, LogicalBatchSize, DeltaTime)
                        .Complete();
                }

                AssertFieldParity(ref aos, ref soa, ref aosoa8);
                AssertHashParity(ref aos, ref soa, ref aosoa8);
                AssertColdFieldsUnchanged(source, ref aos, ref soa, ref aosoa8);
            }
            finally
            {
                aosoa8.Dispose();
                soa.Dispose();
                aos.Dispose();
                if (source.IsCreated)
                    source.Dispose();
            }
        }

        private static void AssertFieldParity(
            ref ParticleAoSStorage aos,
            ref ParticleSoAStorage soa,
            ref ParticleAoSoA8Storage aosoa8)
        {
            for (int index = 0; index < aos.Count; index++)
            {
                ParticleRecord expected = aos.ReadRecord(index);
                ParticleRecord soaRecord = soa.ReadRecord(index);
                ParticleRecord aosoa8Record = aosoa8.ReadRecord(index);

                Assert.That(
                    ParticleStateValidation.ApproximatelyEqual(
                        expected,
                        soaRecord,
                        FieldTolerance,
                        out string soaFailure),
                    Is.True,
                    $"SoA mismatch at index {index}: {soaFailure}");
                Assert.That(
                    ParticleStateValidation.ApproximatelyEqual(
                        expected,
                        aosoa8Record,
                        FieldTolerance,
                        out string aosoa8Failure),
                    Is.True,
                    $"AoSoA8 mismatch at index {index}: {aosoa8Failure}");
            }
        }

        private static void AssertHashParity(
            ref ParticleAoSStorage aos,
            ref ParticleSoAStorage soa,
            ref ParticleAoSoA8Storage aosoa8)
        {
            ulong aosHash = ParticleStateValidation.ComputeHash(ref aos);
            ulong soaHash = ParticleStateValidation.ComputeHash(ref soa);
            ulong aosoa8Hash = ParticleStateValidation.ComputeHash(ref aosoa8);

            Assert.That(soaHash, Is.EqualTo(aosHash),
                "SoA quantized hash differs from AoS.");
            Assert.That(aosoa8Hash, Is.EqualTo(aosHash),
                "AoSoA8 quantized hash differs from AoS.");
        }

        private static void AssertColdFieldsUnchanged(
            NativeArray<ParticleRecord> source,
            ref ParticleAoSStorage aos,
            ref ParticleSoAStorage soa,
            ref ParticleAoSoA8Storage aosoa8)
        {
            for (int index = 0; index < source.Length; index++)
            {
                ParticleRecord original = source[index];
                AssertColdFieldsEqual(original, aos.ReadRecord(index), "AoS", index);
                AssertColdFieldsEqual(original, soa.ReadRecord(index), "SoA", index);
                AssertColdFieldsEqual(original, aosoa8.ReadRecord(index), "AoSoA8", index);
            }
        }

        private static void AssertColdFieldsEqual(
            ParticleRecord expected,
            ParticleRecord actual,
            string layout,
            int index)
        {
            Assert.That(
                math.all(expected.Rotation.value == actual.Rotation.value),
                Is.True,
                $"{layout} rotation changed at index {index}.");
            Assert.That(actual.Category, Is.EqualTo(expected.Category),
                $"{layout} category changed at index {index}.");
        }
    }
}
