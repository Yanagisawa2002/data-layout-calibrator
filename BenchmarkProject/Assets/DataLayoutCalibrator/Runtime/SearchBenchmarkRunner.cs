using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Burst;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark
{
    /// <summary>Explicit opt-in; never changes the ordinary suite or deployment defaults.</summary>
    internal sealed class SearchBenchmarkRunner : MonoBehaviour
    {
        [Serializable] private sealed class FrozenCandidates
        {
            public string CandidateSetSha256;
            public CandidateDescriptor[] Candidates;
        }

        [Serializable] private sealed class SearchEnvironment
        {
            public string RunId;
            public int ProcessId;
            public string CreatedUtc;
            public string Processor;
            public string OperatingSystem;
            public string UnityVersion;
            public int WorkerCount;
            public bool BurstEnabled;
            public string Backend;
            public bool Development;
            public string CandidateFileSha256;
            public string Scope = "process receipt; external runner must bind source/compiler/ISA and actual Player/GameAssembly/Burst library file hashes";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-dla-search-run") < 0) return;
            if (BenchmarkConfiguration.ShouldRun()) throw new InvalidOperationException("Do not combine ordinary and search runners.");
            var host = new GameObject("Search Comparison Runner");
            DontDestroyOnLoad(host); host.AddComponent<SearchBenchmarkRunner>();
        }

        private IEnumerator Start()
        {
            yield return null;
            try { Run(); }
            catch (Exception error) { Debug.LogException(error); Application.Quit(1); yield break; }
            Application.Quit(0);
        }

        private static void Run()
        {
            if (!BurstCompiler.IsEnabled) throw new InvalidOperationException("Burst must be enabled.");
            BenchmarkConfiguration config = BenchmarkConfiguration.FromCommandLine();
            string candidatePath = ReadArgument("-dla-search-candidates");
            string order = ReadArgument("-dla-search-order");
            if (order != "adaptive-first" && order != "exhaustive-first")
                throw new ArgumentException("Declare -dla-search-order adaptive-first or exhaustive-first.");
            byte[] bytes = File.ReadAllBytes(candidatePath);
            FrozenCandidates input = JsonUtility.FromJson<FrozenCandidates>(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
            if (CandidateDefinitionProtocol.ComputeCandidateSetSha256(input.Candidates) != input.CandidateSetSha256)
                throw new InvalidOperationException("Frozen candidate file has an invalid pool hash.");
            if (Directory.Exists(config.OutputDirectory) && Directory.GetFileSystemEntries(config.OutputDirectory).Length > 0)
                throw new IOException("Use a fresh search output directory; evidence cannot be overwritten.");
            Directory.CreateDirectory(config.OutputDirectory);
            var environment = new SearchEnvironment
            {
                RunId = Guid.NewGuid().ToString("N"), ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
                CreatedUtc = DateTime.UtcNow.ToString("O"), Processor = SystemInfo.processorType,
                OperatingSystem = SystemInfo.operatingSystem, UnityVersion = Application.unityVersion,
                WorkerCount = JobsUtility.JobWorkerCount, BurstEnabled = BurstCompiler.IsEnabled,
#if ENABLE_IL2CPP
                Backend = "IL2CPP",
#else
                Backend = "Mono",
#endif
                Development = Debug.isDebugBuild, CandidateFileSha256 = HashBytes(bytes),
            };
            string fingerprint = Save(config.OutputDirectory, "environment", environment);
            Save(config.OutputDirectory, "candidate-input", input);
            using var allocation = new UnityManagedAllocationCounter();
            allocation.Validate();
            var settings = new CalibrationRunSettings
            {
                AllocationCounter = allocation,
                RequiredAllocationScope = config.RequiredAllocationScope,
                ElementCount = config.ElementCount, HoldoutElementCount = config.HoldoutElementCount,
                CalibrationSeed = ParticleDataSet.CalibrationSeed, HoldoutSeed = ParticleDataSet.HoldoutSeed,
                WarmupBlocks = config.WarmupBlocks, MinimumWarmupSeconds = config.MinimumWarmupSeconds,
                SamplesPerCandidate = config.SamplesPerCandidate, BoundarySamplesPerCandidate = config.BoundarySamplesPerCandidate,
                LifetimeTicks = config.LifetimeTicks, TargetBlockMilliseconds = config.TargetBlockMilliseconds,
                MaximumTicksPerBlock = config.MaximumTicksPerBlock, MinimumImprovementPercent = config.MinimumImprovementPercent,
                BootstrapIterations = config.BootstrapIterations, BootstrapConfidenceLevel = config.BootstrapConfidenceLevel,
            };
            BenchmarkSourceIdentity.Bind(settings, new ParticleIntegrateScenarioFactory().Descriptor);
            var policies = new SortedSet<string>(StringComparer.Ordinal);
            foreach (CandidateDescriptor candidate in input.Candidates) policies.Add(candidate.EffectiveExecution.PolicyId);
            foreach (string policy in policies)
            {
                var candidates = new List<CandidateDescriptor>();
                foreach (CandidateDescriptor candidate in input.Candidates)
                    if (candidate.EffectiveExecution.PolicyId == policy) candidates.Add(candidate);
                string directory = Path.Combine(config.OutputDirectory, policy);
                Directory.CreateDirectory(directory);
                // Default Particle contract accesses 28 hot bytes; 20 cold bytes are preserved at boundaries.
                var axis = new AdvantageEnvelopeAxis(settings.ElementCount, settings.LifetimeTicks,
                    28d / 20d, JobsUtility.JobWorkerCount, policy);
                SearchComparisonResult result = ScenarioCalibrationEngine.RunSearchComparison(
                    new ParticleIntegrateScenarioFactory(), settings, candidates.ToArray(), axis,
                    fingerprint, order == "adaptive-first", (name, value) => Save(directory, name, value));
                Save(directory, "comparison", result);
            }
        }

        private static string ReadArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            throw new ArgumentException("Missing " + name);
        }

        private static string Save(string directory, string name, object value)
        {
            string path = Path.Combine(directory, name + ".json");
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(value, true));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(); }
            return HashBytes(File.ReadAllBytes(path));
        }

        private static string HashBytes(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
        }
    }
}
