using System;
using System.Collections.Generic;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>
    /// Deterministic decision math over aligned bootstrap replicates supplied by
    /// the scientific layer. This type deliberately does not choose a resampling
    /// design or inspect raw Player measurements.
    /// </summary>
    internal static class DecisionEvidenceStatistics
    {
        private const double EqualityTolerance = 1e-12d;

        internal static CandidateEvidenceGateStatus EvaluateGate(
            DecisionCandidateEvidence evidence,
            int minimumResidentSamples,
            int minimumBoundarySamples,
            int minimumBootstrapReplicates,
            string expectedPartitionId,
            out string reason)
        {
            CandidateEvidenceGateStatus feasibility = EvaluateFeasibilityGate(
                evidence,
                expectedPartitionId,
                out reason);
            if (feasibility != CandidateEvidenceGateStatus.Eligible)
                return feasibility;

            if (evidence.ResidentSampleCount < minimumResidentSamples ||
                evidence.BoundarySampleCount < minimumBoundarySamples)
            {
                reason = "The evidence does not contain the required resident and boundary sample counts.";
                return CandidateEvidenceGateStatus.InsufficientSamples;
            }

            if (!TryNormalizeReplicates(
                    evidence.BootstrapReplicates,
                    minimumBootstrapReplicates,
                    out _,
                    out reason))
            {
                return CandidateEvidenceGateStatus.InvalidUncertaintyEvidence;
            }

            return CandidateEvidenceGateStatus.Eligible;
        }

        internal static CandidateEvidenceGateStatus EvaluateFeasibilityGate(
            DecisionCandidateEvidence evidence,
            string expectedPartitionId,
            out string reason)
        {
            reason = string.Empty;
            if (evidence == null || !evidence.Completed)
            {
                reason = "Measurement did not complete.";
                return CandidateEvidenceGateStatus.Incomplete;
            }

            if (!HasValidCandidateDescriptor(evidence.Candidate, out reason))
                return CandidateEvidenceGateStatus.InvalidPointEstimate;

            if (!evidence.ContractFeasible)
            {
                reason = "The candidate failed its declared contract feasibility screen.";
                return CandidateEvidenceGateStatus.ContractInfeasible;
            }

            if (!evidence.MemoryFeasible || evidence.ResidentBytes < 0L)
            {
                reason = "The candidate failed its memory feasibility screen.";
                return CandidateEvidenceGateStatus.MemoryInfeasible;
            }

            if (!evidence.ParityPassed)
            {
                reason = "The candidate failed canonical parity.";
                return CandidateEvidenceGateStatus.ParityFailed;
            }

            if (evidence.HotPathManagedAllocationBytes != 0L ||
                evidence.BoundaryManagedAllocationBytes != 0L)
            {
                reason = "The candidate recorded managed allocation in resident or boundary work.";
                return CandidateEvidenceGateStatus.ManagedAllocationDetected;
            }

            if (!IsFiniteNonNegative(evidence.ResidentP95MillisecondsPerTick) ||
                !IsFiniteNonNegative(evidence.IngressP95Milliseconds) ||
                !IsFiniteNonNegative(evidence.ExportP95Milliseconds))
            {
                reason = "Point cost components must be finite and non-negative.";
                return CandidateEvidenceGateStatus.InvalidPointEstimate;
            }

            double pointComponentTotal = evidence.ResidentP95MillisecondsPerTick +
                                         evidence.IngressP95Milliseconds +
                                         evidence.ExportP95Milliseconds;
            if (!(pointComponentTotal > 0d) || double.IsInfinity(pointComponentTotal))
            {
                reason = "At least one point cost component must be positive.";
                return CandidateEvidenceGateStatus.InvalidPointEstimate;
            }

            if (evidence.ResidentSampleCount < 0 || evidence.BoundarySampleCount < 0)
            {
                reason = "Recorded sample counts must not be negative.";
                return CandidateEvidenceGateStatus.InsufficientSamples;
            }

            if (string.IsNullOrWhiteSpace(evidence.EvidencePartitionId) ||
                string.IsNullOrWhiteSpace(evidence.EvidenceHash))
            {
                reason = "Evidence partition and hash are required for provenance.";
                return CandidateEvidenceGateStatus.InvalidUncertaintyEvidence;
            }

            if (!string.IsNullOrEmpty(expectedPartitionId) &&
                !string.Equals(
                    expectedPartitionId,
                    evidence.EvidencePartitionId,
                    StringComparison.Ordinal))
            {
                reason = "The candidate evidence came from a different partition than tuned AoS.";
                return CandidateEvidenceGateStatus.EvidencePartitionMismatch;
            }

            return CandidateEvidenceGateStatus.Eligible;
        }

        internal static bool HasValidCandidateDescriptor(
            EnvelopeCandidateDescriptor candidate,
            out string reason)
        {
            if (string.IsNullOrWhiteSpace(candidate.CandidateId) ||
                string.IsNullOrWhiteSpace(candidate.LayoutPolicyId) ||
                string.IsNullOrWhiteSpace(candidate.KernelPolicyId) ||
                string.IsNullOrWhiteSpace(candidate.BatchPolicyId) ||
                string.IsNullOrWhiteSpace(candidate.ExecutionPolicyId) ||
                candidate.LogicalBatchSize <= 0)
            {
                reason = "CandidateId and every stable factor ID are required.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        internal static bool DescriptorsMatch(
            EnvelopeCandidateDescriptor expected,
            EnvelopeCandidateDescriptor actual)
        {
            return expected == actual;
        }

        internal static double AmortizedCost(
            double residentP95MillisecondsPerTick,
            double ingressP95Milliseconds,
            double exportP95Milliseconds,
            int lifetimeTicks)
        {
            if (lifetimeTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(lifetimeTicks));
            if (!IsFiniteNonNegative(residentP95MillisecondsPerTick) ||
                !IsFiniteNonNegative(ingressP95Milliseconds) ||
                !IsFiniteNonNegative(exportP95Milliseconds))
            {
                throw new ArgumentException("Cost components must be finite and non-negative.");
            }

            double boundary = ingressP95Milliseconds + exportP95Milliseconds;
            double cost = residentP95MillisecondsPerTick + (boundary / lifetimeTicks);
            if (double.IsInfinity(boundary) || double.IsInfinity(cost))
                throw new ArgumentException("Composite cost overflowed.");
            return cost;
        }

        internal static double AmortizedCost(
            DecisionCandidateEvidence evidence,
            int lifetimeTicks)
        {
            if (evidence == null)
                throw new ArgumentNullException(nameof(evidence));
            return AmortizedCost(
                evidence.ResidentP95MillisecondsPerTick,
                evidence.IngressP95Milliseconds,
                evidence.ExportP95Milliseconds,
                lifetimeTicks);
        }

        internal static EnvelopeConfidenceInterval CalculateImprovementInterval(
            DecisionCandidateEvidence baseline,
            DecisionCandidateEvidence candidate,
            int lifetimeTicks,
            double confidenceLevel)
        {
            if (baseline == null)
                throw new ArgumentNullException(nameof(baseline));
            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));
            ValidateConfidenceLevel(confidenceLevel);

            GetAlignedReplicates(
                baseline.BootstrapReplicates,
                candidate.BootstrapReplicates,
                out BootstrapCostReplicate[] baselineReplicates,
                out BootstrapCostReplicate[] candidateReplicates);

            double baselinePoint = AmortizedCost(baseline, lifetimeTicks);
            double candidatePoint = AmortizedCost(candidate, lifetimeTicks);
            if (!(baselinePoint > 0d))
                throw new ArgumentException("Tuned AoS point cost must be positive.", nameof(baseline));

            var improvements = new double[baselineReplicates.Length];
            for (int index = 0; index < improvements.Length; index++)
            {
                double baselineCost = AmortizedCost(
                    baselineReplicates[index].ResidentP95MillisecondsPerTick,
                    baselineReplicates[index].IngressP95Milliseconds,
                    baselineReplicates[index].ExportP95Milliseconds,
                    lifetimeTicks);
                double candidateCost = AmortizedCost(
                    candidateReplicates[index].ResidentP95MillisecondsPerTick,
                    candidateReplicates[index].IngressP95Milliseconds,
                    candidateReplicates[index].ExportP95Milliseconds,
                    lifetimeTicks);
                if (!(baselineCost > 0d))
                {
                    throw new ArgumentException(
                        "Every tuned AoS bootstrap cost must be positive.",
                        nameof(baseline));
                }

                improvements[index] = ImprovementPercent(baselineCost, candidateCost);
            }

            Array.Sort(improvements);
            double tail = (1d - confidenceLevel) * 0.5d;
            return new EnvelopeConfidenceInterval
            {
                ReplicateCount = improvements.Length,
                ConfidenceLevel = confidenceLevel,
                PointEstimatePercent = ImprovementPercent(baselinePoint, candidatePoint),
                LowerBoundPercent = PercentileOfSorted(improvements, tail),
                UpperBoundPercent = PercentileOfSorted(improvements, 1d - tail),
            };
        }

        internal static BreakEvenEstimate CalculateBreakEven(
            DecisionCandidateEvidence baseline,
            DecisionCandidateEvidence candidate,
            double confidenceLevel)
        {
            if (baseline == null)
                throw new ArgumentNullException(nameof(baseline));
            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));
            ValidateConfidenceLevel(confidenceLevel);

            double baselineBoundary = baseline.IngressP95Milliseconds + baseline.ExportP95Milliseconds;
            double candidateBoundary = candidate.IngressP95Milliseconds + candidate.ExportP95Milliseconds;
            if (!IsFiniteNonNegative(baseline.ResidentP95MillisecondsPerTick) ||
                !IsFiniteNonNegative(candidate.ResidentP95MillisecondsPerTick) ||
                !IsFiniteNonNegative(baselineBoundary) ||
                !IsFiniteNonNegative(candidateBoundary))
            {
                throw new ArgumentException(
                    "Break-even point components must be finite and non-negative.");
            }
            double residentDelta = NormalizeDelta(
                candidate.ResidentP95MillisecondsPerTick,
                baseline.ResidentP95MillisecondsPerTick);
            double boundaryDelta = NormalizeDelta(candidateBoundary, baselineBoundary);
            BreakEvenKind pointKind = ClassifyBreakEvenDeltas(
                residentDelta,
                boundaryDelta,
                out double pointLifetime);

            GetAlignedReplicates(
                baseline.BootstrapReplicates,
                candidate.BootstrapReplicates,
                out BootstrapCostReplicate[] baselineReplicates,
                out BootstrapCostReplicate[] candidateReplicates);

            var sameRegimeCrossings = new List<double>();
            int equalCount = 0;
            int alwaysCount = 0;
            int neverCount = 0;
            int aboveCount = 0;
            int belowCount = 0;
            int sameRegimeCount = 0;

            for (int index = 0; index < baselineReplicates.Length; index++)
            {
                BootstrapCostReplicate baselineReplicate = baselineReplicates[index];
                BootstrapCostReplicate candidateReplicate = candidateReplicates[index];
                double replicateResidentDelta = NormalizeDelta(
                    candidateReplicate.ResidentP95MillisecondsPerTick,
                    baselineReplicate.ResidentP95MillisecondsPerTick);
                double replicateBoundaryDelta = NormalizeDelta(
                    candidateReplicate.IngressP95Milliseconds +
                    candidateReplicate.ExportP95Milliseconds,
                    baselineReplicate.IngressP95Milliseconds +
                    baselineReplicate.ExportP95Milliseconds);
                BreakEvenKind kind = ClassifyBreakEvenDeltas(
                    replicateResidentDelta,
                    replicateBoundaryDelta,
                    out double crossing);

                switch (kind)
                {
                    case BreakEvenKind.EqualCosts:
                        equalCount++;
                        break;
                    case BreakEvenKind.CandidateAlwaysAdvantaged:
                        alwaysCount++;
                        break;
                    case BreakEvenKind.CandidateNeverAdvantaged:
                        neverCount++;
                        break;
                    case BreakEvenKind.CandidateWinsAboveLifetime:
                        aboveCount++;
                        break;
                    case BreakEvenKind.CandidateWinsBelowLifetime:
                        belowCount++;
                        break;
                }

                if (kind == pointKind)
                {
                    sameRegimeCount++;
                    if (kind == BreakEvenKind.CandidateWinsAboveLifetime ||
                        kind == BreakEvenKind.CandidateWinsBelowLifetime)
                    {
                        sameRegimeCrossings.Add(crossing);
                    }
                }
            }

            double agreement = baselineReplicates.Length == 0
                ? 0d
                : (sameRegimeCount * 100d) / baselineReplicates.Length;
            bool stable = (sameRegimeCount / (double)baselineReplicates.Length) +
                          EqualityTolerance >= confidenceLevel;
            BreakEvenUncertaintyStatus uncertaintyStatus;
            double lower = 0d;
            double upper = 0d;
            if (pointKind == BreakEvenKind.CandidateWinsAboveLifetime ||
                pointKind == BreakEvenKind.CandidateWinsBelowLifetime)
            {
                if (sameRegimeCrossings.Count > 0)
                {
                    double[] crossings = sameRegimeCrossings.ToArray();
                    Array.Sort(crossings);
                    double tail = (1d - confidenceLevel) * 0.5d;
                    lower = PercentileOfSorted(crossings, tail);
                    upper = PercentileOfSorted(crossings, 1d - tail);
                }
                uncertaintyStatus = stable
                    ? BreakEvenUncertaintyStatus.BoundedCrossing
                    : BreakEvenUncertaintyStatus.MixedRegimes;
            }
            else
            {
                uncertaintyStatus = stable
                    ? BreakEvenUncertaintyStatus.StableRegime
                    : BreakEvenUncertaintyStatus.MixedRegimes;
            }

            return new BreakEvenEstimate
            {
                Kind = pointKind,
                UncertaintyStatus = uncertaintyStatus,
                ResidentDeltaMillisecondsPerTick = residentDelta,
                BoundaryDeltaMilliseconds = boundaryDelta,
                PointLifetimeTicks = pointLifetime,
                LowerConfidenceLifetimeTicks = lower,
                UpperConfidenceLifetimeTicks = upper,
                ReplicateCount = baselineReplicates.Length,
                SameRegimeReplicateCount = sameRegimeCount,
                SameRegimePercent = agreement,
                EqualCostReplicateCount = equalCount,
                AlwaysAdvantagedReplicateCount = alwaysCount,
                NeverAdvantagedReplicateCount = neverCount,
                WinsAboveLifetimeReplicateCount = aboveCount,
                WinsBelowLifetimeReplicateCount = belowCount,
            };
        }

        internal static BreakEvenKind ClassifyBreakEven(
            double baselineResidentP95MillisecondsPerTick,
            double baselineBoundaryP95Milliseconds,
            double candidateResidentP95MillisecondsPerTick,
            double candidateBoundaryP95Milliseconds,
            out double crossingLifetimeTicks)
        {
            if (!IsFiniteNonNegative(baselineResidentP95MillisecondsPerTick) ||
                !IsFiniteNonNegative(baselineBoundaryP95Milliseconds) ||
                !IsFiniteNonNegative(candidateResidentP95MillisecondsPerTick) ||
                !IsFiniteNonNegative(candidateBoundaryP95Milliseconds))
            {
                crossingLifetimeTicks = 0d;
                return BreakEvenKind.Invalid;
            }

            double residentDelta = NormalizeDelta(
                candidateResidentP95MillisecondsPerTick,
                baselineResidentP95MillisecondsPerTick);
            double boundaryDelta = NormalizeDelta(
                candidateBoundaryP95Milliseconds,
                baselineBoundaryP95Milliseconds);
            return ClassifyBreakEvenDeltas(
                residentDelta,
                boundaryDelta,
                out crossingLifetimeTicks);
        }

        internal static int CompareAxis(AdvantageEnvelopeAxis left, AdvantageEnvelopeAxis right)
        {
            int comparison = string.CompareOrdinal(left.ExecutionPolicyId, right.ExecutionPolicyId);
            if (comparison != 0)
                return comparison;
            comparison = left.WorkerCount.CompareTo(right.WorkerCount);
            if (comparison != 0)
                return comparison;
            comparison = left.HotToColdRatio.CompareTo(right.HotToColdRatio);
            if (comparison != 0)
                return comparison;
            comparison = left.ElementCount.CompareTo(right.ElementCount);
            if (comparison != 0)
                return comparison;
            return left.LifetimeTicks.CompareTo(right.LifetimeTicks);
        }

        internal static bool SameRegionAxes(
            AdvantageEnvelopeAxis left,
            AdvantageEnvelopeAxis right)
        {
            return left.ElementCount == right.ElementCount &&
                   left.HotToColdRatio.Equals(right.HotToColdRatio) &&
                   left.WorkerCount == right.WorkerCount &&
                   string.Equals(
                       left.ExecutionPolicyId,
                       right.ExecutionPolicyId,
                       StringComparison.Ordinal);
        }

        internal static int CompareCandidate(
            EnvelopeCandidateDescriptor left,
            EnvelopeCandidateDescriptor right)
        {
            int comparison = left.SortOrder.CompareTo(right.SortOrder);
            if (comparison != 0)
                return comparison;
            comparison = string.CompareOrdinal(left.CandidateId, right.CandidateId);
            if (comparison != 0)
                return comparison;
            comparison = string.CompareOrdinal(left.LayoutPolicyId, right.LayoutPolicyId);
            if (comparison != 0)
                return comparison;
            comparison = string.CompareOrdinal(left.KernelPolicyId, right.KernelPolicyId);
            if (comparison != 0)
                return comparison;
            comparison = string.CompareOrdinal(left.BatchPolicyId, right.BatchPolicyId);
            if (comparison != 0)
                return comparison;
            return string.CompareOrdinal(left.ExecutionPolicyId, right.ExecutionPolicyId);
        }

        internal static bool IsFiniteNonNegative(double value)
        {
            return value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        internal static double Percentile(double[] values, double percentile)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException("At least one value is required.", nameof(values));
            if (percentile < 0d || percentile > 1d || double.IsNaN(percentile))
                throw new ArgumentOutOfRangeException(nameof(percentile));
            var sorted = new double[values.Length];
            Array.Copy(values, sorted, values.Length);
            Array.Sort(sorted);
            return PercentileOfSorted(sorted, percentile);
        }

        private static BreakEvenKind ClassifyBreakEvenDeltas(
            double residentDelta,
            double boundaryDelta,
            out double crossingLifetimeTicks)
        {
            crossingLifetimeTicks = 0d;
            if (residentDelta == 0d && boundaryDelta == 0d)
                return BreakEvenKind.EqualCosts;
            if (residentDelta <= 0d && boundaryDelta <= 0d)
                return BreakEvenKind.CandidateAlwaysAdvantaged;
            if (residentDelta >= 0d && boundaryDelta >= 0d)
                return BreakEvenKind.CandidateNeverAdvantaged;
            if (residentDelta < 0d && boundaryDelta > 0d)
            {
                crossingLifetimeTicks = boundaryDelta / -residentDelta;
                return BreakEvenKind.CandidateWinsAboveLifetime;
            }
            if (residentDelta > 0d && boundaryDelta < 0d)
            {
                crossingLifetimeTicks = -boundaryDelta / residentDelta;
                return BreakEvenKind.CandidateWinsBelowLifetime;
            }

            return BreakEvenKind.Invalid;
        }

        private static double NormalizeDelta(double candidate, double baseline)
        {
            double delta = candidate - baseline;
            double scale = Math.Max(1d, Math.Max(Math.Abs(candidate), Math.Abs(baseline)));
            return Math.Abs(delta) <= EqualityTolerance * scale ? 0d : delta;
        }

        private static double ImprovementPercent(double baselineCost, double candidateCost)
        {
            double result = ((baselineCost - candidateCost) / baselineCost) * 100d;
            if (double.IsNaN(result) || double.IsInfinity(result))
                throw new ArgumentException("Improvement percentage is not finite.");
            return result;
        }

        private static bool TryNormalizeReplicates(
            BootstrapCostReplicate[] source,
            int minimumCount,
            out BootstrapCostReplicate[] normalized,
            out string reason)
        {
            normalized = null;
            reason = string.Empty;
            if (source == null || source.Length < minimumCount)
            {
                reason = "The uncertainty evidence has too few bootstrap replicates.";
                return false;
            }

            normalized = new BootstrapCostReplicate[source.Length];
            Array.Copy(source, normalized, source.Length);
            Array.Sort(normalized, CompareReplicate);
            for (int index = 0; index < normalized.Length; index++)
            {
                BootstrapCostReplicate replicate = normalized[index];
                double componentTotal = replicate.ResidentP95MillisecondsPerTick +
                                        replicate.IngressP95Milliseconds +
                                        replicate.ExportP95Milliseconds;
                if (replicate.ReplicateId < 0 ||
                    !IsFiniteNonNegative(replicate.ResidentP95MillisecondsPerTick) ||
                    !IsFiniteNonNegative(replicate.IngressP95Milliseconds) ||
                    !IsFiniteNonNegative(replicate.ExportP95Milliseconds) ||
                    !(componentTotal > 0d) || double.IsInfinity(componentTotal))
                {
                    reason = "Bootstrap replicate IDs and cost components must be valid, with a positive composite cost.";
                    normalized = null;
                    return false;
                }
                if (index > 0 && normalized[index - 1].ReplicateId == replicate.ReplicateId)
                {
                    reason = "Bootstrap replicate IDs must be unique per candidate.";
                    normalized = null;
                    return false;
                }
            }

            return true;
        }

        private static void GetAlignedReplicates(
            BootstrapCostReplicate[] baselineSource,
            BootstrapCostReplicate[] candidateSource,
            out BootstrapCostReplicate[] baseline,
            out BootstrapCostReplicate[] candidate)
        {
            if (!TryNormalizeReplicates(baselineSource, 1, out baseline, out string baselineReason))
                throw new ArgumentException(baselineReason, nameof(baselineSource));
            if (!TryNormalizeReplicates(candidateSource, 1, out candidate, out string candidateReason))
                throw new ArgumentException(candidateReason, nameof(candidateSource));
            if (baseline.Length != candidate.Length)
            {
                throw new ArgumentException(
                    "Baseline and candidate bootstrap replicate counts differ.",
                    nameof(candidateSource));
            }

            for (int index = 0; index < baseline.Length; index++)
            {
                if (baseline[index].ReplicateId != candidate[index].ReplicateId)
                {
                    throw new ArgumentException(
                        "Baseline and candidate bootstrap replicate IDs are not aligned.",
                        nameof(candidateSource));
                }
            }
        }

        private static int CompareReplicate(
            BootstrapCostReplicate left,
            BootstrapCostReplicate right)
        {
            return left.ReplicateId.CompareTo(right.ReplicateId);
        }

        private static double PercentileOfSorted(double[] sorted, double percentile)
        {
            if (sorted.Length == 1)
                return sorted[0];
            double rank = (sorted.Length - 1) * percentile;
            int lower = (int)rank;
            int upper = Math.Min(lower + 1, sorted.Length - 1);
            double fraction = rank - lower;
            return (sorted[lower] * (1d - fraction)) +
                   (sorted[upper] * fraction);
        }

        private static void ValidateConfidenceLevel(double confidenceLevel)
        {
            if (!(confidenceLevel > 0d && confidenceLevel < 1d) ||
                double.IsNaN(confidenceLevel) || double.IsInfinity(confidenceLevel))
            {
                throw new ArgumentOutOfRangeException(nameof(confidenceLevel));
            }
        }
    }
}
