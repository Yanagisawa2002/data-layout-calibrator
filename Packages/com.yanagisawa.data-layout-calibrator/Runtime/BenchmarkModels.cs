using System;

namespace Yanagisawa.DataLayoutCalibrator
{
    public enum BenchmarkPhase
    {
        Calibration = 0,
        Holdout = 1,
    }

    public enum LayoutSelectionStatus
    {
        Invalid = 0,
        Inconclusive = 1,
        Optimized = 2,
        StatisticalTie = 3,
    }

    [Serializable]
    public struct CandidateDescriptor : IEquatable<CandidateDescriptor>
    {
        public string CandidateId;
        public string LayoutId;
        public string DisplayName;
        public int LogicalBatchSize;
        public bool IsBaseline;
        public int SortOrder;

        public CandidateDescriptor(LayoutKind layout, int logicalBatchSize)
            : this(
                layout.ToString(),
                logicalBatchSize,
                layout == LayoutKind.AoS,
                (int)layout,
                layout.ToString())
        {
        }

        public CandidateDescriptor(
            string layoutId,
            int logicalBatchSize,
            bool isBaseline,
            int sortOrder = 0,
            string displayName = null,
            string candidateId = null)
        {
            if (string.IsNullOrWhiteSpace(layoutId))
                throw new ArgumentException("Layout ID is required.", nameof(layoutId));
            if (logicalBatchSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(logicalBatchSize));

            LayoutId = layoutId;
            LogicalBatchSize = logicalBatchSize;
            IsBaseline = isBaseline;
            SortOrder = sortOrder;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? layoutId : displayName;
            CandidateId = string.IsNullOrWhiteSpace(candidateId)
                ? $"{layoutId}-b{logicalBatchSize}"
                : candidateId;
        }

        public bool Equals(CandidateDescriptor other)
        {
            return string.Equals(CandidateId, other.CandidateId, StringComparison.Ordinal) &&
                   string.Equals(LayoutId, other.LayoutId, StringComparison.Ordinal) &&
                   LogicalBatchSize == other.LogicalBatchSize &&
                   IsBaseline == other.IsBaseline &&
                   SortOrder == other.SortOrder;
        }

        public override bool Equals(object obj)
        {
            return obj is CandidateDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = CandidateId == null ? 0 : CandidateId.GetHashCode();
                hash = (hash * 397) ^ (LayoutId == null ? 0 : LayoutId.GetHashCode());
                hash = (hash * 397) ^ LogicalBatchSize;
                hash = (hash * 397) ^ (IsBaseline ? 1 : 0);
                return (hash * 397) ^ SortOrder;
            }
        }

        public static bool operator ==(CandidateDescriptor left, CandidateDescriptor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(CandidateDescriptor left, CandidateDescriptor right)
        {
            return !left.Equals(right);
        }
    }

    [Serializable]
    public struct LatencySummary
    {
        public int SampleCount;
        public double MinimumMilliseconds;
        public double MedianMilliseconds;
        public double P95Milliseconds;
        public double P99Milliseconds;
        public double MaximumMilliseconds;
        public double MedianAbsoluteDeviationMilliseconds;
    }

    [Serializable]
    public struct BoundaryCostSummary
    {
        public LatencySummary IngressLatency;
        public LatencySummary ExportLatency;
        public int LifetimeTicks;
        public double AmortizedMedianMillisecondsPerTick;
        public double AmortizedP95MillisecondsPerTick;
    }

    [Serializable]
    public struct BootstrapConfidenceInterval
    {
        public int Iterations;
        public double ConfidenceLevel;
        public double PointEstimatePercent;
        public double LowerBoundPercent;
        public double UpperBoundPercent;
    }

    [Serializable]
    public sealed class LayoutBenchmarkResult
    {
        public string ScenarioId;
        public BenchmarkPhase Phase;
        public CandidateDescriptor Candidate;
        public int ElementCount;
        public int StepsPerSample;
        public LatencySummary Latency;
        public BoundaryCostSummary BoundaryCost;
        public LatencySummary AmortizedLatency;
        public double[] ResidentSamplesMillisecondsPerTick;
        public double[] IngressSamplesMilliseconds;
        public double[] ExportSamplesMilliseconds;
        public double[] AmortizedSamplesMillisecondsPerTick;
        public bool Completed;
        public bool ParityPassed;
        public ParityReport Parity;
        public long HotPathManagedAllocationBytes;
        public long BoundaryManagedAllocationBytes;
        public long ResidentBytes;
        public string StateHash;
        public string FailureReason;
    }

    [Serializable]
    public struct LayoutSelectionDecision
    {
        public LayoutSelectionStatus Status;
        public CandidateDescriptor BaselineCandidate;
        public CandidateDescriptor SelectedCandidate;
        public CandidateDescriptor BestMeasuredCandidate;
        public double BaselineP95Milliseconds;
        public double BestMeasuredP95Milliseconds;
        public double ImprovementPercent;
        public BootstrapConfidenceInterval ImprovementConfidenceInterval;
        public double MinimumRequiredImprovementPercent;
        public bool FellBackBecauseStatisticalTie;
        public int EligibleCandidateCount;
        public int RejectedParityCandidateCount;
        public string Reason;
    }

    [Serializable]
    public sealed class ScenarioCalibrationProfile
    {
        public int SchemaVersion = 2;
        public ScenarioDescriptor Scenario;
        public int ElementCount;
        public int HoldoutElementCount;
        public uint CalibrationSeed;
        public uint HoldoutSeed;
        public float FixedDeltaTime;
        public int TicksPerBlock;
        public int WarmupBlocks;
        public int SamplesPerCandidate;
        public int BoundarySamplesPerCandidate;
        public int LifetimeTicks;
        public uint CandidateOrderSeed;
        public int BootstrapIterations;
        public double BootstrapConfidenceLevel;
        public double MinimumImprovementPercent;
        public string PrimaryTimingMetric;
        public string TimingIncludes;
        public string TimingExcludes;
        public string CalibrationDatasetHash;
        public string HoldoutDatasetHash;
        public BoundaryCostDescriptor BoundaryContract;
        public LayoutSelectionDecision CalibrationDecision;
        public LayoutSelectionDecision FinalDecision;
        public LayoutBenchmarkResult[] CalibrationResults;
        public LayoutBenchmarkResult HoldoutBaselineResult;
        public LayoutBenchmarkResult HoldoutSelectedResult;
    }

    [Serializable]
    public struct CalibrationEnvironment
    {
        public string UnityVersion;
        public string BurstVersion;
        public string CollectionsVersion;
        public string MathematicsVersion;
        public string ScriptingBackend;
        public string BuildType;
        public string OperatingSystem;
        public string Processor;
        public int LogicalProcessorCount;
        public int JobWorkerCount;
        public string GraphicsDevice;
    }

    [Serializable]
    public sealed class CalibrationSuiteProfile
    {
        public int SchemaVersion = 2;
        public string ProductName = "Data Layout Calibrator";
        public string RunId;
        public string CreatedUtcIso8601;
        public CalibrationEnvironment Environment;
        public ScenarioCalibrationProfile[] Scenarios;
    }
}
