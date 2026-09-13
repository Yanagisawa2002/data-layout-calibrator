using System;
using System.Collections.Generic;

namespace Yanagisawa.DataLayoutCalibrator
{
    public enum ParetoCandidateStatus
    {
        Invalid = 0,
        Infeasible = 1,
        Frontier = 2,
        StrictlyDominated = 3,
    }

    [Serializable]
    public struct ParetoCandidateMetric
    {
        public EnvelopeCandidateDescriptor Candidate;
        public bool Feasible;
        public double ResidentCostMillisecondsPerTick;
        public double BoundaryCostMilliseconds;
        public long ResidentBytes;
    }

    [Serializable]
    public sealed class ParetoCandidateDecision
    {
        public EnvelopeCandidateDescriptor Candidate;
        public ParetoCandidateStatus Status;
        public string DominatedByCandidateId;
        public string Reason;
    }

    [Serializable]
    public sealed class ParetoFrontierResult
    {
        public int SchemaVersion = 1;
        public ParetoCandidateDecision[] Candidates;
        public string[] FrontierCandidateIds;
    }

    /// <summary>
    /// Deterministic strict Pareto dominance over resident cost, total boundary
    /// cost, and resident bytes. Equality in all dimensions is not dominance.
    /// </summary>
    public static class ParetoFrontier
    {
        public static ParetoFrontierResult Build(ParetoCandidateMetric[] candidates)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));
            if (candidates.Length == 0)
                throw new ArgumentException("At least one Pareto candidate is required.", nameof(candidates));

            var ordered = new ParetoCandidateMetric[candidates.Length];
            Array.Copy(candidates, ordered, candidates.Length);
            for (int index = 0; index < ordered.Length; index++)
            {
                if (!DecisionEvidenceStatistics.HasValidCandidateDescriptor(
                        ordered[index].Candidate,
                        out string reason))
                {
                    throw new ArgumentException(reason, nameof(candidates));
                }
                for (int other = 0; other < index; other++)
                {
                    if (string.Equals(
                            ordered[index].Candidate.CandidateId,
                            ordered[other].Candidate.CandidateId,
                            StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            "Pareto CandidateId values must be unique.",
                            nameof(candidates));
                    }
                }
            }
            Array.Sort(ordered, CompareMetric);

            var decisions = new ParetoCandidateDecision[ordered.Length];
            var frontierIds = new List<string>();
            for (int index = 0; index < ordered.Length; index++)
            {
                ParetoCandidateMetric candidate = ordered[index];
                var decision = new ParetoCandidateDecision
                {
                    Candidate = candidate.Candidate,
                };
                if (!candidate.Feasible)
                {
                    decision.Status = ParetoCandidateStatus.Infeasible;
                    decision.Reason = "The candidate was excluded by a feasibility gate.";
                    decisions[index] = decision;
                    continue;
                }
                if (!IsValidMetric(candidate))
                {
                    decision.Status = ParetoCandidateStatus.Invalid;
                    decision.Reason = "Pareto metrics must be finite, non-negative, and have non-negative resident bytes.";
                    decisions[index] = decision;
                    continue;
                }

                ParetoCandidateMetric? dominator = null;
                for (int other = 0; other < ordered.Length; other++)
                {
                    if (other == index || !ordered[other].Feasible ||
                        !IsValidMetric(ordered[other]))
                    {
                        continue;
                    }
                    if (StrictlyDominates(ordered[other], candidate))
                    {
                        dominator = ordered[other];
                        break;
                    }
                }

                if (dominator.HasValue)
                {
                    decision.Status = ParetoCandidateStatus.StrictlyDominated;
                    decision.DominatedByCandidateId = dominator.Value.Candidate.CandidateId;
                    decision.Reason =
                        "Another feasible candidate is no worse in resident cost, boundary cost, and resident bytes, and is strictly better in at least one dimension.";
                }
                else
                {
                    decision.Status = ParetoCandidateStatus.Frontier;
                    decision.Reason = "No feasible candidate strictly dominates this point.";
                    frontierIds.Add(candidate.Candidate.CandidateId);
                }
                decisions[index] = decision;
            }

            return new ParetoFrontierResult
            {
                SchemaVersion = 1,
                Candidates = decisions,
                FrontierCandidateIds = frontierIds.ToArray(),
            };
        }

        private static bool StrictlyDominates(
            ParetoCandidateMetric left,
            ParetoCandidateMetric right)
        {
            bool noWorse =
                left.ResidentCostMillisecondsPerTick <= right.ResidentCostMillisecondsPerTick &&
                left.BoundaryCostMilliseconds <= right.BoundaryCostMilliseconds &&
                left.ResidentBytes <= right.ResidentBytes;
            bool strictlyBetter =
                left.ResidentCostMillisecondsPerTick < right.ResidentCostMillisecondsPerTick ||
                left.BoundaryCostMilliseconds < right.BoundaryCostMilliseconds ||
                left.ResidentBytes < right.ResidentBytes;
            return noWorse && strictlyBetter;
        }

        private static bool IsValidMetric(ParetoCandidateMetric candidate)
        {
            return DecisionEvidenceStatistics.IsFiniteNonNegative(
                       candidate.ResidentCostMillisecondsPerTick) &&
                   DecisionEvidenceStatistics.IsFiniteNonNegative(
                       candidate.BoundaryCostMilliseconds) &&
                   candidate.ResidentBytes >= 0L;
        }

        private static int CompareMetric(ParetoCandidateMetric left, ParetoCandidateMetric right)
        {
            return DecisionEvidenceStatistics.CompareCandidate(left.Candidate, right.Candidate);
        }
    }

    public enum AdaptiveEliminationPlanStatus
    {
        Invalid = 0,
        ReadyForFullCalibration = 1,
    }

    public enum AdaptiveEliminationStage
    {
        None = 0,
        FeasibilityScreen = 1,
        QuickCalibration = 2,
        ParetoFrontier = 3,
        Finalist = 4,
    }

    public enum AdaptiveCandidateDisposition
    {
        Eliminated = 0,
        Finalist = 1,
        ProtectedTunedAoSBaseline = 2,
    }

    [Serializable]
    public sealed class AdaptiveEliminationPolicy
    {
        public double MinimumImprovementPercent = 10d;
        public double ConfidenceLevel = 0.95d;
        public int MinimumQuickResidentSamples = 3;
        public int MinimumQuickBoundarySamples = 3;
        public int MinimumQuickBootstrapReplicates = 100;
        public int RequiredFullResidentSamplesPerFinalist = 40;
        public int RequiredFullBoundarySamplesPerFinalist = 20;
        public int RequiredFullBootstrapReplicates = 4000;
        public int RequiredHoldoutResidentSamples = 40;
        public int RequiredHoldoutBoundarySamples = 20;
        public int RequiredHoldoutBootstrapReplicates = 4000;
    }

    [Serializable]
    public sealed class AdaptiveEliminationRequest
    {
        public int SchemaVersion = 1;
        public string SearchId;
        public string CreatedUtcIso8601;
        public string ScenarioId;
        public int ContractVersion;
        public string CandidateSetHash;
        public string MeasurementSchemaHash;
        public string EnvironmentFingerprint;
        public string QuickCalibrationSettingsHash;
        public string SourceArtifactId;
        public string SourceArtifactSha256;
        public string CalibrationPartitionId;
        public string PlannedHoldoutPartitionId;
        public string EvidenceScope;
        public string QuickUncertaintyMethod;
        public AdvantageEnvelopeAxis Axis;
        public AdaptiveEliminationPolicy Policy;
        public DecisionCandidateEvidence[] Candidates;
    }

    [Serializable]
    public sealed class AdaptiveCandidateDecision
    {
        public EnvelopeCandidateDescriptor Candidate;
        public CandidateEvidenceGateStatus GateStatus;
        public AdaptiveCandidateDisposition Disposition;
        public AdaptiveEliminationStage Stage;
        public string Reason;
        public string EvidencePartitionId;
        public string SourceEvidenceHash;
        public int QuickResidentSampleCount;
        public int QuickBoundarySampleCount;
        public int QuickBootstrapReplicateCount;
        public bool QuickConfidenceAvailable;
        public EnvelopeConfidenceInterval QuickImprovementConfidenceInterval;
        public ParetoCandidateStatus ParetoStatus;
        public string DominatedByCandidateId;
    }

    [Serializable]
    public sealed class AdaptiveEliminationPlan
    {
        public int SchemaVersion = 1;
        public string ArtifactType = "adaptive-elimination-plan";
        public string DecisionEngineVersion;
        public AdaptiveEliminationPlanStatus Status;
        public string SearchId;
        public string CreatedUtcIso8601;
        public string ScenarioId;
        public int ContractVersion;
        public string CandidateSetHash;
        public string MeasurementSchemaHash;
        public string EnvironmentFingerprint;
        public string QuickCalibrationSettingsHash;
        public string SourceArtifactId;
        public string SourceArtifactSha256;
        public string CalibrationPartitionId;
        public string PlannedHoldoutPartitionId;
        public string EvidenceScope;
        public string QuickUncertaintyMethod;
        public AdvantageEnvelopeAxis Axis;
        public AdaptiveEliminationPolicy Policy;
        public string TunedAoSBaselineCandidateId;
        public AdaptiveCandidateDecision[] CandidateDecisions;
        public string[] FinalistCandidateIds;
        public int FeasibilityEligibleCandidateCount;
        public int EliminatedCandidateCount;
        public long QuickCalibrationComponentSampleUnitsConsumed;
        public long ExhaustiveFullCalibrationComponentSampleUnits;
        public long AdaptiveFullCalibrationComponentSampleUnits;
        public long PlannedFullCalibrationComponentSampleUnitsSaved;
        public long AdaptiveCalibrationComponentSampleUnitsIncludingQuick;
        public int RequiredFullResidentSamplesPerFinalist;
        public int RequiredFullBoundarySamplesPerFinalist;
        public int RequiredFullBootstrapReplicates;
        public int RequiredHoldoutResidentSamples;
        public int RequiredHoldoutBoundarySamples;
        public int RequiredHoldoutBootstrapReplicates;
        public bool FinalEvidenceRequirementsUnchanged;
        public bool HoldoutCanRerank;
        public string FinalEvidencePolicy;
        public string Reason;
    }

    [Serializable]
    public struct FullCalibrationScore
    {
        public string CandidateId;
        public bool Eligible;
        public double AmortizedP95MillisecondsPerTick;
    }

    [Serializable]
    public sealed class AdaptiveRegretAudit
    {
        public int SchemaVersion = 1;
        public bool Valid;
        public bool AuditOnly;
        public string ExhaustiveWinnerCandidateId;
        public string AdaptiveWinnerCandidateId;
        public double ExhaustiveWinnerCostMillisecondsPerTick;
        public double AdaptiveWinnerCostMillisecondsPerTick;
        public double SelectionRegretPercent;
        public double MaximumAllowedRegretPercent;
        public bool ExactWinnerEquivalent;
        public bool WithinRegretBound;
        public string Reason;
    }

    /// <summary>
    /// Conservative calibration-pruning foundation. It can remove feasibility
    /// failures, candidates whose optimistic quick bound misses the minimum
    /// effect, and strictly dominated candidates. It does not run or weaken full
    /// finalist evidence and never receives final holdout results.
    /// </summary>
    public static class AdaptiveEliminationEngine
    {
        private const double ThresholdTolerance = 1e-12d;

        public static AdaptiveEliminationPlan CreatePlan(AdaptiveEliminationRequest request)
        {
            ValidateRequest(request);
            DecisionCandidateEvidence[] evidence = new DecisionCandidateEvidence[
                request.Candidates.Length];
            Array.Copy(request.Candidates, evidence, evidence.Length);
            ValidateAndSortEvidence(evidence);

            int baselineIndex = -1;
            int baselineCount = 0;
            for (int index = 0; index < evidence.Length; index++)
            {
                if (evidence[index].Candidate.IsTunedAoSBaseline)
                {
                    baselineIndex = index;
                    baselineCount++;
                }
            }
            if (baselineCount != 1)
            {
                throw new ArgumentException(
                    "Adaptive elimination requires exactly one tuned AoS baseline.",
                    nameof(request));
            }

            DecisionCandidateEvidence baseline = evidence[baselineIndex];
            var decisions = new AdaptiveCandidateDecision[evidence.Length];
            var active = new bool[evidence.Length];
            int feasibilityEligibleCount = 0;
            long quickUnits = 0L;
            for (int index = 0; index < evidence.Length; index++)
            {
                DecisionCandidateEvidence candidate = evidence[index];
                CandidateEvidenceGateStatus gate =
                    DecisionEvidenceStatistics.EvaluateFeasibilityGate(
                        candidate,
                        request.CalibrationPartitionId,
                        out string reason);
                if (!string.Equals(
                        candidate.Candidate.ExecutionPolicyId,
                        request.Axis.ExecutionPolicyId,
                        StringComparison.Ordinal))
                {
                    gate = CandidateEvidenceGateStatus.ContractInfeasible;
                    reason = "The candidate execution policy does not match the search axis.";
                }

                decisions[index] = new AdaptiveCandidateDecision
                {
                    Candidate = candidate.Candidate,
                    GateStatus = gate,
                    Disposition = AdaptiveCandidateDisposition.Eliminated,
                    Stage = AdaptiveEliminationStage.FeasibilityScreen,
                    Reason = reason,
                    EvidencePartitionId = candidate.EvidencePartitionId,
                    SourceEvidenceHash = candidate.EvidenceHash,
                    QuickResidentSampleCount = candidate.ResidentSampleCount,
                    QuickBoundarySampleCount = candidate.BoundarySampleCount,
                    QuickBootstrapReplicateCount = candidate.BootstrapReplicates == null
                        ? 0
                        : candidate.BootstrapReplicates.Length,
                    ParetoStatus = ParetoCandidateStatus.Infeasible,
                };
                if (gate == CandidateEvidenceGateStatus.Eligible)
                {
                    active[index] = true;
                    feasibilityEligibleCount++;
                    quickUnits += candidate.ResidentSampleCount +
                                  (candidate.BoundarySampleCount * 2L);
                }
            }

            if (!active[baselineIndex])
            {
                for (int index = 0; index < active.Length; index++)
                {
                    if (active[index])
                    {
                        decisions[index].Disposition = AdaptiveCandidateDisposition.Eliminated;
                        decisions[index].Stage = AdaptiveEliminationStage.FeasibilityScreen;
                        decisions[index].Reason =
                            "No adaptive disposition is valid because tuned AoS failed its mandatory feasibility screen.";
                    }
                }
                return BuildPlan(
                    request,
                    decisions,
                    new string[0],
                    feasibilityEligibleCount,
                    quickUnits,
                    AdaptiveEliminationPlanStatus.Invalid,
                    "Tuned AoS failed the feasibility screen; adaptive pruning cannot proceed safely.");
            }

            CandidateEvidenceGateStatus baselineQuickGate =
                DecisionEvidenceStatistics.EvaluateGate(
                    baseline,
                    request.Policy.MinimumQuickResidentSamples,
                    request.Policy.MinimumQuickBoundarySamples,
                    request.Policy.MinimumQuickBootstrapReplicates,
                    request.CalibrationPartitionId,
                    out string baselineQuickReason);
            bool baselineQuickAvailable =
                baselineQuickGate == CandidateEvidenceGateStatus.Eligible;
            decisions[baselineIndex].QuickConfidenceAvailable = baselineQuickAvailable;

            for (int index = 0; index < evidence.Length; index++)
            {
                if (!active[index] || index == baselineIndex)
                    continue;
                if (!baselineQuickAvailable)
                {
                    decisions[index].Stage = AdaptiveEliminationStage.QuickCalibration;
                    decisions[index].Reason =
                        "Tuned AoS quick uncertainty is insufficient, so the candidate is retained conservatively for full calibration: " +
                        baselineQuickReason;
                    continue;
                }
                CandidateEvidenceGateStatus fullQuickGate =
                    DecisionEvidenceStatistics.EvaluateGate(
                        evidence[index],
                        request.Policy.MinimumQuickResidentSamples,
                        request.Policy.MinimumQuickBoundarySamples,
                        request.Policy.MinimumQuickBootstrapReplicates,
                        request.CalibrationPartitionId,
                        out string quickReason);
                if (fullQuickGate != CandidateEvidenceGateStatus.Eligible)
                {
                    decisions[index].Stage = AdaptiveEliminationStage.QuickCalibration;
                    decisions[index].Reason =
                        "Quick uncertainty was insufficient or invalid, so the candidate is retained conservatively for full calibration: " +
                        quickReason;
                    continue;
                }

                try
                {
                    EnvelopeConfidenceInterval interval =
                        DecisionEvidenceStatistics.CalculateImprovementInterval(
                            baseline,
                            evidence[index],
                            request.Axis.LifetimeTicks,
                            request.Policy.ConfidenceLevel);
                    decisions[index].QuickConfidenceAvailable = true;
                    decisions[index].QuickImprovementConfidenceInterval = interval;
                    decisions[index].Stage = AdaptiveEliminationStage.QuickCalibration;
                    if (IsStrictlyBelowThreshold(
                            interval.UpperBoundPercent,
                            request.Policy.MinimumImprovementPercent))
                    {
                        active[index] = false;
                        decisions[index].Reason =
                            "Even the optimistic quick confidence bound is below the frozen minimum improvement threshold.";
                    }
                    else
                    {
                        decisions[index].Reason =
                            "The optimistic quick confidence bound can still reach the frozen minimum improvement threshold.";
                    }
                }
                catch (ArgumentException)
                {
                    decisions[index].Reason =
                        "Quick uncertainty could not be aligned, so the candidate is retained conservatively for full calibration.";
                }
            }

            var paretoMetrics = new List<ParetoCandidateMetric>();
            for (int index = 0; index < evidence.Length; index++)
            {
                if (!active[index] ||
                    (index != baselineIndex &&
                     !decisions[index].QuickConfidenceAvailable))
                    continue;
                paretoMetrics.Add(new ParetoCandidateMetric
                {
                    Candidate = evidence[index].Candidate,
                    Feasible = true,
                    ResidentCostMillisecondsPerTick =
                        evidence[index].ResidentP95MillisecondsPerTick,
                    BoundaryCostMilliseconds = evidence[index].IngressP95Milliseconds +
                                               evidence[index].ExportP95Milliseconds,
                    ResidentBytes = evidence[index].ResidentBytes,
                });
            }
            ParetoFrontierResult frontier = ParetoFrontier.Build(paretoMetrics.ToArray());
            for (int index = 0; index < evidence.Length; index++)
            {
                if (!active[index] ||
                    (index != baselineIndex &&
                     !decisions[index].QuickConfidenceAvailable))
                    continue;
                ParetoCandidateDecision pareto = FindParetoDecision(
                    frontier.Candidates,
                    evidence[index].Candidate.CandidateId);
                decisions[index].ParetoStatus = pareto.Status;
                decisions[index].DominatedByCandidateId = pareto.DominatedByCandidateId;
                if (pareto.Status == ParetoCandidateStatus.StrictlyDominated &&
                    index != baselineIndex)
                {
                    active[index] = false;
                    decisions[index].Stage = AdaptiveEliminationStage.ParetoFrontier;
                    decisions[index].Reason = pareto.Reason;
                }
            }

            var finalistIds = new List<string>();
            for (int index = 0; index < evidence.Length; index++)
            {
                if (!active[index])
                    continue;
                finalistIds.Add(evidence[index].Candidate.CandidateId);
                decisions[index].Stage = AdaptiveEliminationStage.Finalist;
                if (index == baselineIndex)
                {
                    decisions[index].Disposition =
                        AdaptiveCandidateDisposition.ProtectedTunedAoSBaseline;
                    decisions[index].Reason =
                        decisions[index].ParetoStatus == ParetoCandidateStatus.StrictlyDominated
                            ? "Tuned AoS is mathematically dominated in the quick screen but remains protected as the mandatory safe fallback."
                            : "Tuned AoS remains protected as the mandatory safe fallback.";
                }
                else
                {
                    decisions[index].Disposition = AdaptiveCandidateDisposition.Finalist;
                    if (!decisions[index].QuickConfidenceAvailable)
                    {
                        decisions[index].Reason =
                            "The candidate is retained conservatively because quick uncertainty was insufficient for safe elimination.";
                    }
                    else
                    {
                        decisions[index].Reason =
                            "The candidate survived optimistic-bound and strict Pareto screens and requires full calibration evidence.";
                    }
                }
            }

            return BuildPlan(
                request,
                decisions,
                finalistIds.ToArray(),
                feasibilityEligibleCount,
                quickUnits,
                AdaptiveEliminationPlanStatus.ReadyForFullCalibration,
                "Only calibration work was pruned; every finalist retains the frozen full-sampling and independent holdout requirements.");
        }

        /// <summary>
        /// Counterfactual audit using full calibration scores for all feasible
        /// candidates. This is a validation tool, not a final selector, and it
        /// never consumes holdout evidence.
        /// </summary>
        public static AdaptiveRegretAudit AuditAgainstExhaustive(
            AdaptiveEliminationPlan plan,
            FullCalibrationScore[] fullScores,
            double maximumAllowedRegretPercent)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (fullScores == null)
                throw new ArgumentNullException(nameof(fullScores));
            if (!DecisionEvidenceStatistics.IsFiniteNonNegative(maximumAllowedRegretPercent))
                throw new ArgumentOutOfRangeException(nameof(maximumAllowedRegretPercent));

            var audit = new AdaptiveRegretAudit
            {
                SchemaVersion = 1,
                AuditOnly = true,
                MaximumAllowedRegretPercent = maximumAllowedRegretPercent,
            };
            if (plan.Status != AdaptiveEliminationPlanStatus.ReadyForFullCalibration)
            {
                audit.Reason = "The adaptive plan is not valid for a regret audit.";
                return audit;
            }

            ValidateScores(fullScores);
            FullCalibrationScore? exhaustive = null;
            FullCalibrationScore? adaptive = null;
            for (int index = 0; index < plan.CandidateDecisions.Length; index++)
            {
                AdaptiveCandidateDecision decision = plan.CandidateDecisions[index];
                if (decision.GateStatus != CandidateEvidenceGateStatus.Eligible)
                    continue;
                FullCalibrationScore? score = FindScore(
                    fullScores,
                    decision.Candidate.CandidateId);
                if (!score.HasValue)
                {
                    audit.Reason =
                        "A feasible candidate is missing its counterfactual full calibration score.";
                    return audit;
                }
                if (!score.Value.Eligible ||
                    !(score.Value.AmortizedP95MillisecondsPerTick > 0d) ||
                    double.IsNaN(score.Value.AmortizedP95MillisecondsPerTick) ||
                    double.IsInfinity(score.Value.AmortizedP95MillisecondsPerTick))
                {
                    continue;
                }
                if (!exhaustive.HasValue || IsBetterScore(
                        score.Value,
                        exhaustive.Value,
                        plan.CandidateDecisions))
                {
                    exhaustive = score;
                }
                if (Contains(plan.FinalistCandidateIds, score.Value.CandidateId) &&
                    (!adaptive.HasValue || IsBetterScore(
                        score.Value,
                        adaptive.Value,
                        plan.CandidateDecisions)))
                {
                    adaptive = score;
                }
            }

            if (!exhaustive.HasValue || !adaptive.HasValue)
            {
                audit.Reason = "No eligible exhaustive or adaptive winner could be audited.";
                return audit;
            }

            double regret = ((adaptive.Value.AmortizedP95MillisecondsPerTick -
                              exhaustive.Value.AmortizedP95MillisecondsPerTick) /
                             exhaustive.Value.AmortizedP95MillisecondsPerTick) * 100d;
            if (regret < 0d && regret > -1e-12d)
                regret = 0d;
            audit.Valid = true;
            audit.ExhaustiveWinnerCandidateId = exhaustive.Value.CandidateId;
            audit.AdaptiveWinnerCandidateId = adaptive.Value.CandidateId;
            audit.ExhaustiveWinnerCostMillisecondsPerTick =
                exhaustive.Value.AmortizedP95MillisecondsPerTick;
            audit.AdaptiveWinnerCostMillisecondsPerTick =
                adaptive.Value.AmortizedP95MillisecondsPerTick;
            audit.SelectionRegretPercent = regret;
            audit.ExactWinnerEquivalent = string.Equals(
                exhaustive.Value.CandidateId,
                adaptive.Value.CandidateId,
                StringComparison.Ordinal);
            audit.WithinRegretBound = regret <= maximumAllowedRegretPercent;
            audit.Reason = audit.ExactWinnerEquivalent
                ? "Adaptive and exhaustive calibration winners are identical."
                : audit.WithinRegretBound
                    ? "Adaptive calibration differs from exhaustive search but remains within the preregistered regret bound."
                    : "Adaptive calibration exceeds the preregistered regret bound.";
            return audit;
        }

        private static AdaptiveEliminationPlan BuildPlan(
            AdaptiveEliminationRequest request,
            AdaptiveCandidateDecision[] decisions,
            string[] finalistIds,
            int feasibilityEligibleCount,
            long quickUnits,
            AdaptiveEliminationPlanStatus status,
            string reason)
        {
            long perCandidateFullUnits =
                request.Policy.RequiredFullResidentSamplesPerFinalist +
                (request.Policy.RequiredFullBoundarySamplesPerFinalist * 2L);
            long exhaustiveFullUnits = feasibilityEligibleCount * perCandidateFullUnits;
            long adaptiveFullUnits = finalistIds.Length * perCandidateFullUnits;
            int eliminatedCount = 0;
            for (int index = 0; index < decisions.Length; index++)
            {
                if (decisions[index].Disposition == AdaptiveCandidateDisposition.Eliminated)
                    eliminatedCount++;
            }

            return new AdaptiveEliminationPlan
            {
                SchemaVersion = 1,
                ArtifactType = "adaptive-elimination-plan",
                DecisionEngineVersion = AdvantageEnvelopeEngine.Version,
                Status = status,
                SearchId = request.SearchId,
                CreatedUtcIso8601 = request.CreatedUtcIso8601,
                ScenarioId = request.ScenarioId,
                ContractVersion = request.ContractVersion,
                CandidateSetHash = request.CandidateSetHash,
                MeasurementSchemaHash = request.MeasurementSchemaHash,
                EnvironmentFingerprint = request.EnvironmentFingerprint,
                QuickCalibrationSettingsHash = request.QuickCalibrationSettingsHash,
                SourceArtifactId = request.SourceArtifactId,
                SourceArtifactSha256 = request.SourceArtifactSha256,
                CalibrationPartitionId = request.CalibrationPartitionId,
                PlannedHoldoutPartitionId = request.PlannedHoldoutPartitionId,
                EvidenceScope = request.EvidenceScope,
                QuickUncertaintyMethod = request.QuickUncertaintyMethod,
                Axis = request.Axis,
                Policy = ClonePolicy(request.Policy),
                TunedAoSBaselineCandidateId = FindBaselineId(decisions),
                CandidateDecisions = decisions,
                FinalistCandidateIds = finalistIds,
                FeasibilityEligibleCandidateCount = feasibilityEligibleCount,
                EliminatedCandidateCount = eliminatedCount,
                QuickCalibrationComponentSampleUnitsConsumed = quickUnits,
                ExhaustiveFullCalibrationComponentSampleUnits = exhaustiveFullUnits,
                AdaptiveFullCalibrationComponentSampleUnits = adaptiveFullUnits,
                PlannedFullCalibrationComponentSampleUnitsSaved =
                    status == AdaptiveEliminationPlanStatus.ReadyForFullCalibration
                        ? Math.Max(0L, exhaustiveFullUnits - adaptiveFullUnits)
                        : 0L,
                AdaptiveCalibrationComponentSampleUnitsIncludingQuick =
                    quickUnits + adaptiveFullUnits,
                RequiredFullResidentSamplesPerFinalist =
                    request.Policy.RequiredFullResidentSamplesPerFinalist,
                RequiredFullBoundarySamplesPerFinalist =
                    request.Policy.RequiredFullBoundarySamplesPerFinalist,
                RequiredFullBootstrapReplicates =
                    request.Policy.RequiredFullBootstrapReplicates,
                RequiredHoldoutResidentSamples =
                    request.Policy.RequiredHoldoutResidentSamples,
                RequiredHoldoutBoundarySamples =
                    request.Policy.RequiredHoldoutBoundarySamples,
                RequiredHoldoutBootstrapReplicates =
                    request.Policy.RequiredHoldoutBootstrapReplicates,
                FinalEvidenceRequirementsUnchanged =
                    status == AdaptiveEliminationPlanStatus.ReadyForFullCalibration,
                HoldoutCanRerank = false,
                FinalEvidencePolicy =
                    "Run frozen full sampling and bootstrap for every finalist, freeze one calibration winner, then compare only that winner and tuned AoS on the distinct holdout partition.",
                Reason = reason,
            };
        }

        private static void ValidateRequest(AdaptiveEliminationRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.SchemaVersion != 1)
                throw new ArgumentException("Unsupported adaptive request schema.", nameof(request));
            RequireMetadata(request.SearchId, nameof(request.SearchId));
            RequireMetadata(request.CreatedUtcIso8601, nameof(request.CreatedUtcIso8601));
            RequireMetadata(request.ScenarioId, nameof(request.ScenarioId));
            if (request.ContractVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(request.ContractVersion));
            RequireSha256(request.CandidateSetHash, nameof(request.CandidateSetHash));
            RequireSha256(request.MeasurementSchemaHash, nameof(request.MeasurementSchemaHash));
            RequireSha256(request.EnvironmentFingerprint, nameof(request.EnvironmentFingerprint));
            RequireSha256(
                request.QuickCalibrationSettingsHash,
                nameof(request.QuickCalibrationSettingsHash));
            RequireMetadata(request.SourceArtifactId, nameof(request.SourceArtifactId));
            RequireSha256(request.SourceArtifactSha256, nameof(request.SourceArtifactSha256));
            RequireMetadata(request.CalibrationPartitionId, nameof(request.CalibrationPartitionId));
            RequireMetadata(
                request.PlannedHoldoutPartitionId,
                nameof(request.PlannedHoldoutPartitionId));
            if (string.Equals(
                    request.CalibrationPartitionId,
                    request.PlannedHoldoutPartitionId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Calibration and planned holdout partitions must differ.",
                    nameof(request));
            }
            RequireMetadata(request.EvidenceScope, nameof(request.EvidenceScope));
            RequireMetadata(request.QuickUncertaintyMethod, nameof(request.QuickUncertaintyMethod));
            ValidateAxis(request.Axis);
            ValidatePolicy(request.Policy);
            if (request.Candidates == null || request.Candidates.Length == 0)
                throw new ArgumentException("Adaptive candidates are required.", nameof(request));
        }

        private static void ValidatePolicy(AdaptiveEliminationPolicy policy)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            if (!DecisionEvidenceStatistics.IsFiniteNonNegative(
                    policy.MinimumImprovementPercent) ||
                !(policy.ConfidenceLevel > 0d && policy.ConfidenceLevel < 1d) ||
                double.IsNaN(policy.ConfidenceLevel) ||
                double.IsInfinity(policy.ConfidenceLevel))
            {
                throw new ArgumentOutOfRangeException(nameof(policy));
            }
            if (policy.MinimumQuickResidentSamples <= 0 ||
                policy.MinimumQuickBoundarySamples <= 0 ||
                policy.MinimumQuickBootstrapReplicates < 100 ||
                policy.RequiredFullResidentSamplesPerFinalist <
                policy.MinimumQuickResidentSamples ||
                policy.RequiredFullBoundarySamplesPerFinalist <
                policy.MinimumQuickBoundarySamples ||
                policy.RequiredFullBootstrapReplicates <
                policy.MinimumQuickBootstrapReplicates ||
                policy.RequiredHoldoutResidentSamples <
                policy.RequiredFullResidentSamplesPerFinalist ||
                policy.RequiredHoldoutBoundarySamples <
                policy.RequiredFullBoundarySamplesPerFinalist ||
                policy.RequiredHoldoutBootstrapReplicates <
                policy.RequiredFullBootstrapReplicates)
            {
                throw new ArgumentOutOfRangeException(nameof(policy));
            }
        }

        private static AdaptiveEliminationPolicy ClonePolicy(
            AdaptiveEliminationPolicy source)
        {
            return new AdaptiveEliminationPolicy
            {
                MinimumImprovementPercent = source.MinimumImprovementPercent,
                ConfidenceLevel = source.ConfidenceLevel,
                MinimumQuickResidentSamples = source.MinimumQuickResidentSamples,
                MinimumQuickBoundarySamples = source.MinimumQuickBoundarySamples,
                MinimumQuickBootstrapReplicates = source.MinimumQuickBootstrapReplicates,
                RequiredFullResidentSamplesPerFinalist =
                    source.RequiredFullResidentSamplesPerFinalist,
                RequiredFullBoundarySamplesPerFinalist =
                    source.RequiredFullBoundarySamplesPerFinalist,
                RequiredFullBootstrapReplicates = source.RequiredFullBootstrapReplicates,
                RequiredHoldoutResidentSamples = source.RequiredHoldoutResidentSamples,
                RequiredHoldoutBoundarySamples = source.RequiredHoldoutBoundarySamples,
                RequiredHoldoutBootstrapReplicates =
                    source.RequiredHoldoutBootstrapReplicates,
            };
        }

        private static void ValidateAndSortEvidence(DecisionCandidateEvidence[] evidence)
        {
            for (int index = 0; index < evidence.Length; index++)
            {
                if (evidence[index] == null)
                    throw new ArgumentException("Adaptive candidate evidence must not be null.");
                if (!DecisionEvidenceStatistics.HasValidCandidateDescriptor(
                        evidence[index].Candidate,
                        out string reason))
                {
                    throw new ArgumentException(reason);
                }
                RequireSha256(
                    evidence[index].EvidenceHash,
                    nameof(DecisionCandidateEvidence.EvidenceHash));
                for (int other = 0; other < index; other++)
                {
                    if (string.Equals(
                            evidence[index].Candidate.CandidateId,
                            evidence[other].Candidate.CandidateId,
                            StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            "Adaptive CandidateId values must be unique.");
                    }
                }
            }
            Array.Sort(evidence, CompareEvidence);
        }

        private static void ValidateAxis(AdvantageEnvelopeAxis axis)
        {
            if (axis.ElementCount <= 0 || axis.LifetimeTicks <= 0 ||
                axis.WorkerCount <= 0 ||
                !DecisionEvidenceStatistics.IsFiniteNonNegative(axis.HotToColdRatio) ||
                string.IsNullOrWhiteSpace(axis.ExecutionPolicyId))
            {
                throw new ArgumentException("Adaptive search axis is invalid.");
            }
        }

        private static ParetoCandidateDecision FindParetoDecision(
            ParetoCandidateDecision[] decisions,
            string candidateId)
        {
            for (int index = 0; index < decisions.Length; index++)
            {
                if (string.Equals(
                        decisions[index].Candidate.CandidateId,
                        candidateId,
                        StringComparison.Ordinal))
                {
                    return decisions[index];
                }
            }
            throw new InvalidOperationException("Pareto decision is missing a candidate.");
        }

        private static string FindBaselineId(AdaptiveCandidateDecision[] decisions)
        {
            for (int index = 0; index < decisions.Length; index++)
            {
                if (decisions[index].Candidate.IsTunedAoSBaseline)
                    return decisions[index].Candidate.CandidateId;
            }
            return string.Empty;
        }

        private static void ValidateScores(FullCalibrationScore[] scores)
        {
            for (int index = 0; index < scores.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(scores[index].CandidateId))
                    throw new ArgumentException("Full calibration CandidateId is required.");
                for (int other = 0; other < index; other++)
                {
                    if (string.Equals(
                            scores[index].CandidateId,
                            scores[other].CandidateId,
                            StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            "Full calibration score CandidateId values must be unique.");
                    }
                }
            }
        }

        private static FullCalibrationScore? FindScore(
            FullCalibrationScore[] scores,
            string candidateId)
        {
            for (int index = 0; index < scores.Length; index++)
            {
                if (string.Equals(scores[index].CandidateId, candidateId, StringComparison.Ordinal))
                    return scores[index];
            }
            return null;
        }

        private static bool IsBetterScore(
            FullCalibrationScore candidate,
            FullCalibrationScore current,
            AdaptiveCandidateDecision[] order)
        {
            if (candidate.AmortizedP95MillisecondsPerTick !=
                current.AmortizedP95MillisecondsPerTick)
            {
                return candidate.AmortizedP95MillisecondsPerTick <
                       current.AmortizedP95MillisecondsPerTick;
            }
            return CandidateOrder(candidate.CandidateId, order) <
                   CandidateOrder(current.CandidateId, order);
        }

        private static int CandidateOrder(
            string candidateId,
            AdaptiveCandidateDecision[] order)
        {
            for (int index = 0; index < order.Length; index++)
            {
                if (string.Equals(
                        order[index].Candidate.CandidateId,
                        candidateId,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }
            return int.MaxValue;
        }

        private static bool Contains(string[] values, string candidateId)
        {
            if (values == null)
                return false;
            for (int index = 0; index < values.Length; index++)
            {
                if (string.Equals(values[index], candidateId, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static void RequireMetadata(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Required adaptive provenance metadata is missing.", name);
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

        private static int CompareEvidence(
            DecisionCandidateEvidence left,
            DecisionCandidateEvidence right)
        {
            return DecisionEvidenceStatistics.CompareCandidate(left.Candidate, right.Candidate);
        }

        private static bool IsStrictlyBelowThreshold(double value, double threshold)
        {
            double scale = Math.Max(1d, Math.Max(Math.Abs(value), Math.Abs(threshold)));
            return value < threshold - (ThresholdTolerance * scale);
        }
    }
}
