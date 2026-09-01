using System;

namespace Yanagisawa.DataLayoutAutotuner
{
    public static class BenchmarkStatistics
    {
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
