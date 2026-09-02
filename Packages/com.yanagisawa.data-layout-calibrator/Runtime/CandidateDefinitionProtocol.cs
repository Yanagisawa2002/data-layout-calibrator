using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>
    /// Canonical schema-v1 encoding for the complete scientific candidate
    /// definition. DisplayName is deliberately excluded because it is presentation
    /// metadata; every field that can change execution or stable ordering is bound.
    /// </summary>
    public static class CandidateDefinitionProtocol
    {
        public const int CurrentSchemaVersion = 1;
        public const string SchemaIdentifier = "dlc.candidate-definition.v1";

        public static byte[] EncodeCanonical(CandidateDescriptor candidate)
        {
            CandidateDescriptor normalized = candidate.NormalizePolicies();
            normalized.ValidateFactorConsistency();

            var canonical = new StringBuilder(384);
            AppendString(canonical, SchemaIdentifier);
            AppendInteger(canonical, CurrentSchemaVersion);
            AppendInteger(canonical, normalized.PolicySchemaVersion);
            AppendString(canonical, normalized.CandidateId);
            AppendString(canonical, normalized.LayoutId);
            AppendInteger(canonical, normalized.LogicalBatchSize);
            AppendBoolean(canonical, normalized.IsBaseline);
            AppendInteger(canonical, normalized.SortOrder);

            AppendString(canonical, normalized.Layout.PolicyId);
            AppendInteger(canonical, normalized.Layout.BlockWidth);
            AppendInteger(canonical, normalized.Layout.AlignmentBytes);
            AppendInteger(canonical, normalized.Layout.PaddingBytes);

            AppendString(canonical, normalized.Kernel.PolicyId);
            AppendInteger(canonical, (int)normalized.Kernel.ControlFlow);
            AppendInteger(canonical, normalized.Kernel.VectorWidth);

            AppendString(canonical, normalized.Batch.PolicyId);
            AppendInteger(canonical, normalized.Batch.LogicalBatchSize);

            AppendString(canonical, normalized.Execution.PolicyId);
            AppendInteger(canonical, (int)normalized.Execution.Topology);
            AppendInteger(canonical, normalized.Execution.TemporalBlockTicks);
            AppendBoolean(canonical, normalized.Execution.SemanticsPermitReordering);
            return new UTF8Encoding(false, true).GetBytes(canonical.ToString());
        }

        public static string ComputeCandidateDefinitionSha256(CandidateDescriptor candidate)
        {
            return ComputeSha256(EncodeCanonical(candidate));
        }

        /// <summary>
        /// Hashes a set independently of input order. Duplicate CandidateId values
        /// are rejected even when their definitions happen to match.
        /// </summary>
        public static string ComputeCandidateSetSha256(CandidateDescriptor[] candidates)
        {
            if (candidates == null || candidates.Length == 0)
                throw new ArgumentException("At least one candidate definition is required.", nameof(candidates));

            var normalized = new CandidateDescriptor[candidates.Length];
            for (int index = 0; index < candidates.Length; index++)
            {
                normalized[index] = candidates[index].NormalizePolicies();
                normalized[index].ValidateFactorConsistency();
            }
            Array.Sort(normalized, CompareCandidateId);

            var canonical = new StringBuilder(128 + (normalized.Length * 384));
            AppendString(canonical, "dlc.candidate-set.v1");
            AppendInteger(canonical, CurrentSchemaVersion);
            AppendInteger(canonical, normalized.Length);
            for (int index = 0; index < normalized.Length; index++)
            {
                if (index > 0 && string.Equals(
                        normalized[index - 1].CandidateId,
                        normalized[index].CandidateId,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Duplicate canonical candidate identity '{normalized[index].CandidateId}'.",
                        nameof(candidates));
                }

                byte[] definition = EncodeCanonical(normalized[index]);
                AppendInteger(canonical, definition.Length);
                canonical.Append(Encoding.UTF8.GetString(definition));
            }

            return ComputeSha256(new UTF8Encoding(false, true).GetBytes(canonical.ToString()));
        }

        public static EnvelopeCandidateDescriptor ToEnvelopeCandidate(
            CandidateDescriptor candidate)
        {
            CandidateDescriptor normalized = candidate.NormalizePolicies();
            normalized.ValidateFactorConsistency();
            return new EnvelopeCandidateDescriptor(
                normalized.CandidateId,
                ComputeCandidateDefinitionSha256(normalized),
                normalized.Layout.PolicyId,
                normalized.Kernel.PolicyId,
                normalized.Batch.PolicyId,
                normalized.Execution.PolicyId,
                normalized.LogicalBatchSize,
                normalized.IsBaseline,
                normalized.SortOrder,
                normalized.DisplayName);
        }

        public static bool IsCanonicalSha256(string value)
        {
            if (value == null || value.Length != 64)
                return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'A' && character <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }

        internal static string ComputeSha256Utf8(string canonicalText)
        {
            if (canonicalText == null)
                throw new ArgumentNullException(nameof(canonicalText));
            return ComputeSha256(new UTF8Encoding(false, true).GetBytes(canonicalText));
        }

        private static int CompareCandidateId(CandidateDescriptor left, CandidateDescriptor right)
        {
            return string.Compare(left.CandidateId, right.CandidateId, StringComparison.Ordinal);
        }

        private static void AppendString(StringBuilder target, string value)
        {
            if (value == null)
                throw new ArgumentException("Canonical candidate strings must not be null.");
            int byteCount = Encoding.UTF8.GetByteCount(value);
            target.Append(byteCount.ToString(CultureInfo.InvariantCulture));
            target.Append(':');
            target.Append(value);
            target.Append('\n');
        }

        private static void AppendInteger(StringBuilder target, int value)
        {
            target.Append(value.ToString(CultureInfo.InvariantCulture));
            target.Append('\n');
        }

        private static void AppendBoolean(StringBuilder target, bool value)
        {
            target.Append(value ? "1\n" : "0\n");
        }

        private static string ComputeSha256(byte[] bytes)
        {
            byte[] digest;
            using (SHA256 sha256 = SHA256.Create())
                digest = sha256.ComputeHash(bytes);

            var hexadecimal = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; index++)
                hexadecimal.Append(digest[index].ToString("X2", CultureInfo.InvariantCulture));
            return hexadecimal.ToString();
        }
    }
}
