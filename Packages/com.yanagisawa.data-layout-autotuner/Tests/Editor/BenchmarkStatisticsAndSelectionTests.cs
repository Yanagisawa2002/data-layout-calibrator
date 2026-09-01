using NUnit.Framework;
using UnityEngine;

namespace Yanagisawa.DataLayoutAutotuner.Tests
{
    public sealed class BenchmarkStatisticsAndSelectionTests
    {
        [Test]
        public void Calculate_ProducesExpectedPercentilesAndMad()
        {
            double[] samples = { 1d, 2d, 3d, 4d, 100d };
            double[] scratch = new double[samples.Length];

            LatencySummary summary = BenchmarkStatistics.Calculate(
                samples,
                samples.Length,
                scratch);

            Assert.That(summary.SampleCount, Is.EqualTo(5));
            Assert.That(summary.MinimumMilliseconds, Is.EqualTo(1d));
            Assert.That(summary.MedianMilliseconds, Is.EqualTo(3d));
            Assert.That(summary.P95Milliseconds, Is.EqualTo(80.8d).Within(1e-9));
            Assert.That(summary.P99Milliseconds, Is.EqualTo(96.16d).Within(1e-9));
            Assert.That(summary.MaximumMilliseconds, Is.EqualTo(100d));
            Assert.That(summary.MedianAbsoluteDeviationMilliseconds, Is.EqualTo(1d));
        }

        [Test]
        public void SelectCalibration_UsesBestAoSAndRejectsParityFailure()
        {
            LayoutBenchmarkResult[] results =
            {
                CreateResult(LayoutKind.AoS, 32, 10d),
                CreateResult(LayoutKind.AoS, 64, 9d),
                CreateResult(LayoutKind.SoA, 64, 8d),
                CreateResult(LayoutKind.AoSoA8, 64, 1d, parityPassed: false),
            };

            LayoutSelectionDecision decision = LayoutSelector.SelectCalibration(
                results,
                results.Length);

            Assert.That(decision.Status, Is.EqualTo(LayoutSelectionStatus.Optimized));
            Assert.That(decision.BaselineCandidate, Is.EqualTo(new LayoutCandidate(LayoutKind.AoS, 64)));
            Assert.That(decision.SelectedCandidate, Is.EqualTo(new LayoutCandidate(LayoutKind.SoA, 64)));
            Assert.That(decision.RejectedParityCandidateCount, Is.EqualTo(1));
        }

        [Test]
        public void SelectCalibration_WhenGainIsBelowThreshold_FallsBackToBestAoS()
        {
            LayoutBenchmarkResult[] results =
            {
                CreateResult(LayoutKind.AoS, 32, 10d),
                CreateResult(LayoutKind.AoS, 64, 9d),
                CreateResult(LayoutKind.SoA, 64, 8.2d),
            };

            LayoutSelectionDecision decision = LayoutSelector.SelectCalibration(
                results,
                results.Length,
                minimumImprovementPercent: 10d);

            Assert.That(decision.Status, Is.EqualTo(LayoutSelectionStatus.Inconclusive));
            Assert.That(decision.SelectedCandidate, Is.EqualTo(new LayoutCandidate(LayoutKind.AoS, 64)));
            Assert.That(decision.BestMeasuredCandidate, Is.EqualTo(new LayoutCandidate(LayoutKind.SoA, 64)));
        }

        [Test]
        public void ConfirmHoldout_WhenIndependentThresholdClears_RemainsOptimized()
        {
            LayoutSelectionDecision calibration = CreateOptimizedCalibrationDecision(20d);
            LayoutBenchmarkResult baselineHoldout = CreateResult(LayoutKind.AoS, 64, 20d, elementCount: 1000);
            LayoutBenchmarkResult selectedHoldout = CreateResult(LayoutKind.SoA, 64, 16.4d, elementCount: 1000);

            LayoutSelectionDecision decision = LayoutSelector.ConfirmHoldout(
                calibration,
                baselineHoldout,
                selectedHoldout,
                minimumImprovementPercent: 10d);

            Assert.That(decision.Status, Is.EqualTo(LayoutSelectionStatus.Optimized));
            Assert.That(decision.ImprovementPercent, Is.EqualTo(18d).Within(1e-9));
            Assert.That(decision.SelectedCandidate.Layout, Is.EqualTo(LayoutKind.SoA));
        }

        [Test]
        public void ConfirmHoldout_WhenIndependentThresholdFails_FallsBackToAoS()
        {
            LayoutSelectionDecision calibration = CreateOptimizedCalibrationDecision(20d);
            LayoutBenchmarkResult baselineHoldout = CreateResult(LayoutKind.AoS, 64, 20d, elementCount: 1000);
            LayoutBenchmarkResult selectedHoldout = CreateResult(LayoutKind.SoA, 64, 18.2d, elementCount: 1000);

            LayoutSelectionDecision decision = LayoutSelector.ConfirmHoldout(
                calibration,
                baselineHoldout,
                selectedHoldout,
                minimumImprovementPercent: 10d);

            Assert.That(decision.ImprovementPercent, Is.EqualTo(9d).Within(1e-9));
            Assert.That(decision.Status, Is.EqualTo(LayoutSelectionStatus.Inconclusive));
            Assert.That(decision.SelectedCandidate.Layout, Is.EqualTo(LayoutKind.AoS));
        }

        [Test]
        public void ProfileDtos_RoundTripThroughJsonUtility()
        {
            var profile = new LayoutTuningProfile
            {
                CreatedUtcIso8601 = "2026-09-01T00:00:00Z",
                WorkloadId = "particle-step-v1",
                ElementCount = 1000,
                FinalDecision = CreateOptimizedCalibrationDecision(20d),
                CalibrationResults = new[] { CreateResult(LayoutKind.AoS, 64, 10d) },
            };

            string json = JsonUtility.ToJson(profile);
            LayoutTuningProfile restored = JsonUtility.FromJson<LayoutTuningProfile>(json);

            Assert.That(restored.SchemaVersion, Is.EqualTo(1));
            Assert.That(restored.WorkloadId, Is.EqualTo("particle-step-v1"));
            Assert.That(restored.ElementCount, Is.EqualTo(1000));
            Assert.That(restored.FinalDecision.SelectedCandidate.Layout, Is.EqualTo(LayoutKind.SoA));
            Assert.That(restored.CalibrationResults.Length, Is.EqualTo(1));
            Assert.That(restored.CalibrationResults[0].Latency.P95Milliseconds, Is.EqualTo(10d));
        }

        private static LayoutSelectionDecision CreateOptimizedCalibrationDecision(double improvementPercent)
        {
            return new LayoutSelectionDecision
            {
                Status = LayoutSelectionStatus.Optimized,
                BaselineCandidate = new LayoutCandidate(LayoutKind.AoS, 64),
                SelectedCandidate = new LayoutCandidate(LayoutKind.SoA, 64),
                BestMeasuredCandidate = new LayoutCandidate(LayoutKind.SoA, 64),
                BaselineP95Milliseconds = 10d,
                BestMeasuredP95Milliseconds = 8d,
                ImprovementPercent = improvementPercent,
                MinimumRequiredImprovementPercent = 10d,
            };
        }

        private static LayoutBenchmarkResult CreateResult(
            LayoutKind layout,
            int batchSize,
            double p95Milliseconds,
            bool parityPassed = true,
            int elementCount = 1000)
        {
            return new LayoutBenchmarkResult
            {
                Candidate = new LayoutCandidate(layout, batchSize),
                ElementCount = elementCount,
                StepsPerSample = 1,
                Completed = true,
                ParityPassed = parityPassed,
                Latency = new LatencySummary
                {
                    SampleCount = 20,
                    MedianMilliseconds = p95Milliseconds * 0.9d,
                    P95Milliseconds = p95Milliseconds,
                },
            };
        }
    }
}
