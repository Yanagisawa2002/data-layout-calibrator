using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Burst;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using Yanagisawa.DataLayoutCalibrator;
using Yanagisawa.DataLayoutCalibrator.Generated;
using Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate;
using Yanagisawa.DataLayoutCalibrator.Samples.TransformExport;
using Debug = UnityEngine.Debug;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark
{
    internal sealed class LayoutBenchmarkRunner : MonoBehaviour
    {
        private const string BurstPackageVersion = "1.8.29";
        private const string CollectionsPackageVersion = "6.5.0";
        private const string MathematicsPackageVersion = "1.4.0";

        private BenchmarkConfiguration _configuration;
        private string _phase = "STARTING";
        private string _detail = string.Empty;
        private float _progress;
        private CalibrationSuiteProfile _suite;
        private ScenarioCalibrationProfile _latestScenario;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (!BenchmarkConfiguration.ShouldRun())
                return;

            var host = new GameObject("Data Layout Calibrator Runner");
            DontDestroyOnLoad(host);
            host.AddComponent<LayoutBenchmarkRunner>();
        }

        private IEnumerator Start()
        {
            _configuration = BenchmarkConfiguration.FromCommandLine();
            yield return null;

            try
            {
                RunSuite();
            }
            catch (Exception exception)
            {
                Fail(exception);
                yield break;
            }

            _phase = "COMPLETE";
            _progress = 1f;
            _detail = $"Fixed result: {Path.Combine(_configuration.OutputDirectory, "calibration-suite.json")}";
            Debug.Log($"[DataLayoutCalibrator] Complete. Results: {_configuration.OutputDirectory}");
            if (_configuration.QuitWhenComplete)
                Application.Quit(0);
        }

        private void RunSuite()
        {
            if (!BurstCompiler.IsEnabled)
                throw new InvalidOperationException("Burst is disabled. Refusing to calibrate with managed jobs.");

            Directory.CreateDirectory(_configuration.OutputDirectory);
            ICalibrationScenarioFactory[] factories =
                GeneratedCalibrationScenarioRegistry.CreateFactories();
            var profiles = new ScenarioCalibrationProfile[factories.Length];
            for (int index = 0; index < factories.Length; index++)
            {
                ICalibrationScenarioFactory factory = factories[index];
                _phase = "CALIBRATING";
                _detail = factory.Descriptor.DisplayName;
                _progress = index / (float)factories.Length;
                profiles[index] = ScenarioCalibrationEngine.Run(
                    factory,
                    CreateSettings(factory.Descriptor.ScenarioId));
                _latestScenario = profiles[index];
                WriteScenarioArtifacts(profiles[index]);
                _progress = (index + 1) / (float)factories.Length;
            }

            _phase = "WRITING FIXED RESULT";
            _suite = new CalibrationSuiteProfile
            {
                RunId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture),
                CreatedUtcIso8601 = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Environment = CaptureEnvironment(),
                Scenarios = profiles,
            };
            WriteSuiteArtifacts(_suite);
        }

        private CalibrationRunSettings CreateSettings(string scenarioId)
        {
            uint calibrationSeed;
            uint holdoutSeed;
            switch (scenarioId)
            {
                case "particle-integrate-v2":
                    calibrationSeed = ParticleDataSet.CalibrationSeed;
                    holdoutSeed = ParticleDataSet.HoldoutSeed;
                    break;
                case "transform-export-v1":
                    calibrationSeed = TransformExportDataSet.CalibrationSeed;
                    holdoutSeed = TransformExportDataSet.HoldoutSeed;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenarioId), scenarioId, "No dataset seeds are registered.");
            }

            return new CalibrationRunSettings
            {
                ElementCount = _configuration.ElementCount,
                HoldoutElementCount = _configuration.HoldoutElementCount,
                CalibrationSeed = calibrationSeed,
                HoldoutSeed = holdoutSeed,
                WarmupBlocks = _configuration.WarmupBlocks,
                MinimumWarmupSeconds = _configuration.MinimumWarmupSeconds,
                SamplesPerCandidate = _configuration.SamplesPerCandidate,
                BoundarySamplesPerCandidate = _configuration.BoundarySamplesPerCandidate,
                LifetimeTicks = _configuration.LifetimeTicks,
                TargetBlockMilliseconds = _configuration.TargetBlockMilliseconds,
                MaximumTicksPerBlock = _configuration.MaximumTicksPerBlock,
                MinimumImprovementPercent = _configuration.MinimumImprovementPercent,
                BootstrapIterations = _configuration.BootstrapIterations,
                BootstrapConfidenceLevel = _configuration.BootstrapConfidenceLevel,
            };
        }

        private static CalibrationEnvironment CaptureEnvironment()
        {
            return new CalibrationEnvironment
            {
                UnityVersion = Application.unityVersion,
                BurstVersion = BurstPackageVersion,
                CollectionsVersion = CollectionsPackageVersion,
                MathematicsVersion = MathematicsPackageVersion,
                ScriptingBackend = ScriptingBackendName(),
                BuildType = Debug.isDebugBuild ? "Development" : "Release",
                OperatingSystem = SystemInfo.operatingSystem,
                Processor = SystemInfo.processorType,
                LogicalProcessorCount = SystemInfo.processorCount,
                JobWorkerCount = JobsUtility.JobWorkerCount,
                GraphicsDevice = SystemInfo.graphicsDeviceName,
            };
        }

        private void WriteSuiteArtifacts(CalibrationSuiteProfile suite)
        {
            string suitePath = Path.Combine(_configuration.OutputDirectory, "calibration-suite.json");
            File.WriteAllText(suitePath, JsonUtility.ToJson(suite, true));

            var summary = new StringBuilder(2048);
            summary.AppendLine("Data Layout Calibrator")
                .AppendLine($"Run: {suite.RunId}")
                .AppendLine($"Created UTC: {suite.CreatedUtcIso8601}")
                .AppendLine($"Unity / Burst: {suite.Environment.UnityVersion} / {suite.Environment.BurstVersion}")
                .AppendLine($"Backend / build: {suite.Environment.ScriptingBackend} / {suite.Environment.BuildType}")
                .AppendLine($"CPU: {suite.Environment.Processor}")
                .AppendLine($"Job workers: {suite.Environment.JobWorkerCount}")
                .AppendLine();
            for (int index = 0; index < suite.Scenarios.Length; index++)
            {
                ScenarioCalibrationProfile scenario = suite.Scenarios[index];
                summary.AppendLine($"{scenario.Scenario.DisplayName}: {BuildDecisionLine(scenario.FinalDecision)}")
                    .AppendLine($"  {scenario.FinalDecision.Reason}");
            }
            summary.AppendLine()
                .AppendLine("Presentation contract: calibration-suite.json is immutable input. A renderer may filter or format it, but may not recompute or replace FinalDecision.");
            File.WriteAllText(Path.Combine(_configuration.OutputDirectory, "summary.txt"), summary.ToString());
        }

        private void WriteScenarioArtifacts(ScenarioCalibrationProfile profile)
        {
            string directory = Path.Combine(_configuration.OutputDirectory, profile.Scenario.ScenarioId);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "profile.json"), JsonUtility.ToJson(profile, true));
            WriteSamplesCsv(Path.Combine(directory, "samples.csv"), profile);

            LayoutSelectionDecision decision = profile.FinalDecision;
            var summary = new StringBuilder(1536);
            summary.AppendLine($"Data Layout Calibrator - {profile.Scenario.DisplayName}")
                .AppendLine($"Scenario contract: {profile.Scenario.ScenarioId}/v{profile.Scenario.ContractVersion}")
                .AppendLine($"Records: {profile.ElementCount:N0}")
                .AppendLine($"Resident ticks per sample: {profile.TicksPerBlock}")
                .AppendLine($"Resident / boundary samples: {profile.SamplesPerCandidate} / {profile.BoundarySamplesPerCandidate}")
                .AppendLine($"Lifetime amortization: {profile.LifetimeTicks} ticks")
                .AppendLine($"Measurement order: {profile.SamplingDesign?.CandidateOrder.ToString() ?? "Unspecified"}")
                .AppendLine($"Bootstrap: paired measurement blocks, {profile.BootstrapIterations} iterations at {profile.BootstrapConfidenceLevel:P0}")
                .AppendLine($"Evidence scope: {profile.SamplingDesign?.EvidenceScope.ToString() ?? "Unspecified"}")
                .AppendLine($"Decision uncertainty: {BuildUncertaintyLine(decision, profile.SamplingDesign)}")
                .AppendLine($"Decision: {BuildDecisionLine(decision)}")
                .AppendLine($"Reason: {decision.Reason}")
                .AppendLine($"Ingress: {profile.BoundaryContract.IngressContract}")
                .AppendLine($"Export: {profile.BoundaryContract.ExportContract}")
                .AppendLine()
                .AppendLine($"Primary metric: {profile.PrimaryTimingMetric}")
                .AppendLine($"Includes: {profile.TimingIncludes}")
                .AppendLine($"Excludes: {profile.TimingExcludes}");
            File.WriteAllText(Path.Combine(directory, "summary.txt"), summary.ToString());
        }

        private static void WriteSamplesCsv(string path, ScenarioCalibrationProfile profile)
        {
            var csv = new StringBuilder(8192);
            csv.AppendLine(
                "scenario,phase,candidate_id,layout,logical_batch,sample_index,resident_ms_per_tick,ingress_ms,export_ms,amortized_ms_per_tick,amortized_p95_ms_per_tick,resident_alloc_bytes,boundary_alloc_bytes,parity_passed,layout_policy_id,kernel_policy_id,batch_policy_id,execution_policy_id,resident_block_id,resident_order_position,ingress_block_id,ingress_order_position,export_block_id,export_order_position,scenario_contract_version");
            AppendResults(csv, profile.CalibrationResults);
            if (profile.HoldoutBaselineResult != null)
                AppendResult(csv, profile.HoldoutBaselineResult);
            if (profile.HoldoutSelectedResult != null)
                AppendResult(csv, profile.HoldoutSelectedResult);
            File.WriteAllText(path, csv.ToString());
        }

        private static void AppendResults(StringBuilder csv, LayoutBenchmarkResult[] results)
        {
            if (results == null)
                return;
            for (int index = 0; index < results.Length; index++)
                AppendResult(csv, results[index]);
        }

        private static void AppendResult(StringBuilder csv, LayoutBenchmarkResult result)
        {
            if (result == null)
                return;
            int residentCount = Length(result.ResidentSamplesMillisecondsPerTick);
            int ingressCount = Length(result.IngressSamplesMilliseconds);
            int exportCount = Length(result.ExportSamplesMilliseconds);
            int amortizedCount = Length(result.AmortizedSamplesMillisecondsPerTick);
            int rows = Math.Max(Math.Max(residentCount, ingressCount), Math.Max(exportCount, amortizedCount));
            for (int sample = 0; sample < rows; sample++)
            {
                csv.Append(result.ScenarioId).Append(',')
                    .Append(result.Phase.ToString().ToLowerInvariant()).Append(',')
                    .Append(CandidateId(result.Candidate)).Append(',')
                    .Append(result.Candidate.LayoutId).Append(',')
                    .Append(result.Candidate.LogicalBatchSize).Append(',')
                    .Append(sample).Append(',');
                AppendOptional(csv, result.ResidentSamplesMillisecondsPerTick, sample);
                csv.Append(',');
                AppendOptional(csv, result.IngressSamplesMilliseconds, sample);
                csv.Append(',');
                AppendOptional(csv, result.ExportSamplesMilliseconds, sample);
                csv.Append(',');
                AppendOptional(csv, result.AmortizedSamplesMillisecondsPerTick, sample);
                csv.Append(',')
                    .Append(result.AmortizedLatency.P95Milliseconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(result.HotPathManagedAllocationBytes).Append(',')
                    .Append(result.BoundaryManagedAllocationBytes).Append(',')
                    .Append(result.ParityPassed ? "true" : "false").Append(',')
                    .Append(result.Candidate.EffectiveLayout.PolicyId).Append(',')
                    .Append(result.Candidate.EffectiveKernel.PolicyId).Append(',')
                    .Append(result.Candidate.EffectiveBatch.PolicyId).Append(',')
                    .Append(result.Candidate.EffectiveExecution.PolicyId).Append(',');
                AppendOptional(csv, result.ResidentBlockIds, sample);
                csv.Append(',');
                AppendOptional(csv, result.ResidentOrderPositions, sample);
                csv.Append(',');
                AppendOptional(csv, result.IngressBlockIds, sample);
                csv.Append(',');
                AppendOptional(csv, result.IngressOrderPositions, sample);
                csv.Append(',');
                AppendOptional(csv, result.ExportBlockIds, sample);
                csv.Append(',');
                AppendOptional(csv, result.ExportOrderPositions, sample);
                csv.Append(',')
                    .Append(result.ScenarioContractVersion.ToString(CultureInfo.InvariantCulture))
                    .AppendLine();
            }
        }

        private static int Length(double[] values) => values == null ? 0 : values.Length;

        private static void AppendOptional(StringBuilder builder, double[] values, int index)
        {
            if (values != null && index < values.Length)
                builder.Append(values[index].ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendOptional(StringBuilder builder, int[] values, int index)
        {
            if (values != null && index < values.Length)
                builder.Append(values[index].ToString(CultureInfo.InvariantCulture));
        }

        private static string BuildDecisionLine(LayoutSelectionDecision decision)
        {
            string interval = decision.ImprovementConfidenceInterval.Iterations > 0
                ? $"; {decision.ImprovementConfidenceInterval.ConfidenceLevel:P0} CI " +
                  $"[{decision.ImprovementConfidenceInterval.LowerBoundPercent:F1}%, " +
                  $"{decision.ImprovementConfidenceInterval.UpperBoundPercent:F1}%]"
                : string.Empty;
            if (decision.Status == LayoutSelectionStatus.Optimized)
            {
                return $"Optimized: {CandidateId(decision.SelectedCandidate)} " +
                       $"has {decision.ImprovementPercent:F1}% lower amortized P95 than " +
                       $"{CandidateId(decision.BaselineCandidate)}{interval}";
            }

            return $"{decision.Status}: use {CandidateId(decision.BaselineCandidate)}; " +
                   $"best measured {CandidateId(decision.BestMeasuredCandidate)} " +
                   $"had {decision.ImprovementPercent:F1}% lower amortized P95{interval}";
        }

        private static string BuildUncertaintyLine(
            LayoutSelectionDecision decision,
            SamplingDesignDescriptor samplingDesign)
        {
            BootstrapConfidenceInterval interval = decision.ImprovementConfidenceInterval;
            if (interval.Iterations <= 0)
                return "descriptive measurements only; no inferential confidence interval";

            string scope = samplingDesign?.EvidenceScope.ToString() ?? "Unspecified";
            return $"{interval.ResamplingUnit}; {scope}";
        }

        private static string CandidateId(CandidateDescriptor candidate)
        {
            return string.IsNullOrEmpty(candidate.CandidateId)
                ? $"{candidate.LayoutId}-b{candidate.LogicalBatchSize}"
                : candidate.CandidateId;
        }

        private static string ScriptingBackendName()
        {
#if ENABLE_IL2CPP
            return "IL2CPP";
#else
            return "Mono";
#endif
        }

        private void Fail(Exception exception)
        {
            _phase = "FAILED";
            _detail = exception.Message;
            Debug.LogException(exception);
            if (_configuration != null && _configuration.QuitWhenComplete)
                Application.Quit(2);
        }

        private void OnGUI()
        {
            if (_configuration == null || !_configuration.ShowGui)
                return;

            EnsureStyles();
            float width = Mathf.Min(980f, Screen.width - 64f);
            var panel = new Rect(32f, 32f, width, Mathf.Min(560f, Screen.height - 64f));
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 28f, panel.y + 24f, panel.width - 56f, panel.height - 48f));
            GUILayout.Label("DATA LAYOUT CALIBRATOR", _titleStyle);
            GUILayout.Space(8f);
            GUILayout.Label(_phase, _bodyStyle);
            GUILayout.Label(_detail, _bodyStyle);
            GUILayout.Space(12f);
            Rect progressRect = GUILayoutUtility.GetRect(100f, 24f, GUILayout.ExpandWidth(true));
            GUI.Box(progressRect, GUIContent.none);
            GUI.Box(new Rect(progressRect.x, progressRect.y, progressRect.width * _progress, progressRect.height), GUIContent.none);
            GUILayout.Space(18f);

            if (_latestScenario != null)
            {
                LayoutSelectionDecision decision = _latestScenario.FinalDecision;
                GUILayout.Label(_latestScenario.Scenario.DisplayName, _titleStyle);
                GUILayout.Label(BuildDecisionLine(decision), _bodyStyle);
                GUILayout.Space(10f);
                GUILayout.Label($"AoS amortized P95   {decision.BaselineP95Milliseconds:F4} ms/tick", _bodyStyle);
                GUILayout.Label($"Best amortized P95  {decision.BestMeasuredP95Milliseconds:F4} ms/tick", _bodyStyle);
                GUILayout.Label($"Lifetime             {_latestScenario.LifetimeTicks} ticks", _bodyStyle);
            }
            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
                return;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.78f, 0.95f, 1f) },
            };
            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                normal = { textColor = Color.white },
                wordWrap = true,
            };
        }
    }
}
