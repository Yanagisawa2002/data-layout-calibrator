using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>
    /// A strict, fixed-order, length-safe profile codec. It uses direct field
    /// assignment and contains no reflection, runtime type discovery, or winner
    /// recomputation. Schema 1 documents are migrated in memory to schema 2.
    /// </summary>
    public static class FrozenDeploymentProfileCodec
    {
        private const string HeaderPrefix = "DLC-FROZEN-PROFILE|";
        private const string ChecksumPrefix = "SHA256=";
        private const int Version1FieldCount = 36;
        private const int Version2FieldCount = 37;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static string Encode(FrozenDeploymentProfile profile)
        {
            if (!FrozenDeploymentProfileFactory.HasValidIntegrity(profile))
                throw new ArgumentException("The frozen profile is incomplete or failed integrity validation.", nameof(profile));

            var lines = new List<string>(Version2FieldCount + 2)
            {
                HeaderPrefix + DeploymentProfileSchema.CurrentProfileVersion.ToString(CultureInfo.InvariantCulture),
            };
            AddFingerprint(lines, profile.Fingerprint);
            AddDecision(lines, profile.FinalDecision);
            Add(lines, profile.RawSuitePayload);
            Add(lines, profile.Provenance.RunId);
            Add(lines, profile.Provenance.CreatedUtcIso8601);
            Add(lines, profile.Provenance.SourceRepository);
            Add(lines, profile.Provenance.SourceCommit);
            Add(lines, profile.Provenance.RawSuiteSha256);
            Add(lines, profile.Provenance.DecisionSha256);
            Add(lines, profile.Provenance.EvidenceScope);
            return Complete(lines);
        }

        public static ProfileDocumentLoadResult Decode(string document)
        {
            if (string.IsNullOrEmpty(document))
                return Corrupt("The profile document is empty.");

            try
            {
                string[] lines = document.Split('\n');
                if (lines.Length < 3 || lines[lines.Length - 1].Length == 0)
                    return Corrupt("The profile document has an invalid line structure.");

                int schemaVersion = ParseHeader(lines[0]);
                if (schemaVersion < DeploymentProfileSchema.MinimumMigratableProfileVersion ||
                    schemaVersion > DeploymentProfileSchema.CurrentProfileVersion)
                {
                    return new ProfileDocumentLoadResult
                    {
                        Status = ProfileDocumentLoadStatus.UnsupportedSchema,
                        SourceSchemaVersion = schemaVersion,
                        Diagnostic = $"Profile schema {schemaVersion} is unsupported.",
                    };
                }

                int expectedFields = schemaVersion == 1
                    ? Version1FieldCount
                    : Version2FieldCount;
                if (lines.Length != expectedFields + 2)
                {
                    return Corrupt(
                        $"Profile schema {schemaVersion} expected {expectedFields} fields but found {lines.Length - 2}.",
                        schemaVersion);
                }

                string payload = string.Join("\n", lines, 0, lines.Length - 1);
                string checksumLine = lines[lines.Length - 1];
                if (!checksumLine.StartsWith(ChecksumPrefix, StringComparison.Ordinal))
                    return Corrupt("The profile checksum line is missing.", schemaVersion);
                string storedChecksum = checksumLine.Substring(ChecksumPrefix.Length);
                string actualChecksum = ProfileCanonicalization.Sha256(payload);
                if (!string.Equals(storedChecksum, actualChecksum, StringComparison.Ordinal))
                    return Corrupt("The profile document checksum does not match its contents.", schemaVersion);

                var cursor = new FieldCursor(lines, 1, lines.Length - 1);
                CalibrationProfileFingerprint fingerprint = ReadFingerprint(cursor);
                FrozenDeploymentDecision decision = ReadDecision(cursor);
                string rawSuite = cursor.Read();
                var provenance = new DeploymentProfileProvenance
                {
                    RunId = cursor.Read(),
                    CreatedUtcIso8601 = cursor.Read(),
                    SourceRepository = cursor.Read(),
                    SourceCommit = cursor.Read(),
                    RawSuiteSha256 = cursor.Read(),
                    DecisionSha256 = schemaVersion >= 2
                        ? cursor.Read()
                        : FrozenDeploymentProfileFactory.ComputeDecisionSha256(decision),
                    EvidenceScope = cursor.Read(),
                };
                cursor.RequireEnd();

                var profile = new FrozenDeploymentProfile
                {
                    ProfileSchemaVersion = DeploymentProfileSchema.CurrentProfileVersion,
                    Fingerprint = fingerprint,
                    FinalDecision = decision,
                    RawSuitePayload = rawSuite,
                    Provenance = provenance,
                };
                if (!FrozenDeploymentProfileFactory.HasValidIntegrity(profile))
                    return Corrupt("The decoded profile failed field-level integrity validation.", schemaVersion);

                return new ProfileDocumentLoadResult
                {
                    Status = ProfileDocumentLoadStatus.Loaded,
                    Profile = profile,
                    SourceSchemaVersion = schemaVersion,
                    WasMigrated = schemaVersion != DeploymentProfileSchema.CurrentProfileVersion,
                    Diagnostic = schemaVersion == DeploymentProfileSchema.CurrentProfileVersion
                        ? "Loaded an exact schema-2 frozen profile document."
                        : "Migrated a schema-1 frozen profile document to schema 2 in memory.",
                };
            }
            catch (Exception exception) when (
                exception is FormatException ||
                exception is ArgumentException ||
                exception is OverflowException ||
                exception is DecoderFallbackException)
            {
                return Corrupt("The profile document could not be parsed: " + exception.Message);
            }
        }

        private static int ParseHeader(string line)
        {
            if (!line.StartsWith(HeaderPrefix, StringComparison.Ordinal))
                throw new FormatException("The profile header is invalid.");
            return int.Parse(
                line.Substring(HeaderPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture);
        }

        private static void AddFingerprint(
            ICollection<string> lines,
            CalibrationProfileFingerprint fingerprint)
        {
            Add(lines, fingerprint.SchemaVersion);
            Add(lines, fingerprint.WorkloadId);
            Add(lines, fingerprint.WorkloadContractVersion);
            Add(lines, fingerprint.RecordSchemaId);
            Add(lines, fingerprint.RecordSchemaVersion);
            Add(lines, fingerprint.RecordSchemaHash);
            Add(lines, fingerprint.CandidateSetHash);
            Add(lines, fingerprint.UnityVersion);
            Add(lines, fingerprint.BurstVersion);
            Add(lines, fingerprint.CollectionsVersion);
            Add(lines, fingerprint.MathematicsVersion);
            Add(lines, fingerprint.ScriptingBackend);
            Add(lines, fingerprint.BuildTarget);
            Add(lines, fingerprint.Architecture);
            Add(lines, fingerprint.BuildFlagsCanonical);
            Add(lines, fingerprint.OperatingSystem);
            Add(lines, fingerprint.Processor);
            Add(lines, fingerprint.InstructionSet);
            Add(lines, fingerprint.LogicalProcessorCount);
            Add(lines, fingerprint.JobWorkerCount);
            Add(lines, fingerprint.BinaryHash);
            Add(lines, fingerprint.CalibrationSettingsCanonical);
            Add(lines, fingerprint.CalibrationSettingsHash);
            Add(lines, fingerprint.FingerprintSha256);
        }

        private static CalibrationProfileFingerprint ReadFingerprint(FieldCursor cursor)
        {
            return new CalibrationProfileFingerprint
            {
                SchemaVersion = cursor.ReadInt32(),
                WorkloadId = cursor.Read(),
                WorkloadContractVersion = cursor.ReadInt32(),
                RecordSchemaId = cursor.Read(),
                RecordSchemaVersion = cursor.ReadInt32(),
                RecordSchemaHash = cursor.Read(),
                CandidateSetHash = cursor.Read(),
                UnityVersion = cursor.Read(),
                BurstVersion = cursor.Read(),
                CollectionsVersion = cursor.Read(),
                MathematicsVersion = cursor.Read(),
                ScriptingBackend = cursor.Read(),
                BuildTarget = cursor.Read(),
                Architecture = cursor.Read(),
                BuildFlagsCanonical = cursor.Read(),
                OperatingSystem = cursor.Read(),
                Processor = cursor.Read(),
                InstructionSet = cursor.Read(),
                LogicalProcessorCount = cursor.ReadInt32(),
                JobWorkerCount = cursor.ReadInt32(),
                BinaryHash = cursor.Read(),
                CalibrationSettingsCanonical = cursor.Read(),
                CalibrationSettingsHash = cursor.Read(),
                FingerprintSha256 = cursor.Read(),
            };
        }

        private static void AddDecision(
            ICollection<string> lines,
            FrozenDeploymentDecision decision)
        {
            Add(lines, decision.BaselineCandidateId);
            Add(lines, decision.SelectedCandidateId);
            Add(lines, (int)decision.Status);
            Add(lines, decision.ImprovementPercent.ToString("R", CultureInfo.InvariantCulture));
            Add(lines, decision.Reason);
        }

        private static FrozenDeploymentDecision ReadDecision(FieldCursor cursor)
        {
            string baselineCandidateId = cursor.Read();
            string selectedCandidateId = cursor.Read();
            int statusValue = cursor.ReadInt32();
            var status = (LayoutSelectionStatus)statusValue;
            if (!FrozenDeploymentProfileFactory.IsSupportedDecisionStatus(status))
                throw new FormatException("The frozen decision status is invalid.");
            return new FrozenDeploymentDecision
            {
                BaselineCandidateId = baselineCandidateId,
                SelectedCandidateId = selectedCandidateId,
                Status = status,
                ImprovementPercent = cursor.ReadDouble(),
                Reason = cursor.Read(),
            };
        }

        private static void Add(ICollection<string> lines, int value)
        {
            Add(lines, value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Add(ICollection<string> lines, string value)
        {
            byte[] bytes = StrictUtf8.GetBytes(value ?? string.Empty);
            lines.Add(Convert.ToBase64String(bytes));
        }

        private static string Complete(ICollection<string> lines)
        {
            string[] array = new string[lines.Count];
            lines.CopyTo(array, 0);
            string payload = string.Join("\n", array);
            return payload + "\n" + ChecksumPrefix + ProfileCanonicalization.Sha256(payload);
        }

        private static ProfileDocumentLoadResult Corrupt(
            string diagnostic,
            int sourceSchemaVersion = 0)
        {
            return new ProfileDocumentLoadResult
            {
                Status = ProfileDocumentLoadStatus.Corrupt,
                SourceSchemaVersion = sourceSchemaVersion,
                Diagnostic = diagnostic,
            };
        }

        private sealed class FieldCursor
        {
            private readonly string[] _lines;
            private readonly int _end;
            private int _index;

            internal FieldCursor(string[] lines, int start, int end)
            {
                _lines = lines;
                _index = start;
                _end = end;
            }

            internal string Read()
            {
                if (_index >= _end)
                    throw new FormatException("The profile ended before all fields were read.");
                string encoded = _lines[_index++];
                byte[] bytes = Convert.FromBase64String(encoded);
                if (!string.Equals(Convert.ToBase64String(bytes), encoded, StringComparison.Ordinal))
                    throw new FormatException("A profile field is not canonical Base64.");
                return StrictUtf8.GetString(bytes);
            }

            internal int ReadInt32()
            {
                return int.Parse(Read(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            }

            internal double ReadDouble()
            {
                return double.Parse(Read(), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            internal void RequireEnd()
            {
                if (_index != _end)
                    throw new FormatException("The profile has unexpected trailing fields.");
            }
        }
    }
}
