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
        Regression = 4,
    }

    [Serializable]
    public struct CandidateDescriptor : IEquatable<CandidateDescriptor>
    {
        public const int LegacyPolicySchemaVersion = 0;
        public const int CurrentPolicySchemaVersion = 1;

        public int PolicySchemaVersion;
        public string CandidateId;
        public string LayoutId;
        public string DisplayName;
        public int LogicalBatchSize;
        public bool IsBaseline;
        public int SortOrder;
        public LayoutPolicy Layout;
        public KernelPolicy Kernel;
        public BatchPolicy Batch;
        public ExecutionPolicy Execution;

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
            ProtocolIdentifier.RequireCanonical(layoutId, nameof(layoutId), "Layout ID");
            if (logicalBatchSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(logicalBatchSize));
            if (candidateId != null && candidateId.Length > 0)
                ProtocolIdentifier.RequireCanonical(candidateId, nameof(candidateId), "Candidate ID");

            LayoutId = layoutId;
            LogicalBatchSize = logicalBatchSize;
            IsBaseline = isBaseline;
            SortOrder = sortOrder;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? layoutId : displayName;
            CandidateId = string.IsNullOrWhiteSpace(candidateId)
                ? $"{layoutId}-b{logicalBatchSize}"
                : candidateId;
            PolicySchemaVersion = CurrentPolicySchemaVersion;
            Layout = LayoutPolicy.FromLegacy(layoutId);
            Kernel = KernelPolicy.LegacyUnspecified;
            Batch = BatchPolicy.JobBatch(logicalBatchSize);
            Execution = ExecutionPolicy.FrameFaithful;
        }

        public CandidateDescriptor(
            LayoutPolicy layout,
            KernelPolicy kernel,
            BatchPolicy batch,
            ExecutionPolicy execution,
            bool isBaseline,
            int sortOrder = 0,
            string displayName = null,
            string candidateId = null)
        {
            if (!layout.IsSpecified)
                throw new ArgumentException("A layout policy is required.", nameof(layout));
            if (!kernel.IsSpecified)
                throw new ArgumentException("A kernel policy is required.", nameof(kernel));
            if (!batch.IsSpecified || batch.LogicalBatchSize <= 0)
                throw new ArgumentException("A positive batch policy is required.", nameof(batch));
            if (!execution.IsSpecified)
                throw new ArgumentException("An execution policy is required.", nameof(execution));
            if (candidateId != null && candidateId.Length > 0)
                ProtocolIdentifier.RequireCanonical(candidateId, nameof(candidateId), "Candidate ID");

            PolicySchemaVersion = CurrentPolicySchemaVersion;
            Layout = layout;
            Kernel = kernel;
            Batch = batch;
            Execution = execution;
            LayoutId = layout.PolicyId;
            LogicalBatchSize = batch.LogicalBatchSize;
            IsBaseline = isBaseline;
            SortOrder = sortOrder;
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? $"{layout.PolicyId} / {kernel.PolicyId} / {execution.PolicyId} / b{batch.LogicalBatchSize}"
                : displayName;
            CandidateId = string.IsNullOrWhiteSpace(candidateId)
                ? $"{layout.PolicyId}-{kernel.PolicyId}-b{batch.LogicalBatchSize}-{execution.PolicyId}"
                : candidateId;
        }

        public LayoutPolicy EffectiveLayout =>
            Layout.IsSpecified ? Layout : LayoutPolicy.FromLegacy(LayoutId);

        public KernelPolicy EffectiveKernel =>
            Kernel.IsSpecified ? Kernel : KernelPolicy.LegacyUnspecified;

        public BatchPolicy EffectiveBatch =>
            Batch.IsSpecified
                ? Batch
                : LogicalBatchSize > 0
                    ? BatchPolicy.JobBatch(LogicalBatchSize)
                    : default;

        public ExecutionPolicy EffectiveExecution =>
            Execution.IsSpecified ? Execution : ExecutionPolicy.FrameFaithful;

        public CandidateDescriptor NormalizePolicies()
        {
            if (PolicySchemaVersion == CurrentPolicySchemaVersion)
            {
                ValidateFactorConsistency();
                return this;
            }
            if (PolicySchemaVersion != LegacyPolicySchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported candidate policy schema {PolicySchemaVersion}.");
            }

            CandidateDescriptor normalized = this;
            normalized.PolicySchemaVersion = CurrentPolicySchemaVersion;
            normalized.Layout = EffectiveLayout;
            normalized.Kernel = EffectiveKernel;
            normalized.Batch = EffectiveBatch;
            normalized.Execution = EffectiveExecution;
            normalized.ValidateFactorConsistency();
            return normalized;
        }

        public void ValidateFactorConsistency()
        {
            if (PolicySchemaVersion != CurrentPolicySchemaVersion)
                throw new InvalidOperationException($"Unsupported candidate policy schema {PolicySchemaVersion}.");
            if (!ProtocolIdentifier.IsCanonical(CandidateId))
                throw new InvalidOperationException("CandidateId is the required canonical candidate identity.");
            if (!ProtocolIdentifier.IsCanonical(LayoutId) || LogicalBatchSize <= 0)
                throw new InvalidOperationException("Legacy layout and logical-batch compatibility fields are required.");
            LayoutPolicy layout = Layout;
            KernelPolicy kernel = Kernel;
            BatchPolicy batch = Batch;
            ExecutionPolicy execution = Execution;
            if (!ProtocolIdentifier.IsCanonical(layout.PolicyId) ||
                !ProtocolIdentifier.IsCanonical(kernel.PolicyId) ||
                !ProtocolIdentifier.IsCanonical(batch.PolicyId) ||
                !ProtocolIdentifier.IsCanonical(execution.PolicyId))
            {
                throw new InvalidOperationException(
                    "Policy IDs must be non-empty and have no surrounding whitespace.");
            }
            if (!string.Equals(LayoutId, layout.PolicyId, StringComparison.Ordinal))
                throw new InvalidOperationException("LayoutId must match Layout.PolicyId.");
            if (layout.BlockWidth <= 0 || layout.AlignmentBytes < 0 || layout.PaddingBytes < 0)
                throw new InvalidOperationException("Layout policy dimensions are invalid.");
            if (LogicalBatchSize != batch.LogicalBatchSize || batch.LogicalBatchSize <= 0)
                throw new InvalidOperationException("LogicalBatchSize must match Batch.LogicalBatchSize.");
            if (!kernel.IsSpecified || kernel.VectorWidth <= 0 ||
                !Enum.IsDefined(typeof(KernelControlFlow), kernel.ControlFlow))
                throw new InvalidOperationException("Kernel policy metadata is invalid.");
            if (!execution.IsSpecified ||
                !Enum.IsDefined(typeof(ExecutionTopology), execution.Topology) ||
                execution.TemporalBlockTicks <= 0 ||
                (execution.Topology == ExecutionTopology.TemporalBlock &&
                 (execution.TemporalBlockTicks <= 1 || !execution.SemanticsPermitReordering)) ||
                (execution.Topology != ExecutionTopology.TemporalBlock &&
                 execution.TemporalBlockTicks != 1))
            {
                throw new InvalidOperationException("Execution policy metadata is invalid.");
            }
        }

        public bool Equals(CandidateDescriptor other)
        {
            return string.Equals(CandidateId, other.CandidateId, StringComparison.Ordinal) &&
                   string.Equals(LayoutId, other.LayoutId, StringComparison.Ordinal) &&
                   LogicalBatchSize == other.LogicalBatchSize &&
                   IsBaseline == other.IsBaseline &&
                   SortOrder == other.SortOrder &&
                   EffectiveLayout.Equals(other.EffectiveLayout) &&
                   EffectiveKernel.Equals(other.EffectiveKernel) &&
                   EffectiveBatch.Equals(other.EffectiveBatch) &&
                   EffectiveExecution.Equals(other.EffectiveExecution);
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
                hash = (hash * 397) ^ SortOrder;
                hash = (hash * 397) ^ EffectiveLayout.GetHashCode();
                hash = (hash * 397) ^ EffectiveKernel.GetHashCode();
                hash = (hash * 397) ^ EffectiveBatch.GetHashCode();
                return (hash * 397) ^ EffectiveExecution.GetHashCode();
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
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion;
        public BootstrapEstimatorKind EstimatorKind;
        public bool HasLogRatioEstimate;
        public int Iterations;
        public double ConfidenceLevel;
        public uint RandomSeed;
        public string Estimand;
        public string ResamplingUnit;
        public double PointEstimateLogRatio;
        public double LowerBoundLogRatio;
        public double UpperBoundLogRatio;
        public double PointEstimatePercent;
        public double LowerBoundPercent;
        public double UpperBoundPercent;
    }

    [Serializable]
    public sealed class SamplingDesignDescriptor
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public MeasurementOrderKind CandidateOrder;
        public string PairingUnit;
        public EvidenceScope EvidenceScope;
        public bool CalibrationTunesCandidates;
        public bool HoldoutRetuningPermitted;
        /// <summary>
        /// True only for an in-memory schema-2 migration. Missing historical
        /// phase/count fields remain unknown and are never synthesized.
        /// </summary>
        public bool ReconstructedFromSchema2;
        public string UncertaintyDescription;
    }

    [Serializable]
    public sealed class LayoutBenchmarkResult
    {
        public const int LegacySampleSchemaVersion = 0;
        public const int CurrentSampleSchemaVersion = 1;

        public int SampleSchemaVersion = CurrentSampleSchemaVersion;
        public string ScenarioId;
        public int ScenarioContractVersion;
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
        public int[] ResidentBlockIds;
        public int[] IngressBlockIds;
        public int[] ExportBlockIds;
        public int[] ResidentOrderPositions;
        public int[] IngressOrderPositions;
        public int[] ExportOrderPositions;
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
        public DecisionStage DecisionStage;
        public LayoutSelectionStatus Status;
        public CandidateDescriptor BaselineCandidate;
        public CandidateDescriptor SelectedCandidate;
        public CandidateDescriptor BestMeasuredCandidate;
        public double BaselineP95Milliseconds;
        public double BestMeasuredP95Milliseconds;
        public double ImprovementPercent;
        public BootstrapConfidenceInterval ImprovementConfidenceInterval;
        public double MinimumRequiredImprovementPercent;
        public double SelectionRegretPercent;
        public bool FellBackBecauseStatisticalTie;
        public int EligibleCandidateCount;
        public int RejectedParityCandidateCount;
        public string MultiplicityControl;
        public string Reason;
    }

    [Serializable]
    public sealed class ProcessPairedBenchmarkResult
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public string ProcessId;
        public string DeviceId;
        public LayoutBenchmarkResult Baseline;
        public LayoutBenchmarkResult Candidate;
    }

    [Serializable]
    public struct HierarchicalBootstrapConfidenceInterval
    {
        public int SchemaVersion;
        public EvidenceScope EvidenceScope;
        public int ProcessCount;
        public int DeviceCount;
        public BootstrapConfidenceInterval ImprovementConfidenceInterval;
    }

    [Serializable]
    public sealed class ScenarioCalibrationProfile
    {
        public int SchemaVersion = 3;
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
        public SamplingDesignDescriptor SamplingDesign;
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
        public int SchemaVersion = 3;
        public string ProductName = "Data Layout Calibrator";
        public string RunId;
        public string CreatedUtcIso8601;
        public CalibrationEnvironment Environment;
        public ScenarioCalibrationProfile[] Scenarios;
    }
}
