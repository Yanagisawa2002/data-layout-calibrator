using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Yanagisawa.DataLayoutCalibrator
{
    public static class CalibrationProfileFingerprintBuilder
    {
        public static CalibrationProfileFingerprint Create(
            CalibrationProfileFingerprintInput input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            if (input.WorkloadContractVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(input.WorkloadContractVersion));
            if (input.RecordSchemaVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(input.RecordSchemaVersion));
            if (input.LogicalProcessorCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(input.LogicalProcessorCount));
            if (input.JobWorkerCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(input.JobWorkerCount));

            string candidates = ProfileCanonicalization.CanonicalizeSet(
                input.CandidateDefinitions,
                nameof(input.CandidateDefinitions),
                requireEntry: true);
            string buildFlags = ProfileCanonicalization.CanonicalizeSet(
                input.BuildFlags,
                nameof(input.BuildFlags),
                requireEntry: true);
            string settings = ProfileCanonicalization.CanonicalizeSet(
                input.CalibrationSettings,
                nameof(input.CalibrationSettings),
                requireEntry: true);

            var fingerprint = new CalibrationProfileFingerprint
            {
                WorkloadId = ProfileCanonicalization.Required(input.WorkloadId, nameof(input.WorkloadId)),
                WorkloadContractVersion = input.WorkloadContractVersion,
                RecordSchemaId = ProfileCanonicalization.Required(input.RecordSchemaId, nameof(input.RecordSchemaId)),
                RecordSchemaVersion = input.RecordSchemaVersion,
                RecordSchemaHash = ProfileCanonicalization.RequiredSha256(input.RecordSchemaHash, nameof(input.RecordSchemaHash)),
                CandidateSetHash = ProfileCanonicalization.Sha256(candidates),
                UnityVersion = ProfileCanonicalization.Required(input.UnityVersion, nameof(input.UnityVersion)),
                BurstVersion = ProfileCanonicalization.Required(input.BurstVersion, nameof(input.BurstVersion)),
                CollectionsVersion = ProfileCanonicalization.Required(input.CollectionsVersion, nameof(input.CollectionsVersion)),
                MathematicsVersion = ProfileCanonicalization.Required(input.MathematicsVersion, nameof(input.MathematicsVersion)),
                ScriptingBackend = ProfileCanonicalization.Required(input.ScriptingBackend, nameof(input.ScriptingBackend)),
                BuildTarget = ProfileCanonicalization.Required(input.BuildTarget, nameof(input.BuildTarget)),
                Architecture = ProfileCanonicalization.Required(input.Architecture, nameof(input.Architecture)),
                BuildFlagsCanonical = buildFlags,
                OperatingSystem = ProfileCanonicalization.Required(input.OperatingSystem, nameof(input.OperatingSystem)),
                Processor = ProfileCanonicalization.Required(input.Processor, nameof(input.Processor)),
                InstructionSet = ProfileCanonicalization.Required(input.InstructionSet, nameof(input.InstructionSet)),
                LogicalProcessorCount = input.LogicalProcessorCount,
                JobWorkerCount = input.JobWorkerCount,
                BinaryHash = ProfileCanonicalization.RequiredSha256(input.BinaryHash, nameof(input.BinaryHash)),
                CalibrationSettingsCanonical = settings,
                CalibrationSettingsHash = ProfileCanonicalization.Sha256(settings),
            };
            fingerprint.FingerprintSha256 = ComputeSha256(fingerprint);
            return fingerprint;
        }

        public static bool HasValidIntegrity(CalibrationProfileFingerprint fingerprint)
        {
            if (fingerprint == null ||
                fingerprint.SchemaVersion != DeploymentProfileSchema.CurrentFingerprintVersion ||
                !ProfileCanonicalization.IsPresent(fingerprint.WorkloadId) ||
                fingerprint.WorkloadContractVersion <= 0 ||
                !ProfileCanonicalization.IsPresent(fingerprint.RecordSchemaId) ||
                fingerprint.RecordSchemaVersion <= 0 ||
                !ProfileCanonicalization.IsSha256(fingerprint.RecordSchemaHash) ||
                !ProfileCanonicalization.IsSha256(fingerprint.CandidateSetHash) ||
                !ProfileCanonicalization.IsPresent(fingerprint.UnityVersion) ||
                !ProfileCanonicalization.IsPresent(fingerprint.BurstVersion) ||
                !ProfileCanonicalization.IsPresent(fingerprint.CollectionsVersion) ||
                !ProfileCanonicalization.IsPresent(fingerprint.MathematicsVersion) ||
                !ProfileCanonicalization.IsPresent(fingerprint.ScriptingBackend) ||
                !ProfileCanonicalization.IsPresent(fingerprint.BuildTarget) ||
                !ProfileCanonicalization.IsPresent(fingerprint.Architecture) ||
                string.IsNullOrEmpty(fingerprint.BuildFlagsCanonical) ||
                !ProfileCanonicalization.IsPresent(fingerprint.OperatingSystem) ||
                !ProfileCanonicalization.IsPresent(fingerprint.Processor) ||
                !ProfileCanonicalization.IsPresent(fingerprint.InstructionSet) ||
                fingerprint.LogicalProcessorCount <= 0 ||
                fingerprint.JobWorkerCount <= 0 ||
                !ProfileCanonicalization.IsSha256(fingerprint.BinaryHash) ||
                string.IsNullOrEmpty(fingerprint.CalibrationSettingsCanonical) ||
                !ProfileCanonicalization.IsSha256(fingerprint.CalibrationSettingsHash) ||
                !ProfileCanonicalization.IsSha256(fingerprint.FingerprintSha256))
            {
                return false;
            }

            string settingsHash = ProfileCanonicalization.Sha256(
                fingerprint.CalibrationSettingsCanonical ?? string.Empty);
            if (!string.Equals(
                    settingsHash,
                    fingerprint.CalibrationSettingsHash,
                    StringComparison.Ordinal))
            {
                return false;
            }

            return string.Equals(
                ComputeSha256(fingerprint),
                fingerprint.FingerprintSha256,
                StringComparison.Ordinal);
        }

        public static string ComputeSha256(CalibrationProfileFingerprint fingerprint)
        {
            if (fingerprint == null)
                throw new ArgumentNullException(nameof(fingerprint));

            var canonical = new StringBuilder(1024);
            Append(canonical, "fingerprint-schema", fingerprint.SchemaVersion);
            Append(canonical, "workload-id", fingerprint.WorkloadId);
            Append(canonical, "workload-contract", fingerprint.WorkloadContractVersion);
            Append(canonical, "record-schema-id", fingerprint.RecordSchemaId);
            Append(canonical, "record-schema-version", fingerprint.RecordSchemaVersion);
            Append(canonical, "record-schema-hash", fingerprint.RecordSchemaHash);
            Append(canonical, "candidate-set-hash", fingerprint.CandidateSetHash);
            Append(canonical, "unity", fingerprint.UnityVersion);
            Append(canonical, "burst", fingerprint.BurstVersion);
            Append(canonical, "collections", fingerprint.CollectionsVersion);
            Append(canonical, "mathematics", fingerprint.MathematicsVersion);
            Append(canonical, "backend", fingerprint.ScriptingBackend);
            Append(canonical, "build-target", fingerprint.BuildTarget);
            Append(canonical, "architecture", fingerprint.Architecture);
            Append(canonical, "build-flags", fingerprint.BuildFlagsCanonical);
            Append(canonical, "operating-system", fingerprint.OperatingSystem);
            Append(canonical, "processor", fingerprint.Processor);
            Append(canonical, "instruction-set", fingerprint.InstructionSet);
            Append(canonical, "logical-processors", fingerprint.LogicalProcessorCount);
            Append(canonical, "job-workers", fingerprint.JobWorkerCount);
            Append(canonical, "binary-hash", fingerprint.BinaryHash);
            Append(canonical, "calibration-settings", fingerprint.CalibrationSettingsCanonical);
            Append(canonical, "calibration-settings-hash", fingerprint.CalibrationSettingsHash);
            return ProfileCanonicalization.Sha256(canonical.ToString());
        }

        private static void Append(StringBuilder builder, string name, int value)
        {
            Append(builder, name, value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Append(StringBuilder builder, string name, string value)
        {
            value = value ?? string.Empty;
            builder.Append(name.Length)
                .Append(':')
                .Append(name)
                .Append('=')
                .Append(value.Length)
                .Append(':')
                .Append(value)
                .Append('\n');
        }
    }

    /// <summary>
    /// Trusted capture boundary for deployment profiles. The caller must pass a
    /// decision copied from the authoritative ScenarioCalibrationProfile.FinalDecision.
    /// Hashes protect the captured fields from later mutation; they do not
    /// authenticate the caller or prove that an opaque raw suite semantically
    /// produced the supplied decision.
    /// </summary>
    public static class FrozenDeploymentProfileFactory
    {
        public static FrozenDeploymentProfile Create(
            CalibrationProfileFingerprint fingerprint,
            FrozenDeploymentDecision finalDecision,
            string rawSuitePayload,
            DeploymentProfileProvenance provenance)
        {
            if (!CalibrationProfileFingerprintBuilder.HasValidIntegrity(fingerprint))
                throw new ArgumentException("The fingerprint is incomplete or has invalid integrity.", nameof(fingerprint));
            if (finalDecision == null)
                throw new ArgumentNullException(nameof(finalDecision));
            finalDecision.BaselineCandidateId = ProfileCanonicalization.Required(
                finalDecision.BaselineCandidateId,
                nameof(finalDecision.BaselineCandidateId));
            finalDecision.SelectedCandidateId = ProfileCanonicalization.Required(
                finalDecision.SelectedCandidateId,
                nameof(finalDecision.SelectedCandidateId));
            finalDecision.Reason = ProfileCanonicalization.Required(
                finalDecision.Reason,
                nameof(finalDecision.Reason));
            if (!IsSupportedDecisionStatus(finalDecision.Status))
                throw new ArgumentException("The frozen decision status is unsupported.", nameof(finalDecision));
            if (!HasSafeSelectionForStatus(finalDecision))
            {
                throw new ArgumentException(
                    "Every non-Optimized frozen decision must select its baseline candidate.",
                    nameof(finalDecision));
            }
            if (double.IsNaN(finalDecision.ImprovementPercent) ||
                double.IsInfinity(finalDecision.ImprovementPercent))
            {
                throw new ArgumentException("The frozen improvement must be finite.", nameof(finalDecision));
            }

            if (string.IsNullOrWhiteSpace(rawSuitePayload))
                throw new ArgumentException("The raw calibration suite payload is required.", nameof(rawSuitePayload));
            if (provenance == null)
                throw new ArgumentNullException(nameof(provenance));

            provenance.RunId = ProfileCanonicalization.Required(provenance.RunId, nameof(provenance.RunId));
            provenance.CreatedUtcIso8601 = ProfileCanonicalization.Required(
                provenance.CreatedUtcIso8601,
                nameof(provenance.CreatedUtcIso8601));
            provenance.SourceRepository = ProfileCanonicalization.Required(
                provenance.SourceRepository,
                nameof(provenance.SourceRepository));
            provenance.SourceCommit = ProfileCanonicalization.RequiredGitCommit(
                provenance.SourceCommit,
                nameof(provenance.SourceCommit));
            provenance.EvidenceScope = ProfileCanonicalization.Required(
                provenance.EvidenceScope,
                nameof(provenance.EvidenceScope));
            provenance.RawSuiteSha256 = ProfileCanonicalization.Sha256(rawSuitePayload);
            provenance.DecisionSha256 = ComputeDecisionSha256(finalDecision);

            return new FrozenDeploymentProfile
            {
                Fingerprint = fingerprint,
                FinalDecision = finalDecision,
                RawSuitePayload = rawSuitePayload,
                Provenance = provenance,
            };
        }

        public static bool HasValidIntegrity(FrozenDeploymentProfile profile)
        {
            if (profile == null ||
                profile.ProfileSchemaVersion != DeploymentProfileSchema.CurrentProfileVersion ||
                !CalibrationProfileFingerprintBuilder.HasValidIntegrity(profile.Fingerprint) ||
                profile.FinalDecision == null ||
                profile.Provenance == null ||
                string.IsNullOrEmpty(profile.RawSuitePayload) ||
                !ProfileCanonicalization.IsPresent(profile.FinalDecision.BaselineCandidateId) ||
                !ProfileCanonicalization.IsPresent(profile.FinalDecision.SelectedCandidateId) ||
                !IsSupportedDecisionStatus(profile.FinalDecision.Status) ||
                !HasSafeSelectionForStatus(profile.FinalDecision) ||
                double.IsNaN(profile.FinalDecision.ImprovementPercent) ||
                double.IsInfinity(profile.FinalDecision.ImprovementPercent) ||
                !ProfileCanonicalization.IsPresent(profile.FinalDecision.Reason) ||
                !ProfileCanonicalization.IsPresent(profile.Provenance.RunId) ||
                !ProfileCanonicalization.IsPresent(profile.Provenance.CreatedUtcIso8601) ||
                !ProfileCanonicalization.IsPresent(profile.Provenance.SourceRepository) ||
                !ProfileCanonicalization.IsGitCommit(profile.Provenance.SourceCommit) ||
                !ProfileCanonicalization.IsSha256(profile.Provenance.RawSuiteSha256) ||
                !ProfileCanonicalization.IsSha256(profile.Provenance.DecisionSha256) ||
                !ProfileCanonicalization.IsPresent(profile.Provenance.EvidenceScope))
            {
                return false;
            }

            return string.Equals(
                       ProfileCanonicalization.Sha256(profile.RawSuitePayload),
                       profile.Provenance.RawSuiteSha256,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       ComputeDecisionSha256(profile.FinalDecision),
                       profile.Provenance.DecisionSha256,
                       StringComparison.Ordinal);
        }

        public static string ComputeDecisionSha256(FrozenDeploymentDecision decision)
        {
            if (decision == null)
                throw new ArgumentNullException(nameof(decision));

            string canonical =
                ProfileCanonicalization.LengthPrefix(decision.BaselineCandidateId) +
                ProfileCanonicalization.LengthPrefix(decision.SelectedCandidateId) +
                ((int)decision.Status).ToString(CultureInfo.InvariantCulture) + "\n" +
                decision.ImprovementPercent.ToString("R", CultureInfo.InvariantCulture) + "\n" +
                ProfileCanonicalization.LengthPrefix(decision.Reason);
            return ProfileCanonicalization.Sha256(canonical);
        }

        public static bool IsSupportedDecisionStatus(LayoutSelectionStatus status)
        {
            switch (status)
            {
                case LayoutSelectionStatus.Invalid:
                case LayoutSelectionStatus.Inconclusive:
                case LayoutSelectionStatus.Optimized:
                case LayoutSelectionStatus.StatisticalTie:
                case LayoutSelectionStatus.Regression:
                    return true;
                default:
                    return false;
            }
        }

        private static bool HasSafeSelectionForStatus(FrozenDeploymentDecision decision)
        {
            return decision.Status == LayoutSelectionStatus.Optimized ||
                   string.Equals(
                       decision.SelectedCandidateId,
                       decision.BaselineCandidateId,
                       StringComparison.Ordinal);
        }
    }

    internal static class ProfileCanonicalization
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty explicit value is required.", parameterName);
            return value.Trim();
        }

        internal static bool IsPresent(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   string.Equals(value, value.Trim(), StringComparison.Ordinal);
        }

        internal static string RequiredSha256(string value, string parameterName)
        {
            string normalized = Required(value, parameterName).ToUpperInvariant();
            if (!IsSha256(normalized))
                throw new ArgumentException("A 64-character hexadecimal SHA-256 is required.", parameterName);
            return normalized;
        }

        internal static string RequiredGitCommit(string value, string parameterName)
        {
            string normalized = Required(value, parameterName).ToLowerInvariant();
            if (!IsGitCommit(normalized))
                throw new ArgumentException("A 40- or 64-character hexadecimal Git commit is required.", parameterName);
            return normalized;
        }

        internal static bool IsGitCommit(string value)
        {
            if (value == null || (value.Length != 40 && value.Length != 64))
                return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool digit = character >= '0' && character <= '9';
                bool lower = character >= 'a' && character <= 'f';
                if (!digit && !lower)
                    return false;
            }

            return true;
        }

        internal static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64)
                return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool digit = character >= '0' && character <= '9';
                bool upper = character >= 'A' && character <= 'F';
                if (!digit && !upper)
                    return false;
            }

            return true;
        }

        internal static string CanonicalizeSet(
            string[] values,
            string parameterName,
            bool requireEntry)
        {
            if (values == null)
                throw new ArgumentNullException(parameterName);
            if (requireEntry && values.Length == 0)
                throw new ArgumentException("At least one entry is required.", parameterName);

            var normalized = new List<string>(values.Length);
            for (int index = 0; index < values.Length; index++)
                normalized.Add(Required(values[index], parameterName));
            normalized.Sort(StringComparer.Ordinal);
            for (int index = 1; index < normalized.Count; index++)
            {
                if (string.Equals(normalized[index - 1], normalized[index], StringComparison.Ordinal))
                    throw new ArgumentException("Duplicate canonical entries are not allowed.", parameterName);
            }

            var canonical = new StringBuilder();
            for (int index = 0; index < normalized.Count; index++)
                canonical.Append(LengthPrefix(normalized[index]));
            return canonical.ToString();
        }

        internal static string LengthPrefix(string value)
        {
            value = value ?? string.Empty;
            return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value + "\n";
        }

        internal static string Sha256(string value)
        {
            byte[] bytes = StrictUtf8.GetBytes(value ?? string.Empty);
            byte[] digest;
            using (SHA256 algorithm = SHA256.Create())
                digest = algorithm.ComputeHash(bytes);

            var hex = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; index++)
                hex.Append(digest[index].ToString("X2", CultureInfo.InvariantCulture));
            return hex.ToString();
        }
    }
}
