using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using Yanagisawa.DataLayoutCalibrator.Generated;
using Hash = Yanagisawa.DataLayoutCalibrator.WindowsProcessCycleCounterProvider;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark
{
    /// <summary>Opt-in diagnostic evidence. Never feeds a calibration decision or a deployment profile.</summary>
    public static class CpuCounterEvidenceRunner
    {
        [Serializable] public sealed class Identity
        {
            public string RunId, ProcessEvidenceId, Cpu, OperatingSystem, ProcessArchitecture, RuntimeBurstTargetCapabilities;
            public string IsaScope = "Capabilities returned inside a dispatched Burst job; not instruction-by-instruction disassembly or a per-kernel ISA claim.";
            public string UnityVersion, Backend, BuildType, SourceIdentityStatus;
            public int ProcessId, LogicalProcessors, JobWorkers;
            public Binary[] Binaries;
        }
        [Serializable] public sealed class Binary { public string Path, Sha256; }
        [Serializable] public sealed class CandidateSetFile { public int SchemaVersion; public ScenarioCandidates[] Scenarios; }
        [Serializable] public sealed class ScenarioCandidates { public string ScenarioId; public CandidateDescriptor[] Candidates; }
        [Serializable] public sealed class MetricAvailability
        {
            public string MetricId, Status, Code, Reason;
        }
        [Serializable] public sealed class Row
        {
            public string ScenarioId, DatasetHash, CandidateId, CandidateDefinitionSha256, StateHash;
            public CandidateDescriptor Candidate;
            public int Pair, OrderPosition, Ticks, ElementCount;
            public bool Enabled;
            public double EndToEndNanoseconds;
            public CounterCaptureResult Capture;
        }
        [Serializable] public sealed class CandidateSummary
        {
            public string ScenarioId, CandidateId;
            public CounterOverheadMetadata Overhead;
            public bool ParityPassed;
            public long ResidentAllocationBytes, IngressAllocationBytes, ExportAllocationBytes;
        }
        [Serializable] public sealed class Evidence
        {
            public int SchemaVersion = 1;
            public Identity Identity;
            public string Scope = "Diagnostic same-process counter correlation and complete-adapter on/off overhead; no layout winner promotion. Whole-process cycles include all Unity/job/background threads, kernel time, scheduling and collection boundary overhead. No GPU counters.";
            public string Protocol = "Per candidate paired AB/BA alternating order, reset before each arm; same input/ticks; warm both paths. EndToEndNanoseconds includes CounterCaptureRunner, Probe, Begin, Complete, raw artifact persistence and Dispose; excludes reset, export/hash, and final JSON serialization. Retain negative/noisy overhead deltas.";
            public int ElementCount, Ticks, Pairs;
            public int CollectedCaptures, UnavailableCaptures, FailedCaptures;
            public string ActualCounterGate = "not-run";
            public string CandidateSetSha256;
            public MetricAvailability[] Metrics;
            public Row[] Rows;
            public CandidateSummary[] Summaries;
            public string Failure;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (!Has("-dla-counter-run")) return;
            string output = Path.GetFullPath(Value("-dla-output") ?? Path.Combine(Application.persistentDataPath, "cpu-counters", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(output);
            if (File.Exists(Path.Combine(output, "cpu-counter-evidence.json")))
            {
                UnityEngine.Debug.LogError("Refusing to overwrite retained CPU counter evidence: " + output);
                Application.Quit(2); return;
            }
            var evidence = new Evidence();
            var rows = new List<Row>();
            var summaries = new List<CandidateSummary>();
            try
            {
                if (Has("-dla-run")) throw new ArgumentException("Use -dla-counter-run separately from -dla-run to avoid overlapping runners.");
                if (Application.isEditor || UnityEngine.Debug.isDebugBuild || !BurstCompiler.IsEnabled)
                    throw new InvalidOperationException("Counter evidence requires a non-Development Release Player with Burst enabled.");
                evidence.ElementCount = Integer("-dla-counter-count", 4099, 1, 1048576);
                evidence.Ticks = Integer("-dla-counter-ticks", 32, 1, 4096);
                evidence.Pairs = Integer("-dla-counter-pairs", 8, 2, 128);
                evidence.Identity = CaptureIdentity();
                string buildManifest = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "source-identity.json"));
                if (File.Exists(buildManifest)) File.Copy(buildManifest, Path.Combine(output, "source-identity.json"), false);
                CandidateSetFile candidateSets = null;
                string candidateFile = Value("-dla-counter-candidates");
                if (candidateFile != null)
                {
                    candidateSets = JsonUtility.FromJson<CandidateSetFile>(File.ReadAllText(candidateFile));
                    if (candidateSets == null || candidateSets.SchemaVersion != 1 || candidateSets.Scenarios == null)
                        throw new ArgumentException("Invalid frozen counter candidate-set file.");
                    evidence.CandidateSetSha256 = Hash.HashFile(candidateFile);
                    File.Copy(candidateFile, Path.Combine(output, "counter-candidates.json"), false);
                }
                string identityJson = JsonUtility.ToJson(evidence.Identity);
                string environmentHash = Hash.HashText(identityJson);
                string cpuHash = Hash.HashText(evidence.Identity.Cpu + "|" + evidence.Identity.ProcessArchitecture + "|" + evidence.Identity.RuntimeBurstTargetCapabilities);
                string settingsHash = Hash.HashText(evidence.ElementCount + "|" + evidence.Ticks + "|" + evidence.Pairs + "|ABBA-v1|" + evidence.CandidateSetSha256);
                string implementation = ImplementationBinary();
                ICounterProvider provider = Has("-dla-counter-no-provider") ? null : new Hash(implementation, Path.Combine(output, "raw"));
                CounterProviderAvailability availability = provider == null
                    ? CounterProviderAvailability.Unavailable("provider-not-configured", "No provider configured by explicit command line.") : provider.Probe();
                evidence.Metrics = Metrics(availability);
                string filter = Value("-dla-counter-scenarios") ?? "spatial-neighborhood-v1,animation-state-v1";
                var selected = new HashSet<string>(filter.Split(','), StringComparer.Ordinal);
                foreach (ICalibrationScenarioFactory factory in GeneratedCalibrationScenarioRegistry.CreateFactories())
                {
                    if (!selected.Remove(factory.Descriptor.ScenarioId)) continue;
                    using (ICalibrationScenario scenario = factory.Create(evidence.ElementCount, 0x19283745, ResolveCandidates(candidateSets, factory.Descriptor.ScenarioId)))
                    {
                        ICalibrationCandidate reference = scenario.GetCandidate(scenario.ReferenceCandidateIndex);
                        reference.BoundaryCost.Ingress(); reference.Execute(evidence.Ticks, 1f / 60f);
                        for (int c = 0; c < scenario.CandidateCount; c++)
                        {
                            ICalibrationCandidate candidate = scenario.GetCandidate(c);
                            candidate.BoundaryCost.Ingress(); candidate.Execute(evidence.Ticks, 1f / 60f);
                            ParityReport parity = scenario.ParityValidator.Validate(reference, candidate, 1e-5f);
                            if (!parity.Passed) throw new InvalidOperationException("Counter candidate parity failed: " + parity.Reason);
                            var context = new CounterCaptureContext
                            {
                                RunId = evidence.Identity.RunId, ScenarioId = factory.Descriptor.ScenarioId, ContractVersion = factory.Descriptor.ContractVersion,
                                CandidateId = candidate.Descriptor.CandidateId, CandidateSchemaSha256 = CandidateDefinitionProtocol.ComputeCandidateDefinitionSha256(candidate.Descriptor),
                                Phase = BenchmarkPhase.Calibration, ElementCount = evidence.ElementCount,
                                ProcessEvidenceId = evidence.Identity.ProcessEvidenceId, DeviceId = "cpu-" + cpuHash,
                                DeviceIdentitySha256 = cpuHash, EnvironmentFingerprintSha256 = environmentHash, SettingsFingerprintSha256 = settingsHash,
                            };
                            Action action = () => candidate.Execute(evidence.Ticks, 1f / 60f);
                            // Warm allocations and both collection paths before overhead samples.
                            candidate.BoundaryCost.Ingress(); CounterCaptureRunner.Capture(provider, false, context, action);
                            candidate.BoundaryCost.Ingress(); CounterCaptureRunner.Capture(provider, true, context, action);
                            candidate.BoundaryCost.Ingress(); action(); candidate.BoundaryCost.Export();
                            long before = GC.GetAllocatedBytesForCurrentThread(); action();
                            long residentAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
                            before = GC.GetAllocatedBytesForCurrentThread(); candidate.BoundaryCost.Ingress();
                            long ingressAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
                            before = GC.GetAllocatedBytesForCurrentThread(); candidate.BoundaryCost.Export();
                            long exportAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
                            var disabled = new double[evidence.Pairs]; var enabled = new double[evidence.Pairs];
                            string expectedHash = null;
                            for (int pair = 0; pair < evidence.Pairs; pair++)
                            for (int position = 0; position < 2; position++)
                            {
                                bool on = (pair % 2 == 0) == (position == 1);
                                candidate.BoundaryCost.Ingress(); context.RoundIndex = pair;
                                long start = Stopwatch.GetTimestamp();
                                CounterCaptureResult capture = CounterCaptureRunner.Capture(provider, on, context, action);
                                long end = Stopwatch.GetTimestamp();
                                double ns = (end - start) * (1e9 / Stopwatch.Frequency);
                                candidate.BoundaryCost.Export(); string state = candidate.ExportedStateHash;
                                if (expectedHash == null) expectedHash = state;
                                if (state != expectedHash) throw new InvalidOperationException("Provider on/off changed workload state.");
                                if (on) enabled[pair] = ns; else disabled[pair] = ns;
                                rows.Add(new Row { ScenarioId = factory.Descriptor.ScenarioId, DatasetHash = scenario.DatasetHash,
                                    Candidate = candidate.Descriptor, CandidateId = context.CandidateId, CandidateDefinitionSha256 = context.CandidateSchemaSha256,
                                    Pair = pair, OrderPosition = position, Enabled = on, Ticks = evidence.Ticks, ElementCount = evidence.ElementCount,
                                    EndToEndNanoseconds = ns, StateHash = state, Capture = capture });
                                if (on)
                                {
                                    if (capture.Status == CounterCollectionStatus.Collected) evidence.CollectedCaptures++;
                                    else if (capture.Status == CounterCollectionStatus.Unavailable) evidence.UnavailableCaptures++;
                                    else evidence.FailedCaptures++;
                                }
                            }
                            summaries.Add(new CandidateSummary { ScenarioId = factory.Descriptor.ScenarioId, CandidateId = context.CandidateId,
                                ParityPassed = true, ResidentAllocationBytes = residentAllocation, IngressAllocationBytes = ingressAllocation, ExportAllocationBytes = exportAllocation,
                                Overhead = CounterOverheadEstimator.Estimate(disabled, enabled, "alternating-AB-BA-full-adapter-including-raw-persistence") });
                            if (residentAllocation != 0 || ingressAllocation != 0 || exportAllocation != 0)
                                throw new InvalidOperationException("Workload steady-state or boundary allocated managed bytes.");
                        }
                    }
                }
                if (selected.Count != 0) throw new ArgumentException("Unregistered scenario IDs: " + string.Join(",", selected));
                evidence.ActualCounterGate = evidence.FailedCaptures != 0 ? "failed" : evidence.UnavailableCaptures != 0 ? "unavailable" :
                    evidence.CollectedCaptures > 0 ? "passed-process-cycles-only" : "failed-no-captures";
                if (evidence.FailedCaptures != 0 || (availability.Status == CounterProviderAvailabilityStatus.Available && evidence.CollectedCaptures == 0))
                    throw new InvalidOperationException("Actual CPU counter gate failed; inspect retained capture StatusCode/StatusReason.");
            }
            catch (Exception exception) { evidence.Failure = exception.ToString(); evidence.ActualCounterGate = "failed"; UnityEngine.Debug.LogException(exception); }
            finally
            {
                evidence.Rows = rows.ToArray(); evidence.Summaries = summaries.ToArray();
                File.WriteAllText(Path.Combine(output, "cpu-counter-evidence.json"), JsonUtility.ToJson(evidence, true));
                UnityEngine.Debug.Log("[DataLayoutCalibrator] CPU counter evidence: " + output);
                if (Application.isBatchMode || Has("-dla-quit")) Application.Quit(evidence.Failure == null ? 0 : 2);
            }
        }

        private static CandidateDescriptor[] ResolveCandidates(CandidateSetFile file, string scenarioId)
        {
            if (file == null) return null;
            CandidateDescriptor[] found = null;
            foreach (ScenarioCandidates row in file.Scenarios)
            {
                if (row.ScenarioId != scenarioId) continue;
                if (found != null || row.Candidates == null || row.Candidates.Length == 0)
                    throw new ArgumentException("Duplicate or empty frozen candidate set: " + scenarioId);
                found = row.Candidates;
                CandidateDefinitionProtocol.ComputeCandidateSetSha256(found);
            }
            if (found == null) throw new ArgumentException("Frozen candidate file omits requested scenario: " + scenarioId);
            return found;
        }

        private static Identity CaptureIdentity()
        {
            string run = Guid.NewGuid().ToString("N");
            using (Process process = Process.GetCurrentProcess())
            {
                var binaries = new List<Binary>();
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                foreach (string path in new[] { process.MainModule.FileName, Path.Combine(root, "UnityPlayer.dll"), ImplementationBinary(),
                    Path.Combine(Application.dataPath, "Plugins", "x86_64", "lib_burst_generated.dll"), Path.Combine(root, "source-identity.json"),
                    Path.Combine(Environment.SystemDirectory, "kernel32.dll"), Path.Combine(Environment.SystemDirectory, "KernelBase.dll") })
                    if (File.Exists(path)) binaries.Add(new Binary { Path = path, Sha256 = Hash.HashFile(path) });
                string managed = Path.Combine(Application.dataPath, "Managed");
                if (Directory.Exists(managed))
                    foreach (string path in Directory.GetFiles(managed, "Yanagisawa*.dll"))
                        if (!binaries.Exists(b => string.Equals(b.Path, path, StringComparison.OrdinalIgnoreCase)))
                            binaries.Add(new Binary { Path = path, Sha256 = Hash.HashFile(path) });
                var isa = new NativeArray<int>(1, Allocator.TempJob);
                string capabilities;
                try { new CounterIsaIdentityJob { Result = isa }.Schedule().Complete(); capabilities = isa[0] == 2 ? "AVX2" : isa[0] == 1 ? "SSE2" : "unavailable"; }
                finally { isa.Dispose(); }
                return new Identity { RunId = run, ProcessId = process.Id,
                    ProcessEvidenceId = process.Id + "-" + process.StartTime.ToUniversalTime().Ticks + "-" + run,
                    Cpu = SystemInfo.processorType, OperatingSystem = SystemInfo.operatingSystem,
                    ProcessArchitecture = IntPtr.Size == 8 ? "64-bit-Windows-player" : "32-bit-Windows-player", RuntimeBurstTargetCapabilities = capabilities,
                    UnityVersion = Application.unityVersion, Backend = Backend(), BuildType = UnityEngine.Debug.isDebugBuild ? "Development" : "Release",
                    LogicalProcessors = SystemInfo.processorCount, JobWorkers = JobsUtility.JobWorkerCount, Binaries = binaries.ToArray(),
                    SourceIdentityStatus = File.Exists(Path.Combine(root, "source-identity.json")) ? "captured-build-source-manifest" : "unavailable-no-build-source-manifest" };
            }
        }
        private static string ImplementationBinary()
        {
#if ENABLE_IL2CPP
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "GameAssembly.dll"));
#else
            return typeof(Hash).Assembly.Location;
#endif
        }
        private static string Backend()
        {
#if ENABLE_IL2CPP
            return "IL2CPP";
#else
            return "Mono";
#endif
        }
        private static MetricAvailability[] Metrics(CounterProviderAvailability a) => new[]
        {
            new MetricAvailability { MetricId = Hash.CounterId, Status = a.Status.ToString(), Code = a.Code, Reason = a.Reason },
            Missing("retired-instructions"), Missing("cache-references"), Missing("cache-misses"), Missing("branch-instructions"), Missing("branch-misses"),
        };
        private static MetricAvailability Missing(string metric) => new MetricAvailability { MetricId = metric, Status = "Unavailable",
            Code = "not-exposed-by-query-process-cycle-time", Reason = "This OS API exposes process cycle accounting only. No configured PMU provider/event mapping; hardware support and capture privileges are not inferred. No value emitted." };
        private static int Integer(string name, int fallback, int minimum, int maximum)
        {
            string value = Value(name); if (value == null) return fallback;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < minimum || n > maximum)
                throw new ArgumentOutOfRangeException(name);
            return n;
        }
        private static bool Has(string name) => Array.Exists(Environment.GetCommandLineArgs(), s => s == name);
        private static string Value(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }

    [BurstCompile]
    public struct CounterIsaIdentityJob : IJob
    {
        public NativeArray<int> Result;
        public void Execute() => Result[0] = X86.Avx2.IsAvx2Supported ? 2 : X86.Sse2.IsSse2Supported ? 1 : 0;
    }
}
