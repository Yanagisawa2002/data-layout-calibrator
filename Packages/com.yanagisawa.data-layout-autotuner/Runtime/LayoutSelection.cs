namespace Yanagisawa.DataLayoutAutotuner
{
    public static class LayoutSelector
    {
        public const double DefaultMinimumImprovementPercent = 10d;

        public static LayoutSelectionDecision SelectCalibration(
            LayoutBenchmarkResult[] results,
            int count,
            double minimumImprovementPercent = DefaultMinimumImprovementPercent)
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
                if (candidate.Candidate.Layout == LayoutKind.AoS && IsBetter(candidate, baseline))
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
                baseline.Latency.P95Milliseconds,
                best.Latency.P95Milliseconds);

            LayoutSelectionDecision decision = new LayoutSelectionDecision
            {
                Status = LayoutSelectionStatus.Inconclusive,
                BaselineCandidate = baseline.Candidate,
                SelectedCandidate = baseline.Candidate,
                BestMeasuredCandidate = best.Candidate,
                BaselineP95Milliseconds = baseline.Latency.P95Milliseconds,
                BestMeasuredP95Milliseconds = best.Latency.P95Milliseconds,
                ImprovementPercent = improvementPercent,
                MinimumRequiredImprovementPercent = minimumImprovementPercent,
                EligibleCandidateCount = eligibleCount,
                RejectedParityCandidateCount = rejectedParityCount,
                Reason = "The best valid result did not clear the required P95 improvement over the best AoS result.",
            };

            if (best.Candidate.Layout != LayoutKind.AoS && improvementPercent >= minimumImprovementPercent)
            {
                decision.Status = LayoutSelectionStatus.Optimized;
                decision.SelectedCandidate = best.Candidate;
                decision.Reason = "A non-AoS candidate cleared the required P95 improvement over the best AoS result.";
            }

            return decision;
        }

        public static LayoutSelectionDecision ConfirmHoldout(
            LayoutSelectionDecision calibrationDecision,
            LayoutBenchmarkResult baselineHoldout,
            LayoutBenchmarkResult selectedHoldout,
            double minimumImprovementPercent = DefaultMinimumImprovementPercent)
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
                baselineHoldout.Candidate != calibrationDecision.BaselineCandidate)
            {
                LayoutSelectionDecision invalid = HoldoutFallback(
                    calibrationDecision,
                    "The holdout run has no valid matching AoS baseline.");
                invalid.Status = LayoutSelectionStatus.Invalid;
                return invalid;
            }

            if (!IsEligible(selectedHoldout) ||
                selectedHoldout.Candidate != calibrationDecision.SelectedCandidate)
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
                baselineHoldout.Latency.P95Milliseconds,
                selectedHoldout.Latency.P95Milliseconds);
            LayoutSelectionDecision decision = new LayoutSelectionDecision
            {
                Status = LayoutSelectionStatus.Optimized,
                BaselineCandidate = baselineHoldout.Candidate,
                SelectedCandidate = selectedHoldout.Candidate,
                BestMeasuredCandidate = selectedHoldout.Candidate,
                BaselineP95Milliseconds = baselineHoldout.Latency.P95Milliseconds,
                BestMeasuredP95Milliseconds = selectedHoldout.Latency.P95Milliseconds,
                ImprovementPercent = holdoutImprovement,
                MinimumRequiredImprovementPercent = minimumImprovementPercent,
                EligibleCandidateCount = 2,
                RejectedParityCandidateCount = 0,
                Reason = "The selected candidate repeated the required P95 improvement on holdout data.",
            };

            if (holdoutImprovement < minimumImprovementPercent)
            {
                decision.Status = LayoutSelectionStatus.Inconclusive;
                decision.SelectedCandidate = baselineHoldout.Candidate;
                decision.Reason = "The selected candidate did not repeat the required P95 improvement on holdout data; the profile falls back to AoS.";
                return decision;
            }

            return decision;
        }

        private static bool IsEligible(LayoutBenchmarkResult result)
        {
            if (result == null || !result.Completed || !result.ParityPassed ||
                result.HotPathManagedAllocationBytes != 0 || result.Candidate.LogicalBatchSize <= 0 ||
                result.Latency.SampleCount <= 0)
            {
                return false;
            }

            double p95 = result.Latency.P95Milliseconds;
            return p95 > 0d && !double.IsNaN(p95) && !double.IsInfinity(p95);
        }

        private static bool IsBetter(LayoutBenchmarkResult candidate, LayoutBenchmarkResult current)
        {
            if (current == null)
            {
                return true;
            }

            double candidateP95 = candidate.Latency.P95Milliseconds;
            double currentP95 = current.Latency.P95Milliseconds;
            if (candidateP95 != currentP95)
            {
                return candidateP95 < currentP95;
            }

            double candidateMedian = candidate.Latency.MedianMilliseconds;
            double currentMedian = current.Latency.MedianMilliseconds;
            if (candidateMedian != currentMedian)
            {
                return candidateMedian < currentMedian;
            }

            if (candidate.Candidate.Layout != current.Candidate.Layout)
            {
                return (int)candidate.Candidate.Layout < (int)current.Candidate.Layout;
            }

            return candidate.Candidate.LogicalBatchSize < current.Candidate.LogicalBatchSize;
        }

        private static double ImprovementPercent(double baselineP95, double candidateP95)
        {
            return ((baselineP95 - candidateP95) / baselineP95) * 100d;
        }

        private static LayoutSelectionDecision InvalidDecision(double minimumImprovementPercent, string reason)
        {
            return new LayoutSelectionDecision
            {
                Status = LayoutSelectionStatus.Invalid,
                MinimumRequiredImprovementPercent = minimumImprovementPercent,
                Reason = reason,
            };
        }

        private static LayoutSelectionDecision HoldoutFallback(
            LayoutSelectionDecision calibrationDecision,
            string reason)
        {
            calibrationDecision.Status = LayoutSelectionStatus.Inconclusive;
            calibrationDecision.SelectedCandidate = calibrationDecision.BaselineCandidate;
            calibrationDecision.Reason = reason;
            return calibrationDecision;
        }
    }
}
