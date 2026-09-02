using System;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>
    /// The aligned component-P95 draws produced by one paired scientific
    /// bootstrap. Baseline and candidate arrays share ReplicateId values.
    /// </summary>
    [Serializable]
    public sealed class PairedBootstrapCostReplicateSet
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public BootstrapEstimatorKind EstimatorKind;
        public uint RandomSeed;
        public string ResamplingUnit;
        public BootstrapCostReplicate[] BaselineReplicates;
        public BootstrapCostReplicate[] CandidateReplicates;
    }

    /// <summary>
    /// Explicit provenance and feasibility declarations used to adapt one
    /// scientific result. Contract and memory feasibility are intentionally not
    /// inferred from timing data.
    /// </summary>
    [Serializable]
    public sealed class ScientificEvidenceBinding
    {
        public LayoutBenchmarkResult Result;
        public bool ContractFeasible;
        public bool MemoryFeasible;
        public string EvidencePartitionId;
        public string EvidenceSha256;
    }

    /// <summary>
    /// Integration-owned bridge between schema-3 scientific measurements and the
    /// schema-1 advantage-envelope decision engine.
    /// </summary>
    public static class ScientificAdvantageEnvelopeAdapter
    {
        public const string PairedBlockUncertaintyMethod =
            "dlc.paired-block-bootstrap-log-ratio.v1";
        public const string ProcessHierarchicalUncertaintyMethod =
            "dlc.process-hierarchical-bootstrap-log-ratio.v1";
        public const string MeasurementSchemaCanonicalDescriptor =
            "dlc.scientific-envelope-measurement.v1\n" +
            "layout-benchmark-sample-schema=1\n" +
            "candidate-definition-schema=1\n" +
            "components=resident-p95-ms-per-tick,ingress-p95-ms,export-p95-ms\n" +
            "replicate-alignment=paired-measurement-block\n" +
            "estimand=log(candidate-amortized-p95/baseline-amortized-p95)\n";

        public static string MeasurementSchemaSha256 =>
            CandidateDefinitionProtocol.ComputeSha256Utf8(
                MeasurementSchemaCanonicalDescriptor);

        public static string ComputeCandidateSetSha256(
            ScientificEvidenceBinding[] bindings)
        {
            if (bindings == null || bindings.Length == 0)
                throw new ArgumentException("At least one evidence binding is required.", nameof(bindings));
            var candidates = new CandidateDescriptor[bindings.Length];
            for (int index = 0; index < bindings.Length; index++)
            {
                if (bindings[index] == null || bindings[index].Result == null)
                    throw new ArgumentException("Evidence bindings require results.", nameof(bindings));
                candidates[index] = bindings[index].Result.Candidate;
            }
            return CandidateDefinitionProtocol.ComputeCandidateSetSha256(candidates);
        }

        public static AdvantageEnvelopeCellInput CreateCalibrationCell(
            AdvantageEnvelopeAxis axis,
            ScientificEvidenceBinding[] bindings,
            int bootstrapIterations = BenchmarkStatistics.DefaultBootstrapIterations,
            uint bootstrapSeed = 0x9E3779B9u)
        {
            PreparedBinding[] prepared = PrepareBindings(
                axis,
                bindings,
                BenchmarkPhase.Calibration);
            int baselineIndex = FindSingleBaseline(prepared);
            if (prepared.Length < 2)
            {
                throw new ArgumentException(
                    "A calibration cell requires tuned AoS and at least one distinct candidate.",
                    nameof(bindings));
            }

            var evidence = new DecisionCandidateEvidence[prepared.Length];
            BootstrapCostReplicate[] sharedBaselineReplicates = null;
            for (int index = 0; index < prepared.Length; index++)
            {
                if (index == baselineIndex)
                    continue;

                PairedBootstrapCostReplicateSet pair =
                    BenchmarkStatistics.BootstrapAmortizedP95CostReplicates(
                        prepared[baselineIndex].Binding.Result,
                        prepared[index].Binding.Result,
                        bootstrapIterations,
                        bootstrapSeed);
                ValidateReplicateSet(pair, bootstrapIterations);
                if (sharedBaselineReplicates == null)
                {
                    sharedBaselineReplicates = pair.BaselineReplicates;
                }
                else if (!ReplicatesMatch(sharedBaselineReplicates, pair.BaselineReplicates))
                {
                    throw new InvalidOperationException(
                        "Identical tuned-AoS inputs and bootstrap seeds produced different aligned draws.");
                }
                evidence[index] = CreateEvidence(prepared[index], pair.CandidateReplicates);
            }

            evidence[baselineIndex] = CreateEvidence(
                prepared[baselineIndex],
                sharedBaselineReplicates);
            return new AdvantageEnvelopeCellInput
            {
                Axis = axis,
                CalibrationCandidates = evidence,
            };
        }

        public static AdvantageEnvelopeHoldoutCellInput CreateHoldoutCell(
            AdvantageEnvelopeAxis axis,
            ScientificEvidenceBinding baseline,
            ScientificEvidenceBinding frozenCandidate,
            int bootstrapIterations = BenchmarkStatistics.DefaultBootstrapIterations,
            uint bootstrapSeed = 0x9E3779B9u)
        {
            PreparedBinding[] prepared = PrepareBindings(
                axis,
                new[] { baseline, frozenCandidate },
                BenchmarkPhase.Holdout);
            int baselineIndex = FindSingleBaseline(prepared);
            int candidateIndex = baselineIndex == 0 ? 1 : 0;
            PairedBootstrapCostReplicateSet pair =
                BenchmarkStatistics.BootstrapAmortizedP95CostReplicates(
                    prepared[baselineIndex].Binding.Result,
                    prepared[candidateIndex].Binding.Result,
                    bootstrapIterations,
                    bootstrapSeed);
            ValidateReplicateSet(pair, bootstrapIterations);
            return new AdvantageEnvelopeHoldoutCellInput
            {
                Axis = axis,
                Baseline = CreateEvidence(prepared[baselineIndex], pair.BaselineReplicates),
                FrozenCandidate = CreateEvidence(prepared[candidateIndex], pair.CandidateReplicates),
            };
        }

        public static AdvantageEnvelopeArtifactReference CreateArtifactReference(
            ScenarioCalibrationProfile scenario,
            AdvantageEnvelopeProfile envelope,
            string artifactId,
            string artifactSha256)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));
            if (envelope == null)
                throw new ArgumentNullException(nameof(envelope));
            ProtocolIdentifier.RequireCanonical(artifactId, nameof(artifactId), "Artifact ID");
            if (!CandidateDefinitionProtocol.IsCanonicalSha256(artifactSha256))
                throw new ArgumentException("Artifact SHA-256 must be canonical uppercase hexadecimal.", nameof(artifactSha256));
            if (scenario.SchemaVersion != 3 || envelope.SchemaVersion != 1 ||
                !string.Equals(envelope.ArtifactType, "advantage-envelope", StringComparison.Ordinal) ||
                !string.Equals(envelope.DecisionEngineVersion, AdvantageEnvelopeEngine.Version, StringComparison.Ordinal) ||
                !envelope.FinalDecisionLocked || envelope.HoldoutCanRerank)
            {
                throw new ArgumentException("Only a locked schema-1 advantage envelope can be attached.", nameof(envelope));
            }
            if (!string.Equals(scenario.Scenario.ScenarioId, envelope.ScenarioId, StringComparison.Ordinal) ||
                scenario.Scenario.ContractVersion != envelope.ContractVersion)
            {
                throw new ArgumentException("The envelope scenario contract does not match the profile.", nameof(envelope));
            }

            string candidateSet = ComputeScenarioCandidateSetSha256(scenario);
            if (!string.Equals(candidateSet, envelope.CandidateSetHash, StringComparison.Ordinal) ||
                !string.Equals(MeasurementSchemaSha256, envelope.MeasurementSchemaHash, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The envelope candidate set or measurement schema does not match the scientific profile.",
                    nameof(envelope));
            }

            return new AdvantageEnvelopeArtifactReference
            {
                SchemaVersion = AdvantageEnvelopeArtifactReference.CurrentSchemaVersion,
                ArtifactId = artifactId,
                ArtifactSha256 = artifactSha256,
                ArtifactSchemaVersion = envelope.SchemaVersion,
                DecisionEngineVersion = envelope.DecisionEngineVersion,
                ScenarioId = envelope.ScenarioId,
                ContractVersion = envelope.ContractVersion,
                CandidateSetSha256 = envelope.CandidateSetHash,
                MeasurementSchemaSha256 = envelope.MeasurementSchemaHash,
            };
        }

        public static void ValidateArtifactReference(ScenarioCalibrationProfile scenario)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));
            AdvantageEnvelopeArtifactReference reference = scenario.AdvantageEnvelope;
            if (reference == null)
                return;
            if (reference.SchemaVersion != AdvantageEnvelopeArtifactReference.CurrentSchemaVersion ||
                reference.ArtifactSchemaVersion != 1)
            {
                throw new ArgumentException("The advantage-envelope reference has an unsupported schema.");
            }
            if (!ProtocolIdentifier.IsCanonical(reference.ArtifactId) ||
                !CandidateDefinitionProtocol.IsCanonicalSha256(reference.ArtifactSha256) ||
                !string.Equals(reference.DecisionEngineVersion, AdvantageEnvelopeEngine.Version, StringComparison.Ordinal) ||
                !string.Equals(reference.ScenarioId, scenario.Scenario.ScenarioId, StringComparison.Ordinal) ||
                reference.ContractVersion != scenario.Scenario.ContractVersion)
            {
                throw new ArgumentException("The advantage-envelope reference identity or provenance is invalid.");
            }
            string candidateSet = ComputeScenarioCandidateSetSha256(scenario);
            if (!CandidateDefinitionProtocol.IsCanonicalSha256(reference.CandidateSetSha256) ||
                !string.Equals(reference.CandidateSetSha256, candidateSet, StringComparison.Ordinal) ||
                !CandidateDefinitionProtocol.IsCanonicalSha256(reference.MeasurementSchemaSha256) ||
                !string.Equals(reference.MeasurementSchemaSha256, MeasurementSchemaSha256, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The advantage-envelope reference is not bound to this candidate set and measurement schema.");
            }
        }

        private static string ComputeScenarioCandidateSetSha256(
            ScenarioCalibrationProfile scenario)
        {
            if (scenario.CalibrationResults == null || scenario.CalibrationResults.Length == 0)
                throw new ArgumentException("A scenario requires calibration results to bind an envelope.");
            var candidates = new CandidateDescriptor[scenario.CalibrationResults.Length];
            for (int index = 0; index < scenario.CalibrationResults.Length; index++)
            {
                if (scenario.CalibrationResults[index] == null)
                    throw new ArgumentException("A scenario calibration result is null.");
                candidates[index] = scenario.CalibrationResults[index].Candidate;
            }
            return CandidateDefinitionProtocol.ComputeCandidateSetSha256(candidates);
        }

        private static PreparedBinding[] PrepareBindings(
            AdvantageEnvelopeAxis axis,
            ScientificEvidenceBinding[] bindings,
            BenchmarkPhase expectedPhase)
        {
            if (axis.ElementCount <= 0 || axis.LifetimeTicks <= 0 ||
                axis.HotToColdRatio < 0d || double.IsNaN(axis.HotToColdRatio) ||
                double.IsInfinity(axis.HotToColdRatio) || axis.WorkerCount <= 0 ||
                !ProtocolIdentifier.IsCanonical(axis.ExecutionPolicyId))
            {
                throw new ArgumentException("The advantage-envelope axis is invalid.", nameof(axis));
            }
            if (bindings == null || bindings.Length == 0)
                throw new ArgumentException("At least one evidence binding is required.", nameof(bindings));

            var prepared = new PreparedBinding[bindings.Length];
            string scenarioId = null;
            int contractVersion = 0;
            string partitionId = null;
            for (int index = 0; index < bindings.Length; index++)
            {
                ScientificEvidenceBinding binding = bindings[index];
                if (binding == null || binding.Result == null)
                    throw new ArgumentException("Every evidence binding requires a result.", nameof(bindings));
                if (!ProtocolIdentifier.IsCanonical(binding.EvidencePartitionId) ||
                    !CandidateDefinitionProtocol.IsCanonicalSha256(binding.EvidenceSha256))
                {
                    throw new ArgumentException(
                        "Every evidence binding requires a canonical partition ID and SHA-256.",
                        nameof(bindings));
                }

                LayoutBenchmarkResult result = binding.Result;
                CandidateDescriptor candidate = result.Candidate.NormalizePolicies();
                candidate.ValidateFactorConsistency();
                if (!result.Completed ||
                    result.SampleSchemaVersion != LayoutBenchmarkResult.CurrentSampleSchemaVersion ||
                    result.Phase != expectedPhase ||
                    result.ElementCount != axis.ElementCount ||
                    result.BoundaryCost.LifetimeTicks != axis.LifetimeTicks ||
                    !string.Equals(
                        candidate.Execution.PolicyId,
                        axis.ExecutionPolicyId,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "A scientific result does not match the cell phase, dimensions, execution policy, or completion gate.",
                        nameof(bindings));
                }
                if (!ProtocolIdentifier.IsCanonical(result.ScenarioId) ||
                    result.ScenarioContractVersion <= 0)
                {
                    throw new ArgumentException("A scientific result has no canonical scenario contract.", nameof(bindings));
                }

                if (index == 0)
                {
                    scenarioId = result.ScenarioId;
                    contractVersion = result.ScenarioContractVersion;
                    partitionId = binding.EvidencePartitionId;
                }
                else if (!string.Equals(scenarioId, result.ScenarioId, StringComparison.Ordinal) ||
                         contractVersion != result.ScenarioContractVersion ||
                         !string.Equals(partitionId, binding.EvidencePartitionId, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Cell evidence must share one scenario contract and one partition.",
                        nameof(bindings));
                }

                prepared[index] = new PreparedBinding(binding, candidate);
            }

            Array.Sort(prepared, ComparePreparedBinding);
            for (int index = 1; index < prepared.Length; index++)
            {
                if (string.Equals(
                        prepared[index - 1].Candidate.CandidateId,
                        prepared[index].Candidate.CandidateId,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException("Cell evidence contains a duplicate CandidateId.", nameof(bindings));
                }
            }
            return prepared;
        }

        private static int FindSingleBaseline(PreparedBinding[] prepared)
        {
            int baselineIndex = -1;
            for (int index = 0; index < prepared.Length; index++)
            {
                if (!prepared[index].Candidate.IsBaseline)
                    continue;
                if (baselineIndex >= 0)
                    throw new ArgumentException("Cell evidence must contain exactly one tuned AoS baseline.");
                baselineIndex = index;
            }
            if (baselineIndex < 0)
                throw new ArgumentException("Cell evidence must contain exactly one tuned AoS baseline.");
            return baselineIndex;
        }

        private static DecisionCandidateEvidence CreateEvidence(
            PreparedBinding prepared,
            BootstrapCostReplicate[] replicates)
        {
            if (replicates == null || replicates.Length == 0)
                throw new ArgumentException("Aligned bootstrap replicates are required.", nameof(replicates));
            LayoutBenchmarkResult result = prepared.Binding.Result;
            return new DecisionCandidateEvidence
            {
                Candidate = CandidateDefinitionProtocol.ToEnvelopeCandidate(prepared.Candidate),
                Completed = result.Completed,
                ContractFeasible = prepared.Binding.ContractFeasible,
                MemoryFeasible = prepared.Binding.MemoryFeasible,
                ParityPassed = result.ParityPassed,
                HotPathManagedAllocationBytes = result.HotPathManagedAllocationBytes,
                BoundaryManagedAllocationBytes = result.BoundaryManagedAllocationBytes,
                ResidentBytes = result.ResidentBytes,
                ResidentP95MillisecondsPerTick = CalculateP95(
                    result.ResidentSamplesMillisecondsPerTick),
                IngressP95Milliseconds = CalculateP95(result.IngressSamplesMilliseconds),
                ExportP95Milliseconds = CalculateP95(result.ExportSamplesMilliseconds),
                ResidentSampleCount = result.ResidentSamplesMillisecondsPerTick.Length,
                BoundarySampleCount = Math.Min(
                    result.IngressSamplesMilliseconds.Length,
                    result.ExportSamplesMilliseconds.Length),
                EvidencePartitionId = prepared.Binding.EvidencePartitionId,
                EvidenceHash = prepared.Binding.EvidenceSha256,
                BootstrapReplicates = replicates,
            };
        }

        private static double CalculateP95(double[] samples)
        {
            if (samples == null || samples.Length == 0)
                throw new ArgumentException("Scientific component samples are required.", nameof(samples));
            return BenchmarkStatistics.Calculate(
                samples,
                samples.Length,
                new double[samples.Length]).P95Milliseconds;
        }

        private static void ValidateReplicateSet(
            PairedBootstrapCostReplicateSet pair,
            int expectedCount)
        {
            if (pair == null ||
                pair.SchemaVersion != PairedBootstrapCostReplicateSet.CurrentSchemaVersion ||
                pair.EstimatorKind != BootstrapEstimatorKind.PairedBlockLogRatio ||
                pair.RandomSeed == 0u ||
                !string.Equals(pair.ResamplingUnit, "paired measurement block", StringComparison.Ordinal) ||
                pair.BaselineReplicates == null ||
                pair.CandidateReplicates == null ||
                pair.BaselineReplicates.Length != expectedCount ||
                pair.CandidateReplicates.Length != expectedCount)
            {
                throw new InvalidOperationException("The scientific bootstrap returned an invalid replicate set.");
            }
        }

        private static bool ReplicatesMatch(
            BootstrapCostReplicate[] left,
            BootstrapCostReplicate[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int index = 0; index < left.Length; index++)
            {
                if (left[index].ReplicateId != right[index].ReplicateId ||
                    left[index].ResidentP95MillisecondsPerTick != right[index].ResidentP95MillisecondsPerTick ||
                    left[index].IngressP95Milliseconds != right[index].IngressP95Milliseconds ||
                    left[index].ExportP95Milliseconds != right[index].ExportP95Milliseconds)
                {
                    return false;
                }
            }
            return true;
        }

        private static int ComparePreparedBinding(PreparedBinding left, PreparedBinding right)
        {
            return string.Compare(
                left.Candidate.CandidateId,
                right.Candidate.CandidateId,
                StringComparison.Ordinal);
        }

        private sealed class PreparedBinding
        {
            public readonly ScientificEvidenceBinding Binding;
            public readonly CandidateDescriptor Candidate;

            public PreparedBinding(
                ScientificEvidenceBinding binding,
                CandidateDescriptor candidate)
            {
                Binding = binding;
                Candidate = candidate;
            }
        }
    }
}
