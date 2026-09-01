using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutAutotuner.Tests
{
    public sealed class ParticleDataSetTests
    {
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(4099)]
        public void Create_WithSameSeed_ProducesIdenticalRecords(int count)
        {
            NativeArray<ParticleRecord> first = default;
            NativeArray<ParticleRecord> second = default;

            try
            {
                first = ParticleDataSet.Create(
                    count,
                    ParticleDataSet.CalibrationSeed,
                    Allocator.TempJob);
                second = ParticleDataSet.Create(
                    count,
                    ParticleDataSet.CalibrationSeed,
                    Allocator.TempJob);

                for (int index = 0; index < count; index++)
                    AssertRecordsExactlyEqual(first[index], second[index], index);
            }
            finally
            {
                if (second.IsCreated)
                    second.Dispose();
                if (first.IsCreated)
                    first.Dispose();
            }
        }

        [Test]
        public void Create_WithDifferentSeed_ChangesGeneratedData()
        {
            NativeArray<ParticleRecord> calibration = default;
            NativeArray<ParticleRecord> holdout = default;

            try
            {
                calibration = ParticleDataSet.Create(
                    32,
                    ParticleDataSet.CalibrationSeed,
                    Allocator.TempJob);
                holdout = ParticleDataSet.Create(
                    32,
                    ParticleDataSet.HoldoutSeed,
                    Allocator.TempJob);

                bool foundDifference = false;
                for (int index = 0; index < calibration.Length; index++)
                {
                    ParticleRecord left = calibration[index];
                    ParticleRecord right = holdout[index];
                    if (!math.all(left.Position == right.Position) ||
                        !math.all(left.Velocity == right.Velocity) ||
                        left.Lifetime != right.Lifetime ||
                        left.Category != right.Category)
                    {
                        foundDifference = true;
                        break;
                    }
                }

                Assert.That(foundDifference, Is.True,
                    "Calibration and holdout seeds produced the same records.");
            }
            finally
            {
                if (holdout.IsCreated)
                    holdout.Dispose();
                if (calibration.IsCreated)
                    calibration.Dispose();
            }
        }

        private static void AssertRecordsExactlyEqual(
            ParticleRecord left,
            ParticleRecord right,
            int index)
        {
            Assert.That(math.all(left.Position == right.Position), Is.True,
                $"Position differs at index {index}.");
            Assert.That(math.all(left.Velocity == right.Velocity), Is.True,
                $"Velocity differs at index {index}.");
            Assert.That(math.all(left.Rotation.value == right.Rotation.value), Is.True,
                $"Rotation differs at index {index}.");
            Assert.That(left.Lifetime, Is.EqualTo(right.Lifetime),
                $"Lifetime differs at index {index}.");
            Assert.That(left.Category, Is.EqualTo(right.Category),
                $"Category differs at index {index}.");
        }
    }
}
