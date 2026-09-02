using System;
using NUnit.Framework;

namespace Yanagisawa.DataLayoutCalibrator.Tests
{
    public sealed class VNextIntegrationProtocolTests
    {
        private const int ElementCount = 1024;
        private const int LifetimeTicks = 10;
        private const uint BootstrapSeed = 123456789u;

        [Test]
        public void CandidateDefinitionHash_IgnoresDisplayNameAndBindsSemanticFields()
        {
            CandidateDescriptor original = Candidate("aos", true, 0);
            Assert.That(
                CandidateDefinitionProtocol.ComputeCandidateDefinitionSha256(original),
                Is.EqualTo("9484C1C638CF82EB5D499BB5DDBEF86C2F7610B202C6BA323597D0CC3E69470F"));
            CandidateDescriptor renamed = original;
            renamed.DisplayName = "Presentation-only rename";
            Assert.That(
                CandidateDefinitionProtocol.ComputeCandidateDefinitionSha256(renamed),
                Is.EqualTo(CandidateDefinitionProtocol.ComputeCandidateDefinitionSha256(original)));

            CandidateDescriptor realigned = original;
            LayoutPolicy layout = realigned.Layout;
            layout.AlignmentBytes = 64;
            realigned.Layout = layout;
            Assert.That(
                CandidateDefinitionProtocol.ComputeCandidateDefinitionSha256(realigned),
                Is.Not.EqualTo(CandidateDefinitionProtocol.ComputeCandidateDefinitionSha256(original)));
            Assert.That(
                CandidateDefinitionProtocol.IsCanonicalSha256(
                    CandidateDefinitionProtocol.ComputeCandidateDefinitionSha256(original)),
                Is.True);
        }

        [Test]
        public void CandidateSetHash_IsOrderIndependentAndRejectsDuplicateIdentity()
        {
            CandidateDescriptor baseline = Candidate("aos", true, 0);
            CandidateDescriptor candidate = Candidate("soa", false, 1);
            string forward = CandidateDefinitionProtocol.ComputeCandidateSetSha256(
                new[] { baseline, candidate });
            string reverse = CandidateDefinitionProtocol.ComputeCandidateSetSha256(
                new[] { candidate, baseline });

            Assert.That(reverse, Is.EqualTo(forward));
            Assert.Throws<ArgumentException>(() =>
                CandidateDefinitionProtocol.ComputeCandidateSetSha256(
                    new[] { baseline, baseline }));
        }

        [Test]
        public void AdapterEnvelopeInterval_EqualsScientificLogRatioInterval()
        {
            LayoutBenchmarkResult baseline = Result(
                "aos",
                true,
                BenchmarkPhase.Calibration,
                new[] { 10d, 13d, 9d, 12d, 11d },
                new[] { 2d, 3d, 2.5d, 2.25d, 2.75d },
                new[] { 1d, 1.5d, 1.25d, 1.75d, 1.1d });
            LayoutBenchmarkResult candidate = Result(
                "soa",
                false,
                BenchmarkPhase.Calibration,
                new[] { 7d, 8d, 6.5d, 7.5d, 7.25d },
                new[] { 2.5d, 3.5d, 3d, 2.75d, 3.25d },
                new[] { 1.5d, 1.75d, 1.6d, 2d, 1.4d });
            ScientificEvidenceBinding[] bindings =
            {
                Binding(baseline, "calibration", 'A'),
                Binding(candidate, "calibration", 'B'),
            };
            AdvantageEnvelopeAxis axis = Axis();
            AdvantageEnvelopeCellInput cell =
                ScientificAdvantageEnvelopeAdapter.CreateCalibrationCell(
                    axis,
                    bindings,
                    100,
                    BootstrapSeed);

            BootstrapConfidenceInterval scientific =
                BenchmarkStatistics.BootstrapAmortizedP95Improvement(
                    baseline,
                    candidate,
                    100,
                    0.95d,
                    BootstrapSeed);
            AdvantageEnvelopeCalibration envelope = AdvantageEnvelopeEngine.Calibrate(
                CalibrationRequest(cell, bindings));
            EnvelopeCandidateOutcome outcome = FindOutcome(
                envelope.Cells[0],
                "soa");

            Assert.That(outcome.ImprovementConfidenceInterval.PointEstimatePercent,
                Is.EqualTo(scientific.PointEstimatePercent).Within(1e-12d));
            Assert.That(outcome.ImprovementConfidenceInterval.LowerBoundPercent,
                Is.EqualTo(scientific.LowerBoundPercent).Within(1e-12d));
            Assert.That(outcome.ImprovementConfidenceInterval.UpperBoundPercent,
                Is.EqualTo(scientific.UpperBoundPercent).Within(1e-12d));
            Assert.That(envelope.CalibrationUncertaintyMethod,
                Is.EqualTo(ScientificAdvantageEnvelopeAdapter.PairedBlockUncertaintyMethod));
        }

        [Test]
        public void Adapter_PreservesExplicitContractInfeasibility()
        {
            LayoutBenchmarkResult baseline = Result(
                "aos", true, BenchmarkPhase.Calibration,
                new[] { 10d, 11d, 12d, 13d, 14d });
            LayoutBenchmarkResult candidate = Result(
                "soa", false, BenchmarkPhase.Calibration,
                new[] { 6d, 7d, 8d, 9d, 10d });
            ScientificEvidenceBinding[] bindings =
            {
                Binding(baseline, "calibration", 'A'),
                Binding(candidate, "calibration", 'B', contractFeasible: false),
            };
            AdvantageEnvelopeCalibration envelope = AdvantageEnvelopeEngine.Calibrate(
                CalibrationRequest(
                    ScientificAdvantageEnvelopeAdapter.CreateCalibrationCell(
                        Axis(), bindings, 100, BootstrapSeed),
                    bindings));

            Assert.That(FindOutcome(envelope.Cells[0], "soa").GateStatus,
                Is.EqualTo(CandidateEvidenceGateStatus.ContractInfeasible));
            Assert.That(envelope.Cells[0].FrozenCalibrationWinnerCandidateId,
                Is.EqualTo("aos"));
        }

        [Test]
        public void HoldoutAdapter_RequiresUnusedHoldoutPhaseAndPreservesPairing()
        {
            LayoutBenchmarkResult baseline = Result(
                "aos", true, BenchmarkPhase.Holdout,
                new[] { 10d, 11d, 12d, 13d, 14d });
            LayoutBenchmarkResult candidate = Result(
                "soa", false, BenchmarkPhase.Holdout,
                new[] { 7d, 8d, 9d, 10d, 11d });
            AdvantageEnvelopeHoldoutCellInput cell =
                ScientificAdvantageEnvelopeAdapter.CreateHoldoutCell(
                    Axis(),
                    Binding(baseline, "holdout", 'C'),
                    Binding(candidate, "holdout", 'D'),
                    100,
                    BootstrapSeed);

            Assert.That(cell.Baseline.Candidate.CandidateId, Is.EqualTo("aos"));
            Assert.That(cell.FrozenCandidate.Candidate.CandidateId, Is.EqualTo("soa"));
            Assert.That(cell.Baseline.BootstrapReplicates[0].ReplicateId,
                Is.EqualTo(cell.FrozenCandidate.BootstrapReplicates[0].ReplicateId));

            candidate.Phase = BenchmarkPhase.Calibration;
            Assert.Throws<ArgumentException>(() =>
                ScientificAdvantageEnvelopeAdapter.CreateHoldoutCell(
                    Axis(),
                    Binding(baseline, "holdout", 'C'),
                    Binding(candidate, "holdout", 'D'),
                    100,
                    BootstrapSeed));
        }

        [Test]
        public void Schema3EnvelopeReference_BindsArtifactCandidateSetAndMeasurementSchema()
        {
            LayoutBenchmarkResult baseline = Result(
                "aos", true, BenchmarkPhase.Calibration,
                new[] { 10d, 11d, 12d, 13d, 14d });
            LayoutBenchmarkResult candidate = Result(
                "soa", false, BenchmarkPhase.Calibration,
                new[] { 7d, 8d, 9d, 10d, 11d });
            ScenarioCalibrationProfile scenario = ScenarioProfile(baseline, candidate);
            string candidateSet = CandidateDefinitionProtocol.ComputeCandidateSetSha256(
                new[] { baseline.Candidate, candidate.Candidate });
            var envelope = new AdvantageEnvelopeProfile
            {
                DecisionEngineVersion = AdvantageEnvelopeEngine.Version,
                ScenarioId = scenario.Scenario.ScenarioId,
                ContractVersion = scenario.Scenario.ContractVersion,
                CandidateSetHash = candidateSet,
                MeasurementSchemaHash = ScientificAdvantageEnvelopeAdapter.MeasurementSchemaSha256,
                FinalDecisionLocked = true,
                HoldoutCanRerank = false,
            };
            scenario.AdvantageEnvelope =
                ScientificAdvantageEnvelopeAdapter.CreateArtifactReference(
                    scenario,
                    envelope,
                    "synthetic-envelope-artifact",
                    Hash('E'));

            Assert.That(CalibrationProfileMigration.UpgradeInMemory(scenario),
                Is.SameAs(scenario));
            scenario.AdvantageEnvelope.CandidateSetSha256 = Hash('F');
            Assert.Throws<ArgumentException>(() =>
                CalibrationProfileMigration.UpgradeInMemory(scenario));
        }

        private static AdvantageEnvelopeCalibrationRequest CalibrationRequest(
            AdvantageEnvelopeCellInput cell,
            ScientificEvidenceBinding[] bindings)
        {
            return new AdvantageEnvelopeCalibrationRequest
            {
                EnvelopeId = "synthetic-integrated-envelope",
                CreatedUtcIso8601 = "2026-09-02T00:00:00Z",
                ScenarioId = "synthetic-integrated-scenario",
                ContractVersion = 1,
                CandidateSetHash =
                    ScientificAdvantageEnvelopeAdapter.ComputeCandidateSetSha256(bindings),
                MeasurementSchemaHash =
                    ScientificAdvantageEnvelopeAdapter.MeasurementSchemaSha256,
                EnvironmentFingerprint = Hash('1'),
                CalibrationSettingsHash = Hash('2'),
                SourceArtifactId = "synthetic-calibration-artifact",
                SourceArtifactSha256 = Hash('3'),
                EvidenceScope = "synthetic-test-fixture",
                CalibrationUncertaintyMethod =
                    ScientificAdvantageEnvelopeAdapter.PairedBlockUncertaintyMethod,
                Policy = new AdvantageEnvelopePolicy
                {
                    MinimumImprovementPercent = 1d,
                    MinimumBootstrapReplicates = 100,
                    MinimumCalibrationResidentSamples = 5,
                    MinimumCalibrationBoundarySamples = 5,
                    MinimumHoldoutResidentSamples = 5,
                    MinimumHoldoutBoundarySamples = 5,
                },
                Cells = new[] { cell },
            };
        }

        private static ScenarioCalibrationProfile ScenarioProfile(
            LayoutBenchmarkResult baseline,
            LayoutBenchmarkResult candidate)
        {
            return new ScenarioCalibrationProfile
            {
                Scenario = new ScenarioDescriptor(
                    "synthetic-integrated-scenario",
                    "Synthetic integrated scenario",
                    1,
                    "Synthetic-only operation"),
                ElementCount = ElementCount,
                SamplingDesign = new SamplingDesignDescriptor
                {
                    CandidateOrder = MeasurementOrderKind.BalancedLatinSquare,
                    PairingUnit = "complete measurement block",
                    EvidenceScope = EvidenceScope.SinglePlayer,
                    CalibrationTunesCandidates = true,
                    HoldoutRetuningPermitted = false,
                    UncertaintyDescription = "Synthetic validation fixture.",
                },
                CalibrationResults = new[] { baseline, candidate },
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

        private static EnvelopeCandidateOutcome FindOutcome(
            EnvelopeCalibrationCellDecision cell,
            string candidateId)
        {
            for (int index = 0; index < cell.CandidateOutcomes.Length; index++)
            {
                if (string.Equals(
                        cell.CandidateOutcomes[index].Candidate.CandidateId,
                        candidateId,
                        StringComparison.Ordinal))
                {
                    return cell.CandidateOutcomes[index];
                }
            }
            Assert.Fail("Candidate outcome was not found.");
            return null;
        }

        private static AdvantageEnvelopeAxis Axis()
        {
            return new AdvantageEnvelopeAxis(
                ElementCount,
                LifetimeTicks,
                3d,
                4,
                ExecutionPolicy.FrameFaithful.PolicyId);
        }

        private static ScientificEvidenceBinding Binding(
            LayoutBenchmarkResult result,
            string partition,
            char hash,
            bool contractFeasible = true,
            bool memoryFeasible = true)
        {
            return new ScientificEvidenceBinding
            {
                Result = result,
                ContractFeasible = contractFeasible,
                MemoryFeasible = memoryFeasible,
                EvidencePartitionId = partition,
                EvidenceSha256 = Hash(hash),
            };
        }

        private static LayoutBenchmarkResult Result(
            string candidateId,
            bool isBaseline,
            BenchmarkPhase phase,
            double[] resident,
            double[] ingress = null,
            double[] export = null)
        {
            ingress = ingress ?? new[] { 1d, 1d, 1d, 1d, 1d };
            export = export ?? new[] { 1d, 1d, 1d, 1d, 1d };
            var blockIds = new int[resident.Length];
            var orderPositions = new int[resident.Length];
            for (int index = 0; index < resident.Length; index++)
            {
                blockIds[index] = index;
                orderPositions[index] = index;
            }

            var result = new LayoutBenchmarkResult
            {
                ScenarioId = "synthetic-integrated-scenario",
                ScenarioContractVersion = 1,
                Phase = phase,
                Candidate = Candidate(candidateId, isBaseline, isBaseline ? 0 : 1),
                ElementCount = ElementCount,
                StepsPerSample = 1,
                Completed = true,
                ParityPassed = true,
                ResidentBytes = isBaseline ? 4096 : 3072,
                BoundaryCost = new BoundaryCostSummary { LifetimeTicks = LifetimeTicks },
                ResidentSamplesMillisecondsPerTick = (double[])resident.Clone(),
                IngressSamplesMilliseconds = (double[])ingress.Clone(),
                ExportSamplesMilliseconds = (double[])export.Clone(),
                ResidentBlockIds = (int[])blockIds.Clone(),
                IngressBlockIds = (int[])blockIds.Clone(),
                ExportBlockIds = (int[])blockIds.Clone(),
                ResidentOrderPositions = (int[])orderPositions.Clone(),
                IngressOrderPositions = (int[])orderPositions.Clone(),
                ExportOrderPositions = (int[])orderPositions.Clone(),
            };
            var scratch = new double[resident.Length];
            result.Latency = BenchmarkStatistics.Calculate(
                result.ResidentSamplesMillisecondsPerTick,
                resident.Length,
                scratch);
            result.BoundaryCost.IngressLatency = BenchmarkStatistics.Calculate(
                result.IngressSamplesMilliseconds,
                ingress.Length,
                scratch);
            result.BoundaryCost.ExportLatency = BenchmarkStatistics.Calculate(
                result.ExportSamplesMilliseconds,
                export.Length,
                scratch);
            result.AmortizedSamplesMillisecondsPerTick = new double[resident.Length];
            result.AmortizedLatency = BenchmarkStatistics.CalculateAmortizedLatency(
                result.ResidentSamplesMillisecondsPerTick,
                result.IngressSamplesMilliseconds,
                result.ExportSamplesMilliseconds,
                LifetimeTicks,
                result.AmortizedSamplesMillisecondsPerTick,
                scratch);
            return result;
        }

        private static CandidateDescriptor Candidate(
            string candidateId,
            bool isBaseline,
            int sortOrder)
        {
            return new CandidateDescriptor(
                isBaseline ? "AoS" : "SoA",
                64,
                isBaseline,
                sortOrder,
                candidateId,
                candidateId);
        }

        private static string Hash(char character)
        {
            return new string(character, 64);
        }
    }
}
