using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Yanagisawa.DataLayoutCalibrator
{
    [Serializable]
    public sealed class SearchComparisonResult
    {
        public int SchemaVersion = 1;
        public string ArtifactType = "measured-adaptive-exhaustive-comparison";
        public string CandidateSetSha256;
        public string SettingsSha256;
        public string EnvironmentFingerprint;
        public AdvantageEnvelopeAxis Axis;
        public bool AdaptiveFirst;
        public bool BothSelectionsFrozenBeforeHoldout;
        public bool FinalEvidenceRequirementsUnchanged;
        public string CostScope = "shared preflight charged to both; adaptive includes quick measurement, selection, evidence persistence, planning and independent full calibration; holdout separately timed";
        public string AuditScope = "exhaustive calibration is an empirical oracle, never used for adaptive tuning; regret is not population truth";
        public double SharedPreflightMilliseconds;
        public double QuickMilliseconds;
        public double PlanningMilliseconds;
        public double AdaptiveFullMilliseconds;
        public double ExhaustiveFullMilliseconds;
        public double AdaptiveCalibrationMilliseconds;
        public double ExhaustiveCalibrationMilliseconds;
        public double AdaptiveHoldoutMilliseconds;
        public double ExhaustiveHoldoutMilliseconds;
        public long AdaptiveComponentEvaluationCount;
        public long ExhaustiveComponentEvaluationCount;
        public int AdaptiveFullCandidateCount;
        public int ExhaustiveFullCandidateCount;
        public string EvaluationCountScope = "observed resident + ingress + export sample units; excludes preflight, warmup, ticks within samples, bootstrap draws";
        public string QuickSha256;
        public string AdaptiveCalibrationSha256;
        public string ExhaustiveCalibrationSha256;
        public string FrozenSelectionsSha256;
        public string AdaptiveFinalSha256;
        public string ExhaustiveFinalSha256;
        public string[] ExecutedFinalistIds;
        public bool ExhaustiveWinnerEliminated;
        public double ShortlistOracleRegretPercent;
        public double ActualAdaptiveSelectionOracleRegretPercent;
        public double MaximumAllowedRegretPercent = 1d;
        public bool RegretGatePassed;
        public AdaptiveEliminationPlan Plan;
        public ScenarioCalibrationProfile Quick;
        public ScenarioCalibrationProfile Adaptive;
        public ScenarioCalibrationProfile Exhaustive;
    }

    public static partial class ScenarioCalibrationEngine
    {
        /// <summary>
        /// Actual, independent quick/full measurements on one frozen candidate pool.
        /// The persistence callback must write a NEW immutable UTF-8 artifact and return
        /// its actual uppercase SHA-256. It is called before any holdout is generated.
        /// External orchestration owns source/binary/CPU identity and process ordering.
        /// </summary>
        public static SearchComparisonResult RunSearchComparison(
            ICalibrationScenarioFactory factory, CalibrationRunSettings settings,
            CandidateDescriptor[] frozenCandidates, AdvantageEnvelopeAxis axis,
            string environmentFingerprint, bool adaptiveFirst,
            Func<string, object, string> persist)
        {
            ValidateSettings(settings);
            settings = CloneSearchSettings(settings);
            if (factory == null || persist == null) throw new ArgumentNullException();
            if (frozenCandidates == null || frozenCandidates.Length < 2)
                throw new ArgumentException("Freeze at least two candidates before search.");
            if (settings.CalibrationSeed == settings.HoldoutSeed)
                throw new ArgumentException("Holdout must use a distinct dataset seed.");
            if (!CandidateDefinitionProtocol.IsCanonicalSha256(environmentFingerprint))
                throw new ArgumentException("An actual environment fingerprint is required.");
            if (axis.ElementCount != settings.ElementCount || axis.LifetimeTicks != settings.LifetimeTicks)
                throw new ArgumentException("Search axis must match executed settings.");
            foreach (CandidateDescriptor descriptor in frozenCandidates)
                if (descriptor.EffectiveExecution.PolicyId != axis.ExecutionPolicyId)
                    throw new ArgumentException("Freeze one actual execution-policy cell per comparison.");
            var frozen = (CandidateDescriptor[])frozenCandidates.Clone();
            var result = new SearchComparisonResult
            {
                CandidateSetSha256 = CandidateDefinitionProtocol.ComputeCandidateSetSha256(frozen),
                SettingsSha256 = PersistSearchArtifact(persist, "settings", settings),
                EnvironmentFingerprint = environmentFingerprint, Axis = axis, AdaptiveFirst = adaptiveFirst,
            };
            var quickSettings = CloneSearchSettings(settings);
            quickSettings.SamplesPerCandidate = Math.Min(6, settings.SamplesPerCandidate);
            quickSettings.BoundarySamplesPerCandidate = Math.Min(4, settings.BoundarySamplesPerCandidate);
            quickSettings.BootstrapIterations = Math.Min(200, settings.BootstrapIterations);
            string quickSettingsHash;
            var clock = Stopwatch.StartNew();
            // Preflight only the declared pool, not a potentially different factory default.
            RunPreflight(new FrozenSearchFactory(factory, frozen), settings);
            int commonTicks, commonWarmup;
            using (ICalibrationScenario scenario = factory.Create(settings.ElementCount, settings.CalibrationSeed, frozen))
            {
                ICalibrationCandidate reference = scenario.GetCandidate(scenario.ReferenceCandidateIndex);
                reference.BoundaryCost.Ingress();
                commonTicks = DetermineTicksPerBlock(reference, settings);
                commonWarmup = DetermineWarmupBlocks(reference, commonTicks, settings);
            }
            clock.Stop(); result.SharedPreflightMilliseconds = clock.Elapsed.TotalMilliseconds;

            // Quick is always sampled independently, even if exhaustive happens first.
            // It must never read an exhaustive result, selected candidate or holdout.
            if (!adaptiveFirst)
            {
                clock.Restart();
                result.Exhaustive = MeasureSearchCalibration(factory, settings, frozen, commonTicks, commonWarmup);
                result.ExhaustiveCalibrationSha256 = PersistSearchArtifact(persist, "exhaustive-calibration", result.Exhaustive);
                clock.Stop(); result.ExhaustiveFullMilliseconds = clock.Elapsed.TotalMilliseconds;
            }
            clock.Restart();
            quickSettingsHash = PersistSearchArtifact(persist, "quick-settings", quickSettings);
            result.Quick = MeasureSearchCalibration(factory, quickSettings, frozen, commonTicks, commonWarmup);
            VerifySearchPool(result.Quick, result.CandidateSetSha256);
            result.QuickSha256 = PersistSearchArtifact(persist, "quick", result.Quick);
            clock.Stop(); result.QuickMilliseconds = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            result.Plan = BuildSearchPlan(result, settings, quickSettingsHash);
            var finalists = new List<CandidateDescriptor>();
            foreach (CandidateDescriptor candidate in frozen)
            {
                // Keep ALL AoS controls: quick tuning must not silently weaken the full baseline.
                if (candidate.IsBaseline || result.Plan.Status != AdaptiveEliminationPlanStatus.ReadyForFullCalibration ||
                    Array.IndexOf(result.Plan.FinalistCandidateIds, candidate.CandidateId) >= 0)
                    finalists.Add(candidate);
            }
            result.ExecutedFinalistIds = finalists.ConvertAll(c => c.CandidateId).ToArray();
            PersistSearchArtifact(persist, "plan", result.Plan);
            clock.Stop(); result.PlanningMilliseconds = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            result.Adaptive = MeasureSearchCalibration(factory, settings, finalists.ToArray(), commonTicks, commonWarmup);
            result.AdaptiveCalibrationSha256 = PersistSearchArtifact(persist, "adaptive-calibration", result.Adaptive);
            clock.Stop(); result.AdaptiveFullMilliseconds = clock.Elapsed.TotalMilliseconds;
            VerifySearchPool(result.Adaptive, CandidateDefinitionProtocol.ComputeCandidateSetSha256(finalists.ToArray()));
            if (adaptiveFirst)
            {
                clock.Restart();
                result.Exhaustive = MeasureSearchCalibration(factory, settings, frozen, commonTicks, commonWarmup);
                result.ExhaustiveCalibrationSha256 = PersistSearchArtifact(persist, "exhaustive-calibration", result.Exhaustive);
                clock.Stop(); result.ExhaustiveFullMilliseconds = clock.Elapsed.TotalMilliseconds;
            }
            VerifySearchPool(result.Exhaustive, result.CandidateSetSha256);
            if (result.Quick.CalibrationDatasetHash != result.Exhaustive.CalibrationDatasetHash ||
                result.Adaptive.CalibrationDatasetHash != result.Exhaustive.CalibrationDatasetHash)
                throw new InvalidOperationException("Frozen calibration data changed between search paths.");
            result.AdaptiveFullCandidateCount = result.Adaptive.CalibrationResults.Length;
            result.ExhaustiveFullCandidateCount = result.Exhaustive.CalibrationResults.Length;
            result.AdaptiveComponentEvaluationCount = CountSearchSamples(result.Quick) + CountSearchSamples(result.Adaptive);
            result.ExhaustiveComponentEvaluationCount = CountSearchSamples(result.Exhaustive);
            result.AdaptiveCalibrationMilliseconds = result.SharedPreflightMilliseconds + result.QuickMilliseconds +
                result.PlanningMilliseconds + result.AdaptiveFullMilliseconds;
            result.ExhaustiveCalibrationMilliseconds = result.SharedPreflightMilliseconds + result.ExhaustiveFullMilliseconds;
            AuditSearchSelection(result);
            result.FinalEvidenceRequirementsUnchanged = true;
            result.BothSelectionsFrozenBeforeHoldout = true;
            result.FrozenSelectionsSha256 = PersistSearchArtifact(persist, "frozen-selections", result);
            // Audit is read-only. Holdout can confirm/reject each frozen choice, never rerank.
            if (adaptiveFirst)
            {
                result.AdaptiveHoldoutMilliseconds = ConfirmSearch(factory, settings, result.Adaptive);
                result.ExhaustiveHoldoutMilliseconds = ConfirmSearch(factory, settings, result.Exhaustive);
            }
            else
            {
                result.ExhaustiveHoldoutMilliseconds = ConfirmSearch(factory, settings, result.Exhaustive);
                result.AdaptiveHoldoutMilliseconds = ConfirmSearch(factory, settings, result.Adaptive);
            }
            result.AdaptiveFinalSha256 = PersistSearchArtifact(persist, "adaptive-final", result.Adaptive);
            result.ExhaustiveFinalSha256 = PersistSearchArtifact(persist, "exhaustive-final", result.Exhaustive);
            return result;
        }

        private static string PersistSearchArtifact(Func<string, object, string> persist, string name, object value)
        {
            string before = SearchMutationFingerprint(value);
            string hash = RequirePersistedHash(persist(name, value));
            if (before != SearchMutationFingerprint(value))
                throw new InvalidOperationException("Persistence changed the frozen scientific payload: " + name);
            return hash;
        }

        // Integrity guard only, not an artifact hash. Public serializable fields are
        // already retained by the Player serializer. No reflected candidate execution.
        private static string SearchMutationFingerprint(object value)
        {
            var text = new StringBuilder();
            AppendSearchFields(text, value);
            return CandidateDefinitionProtocol.ComputeSha256Utf8(text.ToString());
        }

        private static void AppendSearchFields(StringBuilder text, object value)
        {
            if (value == null) { text.Append("null;"); return; }
            Type type = value.GetType();
            text.Append(type.FullName).Append(':');
            if (value is string label) { text.Append(label.Length).Append(':').Append(label); return; }
            if (value is double real) { text.Append(BitConverter.DoubleToInt64Bits(real).ToString("X16", CultureInfo.InvariantCulture)); return; }
            if (value is float single) { text.Append(BitConverter.ToString(BitConverter.GetBytes(single))); return; }
            if (type.IsPrimitive || type.IsEnum || value is decimal)
            { text.Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append(';'); return; }
            if (value is Array array)
            {
                text.Append(array.Length).Append('[');
                foreach (object item in array) AppendSearchFields(text, item);
                text.Append(']'); return;
            }
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (FieldInfo field in fields)
            { text.Append(field.Name).Append('='); AppendSearchFields(text, field.GetValue(value)); }
        }

        private static string RequirePersistedHash(string value)
        {
            if (!CandidateDefinitionProtocol.IsCanonicalSha256(value))
                throw new InvalidOperationException("Persistence must return the actual uppercase file SHA-256.");
            return value;
        }

        private static void VerifySearchPool(ScenarioCalibrationProfile profile, string expected)
        {
            var descriptors = new CandidateDescriptor[profile.CalibrationResults.Length];
            for (int i = 0; i < descriptors.Length; i++) descriptors[i] = profile.CalibrationResults[i].Candidate;
            if (CandidateDefinitionProtocol.ComputeCandidateSetSha256(descriptors) != expected)
                throw new InvalidOperationException("Factory did not execute the frozen candidate definitions.");
        }

        private static AdaptiveEliminationPlan BuildSearchPlan(SearchComparisonResult result,
            CalibrationRunSettings settings, string quickSettingsHash)
        {
            var bindings = new ScientificEvidenceBinding[result.Quick.CalibrationResults.Length];
            for (int i = 0; i < bindings.Length; i++) bindings[i] = new ScientificEvidenceBinding
            {
                Result = result.Quick.CalibrationResults[i], ContractFeasible = true, MemoryFeasible = true,
                EvidencePartitionId = "calibration", EvidenceSha256 = result.QuickSha256,
            };
            AdvantageEnvelopeCellInput cell = ScientificAdvantageEnvelopeAdapter.CreateCalibrationCell(
                result.Axis, bindings, result.Quick.BootstrapIterations, settings.BootstrapSeed,
                result.Quick.CalibrationDecision.BaselineCandidate.CandidateId);
            return AdaptiveEliminationEngine.CreatePlan(new AdaptiveEliminationRequest
            {
                SearchId = "measured-search", CreatedUtcIso8601 = DateTime.UtcNow.ToString("O"),
                ScenarioId = result.Quick.Scenario.ScenarioId, ContractVersion = result.Quick.Scenario.ContractVersion,
                CandidateSetHash = result.CandidateSetSha256,
                MeasurementSchemaHash = ScientificAdvantageEnvelopeAdapter.MeasurementSchemaSha256,
                EnvironmentFingerprint = result.EnvironmentFingerprint, QuickCalibrationSettingsHash = quickSettingsHash,
                SourceArtifactId = "quick", SourceArtifactSha256 = result.QuickSha256,
                CalibrationPartitionId = "calibration", PlannedHoldoutPartitionId = "holdout",
                EvidenceScope = "SinglePlayer", QuickUncertaintyMethod = ScientificAdvantageEnvelopeAdapter.PairedBlockUncertaintyMethod,
                Axis = result.Axis, Candidates = cell.CalibrationCandidates,
                Policy = new AdaptiveEliminationPolicy
                {
                    MinimumImprovementPercent = settings.MinimumImprovementPercent,
                    ConfidenceLevel = settings.BootstrapConfidenceLevel,
                    MinimumQuickResidentSamples = result.Quick.SamplesPerCandidate,
                    MinimumQuickBoundarySamples = result.Quick.BoundarySamplesPerCandidate,
                    MinimumQuickBootstrapReplicates = result.Quick.BootstrapIterations,
                    RequiredFullResidentSamplesPerFinalist = settings.SamplesPerCandidate,
                    RequiredFullBoundarySamplesPerFinalist = settings.BoundarySamplesPerCandidate,
                    RequiredFullBootstrapReplicates = settings.BootstrapIterations,
                    RequiredHoldoutResidentSamples = settings.SamplesPerCandidate,
                    RequiredHoldoutBoundarySamples = settings.BoundarySamplesPerCandidate,
                    RequiredHoldoutBootstrapReplicates = settings.BootstrapIterations,
                },
            });
        }

        private static long CountSearchSamples(ScenarioCalibrationProfile profile)
        {
            long count = 0;
            foreach (LayoutBenchmarkResult r in profile.CalibrationResults)
                count += r.ResidentSamplesMillisecondsPerTick.Length + r.IngressSamplesMilliseconds.Length + r.ExportSamplesMilliseconds.Length;
            return count;
        }

        private static void AuditSearchSelection(SearchComparisonResult result)
        {
            LayoutBenchmarkResult best = null, shortlist = null, actual = null;
            string selected = result.Adaptive.CalibrationDecision.SelectedCandidate.CandidateId;
            foreach (LayoutBenchmarkResult r in result.Exhaustive.CalibrationResults)
            {
                if (!r.Completed || !r.ParityPassed || r.HotPathManagedAllocationBytes != 0 || r.BoundaryManagedAllocationBytes != 0)
                    throw new InvalidOperationException("Exhaustive oracle contains failed evidence; comparison cannot pass.");
                double cost = r.AmortizedLatency.P95Milliseconds;
                if (!(cost > 0) || double.IsInfinity(cost)) throw new InvalidOperationException("Invalid oracle cost.");
                if (best == null || cost < best.AmortizedLatency.P95Milliseconds) best = r;
                if (Array.IndexOf(result.ExecutedFinalistIds, r.Candidate.CandidateId) >= 0 &&
                    (shortlist == null || cost < shortlist.AmortizedLatency.P95Milliseconds)) shortlist = r;
                if (r.Candidate.CandidateId == selected) actual = r;
            }
            if (best == null || shortlist == null || actual == null) throw new InvalidOperationException("Incomplete regret oracle.");
            result.ExhaustiveWinnerEliminated = Array.IndexOf(result.ExecutedFinalistIds, best.Candidate.CandidateId) < 0;
            result.ShortlistOracleRegretPercent = 100d * (shortlist.AmortizedLatency.P95Milliseconds / best.AmortizedLatency.P95Milliseconds - 1d);
            result.ActualAdaptiveSelectionOracleRegretPercent = 100d * (actual.AmortizedLatency.P95Milliseconds / best.AmortizedLatency.P95Milliseconds - 1d);
            result.RegretGatePassed = result.ActualAdaptiveSelectionOracleRegretPercent <= result.MaximumAllowedRegretPercent;
        }

        private static double ConfirmSearch(ICalibrationScenarioFactory factory, CalibrationRunSettings settings,
            ScenarioCalibrationProfile profile)
        {
            var clock = Stopwatch.StartNew();
            if (profile.CalibrationDecision.Status == LayoutSelectionStatus.Optimized)
            {
                PhaseMeasurement holdout = MeasurePhase(factory, settings, BenchmarkPhase.Holdout,
                    settings.HoldoutElementCount, settings.HoldoutSeed, HoldoutIsolation.Freeze(profile.CalibrationDecision),
                    profile.TicksPerBlock, profile.WarmupBlocks, settings.CandidateOrderSeed ^ 0x9E3779B9u);
                if (holdout.DatasetHash == profile.CalibrationDatasetHash)
                    throw new InvalidOperationException("Holdout dataset hash matches calibration.");
                profile.HoldoutDatasetHash = holdout.DatasetHash;
                profile.HoldoutBaselineResult = holdout.Results[0];
                profile.HoldoutSelectedResult = holdout.Results[1];
                profile.FinalDecision = LayoutSelector.ConfirmHoldout(profile.CalibrationDecision,
                    holdout.Results[0], holdout.Results[1], settings.MinimumImprovementPercent,
                    settings.BootstrapIterations, settings.BootstrapConfidenceLevel, settings.BootstrapSeed ^ 0x68E31DA4u);
            }
            clock.Stop(); return clock.Elapsed.TotalMilliseconds;
        }

        private sealed class FrozenSearchFactory : ICalibrationScenarioFactory
        {
            private readonly ICalibrationScenarioFactory _factory;
            private readonly CandidateDescriptor[] _candidates;
            public FrozenSearchFactory(ICalibrationScenarioFactory factory, CandidateDescriptor[] candidates)
            { _factory = factory; _candidates = candidates; }
            public ScenarioDescriptor Descriptor => _factory.Descriptor;
            public ICalibrationScenario Create(int count, uint seed, CandidateDescriptor[] candidates = null)
                => _factory.Create(count, seed, candidates ?? _candidates);
        }

        private static ScenarioCalibrationProfile MeasureSearchCalibration(
            ICalibrationScenarioFactory factory, CalibrationRunSettings settings,
            CandidateDescriptor[] candidates, int ticks, int warmup)
        {
            PhaseMeasurement calibration = MeasurePhase(factory, settings, BenchmarkPhase.Calibration,
                settings.ElementCount, settings.CalibrationSeed, candidates, ticks, warmup, settings.CandidateOrderSeed);
            LayoutSelectionDecision calibrationDecision = LayoutSelector.SelectCalibration(calibration.Results,
                calibration.Results.Length, settings.MinimumImprovementPercent, settings.BootstrapIterations,
                settings.BootstrapConfidenceLevel, settings.BootstrapSeed);
            return new ScenarioCalibrationProfile
            {
                Scenario = factory.Descriptor,
                ElementCount = settings.ElementCount,
                HoldoutElementCount = settings.HoldoutElementCount,
                CalibrationSeed = settings.CalibrationSeed,
                HoldoutSeed = settings.HoldoutSeed,
                FixedDeltaTime = settings.FixedDeltaTime,
                TicksPerBlock = calibration.TicksPerBlock,
                WarmupBlocks = calibration.WarmupBlocks,
                SamplesPerCandidate = settings.SamplesPerCandidate,
                BoundarySamplesPerCandidate = settings.BoundarySamplesPerCandidate,
                LifetimeTicks = settings.LifetimeTicks,
                CandidateOrderSeed = settings.CandidateOrderSeed,
                BootstrapIterations = settings.BootstrapIterations,
                BootstrapConfidenceLevel = settings.BootstrapConfidenceLevel,
                MinimumImprovementPercent = settings.MinimumImprovementPercent,
                PrimaryTimingMetric =
                    "amortized_p95_ms_per_tick = resident_p95 + (ingress_p95 + export_p95) / lifetime_ticks",
                ManagedAllocationMeasurement = settings.AllocationCounter.Identity,
                TimingIncludes =
                    "candidate dispatch; job Schedule; worker execution; Complete; separately timed full ingress and export",
                TimingExcludes =
                    "allocation; dataset generation; parity scan; hashing; JSON/CSV serialization; visualization",
                CalibrationDatasetHash = calibration.DatasetHash,
                HoldoutDatasetHash = string.Empty,
                SamplingDesign = new SamplingDesignDescriptor
                {
                    CandidateOrder = settings.MeasurementOrder,
                    PairingUnit = "complete measurement block",
                    EvidenceScope = EvidenceScope.SinglePlayer,
                    CalibrationTunesCandidates = true,
                    HoldoutRetuningPermitted = false,
                    UncertaintyDescription =
                        "Paired block bootstrap within one Player process; this interval is not cross-process or cross-device evidence.",
                },
                BoundaryContract = calibration.BoundaryContract,
                CalibrationDecision = calibrationDecision,
                FinalDecision = calibrationDecision,
                CalibrationResults = calibration.Results,
                HoldoutBaselineResult = null,
                HoldoutSelectedResult = null,
            };
        }

        private static CalibrationRunSettings CloneSearchSettings(CalibrationRunSettings source)
        {
            return new CalibrationRunSettings
            {
                AllocationCounter = source.AllocationCounter,
                ElementCount = source.ElementCount,
                HoldoutElementCount = source.HoldoutElementCount,
                CalibrationSeed = source.CalibrationSeed,
                HoldoutSeed = source.HoldoutSeed,
                PreflightElementCount = source.PreflightElementCount,
                PreflightTicks = source.PreflightTicks,
                FixedDeltaTime = source.FixedDeltaTime,
                WarmupBlocks = source.WarmupBlocks,
                MinimumWarmupSeconds = source.MinimumWarmupSeconds,
                SamplesPerCandidate = source.SamplesPerCandidate,
                BoundarySamplesPerCandidate = source.BoundarySamplesPerCandidate,
                LifetimeTicks = source.LifetimeTicks,
                TargetBlockMilliseconds = source.TargetBlockMilliseconds,
                MaximumTicksPerBlock = source.MaximumTicksPerBlock,
                MinimumImprovementPercent = source.MinimumImprovementPercent,
                BootstrapIterations = source.BootstrapIterations,
                BootstrapConfidenceLevel = source.BootstrapConfidenceLevel,
                CandidateOrderSeed = source.CandidateOrderSeed,
                BootstrapSeed = source.BootstrapSeed,
                ParityTolerance = source.ParityTolerance,
                MeasurementOrder = source.MeasurementOrder,
            };
        }

    }
}
