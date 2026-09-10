using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using Yanagisawa.DataLayoutCalibrator.Samples.ExternalWorkloads;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark
{
    // Explicit external-workload host. No registration or default layout selection.
    internal sealed class ExternalComparisonRunner : MonoBehaviour
    {
        [Serializable] internal sealed class Samples { public double[] values; }
        [Serializable] internal sealed class Result
        {
            public string workload, unity, unit = "ms", allocationEligibility = "Unknown", allocationDiagnostic;
            public int count, iterations, workers;
            public AllocationCounterCapability allocationCapability;
            public double constructMs, initMs, exportMs, disposeMs, storageLifecycleMs, sum;
            public Samples[] operationMs;
            public double[] wholeStepMs;
            public bool fullArrayCheckPassed;
        }

        internal static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Argument("-dla-external") == null) return;
            var host = new GameObject("Explicit external workload comparison");
            DontDestroyOnLoad(host); host.AddComponent<ExternalComparisonRunner>();
        }

        private IEnumerator Start()
        {
            yield return null;
            try
            {
                if (!BurstCompiler.IsEnabled) throw new InvalidOperationException("Burst must be enabled.");
                string prefix = Path.GetFullPath(Argument("-dla-external-output") ?? throw new ArgumentException("Missing output prefix"));
                if (File.Exists(prefix + ".json") || File.Exists(prefix + ".bin")) throw new IOException("Output already exists");
                var result = new Result { unity = Application.unityVersion, workers = JobsUtility.JobWorkerCount };
                // Test the formerly false-zero API in the actual runtime. This is NOT
                // allocation eligibility: worker/native coverage and workload windows are absent.
                var counter = new ThreadManagedAllocationCounter();
                try { counter.Validate(); }
                catch (Exception e) { result.allocationDiagnostic = e.Message; }
                result.allocationCapability = counter.Capability;
                if (Argument("-dla-external") == "babel") RunBabel(prefix, result);
                else if (Argument("-dla-external") == "llama") RunLlama(prefix, result);
                else throw new ArgumentException("Unknown external workload");
                File.WriteAllText(prefix + ".json", JsonUtility.ToJson(result, true));
                UnityEngine.Debug.Log("External comparison completed; full output persisted. Allocation eligibility remains Unknown.");
                Application.Quit(0);
            }
            catch (Exception e) { UnityEngine.Debug.LogException(e); Application.Quit(1); }
        }

        private static double Since(long start) => (Stopwatch.GetTimestamp() - start) * (1000.0 / Stopwatch.Frequency);

        private static void RunBabel(string prefix, Result r)
        {
            int n = BabelStreamContract.DefaultArraySize, repeats = BabelStreamContract.DefaultIterations;
            r.workload = "C#/Burst variant of BabelStream; serial contract Dot"; r.count = n; r.iterations = repeats;
            r.operationMs = new Samples[5];
            for (int i = 0; i < 5; i++) r.operationMs[i] = new Samples { values = new double[repeats] };
            // Same caller-owned canonical output contract as the native adapter.
            var outA = new double[n]; var outB = new double[n]; var outC = new double[n];
            var a = default(NativeArray<double>); var b = a; var c = a; var sum = a;
            long life = Stopwatch.GetTimestamp(), phase = life;
            try
            {
                a = new NativeArray<double>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                b = new NativeArray<double>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                c = new NativeArray<double>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                sum = new NativeArray<double>(1, Allocator.Persistent);
                new BabelInitialiseJob { A = a, B = b, C = c }.Schedule(n, 4096).Complete();
                r.constructMs = Since(phase); phase = Stopwatch.GetTimestamp();
                // Upstream OpenMP constructor initializes once, then driver initializes again.
                new BabelInitialiseJob { A = a, B = b, C = c }.Schedule(n, 4096).Complete();
                r.initMs = Since(phase);
                for (int k = 0; k < repeats; k++)
                {
                    phase = Stopwatch.GetTimestamp(); new BabelCopyJob { A = a, C = c }.Schedule(n, 4096).Complete(); r.operationMs[0].values[k] = Since(phase);
                    phase = Stopwatch.GetTimestamp(); new BabelMulJob { B = b, C = c }.Schedule(n, 4096).Complete(); r.operationMs[1].values[k] = Since(phase);
                    phase = Stopwatch.GetTimestamp(); new BabelAddJob { A = a, B = b, C = c }.Schedule(n, 4096).Complete(); r.operationMs[2].values[k] = Since(phase);
                    phase = Stopwatch.GetTimestamp(); new BabelTriadJob { A = a, B = b, C = c }.Schedule(n, 4096).Complete(); r.operationMs[3].values[k] = Since(phase);
                    phase = Stopwatch.GetTimestamp(); new BabelDotContractJob { A = a, B = b, Sum = sum }.Schedule().Complete(); r.operationMs[4].values[k] = Since(phase);
                }
                r.sum = sum[0]; phase = Stopwatch.GetTimestamp();
                a.CopyTo(outA); b.CopyTo(outB); c.CopyTo(outC); r.exportMs = Since(phase);
            }
            finally
            {
                phase = Stopwatch.GetTimestamp();
                if (a.IsCreated) a.Dispose(); if (b.IsCreated) b.Dispose(); if (c.IsCreated) c.Dispose(); if (sum.IsCreated) sum.Dispose();
                r.disposeMs = Since(phase);
            }
            r.storageLifecycleMs = Since(life);
            WriteArrays(prefix + ".bin", outA, outB, outC);
            // Persist all samples before checking, including a failed attempt.
            File.WriteAllText(prefix + ".json", JsonUtility.ToJson(r, true));
            double ga = BabelStreamContract.StartA, gb = BabelStreamContract.StartB, gc = BabelStreamContract.StartC, gs = 0;
            for (int k = 0; k < repeats; k++) BabelStreamContract.AdvanceClassicGold(ref ga, ref gb, ref gc, n, out gs);
            for (int i = 0; i < n; i++)
                if (!BabelStreamContract.Matches(outA[i], ga) || !BabelStreamContract.Matches(outB[i], gb) || !BabelStreamContract.Matches(outC[i], gc))
                    throw new InvalidOperationException("BabelStream array validation failed at " + i);
            if (!BabelStreamContract.Matches(r.sum, gs, true)) throw new InvalidOperationException("BabelStream Dot validation failed");
            r.fullArrayCheckPassed = true;
        }

        private static void WriteArrays(string path, params double[][] arrays)
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            var buffer = new byte[1024 * 1024];
            foreach (double[] a in arrays)
                for (int offset = 0; offset < a.Length * 8; offset += buffer.Length)
                {
                    int count = Math.Min(buffer.Length, a.Length * 8 - offset);
                    Buffer.BlockCopy(a, offset, buffer, 0, count); file.Write(buffer, 0, count);
                }
        }

        private static void RunLlama(string prefix, Result r)
        {
            int n = LlamaNBodyContract.DefaultCount;
            string inputPath = Argument("-dla-external-input") ?? throw new ArgumentException("Missing canonical input");
            if (new FileInfo(inputPath).Length != n * 28L) throw new ArgumentException("Exact default canonical input required");
            using var input = new NativeArray<LlamaParticle>(n, Allocator.Persistent);
            using var output = new NativeArray<LlamaParticle>(n, Allocator.Persistent);
            var writableInput = input; // NativeArray indexer mutates the struct; the using owner remains readonly.
            using (var reader = new BinaryReader(File.OpenRead(inputPath)))
                for (int i = 0; i < n; i++) writableInput[i] = new LlamaParticle {
                    Position = new float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    Velocity = new float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()), Mass = reader.ReadSingle() };
            r.workload = "C#/Burst port of LLAMA code_comp; packed4"; r.count = n; r.iterations = LlamaNBodyContract.DefaultSteps;
            r.wholeStepMs = new double[r.iterations];
            long life = Stopwatch.GetTimestamp(), phase = life;
            var storage = LlamaNBodyPackedStorage.FromRecords(input, Allocator.Persistent);
            r.constructMs = Since(phase);
            try
            {
                for (int k = 0; k < r.iterations; k++) { phase = Stopwatch.GetTimestamp(); storage.ScheduleStep(256).Complete(); r.wholeStepMs[k] = Since(phase); }
                phase = Stopwatch.GetTimestamp(); storage.Export(output); r.exportMs = Since(phase);
            }
            finally { phase = Stopwatch.GetTimestamp(); storage.Dispose(); r.disposeMs = Since(phase); }
            r.storageLifecycleMs = Since(life);
            using var writer = new BinaryWriter(new FileStream(prefix + ".bin", FileMode.CreateNew, FileAccess.Write));
            for (int i = 0; i < n; i++) { var p = output[i]; writer.Write(p.Position.x); writer.Write(p.Position.y); writer.Write(p.Position.z);
                writer.Write(p.Velocity.x); writer.Write(p.Velocity.y); writer.Write(p.Velocity.z); writer.Write(p.Mass); }
            // Full numerical parity is checked offline against the persisted native output.
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict)]
    internal struct BabelInitialiseJob : IJobParallelFor
    {
        [WriteOnly] public NativeArray<double> A, B, C;
        public void Execute(int i) { A[i] = BabelStreamContract.StartA; B[i] = BabelStreamContract.StartB; C[i] = BabelStreamContract.StartC; }
    }
}
