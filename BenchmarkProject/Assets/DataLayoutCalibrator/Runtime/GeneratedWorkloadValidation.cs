using System;
using System.Collections.Generic;
using System.IO;
using Unity.Burst;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using Yanagisawa.DataLayoutCalibrator.Generated;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark
{
    /// <summary>Runs the actual registered production candidates, including their boundaries.
    /// This is correctness/allocation evidence, never a performance selection.</summary>
    internal static class GeneratedWorkloadValidation
    {
        [Serializable]
        private sealed class Receipt
        {
            public string Scope = "Actual registered workload correctness, lifecycle and main-thread steady-state allocation; not timing evidence";
            public string UnityVersion, Backend, Processor, OperatingSystem, Error;
            public bool Passed, Release, BurstEnabled;
            public int WorkerCount;
            public string AllocationProvider, AllocationValueUnit;
            public bool AllocationCounterValidated;
            public long AllocationCounterPositiveControlBytes = -1;
            public long AllocationCounterPositiveControlEvents;
            public Row[] Candidates;
        }

        [Serializable]
        private sealed class Row
        {
            public string Scenario, Candidate, StateHash;
            public int Count;
            public uint Seed;
            public long IngressAllocatedBytes = -1, ExecuteAllocatedBytes = -1, ExportAllocatedBytes = -1;
            public long IngressAllocationEvents = -1, ExecuteAllocationEvents = -1, ExportAllocationEvents = -1;
            public bool ParityPassed, ResetPassed, DisposedAccessRejected;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-dla-generated-workloads") < 0) return;
            int outputIndex = Array.IndexOf(args, "-dla-output");
            string output = outputIndex >= 0 && outputIndex + 1 < args.Length ? args[outputIndex + 1] : Application.persistentDataPath;
            var receipt = new Receipt
            {
                UnityVersion = Application.unityVersion,
#if ENABLE_IL2CPP
                Backend = "IL2CPP",
#else
                Backend = "Mono",
#endif
                Processor = SystemInfo.processorType, OperatingSystem = SystemInfo.operatingSystem,
                Release = !Debug.isDebugBuild, BurstEnabled = BurstCompiler.IsEnabled,
                WorkerCount = JobsUtility.JobWorkerCount,
            };
            var rows = new List<Row>();
            try
            {
                if (!receipt.Release || !receipt.BurstEnabled || Application.isEditor)
                    throw new InvalidOperationException("Validation requires a Release Player with Burst enabled.");
                receipt.AllocationProvider = UnityAllocationRecorder.Provider;
                using (var allocation = new UnityAllocationRecorder())
                {
                    var control = allocation.ValidatePositiveAndEmptyControls();
                    receipt.AllocationCounterPositiveControlBytes = control.Bytes;
                    receipt.AllocationCounterPositiveControlEvents = control.Events;
                    receipt.AllocationValueUnit = allocation.Unit;
                    receipt.AllocationCounterValidated = true;
                    ICalibrationScenarioFactory[] factories = GeneratedCalibrationScenarioRegistry.CreateFactories();
                    ICalibrationScenarioFactory[] again = GeneratedCalibrationScenarioRegistry.CreateFactories();
                    if (factories.Length != again.Length) throw new InvalidOperationException("Registry is nondeterministic.");
                    int workloads = 0;
                    for (int f = 0; f < factories.Length; f++)
                    {
                        string id = factories[f].Descriptor.ScenarioId;
                        if (id != again[f].Descriptor.ScenarioId) throw new InvalidOperationException("Registry order differs.");
                        if (id != "particle-integrate-v2" && id != "transform-export-v1") continue;
                        workloads++;
                        foreach (int count in new[] { 1, 3, 4, 5, 7, 8, 9, 15, 16, 17, 4099 })
                        foreach (uint seed in new[] { 137u, 9137u })
                            Validate(factories[f], count, seed, rows, allocation);
                    }
                    if (workloads != 2) throw new InvalidOperationException("Both production workload factories must be registered.");
                    allocation.ValidatePositiveAndEmptyControls();
                    receipt.Passed = true;
                }
            }
            catch (Exception exception)
            {
                receipt.Error = exception.ToString();
                Debug.LogException(exception);
            }
            receipt.Candidates = rows.ToArray();
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "generated-workload-validation.json"), JsonUtility.ToJson(receipt, true));
            Debug.Log($"Generated production workload validation: passed={receipt.Passed}, rows={rows.Count}, backend={receipt.Backend}");
            Application.Quit(receipt.Passed ? 0 : 1);
        }

        private static void Validate(ICalibrationScenarioFactory factory, int count, uint seed, List<Row> rows, UnityAllocationRecorder allocation)
        {
            using (ICalibrationScenario scenario = factory.Create(count, seed))
            {
                var candidates = new ICalibrationCandidate[scenario.CandidateCount];
                var local = new Row[candidates.Length];
                for (int i = 0; i < candidates.Length; i++)
                {
                    var candidate = candidates[i] = scenario.GetCandidate(i);
                    var row = local[i] = new Row { Scenario = factory.Descriptor.ScenarioId,
                        Candidate = candidate.Descriptor.CandidateId, Count = count, Seed = seed };
                    rows.Add(row);
                    // Warm all static generic schedule wrappers and runtime bookkeeping.
                    for (int warm = 0; warm < 16; warm++)
                    {
                        candidate.BoundaryCost.Ingress(); candidate.Execute(3, 1f / 60f); candidate.BoundaryCost.Export();
                    }
                    allocation.Begin();
                    for (int repeat = 0; repeat < 16; repeat++) candidate.BoundaryCost.Ingress();
                    var ingressObservation = allocation.End();
                    row.IngressAllocatedBytes = ingressObservation.Bytes;
                    row.IngressAllocationEvents = ingressObservation.Events;
                    allocation.Begin();
                    for (int repeat = 0; repeat < 16; repeat++) candidate.Execute(3, 1f / 60f);
                    var executeObservation = allocation.End();
                    row.ExecuteAllocatedBytes = executeObservation.Bytes;
                    row.ExecuteAllocationEvents = executeObservation.Events;
                    allocation.Begin();
                    for (int repeat = 0; repeat < 16; repeat++) candidate.BoundaryCost.Export();
                    var exportObservation = allocation.End();
                    row.ExportAllocatedBytes = exportObservation.Bytes;
                    row.ExportAllocationEvents = exportObservation.Events;
                    row.StateHash = candidate.ExportedStateHash;
                    if (row.IngressAllocationEvents != 0 || row.ExecuteAllocationEvents != 0 || row.ExportAllocationEvents != 0)
                        throw new InvalidOperationException("Steady-state managed allocation: " + row.Candidate);
                }
                var reference = candidates[scenario.ReferenceCandidateIndex];
                for (int i = 0; i < candidates.Length; i++)
                {
                    var parity = scenario.ParityValidator.Validate(reference, candidates[i], 1e-5f);
                    local[i].ParityPassed = parity.Passed;
                    if (!parity.Passed) throw new InvalidOperationException(local[i].Candidate + ": " + parity.Reason);
                }
                for (int i = 0; i < candidates.Length; i++)
                {
                    var candidate = candidates[i];
                    candidate.BoundaryCost.Ingress(); candidate.Execute(48, 1f / 60f); candidate.BoundaryCost.Export();
                    local[i].ResetPassed = candidate.ExportedStateHash == local[i].StateHash;
                    if (!local[i].ResetPassed) throw new InvalidOperationException("Ingress failed to restore complete state.");
                    candidate.Dispose(); candidate.Dispose();
                    try { candidate.BoundaryCost.Ingress(); }
                    catch (ObjectDisposedException) { local[i].DisposedAccessRejected = true; }
                    if (!local[i].DisposedAccessRejected) throw new InvalidOperationException("Disposed candidate accepted ingress.");
                }
                allocation.ValidatePositiveAndEmptyControls();
                scenario.Dispose(); // Subsequent using disposal must also be inert.
            }
        }
    }
}
