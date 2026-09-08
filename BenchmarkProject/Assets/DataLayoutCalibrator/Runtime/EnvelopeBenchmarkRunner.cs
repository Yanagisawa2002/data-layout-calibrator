using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Unity.Burst;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark
{
    [Serializable]
    public sealed class EnvelopeGridDeclaration
    {
        public string Protocol = "dlc.measured-envelope-grid.v1";
        public int[] ElementCounts = { 4096, 65536 };
        public int[] LifetimeTicks = { 1, 16, 256 };
        public int[] ColdAccessEveryTicks = { 1, 8 };
        public int[] WorkerCounts = { 1, 7 };
        public int IndependentProcesses = 5;
        public CandidateDescriptor[] Candidates;
        public CalibrationRunSettings Settings = new CalibrationRunSettings
        {
            WarmupBlocks = 32, MinimumWarmupSeconds = 0.1,
            TargetBlockMilliseconds = 2, MaximumTicksPerBlock = 256,
        };
        public string AccessDefinition = "hot logical bytes=28 per tick; cold logical bytes=20 per cold pass; " +
            "hot/cold ratio=1.4*period; each pass reads/writes all Rotation components and Category in layout-owned storage; " +
            "separate cold pass dispatch included; not physical bandwidth or cache-cold evidence";
        public string LifetimeDefinition = "resident component P95 plus complete ingress/export P95 divided by declared lifetime; " +
            "fresh measurements per cell; modeled amortization, not direct finite-lifetime transaction P95";
        public string UnmeasuredAxes = "other counts/lifetimes/access frequencies/workers; other CPUs/ISAs/devices; " +
            "other execution topologies; other workloads; cache-cold; hardware counters; cross-process confidence intervals";

        public void Validate()
        {
            if (Protocol != "dlc.measured-envelope-grid.v1" || IndependentProcesses != 5 || Candidates == null || Candidates.Length != 30)
                throw new ArgumentException("Unsupported or incomplete grid declaration.");
            CheckAxis(ElementCounts, new[] { 4096, 65536 });
            CheckAxis(LifetimeTicks, new[] { 1, 16, 256 });
            CheckAxis(ColdAccessEveryTicks, new[] { 1, 8 });
            CheckAxis(WorkerCounts, new[] { 1, 7 });
            var ids = new HashSet<string>();
            var matched = new HashSet<string>();
            foreach (CandidateDescriptor candidate in Candidates)
            {
                candidate.ValidateFactorConsistency();
                if (!ids.Add(candidate.CandidateId) || candidate.Execution.PolicyId != "FrameFaithful" ||
                    (candidate.LogicalBatchSize != 64 && candidate.LogicalBatchSize != 256))
                    throw new ArgumentException("Candidate identity, execution or batch differs from declaration.");
                matched.Add(candidate.LayoutId + "/" + candidate.Kernel.PolicyId + "/" + candidate.LogicalBatchSize);
            }
            foreach (string layout in new[] { "AoS", "SoA", "AoSoA4", "AoSoA8", "AoSoA16", "AoSPadded64" })
            foreach (string kernel in new[] { "ScalarBranched", "ScalarBranchless" })
            foreach (int batch in new[] { 64, 256 })
                if (!matched.Contains(layout + "/" + kernel + "/" + batch))
                    throw new ArgumentException("The expanded matched matrix is missing " + layout + "/" + kernel + "/" + batch);
            foreach (int width in new[] { 4, 8, 16 })
            foreach (int batch in new[] { 64, 256 })
                if (!matched.Contains("AoSoA" + width + "/PackedBranchless" + width + "/" + batch))
                    throw new ArgumentException("The expanded packed matrix is incomplete.");
            if (Settings.SamplesPerCandidate < 40 || Settings.BoundarySamplesPerCandidate < 20 ||
                Settings.BootstrapIterations < 4000 || Settings.MinimumImprovementPercent != 10 ||
                Settings.BootstrapConfidenceLevel != 0.95)
                throw new ArgumentException("Formal evidence minima or decision thresholds changed.");
        }

        private static void CheckAxis(int[] actual, int[] expected)
        {
            if (actual == null || actual.Length != expected.Length) throw new ArgumentException("Grid axis changed.");
            for (int i = 0; i < actual.Length; i++)
                if (actual[i] != expected[i]) throw new ArgumentException("Grid axis changed.");
        }
    }

    [Serializable]
    public sealed class EnvelopePlayerReceipt
    {
        public string Protocol;
        public string DeclarationSha256;
        public string CandidateSetSha256;
        public string BuildIdentity;
        public string UnityVersion;
        public string Processor;
        public int LogicalProcessors;
        public int StartupJobWorkerCount;
        public int JobWorkerMaximumCount;
        public int ProcessId;
        public int ProcessIndex;
        public string OperatingSystem;
        public string ScriptingBackend;
        public bool DevelopmentBuild;
        public bool BurstEnabled;
        public string BurstAssemblyVersion;
        public string CollectionsAssemblyVersion;
        public string MathematicsAssemblyVersion;
        public int BurstIsaProbeMask;
        public string BurstIsaProbeDefinition = "Separate executed Burst AOT probe: bit0=SSE2, bit1=AVX2. This identifies the probe's dispatched ISA, not every kernel's instruction mix.";
        public string StartUtc;
        public double ElapsedSeconds;
        public int CompletedCells;
        public string Failure;
        public string UncontrolledInterference = "User applications, thermal state, clocks, affinity and caches are uncontrolled; process-cold only.";
    }

    internal sealed class EnvelopeBenchmarkRunner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Argument("-dla-envelope-run") == null) return;
            var host = new GameObject("Measured envelope runner");
            DontDestroyOnLoad(host);
            host.AddComponent<EnvelopeBenchmarkRunner>();
        }

        private IEnumerator Start()
        {
            yield return null;
            int exitCode = 0;
            try { Run(); }
            catch (Exception exception) { UnityEngine.Debug.LogException(exception); exitCode = 1; }
            Application.Quit(exitCode);
        }

        private static void Run()
        {
            if (!IsIl2CppPlayer())
                throw new InvalidOperationException("Formal envelope requires a standalone IL2CPP Release Player.");
            if (UnityEngine.Debug.isDebugBuild || !BurstCompiler.IsEnabled)
                throw new InvalidOperationException("Release Burst AOT is required.");
            string declarationJson = File.ReadAllText(Argument("-dla-envelope-run"));
            var grid = JsonUtility.FromJson<EnvelopeGridDeclaration>(declarationJson);
            grid.Validate();
            int processIndex = int.Parse(Argument("-dla-process-index"), CultureInfo.InvariantCulture);
            if (processIndex < 1 || processIndex > grid.IndependentProcesses) throw new ArgumentException("Invalid process index.");
            string output = Path.GetFullPath(Argument("-dla-output"));
            Directory.CreateDirectory(output);
            string buildIdentity = Argument("-dla-build-identity");
            if (!CandidateDefinitionProtocol.IsCanonicalSha256(buildIdentity))
                throw new ArgumentException("Build/source/compiler artifact hash is required.");
            var receipt = new EnvelopePlayerReceipt
            {
                Protocol = grid.Protocol, DeclarationSha256 = Hash(declarationJson),
                CandidateSetSha256 = CandidateDefinitionProtocol.ComputeCandidateSetSha256(grid.Candidates),
                BuildIdentity = buildIdentity, UnityVersion = Application.unityVersion,
                Processor = SystemInfo.processorType, LogicalProcessors = SystemInfo.processorCount,
                StartupJobWorkerCount = JobsUtility.JobWorkerCount,
                JobWorkerMaximumCount = JobsUtility.JobWorkerMaximumCount,
                ProcessId = Process.GetCurrentProcess().Id, ProcessIndex = processIndex,
                OperatingSystem = SystemInfo.operatingSystem, ScriptingBackend = "IL2CPP",
                BurstEnabled = BurstCompiler.IsEnabled, DevelopmentBuild = UnityEngine.Debug.isDebugBuild,
                BurstAssemblyVersion = typeof(BurstCompiler).Assembly.GetName().Version.ToString(),
                CollectionsAssemblyVersion = typeof(Unity.Collections.NativeArray<>).Assembly.GetName().Version.ToString(),
                MathematicsAssemblyVersion = typeof(Unity.Mathematics.float4).Assembly.GetName().Version.ToString(),
                BurstIsaProbeMask = EnvelopeBurstIsaProbe.Capture(),
                StartUtc = DateTime.UtcNow.ToString("O"),
            };
            WriteNew(output, "declaration", declarationJson);
            WriteNew(output, "preflight", JsonUtility.ToJson(receipt, true));
            Stopwatch timer = Stopwatch.StartNew();
            int previousWorkers = JobsUtility.JobWorkerCount;
            try
            {
                using var allocation = new UnityManagedAllocationCounter();
                allocation.Validate();
                foreach (int workers in grid.WorkerCounts)
                    if (workers > JobsUtility.JobWorkerMaximumCount)
                        throw new InvalidOperationException("Declared worker axis unavailable; no formal cell was measured.");
                int cellIndex = 0;
                foreach (int workers in grid.WorkerCounts)
                {
                    if (workers > JobsUtility.JobWorkerMaximumCount) throw new InvalidOperationException("Worker axis unavailable.");
                    JobsUtility.JobWorkerCount = workers;
                    if (JobsUtility.JobWorkerCount != workers) throw new InvalidOperationException("Worker setting was not applied.");
                    foreach (int count in grid.ElementCounts)
                    foreach (int period in grid.ColdAccessEveryTicks)
                    foreach (int lifetime in grid.LifetimeTicks)
                    {
                        var axis = new AdvantageEnvelopeAxis(count, lifetime, 1.4d * period, workers, "FrameFaithful");
                        var factory = new FrozenEnvelopeFactory(new ParticleIntegrateScenarioFactory(period), grid.Candidates);
                        var settings = JsonUtility.FromJson<CalibrationRunSettings>(JsonUtility.ToJson(grid.Settings));
                        settings.AllocationCounter = allocation;
                        settings.ElementCount = settings.HoldoutElementCount = count;
                        settings.LifetimeTicks = lifetime;
                        // Seeds are predeclared functions of process and cell, disjoint by phase.
                        uint offset = (uint)(processIndex * 1000 + cellIndex);
                        settings.CalibrationSeed = 0xA5110000u + offset;
                        settings.HoldoutSeed = 0xD84F0000u + offset;
                        settings.CandidateOrderSeed = 0xA3410000u + offset;
                        settings.BootstrapSeed = 0xB5290000u + offset;
                        string id = "p" + processIndex + "-c" + cellIndex.ToString("D2");
                        string fingerprint = Hash(receipt.BuildIdentity + "\n" + receipt.UnityVersion + "\n" +
                            receipt.Processor + "\n" + receipt.OperatingSystem + "\nworkers=" + workers);
                        WriteNew(output, id + "-settings", JsonUtility.ToJson(settings, true));
                        var measured = ScenarioCalibrationEngine.RunEnvelopeCell(factory, settings, axis, fingerprint,
                            id, new UnityEnvelopeCodec(), (name, json) => WriteNew(output, name, json));
                        // Summary references raw phase files, without duplicating large raw arrays.
                        WriteNew(output, id + "-axis", JsonUtility.ToJson(axis, true));
                        receipt.CompletedCells++;
                        cellIndex++;
                        UnityEngine.Debug.Log("Envelope cell complete " + id + ": " + measured.Envelope.Cells[0].Status);
                    }
                }
            }
            catch (Exception error) { receipt.Failure = error.ToString(); throw; }
            finally
            {
                JobsUtility.JobWorkerCount = previousWorkers;
                receipt.ElapsedSeconds = timer.Elapsed.TotalSeconds;
                WriteNew(output, "receipt", JsonUtility.ToJson(receipt, true));
            }
        }

        private static bool IsIl2CppPlayer()
        {
#if ENABLE_IL2CPP && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }

        private sealed class UnityEnvelopeCodec : IEnvelopeArtifactCodec
        {
            public string Serialize<T>(T value, bool pretty = false) => JsonUtility.ToJson(value, pretty);
            public T Deserialize<T>(string json) => JsonUtility.FromJson<T>(json);
        }

        private static void WriteNew(string directory, string name, string json)
        {
            using (var stream = new FileStream(Path.Combine(directory, name + ".json"), FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(json);
        }

        private static string Hash(string text)
        {
            using (SHA256 algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(new UTF8Encoding(false, true).GetBytes(text))).Replace("-", "");
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        private sealed class FrozenEnvelopeFactory : ICalibrationScenarioFactory
        {
            private readonly ICalibrationScenarioFactory _inner;
            private readonly CandidateDescriptor[] _candidates;
            public FrozenEnvelopeFactory(ICalibrationScenarioFactory inner, CandidateDescriptor[] candidates)
            { _inner = inner; _candidates = (CandidateDescriptor[])candidates.Clone(); }
            public ScenarioDescriptor Descriptor => _inner.Descriptor;
            public ICalibrationScenario Create(int count, uint seed, CandidateDescriptor[] candidates = null) =>
                _inner.Create(count, seed, candidates ?? _candidates);
        }
    }
}
