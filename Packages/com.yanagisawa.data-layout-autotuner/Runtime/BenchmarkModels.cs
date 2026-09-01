using System;

namespace Yanagisawa.DataLayoutAutotuner
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
    }

    [Serializable]
    public struct LayoutCandidate : IEquatable<LayoutCandidate>
    {
        public LayoutKind Layout;
        public int LogicalBatchSize;

        public LayoutCandidate(LayoutKind layout, int logicalBatchSize)
        {
            Layout = layout;
            LogicalBatchSize = logicalBatchSize;
        }

        public bool Equals(LayoutCandidate other)
        {
            return Layout == other.Layout && LogicalBatchSize == other.LogicalBatchSize;
        }

        public override bool Equals(object obj)
        {
            return obj is LayoutCandidate other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Layout * 397) ^ LogicalBatchSize;
            }
        }

        public static bool operator ==(LayoutCandidate left, LayoutCandidate right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(LayoutCandidate left, LayoutCandidate right)
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
    public sealed class LayoutBenchmarkResult
    {
        public BenchmarkPhase Phase;
        public LayoutCandidate Candidate;
        public int ElementCount;
        public int StepsPerSample;
        public LatencySummary Latency;
        public bool Completed;
        public bool ParityPassed;
        public long HotPathManagedAllocationBytes;
        public long ResidentBytes;
        public string StateHash;
        public string FailureReason;
    }

    [Serializable]
    public struct LayoutSelectionDecision
    {
        public LayoutSelectionStatus Status;
        public LayoutCandidate BaselineCandidate;
        public LayoutCandidate SelectedCandidate;
        public LayoutCandidate BestMeasuredCandidate;
        public double BaselineP95Milliseconds;
        public double BestMeasuredP95Milliseconds;
        public double ImprovementPercent;
        public double MinimumRequiredImprovementPercent;
        public int EligibleCandidateCount;
        public int RejectedParityCandidateCount;
        public string Reason;
    }

    [Serializable]
    public sealed class LayoutTuningProfile
    {
        public int SchemaVersion = 1;
        public string RunId;
        public string CreatedUtcIso8601;
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
        public string WorkloadId;
        public int ElementCount;
        public int HoldoutElementCount;
        public uint CalibrationSeed;
        public uint HoldoutSeed;
        public float FixedDeltaTime;
        public int TicksPerBlock;
        public int WarmupBlocks;
        public int SamplesPerCandidate;
        public uint CandidateOrderSeed;
        public string PrimaryTimingMetric;
        public string TimingIncludes;
        public string TimingExcludes;
        public string CalibrationDatasetHash;
        public string HoldoutDatasetHash;
        public LayoutSelectionDecision CalibrationDecision;
        public LayoutSelectionDecision FinalDecision;
        public LayoutBenchmarkResult[] CalibrationResults;
        public LayoutBenchmarkResult HoldoutBaselineResult;
        public LayoutBenchmarkResult HoldoutSelectedResult;
    }
}
