// C# / Burst port of BabelStream operations, not an original BabelStream result.
// Copyright 2015-16 Tom Deakin, Simon McIntosh-Smith, University of Bristol HPC.
// Derivative terms and run rules: ../Upstream~/BabelStream/LICENSE.
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ExternalWorkloads
{
    public enum BabelStreamOperation { Copy, Mul, Add, Triad, Nstream }

    public static class BabelStreamContract
    {
        public const string UpstreamCommit = "17ab377b0e919e14fd3df2b67268761fdac8abb3";
        public const string EvidenceStatus = "Unmeasured";
        public const int DefaultArraySize = 33554432, DefaultIterations = 100;
        public const double StartA = 0.1, StartB = 0.2, StartC = 0.0, Scalar = 0.4;
        // numeric_limits<double>::epsilon(), not System.Double.Epsilon.
        public const double MachineEpsilon = 2.2204460492503131e-16;

        public static bool Matches(double actual, double expected, bool dot = false)
        {
            double limit = MachineEpsilon * (dot ? 10000000.0 : 100.0);
            return !double.IsNaN(actual) && !double.IsInfinity(actual) && !double.IsNaN(expected) && !double.IsInfinity(expected)
                && Math.Abs(actual - expected) <= Math.Max(Math.Abs(actual), Math.Abs(expected)) * limit;
        }

        // Exactly the five classic operations; nstream is an upstream optional selection.
        public static void AdvanceClassicGold(ref double a, ref double b, ref double c, int count, out double dot)
        {
            c = a; b = Scalar * c; c = a + b; a = b + Scalar * c; dot = a * b * count;
        }

    }

    // Separate entrypoints preserve upstream pass boundaries and field traffic.
    // No initialization, timing, repeat loop, default allocation, or registration.
    [BurstCompile] public struct BabelCopyJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<double> A;
        [WriteOnly] public NativeArray<double> C;
        public void Execute(int i) { C[i] = A[i]; }
    }
    [BurstCompile] public struct BabelMulJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<double> C;
        [WriteOnly] public NativeArray<double> B;
        public void Execute(int i) { B[i] = BabelStreamContract.Scalar * C[i]; }
    }
    [BurstCompile] public struct BabelAddJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<double> A, B;
        [WriteOnly] public NativeArray<double> C;
        public void Execute(int i) { C[i] = A[i] + B[i]; }
    }
    [BurstCompile] public struct BabelTriadJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<double> B, C;
        [WriteOnly] public NativeArray<double> A;
        public void Execute(int i) { A[i] = B[i] + BabelStreamContract.Scalar * C[i]; }
    }
    [BurstCompile] public struct BabelNstreamJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<double> B, C;
        public NativeArray<double> A;
        public void Execute(int i) { A[i] += B[i] + BabelStreamContract.Scalar * C[i]; }
    }
    [BurstCompile] public struct BabelDotContractJob : IJob
    {
        [ReadOnly] public NativeArray<double> A, B;
        [WriteOnly] public NativeArray<double> Sum;
        public void Execute()
        {
            double sum = 0;
            for (int i = 0; i < A.Length; i++) sum += A[i] * B[i];
            Sum[0] = sum;
        }
    }

    // Keep the original serial job above as the reproducible baseline. This API
    // owns no storage: callers keep all arrays alive until the returned handle
    // completes, and complete the previous reduction before reusing scratch.
    public static class BabelDotReduction
    {
        public const int DefaultChunkLength = 65536;

        public static int PartialCount(int length, int chunkLength = DefaultChunkLength)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (chunkLength <= 0) throw new ArgumentOutOfRangeException(nameof(chunkLength));
            return length / chunkLength + (length % chunkLength == 0 ? 0 : 1);
        }

        public static JobHandle Schedule(NativeArray<double> a, NativeArray<double> b,
            NativeArray<BabelDotPartial> partials, NativeArray<double> sum,
            int chunkLength = DefaultChunkLength, JobHandle dependency = default)
        {
            int count = PartialCount(a.Length, chunkLength);
            if (!a.IsCreated || !b.IsCreated || a.Length != b.Length)
                throw new ArgumentException("Dot inputs must be created and have equal lengths.");
            if (!partials.IsCreated || partials.Length != count || !sum.IsCreated || sum.Length != 1)
                throw new ArgumentException("Dot requires exact-size caller-owned partials and one output.");
            // Every slot is assigned on every invocation; no read of stale or
            // uninitialized scratch, and no separate per-call clear is needed.
            var chunks = new BabelDotPartialJob { A = a, B = b, Partials = partials, ChunkLength = chunkLength }
                .Schedule(count, 1, dependency);
            return new BabelDotMergeJob { Partials = partials, Sum = sum }.Schedule(chunks);
        }

        internal static void Add(ref double sum, ref double correction, double value)
        {
            double next = sum + value;
            correction += Math.Abs(sum) >= Math.Abs(value) ? (sum - next) + value : (value - next) + sum;
            sum = next;
        }
    }

    public struct BabelDotPartial { public double Sum, Correction; }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    public struct BabelDotPartialJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<double> A, B;
        [WriteOnly] public NativeArray<BabelDotPartial> Partials;
        public int ChunkLength;

        public void Execute(int chunk)
        {
            int start = chunk * ChunkLength;
            // Subtract before adding, avoiding overflow at the final int-sized tail.
            int end = start + Math.Min(ChunkLength, A.Length - start);
            double sum = 0, correction = 0;
            for (int i = start; i < end; i++) BabelDotReduction.Add(ref sum, ref correction, A[i] * B[i]);
            Partials[chunk] = new BabelDotPartial { Sum = sum, Correction = correction };
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    public struct BabelDotMergeJob : IJob
    {
        [ReadOnly] public NativeArray<BabelDotPartial> Partials;
        [WriteOnly] public NativeArray<double> Sum;

        public void Execute()
        {
            double sum = 0, correction = 0;
            // Merge BOTH components in ascending chunk order, independent of
            // worker scheduling. Collapsing a partial first loses cancellation residuals.
            for (int i = 0; i < Partials.Length; i++)
            {
                var partial = Partials[i];
                BabelDotReduction.Add(ref sum, ref correction, partial.Sum);
                BabelDotReduction.Add(ref sum, ref correction, partial.Correction);
            }
            Sum[0] = sum + correction;
        }
    }
}
