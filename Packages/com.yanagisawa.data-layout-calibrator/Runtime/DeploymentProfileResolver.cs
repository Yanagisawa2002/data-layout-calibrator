using System;
using System.Collections.Generic;

namespace Yanagisawa.DataLayoutCalibrator
{
    public static class DeploymentProfileResolver
    {
        private const ProfileInvalidationReason NeverCompatible =
            ProfileInvalidationReason.ProfileSchemaVersion |
            ProfileInvalidationReason.FingerprintSchemaVersion |
            ProfileInvalidationReason.Workload |
            ProfileInvalidationReason.WorkloadContractVersion |
            ProfileInvalidationReason.RecordSchemaId |
            ProfileInvalidationReason.RecordSchemaVersion |
            ProfileInvalidationReason.RecordSchemaHash |
            ProfileInvalidationReason.CandidateSet |
            ProfileInvalidationReason.ScriptingBackend |
            ProfileInvalidationReason.BuildTarget |
            ProfileInvalidationReason.Architecture |
            ProfileInvalidationReason.BuildFlags |
            ProfileInvalidationReason.OperatingSystem |
            ProfileInvalidationReason.Processor |
            ProfileInvalidationReason.InstructionSet |
            ProfileInvalidationReason.LogicalProcessorCount |
            ProfileInvalidationReason.JobWorkerCount |
            ProfileInvalidationReason.BinaryHash |
            ProfileInvalidationReason.CalibrationSettings |
            ProfileInvalidationReason.FingerprintIntegrity |
            ProfileInvalidationReason.RawSuiteIntegrity |
            ProfileInvalidationReason.DecisionIntegrity |
            ProfileInvalidationReason.BaselineCandidate |
            ProfileInvalidationReason.SelectedCandidateUnavailable;

        public static ProfileResolution Resolve(
            CalibrationProfileFingerprint expected,
            ProfileDocumentLoadResult loaded,
            string tunedAoSBaselineCandidateId,
            IReadOnlyList<string> currentCandidateIds,
            ProfileResolverOptions options = null)
        {
            if (!CalibrationProfileFingerprintBuilder.HasValidIntegrity(expected))
                throw new ArgumentException("The expected fingerprint failed integrity validation.", nameof(expected));
            if (loaded == null)
                throw new ArgumentNullException(nameof(loaded));

            string baseline = ProfileCanonicalization.Required(
                tunedAoSBaselineCandidateId,
                nameof(tunedAoSBaselineCandidateId));
            var candidateSet = BuildCandidateSet(currentCandidateIds);
            if (!candidateSet.Contains(baseline))
            {
                throw new ArgumentException(
                    "The tuned AoS fallback must be present in the current candidate set.",
                    nameof(tunedAoSBaselineCandidateId));
            }
            options = options ?? ProfileResolverOptions.ExactOnly();

            switch (loaded.Status)
            {
                case ProfileDocumentLoadStatus.Missing:
                    return Fallback(
                        ProfileResolutionStatus.MissingProfile,
                        baseline,
                        ProfileInvalidationReason.Missing,
                        loaded.Diagnostic,
                        null);
                case ProfileDocumentLoadStatus.Corrupt:
                    return Fallback(
                        ProfileResolutionStatus.CorruptProfile,
                        baseline,
                        ProfileInvalidationReason.Corrupt,
                        loaded.Diagnostic,
                        null);
                case ProfileDocumentLoadStatus.StorageError:
                    return Fallback(
                        ProfileResolutionStatus.StorageError,
                        baseline,
                        ProfileInvalidationReason.StorageFailure,
                        loaded.Diagnostic,
                        null);
                case ProfileDocumentLoadStatus.UnsupportedSchema:
                    return Fallback(
                        ProfileResolutionStatus.UnsupportedMatch,
                        baseline,
                        ProfileInvalidationReason.ProfileSchemaVersion,
                        loaded.Diagnostic,
                        null);
                case ProfileDocumentLoadStatus.Loaded:
                    break;
                default:
                    return Fallback(
                        ProfileResolutionStatus.UnsupportedMatch,
                        baseline,
                        ProfileInvalidationReason.ProfileSchemaVersion,
                        "The profile load status is unknown.",
                        null);
            }

            FrozenDeploymentProfile profile = loaded.Profile;
            ProfileInvalidationReason reasons = Compare(expected, profile);
            if (profile != null && profile.FinalDecision != null)
            {
                if (!string.Equals(
                        profile.FinalDecision.BaselineCandidateId,
                        baseline,
                        StringComparison.Ordinal))
                {
                    reasons |= ProfileInvalidationReason.BaselineCandidate;
                }

                if (!candidateSet.Contains(profile.FinalDecision.SelectedCandidateId))
                    reasons |= ProfileInvalidationReason.SelectedCandidateUnavailable;
            }

            if (reasons == ProfileInvalidationReason.None)
            {
                return new ProfileResolution
                {
                    Status = ProfileResolutionStatus.ExactMatch,
                    CandidateId = profile.FinalDecision.SelectedCandidateId,
                    BaselineCandidateId = baseline,
                    UsedAoSFallback = false,
                    InvalidationReasons = ProfileInvalidationReason.None,
                    Diagnostic = "The frozen decision exactly matches the current environment and binary fingerprint.",
                    Profile = profile,
                };
            }

            ExplicitProfileCompatibilityRule compatibleRule = FindCompatibleRule(
                expected,
                profile,
                reasons,
                options);
            if (compatibleRule != null)
            {
                return new ProfileResolution
                {
                    Status = ProfileResolutionStatus.CompatibleMatch,
                    CandidateId = profile.FinalDecision.SelectedCandidateId,
                    BaselineCandidateId = baseline,
                    UsedAoSFallback = false,
                    InvalidationReasons = reasons,
                    CompatibilityRuleId = compatibleRule.RuleId,
                    Diagnostic = "A precise fingerprint-pair compatibility rule authorized the frozen decision: " +
                                 compatibleRule.EvidenceReference,
                    Profile = profile,
                };
            }

            reasons |= ProfileInvalidationReason.CompatibilityNotAuthorized;
            return Fallback(
                ProfileResolutionStatus.UnsupportedMatch,
                baseline,
                reasons,
                "The cached decision is incompatible with the current fingerprint; tuned AoS is required. " + reasons,
                profile);
        }

        public static ProfileInvalidationReason Compare(
            CalibrationProfileFingerprint expected,
            FrozenDeploymentProfile storedProfile)
        {
            if (storedProfile == null)
            {
                return ProfileInvalidationReason.Corrupt |
                       ProfileInvalidationReason.FingerprintIntegrity |
                       ProfileInvalidationReason.RawSuiteIntegrity |
                       ProfileInvalidationReason.DecisionIntegrity;
            }

            ProfileInvalidationReason reasons = ProfileInvalidationReason.None;
            if (storedProfile.ProfileSchemaVersion != DeploymentProfileSchema.CurrentProfileVersion)
                reasons |= ProfileInvalidationReason.ProfileSchemaVersion;
            if (!FrozenDeploymentProfileFactory.HasValidIntegrity(storedProfile))
            {
                if (!CalibrationProfileFingerprintBuilder.HasValidIntegrity(storedProfile.Fingerprint))
                    reasons |= ProfileInvalidationReason.FingerprintIntegrity;
                if (storedProfile.Provenance == null ||
                    !string.Equals(
                        ProfileCanonicalization.Sha256(storedProfile.RawSuitePayload ?? string.Empty),
                        storedProfile.Provenance.RawSuiteSha256,
                        StringComparison.Ordinal))
                {
                    reasons |= ProfileInvalidationReason.RawSuiteIntegrity;
                }

                if (storedProfile.FinalDecision == null ||
                    storedProfile.Provenance == null ||
                    !string.Equals(
                        storedProfile.FinalDecision == null
                            ? string.Empty
                            : FrozenDeploymentProfileFactory.ComputeDecisionSha256(storedProfile.FinalDecision),
                        storedProfile.Provenance == null
                            ? string.Empty
                            : storedProfile.Provenance.DecisionSha256,
                        StringComparison.Ordinal))
                {
                    reasons |= ProfileInvalidationReason.DecisionIntegrity;
                }
            }

            CalibrationProfileFingerprint stored = storedProfile.Fingerprint;
            if (stored == null)
                return reasons | ProfileInvalidationReason.FingerprintIntegrity;

            Difference(ref reasons, ProfileInvalidationReason.FingerprintSchemaVersion, expected.SchemaVersion, stored.SchemaVersion);
            Difference(ref reasons, ProfileInvalidationReason.Workload, expected.WorkloadId, stored.WorkloadId);
            Difference(
                ref reasons,
                ProfileInvalidationReason.WorkloadContractVersion,
                expected.WorkloadContractVersion,
                stored.WorkloadContractVersion);
            Difference(ref reasons, ProfileInvalidationReason.RecordSchemaId, expected.RecordSchemaId, stored.RecordSchemaId);
            Difference(
                ref reasons,
                ProfileInvalidationReason.RecordSchemaVersion,
                expected.RecordSchemaVersion,
                stored.RecordSchemaVersion);
            Difference(ref reasons, ProfileInvalidationReason.RecordSchemaHash, expected.RecordSchemaHash, stored.RecordSchemaHash);
            Difference(ref reasons, ProfileInvalidationReason.CandidateSet, expected.CandidateSetHash, stored.CandidateSetHash);
            Difference(ref reasons, ProfileInvalidationReason.UnityVersion, expected.UnityVersion, stored.UnityVersion);
            Difference(ref reasons, ProfileInvalidationReason.BurstVersion, expected.BurstVersion, stored.BurstVersion);
            Difference(ref reasons, ProfileInvalidationReason.CollectionsVersion, expected.CollectionsVersion, stored.CollectionsVersion);
            Difference(ref reasons, ProfileInvalidationReason.MathematicsVersion, expected.MathematicsVersion, stored.MathematicsVersion);
            Difference(ref reasons, ProfileInvalidationReason.ScriptingBackend, expected.ScriptingBackend, stored.ScriptingBackend);
            Difference(ref reasons, ProfileInvalidationReason.BuildTarget, expected.BuildTarget, stored.BuildTarget);
            Difference(ref reasons, ProfileInvalidationReason.Architecture, expected.Architecture, stored.Architecture);
            Difference(ref reasons, ProfileInvalidationReason.BuildFlags, expected.BuildFlagsCanonical, stored.BuildFlagsCanonical);
            Difference(ref reasons, ProfileInvalidationReason.OperatingSystem, expected.OperatingSystem, stored.OperatingSystem);
            Difference(ref reasons, ProfileInvalidationReason.Processor, expected.Processor, stored.Processor);
            Difference(ref reasons, ProfileInvalidationReason.InstructionSet, expected.InstructionSet, stored.InstructionSet);
            Difference(
                ref reasons,
                ProfileInvalidationReason.LogicalProcessorCount,
                expected.LogicalProcessorCount,
                stored.LogicalProcessorCount);
            Difference(ref reasons, ProfileInvalidationReason.JobWorkerCount, expected.JobWorkerCount, stored.JobWorkerCount);
            Difference(ref reasons, ProfileInvalidationReason.BinaryHash, expected.BinaryHash, stored.BinaryHash);
            Difference(
                ref reasons,
                ProfileInvalidationReason.CalibrationSettings,
                expected.CalibrationSettingsHash,
                stored.CalibrationSettingsHash);
            return reasons;
        }

        private static HashSet<string> BuildCandidateSet(IReadOnlyList<string> candidateIds)
        {
            if (candidateIds == null || candidateIds.Count == 0)
                throw new ArgumentException("At least one current candidate ID is required.", nameof(candidateIds));
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < candidateIds.Count; index++)
            {
                string candidateId = ProfileCanonicalization.Required(candidateIds[index], nameof(candidateIds));
                if (!candidates.Add(candidateId))
                    throw new ArgumentException("Current candidate IDs must be unique.", nameof(candidateIds));
            }

            return candidates;
        }

        private static ExplicitProfileCompatibilityRule FindCompatibleRule(
            CalibrationProfileFingerprint expected,
            FrozenDeploymentProfile profile,
            ProfileInvalidationReason reasons,
            ProfileResolverOptions options)
        {
            if (!options.AllowCompatibleMatches ||
                options.CompatibilityRules == null ||
                profile == null ||
                profile.Fingerprint == null ||
                (reasons & NeverCompatible) != 0)
            {
                return null;
            }

            for (int index = 0; index < options.CompatibilityRules.Length; index++)
            {
                ExplicitProfileCompatibilityRule rule = options.CompatibilityRules[index];
                if (rule == null ||
                    string.IsNullOrWhiteSpace(rule.RuleId) ||
                    string.IsNullOrWhiteSpace(rule.EvidenceReference))
                {
                    continue;
                }

                if (!string.Equals(
                        rule.ExpectedFingerprintSha256,
                        expected.FingerprintSha256,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        rule.StoredFingerprintSha256,
                        profile.Fingerprint.FingerprintSha256,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if ((rule.AllowedDifferences & NeverCompatible) != 0)
                    continue;
                if ((reasons & ~rule.AllowedDifferences) != 0)
                    continue;
                return rule;
            }

            return null;
        }

        private static ProfileResolution Fallback(
            ProfileResolutionStatus status,
            string baseline,
            ProfileInvalidationReason reasons,
            string diagnostic,
            FrozenDeploymentProfile profile)
        {
            return new ProfileResolution
            {
                Status = status,
                CandidateId = baseline,
                BaselineCandidateId = baseline,
                UsedAoSFallback = true,
                InvalidationReasons = reasons,
                Diagnostic = diagnostic ?? "The frozen profile is unavailable; tuned AoS is required.",
                Profile = profile,
            };
        }

        private static void Difference(
            ref ProfileInvalidationReason reasons,
            ProfileInvalidationReason reason,
            string expected,
            string stored)
        {
            if (!string.Equals(expected, stored, StringComparison.Ordinal))
                reasons |= reason;
        }

        private static void Difference(
            ref ProfileInvalidationReason reasons,
            ProfileInvalidationReason reason,
            int expected,
            int stored)
        {
            if (expected != stored)
                reasons |= reason;
        }
    }
}
