using System;
using System.Collections.Generic;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>One fresh process completing an application task. Elapsed time
    /// includes the declared input, ownership, updates, exports and consumer.
    /// This is never a component-P95 score or an independent tick sample.</summary>
    [Serializable]
    public sealed class WholeTaskProcessSample
    {
        public string ProcessIdentity;
        public string PairId;
        public string CandidateId;
        public string PartitionId;
        public string DatasetHash;
        public string SourceFingerprint;
        public string ContractId;
        public bool Observed;
        public bool Completed;
        public bool ParityPassed;
        public bool IncludesCompleteBoundary;
        public bool EnvironmentQualified;
        public bool Diagnostic;
        public double CompleteMilliseconds;
    }

    /// <summary>A provisional timing recommendation for an explicit experiment.
    /// It is not a frozen deployment profile and cannot certify allocation scope.</summary>
    [Serializable]
    public sealed class WholeTaskTimingDecision
    {
        public string BaselineCandidateId;
        public string CandidateId;
        public string CalibrationPartitionId;
        public string CalibrationDatasetHash;
        public string SourceFingerprint;
        public string ContractId;
        public int IndependentProcessPairs;
        public double ImprovementPercent;
        public string CalibrationBestCandidateId;
        public double CalibrationBestImprovementPercent;
        public double? CalibrationBestLowerImprovementPercent;
        public double? CalibrationBestUpperImprovementPercent;
        public double? LowerImprovementPercent;
        public double? UpperImprovementPercent;
        public double MinimumImprovementPercent;
        public bool TimingGatePassed;
        public string Reason;
        public string AllocationEligibility = "Unknown";
        public bool IsDeploymentProfile = false;
        public string[] CalibrationProcessIdentities;
    }

    [Serializable]
    public sealed class WholeTaskTimingConfirmation
    {
        public string ExecutedCandidateId;
        public string BaselineCandidateId;
        public string ConfirmationPartitionId;
        public string ConfirmationDatasetHash;
        public int IndependentProcessPairs;
        public double ImprovementPercent;
        public double LowerImprovementPercent;
        public double UpperImprovementPercent;
        public bool ConfirmedTimingGain;
        public string AllocationEligibility = "Unknown";
        public bool IsDeploymentProfile = false;
        public string Reason;
    }

    /// <summary>Ranks observed complete-task means on an explicitly supplied
    /// calibration partition, using paired fresh-process bootstrap uncertainty.
    /// A caller freezes this decision BEFORE executing independent task inputs.
    /// Holdout outcomes cannot enter Select. Existing component-P95 selection
    /// and deployment/allocation gates are unchanged.</summary>
    public static class WholeTaskLayoutSelector
    {
        public static WholeTaskTimingDecision Select(WholeTaskProcessSample[] calibration,
            string[] frozenCandidateIds, string baselineId, string calibrationPartitionId,
            double minimumImprovementPercent = 2.0, int bootstrapIterations = 10000)
        {
            if (calibration == null || frozenCandidateIds == null || frozenCandidateIds.Length < 2 ||
                string.IsNullOrWhiteSpace(calibrationPartitionId) ||
                minimumImprovementPercent < 0 || !Finite(minimumImprovementPercent) || bootstrapIterations < 1000)
                throw new ArgumentException("Invalid calibration protocol.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var groups = new Dictionary<string, SortedDictionary<string, WholeTaskProcessSample>>(StringComparer.Ordinal);
            foreach (string id in frozenCandidateIds)
            {
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new ArgumentException("Invalid frozen candidates.");
                groups.Add(id, new SortedDictionary<string, WholeTaskProcessSample>(StringComparer.Ordinal));
            }
            if (!groups.ContainsKey(baselineId)) throw new ArgumentException("Missing fixed baseline.");
            var processes = new HashSet<string>(StringComparer.Ordinal);
            WholeTaskProcessSample first = null;
            foreach (var item in calibration)
            {
                Validate(item);
                if (item.PartitionId != calibrationPartitionId || !groups.ContainsKey(item.CandidateId) ||
                    !processes.Add(item.ProcessIdentity))
                    throw new ArgumentException("Unexpected partition/candidate or repeated process; ticks are not replications.");
                if (first == null) first = item;
                if (item.DatasetHash != first.DatasetHash || item.SourceFingerprint != first.SourceFingerprint ||
                    item.ContractId != first.ContractId)
                    throw new ArgumentException("Mixed calibration input, source or task boundary.");
                if (groups[item.CandidateId].ContainsKey(item.PairId)) throw new ArgumentException("Duplicate pair.");
                groups[item.CandidateId].Add(item.PairId, item);
            }
            var baseline = groups[baselineId];
            if (baseline.Count < 3) throw new ArgumentException("At least three independent process pairs required.");
            foreach (var group in groups.Values)
            {
                if (group.Count != baseline.Count) throw new ArgumentException("Unbalanced candidate coverage.");
                foreach (var pair in baseline.Keys)
                    if (!group.ContainsKey(pair)) throw new ArgumentException("Unpaired process evidence.");
            }
            string winner = baselineId;
            double bestMean = Mean(baseline), baselineMean = bestMean;
            // Protocol order breaks exact nonbaseline ties; the baseline wins
            // any exact tie, regardless of its position in the input array.
            foreach (var id in frozenCandidateIds)
            {
                double value = Mean(groups[id]);
                if (value < bestMean) { bestMean = value; winner = id; }
            }
            var decision = new WholeTaskTimingDecision {
                BaselineCandidateId = baselineId, CandidateId = baselineId,
                CalibrationPartitionId = calibrationPartitionId, CalibrationDatasetHash = first.DatasetHash,
                SourceFingerprint = first.SourceFingerprint, ContractId = first.ContractId,
                IndependentProcessPairs = baseline.Count, MinimumImprovementPercent = minimumImprovementPercent,
                CalibrationProcessIdentities = new List<string>(processes).ToArray(),
                CalibrationBestCandidateId = winner,
                CalibrationBestImprovementPercent = (baselineMean - bestMean) / baselineMean * 100.0,
                Reason = "The reasonable fixed baseline remains selected; no complete-task improvement cleared the timing gate."
            };
            if (winner == baselineId) return decision;
            Interval(baseline, groups[winner], bootstrapIterations, out double lower, out double upper);
            decision.CalibrationBestLowerImprovementPercent = lower; decision.CalibrationBestUpperImprovementPercent = upper;
            if (lower > 0 && decision.CalibrationBestImprovementPercent >= minimumImprovementPercent)
            {
                decision.CandidateId = winner; decision.TimingGatePassed = true;
                decision.ImprovementPercent = decision.CalibrationBestImprovementPercent;
                decision.LowerImprovementPercent = lower; decision.UpperImprovementPercent = upper;
                decision.Reason = "Provisional complete-task timing recommendation from calibration only; independent confirmation and allocation qualification are separate.";
            }
            return decision;
        }

        /// <summary>Confirms only the already frozen candidate. Never searches
        /// the independent partition or substitutes its hindsight winner.</summary>
        public static WholeTaskTimingConfirmation Confirm(WholeTaskTimingDecision frozen,
            WholeTaskProcessSample[] baselineSamples, WholeTaskProcessSample[] executedSamples,
            int bootstrapIterations = 10000)
        {
            if (frozen == null || frozen.CalibrationProcessIdentities == null || baselineSamples == null || executedSamples == null ||
                baselineSamples.Length < 3 || baselineSamples.Length != executedSamples.Length || bootstrapIterations < 1000)
                throw new ArgumentException("Incomplete independent confirmation.");
            var baseline = new SortedDictionary<string, WholeTaskProcessSample>(StringComparer.Ordinal);
            var executed = new SortedDictionary<string, WholeTaskProcessSample>(StringComparer.Ordinal);
            var processes = new HashSet<string>(frozen.CalibrationProcessIdentities, StringComparer.Ordinal);
            WholeTaskProcessSample first = baselineSamples[0];
            Validate(first);
            for (int arm = 0; arm < 2; arm++)
            {
                var list = arm == 0 ? baselineSamples : executedSamples;
                var group = arm == 0 ? baseline : executed;
                string expected = arm == 0 ? frozen.BaselineCandidateId : frozen.CandidateId;
                foreach (var value in list)
                {
                    Validate(value);
                    if (value.CandidateId != expected || value.PartitionId == frozen.CalibrationPartitionId ||
                        value.DatasetHash == frozen.CalibrationDatasetHash || value.DatasetHash != first.DatasetHash ||
                        value.PartitionId != first.PartitionId || value.SourceFingerprint != frozen.SourceFingerprint ||
                        value.ContractId != frozen.ContractId || !processes.Add(value.ProcessIdentity) || group.ContainsKey(value.PairId))
                        throw new ArgumentException("Unfrozen candidate, reused input/process or incompatible confirmation boundary.");
                    group.Add(value.PairId, value);
                }
            }
            foreach (string pair in baseline.Keys)
                if (!executed.ContainsKey(pair)) throw new ArgumentException("Unpaired confirmation.");
            Interval(baseline, executed, bootstrapIterations, out double lower, out double upper);
            double improvement = (Mean(baseline) - Mean(executed)) / Mean(baseline) * 100.0;
            bool confirmed = frozen.TimingGatePassed && lower > 0 && improvement >= frozen.MinimumImprovementPercent;
            return new WholeTaskTimingConfirmation {
                ExecutedCandidateId = frozen.CandidateId, BaselineCandidateId = frozen.BaselineCandidateId,
                ConfirmationPartitionId = first.PartitionId, ConfirmationDatasetHash = first.DatasetHash,
                IndependentProcessPairs = baseline.Count, ImprovementPercent = improvement,
                LowerImprovementPercent = lower, UpperImprovementPercent = upper, ConfirmedTimingGain = confirmed,
                Reason = confirmed ? "The previously frozen timing recommendation repeated on independent complete tasks; calibration cost and allocation qualification still apply."
                    : "No independently confirmed timing replacement; retain the reasonable fixed baseline."
            };
        }

        static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        static void Validate(WholeTaskProcessSample x)
        {
            if (x == null || !x.Observed || !x.Completed || !x.ParityPassed || !x.IncludesCompleteBoundary || !x.EnvironmentQualified || x.Diagnostic ||
                !(x.CompleteMilliseconds > 0) || !Finite(x.CompleteMilliseconds) ||
                string.IsNullOrWhiteSpace(x.ProcessIdentity) || string.IsNullOrWhiteSpace(x.PairId) ||
                string.IsNullOrWhiteSpace(x.CandidateId) || string.IsNullOrWhiteSpace(x.PartitionId) ||
                string.IsNullOrWhiteSpace(x.DatasetHash) || string.IsNullOrWhiteSpace(x.SourceFingerprint) ||
                string.IsNullOrWhiteSpace(x.ContractId))
                throw new ArgumentException("Incomplete, diagnostic or non-observed whole-task evidence.");
        }
        static double Mean(SortedDictionary<string, WholeTaskProcessSample> values)
        {
            double mean = 0; int n = 0;
            foreach (var value in values.Values) mean += (value.CompleteMilliseconds - mean) / ++n;
            return mean;
        }
        static void Interval(SortedDictionary<string, WholeTaskProcessSample> baseline,
            SortedDictionary<string, WholeTaskProcessSample> selected, int iterations, out double lower, out double upper)
        {
            int n = baseline.Count, index = 0;
            var b = new double[n]; var c = new double[n];
            foreach (var key in baseline.Keys) { b[index] = baseline[key].CompleteMilliseconds; c[index++] = selected[key].CompleteMilliseconds; }
            var gains = new double[iterations]; uint state = 0x71A94035u;
            for (int k = 0; k < iterations; k++)
            {
                double baseSum = 0, candidateSum = 0;
                for (int j = 0; j < n; j++)
                {
                    state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                    int pick = (int)(state % (uint)n);
                    baseSum += b[pick] / n; candidateSum += c[pick] / n;
                }
                gains[k] = (baseSum - candidateSum) / baseSum * 100.0;
            }
            Array.Sort(gains);
            lower = Quantile(gains, .025); upper = Quantile(gains, .975);
        }
        static double Quantile(double[] values, double p)
        {
            double x = (values.Length - 1) * p; int lo = (int)x;
            return values[lo] + (values[Math.Min(lo + 1, values.Length - 1)] - values[lo]) * (x - lo);
        }
    }
}
