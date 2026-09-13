using System;
using System.Collections.Generic;

namespace Yanagisawa.DataLayoutCalibrator
{
    [Serializable]
    public sealed class LifetimeProcessCosts
    {
        public string ProcessId;
        // Use CalibrationProfileFingerprintBuilder: includes device, compiler, binary/kernel,
        // workload, candidate definitions and settings. Never replace this with a display name.
        public string SourceFingerprint;
        public string BaselineCandidateId;
        public string CandidateId;
        public TimingObservationOrigin Origin;
        public bool IncludesConstructionIngressExportDisposal;
        public double BaselineResidentScoreMillisecondsPerTick;
        public double CandidateResidentScoreMillisecondsPerTick;
        public double BaselineOneTimeMilliseconds;
        public double CandidateOneTimeMilliseconds;
    }

    public enum LifetimeEnvelopeStatus { Unknown = 0, FiniteSustainedBreakEven = 1, NoSustainedAdvantage = 2 }

    [Serializable]
    public sealed class ConservativeLifetimeEstimate
    {
        public LifetimeEnvelopeStatus Status;
        public TimingObservationOrigin Origin;
        public string SourceFingerprint;
        public int IndependentProcessCount;
        public double ConfidenceLevel;
        public double MinimumRequiredImprovementPercent;
        public double LowerLifetimeTicks;
        public double UpperLifetimeTicks;
        public string Diagnostic;
        public string Estimand = "conditional linear lifetime cost score, not measured lifecycle P95";
    }

    public static class ConservativeLifetimeEnvelope
    {
        /// <summary>Paired process bootstrap; each process contributes once. Conservative
        /// Bonferroni marginal bounds on incremental one-time cost and resident savings
        /// form a joint rectangle. Unknown/unbounded denominators never become a finite CI.
        /// This is a conditional constant-cost model, not evidence beyond sampled workloads.</summary>
        public static ConservativeLifetimeEstimate Estimate(LifetimeProcessCosts[] processes,
            double minimumImprovementPercent = 10, double confidence = .95, int iterations = 4000, uint seed = 17)
        {
            var result = new ConservativeLifetimeEstimate { Status = LifetimeEnvelopeStatus.Unknown,
                Diagnostic = "Insufficient independent processes or complete cost/source evidence.",
                MinimumRequiredImprovementPercent = minimumImprovementPercent, ConfidenceLevel = confidence };
            if (!Finite(minimumImprovementPercent) || minimumImprovementPercent < 0 || minimumImprovementPercent >= 100 ||
                !(confidence > 0 && confidence < 1) || iterations < 100) throw new ArgumentException("Invalid inference policy.");
            if (processes == null || processes.Length < 3) return result;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            LifetimeProcessCosts first = processes[0];
            if (first == null) return result;
            foreach (LifetimeProcessCosts row in processes)
            {
                if (row == null || !row.IncludesConstructionIngressExportDisposal ||
                    string.IsNullOrWhiteSpace(row.ProcessId) || !ids.Add(row.ProcessId) ||
                    !CandidateDefinitionProtocol.IsCanonicalSha256(row.SourceFingerprint) ||
                    row.SourceFingerprint != first.SourceFingerprint ||
                    string.IsNullOrWhiteSpace(row.BaselineCandidateId) || string.IsNullOrWhiteSpace(row.CandidateId) ||
                    row.BaselineCandidateId == row.CandidateId || row.BaselineCandidateId != first.BaselineCandidateId ||
                    row.CandidateId != first.CandidateId || row.Origin != first.Origin ||
                    (row.Origin != TimingObservationOrigin.SyntheticFixture && row.Origin != TimingObservationOrigin.Observed) ||
                    !NonNegative(row.BaselineResidentScoreMillisecondsPerTick) || !NonNegative(row.CandidateResidentScoreMillisecondsPerTick) ||
                    !NonNegative(row.BaselineOneTimeMilliseconds) || !NonNegative(row.CandidateOneTimeMilliseconds)) return result;
            }
            result.SourceFingerprint = first.SourceFingerprint; result.Origin = first.Origin;
            result.IndependentProcessCount = processes.Length;
            double fraction = 1 - minimumImprovementPercent / 100;
            var saving = new double[iterations]; var extra = new double[iterations];
            uint state = seed == 0 ? 17u : seed;
            for (int i = 0; i < iterations; i++)
            {
                for (int j = 0; j < processes.Length; j++)
                {
                    state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                    LifetimeProcessCosts row = processes[(int)(state % (uint)processes.Length)];
                    saving[i] += (fraction * row.BaselineResidentScoreMillisecondsPerTick - row.CandidateResidentScoreMillisecondsPerTick) / processes.Length;
                    extra[i] += (row.CandidateOneTimeMilliseconds - fraction * row.BaselineOneTimeMilliseconds) / processes.Length;
                }
                if (!Finite(saving[i]) || !Finite(extra[i])) { result.Diagnostic = "Cost model overflow."; return result; }
            }
            Array.Sort(saving); Array.Sort(extra);
            double tail = (1 - confidence) / 4;
            double savingLow = Quantile(saving, tail), savingHigh = Quantile(saving, 1 - tail);
            double extraLow = Quantile(extra, tail), extraHigh = Quantile(extra, 1 - tail);
            if (savingHigh <= 0 && extraLow >= 0)
            {
                result.Status = LifetimeEnvelopeStatus.NoSustainedAdvantage;
                result.Diagnostic = "No sustained gain under the declared practical threshold and cost bounds.";
                return result;
            }
            if (!(savingLow > 0))
            {
                result.Diagnostic = "Resident savings denominator overlaps zero; finite sustained break-even is Unknown.";
                return result;
            }
            double upper = Math.Max(1, Math.Ceiling(Math.Max(0, extraHigh) / savingLow));
            double lower = Math.Max(1, Math.Ceiling(Math.Max(0, extraLow) / savingHigh));
            if (!Finite(upper) || upper > long.MaxValue || lower > upper)
            { result.Diagnostic = "Break-even bound overflow or inconsistency."; return result; }
            result.Status = LifetimeEnvelopeStatus.FiniteSustainedBreakEven;
            result.LowerLifetimeTicks = lower; result.UpperLifetimeTicks = upper;
            result.Diagnostic = "Process bootstrap with conservative simultaneous cost bounds; conditional model, not a measured speedup or lifecycle quantile.";
            return result;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool NonNegative(double value) => Finite(value) && value >= 0;
        private static double Quantile(double[] sorted, double p)
        {
            double position = (sorted.Length - 1) * p;
            int low = (int)Math.Floor(position), high = (int)Math.Ceiling(position);
            return sorted[low] + (sorted[high] - sorted[low]) * (position - low);
        }
    }
}
