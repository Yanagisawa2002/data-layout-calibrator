using System;

namespace Yanagisawa.DataLayoutCalibrator
{
    public static class BenchmarkStatistics
    {
        public const double DefaultBootstrapConfidenceLevel = 0.95d;
        public const int DefaultBootstrapIterations = 4000;

        public static LatencySummary Calculate(double[] samplesMilliseconds, int count, double[] scratch)
        {
            if (!TryCalculate(samplesMilliseconds, count, scratch, out LatencySummary summary))
            {
                throw new ArgumentException("Samples and scratch must contain at least count finite, non-negative values.");
            }

            return summary;
        }

        public static bool TryCalculate(
            double[] samplesMilliseconds,
            int count,
            double[] scratch,
            out LatencySummary summary)
        {
            summary = default;
            if (samplesMilliseconds == null || scratch == null || count <= 0 ||
                count > samplesMilliseconds.Length || count > scratch.Length)
            {
                return false;
            }

            double minimum = double.MaxValue;
            double maximum = double.MinValue;
            for (int i = 0; i < count; i++)
            {
                double sample = samplesMilliseconds[i];
                if (double.IsNaN(sample) || double.IsInfinity(sample) || sample < 0d)
                {
                    return false;
                }

                scratch[i] = sample;
                if (sample < minimum)
                {
                    minimum = sample;
                }

                if (sample > maximum)
                {
                    maximum = sample;
                }
            }

            HeapSort(scratch, count);
            double median = PercentileOfSorted(scratch, count, 0.5d);
            double p95 = PercentileOfSorted(scratch, count, 0.95d);
            double p99 = PercentileOfSorted(scratch, count, 0.99d);

            for (int i = 0; i < count; i++)
            {
                scratch[i] = Math.Abs(samplesMilliseconds[i] - median);
            }

            HeapSort(scratch, count);
            summary = new LatencySummary
            {
                SampleCount = count,
                MinimumMilliseconds = minimum,
                MedianMilliseconds = median,
                P95Milliseconds = p95,
                P99Milliseconds = p99,
                MaximumMilliseconds = maximum,
                MedianAbsoluteDeviationMilliseconds = PercentileOfSorted(scratch, count, 0.5d),
            };
            return true;
        }

        public static double CalculateAmortizedP95MillisecondsPerTick(
            double[] residentSamplesMillisecondsPerTick,
            double[] ingressSamplesMilliseconds,
            double[] exportSamplesMilliseconds,
            int lifetimeTicks)
        {
            ValidateAmortizedInputs(
                residentSamplesMillisecondsPerTick,
                ingressSamplesMilliseconds,
                exportSamplesMilliseconds,
                lifetimeTicks);

            double residentP95 = CalculatePercentile(
                residentSamplesMillisecondsPerTick,
                0.95d);
            double ingressP95 = CalculatePercentile(ingressSamplesMilliseconds, 0.95d);
            double exportP95 = CalculatePercentile(exportSamplesMilliseconds, 0.95d);
            return residentP95 + ((ingressP95 + exportP95) / lifetimeTicks);
        }

        public static LatencySummary CalculateAmortizedLatency(
            double[] residentSamplesMillisecondsPerTick,
            double[] ingressSamplesMilliseconds,
            double[] exportSamplesMilliseconds,
            int lifetimeTicks,
            double[] amortizedSamplesMillisecondsPerTick,
            double[] scratch)
        {
            ValidateAmortizedInputs(
                residentSamplesMillisecondsPerTick,
                ingressSamplesMilliseconds,
                exportSamplesMilliseconds,
                lifetimeTicks);
            if (amortizedSamplesMillisecondsPerTick == null ||
                amortizedSamplesMillisecondsPerTick.Length < residentSamplesMillisecondsPerTick.Length)
            {
                throw new ArgumentException(
                    "Amortized sample storage must fit every resident sample.",
                    nameof(amortizedSamplesMillisecondsPerTick));
            }

            double boundaryP95 = CalculatePercentile(ingressSamplesMilliseconds, 0.95d) +
                                 CalculatePercentile(exportSamplesMilliseconds, 0.95d);
            double amortizedBoundary = boundaryP95 / lifetimeTicks;
            for (int index = 0; index < residentSamplesMillisecondsPerTick.Length; index++)
            {
                amortizedSamplesMillisecondsPerTick[index] =
                    residentSamplesMillisecondsPerTick[index] + amortizedBoundary;
            }

            return Calculate(
                amortizedSamplesMillisecondsPerTick,
                residentSamplesMillisecondsPerTick.Length,
                scratch);
        }

        /// <summary>
        /// Independent non-parametric bootstrap of the composite P95 metric:
        /// resident P95 + (ingress P95 + export P95) / lifetime ticks.
        /// </summary>
        public static BootstrapConfidenceInterval BootstrapAmortizedP95Improvement(
            LayoutBenchmarkResult baseline,
            LayoutBenchmarkResult candidate,
            int iterations = DefaultBootstrapIterations,
            double confidenceLevel = DefaultBootstrapConfidenceLevel,
            uint seed = 0x9E3779B9u)
        {
            if (baseline == null)
                throw new ArgumentNullException(nameof(baseline));
            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));
            if (iterations < 100)
                throw new ArgumentOutOfRangeException(nameof(iterations), "At least 100 bootstrap iterations are required.");
            if (!(confidenceLevel > 0d && confidenceLevel < 1d))
                throw new ArgumentOutOfRangeException(nameof(confidenceLevel));
            if (baseline.BoundaryCost.LifetimeTicks <= 0 ||
                candidate.BoundaryCost.LifetimeTicks <= 0 ||
                baseline.BoundaryCost.LifetimeTicks != candidate.BoundaryCost.LifetimeTicks)
            {
                throw new ArgumentException("Bootstrap candidates require the same positive lifetime tick count.");
            }

            ValidateAmortizedInputs(
                baseline.ResidentSamplesMillisecondsPerTick,
                baseline.IngressSamplesMilliseconds,
                baseline.ExportSamplesMilliseconds,
                baseline.BoundaryCost.LifetimeTicks);
            ValidateAmortizedInputs(
                candidate.ResidentSamplesMillisecondsPerTick,
                candidate.IngressSamplesMilliseconds,
                candidate.ExportSamplesMilliseconds,
                candidate.BoundaryCost.LifetimeTicks);

            var estimates = new double[iterations];
            var baselineResidentScratch = new double[baseline.ResidentSamplesMillisecondsPerTick.Length];
            var baselineIngressScratch = new double[baseline.IngressSamplesMilliseconds.Length];
            var baselineExportScratch = new double[baseline.ExportSamplesMilliseconds.Length];
            var candidateResidentScratch = new double[candidate.ResidentSamplesMillisecondsPerTick.Length];
            var candidateIngressScratch = new double[candidate.IngressSamplesMilliseconds.Length];
            var candidateExportScratch = new double[candidate.ExportSamplesMilliseconds.Length];
            uint randomState = seed == 0u ? 0x9E3779B9u : seed;
            int lifetimeTicks = baseline.BoundaryCost.LifetimeTicks;

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                double baselineMetric = ResampledCompositeP95(
                    baseline.ResidentSamplesMillisecondsPerTick,
                    baseline.IngressSamplesMilliseconds,
                    baseline.ExportSamplesMilliseconds,
                    lifetimeTicks,
                    baselineResidentScratch,
                    baselineIngressScratch,
                    baselineExportScratch,
                    ref randomState);
                double candidateMetric = ResampledCompositeP95(
                    candidate.ResidentSamplesMillisecondsPerTick,
                    candidate.IngressSamplesMilliseconds,
                    candidate.ExportSamplesMilliseconds,
                    lifetimeTicks,
                    candidateResidentScratch,
                    candidateIngressScratch,
                    candidateExportScratch,
                    ref randomState);
                estimates[iteration] = ImprovementPercent(baselineMetric, candidateMetric);
            }

            HeapSort(estimates, estimates.Length);
            double tail = (1d - confidenceLevel) * 0.5d;
            double baselinePoint = CalculateAmortizedP95MillisecondsPerTick(
                baseline.ResidentSamplesMillisecondsPerTick,
                baseline.IngressSamplesMilliseconds,
                baseline.ExportSamplesMilliseconds,
                lifetimeTicks);
            double candidatePoint = CalculateAmortizedP95MillisecondsPerTick(
                candidate.ResidentSamplesMillisecondsPerTick,
                candidate.IngressSamplesMilliseconds,
                candidate.ExportSamplesMilliseconds,
                lifetimeTicks);
            return new BootstrapConfidenceInterval
            {
                Iterations = iterations,
                ConfidenceLevel = confidenceLevel,
                PointEstimatePercent = ImprovementPercent(baselinePoint, candidatePoint),
                LowerBoundPercent = PercentileOfSorted(estimates, estimates.Length, tail),
                UpperBoundPercent = PercentileOfSorted(estimates, estimates.Length, 1d - tail),
            };
        }

        private static double ResampledCompositeP95(
            double[] resident,
            double[] ingress,
            double[] export,
            int lifetimeTicks,
            double[] residentScratch,
            double[] ingressScratch,
            double[] exportScratch,
            ref uint randomState)
        {
            double residentP95 = ResampledPercentile(resident, residentScratch, 0.95d, ref randomState);
            double ingressP95 = ResampledPercentile(ingress, ingressScratch, 0.95d, ref randomState);
            double exportP95 = ResampledPercentile(export, exportScratch, 0.95d, ref randomState);
            return residentP95 + ((ingressP95 + exportP95) / lifetimeTicks);
        }

        private static double ResampledPercentile(
            double[] source,
            double[] scratch,
            double percentile,
            ref uint randomState)
        {
            for (int index = 0; index < source.Length; index++)
                scratch[index] = source[NextIndex(source.Length, ref randomState)];
            HeapSort(scratch, source.Length);
            return PercentileOfSorted(scratch, source.Length, percentile);
        }

        private static int NextIndex(int exclusiveMaximum, ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (int)(state % (uint)exclusiveMaximum);
        }

        private static double CalculatePercentile(double[] samples, double percentile)
        {
            if (samples == null || samples.Length == 0)
                throw new ArgumentException("At least one sample is required.", nameof(samples));
            var scratch = new double[samples.Length];
            for (int index = 0; index < samples.Length; index++)
            {
                double sample = samples[index];
                if (!IsFiniteNonNegative(sample))
                    throw new ArgumentException("Samples must be finite and non-negative.", nameof(samples));
                scratch[index] = sample;
            }
            HeapSort(scratch, scratch.Length);
            return PercentileOfSorted(scratch, scratch.Length, percentile);
        }

        private static void ValidateAmortizedInputs(
            double[] resident,
            double[] ingress,
            double[] export,
            int lifetimeTicks)
        {
            if (lifetimeTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(lifetimeTicks));
            ValidateSamples(resident, nameof(resident));
            ValidateSamples(ingress, nameof(ingress));
            ValidateSamples(export, nameof(export));
        }

        private static void ValidateSamples(double[] samples, string argumentName)
        {
            if (samples == null || samples.Length == 0)
                throw new ArgumentException("At least one sample is required.", argumentName);
            for (int index = 0; index < samples.Length; index++)
            {
                if (!IsFiniteNonNegative(samples[index]))
                    throw new ArgumentException("Samples must be finite and non-negative.", argumentName);
            }
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double ImprovementPercent(double baseline, double candidate)
        {
            if (!(baseline > 0d))
                throw new ArgumentOutOfRangeException(nameof(baseline));
            return ((baseline - candidate) / baseline) * 100d;
        }

        private static double PercentileOfSorted(double[] sortedValues, int count, double percentile)
        {
            if (count == 1)
            {
                return sortedValues[0];
            }

            double rank = (count - 1) * percentile;
            int lowerIndex = (int)rank;
            int upperIndex = lowerIndex + 1;
            if (upperIndex >= count)
            {
                return sortedValues[lowerIndex];
            }

            double fraction = rank - lowerIndex;
            return sortedValues[lowerIndex] + ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction);
        }

        private static void HeapSort(double[] values, int count)
        {
            for (int root = (count / 2) - 1; root >= 0; root--)
            {
                SiftDown(values, root, count);
            }

            for (int end = count - 1; end > 0; end--)
            {
                double temporary = values[0];
                values[0] = values[end];
                values[end] = temporary;
                SiftDown(values, 0, end);
            }
        }

        private static void SiftDown(double[] values, int root, int count)
        {
            while (true)
            {
                int child = (root * 2) + 1;
                if (child >= count)
                {
                    return;
                }

                if (child + 1 < count && values[child] < values[child + 1])
                {
                    child++;
                }

                if (values[root] >= values[child])
                {
                    return;
                }

                double temporary = values[root];
                values[root] = values[child];
                values[child] = temporary;
                root = child;
            }
        }
    }
}
