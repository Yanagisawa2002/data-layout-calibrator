using System;
using System.Collections.Generic;

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
        /// Paired non-parametric block bootstrap of the composite P95 metric. The
        /// baseline and candidate retain their within-block relationship, and the
        /// estimand is log(candidate / baseline). Migrated schema-2 results without
        /// explicit historical block IDs retain their documented implicit
        /// array-index pairing.
        /// </summary>
        public static BootstrapConfidenceInterval BootstrapAmortizedP95Improvement(
            LayoutBenchmarkResult baseline,
            LayoutBenchmarkResult candidate,
            int iterations = DefaultBootstrapIterations,
            double confidenceLevel = DefaultBootstrapConfidenceLevel,
            uint seed = 0x9E3779B9u)
        {
            ValidateBootstrapSettings(iterations, confidenceLevel);
            PairedBenchmarkData paired = PreparePairedBenchmark(baseline, candidate);
            uint randomState = NonZeroBootstrapSeed(seed);
            var logRatioEstimates = new double[iterations];

            for (int iteration = 0; iteration < iterations; iteration++)
                logRatioEstimates[iteration] = ResampledCompositeLogRatio(paired, ref randomState);

            return BuildLogRatioInterval(
                logRatioEstimates,
                PointCompositeLogRatio(paired),
                iterations,
                confidenceLevel,
                NonZeroBootstrapSeed(seed),
                "paired measurement block");
        }

        /// <summary>
        /// Hierarchical bootstrap for independent Player launches on one explicitly
        /// identified device. Processes are sampled first and paired blocks are then
        /// sampled within each selected process. Multiple device IDs are rejected;
        /// device-level resampling belongs to a future evidence layer.
        /// </summary>
        public static HierarchicalBootstrapConfidenceInterval BootstrapProcessHierarchy(
            ProcessPairedBenchmarkResult[] processes,
            int count,
            int iterations = DefaultBootstrapIterations,
            double confidenceLevel = DefaultBootstrapConfidenceLevel,
            uint seed = 0xC2B2AE35u)
        {
            ValidateBootstrapSettings(iterations, confidenceLevel);
            if (processes == null)
                throw new ArgumentNullException(nameof(processes));
            if (count < 2 || count > processes.Length)
                throw new ArgumentOutOfRangeException(nameof(count), "At least two Player processes are required.");

            var processIds = new HashSet<string>(StringComparer.Ordinal);
            var pairedProcesses = new PairedBenchmarkData[count];
            string deviceId = null;
            string baselineCandidateId = null;
            string candidateId = null;
            string scenarioId = null;
            int scenarioContractVersion = 0;
            BenchmarkPhase phase = default;
            int elementCount = 0;
            int stepsPerSample = 0;
            int lifetimeTicks = 0;
            CandidateDescriptor baselineDefinition = default;
            CandidateDescriptor candidateDefinition = default;
            double pointLogRatioTotal = 0d;
            for (int index = 0; index < count; index++)
            {
                ProcessPairedBenchmarkResult process = processes[index];
                if (process == null)
                    throw new ArgumentException("A process result is null.", nameof(processes));
                if (process.SchemaVersion != 1)
                    throw new ArgumentException("Process evidence has an unsupported schema.", nameof(processes));
                if (string.IsNullOrWhiteSpace(process.ProcessId) || !processIds.Add(process.ProcessId))
                    throw new ArgumentException("Player process IDs must be non-empty and unique.", nameof(processes));
                if (string.IsNullOrWhiteSpace(process.DeviceId))
                    throw new ArgumentException("An explicit, stable device ID is required for process-level evidence.", nameof(processes));
                if (deviceId == null)
                    deviceId = process.DeviceId;
                else if (!string.Equals(deviceId, process.DeviceId, StringComparison.Ordinal))
                    throw new ArgumentException("Process-level bootstrap cannot combine multiple devices.", nameof(processes));

                PairedBenchmarkData paired = PreparePairedBenchmark(process.Baseline, process.Candidate);
                string currentBaselineId = process.Baseline.Candidate.CandidateId;
                string currentCandidateId = process.Candidate.Candidate.CandidateId;
                string currentScenarioId = process.Baseline.ScenarioId;
                int currentScenarioContractVersion = process.Baseline.ScenarioContractVersion;
                if (index == 0)
                {
                    baselineCandidateId = currentBaselineId;
                    candidateId = currentCandidateId;
                    scenarioId = currentScenarioId;
                    scenarioContractVersion = currentScenarioContractVersion;
                    phase = process.Baseline.Phase;
                    elementCount = process.Baseline.ElementCount;
                    stepsPerSample = process.Baseline.StepsPerSample;
                    lifetimeTicks = process.Baseline.BoundaryCost.LifetimeTicks;
                    baselineDefinition = NormalizeAndValidate(process.Baseline.Candidate);
                    candidateDefinition = NormalizeAndValidate(process.Candidate.Candidate);
                }
                else if (!string.Equals(baselineCandidateId, currentBaselineId, StringComparison.Ordinal) ||
                         !string.Equals(candidateId, currentCandidateId, StringComparison.Ordinal) ||
                         !string.Equals(scenarioId, currentScenarioId, StringComparison.Ordinal) ||
                         scenarioContractVersion != currentScenarioContractVersion ||
                         phase != process.Baseline.Phase ||
                         elementCount != process.Baseline.ElementCount ||
                         stepsPerSample != process.Baseline.StepsPerSample ||
                         lifetimeTicks != process.Baseline.BoundaryCost.LifetimeTicks ||
                         baselineDefinition != NormalizeAndValidate(process.Baseline.Candidate) ||
                         candidateDefinition != NormalizeAndValidate(process.Candidate.Candidate))
                {
                    throw new ArgumentException(
                        "Every process must compare the same scenario contract, phase, settings, and canonical candidate definitions.",
                        nameof(processes));
                }

                pairedProcesses[index] = paired;
                pointLogRatioTotal += PointCompositeLogRatio(paired);
            }

            uint randomState = NonZeroBootstrapSeed(seed);
            var logRatioEstimates = new double[iterations];
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                double logRatioTotal = 0d;
                for (int processDraw = 0; processDraw < count; processDraw++)
                {
                    int processIndex = NextIndex(count, ref randomState);
                    logRatioTotal += ResampledCompositeLogRatio(
                        pairedProcesses[processIndex],
                        ref randomState);
                }

                logRatioEstimates[iteration] = logRatioTotal / count;
            }

            return new HierarchicalBootstrapConfidenceInterval
            {
                SchemaVersion = 1,
                EvidenceScope = EvidenceScope.MultipleProcessesSingleDevice,
                ProcessCount = count,
                DeviceCount = 1,
                ImprovementConfidenceInterval = BuildLogRatioInterval(
                    logRatioEstimates,
                    pointLogRatioTotal / count,
                    iterations,
                    confidenceLevel,
                    NonZeroBootstrapSeed(seed),
                    "Player process, then paired measurement block"),
            };
        }

        private static BootstrapConfidenceInterval BuildLogRatioInterval(
            double[] logRatioEstimates,
            double pointLogRatio,
            int iterations,
            double confidenceLevel,
            uint seed,
            string resamplingUnit)
        {
            HeapSort(logRatioEstimates, logRatioEstimates.Length);
            double tail = (1d - confidenceLevel) * 0.5d;
            double lowerLogRatio = PercentileOfSorted(
                logRatioEstimates,
                logRatioEstimates.Length,
                tail);
            double upperLogRatio = PercentileOfSorted(
                logRatioEstimates,
                logRatioEstimates.Length,
                1d - tail);
            return new BootstrapConfidenceInterval
            {
                SchemaVersion = 1,
                Iterations = iterations,
                ConfidenceLevel = confidenceLevel,
                RandomSeed = seed,
                Estimand = "log(candidate_amortized_p95 / baseline_amortized_p95)",
                ResamplingUnit = resamplingUnit,
                PointEstimateLogRatio = pointLogRatio,
                LowerBoundLogRatio = lowerLogRatio,
                UpperBoundLogRatio = upperLogRatio,
                PointEstimatePercent = LogRatioToImprovementPercent(pointLogRatio),
                LowerBoundPercent = LogRatioToImprovementPercent(upperLogRatio),
                UpperBoundPercent = LogRatioToImprovementPercent(lowerLogRatio),
            };
        }

        private static PairedBenchmarkData PreparePairedBenchmark(
            LayoutBenchmarkResult baseline,
            LayoutBenchmarkResult candidate)
        {
            if (baseline == null)
                throw new ArgumentNullException(nameof(baseline));
            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));
            ValidateComparisonContract(baseline, candidate);
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

            return new PairedBenchmarkData
            {
                LifetimeTicks = baseline.BoundaryCost.LifetimeTicks,
                Resident = CreatePairedSeries(
                    baseline.ResidentSamplesMillisecondsPerTick,
                    baseline.ResidentBlockIds,
                    candidate.ResidentSamplesMillisecondsPerTick,
                    candidate.ResidentBlockIds,
                    "resident"),
                Ingress = CreatePairedSeries(
                    baseline.IngressSamplesMilliseconds,
                    baseline.IngressBlockIds,
                    candidate.IngressSamplesMilliseconds,
                    candidate.IngressBlockIds,
                    "ingress"),
                Export = CreatePairedSeries(
                    baseline.ExportSamplesMilliseconds,
                    baseline.ExportBlockIds,
                    candidate.ExportSamplesMilliseconds,
                    candidate.ExportBlockIds,
                    "export"),
            };
        }

        private static void ValidateComparisonContract(
            LayoutBenchmarkResult baseline,
            LayoutBenchmarkResult candidate)
        {
            if (string.IsNullOrWhiteSpace(baseline.ScenarioId) ||
                !string.Equals(baseline.ScenarioId, candidate.ScenarioId, StringComparison.Ordinal) ||
                baseline.ScenarioContractVersion <= 0 ||
                baseline.ScenarioContractVersion != candidate.ScenarioContractVersion)
            {
                throw new ArgumentException(
                    "Paired candidates require the same ScenarioId and positive ContractVersion.");
            }
            if (baseline.Phase != candidate.Phase ||
                baseline.ElementCount <= 0 ||
                baseline.ElementCount != candidate.ElementCount ||
                baseline.StepsPerSample <= 0 ||
                baseline.StepsPerSample != candidate.StepsPerSample)
            {
                throw new ArgumentException(
                    "Paired candidates require the same phase, element count, and steps per sample.");
            }

            CandidateDescriptor normalizedBaseline = NormalizeAndValidate(baseline.Candidate);
            CandidateDescriptor normalizedCandidate = NormalizeAndValidate(candidate.Candidate);
            if (!normalizedBaseline.IsBaseline || normalizedCandidate.IsBaseline ||
                string.Equals(
                    normalizedBaseline.CandidateId,
                    normalizedCandidate.CandidateId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Paired inference requires one tuned AoS baseline and one distinct non-baseline candidate.");
            }
        }

        private static CandidateDescriptor NormalizeAndValidate(CandidateDescriptor candidate)
        {
            try
            {
                CandidateDescriptor normalized = candidate.NormalizePolicies();
                normalized.ValidateFactorConsistency();
                return normalized;
            }
            catch (InvalidOperationException exception)
            {
                throw new ArgumentException(
                    "A candidate definition is incomplete or internally inconsistent.",
                    nameof(candidate),
                    exception);
            }
        }

        private static PairedSeries CreatePairedSeries(
            double[] baseline,
            int[] baselineBlockIds,
            double[] candidate,
            int[] candidateBlockIds,
            string component)
        {
            int[] candidateIndexByBaselineIndex = BuildPairMap(
                baseline.Length,
                baselineBlockIds,
                candidate.Length,
                candidateBlockIds,
                component);
            return new PairedSeries
            {
                Baseline = baseline,
                Candidate = candidate,
                CandidateIndexByBaselineIndex = candidateIndexByBaselineIndex,
                BaselineScratch = new double[baseline.Length],
                CandidateScratch = new double[baseline.Length],
            };
        }

        private static int[] BuildPairMap(
            int baselineLength,
            int[] baselineBlockIds,
            int candidateLength,
            int[] candidateBlockIds,
            string component)
        {
            if (baselineLength != candidateLength)
            {
                throw new ArgumentException(
                    $"Paired {component} samples must contain the same number of blocks.");
            }

            var map = new int[baselineLength];
            if (baselineBlockIds == null && candidateBlockIds == null)
            {
                for (int index = 0; index < map.Length; index++)
                    map[index] = index;
                return map;
            }
            if (baselineBlockIds == null || candidateBlockIds == null ||
                baselineBlockIds.Length != baselineLength ||
                candidateBlockIds.Length != candidateLength)
            {
                throw new ArgumentException(
                    $"Paired {component} block IDs must be present on both candidates and match sample lengths.");
            }

            var candidateIndices = new Dictionary<int, int>(candidateLength);
            for (int index = 0; index < candidateLength; index++)
            {
                int blockId = candidateBlockIds[index];
                if (candidateIndices.ContainsKey(blockId))
                    throw new ArgumentException($"Candidate {component} block IDs must be unique.");
                candidateIndices.Add(blockId, index);
            }

            var baselineIds = new HashSet<int>();
            for (int index = 0; index < baselineLength; index++)
            {
                int blockId = baselineBlockIds[index];
                if (!baselineIds.Add(blockId))
                    throw new ArgumentException($"Baseline {component} block IDs must be unique.");
                if (!candidateIndices.TryGetValue(blockId, out map[index]))
                {
                    throw new ArgumentException(
                        $"Candidate {component} samples are missing baseline block ID {blockId}.");
                }
            }

            return map;
        }

        private static double PointCompositeLogRatio(PairedBenchmarkData paired)
        {
            double baselineMetric = CalculateAmortizedP95MillisecondsPerTick(
                paired.Resident.Baseline,
                paired.Ingress.Baseline,
                paired.Export.Baseline,
                paired.LifetimeTicks);
            double candidateMetric = CalculateAmortizedP95MillisecondsPerTick(
                paired.Resident.Candidate,
                paired.Ingress.Candidate,
                paired.Export.Candidate,
                paired.LifetimeTicks);
            return CompositeLogRatio(baselineMetric, candidateMetric);
        }

        private static double ResampledCompositeLogRatio(
            PairedBenchmarkData paired,
            ref uint randomState)
        {
            ResampledPairedPercentiles(
                paired.Resident,
                0.95d,
                ref randomState,
                out double baselineResident,
                out double candidateResident);
            ResampledPairedPercentiles(
                paired.Ingress,
                0.95d,
                ref randomState,
                out double baselineIngress,
                out double candidateIngress);
            ResampledPairedPercentiles(
                paired.Export,
                0.95d,
                ref randomState,
                out double baselineExport,
                out double candidateExport);

            double baselineMetric = baselineResident +
                                    ((baselineIngress + baselineExport) / paired.LifetimeTicks);
            double candidateMetric = candidateResident +
                                     ((candidateIngress + candidateExport) / paired.LifetimeTicks);
            return CompositeLogRatio(baselineMetric, candidateMetric);
        }

        private static void ResampledPairedPercentiles(
            PairedSeries series,
            double percentile,
            ref uint randomState,
            out double baselinePercentile,
            out double candidatePercentile)
        {
            for (int index = 0; index < series.Baseline.Length; index++)
            {
                int baselineIndex = NextIndex(series.Baseline.Length, ref randomState);
                series.BaselineScratch[index] = series.Baseline[baselineIndex];
                series.CandidateScratch[index] =
                    series.Candidate[series.CandidateIndexByBaselineIndex[baselineIndex]];
            }

            HeapSort(series.BaselineScratch, series.BaselineScratch.Length);
            HeapSort(series.CandidateScratch, series.CandidateScratch.Length);
            baselinePercentile = PercentileOfSorted(
                series.BaselineScratch,
                series.BaselineScratch.Length,
                percentile);
            candidatePercentile = PercentileOfSorted(
                series.CandidateScratch,
                series.CandidateScratch.Length,
                percentile);
        }

        private static double CompositeLogRatio(double baselineMetric, double candidateMetric)
        {
            if (!(baselineMetric > 0d) || !(candidateMetric > 0d) ||
                double.IsNaN(baselineMetric) || double.IsInfinity(baselineMetric) ||
                double.IsNaN(candidateMetric) || double.IsInfinity(candidateMetric))
            {
                throw new ArgumentException("Composite bootstrap metrics must be finite and positive.");
            }
            return Math.Log(candidateMetric / baselineMetric);
        }

        private static double LogRatioToImprovementPercent(double logRatio)
        {
            return (1d - Math.Exp(logRatio)) * 100d;
        }

        private static void ValidateBootstrapSettings(int iterations, double confidenceLevel)
        {
            if (iterations < 100)
                throw new ArgumentOutOfRangeException(nameof(iterations), "At least 100 bootstrap iterations are required.");
            if (!(confidenceLevel > 0d && confidenceLevel < 1d))
                throw new ArgumentOutOfRangeException(nameof(confidenceLevel));
        }

        private static uint NonZeroBootstrapSeed(uint seed) =>
            seed == 0u ? 0x9E3779B9u : seed;

        private static int NextIndex(int exclusiveMaximum, ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (int)(state % (uint)exclusiveMaximum);
        }

        private sealed class PairedBenchmarkData
        {
            public int LifetimeTicks;
            public PairedSeries Resident;
            public PairedSeries Ingress;
            public PairedSeries Export;
        }

        private sealed class PairedSeries
        {
            public double[] Baseline;
            public double[] Candidate;
            public int[] CandidateIndexByBaselineIndex;
            public double[] BaselineScratch;
            public double[] CandidateScratch;
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
