using System;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>
    /// Final, frozen state of one evaluated envelope cell. Every non-advantage
    /// state selects the tuned AoS baseline.
    /// </summary>
    public enum EnvelopeCellStatus
    {
        Invalid = 0,
        AoSFallback = 1,
        StatisticalGreyZone = 2,
        CredibleAdvantage = 3,
        HoldoutRejected = 4,
    }

    public enum CandidateEvidenceGateStatus
    {
        Eligible = 0,
        Incomplete = 1,
        ContractInfeasible = 2,
        MemoryInfeasible = 3,
        ParityFailed = 4,
        ManagedAllocationDetected = 5,
        InvalidPointEstimate = 6,
        InsufficientSamples = 7,
        InvalidUncertaintyEvidence = 8,
        EvidencePartitionMismatch = 9,
    }

    /// <summary>
    /// Shape of candidate cost relative to tuned AoS for positive lifetimes.
    /// CandidateWinsAboveLifetime and CandidateWinsBelowLifetime exclude the
    /// exact crossing, where the two costs are equal.
    /// </summary>
    public enum BreakEvenKind
    {
        Invalid = 0,
        EqualCosts = 1,
        CandidateAlwaysAdvantaged = 2,
        CandidateNeverAdvantaged = 3,
        CandidateWinsAboveLifetime = 4,
        CandidateWinsBelowLifetime = 5,
    }

    public enum BreakEvenUncertaintyStatus
    {
        Invalid = 0,
        StableRegime = 1,
        BoundedCrossing = 2,
        MixedRegimes = 3,
    }

    /// <summary>
    /// Artifact-local candidate identity. CandidateId is always supplied
    /// explicitly and is the canonical join key. CandidateDefinitionSha256
    /// binds that ID to the full scientific candidate definition; DisplayName
    /// is never used for identity or selection.
    /// </summary>
    [Serializable]
    public struct EnvelopeCandidateDescriptor : IEquatable<EnvelopeCandidateDescriptor>
    {
        public string CandidateId;
        public string CandidateDefinitionSha256;
        public string DisplayName;
        public string LayoutPolicyId;
        public string KernelPolicyId;
        public string BatchPolicyId;
        public string ExecutionPolicyId;
        public int LogicalBatchSize;
        public bool IsTunedAoSBaseline;
        public int SortOrder;

        public EnvelopeCandidateDescriptor(
            string candidateId,
            string candidateDefinitionSha256,
            string layoutPolicyId,
            string kernelPolicyId,
            string batchPolicyId,
            string executionPolicyId,
            int logicalBatchSize,
            bool isTunedAoSBaseline,
            int sortOrder = 0,
            string displayName = null)
        {
            if (string.IsNullOrWhiteSpace(candidateId))
                throw new ArgumentException("Candidate ID is required.", nameof(candidateId));
            if (!DecisionEvidenceStatistics.IsCanonicalSha256(candidateDefinitionSha256))
            {
                throw new ArgumentException(
                    "Candidate definition SHA-256 must contain exactly 64 uppercase hexadecimal characters.",
                    nameof(candidateDefinitionSha256));
            }
            if (string.IsNullOrWhiteSpace(layoutPolicyId))
                throw new ArgumentException("Layout policy ID is required.", nameof(layoutPolicyId));
            if (string.IsNullOrWhiteSpace(kernelPolicyId))
                throw new ArgumentException("Kernel policy ID is required.", nameof(kernelPolicyId));
            if (string.IsNullOrWhiteSpace(batchPolicyId))
                throw new ArgumentException("Batch policy ID is required.", nameof(batchPolicyId));
            if (string.IsNullOrWhiteSpace(executionPolicyId))
                throw new ArgumentException("Execution policy ID is required.", nameof(executionPolicyId));
            if (logicalBatchSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(logicalBatchSize));

            CandidateId = candidateId;
            CandidateDefinitionSha256 = candidateDefinitionSha256;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? candidateId : displayName;
            LayoutPolicyId = layoutPolicyId;
            KernelPolicyId = kernelPolicyId;
            BatchPolicyId = batchPolicyId;
            ExecutionPolicyId = executionPolicyId;
            LogicalBatchSize = logicalBatchSize;
            IsTunedAoSBaseline = isTunedAoSBaseline;
            SortOrder = sortOrder;
        }

        public bool Equals(EnvelopeCandidateDescriptor other)
        {
            return string.Equals(CandidateId, other.CandidateId, StringComparison.Ordinal) &&
                   string.Equals(
                       CandidateDefinitionSha256,
                       other.CandidateDefinitionSha256,
                       StringComparison.Ordinal) &&
                   string.Equals(LayoutPolicyId, other.LayoutPolicyId, StringComparison.Ordinal) &&
                   string.Equals(KernelPolicyId, other.KernelPolicyId, StringComparison.Ordinal) &&
                   string.Equals(BatchPolicyId, other.BatchPolicyId, StringComparison.Ordinal) &&
                   string.Equals(ExecutionPolicyId, other.ExecutionPolicyId, StringComparison.Ordinal) &&
                   LogicalBatchSize == other.LogicalBatchSize &&
                   IsTunedAoSBaseline == other.IsTunedAoSBaseline &&
                   SortOrder == other.SortOrder;
        }

        public override bool Equals(object obj)
        {
            return obj is EnvelopeCandidateDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = CandidateId == null ? 0 : CandidateId.GetHashCode();
                hash = (hash * 397) ^ (CandidateDefinitionSha256 == null
                    ? 0
                    : CandidateDefinitionSha256.GetHashCode());
                hash = (hash * 397) ^ (LayoutPolicyId == null ? 0 : LayoutPolicyId.GetHashCode());
                hash = (hash * 397) ^ (KernelPolicyId == null ? 0 : KernelPolicyId.GetHashCode());
                hash = (hash * 397) ^ (BatchPolicyId == null ? 0 : BatchPolicyId.GetHashCode());
                hash = (hash * 397) ^ (ExecutionPolicyId == null ? 0 : ExecutionPolicyId.GetHashCode());
                hash = (hash * 397) ^ LogicalBatchSize;
                hash = (hash * 397) ^ (IsTunedAoSBaseline ? 1 : 0);
                return (hash * 397) ^ SortOrder;
            }
        }

        public static bool operator ==(
            EnvelopeCandidateDescriptor left,
            EnvelopeCandidateDescriptor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            EnvelopeCandidateDescriptor left,
            EnvelopeCandidateDescriptor right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// One point in the explicit deployment-context scan.
    /// </summary>
    [Serializable]
    public struct AdvantageEnvelopeAxis : IEquatable<AdvantageEnvelopeAxis>
    {
        public int ElementCount;
        public int LifetimeTicks;
        public double HotToColdRatio;
        public int WorkerCount;
        public string ExecutionPolicyId;

        public AdvantageEnvelopeAxis(
            int elementCount,
            int lifetimeTicks,
            double hotToColdRatio,
            int workerCount,
            string executionPolicyId)
        {
            if (elementCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(elementCount));
            if (lifetimeTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(lifetimeTicks));
            if (hotToColdRatio < 0d || double.IsNaN(hotToColdRatio) ||
                double.IsInfinity(hotToColdRatio))
            {
                throw new ArgumentOutOfRangeException(nameof(hotToColdRatio));
            }
            if (workerCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(workerCount));
            if (string.IsNullOrWhiteSpace(executionPolicyId))
                throw new ArgumentException("Execution policy ID is required.", nameof(executionPolicyId));

            ElementCount = elementCount;
            LifetimeTicks = lifetimeTicks;
            HotToColdRatio = hotToColdRatio;
            WorkerCount = workerCount;
            ExecutionPolicyId = executionPolicyId;
        }

        public bool Equals(AdvantageEnvelopeAxis other)
        {
            return ElementCount == other.ElementCount &&
                   LifetimeTicks == other.LifetimeTicks &&
                   HotToColdRatio.Equals(other.HotToColdRatio) &&
                   WorkerCount == other.WorkerCount &&
                   string.Equals(ExecutionPolicyId, other.ExecutionPolicyId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AdvantageEnvelopeAxis other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ElementCount;
                hash = (hash * 397) ^ LifetimeTicks;
                hash = (hash * 397) ^ HotToColdRatio.GetHashCode();
                hash = (hash * 397) ^ WorkerCount;
                return (hash * 397) ^ (ExecutionPolicyId == null ? 0 : ExecutionPolicyId.GetHashCode());
            }
        }
    }

    /// <summary>
    /// One already-computed bootstrap replicate. ReplicateId aligns baseline and
    /// candidate draws without prescribing whether the scientific layer used a
    /// paired, hierarchical, or another preregistered resampling method.
    /// </summary>
    [Serializable]
    public struct BootstrapCostReplicate
    {
        public int ReplicateId;
        public double ResidentP95MillisecondsPerTick;
        public double IngressP95Milliseconds;
        public double ExportP95Milliseconds;
    }

    /// <summary>
    /// Decision-ready evidence supplied by the measurement/statistics layer.
    /// Raw samples remain in the source artifact referenced by EvidenceHash.
    /// </summary>
    [Serializable]
    public sealed class DecisionCandidateEvidence
    {
        public EnvelopeCandidateDescriptor Candidate;
        public bool Completed;
        public bool ContractFeasible;
        public bool MemoryFeasible;
        public bool ParityPassed;
        public long HotPathManagedAllocationBytes;
        public long BoundaryManagedAllocationBytes;
        public long ResidentBytes;
        public double ResidentP95MillisecondsPerTick;
        public double IngressP95Milliseconds;
        public double ExportP95Milliseconds;
        public int ResidentSampleCount;
        public int BoundarySampleCount;
        public string EvidencePartitionId;
        public string EvidenceHash;
        public BootstrapCostReplicate[] BootstrapReplicates;
    }

    [Serializable]
    public sealed class AdvantageEnvelopePolicy
    {
        public double MinimumImprovementPercent = 10d;
        public double ConfidenceLevel = 0.95d;
        public int MinimumBootstrapReplicates = 4000;
        public int MinimumCalibrationResidentSamples = 40;
        public int MinimumCalibrationBoundarySamples = 20;
        public int MinimumHoldoutResidentSamples = 40;
        public int MinimumHoldoutBoundarySamples = 20;
    }

    [Serializable]
    public sealed class AdvantageEnvelopeCellInput
    {
        public AdvantageEnvelopeAxis Axis;
        public DecisionCandidateEvidence[] CalibrationCandidates;
    }

    /// <summary>
    /// Calibration-only input. Calling Calibrate cannot inspect holdout data.
    /// </summary>
    [Serializable]
    public sealed class AdvantageEnvelopeCalibrationRequest
    {
        public int SchemaVersion = 1;
        public string EnvelopeId;
        public string CreatedUtcIso8601;
        public string ScenarioId;
        public int ContractVersion;
        public string CandidateSetHash;
        public string MeasurementSchemaHash;
        public string EnvironmentFingerprint;
        public string CalibrationSettingsHash;
        public string SourceArtifactId;
        public string SourceArtifactSha256;
        public string EvidenceScope;
        public string CalibrationUncertaintyMethod;
        public AdvantageEnvelopePolicy Policy;
        public AdvantageEnvelopeCellInput[] Cells;
    }

    [Serializable]
    public struct EnvelopeConfidenceInterval
    {
        public int ReplicateCount;
        public double ConfidenceLevel;
        public double PointEstimatePercent;
        public double LowerBoundPercent;
        public double UpperBoundPercent;
    }

    [Serializable]
    public struct BreakEvenEstimate
    {
        public BreakEvenKind Kind;
        public BreakEvenUncertaintyStatus UncertaintyStatus;
        public double ResidentDeltaMillisecondsPerTick;
        public double BoundaryDeltaMilliseconds;
        public double PointLifetimeTicks;
        public double LowerConfidenceLifetimeTicks;
        public double UpperConfidenceLifetimeTicks;
        public int ReplicateCount;
        public int SameRegimeReplicateCount;
        public double SameRegimePercent;
        public int EqualCostReplicateCount;
        public int AlwaysAdvantagedReplicateCount;
        public int NeverAdvantagedReplicateCount;
        public int WinsAboveLifetimeReplicateCount;
        public int WinsBelowLifetimeReplicateCount;
    }

    [Serializable]
    public sealed class EnvelopeCandidateOutcome
    {
        public EnvelopeCandidateDescriptor Candidate;
        public CandidateEvidenceGateStatus GateStatus;
        public bool Eligible;
        public string GateReason;
        public string EvidencePartitionId;
        public string SourceEvidenceHash;
        public int ResidentSampleCount;
        public int BoundarySampleCount;
        public int BootstrapReplicateCount;
        public long ResidentBytes;
        public double ResidentP95MillisecondsPerTick;
        public double BoundaryP95Milliseconds;
        public double AmortizedP95MillisecondsPerTick;
        public double ImprovementPercent;
        public EnvelopeConfidenceInterval ImprovementConfidenceInterval;
        public BreakEvenEstimate BreakEven;
        public bool MeetsMinimumEffect;
        public bool ClearsConfidenceGate;
        public bool CredibleCalibrationAdvantage;
    }

    [Serializable]
    public sealed class EnvelopeCalibrationCellDecision
    {
        public AdvantageEnvelopeAxis Axis;
        public EnvelopeCellStatus CalibrationStatus;
        public string CalibrationPartitionId;
        public string BaselineCandidateId;
        public string BestMeasuredCandidateId;
        public string FrozenCalibrationWinnerCandidateId;
        public double MinimumRequiredImprovementPercent;
        public double CalibrationImprovementPercent;
        public EnvelopeConfidenceInterval CalibrationConfidenceInterval;
        public EnvelopeCandidateOutcome[] CandidateOutcomes;
        public string Reason;
    }

    [Serializable]
    public sealed class AdvantageEnvelopeCalibration
    {
        public int SchemaVersion = 1;
        public string ArtifactType = "advantage-envelope-calibration";
        public string DecisionEngineVersion;
        public string EnvelopeId;
        public string CreatedUtcIso8601;
        public string ScenarioId;
        public int ContractVersion;
        public string CandidateSetHash;
        public string MeasurementSchemaHash;
        public string EnvironmentFingerprint;
        public string CalibrationSettingsHash;
        public string CalibrationSourceArtifactId;
        public string CalibrationSourceArtifactSha256;
        public string EvidenceScope;
        public string CalibrationUncertaintyMethod;
        public AdvantageEnvelopePolicy Policy;
        public bool HoldoutWasRead;
        public EnvelopeCalibrationCellDecision[] Cells;
    }

    [Serializable]
    public sealed class AdvantageEnvelopeHoldoutCellInput
    {
        public AdvantageEnvelopeAxis Axis;
        public DecisionCandidateEvidence Baseline;
        public DecisionCandidateEvidence FrozenCandidate;
    }

    /// <summary>
    /// Holdout contains only tuned AoS plus the already-frozen calibration winner.
    /// It is confirmatory and can never nominate a replacement candidate.
    /// </summary>
    [Serializable]
    public sealed class AdvantageEnvelopeHoldoutRequest
    {
        public int SchemaVersion = 1;
        public string SourceArtifactId;
        public string SourceArtifactSha256;
        public string CandidateSetHash;
        public string MeasurementSchemaHash;
        public string EnvironmentFingerprint;
        public string HoldoutSettingsHash;
        public string EvidenceScope;
        public string HoldoutUncertaintyMethod;
        public AdvantageEnvelopeHoldoutCellInput[] Cells;
    }

    [Serializable]
    public sealed class EnvelopeCellDecision
    {
        public AdvantageEnvelopeAxis Axis;
        public EnvelopeCellStatus Status;
        public string CalibrationPartitionId;
        public string HoldoutPartitionId;
        public string HoldoutBaselineEvidenceHash;
        public string HoldoutCandidateEvidenceHash;
        public string BaselineCandidateId;
        public string BestMeasuredCandidateId;
        public string FrozenCalibrationWinnerCandidateId;
        public string SelectedCandidateId;
        public bool HoldoutConfirmed;
        public double MinimumRequiredImprovementPercent;
        public double CalibrationImprovementPercent;
        public EnvelopeConfidenceInterval CalibrationConfidenceInterval;
        public double HoldoutImprovementPercent;
        public EnvelopeConfidenceInterval HoldoutConfidenceInterval;
        public EnvelopeCandidateOutcome[] CandidateOutcomes;
        public string Reason;
    }

    /// <summary>
    /// A run over explicitly sampled lifetime points. SampledLifetimeTicks makes
    /// clear that unmeasured integer lifetimes are not silently interpolated.
    /// </summary>
    [Serializable]
    public sealed class EnvelopeWinnerRegion
    {
        public int ElementCount;
        public double HotToColdRatio;
        public int WorkerCount;
        public string ExecutionPolicyId;
        public int MinimumSampledLifetimeTicks;
        public int MaximumSampledLifetimeTicks;
        public int[] SampledLifetimeTicks;
        public EnvelopeCellStatus Status;
        public string SelectedCandidateId;
    }

    [Serializable]
    public sealed class AdvantageEnvelopeSummary
    {
        public int TotalCellCount;
        public int ValidCellCount;
        public int CredibleAdvantageCellCount;
        public int StatisticalGreyCellCount;
        public int AoSFallbackCellCount;
        public int HoldoutRejectedCellCount;
        public double CredibleCoveragePercent;
        public double PeakConfirmedImprovementPercent;
        public double MedianConfirmedImprovementPercent;
        public double FloorConfirmedImprovementPercent;
        public double WorstConfirmedConfidenceLowerBoundPercent;
    }

    /// <summary>
    /// Schema-v1 immutable decision artifact intended to be serialized as
    /// advantage-envelope.json. Presentation code must copy decisions from Cells.
    /// </summary>
    [Serializable]
    public sealed class AdvantageEnvelopeProfile
    {
        public int SchemaVersion = 1;
        public string ArtifactType = "advantage-envelope";
        public string DecisionEngineVersion;
        public string EnvelopeId;
        public string CreatedUtcIso8601;
        public string ScenarioId;
        public int ContractVersion;
        public string CandidateSetHash;
        public string MeasurementSchemaHash;
        public string EnvironmentFingerprint;
        public string CalibrationSettingsHash;
        public string HoldoutSettingsHash;
        public string CalibrationSourceArtifactId;
        public string CalibrationSourceArtifactSha256;
        public string HoldoutSourceArtifactId;
        public string HoldoutSourceArtifactSha256;
        public string EvidenceScope;
        public string CalibrationUncertaintyMethod;
        public string HoldoutUncertaintyMethod;
        public AdvantageEnvelopePolicy Policy;
        public bool FinalDecisionLocked;
        public bool HoldoutCanRerank;
        public EnvelopeCellDecision[] Cells;
        public EnvelopeWinnerRegion[] WinnerRegions;
        public AdvantageEnvelopeSummary Summary;
    }
}
