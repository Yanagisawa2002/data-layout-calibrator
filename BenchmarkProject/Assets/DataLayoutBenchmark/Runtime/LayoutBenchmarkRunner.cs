using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using Yanagisawa.DataLayoutAutotuner;
using Debug = UnityEngine.Debug;

namespace Yanagisawa.DataLayoutBenchmark
{
    internal sealed class LayoutBenchmarkRunner : MonoBehaviour
    {
        private const float FixedDeltaTime = 1.0f / 60.0f;
        private const int PreflightCount = 4099;
        private const int PreflightSteps = 256;
        private const uint CandidateOrderSeed = 0xA341316Cu;
        private const string BurstPackageVersion = "1.8.29";
        private const string CollectionsPackageVersion = "6.5.0";
        private const string MathematicsPackageVersion = "1.4.0";
        private static readonly int[] BatchSizes = { 32, 64, 128, 256 };
        private static readonly LayoutKind[] Layouts =
        {
            LayoutKind.AoS,
            LayoutKind.SoA,
            LayoutKind.AoSoA8,
        };

        private BenchmarkConfiguration _configuration;
        private string _phase = "STARTING";
        private string _detail = string.Empty;
        private float _progress;
        private LayoutTuningProfile _profile;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (!BenchmarkConfiguration.ShouldRun())
                return;

            var host = new GameObject("Data Layout Calibration Runner");
            DontDestroyOnLoad(host);
            host.AddComponent<LayoutBenchmarkRunner>();
        }

        private IEnumerator Start()
        {
            _configuration = BenchmarkConfiguration.FromCommandLine();
            IEnumerator operation = RunBenchmark();
            while (true)
            {
                bool hasNext;
                object current;
                try
                {
                    hasNext = operation.MoveNext();
                    current = hasNext ? operation.Current : null;
                }
                catch (Exception exception)
                {
                    Fail(exception);
                    yield break;
                }

                if (!hasNext)
                    break;
                yield return current;
            }

            _phase = "COMPLETE";
            _progress = 1.0f;
            Debug.Log($"[DataLayoutBenchmark] Complete. Results: {_configuration.OutputDirectory}");
            if (_configuration.QuitWhenComplete)
                Application.Quit(0);
        }

        private IEnumerator RunBenchmark()
        {
            if (!BurstCompiler.IsEnabled)
                throw new InvalidOperationException("Burst is disabled. Refusing to calibrate with managed jobs.");

            Directory.CreateDirectory(_configuration.OutputDirectory);
            _phase = "PREFLIGHT";
            _detail = $"Verifying three AOT job entrypoints on {PreflightCount:N0} records";
            ValidatePlayerParity();
            yield return null;

            _phase = "CALIBRATING BLOCK SIZE";
            int ticksPerBlock = DetermineTicksPerBlock(_configuration.ElementCount);
            int warmupBlocks = DetermineWarmupBlocks(_configuration.ElementCount, ticksPerBlock);
            _detail = $"{ticksPerBlock} ticks/sample, {warmupBlocks} warmup blocks";
            yield return null;

            NativeArray<ParticleRecord> calibrationData = ParticleDataSet.Create(
                _configuration.ElementCount,
                ParticleDataSet.CalibrationSeed,
                Allocator.Persistent);
            CandidateState[] candidates = null;
            var rawSamples = new List<RawSample>(Layouts.Length * BatchSizes.Length * _configuration.SamplesPerCandidate);
            string calibrationInputHash;
            try
            {
                calibrationInputHash = ComputeInputHash(calibrationData);
                candidates = CreateCandidateStates(calibrationData, _configuration.SamplesPerCandidate);
                yield return WarmCandidates(candidates, warmupBlocks, ticksPerBlock, 0.05f, 0.25f);
                yield return MeasureCandidates(candidates, ticksPerBlock, rawSamples, 0.25f, 0.72f);
                ValidateCandidateParity(candidates);
            }
            finally
            {
                if (candidates != null)
                    DisposeCandidates(candidates);
                if (calibrationData.IsCreated)
                    calibrationData.Dispose();
            }

            LayoutBenchmarkResult[] calibrationResults = BuildResults(
                candidates,
                BenchmarkPhase.Calibration,
                _configuration.ElementCount,
                ticksPerBlock);
            LayoutSelectionDecision calibrationDecision = LayoutSelector.SelectCalibration(
                calibrationResults,
                calibrationResults.Length,
                _configuration.MinimumImprovementPercent);

            LayoutBenchmarkResult holdoutBaseline = null;
            LayoutBenchmarkResult holdoutSelected = null;
            string holdoutInputHash = string.Empty;
            LayoutSelectionDecision finalDecision = calibrationDecision;

            if (calibrationDecision.Status == LayoutSelectionStatus.Optimized)
            {
                _phase = "UNSEEN HOLDOUT";
                _detail = $"{_configuration.HoldoutElementCount:N0} records, new seed";
                NativeArray<ParticleRecord> holdoutData = ParticleDataSet.Create(
                    _configuration.HoldoutElementCount,
                    ParticleDataSet.HoldoutSeed,
                    Allocator.Persistent);
                CandidateState[] holdoutCandidates = null;
                try
                {
                    holdoutInputHash = ComputeInputHash(holdoutData);
                    holdoutCandidates = CreateCandidateStates(
                        holdoutData,
                        _configuration.SamplesPerCandidate,
                        calibrationDecision.BaselineCandidate,
                        calibrationDecision.SelectedCandidate);
                    yield return WarmCandidates(holdoutCandidates, warmupBlocks, ticksPerBlock, 0.72f, 0.80f);
                    yield return MeasureCandidates(holdoutCandidates, ticksPerBlock, rawSamples, 0.80f, 0.96f);
                    ValidateCandidateParity(holdoutCandidates);
                    LayoutBenchmarkResult[] holdoutResults = BuildResults(
                        holdoutCandidates,
                        BenchmarkPhase.Holdout,
                        _configuration.HoldoutElementCount,
                        ticksPerBlock);
                    holdoutBaseline = holdoutResults[0];
                    holdoutSelected = holdoutResults[1];
                    finalDecision = LayoutSelector.ConfirmHoldout(
                        calibrationDecision,
                        holdoutBaseline,
                        holdoutSelected,
                        _configuration.MinimumImprovementPercent);
                }
                finally
                {
                    if (holdoutCandidates != null)
                        DisposeCandidates(holdoutCandidates);
                    if (holdoutData.IsCreated)
                        holdoutData.Dispose();
                }
            }

            _phase = "WRITING EVIDENCE";
            _progress = 0.98f;
            _profile = new LayoutTuningProfile
            {
                RunId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture),
                CreatedUtcIso8601 = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
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
                WorkloadId = "particle-integrate-v1",
                ElementCount = _configuration.ElementCount,
                HoldoutElementCount = _configuration.HoldoutElementCount,
                CalibrationSeed = ParticleDataSet.CalibrationSeed,
                HoldoutSeed = ParticleDataSet.HoldoutSeed,
                FixedDeltaTime = FixedDeltaTime,
                TicksPerBlock = ticksPerBlock,
                WarmupBlocks = warmupBlocks,
                SamplesPerCandidate = _configuration.SamplesPerCandidate,
                CandidateOrderSeed = CandidateOrderSeed,
                PrimaryTimingMetric = "schedule_to_complete_ms_per_tick",
                TimingIncludes = "managed layout switch; Schedule; worker execution; Complete",
                TimingExcludes = "allocation; packing; reset; parity validation; rendering; serialization",
                CalibrationDatasetHash = calibrationInputHash,
                HoldoutDatasetHash = holdoutInputHash,
                CalibrationDecision = calibrationDecision,
                FinalDecision = finalDecision,
                CalibrationResults = calibrationResults,
                HoldoutBaselineResult = holdoutBaseline,
                HoldoutSelectedResult = holdoutSelected,
            };
            WriteArtifacts(_profile, rawSamples, ticksPerBlock, warmupBlocks);
            _detail = BuildDecisionLine(finalDecision);
        }

        private void ValidatePlayerParity()
        {
            NativeArray<ParticleRecord> source = ParticleDataSet.Create(
                PreflightCount,
                ParticleDataSet.CalibrationSeed,
                Allocator.Persistent);
            ParticleLayoutDomain aos = null;
            ParticleLayoutDomain soa = null;
            ParticleLayoutDomain aosoa = null;
            try
            {
                aos = ParticleLayoutDomain.Create(LayoutKind.AoS, 64, source);
                soa = ParticleLayoutDomain.Create(LayoutKind.SoA, 64, source);
                aosoa = ParticleLayoutDomain.Create(LayoutKind.AoSoA8, 64, source);
                for (int step = 0; step < PreflightSteps; step++)
                {
                    aos.Schedule(FixedDeltaTime).Complete();
                    soa.Schedule(FixedDeltaTime).Complete();
                    aosoa.Schedule(FixedDeltaTime).Complete();
                }

                CompareDomains(aos, soa);
                CompareDomains(aos, aosoa);
            }
            finally
            {
                aos?.Dispose();
                soa?.Dispose();
                aosoa?.Dispose();
                if (source.IsCreated)
                    source.Dispose();
            }
        }

        private int DetermineTicksPerBlock(int elementCount)
        {
            NativeArray<ParticleRecord> source = ParticleDataSet.Create(
                elementCount,
                ParticleDataSet.CalibrationSeed,
                Allocator.Persistent);
            ParticleLayoutDomain domain = null;
            try
            {
                domain = ParticleLayoutDomain.Create(LayoutKind.AoS, 64, source);
                for (int i = 0; i < 4; i++)
                    domain.Schedule(FixedDeltaTime).Complete();

                int ticks = 1;
                while (true)
                {
                    double milliseconds = MeasureBlock(domain, ticks, out _);
                    if (milliseconds >= _configuration.TargetBlockMilliseconds ||
                        ticks >= _configuration.MaximumTicksPerBlock)
                    {
                        return ticks;
                    }

                    ticks = Math.Min(ticks * 2, _configuration.MaximumTicksPerBlock);
                }
            }
            finally
            {
                domain?.Dispose();
                if (source.IsCreated)
                    source.Dispose();
            }
        }

        private int DetermineWarmupBlocks(int elementCount, int ticksPerBlock)
        {
            if (_configuration.MinimumWarmupSeconds <= 0d)
                return _configuration.WarmupBlocks;

            NativeArray<ParticleRecord> source = ParticleDataSet.Create(
                elementCount,
                ParticleDataSet.CalibrationSeed,
                Allocator.Persistent);
            ParticleLayoutDomain domain = null;
            try
            {
                domain = ParticleLayoutDomain.Create(LayoutKind.AoS, 64, source);
                for (int i = 0; i < 4; i++)
                    domain.Schedule(FixedDeltaTime).Complete();
                double blockMilliseconds = MeasureBlock(domain, ticksPerBlock, out _);
                int timeBased = (int)Math.Ceiling(
                    (_configuration.MinimumWarmupSeconds * 1000d) /
                    Math.Max(0.001d, blockMilliseconds));
                return Math.Max(_configuration.WarmupBlocks, timeBased);
            }
            finally
            {
                domain?.Dispose();
                if (source.IsCreated)
                    source.Dispose();
            }
        }

        private CandidateState[] CreateCandidateStates(
            NativeArray<ParticleRecord> source,
            int sampleCount,
            params LayoutCandidate[] only)
        {
            LayoutCandidate[] definitions;
            if (only != null && only.Length > 0)
            {
                definitions = only;
            }
            else
            {
                definitions = new LayoutCandidate[Layouts.Length * BatchSizes.Length];
                int cursor = 0;
                for (int layout = 0; layout < Layouts.Length; layout++)
                for (int batch = 0; batch < BatchSizes.Length; batch++)
                    definitions[cursor++] = new LayoutCandidate(Layouts[layout], BatchSizes[batch]);
            }

            var states = new CandidateState[definitions.Length];
            try
            {
                for (int i = 0; i < definitions.Length; i++)
                {
                    LayoutCandidate definition = definitions[i];
                    states[i] = new CandidateState
                    {
                        Candidate = definition,
                        Domain = ParticleLayoutDomain.Create(
                            definition.Layout,
                            definition.LogicalBatchSize,
                            source),
                        SamplesMillisecondsPerTick = new double[sampleCount],
                    };
                    states[i].ResidentBytes = states[i].Domain.ResidentBytes;
                }

                return states;
            }
            catch
            {
                DisposeCandidates(states);
                throw;
            }
        }

        private IEnumerator WarmCandidates(
            CandidateState[] candidates,
            int warmupBlocks,
            int ticksPerBlock,
            float progressStart,
            float progressEnd)
        {
            _phase = "WARMUP";
            int total = candidates.Length * warmupBlocks;
            int completed = 0;
            for (int block = 0; block < warmupBlocks; block++)
            {
                for (int candidate = 0; candidate < candidates.Length; candidate++)
                {
                    CandidateState state = candidates[candidate];
                    _detail = $"{CandidateId(state.Candidate)}  block {block + 1}/{warmupBlocks}";
                    for (int tick = 0; tick < ticksPerBlock; tick++)
                        state.Domain.Schedule(FixedDeltaTime).Complete();
                    completed++;
                    _progress = Mathf.Lerp(progressStart, progressEnd, completed / (float)total);
                    yield return null;
                }
            }
        }

        private IEnumerator MeasureCandidates(
            CandidateState[] candidates,
            int ticksPerBlock,
            List<RawSample> rawSamples,
            float progressStart,
            float progressEnd)
        {
            _phase = "MEASURING";
            int[] order = new int[candidates.Length];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;

            uint randomState = CandidateOrderSeed;
            int total = candidates.Length * _configuration.SamplesPerCandidate;
            int completed = 0;
            for (int round = 0; round < _configuration.SamplesPerCandidate; round++)
            {
                Shuffle(order, ref randomState);
                for (int orderIndex = 0; orderIndex < order.Length; orderIndex++)
                {
                    CandidateState state = candidates[order[orderIndex]];
                    _detail = $"{CandidateId(state.Candidate)}  sample {round + 1}/{_configuration.SamplesPerCandidate}";
                    double blockMilliseconds = MeasureBlock(
                        state.Domain,
                        ticksPerBlock,
                        out long allocatedBytes);
                    double perTick = blockMilliseconds / ticksPerBlock;
                    state.SamplesMillisecondsPerTick[round] = perTick;
                    state.HotPathManagedAllocationBytes += allocatedBytes;
                    rawSamples.Add(new RawSample
                    {
                        Phase = candidates.Length == Layouts.Length * BatchSizes.Length ? "calibration" : "holdout",
                        Round = round,
                        OrderIndex = orderIndex,
                        Candidate = state.Candidate,
                        ElementCount = state.Domain.Count,
                        Ticks = ticksPerBlock,
                        BlockMilliseconds = blockMilliseconds,
                        MillisecondsPerTick = perTick,
                        ManagedAllocationBytes = allocatedBytes,
                    });
                    completed++;
                    _progress = Mathf.Lerp(progressStart, progressEnd, completed / (float)total);
                    yield return null;
                }
            }
        }

        private static double MeasureBlock(
            ParticleLayoutDomain domain,
            int ticks,
            out long managedAllocationBytes)
        {
            long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            long timestampStart = Stopwatch.GetTimestamp();
            for (int tick = 0; tick < ticks; tick++)
                domain.Schedule(FixedDeltaTime).Complete();
            long timestampEnd = Stopwatch.GetTimestamp();
            long allocationEnd = GC.GetAllocatedBytesForCurrentThread();
            managedAllocationBytes = Math.Max(0L, allocationEnd - allocationStart);
            return (timestampEnd - timestampStart) * 1000d / Stopwatch.Frequency;
        }

        private static void ValidateCandidateParity(CandidateState[] candidates)
        {
            if (candidates.Length == 0)
                throw new InvalidOperationException("No candidates were measured.");

            ParticleLayoutDomain reference = candidates[0].Domain;
            ulong referenceHash = reference.ComputeQuantizedHash();
            for (int i = 0; i < candidates.Length; i++)
            {
                CandidateState state = candidates[i];
                state.StateHash = state.Domain.ComputeQuantizedHash();
                try
                {
                    CompareDomains(reference, state.Domain);
                    state.ParityPassed = state.StateHash == referenceHash;
                    if (!state.ParityPassed)
                        state.FailureReason = "Quantized state hash differs from the reference candidate.";
                }
                catch (Exception exception)
                {
                    state.ParityPassed = false;
                    state.FailureReason = exception.Message;
                }
            }
        }

        private static void CompareDomains(ParticleLayoutDomain reference, ParticleLayoutDomain candidate)
        {
            if (reference.Count != candidate.Count)
                throw new InvalidOperationException("Candidate logical record counts differ.");

            for (int index = 0; index < reference.Count; index++)
            {
                if (!ParticleStateValidation.ApproximatelyEqual(
                        reference.ReadRecord(index),
                        candidate.ReadRecord(index),
                        1e-5f,
                        out string failure))
                {
                    throw new InvalidOperationException($"Parity failed at record {index}: {failure}");
                }
            }
        }

        private static LayoutBenchmarkResult[] BuildResults(
            CandidateState[] candidates,
            BenchmarkPhase phase,
            int elementCount,
            int ticksPerBlock)
        {
            var results = new LayoutBenchmarkResult[candidates.Length];
            var scratch = new double[candidates[0].SamplesMillisecondsPerTick.Length];
            for (int i = 0; i < candidates.Length; i++)
            {
                CandidateState state = candidates[i];
                results[i] = new LayoutBenchmarkResult
                {
                    Phase = phase,
                    Candidate = state.Candidate,
                    ElementCount = elementCount,
                    StepsPerSample = ticksPerBlock,
                    Latency = BenchmarkStatistics.Calculate(
                        state.SamplesMillisecondsPerTick,
                        state.SamplesMillisecondsPerTick.Length,
                        scratch),
                    Completed = true,
                    ParityPassed = state.ParityPassed,
                    HotPathManagedAllocationBytes = state.HotPathManagedAllocationBytes,
                    ResidentBytes = state.ResidentBytes,
                    StateHash = $"0x{state.StateHash:X16}",
                    FailureReason = state.FailureReason,
                };
            }

            return results;
        }

        private static string ComputeInputHash(NativeArray<ParticleRecord> source)
        {
            using (ParticleLayoutDomain domain = ParticleLayoutDomain.Create(LayoutKind.AoS, 64, source))
                return $"0x{domain.ComputeQuantizedHash():X16}";
        }

        private static void DisposeCandidates(CandidateState[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
                candidates[i]?.Domain?.Dispose();
        }

        private static void Shuffle(int[] order, ref uint state)
        {
            for (int i = order.Length - 1; i > 0; i--)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                int swap = (int)(state % (uint)(i + 1));
                int temporary = order[i];
                order[i] = order[swap];
                order[swap] = temporary;
            }
        }

        private void WriteArtifacts(
            LayoutTuningProfile profile,
            List<RawSample> samples,
            int ticksPerBlock,
            int warmupBlocks)
        {
            string json = JsonUtility.ToJson(profile, true);
            File.WriteAllText(Path.Combine(_configuration.OutputDirectory, "profile.json"), json);

            var csv = new StringBuilder(1024 + samples.Count * 96);
            csv.AppendLine("phase,round,order_index,candidate_id,layout,logical_batch,element_count,ticks,block_ms,ms_per_tick,updates_per_second,managed_alloc_bytes");
            for (int i = 0; i < samples.Count; i++)
            {
                RawSample sample = samples[i];
                double updatesPerSecond = sample.ElementCount / (sample.MillisecondsPerTick / 1000d);
                csv.Append(sample.Phase).Append(',')
                    .Append(sample.Round).Append(',')
                    .Append(sample.OrderIndex).Append(',')
                    .Append(CandidateId(sample.Candidate)).Append(',')
                    .Append(sample.Candidate.Layout).Append(',')
                    .Append(sample.Candidate.LogicalBatchSize).Append(',')
                    .Append(sample.ElementCount).Append(',')
                    .Append(sample.Ticks).Append(',')
                    .Append(sample.BlockMilliseconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(sample.MillisecondsPerTick.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(updatesPerSecond.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(sample.ManagedAllocationBytes)
                    .AppendLine();
            }
            File.WriteAllText(Path.Combine(_configuration.OutputDirectory, "samples.csv"), csv.ToString());

            var summary = new StringBuilder();
            summary.AppendLine("Data Layout Calibrator - particle-integrate-v1")
                .AppendLine($"Created UTC: {profile.CreatedUtcIso8601}")
                .AppendLine($"Unity: {profile.UnityVersion}")
                .AppendLine($"Burst: {profile.BurstVersion}")
                .AppendLine($"Collections / Mathematics: {profile.CollectionsVersion} / {profile.MathematicsVersion}")
                .AppendLine($"Backend / build: {profile.ScriptingBackend} / {profile.BuildType}")
                .AppendLine($"CPU: {profile.Processor}")
                .AppendLine($"Job workers: {profile.JobWorkerCount}")
                .AppendLine($"Calibration records: {_configuration.ElementCount:N0}")
                .AppendLine($"Ticks per timed block: {ticksPerBlock}")
                .AppendLine($"Warmup blocks: {warmupBlocks}")
                .AppendLine($"Samples per candidate: {_configuration.SamplesPerCandidate}")
                .AppendLine($"Decision: {BuildDecisionLine(profile.FinalDecision)}")
                .AppendLine($"Reason: {profile.FinalDecision.Reason}")
                .AppendLine()
                .AppendLine("Timing includes managed layout switch, Schedule, worker execution and Complete.")
                .AppendLine("Timing excludes allocation, packing, reset, parity validation, rendering and serialization.");
            File.WriteAllText(Path.Combine(_configuration.OutputDirectory, "summary.txt"), summary.ToString());
        }

        private static string BuildDecisionLine(LayoutSelectionDecision decision)
        {
            if (decision.Status == LayoutSelectionStatus.Optimized)
            {
                return $"Optimized: {CandidateId(decision.SelectedCandidate)} " +
                       $"is {decision.ImprovementPercent:F1}% faster at P95 than {CandidateId(decision.BaselineCandidate)}";
            }

            return $"{decision.Status}: use {CandidateId(decision.BaselineCandidate)}; " +
                   $"best measured {CandidateId(decision.BestMeasuredCandidate)} was {decision.ImprovementPercent:F1}% faster";
        }

        private static string CandidateId(LayoutCandidate candidate)
        {
            return $"{candidate.Layout}-b{candidate.LogicalBatchSize}";
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
            float width = Mathf.Min(920f, Screen.width - 64f);
            var panel = new Rect(32f, 32f, width, Mathf.Min(540f, Screen.height - 64f));
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

            if (_profile != null)
            {
                LayoutSelectionDecision decision = _profile.FinalDecision;
                GUILayout.Label(BuildDecisionLine(decision), _titleStyle);
                GUILayout.Space(10f);
                GUILayout.Label($"AoS-best P95   {decision.BaselineP95Milliseconds:F3} ms", _bodyStyle);
                GUILayout.Label($"Selected P95   {decision.BestMeasuredP95Milliseconds:F3} ms", _bodyStyle);
                GUILayout.Label($"Parity          {(decision.RejectedParityCandidateCount == 0 ? "PASS" : "FAIL")}", _bodyStyle);
                GUILayout.Label($"CPU             {_profile.Processor}", _bodyStyle);
                GUILayout.Label($"Unity / Burst   {_profile.UnityVersion} / {_profile.BurstVersion}", _bodyStyle);
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
            };
        }

        private sealed class CandidateState
        {
            public LayoutCandidate Candidate;
            public ParticleLayoutDomain Domain;
            public double[] SamplesMillisecondsPerTick;
            public long HotPathManagedAllocationBytes;
            public long ResidentBytes;
            public ulong StateHash;
            public bool ParityPassed;
            public string FailureReason;
        }

        private struct RawSample
        {
            public string Phase;
            public int Round;
            public int OrderIndex;
            public LayoutCandidate Candidate;
            public int ElementCount;
            public int Ticks;
            public double BlockMilliseconds;
            public double MillisecondsPerTick;
            public long ManagedAllocationBytes;
        }
    }
}
