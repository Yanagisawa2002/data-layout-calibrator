using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark.Editor
{
    public static class EnvelopeGridBuild
    {
        // Reflection is confined to Editor declaration generation. Player creation/scheduling is concrete AOT code.
        public static void Declare()
        {
            Type matrix = typeof(ParticleIntegrateScenarioFactory).Assembly.GetType(
                "Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate.ParticleCandidateMatrix", true);
            MethodInfo method = matrix.GetMethod("CreateCandidates", new[] { typeof(int[]) });
            if (method == null) throw new InvalidOperationException("Integrated expanded candidate matrix is required.");
            var candidates = (CandidateDescriptor[])method.Invoke(null, new object[] { new[] { 64, 256 } });
            var grid = new EnvelopeGridDeclaration { Candidates = candidates.Where(
                candidate => candidate.Execution.PolicyId == "FrameFaithful").ToArray() };
            grid.Validate();
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, "-dla-grid-output");
            if (index < 0 || index + 1 >= arguments.Length) throw new ArgumentException("-dla-grid-output is required.");
            string path = Path.GetFullPath(arguments[index + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
                writer.Write(JsonUtility.ToJson(grid, true));
        }
    }
}
