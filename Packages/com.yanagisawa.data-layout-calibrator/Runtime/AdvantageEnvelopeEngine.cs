using System;
using System.Collections.Generic;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>
    /// Builds a deterministic, uncertainty-aware advantage envelope in two
    /// strictly separated phases. Calibrate never receives holdout evidence;
    /// ConfirmHoldout can only confirm the already-frozen candidate or fall back
    /// to tuned AoS.
    /// </summary>
    public static class AdvantageEnvelopeEngine
    {
        public const string Version = "1.0.0";

        public static BreakEvenKind ClassifyBreakEven(
            double baselineResidentP95MillisecondsPerTick,
            double baselineBoundaryP95Milliseconds,
            double candidateResidentP95MillisecondsPerTick,
            double candidateBoundaryP95Milliseconds,
            out double crossingLifetimeTicks)
        {
            return DecisionEvidenceStatistics.ClassifyBreakEven(
                baselineResidentP95MillisecondsPerTick,
                baselineBoundaryP95Milliseconds,
                candidateResidentP95MillisecondsPerTick,
                candidateBoundaryP95Milliseconds,
                out crossingLifetimeTicks);
        }

        public static BreakEvenEstimate CalculateBreakEven(
            DecisionCandidateEvidence baseline,
            DecisionCandidateEvidence candidate,
            double confidenceLevel = 0.95d)
        {
            return DecisionEvidenceStatistics.CalculateBreakEven(
                baseline,
                candidate,
                confidenceLevel);
        }

        public static AdvantageEnvelopeCalibration Calibrate(
            AdvantageEnvelopeCalibrationRequest request)
        {
            ValidateCalibrationRequest(request);
            AdvantageEnvelopePolicy policy = ClonePolicy(request.Policy);
            AdvantageEnvelopeCellInput[] inputs = new AdvantageEnvelopeCellInput[request.Cells.Length];
            Array.Copy(request.Cells, inputs, inputs.Length);
            Array.Sort(inputs, CompareCellInputs);
            RejectDuplicateAxes(inputs);

            var cells = new EnvelopeCalibrationCellDecision[inputs.Length];
            for (int index = 0; index < inputs.Length; index++)
                cells[index] = CalibrateCell(inputs[index], policy);

            return new AdvantageEnvelopeCalibration
            {
                SchemaVersion = 1,
                ArtifactType = "advantage-envelope-calibration",
                DecisionEngineVersion = Version,
                EnvelopeId = request.EnvelopeId,
                CreatedUtcIso8601 = request.CreatedUtcIso8601,
                ScenarioId = request.ScenarioId,
                ContractVersion = request.ContractVersion,
                CandidateSetHash = request.CandidateSetHash,
                MeasurementSchemaHash = request.MeasurementSchemaHash,
                EnvironmentFingerprint = request.EnvironmentFingerprint,
                CalibrationSettingsHash = request.CalibrationSettingsHash,
                CalibrationSourceArtifactId = request.SourceArtifactId,
                CalibrationSourceArtifactSha256 = request.SourceArtifactSha256,
                EvidenceScope = request.EvidenceScope,
                CalibrationUncertaintyMethod = request.CalibrationUncertaintyMethod,
                Policy = policy,
                HoldoutWasRead = false,
                Cells = cells,
            };
        }

        public static AdvantageEnvelopeProfile ConfirmHoldout(
            AdvantageEnvelopeCalibration calibration,
            AdvantageEnvelopeHoldoutRequest holdout)
        {
            ValidateCalibrationArtifact(calibration);
            ValidateHoldoutRequest(calibration, holdout);

            AdvantageEnvelopeHoldoutCellInput[] holdoutCells = holdout.Cells == null
                ? new AdvantageEnvelopeHoldoutCellInput[0]
                : CloneAndSortHoldoutCells(holdout.Cells);
            RejectDuplicateHoldoutAxes(holdoutCells);
            RejectUnfrozenHoldoutCells(calibration.Cells, holdoutCells);

            var finalCells = new EnvelopeCellDecision[calibration.Cells.Length];
            for (int index = 0; index < calibration.Cells.Length; index++)
            {
                EnvelopeCalibrationCellDecision source = calibration.Cells[index];
                AdvantageEnvelopeHoldoutCellInput input = FindHoldoutCell(
                    holdoutCells,
                    source.Axis);
                finalCells[index] = ConfirmCell(source, input, calibration.Policy);
            }

            Array.Sort(finalCells, CompareFinalCells);
            return new AdvantageEnvelopeProfile
            {
                SchemaVersion = 1,
                ArtifactType = "advantage-envelope",
                DecisionEngineVersion = Version,
                EnvelopeId = calibration.EnvelopeId,
                CreatedUtcIso8601 = calibration.CreatedUtcIso8601,
                ScenarioId = calibration.ScenarioId,
                ContractVersion = calibration.ContractVersion,
                CandidateSetHash = calibration.CandidateSetHash,
                MeasurementSchemaHash = calibration.MeasurementSchemaHash,
                EnvironmentFingerprint = calibration.EnvironmentFingerprint,
                CalibrationSettingsHash = calibration.CalibrationSettingsHash,
                HoldoutSettingsHash = holdout.HoldoutSettingsHash,
                CalibrationSourceArtifactId = calibration.CalibrationSourceArtifactId,
                CalibrationSourceArtifactSha256 = calibration.CalibrationSourceArtifactSha256,
                HoldoutSourceArtifactId = holdout.SourceArtifactId,
                HoldoutSourceArtifactSha256 = holdout.SourceArtifactSha256,
                EvidenceScope = calibration.EvidenceScope,
                CalibrationUncertaintyMethod = calibration.CalibrationUncertaintyMethod,
                HoldoutUncertaintyMethod = holdout.HoldoutUncertaintyMethod,
                Policy = ClonePolicy(calibration.Policy),
                FinalDecisionLocked = true,
                HoldoutCanRerank = false,
                Cells = finalCells,
                WinnerRegions = BuildWinnerRegions(finalCells),
                Summary = BuildSummary(finalCells),
            };
        }

        private static EnvelopeCalibrationCellDecision CalibrateCell(
            AdvantageEnvelopeCellInput input,
            AdvantageEnvelopePolicy policy)
        {
            if (input == null)
                throw new ArgumentException("Envelope cells must not be null.", nameof(input));
            ValidateAxis(input.Axis);
            if (input.CalibrationCandidates == null || input.CalibrationCandidates.Length == 0)
            {
                throw new ArgumentException(
                    "Every envelope cell requires calibration candidates.",
                    nameof(input));
            }

            DecisionCandidateEvidence[] candidates = new DecisionCandidateEvidence[
                input.CalibrationCandidates.Length];
            Array.Copy(input.CalibrationCandidates, candidates, candidates.Length);
            ValidateAndSortCandidates(candidates);

            DecisionCandidateEvidence baseline = null;
            int baselineCount = 0;
            for (int index = 0; index < candidates.Length; index++)
            {
                if (candidates[index].Candidate.IsTunedAoSBaseline)
                {
                    baseline = candidates[index];
                    baselineCount++;
                }
            }
            if (baselineCount != 1)
            {
                throw new ArgumentException(
                    "Every cell requires exactly one explicitly marked tuned AoS baseline.",
                    nameof(input));
            }

            string partitionId = baseline.EvidencePartitionId;
            CandidateEvidenceGateStatus baselineGate = DecisionEvidenceStatistics.EvaluateGate(
                baseline,
                policy.MinimumCalibrationResidentSamples,
                policy.MinimumCalibrationBoundarySamples,
                policy.MinimumBootstrapReplicates,
                partitionId,
                out string baselineReason);
            if (!string.Equals(
                    baseline.Candidate.ExecutionPolicyId,
                    input.Axis.ExecutionPolicyId,
                    StringComparison.Ordinal))
            {
                baselineGate = CandidateEvidenceGateStatus.ContractInfeasible;
                baselineReason = "The tuned AoS execution policy does not match the cell axis.";
            }

            bool baselineEligible = baselineGate == CandidateEvidenceGateStatus.Eligible;
            var outcomes = new EnvelopeCandidateOutcome[candidates.Length];
            for (int index = 0; index < candidates.Length; index++)
            {
                DecisionCandidateEvidence candidate = candidates[index];
                CandidateEvidenceGateStatus gate = DecisionEvidenceStatistics.EvaluateGate(
                    candidate,
                    policy.MinimumCalibrationResidentSamples,
                    policy.MinimumCalibrationBoundarySamples,
                    policy.MinimumBootstrapReplicates,
                    partitionId,
                    out string reason);
                if (!string.Equals(
                        candidate.Candidate.ExecutionPolicyId,
                        input.Axis.ExecutionPolicyId,
                        StringComparison.Ordinal))
                {
                    gate = CandidateEvidenceGateStatus.ContractInfeasible;
                    reason = "The candidate execution policy does not match the cell axis.";
                }
                if (candidate.Candidate.IsTunedAoSBaseline)
                {
                    gate = baselineGate;
                    reason = baselineReason;
                }
                else if (!baselineEligible && gate == CandidateEvidenceGateStatus.Eligible)
                {
                    gate = CandidateEvidenceGateStatus.InvalidUncertaintyEvidence;
                    reason = "The candidate cannot be compared because tuned AoS evidence is invalid.";
                }

                outcomes[index] = CreateCalibrationOutcome(
                    baseline,
                    candidate,
                    input.Axis.LifetimeTicks,
                    policy,
                    gate,
                    reason);
            }

            EnvelopeCandidateOutcome baselineOutcome = FindOutcome(
                outcomes,
                baseline.Candidate.CandidateId);
            if (!baselineEligible || baselineOutcome == null || !baselineOutcome.Eligible)
            {
                return new EnvelopeCalibrationCellDecision
                {
                    Axis = input.Axis,
                    CalibrationStatus = EnvelopeCellStatus.Invalid,
                    CalibrationPartitionId = partitionId,
                    BaselineCandidateId = baseline.Candidate.CandidateId,
                    BestMeasuredCandidateId = baseline.Candidate.CandidateId,
                    FrozenCalibrationWinnerCandidateId = baseline.Candidate.CandidateId,
                    MinimumRequiredImprovementPercent = policy.MinimumImprovementPercent,
                    CandidateOutcomes = outcomes,
                    Reason = "Tuned AoS evidence failed a mandatory gate: " + baselineReason,
                };
            }

            EnvelopeCandidateOutcome bestMeasured = baselineOutcome;
            EnvelopeCandidateOutcome credibleWinner = null;
            bool hasGreyZoneCandidate = false;
            for (int index = 0; index < outcomes.Length; index++)
            {
                EnvelopeCandidateOutcome outcome = outcomes[index];
                if (!outcome.Eligible)
                    continue;
                if (IsBetterOutcome(outcome, bestMeasured))
                    bestMeasured = outcome;
                if (outcome.Candidate.IsTunedAoSBaseline)
                    continue;
                if (outcome.CredibleCalibrationAdvantage &&
                    (credibleWinner == null || IsBetterOutcome(outcome, credibleWinner)))
                {
                    credibleWinner = outcome;
                }
                if (outcome.MeetsMinimumEffect && !outcome.ClearsConfidenceGate)
                    hasGreyZoneCandidate = true;
            }

            EnvelopeCellStatus status;
            EnvelopeCandidateOutcome frozenWinner;
            string decisionReason;
            if (credibleWinner != null)
            {
                status = EnvelopeCellStatus.CredibleAdvantage;
                frozenWinner = credibleWinner;
                decisionReason =
                    "The calibration candidate cleared feasibility, minimum-effect, and confidence gates; only this frozen candidate may enter holdout.";
            }
            else if (hasGreyZoneCandidate)
            {
                status = EnvelopeCellStatus.StatisticalGreyZone;
                frozenWinner = baselineOutcome;
                decisionReason =
                    "A non-AoS point estimate cleared the minimum effect, but uncertainty included no advantage; tuned AoS remains selected.";
            }
            else
            {
                status = EnvelopeCellStatus.AoSFallback;
                frozenWinner = baselineOutcome;
                decisionReason =
                    "No eligible non-AoS candidate cleared the frozen minimum-effect and confidence gates; tuned AoS remains selected.";
            }

            return new EnvelopeCalibrationCellDecision
            {
                Axis = input.Axis,
                CalibrationStatus = status,
                CalibrationPartitionId = partitionId,
                BaselineCandidateId = baseline.Candidate.CandidateId,
                BestMeasuredCandidateId = bestMeasured.Candidate.CandidateId,
                FrozenCalibrationWinnerCandidateId = frozenWinner.Candidate.CandidateId,
                MinimumRequiredImprovementPercent = policy.MinimumImprovementPercent,
                CalibrationImprovementPercent = bestMeasured.ImprovementPercent,
                CalibrationConfidenceInterval = bestMeasured.ImprovementConfidenceInterval,
                CandidateOutcomes = outcomes,
                Reason = decisionReason,
            };
        }

        private static EnvelopeCandidateOutcome CreateCalibrationOutcome(
            DecisionCandidateEvidence baseline,
            DecisionCandidateEvidence candidate,
            int lifetimeTicks,
            AdvantageEnvelopePolicy policy,
            CandidateEvidenceGateStatus gate,
            string gateReason)
        {
            var outcome = new EnvelopeCandidateOutcome
            {
                Candidate = candidate.Candidate,
                GateStatus = gate,
                Eligible = gate == CandidateEvidenceGateStatus.Eligible,
                GateReason = gateReason,
                EvidencePartitionId = candidate.EvidencePartitionId,
                SourceEvidenceHash = candidate.EvidenceHash,
                ResidentSampleCount = candidate.ResidentSampleCount,
                BoundarySampleCount = candidate.BoundarySampleCount,
                BootstrapReplicateCount = candidate.BootstrapReplicates == null
                    ? 0
                    : candidate.BootstrapReplicates.Length,
                ResidentBytes = candidate.ResidentBytes,
                ResidentP95MillisecondsPerTick = candidate.ResidentP95MillisecondsPerTick,
                BoundaryP95Milliseconds = candidate.IngressP95Milliseconds +
                                          candidate.ExportP95Milliseconds,
            };
            if (!outcome.Eligible)
                return outcome;

            try
            {
                outcome.AmortizedP95MillisecondsPerTick =
                    DecisionEvidenceStatistics.AmortizedCost(candidate, lifetimeTicks);
                if (candidate.Candidate.IsTunedAoSBaseline)
                {
                    outcome.ImprovementConfidenceInterval = new EnvelopeConfidenceInterval
                    {
                        ReplicateCount = candidate.BootstrapReplicates.Length,
                        ConfidenceLevel = policy.ConfidenceLevel,
                    };
                    outcome.BreakEven = DecisionEvidenceStatistics.CalculateBreakEven(
                        baseline,
                        candidate,
                        policy.ConfidenceLevel);
                    outcome.ClearsConfidenceGate = true;
                    return outcome;
                }

                EnvelopeConfidenceInterval interval =
                    DecisionEvidenceStatistics.CalculateImprovementInterval(
                        baseline,
                        candidate,
                        lifetimeTicks,
                        policy.ConfidenceLevel);
                outcome.ImprovementPercent = interval.PointEstimatePercent;
                outcome.ImprovementConfidenceInterval = interval;
                outcome.BreakEven = DecisionEvidenceStatistics.CalculateBreakEven(
                    baseline,
                    candidate,
                    policy.ConfidenceLevel);
                outcome.MeetsMinimumEffect =
                    interval.PointEstimatePercent >= policy.MinimumImprovementPercent;
                outcome.ClearsConfidenceGate = interval.LowerBoundPercent > 0d;
                outcome.CredibleCalibrationAdvantage =
                    outcome.MeetsMinimumEffect && outcome.ClearsConfidenceGate;
                return outcome;
            }
            catch (ArgumentException)
            {
                outcome.GateStatus = CandidateEvidenceGateStatus.InvalidUncertaintyEvidence;
                outcome.Eligible = false;
                outcome.GateReason =
                    "Candidate uncertainty evidence is invalid or not aligned with tuned AoS.";
                outcome.MeetsMinimumEffect = false;
                outcome.ClearsConfidenceGate = false;
                outcome.CredibleCalibrationAdvantage = false;
                return outcome;
            }
        }

        private static EnvelopeCellDecision ConfirmCell(
            EnvelopeCalibrationCellDecision calibration,
            AdvantageEnvelopeHoldoutCellInput holdout,
            AdvantageEnvelopePolicy policy)
        {
            var result = new EnvelopeCellDecision
            {
                Axis = calibration.Axis,
                Status = calibration.CalibrationStatus,
                CalibrationPartitionId = calibration.CalibrationPartitionId,
                HoldoutPartitionId = string.Empty,
                HoldoutBaselineEvidenceHash = string.Empty,
                HoldoutCandidateEvidenceHash = string.Empty,
                BaselineCandidateId = calibration.BaselineCandidateId,
                BestMeasuredCandidateId = calibration.BestMeasuredCandidateId,
                FrozenCalibrationWinnerCandidateId = calibration.FrozenCalibrationWinnerCandidateId,
                SelectedCandidateId = calibration.BaselineCandidateId,
                MinimumRequiredImprovementPercent = calibration.MinimumRequiredImprovementPercent,
                CalibrationImprovementPercent = calibration.CalibrationImprovementPercent,
                CalibrationConfidenceInterval = calibration.CalibrationConfidenceInterval,
                CandidateOutcomes = CloneOutcomes(calibration.CandidateOutcomes),
                Reason = calibration.Reason,
            };

            if (calibration.CalibrationStatus != EnvelopeCellStatus.CredibleAdvantage)
                return result;
            if (holdout == null)
            {
                result.Status = EnvelopeCellStatus.HoldoutRejected;
                result.Reason =
                    "No unused holdout pair was supplied for the frozen calibration winner; tuned AoS is selected.";
                return result;
            }

            ValidateHoldoutIdentity(calibration, holdout);
            result.HoldoutPartitionId = holdout.Baseline.EvidencePartitionId;
            result.HoldoutBaselineEvidenceHash = holdout.Baseline.EvidenceHash;
            result.HoldoutCandidateEvidenceHash = holdout.FrozenCandidate.EvidenceHash;
            if (string.Equals(
                    result.CalibrationPartitionId,
                    result.HoldoutPartitionId,
                    StringComparison.Ordinal))
            {
                result.Status = EnvelopeCellStatus.HoldoutRejected;
                result.Reason =
                    "Calibration and holdout partition IDs are identical; holdout is not independent and tuned AoS is selected.";
                return result;
            }

            CandidateEvidenceGateStatus baselineGate = DecisionEvidenceStatistics.EvaluateGate(
                holdout.Baseline,
                policy.MinimumHoldoutResidentSamples,
                policy.MinimumHoldoutBoundarySamples,
                policy.MinimumBootstrapReplicates,
                result.HoldoutPartitionId,
                out string baselineReason);
            CandidateEvidenceGateStatus candidateGate = DecisionEvidenceStatistics.EvaluateGate(
                holdout.FrozenCandidate,
                policy.MinimumHoldoutResidentSamples,
                policy.MinimumHoldoutBoundarySamples,
                policy.MinimumBootstrapReplicates,
                result.HoldoutPartitionId,
                out string candidateReason);
            if (baselineGate != CandidateEvidenceGateStatus.Eligible)
            {
                result.Status = EnvelopeCellStatus.HoldoutRejected;
                result.Reason = "Holdout tuned AoS evidence failed: " + baselineReason;
                return result;
            }
            if (candidateGate != CandidateEvidenceGateStatus.Eligible)
            {
                result.Status = EnvelopeCellStatus.HoldoutRejected;
                result.Reason = "The frozen candidate failed holdout evidence gates: " + candidateReason;
                return result;
            }

            try
            {
                EnvelopeConfidenceInterval interval =
                    DecisionEvidenceStatistics.CalculateImprovementInterval(
                        holdout.Baseline,
                        holdout.FrozenCandidate,
                        calibration.Axis.LifetimeTicks,
                        policy.ConfidenceLevel);
                result.HoldoutImprovementPercent = interval.PointEstimatePercent;
                result.HoldoutConfidenceInterval = interval;
                if (interval.PointEstimatePercent < policy.MinimumImprovementPercent)
                {
                    result.Status = EnvelopeCellStatus.HoldoutRejected;
                    result.Reason =
                        "The frozen candidate did not repeat the minimum effect on unused holdout evidence; tuned AoS is selected.";
                    return result;
                }
                if (interval.LowerBoundPercent <= 0d)
                {
                    result.Status = EnvelopeCellStatus.HoldoutRejected;
                    result.Reason =
                        "The frozen candidate's holdout confidence interval includes no advantage; tuned AoS is selected.";
                    return result;
                }

                result.Status = EnvelopeCellStatus.CredibleAdvantage;
                result.SelectedCandidateId = calibration.FrozenCalibrationWinnerCandidateId;
                result.HoldoutConfirmed = true;
                result.Reason =
                    "The frozen calibration winner repeated minimum-effect and confidence gates on an unused holdout partition.";
                return result;
            }
            catch (ArgumentException)
            {
                result.Status = EnvelopeCellStatus.HoldoutRejected;
                result.Reason =
                    "Holdout uncertainty evidence is invalid or unaligned; tuned AoS is selected.";
                return result;
            }
        }

        private static void ValidateHoldoutIdentity(
            EnvelopeCalibrationCellDecision calibration,
            AdvantageEnvelopeHoldoutCellInput holdout)
        {
            if (holdout.Baseline == null || holdout.FrozenCandidate == null)
                throw new ArgumentException("Holdout cells require tuned AoS and the frozen candidate.");
            RequireSha256(
                holdout.Baseline.EvidenceHash,
                nameof(DecisionCandidateEvidence.EvidenceHash));
            RequireSha256(
                holdout.FrozenCandidate.EvidenceHash,
                nameof(DecisionCandidateEvidence.EvidenceHash));
            if (!DecisionEvidenceStatistics.HasValidCandidateDescriptor(
                    holdout.Baseline.Candidate,
                    out string baselineDescriptorReason))
            {
                throw new ArgumentException(
                    "Holdout tuned AoS descriptor is invalid. " + baselineDescriptorReason);
            }
            if (!DecisionEvidenceStatistics.HasValidCandidateDescriptor(
                    holdout.FrozenCandidate.Candidate,
                    out string candidateDescriptorReason))
            {
                throw new ArgumentException(
                    "Holdout frozen-candidate descriptor is invalid. " +
                    candidateDescriptorReason);
            }
            if (!string.Equals(
                    holdout.Baseline.Candidate.CandidateId,
                    calibration.BaselineCandidateId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Holdout baseline CandidateId differs from the frozen tuned AoS CandidateId.");
            }
            if (!string.Equals(
                    holdout.FrozenCandidate.Candidate.CandidateId,
                    calibration.FrozenCalibrationWinnerCandidateId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Holdout attempted to substitute a candidate other than the frozen calibration winner.");
            }
            if (!holdout.Baseline.Candidate.IsTunedAoSBaseline ||
                holdout.FrozenCandidate.Candidate.IsTunedAoSBaseline)
            {
                throw new ArgumentException("Holdout candidate roles do not match the frozen decision.");
            }

            EnvelopeCandidateOutcome expectedBaseline = FindOutcome(
                calibration.CandidateOutcomes,
                calibration.BaselineCandidateId);
            EnvelopeCandidateOutcome expectedCandidate = FindOutcome(
                calibration.CandidateOutcomes,
                calibration.FrozenCalibrationWinnerCandidateId);
            if (expectedBaseline == null || expectedCandidate == null ||
                !DecisionEvidenceStatistics.DescriptorsMatch(
                    expectedBaseline.Candidate,
                    holdout.Baseline.Candidate) ||
                !DecisionEvidenceStatistics.DescriptorsMatch(
                    expectedCandidate.Candidate,
                    holdout.FrozenCandidate.Candidate))
            {
                throw new ArgumentException(
                    "Holdout candidate definitions differ from the frozen calibration descriptors.");
            }
            if (!string.Equals(
                    holdout.Baseline.EvidencePartitionId,
                    holdout.FrozenCandidate.EvidencePartitionId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Holdout tuned AoS and candidate evidence must share one partition ID.");
            }
        }

        private static EnvelopeWinnerRegion[] BuildWinnerRegions(EnvelopeCellDecision[] cells)
        {
            if (cells.Length == 0)
                return new EnvelopeWinnerRegion[0];
            var regions = new List<EnvelopeWinnerRegion>();
            AdvantageEnvelopeAxis currentAxis = cells[0].Axis;
            EnvelopeCellStatus currentStatus = cells[0].Status;
            string currentCandidate = cells[0].SelectedCandidateId;
            var lifetimes = new List<int> { cells[0].Axis.LifetimeTicks };

            for (int index = 1; index < cells.Length; index++)
            {
                EnvelopeCellDecision cell = cells[index];
                bool sameRegion = DecisionEvidenceStatistics.SameRegionAxes(
                                      currentAxis,
                                      cell.Axis) &&
                                  currentStatus == cell.Status &&
                                  string.Equals(
                                      currentCandidate,
                                      cell.SelectedCandidateId,
                                      StringComparison.Ordinal);
                if (!sameRegion)
                {
                    regions.Add(CreateWinnerRegion(
                        currentAxis,
                        currentStatus,
                        currentCandidate,
                        lifetimes));
                    currentAxis = cell.Axis;
                    currentStatus = cell.Status;
                    currentCandidate = cell.SelectedCandidateId;
                    lifetimes = new List<int>();
                }
                lifetimes.Add(cell.Axis.LifetimeTicks);
            }

            regions.Add(CreateWinnerRegion(
                currentAxis,
                currentStatus,
                currentCandidate,
                lifetimes));
            return regions.ToArray();
        }

        private static EnvelopeWinnerRegion CreateWinnerRegion(
            AdvantageEnvelopeAxis axis,
            EnvelopeCellStatus status,
            string selectedCandidateId,
            List<int> lifetimes)
        {
            int[] sampled = lifetimes.ToArray();
            return new EnvelopeWinnerRegion
            {
                ElementCount = axis.ElementCount,
                HotToColdRatio = axis.HotToColdRatio,
                WorkerCount = axis.WorkerCount,
                ExecutionPolicyId = axis.ExecutionPolicyId,
                MinimumSampledLifetimeTicks = sampled[0],
                MaximumSampledLifetimeTicks = sampled[sampled.Length - 1],
                SampledLifetimeTicks = sampled,
                Status = status,
                SelectedCandidateId = selectedCandidateId,
            };
        }

        private static AdvantageEnvelopeSummary BuildSummary(EnvelopeCellDecision[] cells)
        {
            var confirmedImprovements = new List<double>();
            var confirmedLowerBounds = new List<double>();
            var summary = new AdvantageEnvelopeSummary
            {
                TotalCellCount = cells.Length,
            };
            for (int index = 0; index < cells.Length; index++)
            {
                EnvelopeCellDecision cell = cells[index];
                if (cell.Status != EnvelopeCellStatus.Invalid)
                    summary.ValidCellCount++;
                switch (cell.Status)
                {
                    case EnvelopeCellStatus.CredibleAdvantage:
                        summary.CredibleAdvantageCellCount++;
                        confirmedImprovements.Add(cell.HoldoutImprovementPercent);
                        confirmedLowerBounds.Add(
                            cell.HoldoutConfidenceInterval.LowerBoundPercent);
                        break;
                    case EnvelopeCellStatus.StatisticalGreyZone:
                        summary.StatisticalGreyCellCount++;
                        break;
                    case EnvelopeCellStatus.AoSFallback:
                        summary.AoSFallbackCellCount++;
                        break;
                    case EnvelopeCellStatus.HoldoutRejected:
                        summary.HoldoutRejectedCellCount++;
                        break;
                }
            }

            summary.CredibleCoveragePercent = summary.ValidCellCount == 0
                ? 0d
                : (summary.CredibleAdvantageCellCount * 100d) / summary.ValidCellCount;
            if (confirmedImprovements.Count > 0)
            {
                double[] improvements = confirmedImprovements.ToArray();
                Array.Sort(improvements);
                summary.FloorConfirmedImprovementPercent = improvements[0];
                summary.PeakConfirmedImprovementPercent = improvements[improvements.Length - 1];
                summary.MedianConfirmedImprovementPercent =
                    DecisionEvidenceStatistics.Percentile(improvements, 0.5d);

                double[] lowerBounds = confirmedLowerBounds.ToArray();
                Array.Sort(lowerBounds);
                summary.WorstConfirmedConfidenceLowerBoundPercent = lowerBounds[0];
            }
            return summary;
        }

        private static bool IsBetterOutcome(
            EnvelopeCandidateOutcome candidate,
            EnvelopeCandidateOutcome current)
        {
            if (current == null)
                return true;
            if (candidate.AmortizedP95MillisecondsPerTick !=
                current.AmortizedP95MillisecondsPerTick)
            {
                return candidate.AmortizedP95MillisecondsPerTick <
                       current.AmortizedP95MillisecondsPerTick;
            }
            return DecisionEvidenceStatistics.CompareCandidate(
                       candidate.Candidate,
                       current.Candidate) < 0;
        }

        private static EnvelopeCandidateOutcome FindOutcome(
            EnvelopeCandidateOutcome[] outcomes,
            string candidateId)
        {
            if (outcomes == null)
                return null;
            for (int index = 0; index < outcomes.Length; index++)
            {
                if (outcomes[index] != null &&
                    string.Equals(
                        outcomes[index].Candidate.CandidateId,
                        candidateId,
                        StringComparison.Ordinal))
                {
                    return outcomes[index];
                }
            }
            return null;
        }

        private static EnvelopeCandidateOutcome[] CloneOutcomes(
            EnvelopeCandidateOutcome[] source)
        {
            if (source == null)
                return new EnvelopeCandidateOutcome[0];
            var result = new EnvelopeCandidateOutcome[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                EnvelopeCandidateOutcome item = source[index];
                if (item == null)
                    continue;
                result[index] = new EnvelopeCandidateOutcome
                {
                    Candidate = item.Candidate,
                    GateStatus = item.GateStatus,
                    Eligible = item.Eligible,
                    GateReason = item.GateReason,
                    EvidencePartitionId = item.EvidencePartitionId,
                    SourceEvidenceHash = item.SourceEvidenceHash,
                    ResidentSampleCount = item.ResidentSampleCount,
                    BoundarySampleCount = item.BoundarySampleCount,
                    BootstrapReplicateCount = item.BootstrapReplicateCount,
                    ResidentBytes = item.ResidentBytes,
                    ResidentP95MillisecondsPerTick = item.ResidentP95MillisecondsPerTick,
                    BoundaryP95Milliseconds = item.BoundaryP95Milliseconds,
                    AmortizedP95MillisecondsPerTick = item.AmortizedP95MillisecondsPerTick,
                    ImprovementPercent = item.ImprovementPercent,
                    ImprovementConfidenceInterval = item.ImprovementConfidenceInterval,
                    BreakEven = item.BreakEven,
                    MeetsMinimumEffect = item.MeetsMinimumEffect,
                    ClearsConfidenceGate = item.ClearsConfidenceGate,
                    CredibleCalibrationAdvantage = item.CredibleCalibrationAdvantage,
                };
            }
            return result;
        }

        private static void ValidateCalibrationRequest(
            AdvantageEnvelopeCalibrationRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.SchemaVersion != 1)
                throw new ArgumentException("Unsupported calibration request schema.", nameof(request));
            RequireMetadata(request.EnvelopeId, nameof(request.EnvelopeId));
            RequireMetadata(request.CreatedUtcIso8601, nameof(request.CreatedUtcIso8601));
            RequireMetadata(request.ScenarioId, nameof(request.ScenarioId));
            if (request.ContractVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(request.ContractVersion));
            RequireSha256(request.CandidateSetHash, nameof(request.CandidateSetHash));
            RequireSha256(request.MeasurementSchemaHash, nameof(request.MeasurementSchemaHash));
            RequireSha256(request.EnvironmentFingerprint, nameof(request.EnvironmentFingerprint));
            RequireSha256(request.CalibrationSettingsHash, nameof(request.CalibrationSettingsHash));
            RequireMetadata(request.SourceArtifactId, nameof(request.SourceArtifactId));
            RequireSha256(request.SourceArtifactSha256, nameof(request.SourceArtifactSha256));
            RequireMetadata(request.EvidenceScope, nameof(request.EvidenceScope));
            RequireMetadata(
                request.CalibrationUncertaintyMethod,
                nameof(request.CalibrationUncertaintyMethod));
            ValidatePolicy(request.Policy);
            if (request.Cells == null || request.Cells.Length == 0)
                throw new ArgumentException("At least one envelope cell is required.", nameof(request));
        }

        private static void ValidateCalibrationArtifact(AdvantageEnvelopeCalibration calibration)
        {
            if (calibration == null)
                throw new ArgumentNullException(nameof(calibration));
            if (calibration.SchemaVersion != 1 ||
                !string.Equals(
                    calibration.ArtifactType,
                    "advantage-envelope-calibration",
                    StringComparison.Ordinal))
            {
                throw new ArgumentException("Unsupported calibration artifact.", nameof(calibration));
            }
            if (calibration.HoldoutWasRead)
                throw new ArgumentException("Calibration artifact claims that it read holdout data.");
            if (!string.Equals(
                    calibration.DecisionEngineVersion,
                    Version,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Calibration artifact DecisionEngineVersion is unsupported.",
                    nameof(calibration.DecisionEngineVersion));
            }
            RequireMetadata(calibration.EnvelopeId, nameof(calibration.EnvelopeId));
            RequireMetadata(calibration.CreatedUtcIso8601, nameof(calibration.CreatedUtcIso8601));
            RequireMetadata(calibration.ScenarioId, nameof(calibration.ScenarioId));
            if (calibration.ContractVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(calibration.ContractVersion));
            RequireSha256(calibration.CandidateSetHash, nameof(calibration.CandidateSetHash));
            RequireSha256(
                calibration.MeasurementSchemaHash,
                nameof(calibration.MeasurementSchemaHash));
            RequireSha256(
                calibration.EnvironmentFingerprint,
                nameof(calibration.EnvironmentFingerprint));
            RequireSha256(
                calibration.CalibrationSettingsHash,
                nameof(calibration.CalibrationSettingsHash));
            RequireMetadata(
                calibration.CalibrationSourceArtifactId,
                nameof(calibration.CalibrationSourceArtifactId));
            RequireSha256(
                calibration.CalibrationSourceArtifactSha256,
                nameof(calibration.CalibrationSourceArtifactSha256));
            RequireMetadata(calibration.EvidenceScope, nameof(calibration.EvidenceScope));
            RequireMetadata(
                calibration.CalibrationUncertaintyMethod,
                nameof(calibration.CalibrationUncertaintyMethod));
            if (calibration.Cells == null || calibration.Cells.Length == 0)
                throw new ArgumentException("Calibration artifact has no cells.", nameof(calibration));
            ValidatePolicy(calibration.Policy);
            ValidateFrozenCalibrationCells(calibration.Cells);
        }

        private static void ValidateHoldoutRequest(
            AdvantageEnvelopeCalibration calibration,
            AdvantageEnvelopeHoldoutRequest holdout)
        {
            if (holdout == null)
                throw new ArgumentNullException(nameof(holdout));
            if (holdout.SchemaVersion != 1)
                throw new ArgumentException("Unsupported holdout request schema.", nameof(holdout));
            RequireMetadata(holdout.SourceArtifactId, nameof(holdout.SourceArtifactId));
            RequireSha256(holdout.SourceArtifactSha256, nameof(holdout.SourceArtifactSha256));
            RequireSha256(holdout.CandidateSetHash, nameof(holdout.CandidateSetHash));
            RequireSha256(holdout.MeasurementSchemaHash, nameof(holdout.MeasurementSchemaHash));
            RequireSha256(holdout.EnvironmentFingerprint, nameof(holdout.EnvironmentFingerprint));
            RequireSha256(holdout.HoldoutSettingsHash, nameof(holdout.HoldoutSettingsHash));
            RequireMetadata(holdout.HoldoutUncertaintyMethod, nameof(holdout.HoldoutUncertaintyMethod));
            RequireMetadata(holdout.EvidenceScope, nameof(holdout.EvidenceScope));
            if (!string.Equals(
                    calibration.CandidateSetHash,
                    holdout.CandidateSetHash,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    calibration.MeasurementSchemaHash,
                    holdout.MeasurementSchemaHash,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    calibration.EnvironmentFingerprint,
                    holdout.EnvironmentFingerprint,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Holdout candidate set, measurement schema, and environment fingerprint must match calibration.");
            }
            if (!string.Equals(
                    calibration.EvidenceScope,
                    holdout.EvidenceScope,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException("Calibration and holdout evidence scopes differ.");
            }
        }

        private static void ValidatePolicy(AdvantageEnvelopePolicy policy)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            if (!DecisionEvidenceStatistics.IsFiniteNonNegative(
                    policy.MinimumImprovementPercent))
            {
                throw new ArgumentOutOfRangeException(nameof(policy.MinimumImprovementPercent));
            }
            if (!(policy.ConfidenceLevel > 0d && policy.ConfidenceLevel < 1d) ||
                double.IsNaN(policy.ConfidenceLevel) ||
                double.IsInfinity(policy.ConfidenceLevel))
            {
                throw new ArgumentOutOfRangeException(nameof(policy.ConfidenceLevel));
            }
            if (policy.MinimumBootstrapReplicates < 100 ||
                policy.MinimumCalibrationResidentSamples <= 0 ||
                policy.MinimumCalibrationBoundarySamples <= 0 ||
                policy.MinimumHoldoutResidentSamples <
                policy.MinimumCalibrationResidentSamples ||
                policy.MinimumHoldoutBoundarySamples <
                policy.MinimumCalibrationBoundarySamples)
            {
                throw new ArgumentOutOfRangeException(nameof(policy));
            }
        }

        private static AdvantageEnvelopePolicy ClonePolicy(AdvantageEnvelopePolicy source)
        {
            return new AdvantageEnvelopePolicy
            {
                MinimumImprovementPercent = source.MinimumImprovementPercent,
                ConfidenceLevel = source.ConfidenceLevel,
                MinimumBootstrapReplicates = source.MinimumBootstrapReplicates,
                MinimumCalibrationResidentSamples = source.MinimumCalibrationResidentSamples,
                MinimumCalibrationBoundarySamples = source.MinimumCalibrationBoundarySamples,
                MinimumHoldoutResidentSamples = source.MinimumHoldoutResidentSamples,
                MinimumHoldoutBoundarySamples = source.MinimumHoldoutBoundarySamples,
            };
        }

        private static void ValidateAndSortCandidates(
            DecisionCandidateEvidence[] candidates)
        {
            for (int index = 0; index < candidates.Length; index++)
            {
                if (candidates[index] == null)
                    throw new ArgumentException("Candidate evidence must not be null.");
                if (!DecisionEvidenceStatistics.HasValidCandidateDescriptor(
                        candidates[index].Candidate,
                        out string reason))
                {
                    throw new ArgumentException(reason);
                }
                RequireSha256(
                    candidates[index].EvidenceHash,
                    nameof(DecisionCandidateEvidence.EvidenceHash));
                for (int other = 0; other < index; other++)
                {
                    if (string.Equals(
                            candidates[index].Candidate.CandidateId,
                            candidates[other].Candidate.CandidateId,
                            StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            "CandidateId must be unique within an envelope cell.");
                    }
                }
            }
            Array.Sort(candidates, CompareEvidence);
        }

        private static void ValidateFrozenCalibrationCells(
            EnvelopeCalibrationCellDecision[] cells)
        {
            for (int index = 0; index < cells.Length; index++)
            {
                EnvelopeCalibrationCellDecision cell = cells[index];
                if (cell == null)
                    throw new ArgumentException("Calibration artifact contains a null cell.");
                ValidateAxis(cell.Axis);
                for (int other = 0; other < index; other++)
                {
                    if (cells[other] != null && cells[other].Axis.Equals(cell.Axis))
                        throw new ArgumentException("Calibration artifact contains duplicate axes.");
                }
                if (cell.CandidateOutcomes == null || cell.CandidateOutcomes.Length == 0)
                {
                    throw new ArgumentException(
                        "Calibration artifact cell has no candidate outcomes.");
                }

                int tunedBaselineCount = 0;
                for (int outcomeIndex = 0;
                     outcomeIndex < cell.CandidateOutcomes.Length;
                     outcomeIndex++)
                {
                    EnvelopeCandidateOutcome outcome = cell.CandidateOutcomes[outcomeIndex];
                    if (outcome == null)
                    {
                        throw new ArgumentException(
                            "Calibration artifact contains a null candidate outcome.");
                    }
                    if (!DecisionEvidenceStatistics.HasValidCandidateDescriptor(
                            outcome.Candidate,
                            out string reason))
                    {
                        throw new ArgumentException(
                            "Calibration artifact contains an invalid candidate outcome. " +
                            reason);
                    }
                    RequireSha256(
                        outcome.SourceEvidenceHash,
                        nameof(EnvelopeCandidateOutcome.SourceEvidenceHash));
                    if (outcome.Candidate.IsTunedAoSBaseline)
                        tunedBaselineCount++;
                    for (int previous = 0; previous < outcomeIndex; previous++)
                    {
                        if (string.Equals(
                                outcome.Candidate.CandidateId,
                                cell.CandidateOutcomes[previous].Candidate.CandidateId,
                                StringComparison.Ordinal))
                        {
                            throw new ArgumentException(
                                "Calibration artifact contains duplicate CandidateId values.");
                        }
                    }
                }
                if (tunedBaselineCount != 1 ||
                    FindOutcome(cell.CandidateOutcomes, cell.BaselineCandidateId) == null ||
                    FindOutcome(cell.CandidateOutcomes, cell.BestMeasuredCandidateId) == null ||
                    FindOutcome(
                        cell.CandidateOutcomes,
                        cell.FrozenCalibrationWinnerCandidateId) == null)
                {
                    throw new ArgumentException(
                        "Calibration artifact candidate roles do not reference its explicit outcomes.");
                }
                EnvelopeCandidateOutcome baseline = FindOutcome(
                    cell.CandidateOutcomes,
                    cell.BaselineCandidateId);
                if (!baseline.Candidate.IsTunedAoSBaseline)
                    throw new ArgumentException("Calibration baseline role is not tuned AoS.");

                if (cell.CalibrationStatus != EnvelopeCellStatus.Invalid &&
                    cell.CalibrationStatus != EnvelopeCellStatus.AoSFallback &&
                    cell.CalibrationStatus != EnvelopeCellStatus.StatisticalGreyZone &&
                    cell.CalibrationStatus != EnvelopeCellStatus.CredibleAdvantage)
                {
                    throw new ArgumentException(
                        "Calibration artifact contains an unknown or holdout-only status.");
                }
                if (cell.CalibrationStatus == EnvelopeCellStatus.CredibleAdvantage)
                {
                    EnvelopeCandidateOutcome winner = FindOutcome(
                        cell.CandidateOutcomes,
                        cell.FrozenCalibrationWinnerCandidateId);
                    if (winner.Candidate.IsTunedAoSBaseline ||
                        !winner.Eligible ||
                        !winner.CredibleCalibrationAdvantage)
                    {
                        throw new ArgumentException(
                            "Frozen calibration advantage does not reference a credible non-AoS outcome.");
                    }
                }
                else if (!string.Equals(
                             cell.FrozenCalibrationWinnerCandidateId,
                             cell.BaselineCandidateId,
                             StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "A non-advantage calibration state must freeze tuned AoS.");
                }
            }
        }

        private static void ValidateAxis(AdvantageEnvelopeAxis axis)
        {
            if (axis.ElementCount <= 0 || axis.LifetimeTicks <= 0 ||
                axis.WorkerCount <= 0 ||
                !DecisionEvidenceStatistics.IsFiniteNonNegative(axis.HotToColdRatio) ||
                string.IsNullOrWhiteSpace(axis.ExecutionPolicyId))
            {
                throw new ArgumentException("Envelope axis values are invalid.");
            }
        }

        private static void RejectDuplicateAxes(AdvantageEnvelopeCellInput[] cells)
        {
            for (int index = 1; index < cells.Length; index++)
            {
                if (cells[index] == null || cells[index - 1] == null)
                    continue;
                if (cells[index].Axis.Equals(cells[index - 1].Axis))
                    throw new ArgumentException("Envelope axes must be unique.");
            }
        }

        private static AdvantageEnvelopeHoldoutCellInput[] CloneAndSortHoldoutCells(
            AdvantageEnvelopeHoldoutCellInput[] source)
        {
            var result = new AdvantageEnvelopeHoldoutCellInput[source.Length];
            Array.Copy(source, result, source.Length);
            for (int index = 0; index < result.Length; index++)
            {
                if (result[index] == null)
                    throw new ArgumentException("Holdout cells must not be null.");
                ValidateAxis(result[index].Axis);
            }
            Array.Sort(result, CompareHoldoutCells);
            return result;
        }

        private static void RejectDuplicateHoldoutAxes(
            AdvantageEnvelopeHoldoutCellInput[] cells)
        {
            for (int index = 1; index < cells.Length; index++)
            {
                if (cells[index].Axis.Equals(cells[index - 1].Axis))
                    throw new ArgumentException("Holdout axes must be unique.");
            }
        }

        private static void RejectUnfrozenHoldoutCells(
            EnvelopeCalibrationCellDecision[] calibration,
            AdvantageEnvelopeHoldoutCellInput[] holdout)
        {
            for (int index = 0; index < holdout.Length; index++)
            {
                EnvelopeCalibrationCellDecision match = FindCalibrationCell(
                    calibration,
                    holdout[index].Axis);
                if (match == null ||
                    match.CalibrationStatus != EnvelopeCellStatus.CredibleAdvantage)
                {
                    throw new ArgumentException(
                        "Holdout contains a cell that has no frozen non-AoS calibration winner.");
                }
            }
        }

        private static EnvelopeCalibrationCellDecision FindCalibrationCell(
            EnvelopeCalibrationCellDecision[] cells,
            AdvantageEnvelopeAxis axis)
        {
            for (int index = 0; index < cells.Length; index++)
            {
                if (cells[index] != null && cells[index].Axis.Equals(axis))
                    return cells[index];
            }
            return null;
        }

        private static AdvantageEnvelopeHoldoutCellInput FindHoldoutCell(
            AdvantageEnvelopeHoldoutCellInput[] cells,
            AdvantageEnvelopeAxis axis)
        {
            for (int index = 0; index < cells.Length; index++)
            {
                if (cells[index].Axis.Equals(axis))
                    return cells[index];
            }
            return null;
        }

        private static void RequireMetadata(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Required provenance metadata is missing.", name);
        }

        private static void RequireSha256(string value, string name)
        {
            if (!DecisionEvidenceStatistics.IsCanonicalSha256(value))
            {
                throw new ArgumentException(
                    "A canonical SHA-256 value must contain exactly 64 uppercase hexadecimal characters.",
                    name);
            }
        }

        private static int CompareCellInputs(
            AdvantageEnvelopeCellInput left,
            AdvantageEnvelopeCellInput right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return -1;
            if (right == null)
                return 1;
            return DecisionEvidenceStatistics.CompareAxis(left.Axis, right.Axis);
        }

        private static int CompareEvidence(
            DecisionCandidateEvidence left,
            DecisionCandidateEvidence right)
        {
            return DecisionEvidenceStatistics.CompareCandidate(left.Candidate, right.Candidate);
        }

        private static int CompareHoldoutCells(
            AdvantageEnvelopeHoldoutCellInput left,
            AdvantageEnvelopeHoldoutCellInput right)
        {
            return DecisionEvidenceStatistics.CompareAxis(left.Axis, right.Axis);
        }

        private static int CompareFinalCells(
            EnvelopeCellDecision left,
            EnvelopeCellDecision right)
        {
            return DecisionEvidenceStatistics.CompareAxis(left.Axis, right.Axis);
        }
    }
}
