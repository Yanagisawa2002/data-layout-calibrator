using System;

namespace Yanagisawa.DataLayoutCalibrator
{
    public static class DeploymentProfileSchema
    {
        public const int CurrentProfileVersion = 2;
        public const int MinimumMigratableProfileVersion = 1;
        public const int CurrentFingerprintVersion = 1;
    }

    /// <summary>
    /// Every value is supplied explicitly by the Player host. The core never
    /// guesses a CPU, ISA, compiler, backend, or build identity.
    /// </summary>
    [Serializable]
    public sealed class CalibrationProfileFingerprint
    {
        public int SchemaVersion = DeploymentProfileSchema.CurrentFingerprintVersion;
        public string WorkloadId;
        public int WorkloadContractVersion;
        public string RecordSchemaId;
        public int RecordSchemaVersion;
        public string RecordSchemaHash;
        public string CandidateSetHash;
        public string UnityVersion;
        public string BurstVersion;
        public string CollectionsVersion;
        public string MathematicsVersion;
        public string ScriptingBackend;
        public string BuildTarget;
        public string Architecture;
        public string BuildFlagsCanonical;
        public string OperatingSystem;
        public string Processor;
        public string InstructionSet;
        public int LogicalProcessorCount;
        public int JobWorkerCount;
        public string BinaryHash;
        public string CalibrationSettingsCanonical;
        public string CalibrationSettingsHash;
        public string FingerprintSha256;
    }

    public sealed class CalibrationProfileFingerprintInput
    {
        public string WorkloadId;
        public int WorkloadContractVersion;
        public string RecordSchemaId;
        public int RecordSchemaVersion;
        public string RecordSchemaHash;
        public string[] CandidateDefinitions;
        public string UnityVersion;
        public string BurstVersion;
        public string CollectionsVersion;
        public string MathematicsVersion;
        public string ScriptingBackend;
        public string BuildTarget;
        public string Architecture;
        public string[] BuildFlags;
        public string OperatingSystem;
        public string Processor;
        public string InstructionSet;
        public int LogicalProcessorCount;
        public int JobWorkerCount;
        public string BinaryHash;
        public string[] CalibrationSettings;
    }

    [Serializable]
    public sealed class FrozenDeploymentDecision
    {
        public string BaselineCandidateId;
        public string SelectedCandidateId;
        public LayoutSelectionStatus Status;
        public double ImprovementPercent;
        public string Reason;
    }

    [Serializable]
    public sealed class DeploymentProfileProvenance
    {
        public string RunId;
        public string CreatedUtcIso8601;
        public string SourceRepository;
        public string SourceCommit;
        public string RawSuiteSha256;
        public string DecisionSha256;
        public string EvidenceScope;
    }

    /// <summary>
    /// Cache document containing the opaque raw calibration suite, the already
    /// frozen final decision, and provenance. Consumers must not recompute a
    /// winner from RawSuitePayload.
    /// </summary>
    [Serializable]
    public sealed class FrozenDeploymentProfile
    {
        public int ProfileSchemaVersion = DeploymentProfileSchema.CurrentProfileVersion;
        public CalibrationProfileFingerprint Fingerprint;
        public FrozenDeploymentDecision FinalDecision;
        public string RawSuitePayload;
        public DeploymentProfileProvenance Provenance;
    }

    public enum ProfileDocumentLoadStatus
    {
        Loaded = 0,
        Missing = 1,
        Corrupt = 2,
        UnsupportedSchema = 3,
        StorageError = 4,
    }

    public sealed class ProfileDocumentLoadResult
    {
        public ProfileDocumentLoadStatus Status;
        public FrozenDeploymentProfile Profile;
        public int SourceSchemaVersion;
        public bool WasMigrated;
        public string Diagnostic;
    }

    public enum ProfileResolutionStatus
    {
        ExactMatch = 0,
        CompatibleMatch = 1,
        UnsupportedMatch = 2,
        MissingProfile = 3,
        CorruptProfile = 4,
        StorageError = 5,
    }

    [Flags]
    public enum ProfileInvalidationReason : long
    {
        None = 0,
        Missing = 1L << 0,
        Corrupt = 1L << 1,
        StorageFailure = 1L << 2,
        ProfileSchemaVersion = 1L << 3,
        FingerprintSchemaVersion = 1L << 4,
        Workload = 1L << 5,
        WorkloadContractVersion = 1L << 6,
        RecordSchemaId = 1L << 7,
        RecordSchemaVersion = 1L << 8,
        RecordSchemaHash = 1L << 9,
        CandidateSet = 1L << 10,
        UnityVersion = 1L << 11,
        BurstVersion = 1L << 12,
        CollectionsVersion = 1L << 13,
        MathematicsVersion = 1L << 14,
        ScriptingBackend = 1L << 15,
        BuildTarget = 1L << 16,
        Architecture = 1L << 17,
        BuildFlags = 1L << 18,
        OperatingSystem = 1L << 19,
        Processor = 1L << 20,
        InstructionSet = 1L << 21,
        LogicalProcessorCount = 1L << 22,
        JobWorkerCount = 1L << 23,
        BinaryHash = 1L << 24,
        CalibrationSettings = 1L << 25,
        FingerprintIntegrity = 1L << 26,
        RawSuiteIntegrity = 1L << 27,
        DecisionIntegrity = 1L << 28,
        BaselineCandidate = 1L << 29,
        SelectedCandidateUnavailable = 1L << 30,
        CompatibilityNotAuthorized = 1L << 31,
    }

    public sealed class ExplicitProfileCompatibilityRule
    {
        public string RuleId;
        public string ExpectedFingerprintSha256;
        public string StoredFingerprintSha256;
        public ProfileInvalidationReason AllowedDifferences;
        public string EvidenceReference;
    }

    public sealed class ProfileResolverOptions
    {
        public bool AllowCompatibleMatches;
        public ExplicitProfileCompatibilityRule[] CompatibilityRules;

        public static ProfileResolverOptions ExactOnly()
        {
            return new ProfileResolverOptions
            {
                AllowCompatibleMatches = false,
                CompatibilityRules = Array.Empty<ExplicitProfileCompatibilityRule>(),
            };
        }
    }

    public sealed class ProfileResolution
    {
        public ProfileResolutionStatus Status;
        public string CandidateId;
        public string BaselineCandidateId;
        public bool UsedAoSFallback;
        public ProfileInvalidationReason InvalidationReasons;
        public string CompatibilityRuleId;
        public string Diagnostic;
        public FrozenDeploymentProfile Profile;
    }
}
