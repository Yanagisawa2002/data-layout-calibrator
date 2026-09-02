using System;
using NUnit.Framework;
using UnityEngine;

namespace Yanagisawa.DataLayoutCalibrator.Tests
{
    public sealed class AdvantageEnvelopeDecisionTests
    {
        private const string ExecutionPolicy = "frame-faithful-v1";

        [Test]
        public void BreakEven_HandlesCrossingsZeroSlopesEqualCostsAndDominance()
        {
            Assert.That(
                AdvantageEnvelopeEngine.ClassifyBreakEven(
                    10d,
                    0d,
                    8d,
                    20d,
                    out double winsAbove),
                Is.EqualTo(BreakEvenKind.CandidateWinsAboveLifetime));
            Assert.That(winsAbove, Is.EqualTo(10d).Within(1e-12));

            Assert.That(
                AdvantageEnvelopeEngine.ClassifyBreakEven(
                    10d,
                    10d,
                    11d,
                    0d,
                    out double winsBelow),
                Is.EqualTo(BreakEvenKind.CandidateWinsBelowLifetime));
            Assert.That(winsBelow, Is.EqualTo(10d).Within(1e-12));

            Assert.That(
                AdvantageEnvelopeEngine.ClassifyBreakEven(
                    10d,
                    10d,
                    10d,
                    5d,
                    out _),
                Is.EqualTo(BreakEvenKind.CandidateAlwaysAdvantaged));
            Assert.That(
                AdvantageEnvelopeEngine.ClassifyBreakEven(
                    10d,
                    10d,
                    11d,
                    11d,
                    out _),
                Is.EqualTo(BreakEvenKind.CandidateNeverAdvantaged));
            Assert.That(
                AdvantageEnvelopeEngine.ClassifyBreakEven(
                    10d,
                    10d,
                    10d,
                    10d,
                    out double equalCrossing),
                Is.EqualTo(BreakEvenKind.EqualCosts));
            Assert.That(equalCrossing, Is.Zero);
        }

        [Test]
        public void BreakEven_AlignedSyntheticReplicatesProduceBoundedDeterministicInterval()
        {
            DecisionCandidateEvidence baseline = CreateEvidence(
                "aos-tuned",
                true,
                10d,
                0d,
                100,
                "synthetic-calibration");
            DecisionCandidateEvidence candidate = CreateEvidence(
                "soa-candidate",
                false,
                8d,
                20d,
                80,
                "synthetic-calibration");

            BreakEvenEstimate estimate = AdvantageEnvelopeEngine.CalculateBreakEven(
                baseline,
                candidate);

            Assert.That(estimate.Kind, Is.EqualTo(BreakEvenKind.CandidateWinsAboveLifetime));
            Assert.That(
                estimate.UncertaintyStatus,
                Is.EqualTo(BreakEvenUncertaintyStatus.BoundedCrossing));
            Assert.That(estimate.PointLifetimeTicks, Is.EqualTo(10d).Within(1e-12));
            Assert.That(estimate.LowerConfidenceLifetimeTicks, Is.EqualTo(10d).Within(1e-12));
            Assert.That(estimate.UpperConfidenceLifetimeTicks, Is.EqualTo(10d).Within(1e-12));
            Assert.That(estimate.SameRegimeReplicateCount, Is.EqualTo(100));
            Assert.That(estimate.SameRegimePercent, Is.EqualTo(100d));
        }

        [Test]
        public void BreakEven_MixedBootstrapRegimesRemainExplicitlyUncertain()
        {
            DecisionCandidateEvidence baseline = CreateEvidence(
                "aos-tuned",
                true,
                10d,
                0d,
                100,
                "synthetic-calibration");
            DecisionCandidateEvidence candidate = CreateEvidence(
                "soa-candidate",
                false,
                8d,
                20d,
                80,
                "synthetic-calibration");
            for (int index = 50; index < candidate.BootstrapReplicates.Length; index++)
            {
                BootstrapCostReplicate replicate = candidate.BootstrapReplicates[index];
                replicate.ResidentP95MillisecondsPerTick = 12d;
                candidate.BootstrapReplicates[index] = replicate;
            }

            BreakEvenEstimate estimate = AdvantageEnvelopeEngine.CalculateBreakEven(
                baseline,
                candidate);

            Assert.That(estimate.Kind, Is.EqualTo(BreakEvenKind.CandidateWinsAboveLifetime));
            Assert.That(
                estimate.UncertaintyStatus,
                Is.EqualTo(BreakEvenUncertaintyStatus.MixedRegimes));
            Assert.That(estimate.SameRegimeReplicateCount, Is.EqualTo(50));
            Assert.That(estimate.NeverAdvantagedReplicateCount, Is.EqualTo(50));
        }

        [Test]
        public void Envelope_ScansEveryAxisAndReportsFrozenCoverageSummary()
        {
            AdvantageEnvelopeCalibrationRequest request = CreateEnvelopeRequest(
                new[] { 40, 5, 20, 10 });

            AdvantageEnvelopeCalibration calibration =
                AdvantageEnvelopeEngine.Calibrate(request);

            Assert.That(calibration.HoldoutWasRead, Is.False);
            Assert.That(calibration.Cells[0].Axis.LifetimeTicks, Is.EqualTo(5));
            Assert.That(
                calibration.Cells[2].FrozenCalibrationWinnerCandidateId,
                Is.EqualTo("soa-candidate"));

            AdvantageEnvelopeProfile profile = AdvantageEnvelopeEngine.ConfirmHoldout(
                calibration,
                CreateHoldoutRequest(new[] { 40, 20 }));

            Assert.That(profile.SchemaVersion, Is.EqualTo(1));
            Assert.That(profile.ArtifactType, Is.EqualTo("advantage-envelope"));
            Assert.That(profile.ScenarioId, Is.EqualTo("synthetic-scenario"));
            Assert.That(profile.ContractVersion, Is.EqualTo(7));
            Assert.That(profile.FinalDecisionLocked, Is.True);
            Assert.That(profile.HoldoutCanRerank, Is.False);
            Assert.That(profile.Cells[0].SelectedCandidateId, Is.EqualTo("aos-tuned"));
            Assert.That(profile.Cells[1].SelectedCandidateId, Is.EqualTo("aos-tuned"));
            Assert.That(profile.Cells[2].SelectedCandidateId, Is.EqualTo("soa-candidate"));
            Assert.That(profile.Cells[3].SelectedCandidateId, Is.EqualTo("soa-candidate"));
            Assert.That(profile.WinnerRegions, Has.Length.EqualTo(2));
            Assert.That(
                profile.WinnerRegions[0].SampledLifetimeTicks,
                Is.EqualTo(new[] { 5, 10 }));
            Assert.That(
                profile.WinnerRegions[1].SampledLifetimeTicks,
                Is.EqualTo(new[] { 20, 40 }));

            AdvantageEnvelopeSummary summary = profile.Summary;
            Assert.That(summary.TotalCellCount, Is.EqualTo(4));
            Assert.That(summary.ValidCellCount, Is.EqualTo(4));
            Assert.That(summary.CredibleAdvantageCellCount, Is.EqualTo(2));
            Assert.That(summary.AoSFallbackCellCount, Is.EqualTo(2));
            Assert.That(summary.CredibleCoveragePercent, Is.EqualTo(50d));
            Assert.That(summary.FloorConfirmedImprovementPercent, Is.EqualTo(10d).Within(1e-9));
            Assert.That(summary.MedianConfirmedImprovementPercent, Is.EqualTo(12.5d).Within(1e-9));
            Assert.That(summary.PeakConfirmedImprovementPercent, Is.EqualTo(15d).Within(1e-9));
            Assert.That(
                summary.WorstConfirmedConfidenceLowerBoundPercent,
                Is.EqualTo(10d).Within(1e-9));
        }

        [Test]
        public void Envelope_FixedSyntheticInputSerializesIdenticallyRegardlessOfInputOrder()
        {
            AdvantageEnvelopeCalibration firstCalibration =
                AdvantageEnvelopeEngine.Calibrate(CreateEnvelopeRequest(new[] { 40, 5, 20, 10 }));
            AdvantageEnvelopeCalibration secondCalibration =
                AdvantageEnvelopeEngine.Calibrate(CreateEnvelopeRequest(new[] { 10, 20, 5, 40 }));
            AdvantageEnvelopeProfile first = AdvantageEnvelopeEngine.ConfirmHoldout(
                firstCalibration,
                CreateHoldoutRequest(new[] { 40, 20 }));
            AdvantageEnvelopeProfile second = AdvantageEnvelopeEngine.ConfirmHoldout(
                secondCalibration,
                CreateHoldoutRequest(new[] { 20, 40 }));

            string json = JsonUtility.ToJson(first);
            Assert.That(JsonUtility.ToJson(second), Is.EqualTo(json));
            Assert.That(json, Does.Contain("\"HoldoutBaselineEvidenceHash\":\"\""));

            AdvantageEnvelopeProfile restored =
                JsonUtility.FromJson<AdvantageEnvelopeProfile>(json);
            Assert.That(restored.SchemaVersion, Is.EqualTo(1));
            Assert.That(restored.FinalDecisionLocked, Is.True);
            Assert.That(restored.HoldoutCanRerank, Is.False);
            Assert.That(restored.Cells[2].SelectedCandidateId, Is.EqualTo("soa-candidate"));
            Assert.That(
                restored.Cells[2].HoldoutCandidateEvidenceHash,
                Is.EqualTo(SyntheticEvidenceSha("synthetic-holdout", 1)));
            Assert.That(
                restored.Cells[2].CandidateOutcomes[1].Candidate.CandidateId,
                Is.EqualTo("soa-candidate"));
            Assert.That(restored.WinnerRegions[1].SampledLifetimeTicks, Is.EqualTo(new[] { 20, 40 }));
        }

        [Test]
        public void Envelope_UncertainMinimumEffectBecomesGreyZoneAndSelectsTunedAoS()
        {
            AdvantageEnvelopeCalibrationRequest request = CreateEnvelopeRequest(new[] { 100 });
            DecisionCandidateEvidence noisy =
                request.Cells[0].CalibrationCandidates[1];
            noisy.ResidentP95MillisecondsPerTick = 8d;
            for (int index = 0; index < noisy.BootstrapReplicates.Length; index++)
            {
                BootstrapCostReplicate replicate = noisy.BootstrapReplicates[index];
                replicate.ResidentP95MillisecondsPerTick = index % 2 == 0 ? 8d : 12d;
                replicate.IngressP95Milliseconds = 0d;
                noisy.BootstrapReplicates[index] = replicate;
            }

            AdvantageEnvelopeCalibration calibration =
                AdvantageEnvelopeEngine.Calibrate(request);
            AdvantageEnvelopeProfile profile = AdvantageEnvelopeEngine.ConfirmHoldout(
                calibration,
                CreateHoldoutRequest(new int[0]));

            Assert.That(
                profile.Cells[0].Status,
                Is.EqualTo(EnvelopeCellStatus.StatisticalGreyZone));
            Assert.That(profile.Cells[0].SelectedCandidateId, Is.EqualTo("aos-tuned"));
            Assert.That(
                profile.Cells[0].CalibrationConfidenceInterval.LowerBoundPercent,
                Is.LessThanOrEqualTo(0d));
            Assert.That(profile.Summary.StatisticalGreyCellCount, Is.EqualTo(1));
        }

        [Test]
        public void Envelope_HoldoutCannotSubstituteAnotherCandidate()
        {
            AdvantageEnvelopeCalibration calibration = AdvantageEnvelopeEngine.Calibrate(
                CreateEnvelopeRequest(new[] { 40 }));
            AdvantageEnvelopeHoldoutRequest holdout = CreateHoldoutRequest(new[] { 40 });
            holdout.Cells[0].FrozenCandidate = CreateEvidence(
                "invented-faster-candidate",
                false,
                0.001d,
                0d,
                1,
                "synthetic-holdout");

            Assert.That(
                () => AdvantageEnvelopeEngine.ConfirmHoldout(calibration, holdout),
                Throws.ArgumentException.With.Message.Contains("substitute"));
        }

        [Test]
        public void Envelope_ParityFailureIsRecordedAndCannotWin()
        {
            AdvantageEnvelopeCalibrationRequest request = CreateEnvelopeRequest(new[] { 40 });
            request.Cells[0].CalibrationCandidates[1].ParityPassed = false;

            AdvantageEnvelopeCalibration calibration =
                AdvantageEnvelopeEngine.Calibrate(request);

            Assert.That(
                calibration.Cells[0].CalibrationStatus,
                Is.EqualTo(EnvelopeCellStatus.AoSFallback));
            Assert.That(
                calibration.Cells[0].CandidateOutcomes[1].GateStatus,
                Is.EqualTo(CandidateEvidenceGateStatus.ParityFailed));
            Assert.That(
                calibration.Cells[0].FrozenCalibrationWinnerCandidateId,
                Is.EqualTo("aos-tuned"));
        }

        [Test]
        public void Envelope_ManagedAllocationIsRecordedAndCannotWin()
        {
            AdvantageEnvelopeCalibrationRequest request = CreateEnvelopeRequest(new[] { 40 });
            request.Cells[0].CalibrationCandidates[1].HotPathManagedAllocationBytes = 16L;

            AdvantageEnvelopeCalibration calibration =
                AdvantageEnvelopeEngine.Calibrate(request);

            Assert.That(
                calibration.Cells[0].CalibrationStatus,
                Is.EqualTo(EnvelopeCellStatus.AoSFallback));
            Assert.That(
                calibration.Cells[0].CandidateOutcomes[1].GateStatus,
                Is.EqualTo(CandidateEvidenceGateStatus.ManagedAllocationDetected));
            Assert.That(
                calibration.Cells[0].FrozenCalibrationWinnerCandidateId,
                Is.EqualTo("aos-tuned"));
        }

        [Test]
        public void Envelope_MissingHoldoutRejectsProvisionalWinnerWithoutReranking()
        {
            AdvantageEnvelopeCalibration calibration = AdvantageEnvelopeEngine.Calibrate(
                CreateEnvelopeRequest(new[] { 40 }));

            AdvantageEnvelopeProfile profile = AdvantageEnvelopeEngine.ConfirmHoldout(
                calibration,
                CreateHoldoutRequest(new int[0]));

            Assert.That(
                profile.Cells[0].Status,
                Is.EqualTo(EnvelopeCellStatus.HoldoutRejected));
            Assert.That(profile.Cells[0].SelectedCandidateId, Is.EqualTo("aos-tuned"));
            Assert.That(profile.Cells[0].HoldoutConfirmed, Is.False);
            Assert.That(profile.Summary.HoldoutRejectedCellCount, Is.EqualTo(1));
        }

        [Test]
        public void Envelope_FinalProfileDoesNotAliasMutableCalibrationObjects()
        {
            AdvantageEnvelopeCalibration calibration = AdvantageEnvelopeEngine.Calibrate(
                CreateEnvelopeRequest(new[] { 40 }));
            AdvantageEnvelopeProfile profile = AdvantageEnvelopeEngine.ConfirmHoldout(
                calibration,
                CreateHoldoutRequest(new[] { 40 }));

            calibration.Policy.MinimumImprovementPercent = 99d;
            EnvelopeCandidateOutcome calibrationOutcome =
                calibration.Cells[0].CandidateOutcomes[1];
            EnvelopeCandidateDescriptor mutatedDescriptor = calibrationOutcome.Candidate;
            mutatedDescriptor.CandidateId = "mutated-after-confirmation";
            calibrationOutcome.Candidate = mutatedDescriptor;

            Assert.That(profile.Policy.MinimumImprovementPercent, Is.EqualTo(10d));
            Assert.That(profile.Cells[0].SelectedCandidateId, Is.EqualTo("soa-candidate"));
            Assert.That(
                profile.Cells[0].CandidateOutcomes[1].Candidate.CandidateId,
                Is.EqualTo("soa-candidate"));
        }

        [Test]
        public void Envelope_CalibrationRejectsMalformedCompatibilityAndEvidenceHashes()
        {
            AdvantageEnvelopeCalibrationRequest malformedIdentity =
                CreateEnvelopeRequest(new[] { 40 });
            malformedIdentity.CandidateSetHash = "ABC";

            Assert.That(
                () => AdvantageEnvelopeEngine.Calibrate(malformedIdentity),
                Throws.ArgumentException.With.Message.Contains("64 uppercase hexadecimal"));

            AdvantageEnvelopeCalibrationRequest malformedEvidence =
                CreateEnvelopeRequest(new[] { 40 });
            malformedEvidence.Cells[0].CalibrationCandidates[1].EvidenceHash =
                new string('a', 64);

            Assert.That(
                () => AdvantageEnvelopeEngine.Calibrate(malformedEvidence),
                Throws.ArgumentException.With.Message.Contains("64 uppercase hexadecimal"));
        }

        [Test]
        public void Envelope_HoldoutRejectsMalformedArtifactHashesAndEngineVersions()
        {
            AdvantageEnvelopeCalibration unsupported = AdvantageEnvelopeEngine.Calibrate(
                CreateEnvelopeRequest(new[] { 40 }));
            unsupported.DecisionEngineVersion = "2.0.0";

            Assert.That(
                () => AdvantageEnvelopeEngine.ConfirmHoldout(
                    unsupported,
                    CreateHoldoutRequest(new[] { 40 })),
                Throws.ArgumentException.With.Message.Contains("DecisionEngineVersion is unsupported"));

            AdvantageEnvelopeCalibration malformedArtifact = AdvantageEnvelopeEngine.Calibrate(
                CreateEnvelopeRequest(new[] { 40 }));
            malformedArtifact.Cells[0].CandidateOutcomes[1].SourceEvidenceHash = "BAD";

            Assert.That(
                () => AdvantageEnvelopeEngine.ConfirmHoldout(
                    malformedArtifact,
                    CreateHoldoutRequest(new[] { 40 })),
                Throws.ArgumentException.With.Message.Contains("64 uppercase hexadecimal"));

            AdvantageEnvelopeCalibration calibration = AdvantageEnvelopeEngine.Calibrate(
                CreateEnvelopeRequest(new[] { 40 }));
            AdvantageEnvelopeHoldoutRequest malformedHoldout =
                CreateHoldoutRequest(new[] { 40 });
            malformedHoldout.SourceArtifactSha256 = new string('f', 64);

            Assert.That(
                () => AdvantageEnvelopeEngine.ConfirmHoldout(calibration, malformedHoldout),
                Throws.ArgumentException.With.Message.Contains("64 uppercase hexadecimal"));

            malformedHoldout = CreateHoldoutRequest(new[] { 40 });
            malformedHoldout.Cells[0].FrozenCandidate.EvidenceHash = "BAD";

            Assert.That(
                () => AdvantageEnvelopeEngine.ConfirmHoldout(calibration, malformedHoldout),
                Throws.ArgumentException.With.Message.Contains("64 uppercase hexadecimal"));
        }

        [Test]
        public void ParetoFrontier_UsesStrictDominanceAndKeepsEqualPoints()
        {
            ParetoFrontierResult result = ParetoFrontier.Build(new[]
            {
                Metric("aos-tuned", true, 10d, 10d, 100, 0),
                Metric("frontier-a", false, 8d, 8d, 80, 1),
                Metric("frontier-equal", false, 8d, 8d, 80, 2),
                Metric("dominated", false, 9d, 9d, 90, 3),
            });

            Assert.That(
                FindPareto(result, "dominated").Status,
                Is.EqualTo(ParetoCandidateStatus.StrictlyDominated));
            Assert.That(
                FindPareto(result, "dominated").DominatedByCandidateId,
                Is.EqualTo("frontier-a"));
            Assert.That(
                FindPareto(result, "frontier-a").Status,
                Is.EqualTo(ParetoCandidateStatus.Frontier));
            Assert.That(
                FindPareto(result, "frontier-equal").Status,
                Is.EqualTo(ParetoCandidateStatus.Frontier));
        }

        [Test]
        public void AdaptiveElimination_RecordsEveryStageAndPreservesFinalEvidence()
        {
            AdaptiveEliminationPlan plan = AdaptiveEliminationEngine.CreatePlan(
                CreateAdaptiveRequest());
            AdaptiveEliminationRequest reorderedRequest = CreateAdaptiveRequest();
            Array.Reverse(reorderedRequest.Candidates);
            AdaptiveEliminationPlan reordered = AdaptiveEliminationEngine.CreatePlan(
                reorderedRequest);

            Assert.That(
                plan.Status,
                Is.EqualTo(AdaptiveEliminationPlanStatus.ReadyForFullCalibration));
            Assert.That(
                plan.FinalistCandidateIds,
                Is.EqualTo(new[] { "aos-tuned", "strong" }));
            Assert.That(
                FindAdaptive(plan, "aos-tuned").Disposition,
                Is.EqualTo(AdaptiveCandidateDisposition.ProtectedTunedAoSBaseline));
            Assert.That(
                FindAdaptive(plan, "hopeless").Stage,
                Is.EqualTo(AdaptiveEliminationStage.QuickCalibration));
            Assert.That(
                FindAdaptive(plan, "hopeless")
                    .QuickImprovementConfidenceInterval.UpperBoundPercent,
                Is.LessThan(10d));
            Assert.That(
                FindAdaptive(plan, "dominated").Stage,
                Is.EqualTo(AdaptiveEliminationStage.ParetoFrontier));
            Assert.That(
                FindAdaptive(plan, "dominated").DominatedByCandidateId,
                Is.EqualTo("strong"));
            Assert.That(
                FindAdaptive(plan, "parity-failure").GateStatus,
                Is.EqualTo(CandidateEvidenceGateStatus.ParityFailed));
            Assert.That(plan.FinalEvidenceRequirementsUnchanged, Is.True);
            Assert.That(plan.HoldoutCanRerank, Is.False);
            Assert.That(plan.RequiredFullResidentSamplesPerFinalist, Is.EqualTo(40));
            Assert.That(plan.RequiredHoldoutResidentSamples, Is.EqualTo(40));
            Assert.That(plan.PlannedFullCalibrationComponentSampleUnitsSaved, Is.GreaterThan(0));
            Assert.That(JsonUtility.ToJson(reordered), Is.EqualTo(JsonUtility.ToJson(plan)));

            AdaptiveEliminationPlan restored = JsonUtility.FromJson<AdaptiveEliminationPlan>(
                JsonUtility.ToJson(plan));
            Assert.That(restored.SchemaVersion, Is.EqualTo(1));
            Assert.That(restored.Policy.MinimumImprovementPercent, Is.EqualTo(10d));
            Assert.That(restored.FinalistCandidateIds, Is.EqualTo(new[] { "aos-tuned", "strong" }));
        }

        [Test]
        public void AdaptiveElimination_InsufficientQuickUncertaintyIsRetainedConservatively()
        {
            AdaptiveEliminationRequest request = CreateAdaptiveRequest();
            DecisionCandidateEvidence uncertain = CreateEvidence(
                "insufficient-quick",
                false,
                6d,
                100d,
                60,
                "synthetic-quick",
                sortOrder: 5,
                replicateCount: 10);
            var extended = new DecisionCandidateEvidence[request.Candidates.Length + 1];
            Array.Copy(request.Candidates, extended, request.Candidates.Length);
            extended[extended.Length - 1] = uncertain;
            request.Candidates = extended;

            AdaptiveEliminationPlan plan = AdaptiveEliminationEngine.CreatePlan(request);
            AdaptiveCandidateDecision decision = FindAdaptive(plan, "insufficient-quick");

            Assert.That(decision.Disposition, Is.EqualTo(AdaptiveCandidateDisposition.Finalist));
            Assert.That(decision.QuickConfidenceAvailable, Is.False);
            Assert.That(decision.Reason, Does.Contain("retained conservatively"));
        }

        [Test]
        public void AdaptiveElimination_RejectsMalformedCompatibilityAndEvidenceHashes()
        {
            AdaptiveEliminationRequest malformedIdentity = CreateAdaptiveRequest();
            malformedIdentity.QuickCalibrationSettingsHash = "BAD";

            Assert.That(
                () => AdaptiveEliminationEngine.CreatePlan(malformedIdentity),
                Throws.ArgumentException.With.Message.Contains("64 uppercase hexadecimal"));

            AdaptiveEliminationRequest malformedEvidence = CreateAdaptiveRequest();
            malformedEvidence.Candidates[0].EvidenceHash = new string('a', 64);

            Assert.That(
                () => AdaptiveEliminationEngine.CreatePlan(malformedEvidence),
                Throws.ArgumentException.With.Message.Contains("64 uppercase hexadecimal"));
        }

        [Test]
        public void AdaptiveAudit_MatchesExhaustiveSearchOrReportsBoundedRegret()
        {
            AdaptiveEliminationPlan plan = AdaptiveEliminationEngine.CreatePlan(
                CreateAdaptiveRequest());
            FullCalibrationScore[] equivalentScores =
            {
                Score("aos-tuned", 10d),
                Score("strong", 7d),
                Score("hopeless", 9.8d),
                Score("dominated", 9d),
            };

            AdaptiveRegretAudit equivalent = AdaptiveEliminationEngine.AuditAgainstExhaustive(
                plan,
                equivalentScores,
                maximumAllowedRegretPercent: 0d);

            Assert.That(equivalent.Valid, Is.True);
            Assert.That(equivalent.AuditOnly, Is.True);
            Assert.That(equivalent.ExactWinnerEquivalent, Is.True);
            Assert.That(equivalent.SelectionRegretPercent, Is.Zero.Within(1e-12));

            FullCalibrationScore[] shiftedScores =
            {
                Score("aos-tuned", 10d),
                Score("strong", 7d),
                Score("hopeless", 6d),
                Score("dominated", 9d),
            };
            AdaptiveRegretAudit bounded = AdaptiveEliminationEngine.AuditAgainstExhaustive(
                plan,
                shiftedScores,
                maximumAllowedRegretPercent: 17d);
            AdaptiveRegretAudit exceeded = AdaptiveEliminationEngine.AuditAgainstExhaustive(
                plan,
                shiftedScores,
                maximumAllowedRegretPercent: 10d);

            Assert.That(bounded.ExactWinnerEquivalent, Is.False);
            Assert.That(bounded.SelectionRegretPercent, Is.EqualTo(100d / 6d).Within(1e-9));
            Assert.That(bounded.WithinRegretBound, Is.True);
            Assert.That(exceeded.WithinRegretBound, Is.False);
        }

        private static AdvantageEnvelopeCalibrationRequest CreateEnvelopeRequest(int[] lifetimes)
        {
            var cells = new AdvantageEnvelopeCellInput[lifetimes.Length];
            for (int index = 0; index < lifetimes.Length; index++)
            {
                cells[index] = new AdvantageEnvelopeCellInput
                {
                    Axis = Axis(lifetimes[index]),
                    CalibrationCandidates = new[]
                    {
                        CreateEvidence(
                            "aos-tuned",
                            true,
                            10d,
                            0d,
                            100,
                            "synthetic-calibration",
                            sortOrder: 0),
                        CreateEvidence(
                            "soa-candidate",
                            false,
                            8d,
                            20d,
                            80,
                            "synthetic-calibration",
                            sortOrder: 1),
                    },
                };
            }
            return new AdvantageEnvelopeCalibrationRequest
            {
                EnvelopeId = "synthetic-envelope",
                CreatedUtcIso8601 = "2026-09-02T00:00:00Z",
                ScenarioId = "synthetic-scenario",
                ContractVersion = 7,
                CandidateSetHash = SyntheticSha('A'),
                MeasurementSchemaHash = SyntheticSha('B'),
                EnvironmentFingerprint = SyntheticSha('C'),
                CalibrationSettingsHash = SyntheticSha('D'),
                SourceArtifactId = "synthetic-calibration-artifact",
                SourceArtifactSha256 = SyntheticSha('E'),
                EvidenceScope = "synthetic-test-fixture",
                CalibrationUncertaintyMethod = "synthetic-aligned-bootstrap-replicates",
                Policy = new AdvantageEnvelopePolicy
                {
                    MinimumBootstrapReplicates = 100,
                    MinimumCalibrationResidentSamples = 3,
                    MinimumCalibrationBoundarySamples = 3,
                    MinimumHoldoutResidentSamples = 3,
                    MinimumHoldoutBoundarySamples = 3,
                },
                Cells = cells,
            };
        }

        private static AdvantageEnvelopeHoldoutRequest CreateHoldoutRequest(int[] lifetimes)
        {
            var cells = new AdvantageEnvelopeHoldoutCellInput[lifetimes.Length];
            for (int index = 0; index < lifetimes.Length; index++)
            {
                cells[index] = new AdvantageEnvelopeHoldoutCellInput
                {
                    Axis = Axis(lifetimes[index]),
                    Baseline = CreateEvidence(
                        "aos-tuned",
                        true,
                        10d,
                        0d,
                        100,
                        "synthetic-holdout",
                        sortOrder: 0),
                    FrozenCandidate = CreateEvidence(
                        "soa-candidate",
                        false,
                        8d,
                        20d,
                        80,
                        "synthetic-holdout",
                        sortOrder: 1),
                };
            }
            return new AdvantageEnvelopeHoldoutRequest
            {
                SourceArtifactId = "synthetic-holdout-artifact",
                SourceArtifactSha256 = SyntheticSha('F'),
                CandidateSetHash = SyntheticSha('A'),
                MeasurementSchemaHash = SyntheticSha('B'),
                EnvironmentFingerprint = SyntheticSha('C'),
                HoldoutSettingsHash = SyntheticSha('0'),
                EvidenceScope = "synthetic-test-fixture",
                HoldoutUncertaintyMethod = "synthetic-aligned-bootstrap-replicates",
                Cells = cells,
            };
        }

        private static AdaptiveEliminationRequest CreateAdaptiveRequest()
        {
            DecisionCandidateEvidence parityFailure = CreateEvidence(
                "parity-failure",
                false,
                1d,
                0d,
                10,
                "synthetic-quick",
                sortOrder: 4);
            parityFailure.ParityPassed = false;
            return new AdaptiveEliminationRequest
            {
                SearchId = "synthetic-adaptive-search",
                CreatedUtcIso8601 = "2026-09-02T00:00:00Z",
                ScenarioId = "synthetic-scenario",
                ContractVersion = 7,
                CandidateSetHash = SyntheticSha('A'),
                MeasurementSchemaHash = SyntheticSha('B'),
                EnvironmentFingerprint = SyntheticSha('C'),
                QuickCalibrationSettingsHash = SyntheticSha('1'),
                SourceArtifactId = "synthetic-quick-artifact",
                SourceArtifactSha256 = SyntheticSha('2'),
                CalibrationPartitionId = "synthetic-quick",
                PlannedHoldoutPartitionId = "synthetic-holdout",
                EvidenceScope = "synthetic-test-fixture",
                QuickUncertaintyMethod = "synthetic-aligned-bootstrap-replicates",
                Axis = Axis(10),
                Policy = new AdaptiveEliminationPolicy(),
                Candidates = new[]
                {
                    CreateEvidence(
                        "dominated",
                        false,
                        8d,
                        10d,
                        90,
                        "synthetic-quick",
                        sortOrder: 3),
                    CreateEvidence(
                        "hopeless",
                        false,
                        9.8d,
                        0d,
                        70,
                        "synthetic-quick",
                        sortOrder: 2),
                    parityFailure,
                    CreateEvidence(
                        "strong",
                        false,
                        7d,
                        0d,
                        80,
                        "synthetic-quick",
                        sortOrder: 1),
                    CreateEvidence(
                        "aos-tuned",
                        true,
                        10d,
                        0d,
                        100,
                        "synthetic-quick",
                        sortOrder: 0),
                },
            };
        }

        private static DecisionCandidateEvidence CreateEvidence(
            string candidateId,
            bool baseline,
            double residentP95,
            double boundaryP95,
            long residentBytes,
            string partitionId,
            int sortOrder = 1,
            int replicateCount = 100)
        {
            var replicates = new BootstrapCostReplicate[replicateCount];
            for (int index = 0; index < replicates.Length; index++)
            {
                replicates[index] = new BootstrapCostReplicate
                {
                    ReplicateId = index,
                    ResidentP95MillisecondsPerTick = residentP95,
                    IngressP95Milliseconds = boundaryP95,
                    ExportP95Milliseconds = 0d,
                };
            }
            return new DecisionCandidateEvidence
            {
                Candidate = Descriptor(candidateId, baseline, sortOrder),
                Completed = true,
                ContractFeasible = true,
                MemoryFeasible = true,
                ParityPassed = true,
                ResidentBytes = residentBytes,
                ResidentP95MillisecondsPerTick = residentP95,
                IngressP95Milliseconds = boundaryP95,
                ExportP95Milliseconds = 0d,
                ResidentSampleCount = 5,
                BoundarySampleCount = 5,
                EvidencePartitionId = partitionId,
                EvidenceHash = SyntheticEvidenceSha(partitionId, sortOrder),
                BootstrapReplicates = replicates,
            };
        }

        private static string SyntheticEvidenceSha(string partitionId, int sortOrder)
        {
            int offset;
            if (string.Equals(partitionId, "synthetic-calibration", StringComparison.Ordinal))
                offset = 4;
            else if (string.Equals(partitionId, "synthetic-holdout", StringComparison.Ordinal))
                offset = 6;
            else
                offset = 8;
            const string hexadecimal = "0123456789ABCDEF";
            return SyntheticSha(hexadecimal[(offset + sortOrder) % hexadecimal.Length]);
        }

        private static string SyntheticSha(char character)
        {
            return new string(character, 64);
        }

        private static EnvelopeCandidateDescriptor Descriptor(
            string candidateId,
            bool baseline,
            int sortOrder)
        {
            return new EnvelopeCandidateDescriptor(
                candidateId,
                baseline ? "aos-layout-v1" : "soa-layout-v1",
                "synthetic-kernel-v1",
                "batch-64-v1",
                ExecutionPolicy,
                64,
                baseline,
                sortOrder,
                candidateId);
        }

        private static AdvantageEnvelopeAxis Axis(int lifetimeTicks)
        {
            return new AdvantageEnvelopeAxis(
                65536,
                lifetimeTicks,
                3d,
                4,
                ExecutionPolicy);
        }

        private static ParetoCandidateMetric Metric(
            string candidateId,
            bool baseline,
            double resident,
            double boundary,
            long residentBytes,
            int sortOrder)
        {
            return new ParetoCandidateMetric
            {
                Candidate = Descriptor(candidateId, baseline, sortOrder),
                Feasible = true,
                ResidentCostMillisecondsPerTick = resident,
                BoundaryCostMilliseconds = boundary,
                ResidentBytes = residentBytes,
            };
        }

        private static ParetoCandidateDecision FindPareto(
            ParetoFrontierResult result,
            string candidateId)
        {
            for (int index = 0; index < result.Candidates.Length; index++)
            {
                if (result.Candidates[index].Candidate.CandidateId == candidateId)
                    return result.Candidates[index];
            }
            throw new InvalidOperationException("Synthetic Pareto candidate was not found.");
        }

        private static AdaptiveCandidateDecision FindAdaptive(
            AdaptiveEliminationPlan plan,
            string candidateId)
        {
            for (int index = 0; index < plan.CandidateDecisions.Length; index++)
            {
                if (plan.CandidateDecisions[index].Candidate.CandidateId == candidateId)
                    return plan.CandidateDecisions[index];
            }
            throw new InvalidOperationException("Synthetic adaptive candidate was not found.");
        }

        private static FullCalibrationScore Score(string candidateId, double cost)
        {
            return new FullCalibrationScore
            {
                CandidateId = candidateId,
                Eligible = true,
                AmortizedP95MillisecondsPerTick = cost,
            };
        }
    }
}
