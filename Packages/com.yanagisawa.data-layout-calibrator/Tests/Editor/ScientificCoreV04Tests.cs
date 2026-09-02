using System;
using NUnit.Framework;

namespace Yanagisawa.DataLayoutCalibrator.Tests
{
    public sealed class ScientificCoreV04Tests
    {
        [Test]
        public void FactorizedDescriptor_PreservesCanonicalIdAndExplicitPolicies()
        {
            var descriptor = new CandidateDescriptor(
                new LayoutPolicy("AoS"),
                new KernelPolicy("ScalarBranchless", KernelControlFlow.Branchless),
                BatchPolicy.JobBatch(64),
                ExecutionPolicy.DependencyChain,
                isBaseline: true,
                candidateId: "particle-aos-branchless-chain-b64");

            descriptor.ValidateFactorConsistency();

            Assert.That(descriptor.CandidateId, Is.EqualTo("particle-aos-branchless-chain-b64"));
            Assert.That(descriptor.EffectiveLayout.PolicyId, Is.EqualTo("AoS"));
            Assert.That(descriptor.EffectiveKernel.ControlFlow, Is.EqualTo(KernelControlFlow.Branchless));
            Assert.That(descriptor.EffectiveBatch.LogicalBatchSize, Is.EqualTo(64));
            Assert.That(descriptor.EffectiveExecution.Topology, Is.EqualTo(ExecutionTopology.DependencyChain));
        }

        [Test]
        public void LegacyDescriptor_NormalizesPoliciesWithoutChangingCanonicalId()
        {
            var legacy = new CandidateDescriptor(
                "PluginLayout",
                96,
                isBaseline: false,
                candidateId: "stable-plugin-candidate");
            legacy.PolicySchemaVersion = 0;
            legacy.Layout = default;
            legacy.Kernel = default;
            legacy.Batch = default;
            legacy.Execution = default;

            CandidateDescriptor normalized = legacy.NormalizePolicies();

            Assert.That(normalized.CandidateId, Is.EqualTo("stable-plugin-candidate"));
            Assert.That(normalized.Layout.PolicyId, Is.EqualTo("PluginLayout"));
            Assert.That(normalized.Kernel.PolicyId, Is.EqualTo("LegacyUnspecified"));
            Assert.That(normalized.Batch.LogicalBatchSize, Is.EqualTo(96));
            Assert.That(normalized.Execution.Topology, Is.EqualTo(ExecutionTopology.FrameFaithful));
        }

        [Test]
        public void CandidateDescriptor_RejectsUnsupportedPolicySchema()
        {
            var descriptor = new CandidateDescriptor(
                "AoS",
                64,
                isBaseline: true,
                candidateId: "baseline");
            descriptor.PolicySchemaVersion = 99;

            Assert.Throws<InvalidOperationException>(() => descriptor.NormalizePolicies());
            Assert.Throws<InvalidOperationException>(descriptor.ValidateFactorConsistency);
        }

        [Test]
        public void CandidateDescriptor_RejectsSurroundingWhitespaceInProtocolIds()
        {
            Assert.Throws<ArgumentException>(() =>
                new CandidateDescriptor(" AoS", 64, isBaseline: true));
            Assert.Throws<ArgumentException>(() =>
                new CandidateDescriptor("AoS", 64, isBaseline: true, candidateId: " baseline"));

            var descriptor = new CandidateDescriptor(
                "AoS",
                64,
                isBaseline: true,
                candidateId: "baseline");
            descriptor.CandidateId = "baseline ";
            Assert.Throws<InvalidOperationException>(descriptor.ValidateFactorConsistency);

            descriptor.CandidateId = "baseline";
            LayoutPolicy layout = descriptor.Layout;
            layout.PolicyId = "AoS ";
            descriptor.Layout = layout;
            Assert.Throws<InvalidOperationException>(descriptor.ValidateFactorConsistency);
        }

        [Test]
        public void TemporalBlock_RequiresExplicitReorderableSemantics()
        {
            Assert.Throws<ArgumentException>(() => ExecutionPolicy.TemporalBlock(4, false));

            ExecutionPolicy policy = ExecutionPolicy.TemporalBlock(4, true);

            Assert.That(policy.Topology, Is.EqualTo(ExecutionTopology.TemporalBlock));
            Assert.That(policy.TemporalBlockTicks, Is.EqualTo(4));
            Assert.That(policy.SemanticsPermitReordering, Is.True);
        }

        [Test]
        public void DeserializedPolicyMetadata_FailsClosedWhenInternallyInconsistent()
        {
            var descriptor = new CandidateDescriptor(
                new LayoutPolicy("AoS"),
                new KernelPolicy("ScalarBranched", KernelControlFlow.Branched),
                BatchPolicy.JobBatch(64),
                ExecutionPolicy.FrameFaithful,
                isBaseline: true);
            ExecutionPolicy malformed = descriptor.Execution;
            malformed.TemporalBlockTicks = 4;
            descriptor.Execution = malformed;

            Assert.Throws<InvalidOperationException>(descriptor.ValidateFactorConsistency);
        }

        [Test]
        public void BalancedLatinOrder_IsSeededDeterministicAndPositionBalanced()
        {
            BlockedMeasurementOrder first = MeasurementOrder.Create(
                4,
                8,
                0x12345678u,
                MeasurementOrderKind.BalancedLatinSquare);
            BlockedMeasurementOrder second = MeasurementOrder.Create(
                4,
                8,
                0x12345678u,
                MeasurementOrderKind.BalancedLatinSquare);

            CollectionAssert.AreEqual(first.CandidateIndices, second.CandidateIndices);
            var positions = new int[4, 4];
            for (int block = 0; block < first.BlockCount; block++)
            {
                var seen = new bool[4];
                for (int position = 0; position < first.CandidateCount; position++)
                {
                    int candidate = first.GetCandidateIndex(block, position);
                    Assert.That(seen[candidate], Is.False, $"Candidate {candidate} repeated in block {block}.");
                    seen[candidate] = true;
                    positions[candidate, position]++;
                }
            }

            for (int candidate = 0; candidate < 4; candidate++)
            for (int position = 0; position < 4; position++)
                Assert.That(positions[candidate, position], Is.EqualTo(2));
        }

        [Test]
        public void TwoCandidateLatinOrder_FormsAbbaAcrossFirstTwoBlocks()
        {
            BlockedMeasurementOrder order = MeasurementOrder.Create(
                2,
                2,
                0xABBAu,
                MeasurementOrderKind.BalancedLatinSquare);

            int a = order.GetCandidateIndex(0, 0);
            int b = order.GetCandidateIndex(0, 1);
            Assert.That(order.GetCandidateIndex(1, 0), Is.EqualTo(b));
            Assert.That(order.GetCandidateIndex(1, 1), Is.EqualTo(a));
        }

        [Test]
        public void PairedBootstrap_PreservesCommonBlockDriftAndUsesLogRatio()
        {
            double[] baselineValues = { 10d, 20d, 30d, 40d, 50d, 60d, 70d, 80d };
            int[] baselineBlocks = { 0, 1, 2, 3, 4, 5, 6, 7 };
            double[] candidateValues = { 64d, 56d, 48d, 40d, 32d, 24d, 16d, 8d };
            int[] candidateBlocks = { 7, 6, 5, 4, 3, 2, 1, 0 };
            LayoutBenchmarkResult baseline = CreateResult(
                "baseline",
                true,
                baselineValues,
                baselineBlocks);
            LayoutBenchmarkResult candidate = CreateResult(
                "candidate",
                false,
                candidateValues,
                candidateBlocks);

            BootstrapConfidenceInterval interval =
                BenchmarkStatistics.BootstrapAmortizedP95Improvement(
                    baseline,
                    candidate,
                    iterations: 1000,
                    confidenceLevel: 0.95d,
                    seed: 0x10203040u);

            Assert.That(interval.Estimand, Does.StartWith("log(candidate_amortized_p95"));
            Assert.That(interval.ResamplingUnit, Is.EqualTo("paired measurement block"));
            Assert.That(interval.EstimatorKind, Is.EqualTo(BootstrapEstimatorKind.PairedBlockLogRatio));
            Assert.That(interval.HasLogRatioEstimate, Is.True);
            Assert.That(interval.PointEstimateLogRatio, Is.EqualTo(Math.Log(0.8d)).Within(1e-12));
            Assert.That(interval.PointEstimatePercent, Is.EqualTo(20d).Within(1e-9));
            Assert.That(interval.LowerBoundPercent, Is.EqualTo(20d).Within(1e-9));
            Assert.That(interval.UpperBoundPercent, Is.EqualTo(20d).Within(1e-9));
        }

        [Test]
        public void PairedBootstrap_WithSameSeed_IsBitwiseDeterministic()
        {
            LayoutBenchmarkResult baseline = CreateResult(
                "baseline",
                true,
                new[] { 10d, 12d, 9d, 15d, 11d, 13d },
                new[] { 0, 1, 2, 3, 4, 5 });
            LayoutBenchmarkResult candidate = CreateResult(
                "candidate",
                false,
                new[] { 8d, 11d, 7d, 12d, 10d, 9d },
                new[] { 0, 1, 2, 3, 4, 5 });

            BootstrapConfidenceInterval first =
                BenchmarkStatistics.BootstrapAmortizedP95Improvement(
                    baseline, candidate, 1000, 0.95d, 0x55667788u);
            BootstrapConfidenceInterval second =
                BenchmarkStatistics.BootstrapAmortizedP95Improvement(
                    baseline, candidate, 1000, 0.95d, 0x55667788u);

            Assert.That(second.PointEstimateLogRatio, Is.EqualTo(first.PointEstimateLogRatio));
            Assert.That(second.LowerBoundLogRatio, Is.EqualTo(first.LowerBoundLogRatio));
            Assert.That(second.UpperBoundLogRatio, Is.EqualTo(first.UpperBoundLogRatio));
            Assert.That(second.LowerBoundPercent, Is.EqualTo(first.LowerBoundPercent));
            Assert.That(second.UpperBoundPercent, Is.EqualTo(first.UpperBoundPercent));
        }

        [Test]
        public void PairedBootstrap_RejectsMismatchedBlockIdentity()
        {
            LayoutBenchmarkResult baseline = CreateResult(
                "baseline",
                true,
                new[] { 10d, 11d, 12d },
                new[] { 0, 1, 2 });
            LayoutBenchmarkResult candidate = CreateResult(
                "candidate",
                false,
                new[] { 8d, 9d, 10d },
                new[] { 0, 1, 99 });

            Assert.Throws<ArgumentException>(() =>
                BenchmarkStatistics.BootstrapAmortizedP95Improvement(
                    baseline, candidate, 500, 0.95d, 1u));
        }

        [Test]
        public void PairedBootstrap_RejectsUnsupportedSampleSchema()
        {
            LayoutBenchmarkResult baseline = CreateResult(
                "baseline", true, new[] { 10d, 11d, 12d }, new[] { 0, 1, 2 });
            LayoutBenchmarkResult candidate = CreateResult(
                "candidate", false, new[] { 8d, 9d, 10d }, new[] { 0, 1, 2 });
            candidate.SampleSchemaVersion = 99;

            Assert.Throws<ArgumentException>(() =>
                BenchmarkStatistics.BootstrapAmortizedP95Improvement(
                    baseline, candidate, 500, 0.95d, 1u));
        }

        [Test]
        public void PairedBootstrap_RejectsMissingCurrentSampleMetadata()
        {
            LayoutBenchmarkResult baseline = CreateResult(
                "baseline", true, new[] { 10d, 11d, 12d }, new[] { 0, 1, 2 });
            LayoutBenchmarkResult candidate = CreateResult(
                "candidate", false, new[] { 8d, 9d, 10d }, new[] { 0, 1, 2 });
            candidate.ResidentOrderPositions = null;

            Assert.Throws<ArgumentException>(() =>
                BenchmarkStatistics.BootstrapAmortizedP95Improvement(
                    baseline, candidate, 500, 0.95d, 1u));
        }

        [Test]
        public void ProcessHierarchy_ResamplesProcessesAndBlocksOnOneDevice()
        {
            ProcessPairedBenchmarkResult[] processes =
            {
                CreateProcess("synthetic-process-1", "synthetic-device-a", 0.80d),
                CreateProcess("synthetic-process-2", "synthetic-device-a", 0.85d),
                CreateProcess("synthetic-process-3", "synthetic-device-a", 0.90d),
            };

            HierarchicalBootstrapConfidenceInterval first =
                BenchmarkStatistics.BootstrapProcessHierarchy(
                    processes, processes.Length, 1000, 0.95d, 0xCAFEBABEu);
            HierarchicalBootstrapConfidenceInterval second =
                BenchmarkStatistics.BootstrapProcessHierarchy(
                    processes, processes.Length, 1000, 0.95d, 0xCAFEBABEu);

            Assert.That(first.EvidenceScope, Is.EqualTo(EvidenceScope.MultipleProcessesSingleDevice));
            Assert.That(first.ProcessCount, Is.EqualTo(3));
            Assert.That(first.DeviceCount, Is.EqualTo(1));
            Assert.That(first.ImprovementConfidenceInterval.ResamplingUnit,
                Is.EqualTo("Player process, then paired measurement block"));
            Assert.That(first.ImprovementConfidenceInterval.EstimatorKind,
                Is.EqualTo(BootstrapEstimatorKind.ProcessHierarchicalLogRatio));
            Assert.That(first.ImprovementConfidenceInterval.HasLogRatioEstimate, Is.True);
            Assert.That(second.ImprovementConfidenceInterval.LowerBoundLogRatio,
                Is.EqualTo(first.ImprovementConfidenceInterval.LowerBoundLogRatio));
            Assert.That(second.ImprovementConfidenceInterval.UpperBoundLogRatio,
                Is.EqualTo(first.ImprovementConfidenceInterval.UpperBoundLogRatio));
        }

        [Test]
        public void ProcessHierarchy_RejectsCrossDeviceInput()
        {
            ProcessPairedBenchmarkResult[] processes =
            {
                CreateProcess("synthetic-process-1", "synthetic-device-a", 0.80d),
                CreateProcess("synthetic-process-2", "synthetic-device-b", 0.85d),
            };

            Assert.Throws<ArgumentException>(() =>
                BenchmarkStatistics.BootstrapProcessHierarchy(
                    processes, processes.Length, 500, 0.95d, 7u));
        }

        [Test]
        public void ProcessHierarchy_RejectsMixedScenarioContractVersions()
        {
            ProcessPairedBenchmarkResult[] processes =
            {
                CreateProcess("synthetic-process-1", "synthetic-device-a", 0.80d),
                CreateProcess("synthetic-process-2", "synthetic-device-a", 0.85d),
            };
            processes[1].Baseline.ScenarioContractVersion = 2;
            processes[1].Candidate.ScenarioContractVersion = 2;

            Assert.Throws<ArgumentException>(() =>
                BenchmarkStatistics.BootstrapProcessHierarchy(
                    processes, processes.Length, 500, 0.95d, 7u));
        }

        [Test]
        public void HoldoutRegression_IsDistinctFromTieAndFallsBackToAoS()
        {
            var calibration = new LayoutSelectionDecision
            {
                DecisionStage = DecisionStage.Calibration,
                Status = LayoutSelectionStatus.Optimized,
                BaselineCandidate = new CandidateDescriptor("AoS", 64, true, candidateId: "baseline"),
                SelectedCandidate = new CandidateDescriptor("SoA", 64, false, candidateId: "candidate"),
                BestMeasuredCandidate = new CandidateDescriptor("SoA", 64, false, candidateId: "candidate"),
                MultiplicityControl = "untouched holdout confirmation",
            };
            LayoutBenchmarkResult baseline = CreateResult(
                "baseline", true, new[] { 10d, 10d, 10d, 10d }, new[] { 0, 1, 2, 3 }, BenchmarkPhase.Holdout);
            LayoutBenchmarkResult candidate = CreateResult(
                "candidate", false, new[] { 12d, 12d, 12d, 12d }, new[] { 0, 1, 2, 3 }, BenchmarkPhase.Holdout);

            LayoutSelectionDecision decision = LayoutSelector.ConfirmHoldout(
                calibration,
                baseline,
                candidate,
                minimumImprovementPercent: 10d,
                bootstrapIterations: 500,
                bootstrapSeed: 0x1234u);

            Assert.That(decision.Status, Is.EqualTo(LayoutSelectionStatus.Regression));
            Assert.That(decision.SelectedCandidate.CandidateId, Is.EqualTo("baseline"));
            Assert.That(decision.ImprovementConfidenceInterval.UpperBoundPercent, Is.LessThan(0d));
        }

        [Test]
        public void CalibrationTie_IsDistinctFromSubThresholdInconclusive()
        {
            var baselineValues = new double[20];
            var candidateValues = new double[20];
            var blocks = new int[20];
            for (int index = 0; index < blocks.Length; index++)
            {
                baselineValues[index] = 10d;
                candidateValues[index] = 9.5d;
                blocks[index] = index;
            }
            candidateValues[candidateValues.Length - 1] = 11d;
            LayoutBenchmarkResult baseline = CreateResult(
                "baseline", true, baselineValues, blocks);
            LayoutBenchmarkResult candidate = CreateResult(
                "candidate", false, candidateValues, blocks);

            LayoutSelectionDecision decision = LayoutSelector.SelectCalibration(
                new[] { baseline, candidate },
                2,
                minimumImprovementPercent: 10d,
                bootstrapIterations: 2000,
                bootstrapSeed: 0x99887766u);

            Assert.That(decision.ImprovementPercent, Is.LessThan(10d));
            Assert.That(decision.Status, Is.EqualTo(LayoutSelectionStatus.StatisticalTie));
            Assert.That(decision.SelectedCandidate.CandidateId, Is.EqualTo("baseline"));
            Assert.That(decision.SelectionRegretPercent, Is.GreaterThan(0d));
        }

        [Test]
        public void CalibrationNegativeInterval_CannotOptimizeFromContradictorySummary()
        {
            int[] blocks = { 0, 1, 2, 3, 4 };
            LayoutBenchmarkResult baseline = CreateResult(
                "baseline", true, new[] { 10d, 10d, 10d, 10d, 10d }, blocks);
            LayoutBenchmarkResult candidate = CreateResult(
                "candidate", false, new[] { 12d, 12d, 12d, 12d, 12d }, blocks);
            LatencySummary contradictorySummary = candidate.AmortizedLatency;
            contradictorySummary.P95Milliseconds = 8d;
            candidate.AmortizedLatency = contradictorySummary;

            LayoutSelectionDecision decision = LayoutSelector.SelectCalibration(
                new[] { baseline, candidate },
                2,
                minimumImprovementPercent: 10d,
                bootstrapIterations: 500,
                bootstrapSeed: 0x31415926u);

            Assert.That(decision.ImprovementPercent, Is.EqualTo(20d).Within(1e-9));
            Assert.That(decision.ImprovementConfidenceInterval.UpperBoundPercent, Is.LessThan(0d));
            Assert.That(decision.Status, Is.EqualTo(LayoutSelectionStatus.Inconclusive));
            Assert.That(decision.SelectedCandidate.CandidateId, Is.EqualTo("baseline"));
        }

        [Test]
        public void HoldoutFreeze_ContainsOnlyFrozenBaselineAndWinner()
        {
            var calibration = new LayoutSelectionDecision
            {
                DecisionStage = DecisionStage.Calibration,
                Status = LayoutSelectionStatus.Optimized,
                BaselineCandidate = new CandidateDescriptor("AoS", 64, true, candidateId: "baseline"),
                SelectedCandidate = new CandidateDescriptor("SoA", 128, false, candidateId: "winner"),
                BestMeasuredCandidate = new CandidateDescriptor("SoA", 128, false, candidateId: "winner"),
            };

            CandidateDescriptor[] frozen = HoldoutIsolation.Freeze(calibration);

            Assert.That(frozen, Has.Length.EqualTo(2));
            Assert.That(frozen[0].CandidateId, Is.EqualTo("baseline"));
            Assert.That(frozen[1].CandidateId, Is.EqualTo("winner"));
            frozen[1].CandidateId = "mutated-local-copy";
            Assert.That(calibration.SelectedCandidate.CandidateId, Is.EqualTo("winner"));
        }

        [Test]
        public void Schema2Migration_IsAdditiveAndMarksUnknownHistoricalOrder()
        {
            LayoutBenchmarkResult result = CreateResult(
                "baseline",
                true,
                new[] { 10d, 11d, 12d },
                null);
            result.SampleSchemaVersion = 0;
            result.ResidentBlockIds = null;
            result.IngressBlockIds = null;
            result.ExportBlockIds = null;
            result.ResidentOrderPositions = null;
            result.IngressOrderPositions = null;
            result.ExportOrderPositions = null;
            CandidateDescriptor legacyCandidate = result.Candidate;
            legacyCandidate.PolicySchemaVersion = 0;
            legacyCandidate.Layout = default;
            legacyCandidate.Kernel = default;
            legacyCandidate.Batch = default;
            legacyCandidate.Execution = default;
            result.Candidate = legacyCandidate;
            var profile = new ScenarioCalibrationProfile
            {
                SchemaVersion = 2,
                Scenario = new ScenarioDescriptor(
                    "synthetic-statistics-fixture",
                    "Synthetic statistics fixture",
                    1,
                    "Synthetic-only operation"),
                CalibrationResults = new[] { result },
                CalibrationDecision = new LayoutSelectionDecision
                {
                    BaselineCandidate = legacyCandidate,
                    SelectedCandidate = legacyCandidate,
                    BestMeasuredCandidate = legacyCandidate,
                },
                FinalDecision = new LayoutSelectionDecision
                {
                    BaselineCandidate = legacyCandidate,
                    SelectedCandidate = legacyCandidate,
                    BestMeasuredCandidate = legacyCandidate,
                    ImprovementConfidenceInterval = new BootstrapConfidenceInterval
                    {
                        Iterations = 500,
                        ConfidenceLevel = 0.95d,
                        PointEstimatePercent = 10d,
                        LowerBoundPercent = -2d,
                        UpperBoundPercent = 17d,
                    },
                },
            };

            CalibrationProfileMigration.UpgradeInMemory(profile);

            Assert.That(profile.SchemaVersion, Is.EqualTo(3));
            Assert.That(profile.CalibrationResults[0].Candidate.CandidateId, Is.EqualTo("baseline"));
            Assert.That(profile.CalibrationResults[0].ScenarioContractVersion, Is.EqualTo(1));
            Assert.That(profile.CalibrationResults[0].Candidate.Kernel.PolicyId,
                Is.EqualTo("LegacyUnspecified"));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, profile.CalibrationResults[0].ResidentBlockIds);
            CollectionAssert.AreEqual(new[] { -1, -1, -1 }, profile.CalibrationResults[0].ResidentOrderPositions);
            Assert.That(profile.SamplingDesign.CandidateOrder,
                Is.EqualTo(MeasurementOrderKind.RandomizedBlocked));
            Assert.That(profile.SamplingDesign.ReconstructedFromSchema2, Is.True);
            Assert.That(profile.SamplingDesign.UncertaintyDescription,
                Does.Contain("do not retroactively change"));
            Assert.That(profile.ElementCount, Is.Zero);
            Assert.That(profile.CalibrationResults[0].ElementCount, Is.EqualTo(1));
            BootstrapConfidenceInterval migratedInterval =
                profile.FinalDecision.ImprovementConfidenceInterval;
            Assert.That(migratedInterval.EstimatorKind,
                Is.EqualTo(BootstrapEstimatorKind.LegacyIndependentPercent));
            Assert.That(migratedInterval.HasLogRatioEstimate, Is.False);
            Assert.That(migratedInterval.PointEstimateLogRatio, Is.Zero);
            Assert.That(migratedInterval.LowerBoundLogRatio, Is.Zero);
            Assert.That(migratedInterval.UpperBoundLogRatio, Is.Zero);
            Assert.That(migratedInterval.PointEstimatePercent, Is.EqualTo(10d));
            Assert.That(migratedInterval.LowerBoundPercent, Is.EqualTo(-2d));
            Assert.That(migratedInterval.UpperBoundPercent, Is.EqualTo(17d));
        }

        [Test]
        public void SuiteUpgrade_RejectsNullAndUnsupportedNestedScenarios()
        {
            var nullSuite = new CalibrationSuiteProfile
            {
                SchemaVersion = 3,
                Scenarios = new ScenarioCalibrationProfile[] { null },
            };
            Assert.Throws<ArgumentException>(() =>
                CalibrationProfileMigration.UpgradeInMemory(nullSuite));

            var nested = CreateSchema3Profile(CreateResult(
                "baseline", true, new[] { 10d, 11d, 12d }, new[] { 0, 1, 2 }));
            nested.SchemaVersion = 99;
            var mismatchedSuite = new CalibrationSuiteProfile
            {
                SchemaVersion = 3,
                Scenarios = new[] { nested },
            };
            Assert.Throws<ArgumentException>(() =>
                CalibrationProfileMigration.UpgradeInMemory(mismatchedSuite));
            Assert.That(mismatchedSuite.SchemaVersion, Is.EqualTo(3));
            Assert.That(nested.SchemaVersion, Is.EqualTo(99));
        }

        [Test]
        public void Schema3Upgrade_IsValidationOnly()
        {
            LayoutBenchmarkResult result = CreateResult(
                "baseline", true, new[] { 10d, 11d, 12d }, new[] { 0, 1, 2 });
            ScenarioCalibrationProfile profile = CreateSchema3Profile(result);
            int[] residentBlockIds = result.ResidentBlockIds;
            int[] residentOrderPositions = result.ResidentOrderPositions;
            SamplingDesignDescriptor samplingDesign = profile.SamplingDesign;

            ScenarioCalibrationProfile upgraded =
                CalibrationProfileMigration.UpgradeInMemory(profile);

            Assert.That(upgraded, Is.SameAs(profile));
            Assert.That(result.ResidentBlockIds, Is.SameAs(residentBlockIds));
            Assert.That(result.ResidentOrderPositions, Is.SameAs(residentOrderPositions));
            Assert.That(profile.SamplingDesign, Is.SameAs(samplingDesign));
        }

        [Test]
        public void Schema3Upgrade_RejectsUnsupportedSampleAndPolicySchemas()
        {
            LayoutBenchmarkResult unsupportedSample = CreateResult(
                "baseline", true, new[] { 10d, 11d, 12d }, new[] { 0, 1, 2 });
            unsupportedSample.SampleSchemaVersion = 99;
            ScenarioCalibrationProfile sampleProfile = CreateSchema3Profile(unsupportedSample);
            Assert.Throws<ArgumentException>(() =>
                CalibrationProfileMigration.UpgradeInMemory(sampleProfile));
            Assert.That(unsupportedSample.SampleSchemaVersion, Is.EqualTo(99));

            LayoutBenchmarkResult unsupportedPolicy = CreateResult(
                "baseline", true, new[] { 10d, 11d, 12d }, new[] { 0, 1, 2 });
            CandidateDescriptor candidate = unsupportedPolicy.Candidate;
            candidate.PolicySchemaVersion = 99;
            unsupportedPolicy.Candidate = candidate;
            ScenarioCalibrationProfile policyProfile = CreateSchema3Profile(unsupportedPolicy);
            Assert.Throws<ArgumentException>(() =>
                CalibrationProfileMigration.UpgradeInMemory(policyProfile));
            Assert.That(unsupportedPolicy.Candidate.PolicySchemaVersion, Is.EqualTo(99));
        }

        [Test]
        public void Schema3Upgrade_RejectsMissingMetadataAndIdentityMismatchWithoutRepair()
        {
            LayoutBenchmarkResult missingMetadata = CreateResult(
                "baseline", true, new[] { 10d, 11d, 12d }, new[] { 0, 1, 2 });
            missingMetadata.ResidentOrderPositions = null;
            ScenarioCalibrationProfile metadataProfile = CreateSchema3Profile(missingMetadata);
            Assert.Throws<ArgumentException>(() =>
                CalibrationProfileMigration.UpgradeInMemory(metadataProfile));
            Assert.That(missingMetadata.ResidentOrderPositions, Is.Null);

            LayoutBenchmarkResult mismatchedIdentity = CreateResult(
                "baseline", true, new[] { 10d, 11d, 12d }, new[] { 0, 1, 2 });
            mismatchedIdentity.ScenarioId = "different-scenario";
            ScenarioCalibrationProfile identityProfile = CreateSchema3Profile(mismatchedIdentity);
            Assert.Throws<ArgumentException>(() =>
                CalibrationProfileMigration.UpgradeInMemory(identityProfile));
            Assert.That(mismatchedIdentity.ScenarioId, Is.EqualTo("different-scenario"));
        }

        [Test]
        public void Schema3Upgrade_RejectsDecisionCandidateIdentityMismatch()
        {
            LayoutBenchmarkResult result = CreateResult(
                "baseline", true, new[] { 10d, 11d, 12d }, new[] { 0, 1, 2 });
            ScenarioCalibrationProfile profile = CreateSchema3Profile(result);
            profile.CalibrationDecision = new LayoutSelectionDecision
            {
                DecisionStage = DecisionStage.Calibration,
                Status = LayoutSelectionStatus.Inconclusive,
                BaselineCandidate = result.Candidate,
                SelectedCandidate = new CandidateDescriptor(
                    "SoA", 64, false, candidateId: "baseline"),
                BestMeasuredCandidate = result.Candidate,
            };

            Assert.Throws<ArgumentException>(() =>
                CalibrationProfileMigration.UpgradeInMemory(profile));
            Assert.That(profile.CalibrationDecision.SelectedCandidate.LayoutId,
                Is.EqualTo("SoA"));
        }

        [Test]
        public void Schema3Upgrade_RequiresNonEmptyCalibrationResultsWithMatchingPhaseAndCount()
        {
            LayoutBenchmarkResult seedResult = CreateResult(
                "baseline", true, new[] { 10d, 11d, 12d }, new[] { 0, 1, 2 });
            ScenarioCalibrationProfile emptyProfile = CreateSchema3Profile(seedResult);
            emptyProfile.CalibrationResults = new LayoutBenchmarkResult[0];
            Assert.Throws<ArgumentException>(() =>
                CalibrationProfileMigration.UpgradeInMemory(emptyProfile));

            LayoutBenchmarkResult wrongPhase = CreateResult(
                "baseline", true, new[] { 10d, 11d, 12d }, new[] { 0, 1, 2 });
            wrongPhase.Phase = BenchmarkPhase.Holdout;
            ScenarioCalibrationProfile phaseProfile = CreateSchema3Profile(wrongPhase);
            Assert.Throws<ArgumentException>(() =>
                CalibrationProfileMigration.UpgradeInMemory(phaseProfile));

            LayoutBenchmarkResult wrongCount = CreateResult(
                "baseline", true, new[] { 10d, 11d, 12d }, new[] { 0, 1, 2 });
            ScenarioCalibrationProfile countProfile = CreateSchema3Profile(wrongCount);
            countProfile.ElementCount = wrongCount.ElementCount + 1;
            Assert.Throws<ArgumentException>(() =>
                CalibrationProfileMigration.UpgradeInMemory(countProfile));
        }

        [Test]
        public void Schema3Upgrade_RequiresCompleteSamplingDesignDeclarations()
        {
            ScenarioCalibrationProfile missingTuning = CreateSchema3Profile(CreateResult(
                "baseline", true, new[] { 10d, 11d, 12d }, new[] { 0, 1, 2 }));
            missingTuning.SamplingDesign.CalibrationTunesCandidates = false;
            Assert.Throws<ArgumentException>(() =>
                CalibrationProfileMigration.UpgradeInMemory(missingTuning));

            ScenarioCalibrationProfile missingUncertainty = CreateSchema3Profile(CreateResult(
                "baseline", true, new[] { 10d, 11d, 12d }, new[] { 0, 1, 2 }));
            missingUncertainty.SamplingDesign.UncertaintyDescription = " ";
            Assert.Throws<ArgumentException>(() =>
                CalibrationProfileMigration.UpgradeInMemory(missingUncertainty));
        }

        [Test]
        public void Schema3Upgrade_BindsHoldoutToFrozenCalibrationWinner()
        {
            LayoutBenchmarkResult baseline = CreateResult(
                "baseline", true, new[] { 10d, 11d, 12d }, new[] { 0, 1, 2 });
            LayoutBenchmarkResult winner = CreateResult(
                "winner", false, new[] { 8d, 9d, 10d }, new[] { 0, 1, 2 });
            LayoutBenchmarkResult arbitrary = CreateResult(
                "arbitrary", false, new[] { 9d, 10d, 11d }, new[] { 0, 1, 2 });
            ScenarioCalibrationProfile profile = CreateSchema3Profile(baseline);
            profile.CalibrationResults = new[] { baseline, winner, arbitrary };
            profile.HoldoutElementCount = 2;
            profile.CalibrationDecision = new LayoutSelectionDecision
            {
                DecisionStage = DecisionStage.Calibration,
                Status = LayoutSelectionStatus.Optimized,
                BaselineCandidate = baseline.Candidate,
                SelectedCandidate = winner.Candidate,
                BestMeasuredCandidate = winner.Candidate,
            };
            profile.FinalDecision = new LayoutSelectionDecision
            {
                DecisionStage = DecisionStage.HoldoutConfirmation,
                Status = LayoutSelectionStatus.Inconclusive,
                BaselineCandidate = baseline.Candidate,
                SelectedCandidate = baseline.Candidate,
                BestMeasuredCandidate = winner.Candidate,
            };
            LayoutBenchmarkResult holdoutBaseline = CreateResult(
                "baseline",
                true,
                new[] { 10d, 11d, 12d },
                new[] { 0, 1, 2 },
                BenchmarkPhase.Holdout);
            holdoutBaseline.ElementCount = profile.HoldoutElementCount;
            LayoutBenchmarkResult holdoutWinner = CreateResult(
                "winner",
                false,
                new[] { 8d, 9d, 10d },
                new[] { 0, 1, 2 },
                BenchmarkPhase.Holdout);
            holdoutWinner.ElementCount = profile.HoldoutElementCount;
            profile.HoldoutBaselineResult = holdoutBaseline;
            profile.HoldoutSelectedResult = holdoutWinner;

            Assert.That(CalibrationProfileMigration.UpgradeInMemory(profile),
                Is.SameAs(profile));

            LayoutBenchmarkResult holdoutArbitrary = CreateResult(
                "arbitrary",
                false,
                new[] { 9d, 10d, 11d },
                new[] { 0, 1, 2 },
                BenchmarkPhase.Holdout);
            holdoutArbitrary.ElementCount = profile.HoldoutElementCount;
            profile.HoldoutSelectedResult = holdoutArbitrary;
            LayoutSelectionDecision arbitraryFallback = profile.FinalDecision;
            arbitraryFallback.BestMeasuredCandidate = arbitrary.Candidate;
            profile.FinalDecision = arbitraryFallback;

            Assert.Throws<ArgumentException>(() =>
                CalibrationProfileMigration.UpgradeInMemory(profile));
            Assert.That(profile.HoldoutSelectedResult.Candidate.CandidateId,
                Is.EqualTo("arbitrary"));
        }

        [Test]
        public void Schema2Migration_RejectsWrongMetadataInsteadOfOverwritingIt()
        {
            LayoutBenchmarkResult result = CreateResult(
                "baseline", true, new[] { 10d, 11d, 12d }, null);
            result.SampleSchemaVersion = 0;
            result.ResidentBlockIds = new[] { 0 };
            result.ResidentOrderPositions = null;
            ScenarioCalibrationProfile profile = CreateLegacyProfile(result);

            Assert.Throws<ArgumentException>(() =>
                CalibrationProfileMigration.UpgradeInMemory(profile));
            Assert.That(profile.SchemaVersion, Is.EqualTo(2));
            CollectionAssert.AreEqual(new[] { 0 }, result.ResidentBlockIds);
            Assert.That(result.ResidentOrderPositions, Is.Null);
        }

        private static ProcessPairedBenchmarkResult CreateProcess(
            string processId,
            string deviceId,
            double candidateRatio)
        {
            double[] baselineValues = { 10d, 20d, 30d, 40d, 50d };
            var candidateValues = new double[baselineValues.Length];
            var blocks = new int[baselineValues.Length];
            for (int index = 0; index < baselineValues.Length; index++)
            {
                candidateValues[index] = baselineValues[index] * candidateRatio;
                blocks[index] = index;
            }

            return new ProcessPairedBenchmarkResult
            {
                ProcessId = processId,
                DeviceId = deviceId,
                Baseline = CreateResult("baseline", true, baselineValues, blocks),
                Candidate = CreateResult("candidate", false, candidateValues, blocks),
            };
        }

        private static ScenarioCalibrationProfile CreateSchema3Profile(
            LayoutBenchmarkResult result)
        {
            return new ScenarioCalibrationProfile
            {
                SchemaVersion = 3,
                Scenario = new ScenarioDescriptor(
                    "synthetic-statistics-fixture",
                    "Synthetic statistics fixture",
                    1,
                    "Synthetic-only operation"),
                ElementCount = result.ElementCount,
                SamplingDesign = new SamplingDesignDescriptor
                {
                    CandidateOrder = MeasurementOrderKind.BalancedLatinSquare,
                    PairingUnit = "complete measurement block",
                    EvidenceScope = EvidenceScope.SinglePlayer,
                    CalibrationTunesCandidates = true,
                    HoldoutRetuningPermitted = false,
                    UncertaintyDescription = "Synthetic validation fixture.",
                },
                CalibrationResults = new[] { result },
                CalibrationDecision = new LayoutSelectionDecision
                {
                    DecisionStage = DecisionStage.Calibration,
                },
                FinalDecision = new LayoutSelectionDecision
                {
                    DecisionStage = DecisionStage.Calibration,
                },
            };
        }

        private static ScenarioCalibrationProfile CreateLegacyProfile(
            LayoutBenchmarkResult result)
        {
            CandidateDescriptor legacyCandidate = result.Candidate;
            legacyCandidate.PolicySchemaVersion = 0;
            legacyCandidate.Layout = default;
            legacyCandidate.Kernel = default;
            legacyCandidate.Batch = default;
            legacyCandidate.Execution = default;
            result.Candidate = legacyCandidate;
            return new ScenarioCalibrationProfile
            {
                SchemaVersion = 2,
                Scenario = new ScenarioDescriptor(
                    "synthetic-statistics-fixture",
                    "Synthetic statistics fixture",
                    1,
                    "Synthetic-only operation"),
                CalibrationResults = new[] { result },
            };
        }

        private static LayoutBenchmarkResult CreateResult(
            string candidateId,
            bool isBaseline,
            double[] resident,
            int[] blockIds,
            BenchmarkPhase phase = BenchmarkPhase.Calibration)
        {
            var ingress = new double[resident.Length];
            var export = new double[resident.Length];
            var result = new LayoutBenchmarkResult
            {
                ScenarioId = "synthetic-statistics-fixture",
                ScenarioContractVersion = 1,
                Phase = phase,
                Candidate = new CandidateDescriptor(
                    isBaseline ? "AoS" : "SoA",
                    64,
                    isBaseline,
                    candidateId: candidateId),
                ElementCount = 1,
                StepsPerSample = 1,
                Completed = true,
                ParityPassed = true,
                BoundaryCost = new BoundaryCostSummary { LifetimeTicks = 600 },
                ResidentSamplesMillisecondsPerTick = (double[])resident.Clone(),
                IngressSamplesMilliseconds = ingress,
                ExportSamplesMilliseconds = export,
                ResidentBlockIds = blockIds == null ? null : (int[])blockIds.Clone(),
                IngressBlockIds = blockIds == null ? null : (int[])blockIds.Clone(),
                ExportBlockIds = blockIds == null ? null : (int[])blockIds.Clone(),
                ResidentOrderPositions = blockIds == null ? null : new int[resident.Length],
                IngressOrderPositions = blockIds == null ? null : new int[resident.Length],
                ExportOrderPositions = blockIds == null ? null : new int[resident.Length],
            };
            var scratch = new double[resident.Length];
            result.Latency = BenchmarkStatistics.Calculate(
                result.ResidentSamplesMillisecondsPerTick,
                resident.Length,
                scratch);
            result.AmortizedSamplesMillisecondsPerTick = new double[resident.Length];
            result.AmortizedLatency = BenchmarkStatistics.CalculateAmortizedLatency(
                result.ResidentSamplesMillisecondsPerTick,
                ingress,
                export,
                600,
                result.AmortizedSamplesMillisecondsPerTick,
                scratch);
            return result;
        }
    }
}
