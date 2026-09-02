using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace Yanagisawa.DataLayoutCalibrator.Tests
{
    public sealed class DeploymentProfileTests
    {
        private static readonly string[] CurrentCandidateIds = { "AoS-b64", "SoA-b64" };

        [Test]
        public void FingerprintIsDeterministicAndCandidateOrderIndependent()
        {
            CalibrationProfileFingerprint first = CreateFingerprint();
            CalibrationProfileFingerprint second = CreateFingerprint(input =>
                input.CandidateDefinitions = input.CandidateDefinitions.Reverse().ToArray());

            Assert.That(second.FingerprintSha256, Is.EqualTo(first.FingerprintSha256));
            Assert.That(second.CandidateSetHash, Is.EqualTo(first.CandidateSetHash));
            Assert.That(CalibrationProfileFingerprintBuilder.HasValidIntegrity(first), Is.True);
        }

        [Test]
        public void FingerprintRejectsMissingBuildFlagsOrInstructionSet()
        {
            Assert.Throws<ArgumentException>(() => CreateFingerprint(input =>
                input.BuildFlags = Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => CreateFingerprint(input =>
                input.InstructionSet = string.Empty));
        }

        [Test]
        public void ExactFingerprintUsesFrozenDecisionWithoutReselection()
        {
            CalibrationProfileFingerprint fingerprint = CreateFingerprint();
            FrozenDeploymentProfile profile = CreateProfile(fingerprint);
            ProfileDocumentLoadResult loaded = FrozenDeploymentProfileCodec.Decode(
                FrozenDeploymentProfileCodec.Encode(profile));

            ProfileResolution resolution = DeploymentProfileResolver.Resolve(
                fingerprint,
                loaded,
                "AoS-b64",
                CurrentCandidateIds);

            Assert.That(loaded.Status, Is.EqualTo(ProfileDocumentLoadStatus.Loaded));
            Assert.That(resolution.Status, Is.EqualTo(ProfileResolutionStatus.ExactMatch));
            Assert.That(resolution.CandidateId, Is.EqualTo("SoA-b64"));
            Assert.That(resolution.UsedAoSFallback, Is.False);
            Assert.That(resolution.Profile.RawSuitePayload, Does.Contain("synthetic-unit-test"));
        }

        [TestCase("candidate", ProfileInvalidationReason.CandidateSet)]
        [TestCase("compiler", ProfileInvalidationReason.BurstVersion)]
        [TestCase("backend", ProfileInvalidationReason.ScriptingBackend)]
        [TestCase("settings", ProfileInvalidationReason.CalibrationSettings)]
        [TestCase("worker", ProfileInvalidationReason.JobWorkerCount)]
        public void CriticalFingerprintChangesInvalidateAndFallBackToTunedAoS(
            string changedDimension,
            ProfileInvalidationReason expectedReason)
        {
            CalibrationProfileFingerprint storedFingerprint = CreateFingerprint();
            CalibrationProfileFingerprint expected = CreateFingerprint(input =>
            {
                switch (changedDimension)
                {
                    case "candidate":
                        input.CandidateDefinitions = new[]
                        {
                            "AoS-b64|layout=AoS|kernel=faithful",
                            "SoA-b64|layout=SoA|kernel=faithful",
                            "AoSoA8-b64|layout=AoSoA8|kernel=faithful",
                        };
                        break;
                    case "compiler":
                        input.BurstVersion = "1.8.30";
                        break;
                    case "backend":
                        input.ScriptingBackend = "IL2CPP";
                        break;
                    case "settings":
                        input.CalibrationSettings = new[]
                        {
                            "bootstrapIterations=4001",
                            "lifetimeTicks=600",
                            "minimumImprovementPercent=10",
                        };
                        break;
                    case "worker":
                        input.JobWorkerCount = 11;
                        break;
                }
            });
            ProfileResolution resolution = DeploymentProfileResolver.Resolve(
                expected,
                Loaded(CreateProfile(storedFingerprint)),
                "AoS-b64",
                changedDimension == "candidate"
                    ? new[] { "AoS-b64", "SoA-b64", "AoSoA8-b64" }
                    : CurrentCandidateIds);

            Assert.That(resolution.Status, Is.EqualTo(ProfileResolutionStatus.UnsupportedMatch));
            Assert.That(resolution.CandidateId, Is.EqualTo("AoS-b64"));
            Assert.That(resolution.UsedAoSFallback, Is.True);
            Assert.That((resolution.InvalidationReasons & expectedReason) != 0, Is.True);
        }

        [Test]
        public void MissingCorruptUnsupportedAndUnavailableProfilesFallBackToTunedAoS()
        {
            CalibrationProfileFingerprint expected = CreateFingerprint();
            ProfileResolution missing = DeploymentProfileResolver.Resolve(
                expected,
                new ProfileDocumentLoadResult { Status = ProfileDocumentLoadStatus.Missing },
                "AoS-b64",
                CurrentCandidateIds);

            string encoded = FrozenDeploymentProfileCodec.Encode(CreateProfile(expected));
            char replacement = encoded[encoded.Length - 1] == '0' ? '1' : '0';
            ProfileDocumentLoadResult corruptLoad = FrozenDeploymentProfileCodec.Decode(
                encoded.Substring(0, encoded.Length - 1) + replacement);
            ProfileResolution corrupt = DeploymentProfileResolver.Resolve(
                expected,
                corruptLoad,
                "AoS-b64",
                CurrentCandidateIds);

            ProfileDocumentLoadResult unsupportedLoad = FrozenDeploymentProfileCodec.Decode(
                "DLC-FROZEN-PROFILE|99\neA==\nSHA256=0");
            ProfileResolution unsupported = DeploymentProfileResolver.Resolve(
                expected,
                unsupportedLoad,
                "AoS-b64",
                CurrentCandidateIds);

            ProfileResolution unavailable = DeploymentProfileResolver.Resolve(
                expected,
                Loaded(CreateProfile(expected)),
                "AoS-b64",
                new[] { "AoS-b64" });

            AssertFallback(missing, ProfileResolutionStatus.MissingProfile, ProfileInvalidationReason.Missing);
            AssertFallback(corrupt, ProfileResolutionStatus.CorruptProfile, ProfileInvalidationReason.Corrupt);
            AssertFallback(unsupported, ProfileResolutionStatus.UnsupportedMatch, ProfileInvalidationReason.ProfileSchemaVersion);
            AssertFallback(unavailable, ProfileResolutionStatus.UnsupportedMatch, ProfileInvalidationReason.SelectedCandidateUnavailable);
        }

        [Test]
        public void Schema1ProfileMigratesInMemoryWithoutChangingRawSuiteOrDecision()
        {
            FrozenDeploymentProfile original = CreateProfile(CreateFingerprint());
            string schema1 = DowngradeToSchema1(FrozenDeploymentProfileCodec.Encode(original));

            ProfileDocumentLoadResult migrated = FrozenDeploymentProfileCodec.Decode(schema1);

            Assert.That(migrated.Status, Is.EqualTo(ProfileDocumentLoadStatus.Loaded));
            Assert.That(migrated.SourceSchemaVersion, Is.EqualTo(1));
            Assert.That(migrated.WasMigrated, Is.True);
            Assert.That(migrated.Profile.ProfileSchemaVersion, Is.EqualTo(2));
            Assert.That(migrated.Profile.RawSuitePayload, Is.EqualTo(original.RawSuitePayload));
            Assert.That(migrated.Profile.FinalDecision.SelectedCandidateId, Is.EqualTo("SoA-b64"));
            Assert.That(
                migrated.Profile.Provenance.DecisionSha256,
                Is.EqualTo(FrozenDeploymentProfileFactory.ComputeDecisionSha256(original.FinalDecision)));
            Assert.That(FrozenDeploymentProfileCodec.Encode(migrated.Profile), Does.StartWith("DLC-FROZEN-PROFILE|2\n"));
        }

        [Test]
        public void CompatibleMatchRequiresAnExplicitFingerprintPairAndEvidenceReference()
        {
            CalibrationProfileFingerprint stored = CreateFingerprint();
            CalibrationProfileFingerprint expected = CreateFingerprint(input =>
                input.UnityVersion = "6000.5.4f1");
            FrozenDeploymentProfile profile = CreateProfile(stored);

            ProfileResolution exactOnly = DeploymentProfileResolver.Resolve(
                expected,
                Loaded(profile),
                "AoS-b64",
                CurrentCandidateIds);
            var options = new ProfileResolverOptions
            {
                AllowCompatibleMatches = true,
                CompatibilityRules = new[]
                {
                    new ExplicitProfileCompatibilityRule
                    {
                        RuleId = "synthetic-unity-patch-pair",
                        ExpectedFingerprintSha256 = expected.FingerprintSha256,
                        StoredFingerprintSha256 = stored.FingerprintSha256,
                        AllowedDifferences = ProfileInvalidationReason.UnityVersion,
                        EvidenceReference = "Synthetic compatibility-rule unit test; not Player or device evidence.",
                    },
                },
            };
            ProfileResolution compatible = DeploymentProfileResolver.Resolve(
                expected,
                Loaded(profile),
                "AoS-b64",
                CurrentCandidateIds,
                options);

            Assert.That(exactOnly.UsedAoSFallback, Is.True);
            Assert.That(compatible.Status, Is.EqualTo(ProfileResolutionStatus.CompatibleMatch));
            Assert.That(compatible.CandidateId, Is.EqualTo("SoA-b64"));
            Assert.That(compatible.CompatibilityRuleId, Is.EqualTo("synthetic-unity-patch-pair"));
        }

        [Test]
        public void RecordSchemaMigrationCannotAuthorizeReuseImplicitly()
        {
            CalibrationProfileFingerprint stored = CreateFingerprint();
            CalibrationProfileFingerprint expected = CreateFingerprint(input =>
            {
                input.RecordSchemaVersion = 2;
                input.RecordSchemaHash = new string('C', 64);
            });
            var options = new ProfileResolverOptions
            {
                AllowCompatibleMatches = true,
                CompatibilityRules = new[]
                {
                    new ExplicitProfileCompatibilityRule
                    {
                        RuleId = "unsafe-schema-rule",
                        ExpectedFingerprintSha256 = expected.FingerprintSha256,
                        StoredFingerprintSha256 = stored.FingerprintSha256,
                        AllowedDifferences = ProfileInvalidationReason.RecordSchemaVersion |
                                             ProfileInvalidationReason.RecordSchemaHash,
                        EvidenceReference = "Synthetic negative test.",
                    },
                },
            };

            ProfileResolution resolution = DeploymentProfileResolver.Resolve(
                expected,
                Loaded(CreateProfile(stored)),
                "AoS-b64",
                CurrentCandidateIds,
                options);

            AssertFallback(resolution, ProfileResolutionStatus.UnsupportedMatch, ProfileInvalidationReason.RecordSchemaVersion);
            Assert.That(
                (resolution.InvalidationReasons & ProfileInvalidationReason.RecordSchemaHash) != 0,
                Is.True);
        }

        [Test]
        public void FileStoreHandlesMissingSaveReplacementAndCorruption()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "dlc-profile-tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FileFrozenDeploymentProfileStore(directory);
                Assert.That(store.Load("particle-integrate-v2").Status, Is.EqualTo(ProfileDocumentLoadStatus.Missing));

                FrozenDeploymentProfile profile = CreateProfile(CreateFingerprint());
                ProfileStoreWriteResult first = store.Save("particle-integrate-v2", profile);
                ProfileStoreWriteResult second = store.Save("particle-integrate-v2", profile);
                Assert.That(first.Succeeded, Is.True, first.Diagnostic);
                Assert.That(second.Succeeded, Is.True, second.Diagnostic);
                Assert.That(store.Load("particle-integrate-v2").Status, Is.EqualTo(ProfileDocumentLoadStatus.Loaded));

                File.WriteAllText(store.GetProfilePath("particle-integrate-v2"), "corrupt");
                Assert.That(store.Load("particle-integrate-v2").Status, Is.EqualTo(ProfileDocumentLoadStatus.Corrupt));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void RechecksRawSuiteIntegrityAfterDocumentChecksumPasses()
        {
            FrozenDeploymentProfile profile = CreateProfile(CreateFingerprint());
            string encoded = FrozenDeploymentProfileCodec.Encode(profile);
            string tampered = ReplaceFieldAndChecksum(
                encoded,
                lineIndex: 30,
                value: "{\"fixture\":\"synthetic-unit-test-tampered\"}");

            ProfileDocumentLoadResult result = FrozenDeploymentProfileCodec.Decode(tampered);

            Assert.That(result.Status, Is.EqualTo(ProfileDocumentLoadStatus.Corrupt));
            Assert.That(result.Diagnostic, Does.Contain("field-level integrity"));
        }

        private static CalibrationProfileFingerprint CreateFingerprint(
            Action<CalibrationProfileFingerprintInput> modify = null)
        {
            var input = new CalibrationProfileFingerprintInput
            {
                WorkloadId = "particle-integrate-v2",
                WorkloadContractVersion = 2,
                RecordSchemaId = "particle-record",
                RecordSchemaVersion = 1,
                RecordSchemaHash = new string('A', 64),
                CandidateDefinitions = new[]
                {
                    "AoS-b64|layout=AoS|kernel=faithful",
                    "SoA-b64|layout=SoA|kernel=faithful",
                },
                UnityVersion = "6000.5.3f1",
                BurstVersion = "1.8.29",
                CollectionsVersion = "2.6.3",
                MathematicsVersion = "1.3.2",
                ScriptingBackend = "Mono",
                BuildTarget = "StandaloneWindows64",
                Architecture = "x86_64",
                BuildFlags = new[] { "BURST=1", "DEVELOPMENT=0", "SAFETY_CHECKS=0" },
                OperatingSystem = "SyntheticWindows",
                Processor = "SyntheticCpu",
                InstructionSet = "synthetic-x86_64-baseline",
                LogicalProcessorCount = 16,
                JobWorkerCount = 8,
                BinaryHash = new string('B', 64),
                CalibrationSettings = new[]
                {
                    "bootstrapIterations=4000",
                    "lifetimeTicks=600",
                    "minimumImprovementPercent=10",
                },
            };
            modify?.Invoke(input);
            return CalibrationProfileFingerprintBuilder.Create(input);
        }

        private static FrozenDeploymentProfile CreateProfile(
            CalibrationProfileFingerprint fingerprint)
        {
            return FrozenDeploymentProfileFactory.Create(
                fingerprint,
                new FrozenDeploymentDecision
                {
                    BaselineCandidateId = "AoS-b64",
                    SelectedCandidateId = "SoA-b64",
                    Status = LayoutSelectionStatus.Optimized,
                    ImprovementPercent = 12.5,
                    Reason = "Synthetic frozen decision for resolver unit tests.",
                },
                "{\"fixture\":\"synthetic-unit-test\",\"evidence\":false}",
                new DeploymentProfileProvenance
                {
                    RunId = "synthetic-profile-unit-test",
                    CreatedUtcIso8601 = "2026-09-02T00:00:00Z",
                    SourceRepository = "https://github.com/Yanagisawa2002/data-layout-calibrator",
                    SourceCommit = "644893990ed18e56619da8d2737e6b7592eb6080",
                    EvidenceScope = "Synthetic unit-test fixture; not Unity Player, device, ISA, hardware-counter, or cross-device evidence.",
                });
        }

        private static ProfileDocumentLoadResult Loaded(FrozenDeploymentProfile profile)
        {
            return new ProfileDocumentLoadResult
            {
                Status = ProfileDocumentLoadStatus.Loaded,
                Profile = profile,
                SourceSchemaVersion = profile.ProfileSchemaVersion,
            };
        }

        private static void AssertFallback(
            ProfileResolution resolution,
            ProfileResolutionStatus expectedStatus,
            ProfileInvalidationReason expectedReason)
        {
            Assert.That(resolution.Status, Is.EqualTo(expectedStatus));
            Assert.That(resolution.CandidateId, Is.EqualTo("AoS-b64"));
            Assert.That(resolution.UsedAoSFallback, Is.True);
            Assert.That((resolution.InvalidationReasons & expectedReason) != 0, Is.True);
        }

        private static string DowngradeToSchema1(string schema2)
        {
            var lines = schema2.Split('\n').ToList();
            lines.RemoveAt(lines.Count - 1);
            lines[0] = "DLC-FROZEN-PROFILE|1";
            lines.RemoveAt(36);
            string payload = string.Join("\n", lines);
            return payload + "\nSHA256=" + Sha256(payload);
        }

        private static string ReplaceFieldAndChecksum(
            string document,
            int lineIndex,
            string value)
        {
            var lines = document.Split('\n').ToList();
            lines.RemoveAt(lines.Count - 1);
            lines[lineIndex] = Convert.ToBase64String(new UTF8Encoding(false, true).GetBytes(value));
            string payload = string.Join("\n", lines);
            return payload + "\nSHA256=" + Sha256(payload);
        }

        private static string Sha256(string value)
        {
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(value);
            byte[] digest;
            using (SHA256 algorithm = SHA256.Create())
                digest = algorithm.ComputeHash(bytes);
            var result = new StringBuilder(64);
            for (int index = 0; index < digest.Length; index++)
                result.Append(digest[index].ToString("X2"));
            return result.ToString();
        }
    }
}
