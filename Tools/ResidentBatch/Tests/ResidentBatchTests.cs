using System;
using NUnit.Framework;
using Yanagisawa.DataLayoutCalibrator.Batch;
using Yanagisawa.DataLayoutCalibrator.Examples.ResidentBatch;

namespace Yanagisawa.DataLayoutCalibrator.Tests
{
    [TestFixture(PointStorageLayout.AoS)]
    [TestFixture(PointStorageLayout.SoA)]
    public sealed class ResidentBatchTests
    {
        private readonly PointStorageLayout layout;
        public ResidentBatchTests(PointStorageLayout layout) { this.layout = layout; }
        private static BatchPoint3 P(double x, double y, double z) => new BatchPoint3(x, y, z);
        private static void EqualPoint(BatchPoint3 actual, BatchPoint3 expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X));
            Assert.That(actual.Y, Is.EqualTo(expected.Y));
            Assert.That(actual.Z, Is.EqualTo(expected.Z));
        }
        private static BatchPoint3[] Snapshot(ResidentPointBatch batch)
        {
            var result = new BatchPoint3[batch.Count]; batch.CopyTo(result); return result;
        }

        [TestCase(0)] [TestCase(1)] [TestCase(3)] [TestCase(7)] [TestCase(17)] [TestCase(257)]
        public void CompleteOutputMatchesIndependentDecimalAffineOracle(int count)
        {
            var input = new BatchPoint3[count];
            for (int i = 0; i < count; i++) input[i] = P(i * 0.25 - 3, (i % 5) - 2, (i % 7) * 0.5);
            var transform = new AffineTransform3(P(0.5, -2, 1), P(1, 0.25, -0.5), P(-1, 2, 0.25), P(3, -4, 0.5));
            using (var batch = new ResidentPointBatch(count + 2, layout))
            {
                batch.Load(input, 0, count); batch.Transform(transform);
                var actual = Snapshot(batch);
                BatchPoint3 min = default, max = default;
                for (int i = 0; i < count; i++)
                {
                    decimal x = (decimal)input[i].X, y = (decimal)input[i].Y, z = (decimal)input[i].Z;
                    var expected = P((double)(0.5m*x - 2*y + z + 3),
                        (double)(x + 0.25m*y - 0.5m*z - 4), (double)(-x + 2*y + 0.25m*z + 0.5m));
                    EqualPoint(actual[i], expected);
                    if (i == 0) { min = expected; max = expected; }
                    else
                    {
                        min = P(Math.Min(min.X, expected.X), Math.Min(min.Y, expected.Y), Math.Min(min.Z, expected.Z));
                        max = P(Math.Max(max.X, expected.X), Math.Max(max.Y, expected.Y), Math.Max(max.Z, expected.Z));
                    }
                }
                var bounds = batch.ReduceBounds();
                Assert.That(bounds.HasValue, Is.EqualTo(count > 0));
                if (count > 0) { EqualPoint(bounds.Min, min); EqualPoint(bounds.Max, max); }
                Assert.That(batch.Capacity, Is.EqualTo(count + 2));
            }
        }

        [Test] public void EmptyZeroCapacitySupportsAllStagesAndExplicitReserve()
        {
            using (var batch = new ResidentPointBatch(0, layout))
            {
                batch.Load(Array.Empty<BatchPoint3>(), 0, 0); batch.Transform(AffineTransform3.Identity);
                Assert.That(batch.CopyTo(Array.Empty<BatchPoint3>()), Is.Zero);
                Assert.That(batch.ReduceBounds().HasValue, Is.False);
                Assert.That(batch.OwnedCoordinatePayloadBytes, Is.Zero);
                batch.Reserve(2); batch.Load(new[] { P(1, 2, 3) }, 0, 1);
                EqualPoint(Snapshot(batch)[0], P(1, 2, 3));
            }
        }

        [Test] public void IngressAndExportCopyWithoutExposingBackingStorage()
        {
            var input = new[] { P(99, 99, 99), P(1, 2, 3), P(-2, 5, 4), P(99, 99, 99) };
            using (var batch = new ResidentPointBatch(5, layout))
            {
                batch.Load(input, 1, 2); input[1] = P(900, 900, 900);
                var output = new[] { P(88, 88, 88), default(BatchPoint3), default(BatchPoint3), P(88, 88, 88) };
                Assert.That(batch.CopyTo(output, 1), Is.EqualTo(2));
                EqualPoint(output[0], P(88, 88, 88)); EqualPoint(output[3], P(88, 88, 88));
                EqualPoint(output[1], P(1, 2, 3)); EqualPoint(output[2], P(-2, 5, 4));
                output[1] = default;
                EqualPoint(Snapshot(batch)[0], P(1, 2, 3));
            }
        }

        [Test] public void ShorterReloadAndEmptyReloadNeverExposeOldTail()
        {
            using (var batch = new ResidentPointBatch(4, layout))
            {
                batch.Load(new[] { P(9, 9, 9), P(8, 8, 8), P(7, 7, 7) }, 0, 3);
                batch.Load(new[] { P(1, 2, 3) }, 0, 1);
                var output = new[] { default(BatchPoint3), P(99, 99, 99) };
                batch.CopyTo(output); EqualPoint(output[1], P(99, 99, 99));
                EqualPoint(batch.ReduceBounds().Max, P(1, 2, 3));
                batch.Load(Array.Empty<BatchPoint3>(), 0, 0);
                Assert.That(batch.Count, Is.Zero); Assert.That(batch.Capacity, Is.EqualTo(4));
                Assert.That(batch.ReduceBounds().HasValue, Is.False);
            }
        }

        [Test] public void ReservePreservesCurrentDataAndNeverShrinksOrChangesVersion()
        {
            using (var batch = new ResidentPointBatch(2, layout))
            {
                batch.Load(new[] { P(1, 2, 3), P(-1, 0, 9) }, 0, 2);
                batch.Transform(AffineTransform3.Translate(2, 0, -1));
                long version = batch.Version;
                batch.Reserve(7); batch.Reserve(1); batch.Reserve(7);
                Assert.That(batch.Capacity, Is.EqualTo(7)); Assert.That(batch.Count, Is.EqualTo(2));
                Assert.That(batch.Version, Is.EqualTo(version));
                Assert.That(batch.OwnedCoordinatePayloadBytes, Is.EqualTo(48L * 7));
                EqualPoint(Snapshot(batch)[0], P(3, 2, 2)); EqualPoint(Snapshot(batch)[1], P(1, 0, 8));
                batch.Transform(AffineTransform3.Translate(1, 1, 1));
                EqualPoint(Snapshot(batch)[1], P(2, 1, 9));
            }
        }

        [Test] public void InvalidRangesAndInsufficientCapacityPreserveData()
        {
            using (var batch = new ResidentPointBatch(1, layout))
            {
                var input = new[] { P(1, 2, 3), P(4, 5, 6) }; batch.Load(input, 0, 1);
                long version = batch.Version;
                Assert.Throws<ArgumentNullException>(() => batch.Load(null, 0, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => batch.Load(input, -1, 1));
                Assert.Throws<ArgumentOutOfRangeException>(() => batch.Load(input, 0, -1));
                Assert.Throws<ArgumentOutOfRangeException>(() => batch.Load(input, int.MaxValue, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => batch.Load(input, 0, int.MaxValue));
                Assert.Throws<ArgumentException>(() => batch.Load(input, 0, 2));
                Assert.Throws<ArgumentOutOfRangeException>(() => batch.Reserve(-1));
                Assert.Throws<ArgumentNullException>(() => batch.CopyTo(null));
                Assert.Throws<ArgumentOutOfRangeException>(() => batch.CopyTo(Array.Empty<BatchPoint3>()));
                Assert.Throws<ArgumentOutOfRangeException>(() => batch.CopyTo(input, int.MaxValue));
                Assert.That(batch.Version, Is.EqualTo(version)); EqualPoint(Snapshot(batch)[0], P(1, 2, 3));
            }
        }

        [Test] public void NonfiniteIngressRollsBackEvenAfterScratchWasPartlyWritten()
        {
            using (var batch = new ResidentPointBatch(2, layout))
            {
                batch.Load(new[] { P(1, 2, 3), P(4, 5, 6) }, 0, 2); long version = batch.Version;
                foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    Assert.Throws<ArgumentException>(() => batch.Load(new[] { P(99, 99, 99), P(0, bad, 0) }, 0, 2));
                    Assert.That(batch.Version, Is.EqualTo(version));
                    EqualPoint(Snapshot(batch)[0], P(1, 2, 3)); EqualPoint(Snapshot(batch)[1], P(4, 5, 6));
                }
                batch.Transform(AffineTransform3.Identity);
                EqualPoint(Snapshot(batch)[1], P(4, 5, 6));
            }
        }

        [Test] public void OverflowRejectsWholeTransformAndScratchCanBeReused()
        {
            using (var batch = new ResidentPointBatch(2, layout))
            {
                batch.Load(new[] { P(1, 2, 3), P(double.MaxValue, 0, 0) }, 0, 2); long version = batch.Version;
                Assert.Throws<ArithmeticException>(() => batch.Transform(AffineTransform3.Scale(2, 1, 1)));
                Assert.That(batch.Version, Is.EqualTo(version));
                EqualPoint(Snapshot(batch)[0], P(1, 2, 3)); EqualPoint(Snapshot(batch)[1], P(double.MaxValue, 0, 0));
                batch.Transform(AffineTransform3.Identity);
                EqualPoint(Snapshot(batch)[1], P(double.MaxValue, 0, 0));
            }
        }

        [Test] public void RepeatedStepsOperateOnResidentRatherThanOriginalCoordinates()
        {
            using (var batch = new ResidentPointBatch(3, layout))
            {
                batch.Load(new[] { P(1, 2, 3) }, 0, 1);
                for (int i = 0; i < 50; i++) batch.Transform(AffineTransform3.Translate(0.5, -1, 2));
                EqualPoint(Snapshot(batch)[0], P(26, -48, 103));
                Assert.That(batch.Capacity, Is.EqualTo(3));
            }
        }

        [Test] public void DisposedStorageRejectsUseAndDisposeIsIdempotent()
        {
            var batch = new ResidentPointBatch(1, layout); batch.Dispose(); batch.Dispose();
            Assert.That(batch.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => batch.Reserve(1));
            Assert.Throws<ObjectDisposedException>(() => batch.Load(Array.Empty<BatchPoint3>(), 0, 0));
            Assert.Throws<ObjectDisposedException>(() => batch.Transform(AffineTransform3.Identity));
            Assert.Throws<ObjectDisposedException>(() => batch.CopyTo(Array.Empty<BatchPoint3>()));
            Assert.Throws<ObjectDisposedException>(() => batch.ReduceBounds());
            Assert.Throws<ObjectDisposedException>(() => { var value = batch.Count; });
            Assert.Throws<ObjectDisposedException>(() => { var value = batch.Capacity; });
            Assert.Throws<ObjectDisposedException>(() => { var value = batch.Version; });
            Assert.Throws<ObjectDisposedException>(() => { var value = batch.OwnedCoordinatePayloadBytes; });
        }

        [Test] public void PeriodicAndFinalRequestsExportEachVersionOnlyOnce()
        {
            using (var pipeline = new ResidentTransformPipeline(2, new ResidentBatchPlan(layout, 2)))
            {
                pipeline.Load(new[] { P(1, 2, 3) }, 0, 1);
                var output = new[] { P(99, 99, 99) };
                Assert.That(pipeline.ExportIfDue(output), Is.False);
                pipeline.Step(AffineTransform3.Translate(1, 0, 0));
                Assert.That(pipeline.ExportIfDue(null), Is.False);
                EqualPoint(output[0], P(99, 99, 99));
                pipeline.Step(AffineTransform3.Translate(1, 0, 0));
                Assert.That(pipeline.ExportIfDue(output), Is.True); EqualPoint(output[0], P(3, 2, 3));
                Assert.That(pipeline.ExportIfDue(null, finalRequest: true), Is.False);
                pipeline.Step(AffineTransform3.Translate(1, 0, 0));
                Assert.That(pipeline.ExportIfDue(output), Is.False);
                Assert.That(pipeline.ExportIfDue(output, finalRequest: true), Is.True);
                EqualPoint(output[0], P(4, 2, 3)); Assert.That(pipeline.CompletedSteps, Is.EqualTo(3));
            }
        }

        [Test] public void FailedExportDoesNotConsumeOpportunityOrModifyDestination()
        {
            using (var pipeline = new ResidentTransformPipeline(2, new ResidentBatchPlan(layout, 1)))
            {
                pipeline.Load(new[] { P(1, 2, 3), P(4, 5, 6) }, 0, 2); pipeline.Step(AffineTransform3.Identity);
                var tooSmall = new[] { P(99, 99, 99) };
                Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.ExportIfDue(tooSmall));
                EqualPoint(tooSmall[0], P(99, 99, 99));
                var output = new BatchPoint3[2]; Assert.That(pipeline.ExportIfDue(output), Is.True);
                EqualPoint(output[1], P(4, 5, 6));
            }
        }

        [Test] public void FailedStepAndFailedReloadDoNotAdvanceCadence()
        {
            using (var pipeline = new ResidentTransformPipeline(2, new ResidentBatchPlan(layout, 1)))
            {
                pipeline.Load(new[] { P(1, 2, 3), P(double.MaxValue, 0, 0) }, 0, 2);
                pipeline.Step(AffineTransform3.Identity); var output = new BatchPoint3[2];
                Assert.That(pipeline.ExportIfDue(output), Is.True);
                Assert.Throws<ArithmeticException>(() => pipeline.Step(AffineTransform3.Scale(2, 1, 1)));
                Assert.Throws<ArgumentException>(() => pipeline.Load(new[] { P(double.NaN, 0, 0) }, 0, 1));
                Assert.That(pipeline.CompletedSteps, Is.EqualTo(1)); Assert.That(pipeline.Count, Is.EqualTo(2));
                Assert.That(pipeline.ExportIfDue(output, finalRequest: true), Is.False);
                EqualPoint(pipeline.ReduceBounds().Max, P(double.MaxValue, 2, 3));
            }
        }

        [Test] public void SuccessfulReloadResetsCadenceAndAllowsAnotherFinalSnapshot()
        {
            using (var pipeline = new ResidentTransformPipeline(2, new ResidentBatchPlan(layout)))
            {
                var output = new BatchPoint3[2];
                pipeline.Load(new[] { P(1, 2, 3) }, 0, 1);
                Assert.That(pipeline.ExportIfDue(output, finalRequest: true), Is.True);
                pipeline.Step(AffineTransform3.Identity);
                pipeline.Load(new[] { P(4, 5, 6) }, 0, 1);
                Assert.That(pipeline.CompletedSteps, Is.Zero);
                pipeline.Reserve(4);
                Assert.That(pipeline.ExportIfDue(output, finalRequest: true), Is.True);
                EqualPoint(output[0], P(4, 5, 6)); Assert.That(pipeline.Capacity, Is.EqualTo(4));
            }
        }

        [Test] public void EmptyFinalExportAndDisabledExportsAreExplicit()
        {
            using (var pipeline = new ResidentTransformPipeline(0, new ResidentBatchPlan(layout)))
            {
                Assert.That(pipeline.ReduceBounds().HasValue, Is.False);
                Assert.That(pipeline.ExportIfDue(Array.Empty<BatchPoint3>(), finalRequest: true), Is.True);
                Assert.That(pipeline.ExportIfDue(null, finalRequest: true), Is.False);
            }
            using (var pipeline = new ResidentTransformPipeline(0, new ResidentBatchPlan(layout, 0, false)))
                Assert.That(pipeline.ExportIfDue(null, finalRequest: true), Is.False);
        }

        [Test] public void MissedPeriodicEventsAreNotReplayed()
        {
            using (var pipeline = new ResidentTransformPipeline(1, new ResidentBatchPlan(layout, 2)))
            {
                pipeline.Load(new[] { P(0, 0, 0) }, 0, 1);
                for (int i = 0; i < 3; i++) pipeline.Step(AffineTransform3.Translate(1, 0, 0));
                var output = new BatchPoint3[1]; Assert.That(pipeline.ExportIfDue(output), Is.False);
                Assert.That(pipeline.ExportIfDue(output, finalRequest: true), Is.True); EqualPoint(output[0], P(3, 0, 0));
            }
        }

        [Test] public void DisposedPipelineRejectsExecutionAndNoOpExport()
        {
            var pipeline = new ResidentTransformPipeline(0, new ResidentBatchPlan(layout)); pipeline.Dispose(); pipeline.Dispose();
            Assert.That(pipeline.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => pipeline.Step(AffineTransform3.Identity));
            Assert.Throws<ObjectDisposedException>(() => pipeline.Load(Array.Empty<BatchPoint3>(), 0, 0));
            Assert.Throws<ObjectDisposedException>(() => pipeline.ExportIfDue(null));
            Assert.Throws<ObjectDisposedException>(() => pipeline.ReduceBounds());
            Assert.Throws<ObjectDisposedException>(() => pipeline.Reserve(0));
            Assert.Throws<ObjectDisposedException>(() => { var value = pipeline.CompletedSteps; });
        }

        [Test] public void SceneCallerHasCompleteExpectedOutputAndCadence()
        {
            var receipt = Callers.SceneAnchors(layout);
            var expected = new[] { P(1, -6, 5.5), P(4, -4, -1.5), P(3, -7, 3.5), P(9, -1, 1.5), P(-1, -3, 2.5) };
            Assert.That(receipt.FinalPoints.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++) EqualPoint(receipt.FinalPoints[i], expected[i]);
            Assert.That(receipt.ExportCount, Is.EqualTo(2)); Assert.That(receipt.ConsumedPointCount, Is.EqualTo(10));
            Assert.That(receipt.CompletedSteps, Is.EqualTo(3)); Assert.That(receipt.Capacity, Is.EqualTo(8));
            EqualPoint(receipt.Bounds.Min, P(-1, -7, -1.5)); EqualPoint(receipt.Bounds.Max, P(9, -1, 5.5));
            Assert.That(receipt.ConsumerChecksum, Is.EqualTo(24.5));
        }

        [Test] public void PointCloudCallerReusesCapacityAcrossFramesAndConsumesOnlyLogicalCount()
        {
            var receipt = Callers.PointCloudFrames(layout); var expected = new[] { P(-1, 15, -1), P(-3, 8, 3), P(1, 11, 1) };
            Assert.That(receipt.FinalPoints.Length, Is.EqualTo(3));
            for (int i = 0; i < 3; i++) EqualPoint(receipt.FinalPoints[i], expected[i]);
            Assert.That(receipt.ExportCount, Is.EqualTo(2)); Assert.That(receipt.ConsumedPointCount, Is.EqualTo(8));
            Assert.That(receipt.CompletedSteps, Is.EqualTo(2)); Assert.That(receipt.Capacity, Is.EqualTo(7));
            EqualPoint(receipt.Bounds.Min, P(-3, 8, -1)); EqualPoint(receipt.Bounds.Max, P(1, 15, 3));
            Assert.That(receipt.ConsumerChecksum, Is.EqualTo(173));
        }
    }

    public sealed class ResidentBatchConfigurationTests
    {
        [Test] public void InvalidConfigurationIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResidentPointBatch(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResidentPointBatch(0, (PointStorageLayout)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResidentBatchPlan((PointStorageLayout)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResidentBatchPlan(exportEverySteps: -1));
            Assert.Throws<ArgumentNullException>(() => new ResidentTransformPipeline(0, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResidentBatchPlan().ShouldExport(-1));
        }

        [Test] public void AffineRejectsNonfiniteCoefficientsAndDefaultMapsToOrigin()
        {
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var point = new BatchPoint3(0, bad, 0);
                Assert.Throws<ArgumentException>(() => new AffineTransform3(point, default, default, default));
                Assert.Throws<ArgumentException>(() => new AffineTransform3(default, point, default, default));
                Assert.Throws<ArgumentException>(() => new AffineTransform3(default, default, point, default));
                Assert.Throws<ArgumentException>(() => new AffineTransform3(default, default, default, point));
            }
            var result = default(AffineTransform3).Apply(new BatchPoint3(1, 2, 3));
            Assert.That(result.X, Is.Zero); Assert.That(result.Y, Is.Zero); Assert.That(result.Z, Is.Zero);
        }

        [Test] public void PlanExposesNoMutableProperties()
        {
            foreach (var property in typeof(ResidentBatchPlan).GetProperties()) Assert.That(property.SetMethod, Is.Null);
        }

        [Test] public void ExampleEntrypointRunsWithoutMeasurements()
        {
            Assert.That(Program.Main(), Is.Zero);
        }
    }
}
