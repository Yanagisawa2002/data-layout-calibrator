using System;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>
    /// Proposed additive schema transition for the scientific-core branch. Migration
    /// is in-memory only: callers choose where to write upgraded data, and checked-in
    /// schema-2 evidence is never rewritten by this API.
    /// </summary>
    public static class CalibrationProfileMigration
    {
        public const int LegacySchemaVersion = 2;
        public const int ProposedSchemaVersion = 3;

        public static CalibrationSuiteProfile UpgradeInMemory(CalibrationSuiteProfile suite)
        {
            if (suite == null)
                throw new ArgumentNullException(nameof(suite));
            if (suite.SchemaVersion != LegacySchemaVersion &&
                suite.SchemaVersion != ProposedSchemaVersion)
            {
                throw new ArgumentException(
                    $"Unsupported suite schema {suite.SchemaVersion}.",
                    nameof(suite));
            }

            bool migratedFromSchema2 = suite.SchemaVersion == LegacySchemaVersion;
            if (suite.Scenarios != null)
            {
                for (int index = 0; index < suite.Scenarios.Length; index++)
                {
                    ScenarioCalibrationProfile scenario = suite.Scenarios[index];
                    UpgradeScenarioInMemory(
                        scenario,
                        migratedFromSchema2 ||
                        (scenario != null && scenario.SchemaVersion == LegacySchemaVersion));
                }
            }

            suite.SchemaVersion = ProposedSchemaVersion;
            return suite;
        }

        public static ScenarioCalibrationProfile UpgradeInMemory(ScenarioCalibrationProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (profile.SchemaVersion != LegacySchemaVersion &&
                profile.SchemaVersion != ProposedSchemaVersion)
            {
                throw new ArgumentException(
                    $"Unsupported scenario profile schema {profile.SchemaVersion}.",
                    nameof(profile));
            }

            bool migratedFromSchema2 = profile.SchemaVersion == LegacySchemaVersion;
            UpgradeScenarioInMemory(profile, migratedFromSchema2);
            return profile;
        }

        private static void UpgradeScenarioInMemory(
            ScenarioCalibrationProfile profile,
            bool migratedFromSchema2)
        {
            if (profile == null)
                throw new ArgumentException("A suite contains a null scenario profile.");
            if (string.IsNullOrWhiteSpace(profile.Scenario.ScenarioId) ||
                profile.Scenario.ContractVersion <= 0)
            {
                throw new ArgumentException(
                    "A scenario profile requires a stable ScenarioId and positive ContractVersion.",
                    nameof(profile));
            }

            if (profile.CalibrationResults != null)
            {
                for (int index = 0; index < profile.CalibrationResults.Length; index++)
                    UpgradeResult(profile.CalibrationResults[index], profile.Scenario);
            }
            UpgradeResult(profile.HoldoutBaselineResult, profile.Scenario);
            UpgradeResult(profile.HoldoutSelectedResult, profile.Scenario);
            profile.CalibrationDecision = UpgradeDecision(
                profile.CalibrationDecision,
                DecisionStage.Calibration,
                migratedFromSchema2);
            profile.FinalDecision = UpgradeDecision(
                profile.FinalDecision,
                profile.HoldoutBaselineResult == null
                    ? DecisionStage.Calibration
                    : DecisionStage.HoldoutConfirmation,
                migratedFromSchema2);

            if (profile.SamplingDesign == null)
            {
                profile.SamplingDesign = new SamplingDesignDescriptor
                {
                    CandidateOrder = MeasurementOrderKind.RandomizedBlocked,
                    PairingUnit = migratedFromSchema2
                        ? "implicit schema-2 sample-array index (reconstructed in memory)"
                        : "complete measurement block",
                    EvidenceScope = EvidenceScope.SinglePlayer,
                    CalibrationTunesCandidates = true,
                    HoldoutRetuningPermitted = false,
                    UncertaintyDescription = migratedFromSchema2
                        ? "Historical schema-2 confidence intervals used independent resampling. Reconstructed block IDs enable future paired replay, but do not retroactively change the stored interval."
                        : "Paired block uncertainty within one Player process.",
                };
            }

            profile.SchemaVersion = ProposedSchemaVersion;
        }

        private static void UpgradeResult(
            LayoutBenchmarkResult result,
            ScenarioDescriptor scenario)
        {
            if (result == null)
                return;

            if (string.IsNullOrWhiteSpace(result.ScenarioId))
                result.ScenarioId = scenario.ScenarioId;
            else if (!string.Equals(result.ScenarioId, scenario.ScenarioId, StringComparison.Ordinal))
                throw new ArgumentException("A result ScenarioId disagrees with its enclosing profile.");
            if (result.ScenarioContractVersion == 0)
                result.ScenarioContractVersion = scenario.ContractVersion;
            else if (result.ScenarioContractVersion != scenario.ContractVersion)
                throw new ArgumentException("A result ContractVersion disagrees with its enclosing profile.");

            result.SampleSchemaVersion = 1;
            result.Candidate = NormalizeIfPresent(result.Candidate);
            result.ResidentBlockIds = EnsureSequentialBlockIds(
                result.ResidentBlockIds,
                result.ResidentSamplesMillisecondsPerTick);
            result.IngressBlockIds = EnsureSequentialBlockIds(
                result.IngressBlockIds,
                result.IngressSamplesMilliseconds);
            result.ExportBlockIds = EnsureSequentialBlockIds(
                result.ExportBlockIds,
                result.ExportSamplesMilliseconds);
            result.ResidentOrderPositions = EnsureUnknownPositions(
                result.ResidentOrderPositions,
                result.ResidentSamplesMillisecondsPerTick);
            result.IngressOrderPositions = EnsureUnknownPositions(
                result.IngressOrderPositions,
                result.IngressSamplesMilliseconds);
            result.ExportOrderPositions = EnsureUnknownPositions(
                result.ExportOrderPositions,
                result.ExportSamplesMilliseconds);
        }

        private static LayoutSelectionDecision UpgradeDecision(
            LayoutSelectionDecision decision,
            DecisionStage stage,
            bool migratedFromSchema2)
        {
            decision.DecisionStage = stage;
            decision.BaselineCandidate = NormalizeIfPresent(decision.BaselineCandidate);
            decision.SelectedCandidate = NormalizeIfPresent(decision.SelectedCandidate);
            decision.BestMeasuredCandidate = NormalizeIfPresent(decision.BestMeasuredCandidate);
            if (string.IsNullOrWhiteSpace(decision.MultiplicityControl))
            {
                decision.MultiplicityControl =
                    "Calibration winner selection followed by confirmation on an untouched holdout dataset.";
            }

            BootstrapConfidenceInterval interval = decision.ImprovementConfidenceInterval;
            if (interval.Iterations > 0 && interval.SchemaVersion == 0)
            {
                interval.SchemaVersion = 1;
                if (migratedFromSchema2)
                {
                    interval.Estimand = "schema2 independent composite-P95 improvement percent";
                    interval.ResamplingUnit = "independent candidate sample arrays";
                    interval.RandomSeed = 0u;
                }
                decision.ImprovementConfidenceInterval = interval;
            }

            if (decision.Status != LayoutSelectionStatus.Optimized)
                decision.SelectionRegretPercent = Math.Max(0d, decision.ImprovementPercent);
            return decision;
        }

        private static CandidateDescriptor NormalizeIfPresent(CandidateDescriptor candidate)
        {
            return string.IsNullOrWhiteSpace(candidate.CandidateId) || candidate.LogicalBatchSize <= 0
                ? candidate
                : candidate.NormalizePolicies();
        }

        private static int[] EnsureSequentialBlockIds(int[] existing, double[] samples)
        {
            if (samples == null)
                return existing;
            if (existing != null && existing.Length == samples.Length)
                return existing;

            var blockIds = new int[samples.Length];
            for (int index = 0; index < blockIds.Length; index++)
                blockIds[index] = index;
            return blockIds;
        }

        private static int[] EnsureUnknownPositions(int[] existing, double[] samples)
        {
            if (samples == null)
                return existing;
            if (existing != null && existing.Length == samples.Length)
                return existing;

            var positions = new int[samples.Length];
            for (int index = 0; index < positions.Length; index++)
                positions[index] = -1;
            return positions;
        }
    }
}
