using System;

namespace Yanagisawa.DataLayoutCalibrator
{
    public static class LayoutSelector
    {
        public const double DefaultMinimumImprovementPercent = 10d;

        public static LayoutSelectionDecision SelectCalibration(
            LayoutBenchmarkResult[] results,
            int count,
            double minimumImprovementPercent = DefaultMinimumImprovementPercent,
            int bootstrapIterations = BenchmarkStatistics.DefaultBootstrapIterations,
            double bootstrapConfidenceLevel = BenchmarkStatistics.DefaultBootstrapConfidenceLevel,
            uint bootstrapSeed = 0xB5297A4Du)
        {
            if (results == null || count <= 0 || count > results.Length)
            {
                return InvalidDecision(minimumImprovementPercent, "No calibration results were supplied.");
            }

            if (minimumImprovementPercent < 0d || double.IsNaN(minimumImprovementPercent) ||
                double.IsInfinity(minimumImprovementPercent))
            {
                return InvalidDecision(0d, "The minimum improvement threshold is invalid.");
            }

            LayoutBenchmarkResult baseline = null;
            LayoutBenchmarkResult best = null;
            int eligibleCount = 0;
            int rejectedParityCount = 0;

            for (int i = 0; i < count; i++)
            {
                LayoutBenchmarkResult candidate = results[i];
                if (candidate != null && candidate.Completed && !candidate.ParityPassed)
                {
                    rejectedParityCount++;
                }

                if (!IsEligible(candidate))
                {
                    continue;
                }

                eligibleCount++;
                if (candidate.Candidate.IsBaseline && IsBetter(candidate, baseline))
                {
                    baseline = candidate;
                }

                if (IsBetter(candidate, best))
                {
                    best = candidate;
                }
            }

            if (baseline == null)
            {
                LayoutSelectionDecision invalid = InvalidDecision(
                    minimumImprovementPercent,
                    "No valid AoS result is available as the baseline.");
                invalid.EligibleCandidateCount = eligibleCount;
                invalid.RejectedParityCandidateCount = rejectedParityCount;
                return invalid;
            }

            double improvementPercent = ImprovementPercent(
                PrimaryP95(baseline),
                PrimaryP95(best));

            LayoutSelectionDecision decision = new LayoutSelectionDecision
            {
                DecisionStage = DecisionStage.Calibration,
                Status = LayoutSelectionStatus.Inconclusive,
                BaselineCandidate = baseline.Candidate,
                SelectedCandidate = baseline.Candidate,
                BestMeasuredCandidate = best.Candidate,
                BaselineP95Milliseconds = PrimaryP95(baseline),
                BestMeasuredP95Milliseconds = PrimaryP95(best),
                ImprovementPercent = improvementPercent,
                MinimumRequiredImprovementPercent = minimumImprovementPercent,
                SelectionRegretPercent = Math.Max(0d, improvementPercent),
                EligibleCandidateCount = eligibleCount,
                RejectedParityCandidateCount = rejectedParityCount,
                MultiplicityControl =
                    "Calibration winner selection followed by confirmation on an untouched holdout dataset.",
                Reason = "The best valid result did not clear the required P95 improvement over the best AoS result.",
            };

            if (!best.Candidate.IsBaseline)
            {
                if (!HasBootstrapSamples(baseline) || !HasBootstrapSamples(best))
                {
                    decision.Reason =
                        "Raw ingress/resident/export samples required for paired inference are missing; AoS remains selected.";
                    return decision;
                }

                if (!TryBootstrap(
                        baseline,
                        best,
                        bootstrapIterations,
                        bootstrapConfidenceLevel,
                        bootstrapSeed,
                        out BootstrapConfidenceInterval interval,
                        out string bootstrapFailure))
                {
                    decision.Reason =
                        "The paired calibration bootstrap evidence is invalid; AoS remains selected. " +
                        bootstrapFailure;
                    return decision;
                }
                decision.ImprovementConfidenceInterval = interval;
                if (interval.UpperBoundPercent < 0d)
                {
                    decision.Reason =
                        "The paired calibration interval supports a regression and contradicts the candidate ranking; the evidence is inconclusive and AoS remains selected.";
                    return decision;
                }

                if (interval.LowerBoundPercent <= 0d)
                {
                    decision.Status = LayoutSelectionStatus.StatisticalTie;
                    decision.FellBackBecauseStatisticalTie = true;
                    decision.Reason =
                        "The bootstrap confidence interval includes no improvement; the statistically tied result falls back to AoS.";
                    return decision;
                }

                if (improvementPercent < minimumImprovementPercent)
                {
                    decision.Reason =
                        "The paired interval excludes no effect, but the measured gain did not clear the minimum practical improvement; AoS remains selected.";
                    return decision;
                }

                decision.Status = LayoutSelectionStatus.Optimized;
                decision.SelectedCandidate = best.Candidate;
                decision.SelectionRegretPercent = 0d;
                decision.Reason =
                    "A non-AoS candidate cleared both the required amortized P95 improvement and the paired block-bootstrap significance gate.";
            }

            return decision;
        }

        public static LayoutSelectionDecision ConfirmHoldout(
            LayoutSelectionDecision calibrationDecision,
            LayoutBenchmarkResult baselineHoldout,
            LayoutBenchmarkResult selectedHoldout,
            double minimumImprovementPercent = DefaultMinimumImprovementPercent,
            int bootstrapIterations = BenchmarkStatistics.DefaultBootstrapIterations,
            double bootstrapConfidenceLevel = BenchmarkStatistics.DefaultBootstrapConfidenceLevel,
            uint bootstrapSeed = 0x68E31DA4u)
        {
            if (calibrationDecision.Status != LayoutSelectionStatus.Optimized)
            {
                return calibrationDecision;
            }

            if (minimumImprovementPercent < 0d ||
                double.IsNaN(minimumImprovementPercent) ||
                double.IsInfinity(minimumImprovementPercent))
            {
                return HoldoutFallback(calibrationDecision, "The holdout thresholds are invalid.");
            }

            if (!IsEligible(baselineHoldout) ||
                baselineHoldout.Phase != BenchmarkPhase.Holdout ||
                !MatchesFrozenCandidate(
                    baselineHoldout.Candidate,
                    calibrationDecision.BaselineCandidate))
            {
                LayoutSelectionDecision invalid = HoldoutFallback(
                    calibrationDecision,
                    "The holdout run has no valid matching AoS baseline.");
                invalid.Status = LayoutSelectionStatus.Invalid;
                return invalid;
            }

            if (!IsEligible(selectedHoldout) ||
                selectedHoldout.Phase != BenchmarkPhase.Holdout ||
                !MatchesFrozenCandidate(
                    selectedHoldout.Candidate,
                    calibrationDecision.SelectedCandidate))
            {
                return HoldoutFallback(
                    calibrationDecision,
                    "The selected candidate failed holdout validity or parity; the profile falls back to AoS.");
            }

            if (baselineHoldout.ElementCount != selectedHoldout.ElementCount)
            {
                return HoldoutFallback(
                    calibrationDecision,
                    "The holdout candidates used different element counts; the comparison is inconclusive.");
            }

            double holdoutImprovement = ImprovementPercent(
                PrimaryP95(baselineHoldout),
                PrimaryP95(selectedHoldout));
            LayoutSelectionDecision decision = new LayoutSelectionDecision
            {
                DecisionStage = DecisionStage.HoldoutConfirmation,
                Status = LayoutSelectionStatus.Optimized,
                BaselineCandidate = baselineHoldout.Candidate,
                SelectedCandidate = selectedHoldout.Candidate,
                BestMeasuredCandidate = selectedHoldout.Candidate,
                BaselineP95Milliseconds = PrimaryP95(baselineHoldout),
                BestMeasuredP95Milliseconds = PrimaryP95(selectedHoldout),
                ImprovementPercent = holdoutImprovement,
                MinimumRequiredImprovementPercent = minimumImprovementPercent,
                SelectionRegretPercent = 0d,
                EligibleCandidateCount = 2,
                RejectedParityCandidateCount = 0,
                MultiplicityControl = calibrationDecision.MultiplicityControl,
                Reason = "The frozen candidate repeated the required amortized P95 improvement and paired significance gate on untouched holdout data.",
            };

            if (!HasBootstrapSamples(baselineHoldout) || !HasBootstrapSamples(selectedHoldout))
            {
                decision.Status = LayoutSelectionStatus.Inconclusive;
                decision.SelectedCandidate = baselineHoldout.Candidate;
                decision.Reason =
                    "Holdout raw boundary and resident samples are missing; the profile falls back to AoS.";
                return decision;
            }

            if (!TryBootstrap(
                    baselineHoldout,
                    selectedHoldout,
                    bootstrapIterations,
                    bootstrapConfidenceLevel,
                    bootstrapSeed,
                    out BootstrapConfidenceInterval interval,
                    out string bootstrapFailure))
            {
                decision.Status = LayoutSelectionStatus.Inconclusive;
                decision.SelectedCandidate = baselineHoldout.Candidate;
                decision.SelectionRegretPercent = Math.Max(0d, holdoutImprovement);
                decision.Reason =
                    "The paired holdout bootstrap evidence is invalid; the profile falls back to AoS. " +
                    bootstrapFailure;
                return decision;
            }
            decision.ImprovementConfidenceInterval = interval;
            if (interval.UpperBoundPercent < 0d)
            {
                decision.Status = LayoutSelectionStatus.Regression;
                decision.SelectedCandidate = baselineHoldout.Candidate;
                decision.SelectionRegretPercent = 0d;
                decision.Reason =
                    "The frozen candidate is statistically slower on holdout data; the regression falls back to AoS.";
                return decision;
            }

            if (interval.LowerBoundPercent <= 0d && interval.UpperBoundPercent >= 0d)
            {
                decision.Status = LayoutSelectionStatus.StatisticalTie;
                decision.SelectedCandidate = baselineHoldout.Candidate;
                decision.SelectionRegretPercent = Math.Max(0d, holdoutImprovement);
                decision.FellBackBecauseStatisticalTie = true;
                decision.Reason =
                    "The paired holdout confidence interval includes no effect; the statistically tied result falls back to AoS.";
                return decision;
            }

            if (holdoutImprovement < minimumImprovementPercent)
            {
                decision.Status = LayoutSelectionStatus.Inconclusive;
                decision.SelectedCandidate = baselineHoldout.Candidate;
                decision.SelectionRegretPercent = Math.Max(0d, holdoutImprovement);
                decision.Reason =
                    "The frozen candidate is distinguishable from AoS but did not repeat the minimum practical improvement on holdout data; the profile falls back to AoS.";
                return decision;
            }

            return decision;
        }

        private static bool IsEligible(LayoutBenchmarkResult result)
        {
            if (result == null || !result.Completed || !result.ParityPassed ||
                result.HotPathManagedAllocationBytes != 0 ||
                result.BoundaryManagedAllocationBytes != 0 ||
                result.Candidate.LogicalBatchSize <= 0 ||
                result.Latency.SampleCount <= 0)
            {
                return false;
            }

            double p95 = PrimaryP95(result);
            return p95 > 0d && !double.IsNaN(p95) && !double.IsInfinity(p95);
        }

        private static bool IsBetter(LayoutBenchmarkResult candidate, LayoutBenchmarkResult current)
        {
            if (current == null)
            {
                return true;
            }

            double candidateP95 = PrimaryP95(candidate);
            double currentP95 = PrimaryP95(current);
            if (candidateP95 != currentP95)
            {
                return candidateP95 < currentP95;
            }

            double candidateMedian = PrimaryMedian(candidate);
            double currentMedian = PrimaryMedian(current);
            if (candidateMedian != currentMedian)
            {
                return candidateMedian < currentMedian;
            }

            if (candidate.Candidate.SortOrder != current.Candidate.SortOrder)
            {
                return candidate.Candidate.SortOrder < current.Candidate.SortOrder;
            }

            int idComparison = string.CompareOrdinal(
                candidate.Candidate.CandidateId,
                current.Candidate.CandidateId);
            if (idComparison != 0)
                return idComparison < 0;

            return candidate.Candidate.LogicalBatchSize < current.Candidate.LogicalBatchSize;
        }

        private static double ImprovementPercent(double baselineP95, double candidateP95)
        {
            return ((baselineP95 - candidateP95) / baselineP95) * 100d;
        }

        private static double PrimaryP95(LayoutBenchmarkResult result)
        {
            return result.AmortizedLatency.SampleCount > 0
                ? result.AmortizedLatency.P95Milliseconds
                : result.Latency.P95Milliseconds;
        }

        private static double PrimaryMedian(LayoutBenchmarkResult result)
        {
            return result.AmortizedLatency.SampleCount > 0
                ? result.AmortizedLatency.MedianMilliseconds
                : result.Latency.MedianMilliseconds;
        }

        private static bool HasBootstrapSamples(LayoutBenchmarkResult result)
        {
            return result.BoundaryCost.LifetimeTicks > 0 &&
                   result.ResidentSamplesMillisecondsPerTick != null &&
                   result.ResidentSamplesMillisecondsPerTick.Length >= 3 &&
                   result.IngressSamplesMilliseconds != null &&
                   result.IngressSamplesMilliseconds.Length >= 3 &&
                   result.ExportSamplesMilliseconds != null &&
                   result.ExportSamplesMilliseconds.Length >= 3;
        }

        private static bool MatchesFrozenCandidate(
            CandidateDescriptor measured,
            CandidateDescriptor frozen)
        {
            if (string.IsNullOrWhiteSpace(measured.CandidateId) ||
                !string.Equals(measured.CandidateId, frozen.CandidateId, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                CandidateDescriptor normalizedMeasured = measured.NormalizePolicies();
                CandidateDescriptor normalizedFrozen = frozen.NormalizePolicies();
                normalizedMeasured.ValidateFactorConsistency();
                normalizedFrozen.ValidateFactorConsistency();
                return normalizedMeasured == normalizedFrozen;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool TryBootstrap(
            LayoutBenchmarkResult baseline,
            LayoutBenchmarkResult candidate,
            int iterations,
            double confidenceLevel,
            uint seed,
            out BootstrapConfidenceInterval interval,
            out string failure)
        {
            try
            {
                interval = BenchmarkStatistics.BootstrapAmortizedP95Improvement(
                    baseline,
                    candidate,
                    iterations,
                    confidenceLevel,
                    seed);
                failure = string.Empty;
                return true;
            }
            catch (ArgumentException exception)
            {
                interval = default;
                failure = exception.Message;
                return false;
            }
        }

        private static LayoutSelectionDecision InvalidDecision(double minimumImprovementPercent, string reason)
        {
            return new LayoutSelectionDecision
            {
                DecisionStage = DecisionStage.Calibration,
                Status = LayoutSelectionStatus.Invalid,
                MinimumRequiredImprovementPercent = minimumImprovementPercent,
                MultiplicityControl =
                    "Calibration winner selection followed by confirmation on an untouched holdout dataset.",
                Reason = reason,
            };
        }

        private static LayoutSelectionDecision HoldoutFallback(
            LayoutSelectionDecision calibrationDecision,
            string reason)
        {
            calibrationDecision.Status = LayoutSelectionStatus.Inconclusive;
            calibrationDecision.DecisionStage = DecisionStage.HoldoutConfirmation;
            calibrationDecision.SelectedCandidate = calibrationDecision.BaselineCandidate;
            calibrationDecision.SelectionRegretPercent = Math.Max(
                0d,
                calibrationDecision.ImprovementPercent);
            calibrationDecision.Reason = reason;
            return calibrationDecision;
        }
    }
}
