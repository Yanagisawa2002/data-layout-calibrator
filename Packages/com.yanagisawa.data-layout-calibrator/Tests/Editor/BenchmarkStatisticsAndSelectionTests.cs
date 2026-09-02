using NUnit.Framework;
using UnityEngine;

namespace Yanagisawa.DataLayoutCalibrator.Tests
{
    public sealed class BenchmarkStatisticsAndSelectionTests
    {
        [Test]
        public void CoreAssembly_ContainsNoParticleWorkloadTypes()
        {
            System.Type[] coreTypes = typeof(ICalibrationScenario).Assembly.GetTypes();
            for (int index = 0; index < coreTypes.Length; index++)
            {
                Assert.That(
                    coreTypes[index].FullName,
                    Does.Not.Contain("Particle"),
                    $"Particle workload type leaked into the core assembly: {coreTypes[index].FullName}");
            }
        }

        [Test]
        public void CandidateDescriptor_AllowsPluginDefinedLayoutWithoutChangingCoreEnum()
        {
            var descriptor = new CandidateDescriptor(
                "HotCold16",
                logicalBatchSize: 96,
                isBaseline: false,
                sortOrder: 40,
                displayName: "Hot/Cold 16");

            Assert.That(descriptor.CandidateId, Is.EqualTo("HotCold16-b96"));
            Assert.That(descriptor.LayoutId, Is.EqualTo("HotCold16"));
            Assert.That(descriptor.IsBaseline, Is.False);
        }

        [Test]
        public void Calculate_ProducesExpectedPercentilesAndMad()
        {
            double[] samples = { 1d, 2d, 3d, 4d, 100d };
            double[] scratch = new double[samples.Length];

            LatencySummary summary = BenchmarkStatistics.Calculate(samples, samples.Length, scratch);

            Assert.That(summary.SampleCount, Is.EqualTo(5));
            Assert.That(summary.MinimumMilliseconds, Is.EqualTo(1d));
            Assert.That(summary.MedianMilliseconds, Is.EqualTo(3d));
            Assert.That(summary.P95Milliseconds, Is.EqualTo(80.8d).Within(1e-9));
            Assert.That(summary.P99Milliseconds, Is.EqualTo(96.16d).Within(1e-9));
            Assert.That(summary.MaximumMilliseconds, Is.EqualTo(100d));
            Assert.That(summary.MedianAbsoluteDeviationMilliseconds, Is.EqualTo(1d));
        }

        [Test]
        public void AmortizedP95_IncludesFullBoundaryCostOverDeclaredLifetime()
        {
            double[] resident = { 2d, 2d, 2d, 2d, 2d };
            double[] ingress = { 60d, 60d, 60d, 60d, 60d };
            double[] export = { 30d, 30d, 30d, 30d, 30d };

            double result = BenchmarkStatistics.CalculateAmortizedP95MillisecondsPerTick(
                resident,
                ingress,
                export,
                lifetimeTicks: 600);

            Assert.That(result, Is.EqualTo(2.15d).Within(1e-12));
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
                results.Length,
                bootstrapIterations: 500);

            Assert.That(decision.Status, Is.EqualTo(LayoutSelectionStatus.Optimized));
            Assert.That(decision.BaselineCandidate, Is.EqualTo(new CandidateDescriptor(LayoutKind.AoS, 64)));
            Assert.That(decision.SelectedCandidate, Is.EqualTo(new CandidateDescriptor(LayoutKind.SoA, 64)));
            Assert.That(decision.RejectedParityCandidateCount, Is.EqualTo(1));
            Assert.That(decision.ImprovementConfidenceInterval.LowerBoundPercent, Is.GreaterThan(0d));
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
                minimumImprovementPercent: 10d,
                bootstrapIterations: 500);

            Assert.That(decision.Status, Is.EqualTo(LayoutSelectionStatus.Inconclusive));
            Assert.That(decision.SelectedCandidate, Is.EqualTo(new CandidateDescriptor(LayoutKind.AoS, 64)));
            Assert.That(decision.BestMeasuredCandidate, Is.EqualTo(new CandidateDescriptor(LayoutKind.SoA, 64)));
        }

        [Test]
        public void SelectCalibration_WhenBootstrapIncludesZero_ReportsTieAndFallsBackToAoS()
        {
            LayoutBenchmarkResult baseline = CreateResult(LayoutKind.AoS, 64, 10d);
            LayoutBenchmarkResult noisyCandidate = CreateResult(LayoutKind.SoA, 64, 8d);
            noisyCandidate.ResidentSamplesMillisecondsPerTick[19] = 20d;
            Recalculate(noisyCandidate);

            LayoutSelectionDecision decision = LayoutSelector.SelectCalibration(
                new[] { baseline, noisyCandidate },
                2,
                minimumImprovementPercent: 10d,
                bootstrapIterations: 2000,
                bootstrapSeed: 0x12345678u);

            Assert.That(decision.Status, Is.EqualTo(LayoutSelectionStatus.StatisticalTie));
            Assert.That(decision.SelectedCandidate.LayoutId, Is.EqualTo("AoS"));
            Assert.That(decision.FellBackBecauseStatisticalTie, Is.True);
            Assert.That(decision.ImprovementConfidenceInterval.LowerBoundPercent, Is.LessThanOrEqualTo(0d));
        }

        [Test]
        public void ConfirmHoldout_WhenIndependentThresholdAndBootstrapClear_RemainsOptimized()
        {
            LayoutSelectionDecision calibration = CreateOptimizedCalibrationDecision(20d);
            LayoutBenchmarkResult baselineHoldout = CreateResult(LayoutKind.AoS, 64, 20d, elementCount: 1000);
            LayoutBenchmarkResult selectedHoldout = CreateResult(LayoutKind.SoA, 64, 16.4d, elementCount: 1000);
            baselineHoldout.Phase = BenchmarkPhase.Holdout;
            selectedHoldout.Phase = BenchmarkPhase.Holdout;

            LayoutSelectionDecision decision = LayoutSelector.ConfirmHoldout(
                calibration,
                baselineHoldout,
                selectedHoldout,
                minimumImprovementPercent: 10d,
                bootstrapIterations: 500);

            Assert.That(decision.Status, Is.EqualTo(LayoutSelectionStatus.Optimized));
            Assert.That(decision.ImprovementPercent, Is.EqualTo(18d).Within(1e-9));
            Assert.That(decision.SelectedCandidate.LayoutId, Is.EqualTo("SoA"));
        }

        [Test]
        public void ConfirmHoldout_WhenIndependentThresholdFails_FallsBackToAoS()
        {
            LayoutSelectionDecision calibration = CreateOptimizedCalibrationDecision(20d);
            LayoutBenchmarkResult baselineHoldout = CreateResult(LayoutKind.AoS, 64, 20d, elementCount: 1000);
            LayoutBenchmarkResult selectedHoldout = CreateResult(LayoutKind.SoA, 64, 18.2d, elementCount: 1000);
            baselineHoldout.Phase = BenchmarkPhase.Holdout;
            selectedHoldout.Phase = BenchmarkPhase.Holdout;

            LayoutSelectionDecision decision = LayoutSelector.ConfirmHoldout(
                calibration,
                baselineHoldout,
                selectedHoldout,
                minimumImprovementPercent: 10d,
                bootstrapIterations: 500);

            Assert.That(decision.ImprovementPercent, Is.EqualTo(9d).Within(1e-9));
            Assert.That(decision.Status, Is.EqualTo(LayoutSelectionStatus.Inconclusive));
            Assert.That(decision.SelectedCandidate.LayoutId, Is.EqualTo("AoS"));
        }

        [Test]
        public void SuiteDtos_RoundTripThroughJsonUtility()
        {
            var suite = new CalibrationSuiteProfile
            {
                CreatedUtcIso8601 = "2026-09-01T00:00:00Z",
                Scenarios = new[]
                {
                    new ScenarioCalibrationProfile
                    {
                        Scenario = new ScenarioDescriptor("particle-integrate-v2", "Particle Integrate", 2, "Integrate"),
                        ElementCount = 1000,
                        FinalDecision = CreateOptimizedCalibrationDecision(20d),
                        CalibrationResults = new[] { CreateResult(LayoutKind.AoS, 64, 10d) },
                    },
                },
            };

            string json = JsonUtility.ToJson(suite);
            CalibrationSuiteProfile restored = JsonUtility.FromJson<CalibrationSuiteProfile>(json);

            Assert.That(restored.SchemaVersion, Is.EqualTo(3));
            Assert.That(restored.ProductName, Is.EqualTo("Data Layout Calibrator"));
            Assert.That(restored.Scenarios[0].Scenario.ScenarioId, Is.EqualTo("particle-integrate-v2"));
            Assert.That(restored.Scenarios[0].FinalDecision.SelectedCandidate.LayoutId, Is.EqualTo("SoA"));
            Assert.That(restored.Scenarios[0].CalibrationResults[0].AmortizedLatency.P95Milliseconds, Is.EqualTo(10d));
        }

        [Test]
        public void Schema2Json_UpgradesWithoutChangingCandidateIdentity()
        {
            const string json =
                "{\"SchemaVersion\":2,\"RunId\":\"legacy\",\"Scenarios\":[{" +
                "\"SchemaVersion\":2,\"Scenario\":{\"ScenarioId\":\"legacy-scenario\",\"ContractVersion\":1},\"CalibrationResults\":[{" +
                "\"Candidate\":{\"CandidateId\":\"AoS-b64\",\"LayoutId\":\"AoS\",\"LogicalBatchSize\":64,\"IsBaseline\":true}," +
                "\"ResidentSamplesMillisecondsPerTick\":[1.0,1.1,1.2]," +
                "\"IngressSamplesMilliseconds\":[0.1,0.1,0.1]," +
                "\"ExportSamplesMilliseconds\":[0.2,0.2,0.2]}]}]}";
            CalibrationSuiteProfile suite = JsonUtility.FromJson<CalibrationSuiteProfile>(json);

            CalibrationProfileMigration.UpgradeInMemory(suite);

            Assert.That(suite.SchemaVersion, Is.EqualTo(3));
            Assert.That(suite.Scenarios[0].SchemaVersion, Is.EqualTo(3));
            LayoutBenchmarkResult result = suite.Scenarios[0].CalibrationResults[0];
            Assert.That(result.Candidate.CandidateId, Is.EqualTo("AoS-b64"));
            Assert.That(result.Candidate.Layout.PolicyId, Is.EqualTo("AoS"));
            Assert.That(result.Candidate.Execution.PolicyId, Is.EqualTo("FrameFaithful"));
            Assert.That(result.ScenarioId, Is.EqualTo("legacy-scenario"));
            Assert.That(result.ScenarioContractVersion, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, result.ResidentBlockIds);
        }

        private static LayoutSelectionDecision CreateOptimizedCalibrationDecision(double improvementPercent)
        {
            return new LayoutSelectionDecision
            {
                Status = LayoutSelectionStatus.Optimized,
                BaselineCandidate = new CandidateDescriptor(LayoutKind.AoS, 64),
                SelectedCandidate = new CandidateDescriptor(LayoutKind.SoA, 64),
                BestMeasuredCandidate = new CandidateDescriptor(LayoutKind.SoA, 64),
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
            var resident = new double[20];
            var ingress = new double[10];
            var export = new double[10];
            for (int index = 0; index < resident.Length; index++)
                resident[index] = p95Milliseconds;

            var result = new LayoutBenchmarkResult
            {
                ScenarioId = "synthetic-selection-fixture",
                ScenarioContractVersion = 1,
                Candidate = new CandidateDescriptor(layout, batchSize),
                ElementCount = elementCount,
                StepsPerSample = 1,
                Completed = true,
                ParityPassed = parityPassed,
                BoundaryCost = new BoundaryCostSummary { LifetimeTicks = 600 },
                ResidentSamplesMillisecondsPerTick = resident,
                IngressSamplesMilliseconds = ingress,
                ExportSamplesMilliseconds = export,
            };
            Recalculate(result);
            return result;
        }

        private static void Recalculate(LayoutBenchmarkResult result)
        {
            var scratch = new double[result.ResidentSamplesMillisecondsPerTick.Length];
            result.Latency = BenchmarkStatistics.Calculate(
                result.ResidentSamplesMillisecondsPerTick,
                result.ResidentSamplesMillisecondsPerTick.Length,
                scratch);
            result.AmortizedSamplesMillisecondsPerTick =
                new double[result.ResidentSamplesMillisecondsPerTick.Length];
            result.AmortizedLatency = BenchmarkStatistics.CalculateAmortizedLatency(
                result.ResidentSamplesMillisecondsPerTick,
                result.IngressSamplesMilliseconds,
                result.ExportSamplesMilliseconds,
                result.BoundaryCost.LifetimeTicks,
                result.AmortizedSamplesMillisecondsPerTick,
                scratch);
        }
    }
}
