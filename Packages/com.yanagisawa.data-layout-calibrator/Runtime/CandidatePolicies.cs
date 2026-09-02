using System;

namespace Yanagisawa.DataLayoutCalibrator
{
    public enum KernelControlFlow
    {
        Unspecified = 0,
        Branched = 1,
        Branchless = 2,
    }

    public enum ExecutionTopology
    {
        FrameFaithful = 0,
        DependencyChain = 1,
        TemporalBlock = 2,
    }

    public enum MeasurementOrderKind
    {
        RandomizedBlocked = 0,
        BalancedLatinSquare = 1,
    }

    public enum EvidenceScope
    {
        SinglePlayer = 0,
        MultipleProcessesSingleDevice = 1,
        MultipleDevices = 2,
    }

    public enum DecisionStage
    {
        Calibration = 0,
        HoldoutConfirmation = 1,
    }

    [Serializable]
    public struct LayoutPolicy : IEquatable<LayoutPolicy>
    {
        public string PolicyId;
        public int BlockWidth;
        public int AlignmentBytes;
        public int PaddingBytes;

        public LayoutPolicy(
            string policyId,
            int blockWidth = 1,
            int alignmentBytes = 0,
            int paddingBytes = 0)
        {
            if (string.IsNullOrWhiteSpace(policyId))
                throw new ArgumentException("A layout policy ID is required.", nameof(policyId));
            if (blockWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(blockWidth));
            if (alignmentBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(alignmentBytes));
            if (paddingBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(paddingBytes));

            PolicyId = policyId;
            BlockWidth = blockWidth;
            AlignmentBytes = alignmentBytes;
            PaddingBytes = paddingBytes;
        }

        public bool IsSpecified => !string.IsNullOrWhiteSpace(PolicyId);

        public static LayoutPolicy FromLegacy(string layoutId)
        {
            if (string.IsNullOrWhiteSpace(layoutId))
                return default;

            int blockWidth = 1;
            const string prefix = "AoSoA";
            if (layoutId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(layoutId.Substring(prefix.Length), out blockWidth);
                if (blockWidth <= 0)
                    blockWidth = 1;
            }

            return new LayoutPolicy(layoutId, blockWidth);
        }

        public bool Equals(LayoutPolicy other)
        {
            return string.Equals(PolicyId, other.PolicyId, StringComparison.Ordinal) &&
                   BlockWidth == other.BlockWidth &&
                   AlignmentBytes == other.AlignmentBytes &&
                   PaddingBytes == other.PaddingBytes;
        }

        public override bool Equals(object obj) => obj is LayoutPolicy other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = PolicyId == null ? 0 : PolicyId.GetHashCode();
                hash = (hash * 397) ^ BlockWidth;
                hash = (hash * 397) ^ AlignmentBytes;
                return (hash * 397) ^ PaddingBytes;
            }
        }
    }

    [Serializable]
    public struct KernelPolicy : IEquatable<KernelPolicy>
    {
        public string PolicyId;
        public KernelControlFlow ControlFlow;
        public int VectorWidth;

        public KernelPolicy(
            string policyId,
            KernelControlFlow controlFlow,
            int vectorWidth = 1)
        {
            if (string.IsNullOrWhiteSpace(policyId))
                throw new ArgumentException("A kernel policy ID is required.", nameof(policyId));
            if (vectorWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(vectorWidth));

            PolicyId = policyId;
            ControlFlow = controlFlow;
            VectorWidth = vectorWidth;
        }

        public bool IsSpecified => !string.IsNullOrWhiteSpace(PolicyId);

        public static KernelPolicy LegacyUnspecified =>
            new KernelPolicy("LegacyUnspecified", KernelControlFlow.Unspecified);

        public bool Equals(KernelPolicy other)
        {
            return string.Equals(PolicyId, other.PolicyId, StringComparison.Ordinal) &&
                   ControlFlow == other.ControlFlow &&
                   VectorWidth == other.VectorWidth;
        }

        public override bool Equals(object obj) => obj is KernelPolicy other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = PolicyId == null ? 0 : PolicyId.GetHashCode();
                hash = (hash * 397) ^ (int)ControlFlow;
                return (hash * 397) ^ VectorWidth;
            }
        }
    }

    [Serializable]
    public struct BatchPolicy : IEquatable<BatchPolicy>
    {
        public string PolicyId;
        public int LogicalBatchSize;

        public BatchPolicy(string policyId, int logicalBatchSize)
        {
            if (string.IsNullOrWhiteSpace(policyId))
                throw new ArgumentException("A batch policy ID is required.", nameof(policyId));
            if (logicalBatchSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(logicalBatchSize));

            PolicyId = policyId;
            LogicalBatchSize = logicalBatchSize;
        }

        public bool IsSpecified => !string.IsNullOrWhiteSpace(PolicyId);

        public static BatchPolicy JobBatch(int logicalBatchSize) =>
            new BatchPolicy("JobBatch", logicalBatchSize);

        public bool Equals(BatchPolicy other)
        {
            return string.Equals(PolicyId, other.PolicyId, StringComparison.Ordinal) &&
                   LogicalBatchSize == other.LogicalBatchSize;
        }

        public override bool Equals(object obj) => obj is BatchPolicy other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((PolicyId == null ? 0 : PolicyId.GetHashCode()) * 397) ^ LogicalBatchSize;
            }
        }
    }

    [Serializable]
    public struct ExecutionPolicy : IEquatable<ExecutionPolicy>
    {
        public string PolicyId;
        public ExecutionTopology Topology;
        public int TemporalBlockTicks;
        public bool SemanticsPermitReordering;

        public ExecutionPolicy(
            string policyId,
            ExecutionTopology topology,
            int temporalBlockTicks = 1,
            bool semanticsPermitReordering = false)
        {
            if (string.IsNullOrWhiteSpace(policyId))
                throw new ArgumentException("An execution policy ID is required.", nameof(policyId));
            if (temporalBlockTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(temporalBlockTicks));
            if (topology == ExecutionTopology.TemporalBlock &&
                (temporalBlockTicks <= 1 || !semanticsPermitReordering))
            {
                throw new ArgumentException(
                    "TemporalBlock requires more than one tick and an explicit semantic reordering declaration.",
                    nameof(semanticsPermitReordering));
            }
            if (topology != ExecutionTopology.TemporalBlock && temporalBlockTicks != 1)
            {
                throw new ArgumentException(
                    "Only TemporalBlock may declare more than one temporal block tick.",
                    nameof(temporalBlockTicks));
            }

            PolicyId = policyId;
            Topology = topology;
            TemporalBlockTicks = temporalBlockTicks;
            SemanticsPermitReordering = semanticsPermitReordering;
        }

        public bool IsSpecified => !string.IsNullOrWhiteSpace(PolicyId);

        public static ExecutionPolicy FrameFaithful =>
            new ExecutionPolicy("FrameFaithful", ExecutionTopology.FrameFaithful);

        public static ExecutionPolicy DependencyChain =>
            new ExecutionPolicy("DependencyChain", ExecutionTopology.DependencyChain);

        public static ExecutionPolicy TemporalBlock(int ticks, bool semanticsPermitReordering)
        {
            return new ExecutionPolicy(
                $"TemporalBlock{ticks}",
                ExecutionTopology.TemporalBlock,
                ticks,
                semanticsPermitReordering);
        }

        public bool Equals(ExecutionPolicy other)
        {
            return string.Equals(PolicyId, other.PolicyId, StringComparison.Ordinal) &&
                   Topology == other.Topology &&
                   TemporalBlockTicks == other.TemporalBlockTicks &&
                   SemanticsPermitReordering == other.SemanticsPermitReordering;
        }

        public override bool Equals(object obj) => obj is ExecutionPolicy other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = PolicyId == null ? 0 : PolicyId.GetHashCode();
                hash = (hash * 397) ^ (int)Topology;
                hash = (hash * 397) ^ TemporalBlockTicks;
                return (hash * 397) ^ (SemanticsPermitReordering ? 1 : 0);
            }
        }
    }
}
