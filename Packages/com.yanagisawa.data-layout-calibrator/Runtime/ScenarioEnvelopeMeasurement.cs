using System;
using System.Collections.Generic;


namespace Yanagisawa.DataLayoutCalibrator
{
    public interface IEnvelopeArtifactCodec
    {
        string Serialize<T>(T value, bool pretty = false);
        T Deserialize<T>(string json);
    }

    [Serializable]
    public sealed class EnvelopeRawPhase
    {
        public string ArtifactId;
        public string ManagedAllocationMeasurement;
        public string DatasetHash;
        public int TicksPerBlock;
        public int WarmupBlocks;
        public LayoutBenchmarkResult[] Results;
    }

    [Serializable]
    public sealed class EnvelopeCellMeasurement
    {
        public CalibrationRunSettings Settings;
        public AdvantageEnvelopeAxis Axis;
        public EnvelopeRawPhase Calibration;
        public EnvelopeRawPhase Holdout;
        public AdvantageEnvelopeCalibration FrozenCalibration;
        public AdvantageEnvelopeProfile Envelope;
        public string FullMeasuredCandidateSetSha256;
    }

    public static partial class ScenarioCalibrationEngine
    {
        /// <summary>
        /// Measures a declared cell with the scientific engine. The synchronous writer must
        /// persist calibration before holdout starts; its exception aborts confirmation.
        /// A new seed, same cell dimensions, and fresh samples form the held-out partition.
        /// </summary>
        public static EnvelopeCellMeasurement RunEnvelopeCell(
            ICalibrationScenarioFactory factory, CalibrationRunSettings settings,
            AdvantageEnvelopeAxis axis, string environmentFingerprint, string artifactId,
            IEnvelopeArtifactCodec codec, Action<string, string> writeImmutableArtifact)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (codec == null) throw new ArgumentNullException(nameof(codec));
            if (writeImmutableArtifact == null) throw new ArgumentNullException(nameof(writeImmutableArtifact));
            ValidateSettings(settings);
            ProtocolIdentifier.RequireCanonical(artifactId, nameof(artifactId), "Artifact ID");
            if (!CandidateDefinitionProtocol.IsCanonicalSha256(environmentFingerprint))
                throw new ArgumentException("An exact environment fingerprint is required.");
            if (settings.ElementCount != axis.ElementCount || settings.HoldoutElementCount != axis.ElementCount ||
                settings.LifetimeTicks != axis.LifetimeTicks || settings.CalibrationSeed == settings.HoldoutSeed)
                throw new ArgumentException("Calibration and fresh-seed holdout must measure the same declared cell.");
            if (settings.SamplesPerCandidate < 40 || settings.BoundarySamplesPerCandidate < 20 ||
                settings.BootstrapIterations < 4000)
                throw new ArgumentException("Envelope evidence requires 40 resident, 20 boundary and 4000 bootstrap samples.");

            // Snapshot caller-owned mutable settings before any callback or work.
            IManagedAllocationCounter allocationCounter = settings.AllocationCounter;
            settings = codec.Deserialize<CalibrationRunSettings>(codec.Serialize(settings));
            settings.AllocationCounter = allocationCounter;
            allocationCounter.Validate();
            string settingsHash = EnvelopeHash(codec.Serialize(settings));
            RunPreflight(factory, settings);
            PhaseMeasurement measured = MeasurePhase(factory, settings, BenchmarkPhase.Calibration,
                settings.ElementCount, settings.CalibrationSeed, null, 0, 0, settings.CandidateOrderSeed);
            EnvelopeRawPhase raw = EnvelopeRaw(artifactId + "-calibration", measured, allocationCounter.Identity);
            string rawJson = codec.Serialize(raw, true);
            writeImmutableArtifact(raw.ArtifactId, rawJson);

            // Tune the AoS reference using calibration only, retaining all controls in raw evidence.
            LayoutSelectionDecision tuned = LayoutSelector.SelectCalibration(measured.Results,
                measured.Results.Length, settings.MinimumImprovementPercent, settings.BootstrapIterations,
                settings.BootstrapConfidenceLevel, settings.BootstrapSeed);
            var allBindings = BindEnvelope(raw, codec);
            var bindings = new List<ScientificEvidenceBinding>();
            foreach (ScientificEvidenceBinding binding in allBindings)
                if (!binding.Result.Candidate.IsBaseline ||
                    binding.Result.Candidate.CandidateId == tuned.BaselineCandidate.CandidateId)
                    bindings.Add(binding);
            string fullSetHash = ScientificAdvantageEnvelopeAdapter.ComputeCandidateSetSha256(allBindings);
            var request = new AdvantageEnvelopeCalibrationRequest
            {
                EnvelopeId = artifactId, CreatedUtcIso8601 = DateTime.UtcNow.ToString("O"),
                ScenarioId = factory.Descriptor.ScenarioId, ContractVersion = factory.Descriptor.ContractVersion,
                CandidateSetHash = fullSetHash,
                MeasurementSchemaHash = ScientificAdvantageEnvelopeAdapter.MeasurementSchemaSha256,
                EnvironmentFingerprint = environmentFingerprint, CalibrationSettingsHash = settingsHash,
                SourceArtifactId = raw.ArtifactId, SourceArtifactSha256 = EnvelopeHash(rawJson),
                EvidenceScope = "single-player-measured-cell",
                CalibrationUncertaintyMethod = ScientificAdvantageEnvelopeAdapter.PairedBlockUncertaintyMethod,
                Policy = new AdvantageEnvelopePolicy
                {
                    MinimumImprovementPercent = settings.MinimumImprovementPercent,
                    ConfidenceLevel = settings.BootstrapConfidenceLevel,
                    MinimumBootstrapReplicates = settings.BootstrapIterations,
                },
                Cells = new[] { ScientificAdvantageEnvelopeAdapter.CreateCalibrationCell(axis,
                    bindings.ToArray(), settings.BootstrapIterations, settings.BootstrapSeed) },
            };
            AdvantageEnvelopeCalibration frozen = AdvantageEnvelopeEngine.Calibrate(request);
            string frozenJson = codec.Serialize(frozen, true);
            writeImmutableArtifact(artifactId + "-frozen", frozenJson);
            // Callbacks cannot change the internal freeze; confirmation rehydrates the exact persisted bytes.
            frozen = codec.Deserialize<AdvantageEnvelopeCalibration>(frozenJson);
            var holdoutCells = new List<AdvantageEnvelopeHoldoutCellInput>();
            var holdoutRaw = new EnvelopeRawPhase { ArtifactId = artifactId + "-holdout",
                DatasetHash = "not-measured-no-calibration-advantage", Results = new LayoutBenchmarkResult[0] };
            EnvelopeCalibrationCellDecision cell = frozen.Cells[0];
            if (cell.CalibrationStatus == EnvelopeCellStatus.CredibleAdvantage)
            {
                var selected = new CandidateDescriptor[2];
                foreach (LayoutBenchmarkResult result in measured.Results)
                {
                    if (result.Candidate.CandidateId == cell.BaselineCandidateId) selected[0] = result.Candidate;
                    if (result.Candidate.CandidateId == cell.FrozenCalibrationWinnerCandidateId) selected[1] = result.Candidate;
                }
                PhaseMeasurement held = MeasurePhase(factory, settings, BenchmarkPhase.Holdout,
                    settings.HoldoutElementCount, settings.HoldoutSeed, selected, measured.TicksPerBlock,
                    measured.WarmupBlocks, settings.CandidateOrderSeed ^ 0x9E3779B9u);
                holdoutRaw = EnvelopeRaw(holdoutRaw.ArtifactId, held, allocationCounter.Identity);
                ScientificEvidenceBinding[] heldBindings = BindEnvelope(holdoutRaw, codec);
                holdoutCells.Add(ScientificAdvantageEnvelopeAdapter.CreateHoldoutCell(axis,
                    heldBindings[0], heldBindings[1], settings.BootstrapIterations, settings.BootstrapSeed ^ 0x68E31DA4u));
            }
            string holdoutJson = codec.Serialize(holdoutRaw, true);
            writeImmutableArtifact(holdoutRaw.ArtifactId, holdoutJson);
            AdvantageEnvelopeProfile envelope = AdvantageEnvelopeEngine.ConfirmHoldout(frozen,
                new AdvantageEnvelopeHoldoutRequest
                {
                    SourceArtifactId = holdoutRaw.ArtifactId, SourceArtifactSha256 = EnvelopeHash(holdoutJson),
                    CandidateSetHash = fullSetHash, MeasurementSchemaHash = request.MeasurementSchemaHash,
                    EnvironmentFingerprint = environmentFingerprint,
                    HoldoutSettingsHash = EnvelopeHash(settingsHash + "\nholdout\n" + settings.HoldoutSeed),
                    EvidenceScope = request.EvidenceScope,
                    HoldoutUncertaintyMethod = ScientificAdvantageEnvelopeAdapter.PairedBlockUncertaintyMethod,
                    Cells = holdoutCells.ToArray(),
                });
            writeImmutableArtifact(artifactId + "-envelope", codec.Serialize(envelope, true));
            return new EnvelopeCellMeasurement { Settings = settings, Axis = axis, Calibration = raw,
                Holdout = holdoutRaw, FrozenCalibration = frozen, Envelope = envelope,
                FullMeasuredCandidateSetSha256 = fullSetHash };
        }

        private static string EnvelopeHash(string value) => CandidateDefinitionProtocol.ComputeSha256Utf8(value);

        private static EnvelopeRawPhase EnvelopeRaw(string id, PhaseMeasurement phase, string allocationIdentity) => new EnvelopeRawPhase
        {
            ArtifactId = id, ManagedAllocationMeasurement = allocationIdentity, DatasetHash = phase.DatasetHash, Results = phase.Results,
            TicksPerBlock = phase.TicksPerBlock, WarmupBlocks = phase.WarmupBlocks,
        };

        private static ScientificEvidenceBinding[] BindEnvelope(EnvelopeRawPhase phase, IEnvelopeArtifactCodec codec)
        {
            var bindings = new ScientificEvidenceBinding[phase.Results.Length];
            for (int i = 0; i < bindings.Length; i++) bindings[i] = new ScientificEvidenceBinding
            {
                Result = phase.Results[i], ContractFeasible = true, MemoryFeasible = true,
                EvidencePartitionId = phase.ArtifactId,
                EvidenceSha256 = EnvelopeHash(codec.Serialize(phase.Results[i])),
            };
            return bindings;
        }
    }
}
