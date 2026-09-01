using System;
using System.Diagnostics;

namespace Yanagisawa.DataLayoutCalibrator
{
    [Serializable]
    public sealed class CalibrationRunSettings
    {
        public int ElementCount = 1_048_576;
        public int HoldoutElementCount = 1_000_003;
        public uint CalibrationSeed = 0xA511E9B3u;
        public uint HoldoutSeed = 0xD84F21C7u;
        public int PreflightElementCount = 4099;
        public int PreflightTicks = 256;
        public float FixedDeltaTime = 1f / 60f;
        public int WarmupBlocks = 32;
        public double MinimumWarmupSeconds = 1d;
        public int SamplesPerCandidate = 40;
        public int BoundarySamplesPerCandidate = 20;
        public int LifetimeTicks = 600;
        public double TargetBlockMilliseconds = 25d;
        public int MaximumTicksPerBlock = 256;
        public double MinimumImprovementPercent = LayoutSelector.DefaultMinimumImprovementPercent;
        public int BootstrapIterations = BenchmarkStatistics.DefaultBootstrapIterations;
        public double BootstrapConfidenceLevel = BenchmarkStatistics.DefaultBootstrapConfidenceLevel;
        public uint CandidateOrderSeed = 0xA341316Cu;
        public uint BootstrapSeed = 0xB5297A4Du;
        public float ParityTolerance = 1e-5f;
    }

    /// <summary>
    /// Workload-agnostic synchronous calibration pipeline. It knows only the public
    /// Scenario/Candidate/Parity/BoundaryCost contracts.
    /// </summary>
    public static class ScenarioCalibrationEngine
    {
        public static ScenarioCalibrationProfile Run(
            ICalibrationScenarioFactory factory,
            CalibrationRunSettings settings)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            ValidateSettings(settings);
            RunPreflight(factory, settings);

            PhaseMeasurement calibration = MeasurePhase(
                factory,
                settings,
                BenchmarkPhase.Calibration,
                settings.ElementCount,
                settings.CalibrationSeed,
                null,
                0,
                0,
                settings.CandidateOrderSeed);
            LayoutSelectionDecision calibrationDecision = LayoutSelector.SelectCalibration(
                calibration.Results,
                calibration.Results.Length,
                settings.MinimumImprovementPercent,
                settings.BootstrapIterations,
                settings.BootstrapConfidenceLevel,
                settings.BootstrapSeed);

            LayoutBenchmarkResult holdoutBaseline = null;
            LayoutBenchmarkResult holdoutSelected = null;
            string holdoutHash = string.Empty;
            LayoutSelectionDecision finalDecision = calibrationDecision;
            if (calibrationDecision.Status == LayoutSelectionStatus.Optimized)
            {
                var holdoutCandidates = new[]
                {
                    calibrationDecision.BaselineCandidate,
                    calibrationDecision.SelectedCandidate,
                };
                PhaseMeasurement holdout = MeasurePhase(
                    factory,
                    settings,
                    BenchmarkPhase.Holdout,
                    settings.HoldoutElementCount,
                    settings.HoldoutSeed,
                    holdoutCandidates,
                    calibration.TicksPerBlock,
                    calibration.WarmupBlocks,
                    settings.CandidateOrderSeed ^ 0x9E3779B9u);
                holdoutHash = holdout.DatasetHash;
                holdoutBaseline = holdout.Results[0];
                holdoutSelected = holdout.Results[1];
                finalDecision = LayoutSelector.ConfirmHoldout(
                    calibrationDecision,
                    holdoutBaseline,
                    holdoutSelected,
                    settings.MinimumImprovementPercent,
                    settings.BootstrapIterations,
                    settings.BootstrapConfidenceLevel,
                    settings.BootstrapSeed ^ 0x68E31DA4u);
            }

            return new ScenarioCalibrationProfile
            {
                Scenario = factory.Descriptor,
                ElementCount = settings.ElementCount,
                HoldoutElementCount = settings.HoldoutElementCount,
                CalibrationSeed = settings.CalibrationSeed,
                HoldoutSeed = settings.HoldoutSeed,
                FixedDeltaTime = settings.FixedDeltaTime,
                TicksPerBlock = calibration.TicksPerBlock,
                WarmupBlocks = calibration.WarmupBlocks,
                SamplesPerCandidate = settings.SamplesPerCandidate,
                BoundarySamplesPerCandidate = settings.BoundarySamplesPerCandidate,
                LifetimeTicks = settings.LifetimeTicks,
                CandidateOrderSeed = settings.CandidateOrderSeed,
                BootstrapIterations = settings.BootstrapIterations,
                BootstrapConfidenceLevel = settings.BootstrapConfidenceLevel,
                MinimumImprovementPercent = settings.MinimumImprovementPercent,
                PrimaryTimingMetric =
                    "amortized_p95_ms_per_tick = resident_p95 + (ingress_p95 + export_p95) / lifetime_ticks",
                TimingIncludes =
                    "candidate dispatch; job Schedule; worker execution; Complete; separately timed full ingress and export",
                TimingExcludes =
                    "allocation; dataset generation; parity scan; hashing; JSON/CSV serialization; visualization",
                CalibrationDatasetHash = calibration.DatasetHash,
                HoldoutDatasetHash = holdoutHash,
                BoundaryContract = calibration.BoundaryContract,
                CalibrationDecision = calibrationDecision,
                FinalDecision = finalDecision,
                CalibrationResults = calibration.Results,
                HoldoutBaselineResult = holdoutBaseline,
                HoldoutSelectedResult = holdoutSelected,
            };
        }

        private static void RunPreflight(
            ICalibrationScenarioFactory factory,
            CalibrationRunSettings settings)
        {
            using (ICalibrationScenario scenario = factory.Create(
                       settings.PreflightElementCount,
                       settings.CalibrationSeed))
            {
                if (scenario.CandidateCount <= 0)
                    throw new InvalidOperationException("The scenario created no candidates.");

                for (int index = 0; index < scenario.CandidateCount; index++)
                {
                    ICalibrationCandidate candidate = scenario.GetCandidate(index);
                    candidate.BoundaryCost.Ingress();
                    candidate.Execute(settings.PreflightTicks, settings.FixedDeltaTime);
                    candidate.BoundaryCost.Export();
                }

                ICalibrationCandidate reference = scenario.GetCandidate(scenario.ReferenceCandidateIndex);
                for (int index = 0; index < scenario.CandidateCount; index++)
                {
                    ParityReport parity = scenario.ParityValidator.Validate(
                        reference,
                        scenario.GetCandidate(index),
                        settings.ParityTolerance);
                    if (!parity.Passed)
                    {
                        throw new InvalidOperationException(
                            $"Preflight parity failed for {factory.Descriptor.ScenarioId}/" +
                            $"{FormatCandidate(scenario.GetCandidate(index).Descriptor)} at " +
                            $"index {parity.FirstMismatchIndex}: {parity.Reason}");
                    }
                }
            }
        }

        private static PhaseMeasurement MeasurePhase(
            ICalibrationScenarioFactory factory,
            CalibrationRunSettings settings,
            BenchmarkPhase phase,
            int elementCount,
            uint datasetSeed,
            CandidateDescriptor[] requestedCandidates,
            int fixedTicksPerBlock,
            int fixedWarmupBlocks,
            uint orderSeed)
        {
            using (ICalibrationScenario scenario = factory.Create(
                       elementCount,
                       datasetSeed,
                       requestedCandidates))
            {
                CandidateMeasurement[] candidates = CreateMeasurements(
                    scenario,
                    settings.SamplesPerCandidate,
                    settings.BoundarySamplesPerCandidate);
                WarmBoundaryOperations(candidates, settings.FixedDeltaTime);
                MeasureIngress(candidates, settings.BoundarySamplesPerCandidate, orderSeed);

                int ticksPerBlock = fixedTicksPerBlock > 0
                    ? fixedTicksPerBlock
                    : DetermineTicksPerBlock(
                        scenario.GetCandidate(scenario.ReferenceCandidateIndex),
                        settings);
                int warmupBlocks = fixedWarmupBlocks > 0
                    ? fixedWarmupBlocks
                    : DetermineWarmupBlocks(
                        scenario.GetCandidate(scenario.ReferenceCandidateIndex),
                        ticksPerBlock,
                        settings);

                ResetCandidates(candidates);
                WarmResident(candidates, ticksPerBlock, warmupBlocks, settings.FixedDeltaTime);
                MeasureResident(
                    candidates,
                    ticksPerBlock,
                    settings.SamplesPerCandidate,
                    settings.FixedDeltaTime,
                    orderSeed ^ 0x7F4A7C15u);
                MeasureExport(
                    candidates,
                    settings.BoundarySamplesPerCandidate,
                    orderSeed ^ 0x94D049BBu);
                ValidateParity(scenario, candidates, settings.ParityTolerance);

                return new PhaseMeasurement
                {
                    DatasetHash = scenario.DatasetHash,
                    BoundaryContract = scenario
                        .GetCandidate(scenario.ReferenceCandidateIndex)
                        .BoundaryCost
                        .Descriptor,
                    TicksPerBlock = ticksPerBlock,
                    WarmupBlocks = warmupBlocks,
                    Results = BuildResults(
                        factory.Descriptor.ScenarioId,
                        phase,
                        candidates,
                        elementCount,
                        ticksPerBlock,
                        settings.LifetimeTicks),
                };
            }
        }

        private static CandidateMeasurement[] CreateMeasurements(
            ICalibrationScenario scenario,
            int residentSampleCount,
            int boundarySampleCount)
        {
            var measurements = new CandidateMeasurement[scenario.CandidateCount];
            for (int index = 0; index < measurements.Length; index++)
            {
                measurements[index] = new CandidateMeasurement
                {
                    Candidate = scenario.GetCandidate(index),
                    ResidentSamples = new double[residentSampleCount],
                    IngressSamples = new double[boundarySampleCount],
                    ExportSamples = new double[boundarySampleCount],
                };
            }
            return measurements;
        }

        private static void WarmBoundaryOperations(
            CandidateMeasurement[] candidates,
            float fixedDeltaTime)
        {
            for (int index = 0; index < candidates.Length; index++)
            {
                ICalibrationCandidate candidate = candidates[index].Candidate;
                for (int warmup = 0; warmup < 3; warmup++)
                {
                    candidate.BoundaryCost.Ingress();
                    candidate.Execute(1, fixedDeltaTime);
                    candidate.BoundaryCost.Export();
                }
            }
        }

        private static void MeasureIngress(
            CandidateMeasurement[] candidates,
            int sampleCount,
            uint orderSeed)
        {
            int[] order = CreateOrder(candidates.Length);
            uint randomState = NonZero(orderSeed);
            for (int round = 0; round < sampleCount; round++)
            {
                Shuffle(order, ref randomState);
                for (int position = 0; position < order.Length; position++)
                {
                    CandidateMeasurement measurement = candidates[order[position]];
                    measurement.IngressSamples[round] = MeasureIngress(
                        measurement.Candidate,
                        out long allocationBytes);
                    measurement.BoundaryManagedAllocationBytes += allocationBytes;
                }
            }
        }

        private static void ResetCandidates(CandidateMeasurement[] candidates)
        {
            for (int index = 0; index < candidates.Length; index++)
                candidates[index].Candidate.BoundaryCost.Ingress();
        }

        private static void WarmResident(
            CandidateMeasurement[] candidates,
            int ticksPerBlock,
            int warmupBlocks,
            float fixedDeltaTime)
        {
            for (int block = 0; block < warmupBlocks; block++)
            {
                for (int index = 0; index < candidates.Length; index++)
                    candidates[index].Candidate.Execute(ticksPerBlock, fixedDeltaTime);
            }
        }

        private static void MeasureResident(
            CandidateMeasurement[] candidates,
            int ticksPerBlock,
            int sampleCount,
            float fixedDeltaTime,
            uint orderSeed)
        {
            int[] order = CreateOrder(candidates.Length);
            uint randomState = NonZero(orderSeed);
            for (int round = 0; round < sampleCount; round++)
            {
                Shuffle(order, ref randomState);
                for (int position = 0; position < order.Length; position++)
                {
                    CandidateMeasurement measurement = candidates[order[position]];
                    double blockMilliseconds = MeasureResident(
                        measurement.Candidate,
                        ticksPerBlock,
                        fixedDeltaTime,
                        out long allocationBytes);
                    measurement.ResidentSamples[round] = blockMilliseconds / ticksPerBlock;
                    measurement.HotPathManagedAllocationBytes += allocationBytes;
                }
            }
        }

        private static void MeasureExport(
            CandidateMeasurement[] candidates,
            int sampleCount,
            uint orderSeed)
        {
            int[] order = CreateOrder(candidates.Length);
            uint randomState = NonZero(orderSeed);
            for (int round = 0; round < sampleCount; round++)
            {
                Shuffle(order, ref randomState);
                for (int position = 0; position < order.Length; position++)
                {
                    CandidateMeasurement measurement = candidates[order[position]];
                    measurement.ExportSamples[round] = MeasureExport(
                        measurement.Candidate,
                        out long allocationBytes);
                    measurement.BoundaryManagedAllocationBytes += allocationBytes;
                }
            }
        }

        private static void ValidateParity(
            ICalibrationScenario scenario,
            CandidateMeasurement[] candidates,
            float tolerance)
        {
            ICalibrationCandidate reference = scenario.GetCandidate(scenario.ReferenceCandidateIndex);
            for (int index = 0; index < candidates.Length; index++)
            {
                candidates[index].Parity = scenario.ParityValidator.Validate(
                    reference,
                    candidates[index].Candidate,
                    tolerance);
            }
        }

        private static LayoutBenchmarkResult[] BuildResults(
            string scenarioId,
            BenchmarkPhase phase,
            CandidateMeasurement[] candidates,
            int elementCount,
            int ticksPerBlock,
            int lifetimeTicks)
        {
            var results = new LayoutBenchmarkResult[candidates.Length];
            for (int index = 0; index < candidates.Length; index++)
            {
                CandidateMeasurement measurement = candidates[index];
                var residentScratch = new double[measurement.ResidentSamples.Length];
                var boundaryScratch = new double[Math.Max(
                    measurement.IngressSamples.Length,
                    measurement.ExportSamples.Length)];
                LatencySummary resident = BenchmarkStatistics.Calculate(
                    measurement.ResidentSamples,
                    measurement.ResidentSamples.Length,
                    residentScratch);
                LatencySummary ingress = BenchmarkStatistics.Calculate(
                    measurement.IngressSamples,
                    measurement.IngressSamples.Length,
                    boundaryScratch);
                LatencySummary export = BenchmarkStatistics.Calculate(
                    measurement.ExportSamples,
                    measurement.ExportSamples.Length,
                    boundaryScratch);
                var amortizedSamples = new double[measurement.ResidentSamples.Length];
                LatencySummary amortized = BenchmarkStatistics.CalculateAmortizedLatency(
                    measurement.ResidentSamples,
                    measurement.IngressSamples,
                    measurement.ExportSamples,
                    lifetimeTicks,
                    amortizedSamples,
                    residentScratch);

                results[index] = new LayoutBenchmarkResult
                {
                    ScenarioId = scenarioId,
                    Phase = phase,
                    Candidate = measurement.Candidate.Descriptor,
                    ElementCount = elementCount,
                    StepsPerSample = ticksPerBlock,
                    Latency = resident,
                    BoundaryCost = new BoundaryCostSummary
                    {
                        IngressLatency = ingress,
                        ExportLatency = export,
                        LifetimeTicks = lifetimeTicks,
                        AmortizedMedianMillisecondsPerTick =
                            (ingress.MedianMilliseconds + export.MedianMilliseconds) / lifetimeTicks,
                        AmortizedP95MillisecondsPerTick =
                            (ingress.P95Milliseconds + export.P95Milliseconds) / lifetimeTicks,
                    },
                    AmortizedLatency = amortized,
                    ResidentSamplesMillisecondsPerTick = measurement.ResidentSamples,
                    IngressSamplesMilliseconds = measurement.IngressSamples,
                    ExportSamplesMilliseconds = measurement.ExportSamples,
                    AmortizedSamplesMillisecondsPerTick = amortizedSamples,
                    Completed = true,
                    ParityPassed = measurement.Parity.Passed,
                    Parity = measurement.Parity,
                    HotPathManagedAllocationBytes = measurement.HotPathManagedAllocationBytes,
                    BoundaryManagedAllocationBytes = measurement.BoundaryManagedAllocationBytes,
                    ResidentBytes = measurement.Candidate.ResidentBytes,
                    StateHash = measurement.Parity.CandidateStateHash,
                    FailureReason = measurement.Parity.Passed ? string.Empty : measurement.Parity.Reason,
                };
            }
            return results;
        }

        private static int DetermineTicksPerBlock(
            ICalibrationCandidate baseline,
            CalibrationRunSettings settings)
        {
            baseline.BoundaryCost.Ingress();
            baseline.Execute(4, settings.FixedDeltaTime);
            int ticks = 1;
            while (true)
            {
                baseline.BoundaryCost.Ingress();
                double milliseconds = MeasureResident(
                    baseline,
                    ticks,
                    settings.FixedDeltaTime,
                    out _);
                if (milliseconds >= settings.TargetBlockMilliseconds ||
                    ticks >= settings.MaximumTicksPerBlock)
                {
                    baseline.BoundaryCost.Ingress();
                    return ticks;
                }
                ticks = Math.Min(ticks * 2, settings.MaximumTicksPerBlock);
            }
        }

        private static int DetermineWarmupBlocks(
            ICalibrationCandidate baseline,
            int ticksPerBlock,
            CalibrationRunSettings settings)
        {
            if (settings.MinimumWarmupSeconds <= 0d)
                return settings.WarmupBlocks;

            baseline.BoundaryCost.Ingress();
            baseline.Execute(4, settings.FixedDeltaTime);
            double blockMilliseconds = MeasureResident(
                baseline,
                ticksPerBlock,
                settings.FixedDeltaTime,
                out _);
            baseline.BoundaryCost.Ingress();
            int timeBased = (int)Math.Ceiling(
                (settings.MinimumWarmupSeconds * 1000d) /
                Math.Max(0.001d, blockMilliseconds));
            return Math.Max(settings.WarmupBlocks, timeBased);
        }

        private static double MeasureResident(
            ICalibrationCandidate candidate,
            int ticks,
            float fixedDeltaTime,
            out long managedAllocationBytes)
        {
            long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            long timestampStart = Stopwatch.GetTimestamp();
            candidate.Execute(ticks, fixedDeltaTime);
            long timestampEnd = Stopwatch.GetTimestamp();
            long allocationEnd = GC.GetAllocatedBytesForCurrentThread();
            managedAllocationBytes = Math.Max(0L, allocationEnd - allocationStart);
            return TimestampsToMilliseconds(timestampEnd - timestampStart);
        }

        private static double MeasureIngress(
            ICalibrationCandidate candidate,
            out long managedAllocationBytes)
        {
            long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            long timestampStart = Stopwatch.GetTimestamp();
            candidate.BoundaryCost.Ingress();
            long timestampEnd = Stopwatch.GetTimestamp();
            long allocationEnd = GC.GetAllocatedBytesForCurrentThread();
            managedAllocationBytes = Math.Max(0L, allocationEnd - allocationStart);
            return TimestampsToMilliseconds(timestampEnd - timestampStart);
        }

        private static double MeasureExport(
            ICalibrationCandidate candidate,
            out long managedAllocationBytes)
        {
            long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            long timestampStart = Stopwatch.GetTimestamp();
            candidate.BoundaryCost.Export();
            long timestampEnd = Stopwatch.GetTimestamp();
            long allocationEnd = GC.GetAllocatedBytesForCurrentThread();
            managedAllocationBytes = Math.Max(0L, allocationEnd - allocationStart);
            return TimestampsToMilliseconds(timestampEnd - timestampStart);
        }

        private static double TimestampsToMilliseconds(long timestamps)
        {
            return timestamps * 1000d / Stopwatch.Frequency;
        }

        private static int[] CreateOrder(int count)
        {
            var order = new int[count];
            for (int index = 0; index < count; index++)
                order[index] = index;
            return order;
        }

        private static void Shuffle(int[] order, ref uint state)
        {
            for (int index = order.Length - 1; index > 0; index--)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                int swapIndex = (int)(state % (uint)(index + 1));
                int temporary = order[index];
                order[index] = order[swapIndex];
                order[swapIndex] = temporary;
            }
        }

        private static uint NonZero(uint state) => state == 0u ? 0xA341316Cu : state;

        private static string FormatCandidate(CandidateDescriptor candidate)
        {
            return string.IsNullOrEmpty(candidate.CandidateId)
                ? $"{candidate.LayoutId}-b{candidate.LogicalBatchSize}"
                : candidate.CandidateId;
        }

        private static void ValidateSettings(CalibrationRunSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (settings.ElementCount <= 0 || settings.HoldoutElementCount <= 0 ||
                settings.PreflightElementCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(settings), "Element counts must be positive.");
            }
            if (settings.CalibrationSeed == 0u || settings.HoldoutSeed == 0u)
                throw new ArgumentOutOfRangeException(nameof(settings), "Dataset seeds must be non-zero.");
            if (settings.PreflightTicks <= 0 || settings.WarmupBlocks <= 0 ||
                settings.SamplesPerCandidate < 3 || settings.BoundarySamplesPerCandidate < 3 ||
                settings.LifetimeTicks <= 0 || settings.MaximumTicksPerBlock <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(settings), "Sampling and lifetime settings are invalid.");
            }
            if (!(settings.FixedDeltaTime > 0f) || !(settings.TargetBlockMilliseconds > 0d) ||
                settings.MinimumWarmupSeconds < 0d || settings.MinimumImprovementPercent < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(settings), "Timing settings are invalid.");
            }
            if (settings.BootstrapIterations < 100 ||
                !(settings.BootstrapConfidenceLevel > 0d && settings.BootstrapConfidenceLevel < 1d))
            {
                throw new ArgumentOutOfRangeException(nameof(settings), "Bootstrap settings are invalid.");
            }
        }

        private sealed class CandidateMeasurement
        {
            public ICalibrationCandidate Candidate;
            public double[] ResidentSamples;
            public double[] IngressSamples;
            public double[] ExportSamples;
            public long HotPathManagedAllocationBytes;
            public long BoundaryManagedAllocationBytes;
            public ParityReport Parity;
        }

        private sealed class PhaseMeasurement
        {
            public string DatasetHash;
            public BoundaryCostDescriptor BoundaryContract;
            public int TicksPerBlock;
            public int WarmupBlocks;
            public LayoutBenchmarkResult[] Results;
        }
    }
}
