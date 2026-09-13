using System;
using System.IO;
using Unity.Burst;
using UnityEngine;
using Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark
{
    internal static class ParticleMatrixPlayerProbe
    {
        [Serializable]
        private sealed class Receipt
        {
            public string Kind = "release-candidate-correctness-not-performance";
            public string UnityVersion;
            public string Processor;
            public bool DevelopmentBuild;
            public bool BurstEnabled;
            public int Checks;
            public string CandidateSetSha256;
            public CandidateDescriptor[] Candidates;
            public ParticleMatrixCell[] Cells;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-dla-matrix-probe") < 0) return;
            try
            {
                if (Debug.isDebugBuild || !BurstCompiler.IsEnabled)
                    throw new InvalidOperationException("Matrix probe requires Release and enabled Burst.");
                int batchArgument = Array.IndexOf(args, "-dla-matrix-probe-batches");
                int[] batches = null;
                if (batchArgument >= 0)
                {
                    if (batchArgument + 1 >= args.Length) throw new ArgumentException("Missing matrix batches.");
                    batches = Array.ConvertAll(args[batchArgument + 1].Split(','), int.Parse);
                }
                var receipt = new Receipt
                {
                    UnityVersion = Application.unityVersion, Processor = SystemInfo.processorType,
                    DevelopmentBuild = Debug.isDebugBuild, BurstEnabled = BurstCompiler.IsEnabled,
                    Checks = ParticleMatrixValidation.RunOrThrow(),
                    CandidateSetSha256 = CandidateDefinitionProtocol.ComputeCandidateSetSha256(ParticleCandidateMatrix.CreateCandidates(batches)),
                    Candidates = ParticleCandidateMatrix.CreateCandidates(batches),
                    Cells = ParticleCandidateMatrix.CreateCells(batches),
                };
                int output = Array.IndexOf(args, "-dla-matrix-receipt");
                if (output < 0 || output + 1 >= args.Length) throw new ArgumentException("Missing -dla-matrix-receipt path.");
                File.WriteAllText(args[output + 1], JsonUtility.ToJson(receipt, true));
                Debug.Log("Particle crossed matrix probe passed: " + receipt.Checks + " checks. Correctness only; no performance claim.");
                Application.Quit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Application.Quit(1);
            }
        }
    }
}
