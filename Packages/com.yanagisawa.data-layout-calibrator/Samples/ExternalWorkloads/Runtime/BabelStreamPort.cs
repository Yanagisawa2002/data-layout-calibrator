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
            return !double.IsNaN(actual) && Math.Abs(actual - expected) <= Math.Max(Math.Abs(actual), Math.Abs(expected)) * limit;
        }

        // Exactly the five classic operations; nstream is an upstream optional selection.
        public static void AdvanceClassicGold(ref double a, ref double b, ref double c, int count, out double dot)
        {
            c = a; b = Scalar * c; c = a + b; a = b + Scalar * c; dot = a * b * count;
        }

        public static void RefusePerformanceRun() => throw new InvalidOperationException(
            "Performance execution is disabled. New explicit user authorization and a reviewed runner change are required.");
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
}
