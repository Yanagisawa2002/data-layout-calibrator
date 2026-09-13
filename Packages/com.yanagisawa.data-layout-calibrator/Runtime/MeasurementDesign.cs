using System;

namespace Yanagisawa.DataLayoutCalibrator
{
    [Serializable]
    public sealed class BlockedMeasurementOrder
    {
        public int SchemaVersion = 1;
        public MeasurementOrderKind Kind;
        public int CandidateCount;
        public int BlockCount;
        public uint Seed;
        public int[] CandidateIndices;

        public int GetCandidateIndex(int block, int position)
        {
            if ((uint)block >= (uint)BlockCount)
                throw new ArgumentOutOfRangeException(nameof(block));
            if ((uint)position >= (uint)CandidateCount)
                throw new ArgumentOutOfRangeException(nameof(position));
            return CandidateIndices[(block * CandidateCount) + position];
        }
    }

    /// <summary>
    /// Creates deterministic complete blocks. Every block measures every candidate
    /// once; the balanced Latin-square option also balances order position over each
    /// complete candidate-count cycle. With two candidates its first two blocks are
    /// AB then BA, producing the usual ABBA sequence across the block boundary.
    /// </summary>
    public static class MeasurementOrder
    {
        public static BlockedMeasurementOrder Create(
            int candidateCount,
            int blockCount,
            uint seed,
            MeasurementOrderKind kind = MeasurementOrderKind.BalancedLatinSquare)
        {
            if (candidateCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(candidateCount));
            if (blockCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(blockCount));
            if (kind != MeasurementOrderKind.RandomizedBlocked &&
                kind != MeasurementOrderKind.BalancedLatinSquare)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            uint randomState = NonZero(seed);
            int[] labels = CreateIdentity(candidateCount);
            Shuffle(labels, ref randomState);
            var flattened = new int[candidateCount * blockCount];
            var row = new int[candidateCount];

            for (int block = 0; block < blockCount; block++)
            {
                if (kind == MeasurementOrderKind.RandomizedBlocked)
                {
                    Array.Copy(labels, row, candidateCount);
                    Shuffle(row, ref randomState);
                }
                else
                {
                    FillBalancedLatinRow(labels, block, row);
                }

                Array.Copy(row, 0, flattened, block * candidateCount, candidateCount);
            }

            return new BlockedMeasurementOrder
            {
                Kind = kind,
                CandidateCount = candidateCount,
                BlockCount = blockCount,
                Seed = seed,
                CandidateIndices = flattened,
            };
        }

        private static void FillBalancedLatinRow(int[] labels, int block, int[] destination)
        {
            int count = labels.Length;
            int latinRow = block % count;
            bool reverse = (count & 1) != 0 && ((block / count) & 1) != 0;
            for (int position = 0; position < count; position++)
            {
                int sequencePosition = reverse ? count - 1 - position : position;
                int offset;
                if (sequencePosition == 0)
                    offset = 0;
                else if ((sequencePosition & 1) != 0)
                    offset = (sequencePosition + 1) / 2;
                else
                    offset = count - (sequencePosition / 2);

                destination[position] = labels[(latinRow + offset) % count];
            }
        }

        private static int[] CreateIdentity(int count)
        {
            var values = new int[count];
            for (int index = 0; index < count; index++)
                values[index] = index;
            return values;
        }

        private static void Shuffle(int[] values, ref uint state)
        {
            for (int index = values.Length - 1; index > 0; index--)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                int swapIndex = (int)(state % (uint)(index + 1));
                int temporary = values[index];
                values[index] = values[swapIndex];
                values[swapIndex] = temporary;
            }
        }

        private static uint NonZero(uint state) => state == 0u ? 0xA341316Cu : state;
    }

    public static class HoldoutIsolation
    {
        public static CandidateDescriptor[] Freeze(LayoutSelectionDecision calibrationDecision)
        {
            if (calibrationDecision.DecisionStage != DecisionStage.Calibration)
            {
                throw new ArgumentException(
                    "Only a calibration-stage decision can create a holdout plan.",
                    nameof(calibrationDecision));
            }
            if (calibrationDecision.Status != LayoutSelectionStatus.Optimized)
            {
                throw new ArgumentException(
                    "Only an optimized calibration decision requires holdout confirmation.",
                    nameof(calibrationDecision));
            }

            CandidateDescriptor baseline = calibrationDecision.BaselineCandidate.NormalizePolicies();
            CandidateDescriptor selected = calibrationDecision.SelectedCandidate.NormalizePolicies();
            baseline.ValidateFactorConsistency();
            selected.ValidateFactorConsistency();
            if (string.IsNullOrWhiteSpace(baseline.CandidateId) || !baseline.IsBaseline)
                throw new ArgumentException("The frozen holdout baseline is not a valid baseline candidate.");
            if (string.IsNullOrWhiteSpace(selected.CandidateId) ||
                selected.IsBaseline ||
                string.Equals(baseline.CandidateId, selected.CandidateId, StringComparison.Ordinal))
                throw new ArgumentException("The frozen holdout selection must be a distinct non-baseline candidate.");

            return new[] { baseline, selected };
        }
    }
}
