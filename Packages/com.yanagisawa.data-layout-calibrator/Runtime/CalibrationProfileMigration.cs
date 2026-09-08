using System;
using System.Collections.Generic;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>
    /// Proposed additive schema transition for the scientific-core branch. Schema 2
    /// is migrated in memory; schema 3 is validation-only. This API never writes an
    /// evidence file.
    /// </summary>
    public static class CalibrationProfileMigration
    {
        public const int LegacySchemaVersion = 2;
        public const int ProposedSchemaVersion = 3;

        public static CalibrationSuiteProfile UpgradeInMemory(CalibrationSuiteProfile suite)
        {
            if (suite == null)
                throw new ArgumentNullException(nameof(suite));
            ValidateTopLevelVersion(suite.SchemaVersion, "suite");
            if (suite.Scenarios == null)
                throw new ArgumentException("A calibration suite requires a scenario array.", nameof(suite));

            for (int index = 0; index < suite.Scenarios.Length; index++)
            {
                ScenarioCalibrationProfile scenario = suite.Scenarios[index];
                if (scenario == null)
                    throw new ArgumentException($"Suite scenario {index} is null.", nameof(suite));
                if (scenario.SchemaVersion != suite.SchemaVersion)
                {
                    throw new ArgumentException(
                        $"Suite schema {suite.SchemaVersion} cannot contain scenario schema {scenario.SchemaVersion}.",
                        nameof(suite));
                }
            }

            if (suite.SchemaVersion == ProposedSchemaVersion)
            {
                for (int index = 0; index < suite.Scenarios.Length; index++)
                    ValidateScenario3(suite.Scenarios[index]);
                return suite;
            }

            // Validate every legacy scenario before mutating any of them so a bad
            // nested version or corrupt metadata cannot leave a half-migrated suite.
            for (int index = 0; index < suite.Scenarios.Length; index++)
                ValidateScenario2ForMigration(suite.Scenarios[index]);
            for (int index = 0; index < suite.Scenarios.Length; index++)
                MigrateScenario2(suite.Scenarios[index]);

            suite.SchemaVersion = ProposedSchemaVersion;
            return suite;
        }

        public static ScenarioCalibrationProfile UpgradeInMemory(ScenarioCalibrationProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            ValidateTopLevelVersion(profile.SchemaVersion, "scenario profile");
            if (profile.SchemaVersion == ProposedSchemaVersion)
            {
                ValidateScenario3(profile);
                return profile;
            }

            ValidateScenario2ForMigration(profile);
            MigrateScenario2(profile);
            return profile;
        }

        private static void ValidateTopLevelVersion(int schemaVersion, string description)
        {
            if (schemaVersion != LegacySchemaVersion && schemaVersion != ProposedSchemaVersion)
                throw new ArgumentException($"Unsupported {description} schema {schemaVersion}.");
        }

        private static void ValidateScenario2ForMigration(ScenarioCalibrationProfile profile)
        {
            if (profile.SchemaVersion != LegacySchemaVersion)
                throw new ArgumentException($"Unsupported scenario profile schema {profile.SchemaVersion}.");
            ValidateScenarioIdentity(profile.Scenario);
            if (profile.CalibrationResults == null)
                throw new ArgumentException("A schema-2 profile requires a calibration-result array.");
            bool holdoutBaselineAbsent =
                IsAbsentOptionalResultShape(profile.HoldoutBaselineResult);
            bool holdoutSelectedAbsent =
                IsAbsentOptionalResultShape(profile.HoldoutSelectedResult);
            if (holdoutBaselineAbsent != holdoutSelectedAbsent)
                throw new ArgumentException("Schema-2 holdout results must be both present or both absent.");
            Dictionary<string, CandidateDescriptor> candidates =
                ValidateResultArrayForMigration(
                    profile.CalibrationResults,
                    profile.Scenario,
                    profile.ElementCount);
            if (!holdoutBaselineAbsent)
            {
                ValidateResultForMigration(profile.HoldoutBaselineResult, profile.Scenario);
                ValidateMeasurementAssignment(
                    profile.HoldoutBaselineResult,
                    BenchmarkPhase.Holdout,
                    profile.HoldoutElementCount,
                    true,
                    "holdout baseline");
            }
            if (!holdoutSelectedAbsent)
            {
                ValidateResultForMigration(profile.HoldoutSelectedResult, profile.Scenario);
                ValidateMeasurementAssignment(
                    profile.HoldoutSelectedResult,
                    BenchmarkPhase.Holdout,
                    profile.HoldoutElementCount,
                    true,
                    "holdout selected");
            }
            ValidateDecisionForMigration(profile.CalibrationDecision);
            ValidateDecisionForMigration(profile.FinalDecision);
            if (!HasAbsentAdvantageEnvelopeReferenceShape(profile.AdvantageEnvelope))
            {
                throw new ArgumentException(
                    "Schema 2 cannot contain a schema-3 advantage-envelope reference.");
            }
            if (!HasLegacyAbsentSamplingDesignShape(profile.SamplingDesign))
                ValidateSamplingDesign(profile.SamplingDesign);

            LayoutSelectionDecision calibrationPreview = MigrateDecision2(
                profile.CalibrationDecision,
                DecisionStage.Calibration);
            LayoutSelectionDecision finalPreview = MigrateDecision2(
                profile.FinalDecision,
                holdoutBaselineAbsent
                    ? DecisionStage.Calibration
                    : DecisionStage.HoldoutConfirmation);
            ValidateDecision3(
                calibrationPreview,
                DecisionStage.Calibration,
                candidates);
            if (!holdoutBaselineAbsent)
            {
                if (calibrationPreview.Status != LayoutSelectionStatus.Optimized)
                {
                    throw new ArgumentException(
                        "Schema-2 holdout evidence requires an optimized frozen calibration winner.");
                }
                ValidateHoldoutCandidateIdentity(
                    NormalizeCandidateIfPresent(profile.HoldoutBaselineResult.Candidate),
                    candidates,
                    calibrationPreview.BaselineCandidate,
                    "baseline");
                ValidateHoldoutCandidateIdentity(
                    NormalizeCandidateIfPresent(profile.HoldoutSelectedResult.Candidate),
                    candidates,
                    calibrationPreview.SelectedCandidate,
                    "selected");
            }
            ValidateDecision3(
                finalPreview,
                holdoutBaselineAbsent
                    ? DecisionStage.Calibration
                    : DecisionStage.HoldoutConfirmation,
                candidates);
        }

        private static void MigrateScenario2(ScenarioCalibrationProfile profile)
        {
            if (IsAbsentOptionalResultShape(profile.HoldoutBaselineResult))
                profile.HoldoutBaselineResult = null;
            if (IsAbsentOptionalResultShape(profile.HoldoutSelectedResult))
                profile.HoldoutSelectedResult = null;

            if (profile.CalibrationResults != null)
            {
                for (int index = 0; index < profile.CalibrationResults.Length; index++)
                    MigrateResult2(profile.CalibrationResults[index], profile.Scenario);
            }
            MigrateResult2(profile.HoldoutBaselineResult, profile.Scenario);
            MigrateResult2(profile.HoldoutSelectedResult, profile.Scenario);
            profile.CalibrationDecision = MigrateDecision2(
                profile.CalibrationDecision,
                DecisionStage.Calibration);
            profile.FinalDecision = MigrateDecision2(
                profile.FinalDecision,
                profile.HoldoutBaselineResult == null
                    ? DecisionStage.Calibration
                    : DecisionStage.HoldoutConfirmation);

            if (HasLegacyAbsentSamplingDesignShape(profile.SamplingDesign))
            {
                profile.SamplingDesign = new SamplingDesignDescriptor
                {
                    CandidateOrder = MeasurementOrderKind.RandomizedBlocked,
                    PairingUnit = "implicit schema-2 sample-array index (reconstructed in memory)",
                    EvidenceScope = EvidenceScope.SinglePlayer,
                    CalibrationTunesCandidates = true,
                    HoldoutRetuningPermitted = false,
                    UncertaintyDescription =
                        "Historical schema-2 confidence intervals used independent resampling. Reconstructed block IDs enable future paired replay, but do not retroactively change the stored interval.",
                };
            }
            profile.SamplingDesign.ReconstructedFromSchema2 = true;

            profile.SchemaVersion = ProposedSchemaVersion;
            ValidateScenario3(profile);
        }

        private static bool IsAbsentOptionalResultShape(
            LayoutBenchmarkResult result)
        {
            if (result == null)
                return true;

            // JsonUtility materializes missing nested reference fields as empty
            // shells. This exact shape represents an absent optional holdout
            // result; any partially populated result is validated normally. Schema
            // 3 merely recognizes the shape and does not mutate it.
            return result.SampleSchemaVersion == LayoutBenchmarkResult.CurrentSampleSchemaVersion &&
                   string.IsNullOrEmpty(result.ScenarioId) &&
                   result.ScenarioContractVersion == 0 &&
                   result.Phase == default &&
                   IsCandidateAbsent(result.Candidate) &&
                   result.ElementCount == 0 &&
                   result.StepsPerSample == 0 &&
                   result.Latency.Equals(default(LatencySummary)) &&
                   result.BoundaryCost.Equals(default(BoundaryCostSummary)) &&
                   result.AmortizedLatency.Equals(default(LatencySummary)) &&
                   result.ResidentSamplesMillisecondsPerTick == null &&
                   result.IngressSamplesMilliseconds == null &&
                   result.ExportSamplesMilliseconds == null &&
                   result.AmortizedSamplesMillisecondsPerTick == null &&
                   result.ResidentBlockIds == null &&
                   result.IngressBlockIds == null &&
                   result.ExportBlockIds == null &&
                   result.ResidentOrderPositions == null &&
                   result.IngressOrderPositions == null &&
                   result.ExportOrderPositions == null &&
                   !result.Completed &&
                   !result.ParityPassed &&
                   result.Parity.Equals(default(ParityReport)) &&
                   result.HotPathManagedAllocationBytes == 0L &&
                   result.BoundaryManagedAllocationBytes == 0L &&
                   result.ResidentBytes == 0L &&
                   string.IsNullOrEmpty(result.StateHash) &&
                   string.IsNullOrEmpty(result.FailureReason);
        }

        private static bool HasLegacyAbsentSamplingDesignShape(
            SamplingDesignDescriptor design)
        {
            if (design == null)
                return true;

            // JsonUtility may instantiate a missing nested serializable class and
            // run its field initializers. Treat only that entirely empty schema-2
            // shape as absent; partial metadata remains invalid.
            return design.SchemaVersion == SamplingDesignDescriptor.CurrentSchemaVersion &&
                   design.CandidateOrder == default &&
                   string.IsNullOrEmpty(design.PairingUnit) &&
                   design.EvidenceScope == default &&
                   !design.CalibrationTunesCandidates &&
                   !design.HoldoutRetuningPermitted &&
                   !design.ReconstructedFromSchema2 &&
                   string.IsNullOrEmpty(design.UncertaintyDescription);
        }

        private static bool HasAbsentAdvantageEnvelopeReferenceShape(
            AdvantageEnvelopeArtifactReference reference)
        {
            if (reference == null)
                return true;
            return reference.SchemaVersion == AdvantageEnvelopeArtifactReference.CurrentSchemaVersion &&
                   string.IsNullOrEmpty(reference.ArtifactId) &&
                   string.IsNullOrEmpty(reference.ArtifactSha256) &&
                   reference.ArtifactSchemaVersion == 0 &&
                   string.IsNullOrEmpty(reference.DecisionEngineVersion) &&
                   string.IsNullOrEmpty(reference.ScenarioId) &&
                   reference.ContractVersion == 0 &&
                   string.IsNullOrEmpty(reference.CandidateSetSha256) &&
                   string.IsNullOrEmpty(reference.MeasurementSchemaSha256);
        }

        private static void ValidateScenario3(ScenarioCalibrationProfile profile)
        {
            if (profile.SchemaVersion != ProposedSchemaVersion)
                throw new ArgumentException($"Unsupported scenario profile schema {profile.SchemaVersion}.");
            ValidateScenarioIdentity(profile.Scenario);
            if (profile.SamplingDesign == null)
                throw new ArgumentException("Schema 3 requires an explicit sampling design.", nameof(profile));
            ValidateSamplingDesign(profile.SamplingDesign);
            bool reconstructedFromSchema2 = profile.SamplingDesign.ReconstructedFromSchema2;
            Dictionary<string, CandidateDescriptor> candidates =
                ValidateResultArray3(
                    profile.CalibrationResults,
                    profile.Scenario,
                    profile.ElementCount,
                    reconstructedFromSchema2);
            if (!HasAbsentAdvantageEnvelopeReferenceShape(profile.AdvantageEnvelope))
                ScientificAdvantageEnvelopeAdapter.ValidateArtifactReference(profile);
            bool holdoutBaselineAbsent =
                IsAbsentOptionalResultShape(profile.HoldoutBaselineResult);
            bool holdoutSelectedAbsent =
                IsAbsentOptionalResultShape(profile.HoldoutSelectedResult);
            if (!holdoutBaselineAbsent)
                ValidateResult3(profile.HoldoutBaselineResult, profile.Scenario);
            if (!holdoutSelectedAbsent)
                ValidateResult3(profile.HoldoutSelectedResult, profile.Scenario);
            if (holdoutBaselineAbsent != holdoutSelectedAbsent)
                throw new ArgumentException("Schema 3 holdout results must be both present or both absent.");

            ValidateDecision3(
                profile.CalibrationDecision,
                DecisionStage.Calibration,
                candidates);
            if (!holdoutBaselineAbsent)
            {
                ValidateMeasurementAssignment(
                    profile.HoldoutBaselineResult,
                    BenchmarkPhase.Holdout,
                    profile.HoldoutElementCount,
                    reconstructedFromSchema2,
                    "holdout baseline");
                ValidateMeasurementAssignment(
                    profile.HoldoutSelectedResult,
                    BenchmarkPhase.Holdout,
                    profile.HoldoutElementCount,
                    reconstructedFromSchema2,
                    "holdout selected");
                if (profile.CalibrationDecision.Status != LayoutSelectionStatus.Optimized)
                {
                    throw new ArgumentException(
                        "Schema-3 holdout evidence requires an optimized frozen calibration winner.");
                }
                ValidateHoldoutIdentity(
                    profile.HoldoutBaselineResult,
                    candidates,
                    profile.CalibrationDecision.BaselineCandidate,
                    "baseline");
                ValidateHoldoutIdentity(
                    profile.HoldoutSelectedResult,
                    candidates,
                    profile.CalibrationDecision.SelectedCandidate,
                    "selected");
            }
            ValidateDecision3(
                profile.FinalDecision,
                holdoutBaselineAbsent
                    ? DecisionStage.Calibration
                    : DecisionStage.HoldoutConfirmation,
                candidates);
        }

        private static void ValidateScenarioIdentity(ScenarioDescriptor scenario)
        {
            if (!ProtocolIdentifier.IsCanonical(scenario.ScenarioId) ||
                scenario.ContractVersion <= 0)
            {
                throw new ArgumentException(
                    "A scenario profile requires a canonical ScenarioId and positive ContractVersion.");
            }
        }

        private static void ValidateSamplingDesign(SamplingDesignDescriptor design)
        {
            if (design.SchemaVersion != SamplingDesignDescriptor.CurrentSchemaVersion)
                throw new ArgumentException($"Unsupported sampling-design schema {design.SchemaVersion}.");
            if (!Enum.IsDefined(typeof(MeasurementOrderKind), design.CandidateOrder) ||
                !Enum.IsDefined(typeof(EvidenceScope), design.EvidenceScope))
            {
                throw new ArgumentException("Sampling-design enum values are invalid.");
            }
            if (string.IsNullOrWhiteSpace(design.PairingUnit))
                throw new ArgumentException("A sampling design requires an explicit pairing unit.");
            if (!design.CalibrationTunesCandidates)
                throw new ArgumentException("A sampling design must declare calibration tuning.");
            if (design.HoldoutRetuningPermitted)
                throw new ArgumentException("Schema 3 does not permit holdout retuning.");
            if (string.IsNullOrWhiteSpace(design.UncertaintyDescription))
                throw new ArgumentException("A sampling design requires an uncertainty description.");
        }

        private static Dictionary<string, CandidateDescriptor> ValidateResultArrayForMigration(
            LayoutBenchmarkResult[] results,
            ScenarioDescriptor scenario,
            int expectedElementCount)
        {
            if (results == null || results.Length == 0)
                throw new ArgumentException("A schema-2 profile requires a non-empty calibration-result array.");
            var candidates = new Dictionary<string, CandidateDescriptor>(StringComparer.Ordinal);
            for (int index = 0; index < results.Length; index++)
            {
                if (results[index] == null)
                    throw new ArgumentException($"Calibration result {index} is null.");
                ValidateResultForMigration(results[index], scenario);
                ValidateMeasurementAssignment(
                    results[index],
                    BenchmarkPhase.Calibration,
                    expectedElementCount,
                    true,
                    $"calibration result {index}");
                CandidateDescriptor candidate = NormalizeCandidateIfPresent(results[index].Candidate);
                if (IsCandidateAbsent(candidate))
                    throw new ArgumentException("A schema-2 calibration result requires a candidate definition.");
                if (candidates.ContainsKey(candidate.CandidateId))
                {
                    throw new ArgumentException(
                        $"Schema 2 contains duplicate candidate identity '{candidate.CandidateId}'.");
                }
                candidates.Add(candidate.CandidateId, candidate);
            }
            return candidates;
        }

        private static void ValidateResultForMigration(
            LayoutBenchmarkResult result,
            ScenarioDescriptor scenario)
        {
            if (result == null)
                return;
            if (!string.IsNullOrEmpty(result.ScenarioId) &&
                (!ProtocolIdentifier.IsCanonical(result.ScenarioId) ||
                 !string.Equals(result.ScenarioId, scenario.ScenarioId, StringComparison.Ordinal)))
            {
                throw new ArgumentException("A result ScenarioId disagrees with its enclosing profile.");
            }
            if (result.ScenarioContractVersion != 0 &&
                result.ScenarioContractVersion != scenario.ContractVersion)
            {
                throw new ArgumentException("A result ContractVersion disagrees with its enclosing profile.");
            }
            if (result.SampleSchemaVersion != LayoutBenchmarkResult.LegacySampleSchemaVersion &&
                result.SampleSchemaVersion != LayoutBenchmarkResult.CurrentSampleSchemaVersion)
            {
                throw new ArgumentException($"Unsupported sample schema {result.SampleSchemaVersion}.");
            }

            NormalizeCandidateIfPresent(result.Candidate);
            if (result.SampleSchemaVersion == LayoutBenchmarkResult.CurrentSampleSchemaVersion &&
                !HasLegacySchema2MetadataShape(result))
            {
                ValidateResultMetadata(result);
            }
            else
            {
                ValidateOptionalLegacySeriesMetadata(
                    result.ResidentSamplesMillisecondsPerTick,
                    result.ResidentBlockIds,
                    result.ResidentOrderPositions,
                    "resident");
                ValidateOptionalLegacySeriesMetadata(
                    result.IngressSamplesMilliseconds,
                    result.IngressBlockIds,
                    result.IngressOrderPositions,
                    "ingress");
                ValidateOptionalLegacySeriesMetadata(
                    result.ExportSamplesMilliseconds,
                    result.ExportBlockIds,
                    result.ExportOrderPositions,
                    "export");
            }
        }

        private static void MigrateResult2(
            LayoutBenchmarkResult result,
            ScenarioDescriptor scenario)
        {
            if (result == null)
                return;

            if (string.IsNullOrEmpty(result.ScenarioId))
                result.ScenarioId = scenario.ScenarioId;
            if (result.ScenarioContractVersion == 0)
                result.ScenarioContractVersion = scenario.ContractVersion;
            result.Candidate = NormalizeCandidateIfPresent(result.Candidate);

            if (result.SampleSchemaVersion == LayoutBenchmarkResult.LegacySampleSchemaVersion ||
                HasLegacySchema2MetadataShape(result))
            {
                result.ResidentBlockIds = MigrateLegacyBlockIds(
                    result.ResidentBlockIds,
                    result.ResidentSamplesMillisecondsPerTick,
                    "resident");
                result.IngressBlockIds = MigrateLegacyBlockIds(
                    result.IngressBlockIds,
                    result.IngressSamplesMilliseconds,
                    "ingress");
                result.ExportBlockIds = MigrateLegacyBlockIds(
                    result.ExportBlockIds,
                    result.ExportSamplesMilliseconds,
                    "export");
                result.ResidentOrderPositions = MigrateLegacyOrderPositions(
                    result.ResidentOrderPositions,
                    result.ResidentSamplesMillisecondsPerTick,
                    "resident");
                result.IngressOrderPositions = MigrateLegacyOrderPositions(
                    result.IngressOrderPositions,
                    result.IngressSamplesMilliseconds,
                    "ingress");
                result.ExportOrderPositions = MigrateLegacyOrderPositions(
                    result.ExportOrderPositions,
                    result.ExportSamplesMilliseconds,
                    "export");
                result.SampleSchemaVersion = LayoutBenchmarkResult.CurrentSampleSchemaVersion;
            }
        }

        private static bool HasLegacySchema2MetadataShape(LayoutBenchmarkResult result)
        {
            // JsonUtility applies the current field initializer when a historical
            // schema-2 JSON object has no SampleSchemaVersion member. The complete
            // absence of all six metadata arrays is the unambiguous legacy shape;
            // partially present schema-1 metadata remains an error.
            return result.ResidentBlockIds == null &&
                   result.IngressBlockIds == null &&
                   result.ExportBlockIds == null &&
                   result.ResidentOrderPositions == null &&
                   result.IngressOrderPositions == null &&
                   result.ExportOrderPositions == null;
        }

        private static Dictionary<string, CandidateDescriptor> ValidateResultArray3(
            LayoutBenchmarkResult[] results,
            ScenarioDescriptor scenario,
            int expectedElementCount,
            bool reconstructedFromSchema2)
        {
            if (results == null || results.Length == 0)
                throw new ArgumentException("Schema 3 requires a non-empty calibration-result array.");
            var candidates = new Dictionary<string, CandidateDescriptor>(StringComparer.Ordinal);
            for (int index = 0; index < results.Length; index++)
            {
                ValidateResult3(results[index], scenario);
                ValidateMeasurementAssignment(
                    results[index],
                    BenchmarkPhase.Calibration,
                    expectedElementCount,
                    reconstructedFromSchema2,
                    $"calibration result {index}");
                CandidateDescriptor candidate = results[index].Candidate;
                if (candidates.ContainsKey(candidate.CandidateId))
                {
                    throw new ArgumentException(
                        $"Schema 3 contains duplicate candidate identity '{candidate.CandidateId}'.");
                }
                candidates.Add(candidate.CandidateId, candidate);
            }
            return candidates;
        }

        private static void ValidateHoldoutIdentity(
            LayoutBenchmarkResult result,
            Dictionary<string, CandidateDescriptor> candidates,
            CandidateDescriptor frozenCandidate,
            string role)
        {
            if (result == null)
                return;
            ValidateHoldoutCandidateIdentity(
                result.Candidate,
                candidates,
                frozenCandidate,
                role);
        }

        private static void ValidateHoldoutCandidateIdentity(
            CandidateDescriptor candidate,
            Dictionary<string, CandidateDescriptor> candidates,
            CandidateDescriptor frozenCandidate,
            string role)
        {
            if (!candidates.TryGetValue(candidate.CandidateId, out CandidateDescriptor expected) ||
                candidate != expected ||
                IsCandidateAbsent(frozenCandidate) ||
                candidate != frozenCandidate)
            {
                throw new ArgumentException(
                    $"The schema-3 holdout {role} candidate disagrees with the frozen calibration identity.");
            }
        }

        private static void ValidateMeasurementAssignment(
            LayoutBenchmarkResult result,
            BenchmarkPhase expectedPhase,
            int expectedElementCount,
            bool reconstructedFromSchema2,
            string context)
        {
            if (!Enum.IsDefined(typeof(BenchmarkPhase), result.Phase))
                throw new ArgumentException($"The {context} has an unknown benchmark phase.");
            if (result.Phase != expectedPhase &&
                !(reconstructedFromSchema2 &&
                  expectedPhase == BenchmarkPhase.Holdout &&
                  result.Phase == default))
            {
                throw new ArgumentException($"The {context} has the wrong benchmark phase.");
            }
            if (expectedElementCount < 0 || result.ElementCount < 0)
                throw new ArgumentException($"The {context} has a negative element count.");
            if (expectedElementCount == 0 || result.ElementCount == 0)
            {
                if (!reconstructedFromSchema2)
                {
                    throw new ArgumentException(
                        $"The {context} requires a positive matching element count.");
                }
                return;
            }
            if (result.ElementCount != expectedElementCount)
                throw new ArgumentException($"The {context} element count disagrees with its profile.");
        }

        private static void ValidateResult3(
            LayoutBenchmarkResult result,
            ScenarioDescriptor scenario)
        {
            if (result == null)
                throw new ArgumentException("A schema-3 calibration result is null.");
            if (result.SampleSchemaVersion != LayoutBenchmarkResult.CurrentSampleSchemaVersion)
                throw new ArgumentException($"Unsupported sample schema {result.SampleSchemaVersion}.");
            if (!ProtocolIdentifier.IsCanonical(result.ScenarioId) ||
                !string.Equals(result.ScenarioId, scenario.ScenarioId, StringComparison.Ordinal))
            {
                throw new ArgumentException("A result ScenarioId disagrees with its enclosing profile.");
            }
            if (result.ScenarioContractVersion != scenario.ContractVersion)
                throw new ArgumentException("A result ContractVersion disagrees with its enclosing profile.");
            if (IsCandidateAbsent(result.Candidate))
                throw new ArgumentException("A schema-3 result requires a candidate definition.");
            ValidateCurrentCandidate(result.Candidate, "schema-3 result");
            ValidateResultMetadata(result);
        }

        private static void ValidateResultMetadata(LayoutBenchmarkResult result)
        {
            ValidateSeriesMetadata(
                result.ResidentSamplesMillisecondsPerTick,
                result.ResidentBlockIds,
                result.ResidentOrderPositions,
                "resident");
            ValidateSeriesMetadata(
                result.IngressSamplesMilliseconds,
                result.IngressBlockIds,
                result.IngressOrderPositions,
                "ingress");
            ValidateSeriesMetadata(
                result.ExportSamplesMilliseconds,
                result.ExportBlockIds,
                result.ExportOrderPositions,
                "export");
        }

        private static void ValidateOptionalLegacySeriesMetadata(
            double[] samples,
            int[] blockIds,
            int[] orderPositions,
            string component)
        {
            if (samples == null)
            {
                if (blockIds != null || orderPositions != null)
                    throw new ArgumentException($"Legacy {component} metadata has no sample array.");
                return;
            }
            if (blockIds != null)
                ValidateBlockIds(blockIds, samples.Length, component);
            if (orderPositions != null)
                ValidateOrderPositions(orderPositions, samples.Length, component);
        }

        private static void ValidateSeriesMetadata(
            double[] samples,
            int[] blockIds,
            int[] orderPositions,
            string component)
        {
            if (samples == null)
            {
                if (blockIds != null || orderPositions != null)
                    throw new ArgumentException($"Schema-3 {component} metadata has no sample array.");
                return;
            }
            if (blockIds == null || orderPositions == null)
                throw new ArgumentException($"Schema 3 requires {component} block and order metadata.");
            ValidateBlockIds(blockIds, samples.Length, component);
            ValidateOrderPositions(orderPositions, samples.Length, component);
        }

        private static void ValidateBlockIds(int[] blockIds, int sampleCount, string component)
        {
            if (blockIds.Length != sampleCount)
                throw new ArgumentException($"The {component} block-ID length does not match its samples.");
            var seen = new HashSet<int>();
            for (int index = 0; index < blockIds.Length; index++)
            {
                if (blockIds[index] < 0 || !seen.Add(blockIds[index]))
                    throw new ArgumentException($"The {component} block IDs are invalid or duplicated.");
            }
        }

        private static void ValidateOrderPositions(
            int[] orderPositions,
            int sampleCount,
            string component)
        {
            if (orderPositions.Length != sampleCount)
                throw new ArgumentException($"The {component} order-position length does not match its samples.");
            for (int index = 0; index < orderPositions.Length; index++)
            {
                if (orderPositions[index] < -1)
                    throw new ArgumentException($"The {component} order positions are invalid.");
            }
        }

        private static int[] MigrateLegacyBlockIds(
            int[] existing,
            double[] samples,
            string component)
        {
            if (samples == null)
                return existing;
            if (existing != null)
            {
                ValidateBlockIds(existing, samples.Length, component);
                return existing;
            }

            var blockIds = new int[samples.Length];
            for (int index = 0; index < blockIds.Length; index++)
                blockIds[index] = index;
            return blockIds;
        }

        private static int[] MigrateLegacyOrderPositions(
            int[] existing,
            double[] samples,
            string component)
        {
            if (samples == null)
                return existing;
            if (existing != null)
            {
                ValidateOrderPositions(existing, samples.Length, component);
                return existing;
            }

            var positions = new int[samples.Length];
            for (int index = 0; index < positions.Length; index++)
                positions[index] = -1;
            return positions;
        }

        private static void ValidateDecisionForMigration(LayoutSelectionDecision decision)
        {
            if (!Enum.IsDefined(typeof(LayoutSelectionStatus), decision.Status))
                throw new ArgumentException("A schema-2 decision has an unknown status.");
            NormalizeCandidateIfPresent(decision.BaselineCandidate);
            NormalizeCandidateIfPresent(decision.SelectedCandidate);
            NormalizeCandidateIfPresent(decision.BestMeasuredCandidate);
            BootstrapConfidenceInterval interval = decision.ImprovementConfidenceInterval;
            if (interval.Iterations <= 0)
            {
                ValidateBootstrapInterval(interval);
                return;
            }
            if (interval.SchemaVersion == 0)
            {
                ValidateLegacyPercentBounds(interval);
                return;
            }
            ValidateBootstrapInterval(interval);
            if (interval.EstimatorKind != BootstrapEstimatorKind.LegacyIndependentPercent)
                throw new ArgumentException("Schema-2 intervals must retain the legacy estimator marker.");
        }

        private static void ValidateLegacyPercentBounds(BootstrapConfidenceInterval interval)
        {
            if (interval.Iterations < 100 ||
                !(interval.ConfidenceLevel > 0d && interval.ConfidenceLevel < 1d) ||
                !IsFinite(interval.PointEstimatePercent) ||
                !IsFinite(interval.LowerBoundPercent) ||
                !IsFinite(interval.UpperBoundPercent) ||
                interval.LowerBoundPercent > interval.UpperBoundPercent)
            {
                throw new ArgumentException("Legacy bootstrap percent bounds are invalid.");
            }
        }

        private static LayoutSelectionDecision MigrateDecision2(
            LayoutSelectionDecision decision,
            DecisionStage stage)
        {
            decision.DecisionStage = stage;
            decision.BaselineCandidate = NormalizeCandidateIfPresent(decision.BaselineCandidate);
            decision.SelectedCandidate = NormalizeCandidateIfPresent(decision.SelectedCandidate);
            decision.BestMeasuredCandidate = NormalizeCandidateIfPresent(decision.BestMeasuredCandidate);
            if (string.IsNullOrWhiteSpace(decision.MultiplicityControl))
            {
                decision.MultiplicityControl =
                    "Calibration winner selection followed by confirmation on an untouched holdout dataset.";
            }

            BootstrapConfidenceInterval interval = decision.ImprovementConfidenceInterval;
            if (interval.Iterations > 0 && interval.SchemaVersion == 0)
            {
                interval.SchemaVersion = BootstrapConfidenceInterval.CurrentSchemaVersion;
                interval.EstimatorKind = BootstrapEstimatorKind.LegacyIndependentPercent;
                interval.HasLogRatioEstimate = false;
                interval.Estimand = "schema2 independent composite-P95 improvement percent";
                interval.ResamplingUnit = "independent candidate sample arrays";
                interval.RandomSeed = 0u;
                interval.PointEstimateLogRatio = 0d;
                interval.LowerBoundLogRatio = 0d;
                interval.UpperBoundLogRatio = 0d;
                decision.ImprovementConfidenceInterval = interval;
            }

            if (decision.Status != LayoutSelectionStatus.Optimized)
                decision.SelectionRegretPercent = Math.Max(0d, decision.ImprovementPercent);
            return decision;
        }

        private static void ValidateDecision3(
            LayoutSelectionDecision decision,
            DecisionStage expectedStage,
            Dictionary<string, CandidateDescriptor> candidates)
        {
            if (decision.DecisionStage != expectedStage)
                throw new ArgumentException("A schema-3 decision has the wrong decision stage.");
            if (!Enum.IsDefined(typeof(LayoutSelectionStatus), decision.Status))
                throw new ArgumentException("A schema-3 decision has an unknown status.");
            bool baselineAbsent = IsCandidateAbsent(decision.BaselineCandidate);
            bool selectedAbsent = IsCandidateAbsent(decision.SelectedCandidate);
            bool bestAbsent = IsCandidateAbsent(decision.BestMeasuredCandidate);
            if (decision.Status != LayoutSelectionStatus.Invalid &&
                (baselineAbsent || selectedAbsent || bestAbsent))
            {
                throw new ArgumentException(
                    "A non-invalid schema-3 decision requires baseline, selected, and best candidate identities.");
            }

            ValidateDecisionCandidate(
                decision.BaselineCandidate,
                candidates,
                "baseline");
            ValidateDecisionCandidate(
                decision.SelectedCandidate,
                candidates,
                "selected");
            ValidateDecisionCandidate(
                decision.BestMeasuredCandidate,
                candidates,
                "best measured");
            if (!baselineAbsent && !decision.BaselineCandidate.IsBaseline)
                throw new ArgumentException("A schema-3 decision baseline is not marked as the baseline.");
            if (decision.Status == LayoutSelectionStatus.Optimized &&
                decision.SelectedCandidate != decision.BestMeasuredCandidate)
            {
                throw new ArgumentException(
                    "An optimized schema-3 decision must select its best measured candidate.");
            }
            if (decision.Status != LayoutSelectionStatus.Invalid &&
                decision.Status != LayoutSelectionStatus.Optimized &&
                decision.SelectedCandidate != decision.BaselineCandidate)
            {
                throw new ArgumentException(
                    "A fallback schema-3 decision must retain its baseline candidate.");
            }
            ValidateBootstrapInterval(decision.ImprovementConfidenceInterval);
        }

        private static void ValidateDecisionCandidate(
            CandidateDescriptor candidate,
            Dictionary<string, CandidateDescriptor> candidates,
            string role)
        {
            if (IsCandidateAbsent(candidate))
                return;
            ValidateCurrentCandidate(candidate, $"schema-3 decision {role}");
            if (!candidates.TryGetValue(candidate.CandidateId, out CandidateDescriptor expected) ||
                candidate != expected)
            {
                throw new ArgumentException(
                    $"The schema-3 decision {role} candidate disagrees with CalibrationResults.");
            }
        }

        private static void ValidateBootstrapInterval(BootstrapConfidenceInterval interval)
        {
            if (interval.Iterations <= 0)
            {
                if (interval.Iterations != 0 ||
                    interval.SchemaVersion != 0 ||
                    interval.EstimatorKind != BootstrapEstimatorKind.Unspecified ||
                    interval.HasLogRatioEstimate ||
                    interval.ConfidenceLevel != 0d ||
                    interval.RandomSeed != 0u ||
                    !string.IsNullOrEmpty(interval.Estimand) ||
                    !string.IsNullOrEmpty(interval.ResamplingUnit) ||
                    interval.PointEstimateLogRatio != 0d ||
                    interval.LowerBoundLogRatio != 0d ||
                    interval.UpperBoundLogRatio != 0d ||
                    interval.PointEstimatePercent != 0d ||
                    interval.LowerBoundPercent != 0d ||
                    interval.UpperBoundPercent != 0d)
                {
                    throw new ArgumentException(
                        "An absent confidence interval must not declare realized estimator metadata.");
                }
                return;
            }
            if (interval.SchemaVersion != BootstrapConfidenceInterval.CurrentSchemaVersion)
                throw new ArgumentException($"Unsupported bootstrap schema {interval.SchemaVersion}.");
            if (interval.Iterations < 100 ||
                !(interval.ConfidenceLevel > 0d && interval.ConfidenceLevel < 1d) ||
                string.IsNullOrWhiteSpace(interval.Estimand) ||
                string.IsNullOrWhiteSpace(interval.ResamplingUnit) ||
                !IsFinite(interval.PointEstimatePercent) ||
                !IsFinite(interval.LowerBoundPercent) ||
                !IsFinite(interval.UpperBoundPercent) ||
                interval.LowerBoundPercent > interval.UpperBoundPercent)
            {
                throw new ArgumentException("Bootstrap interval metadata or percent bounds are invalid.");
            }

            switch (interval.EstimatorKind)
            {
                case BootstrapEstimatorKind.LegacyIndependentPercent:
                    if (interval.HasLogRatioEstimate ||
                        interval.RandomSeed != 0u ||
                        interval.PointEstimateLogRatio != 0d ||
                        interval.LowerBoundLogRatio != 0d ||
                        interval.UpperBoundLogRatio != 0d)
                    {
                        throw new ArgumentException(
                            "A legacy percent interval must not expose realized log-ratio values.");
                    }
                    break;

                case BootstrapEstimatorKind.PairedBlockLogRatio:
                case BootstrapEstimatorKind.ProcessHierarchicalLogRatio:
                    if (!interval.HasLogRatioEstimate ||
                        interval.RandomSeed == 0u ||
                        !IsFinite(interval.PointEstimateLogRatio) ||
                        !IsFinite(interval.LowerBoundLogRatio) ||
                        !IsFinite(interval.UpperBoundLogRatio) ||
                        interval.LowerBoundLogRatio > interval.UpperBoundLogRatio)
                    {
                        throw new ArgumentException("A log-ratio interval has invalid estimator metadata.");
                    }
                    if (!ApproximatelyEqual(
                            interval.PointEstimatePercent,
                            LogRatioToImprovementPercent(interval.PointEstimateLogRatio)) ||
                        !ApproximatelyEqual(
                            interval.LowerBoundPercent,
                            LogRatioToImprovementPercent(interval.UpperBoundLogRatio)) ||
                        !ApproximatelyEqual(
                            interval.UpperBoundPercent,
                            LogRatioToImprovementPercent(interval.LowerBoundLogRatio)))
                    {
                        throw new ArgumentException(
                            "A log-ratio interval has inconsistent percentage transforms.");
                    }
                    break;

                default:
                    throw new ArgumentException(
                        $"Unsupported bootstrap estimator kind {interval.EstimatorKind}.");
            }
        }

        private static CandidateDescriptor NormalizeCandidateIfPresent(CandidateDescriptor candidate)
        {
            if (IsCandidateAbsent(candidate))
                return candidate;
            try
            {
                return candidate.NormalizePolicies();
            }
            catch (InvalidOperationException exception)
            {
                throw new ArgumentException(
                    "A candidate has an unsupported or internally inconsistent policy schema.",
                    nameof(candidate),
                    exception);
            }
        }

        private static void ValidateCurrentCandidate(
            CandidateDescriptor candidate,
            string context)
        {
            try
            {
                candidate.ValidateFactorConsistency();
            }
            catch (InvalidOperationException exception)
            {
                throw new ArgumentException(
                    $"The {context} candidate has an unsupported or internally inconsistent policy schema.",
                    nameof(candidate),
                    exception);
            }
        }

        private static bool IsCandidateAbsent(CandidateDescriptor candidate)
        {
            return candidate.PolicySchemaVersion == CandidateDescriptor.LegacyPolicySchemaVersion &&
                   string.IsNullOrEmpty(candidate.CandidateId) &&
                   string.IsNullOrEmpty(candidate.LayoutId) &&
                   candidate.LogicalBatchSize == 0 &&
                   string.IsNullOrEmpty(candidate.Layout.PolicyId) &&
                   string.IsNullOrEmpty(candidate.Kernel.PolicyId) &&
                   string.IsNullOrEmpty(candidate.Batch.PolicyId) &&
                   string.IsNullOrEmpty(candidate.Execution.PolicyId);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double LogRatioToImprovementPercent(double logRatio)
        {
            return (1d - Math.Exp(logRatio)) * 100d;
        }

        private static bool ApproximatelyEqual(double left, double right)
        {
            if (!IsFinite(left) || !IsFinite(right))
                return false;
            double scale = Math.Max(1d, Math.Max(Math.Abs(left), Math.Abs(right)));
            return Math.Abs(left - right) <= 1e-9d * scale;
        }
    }
}
