using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark
{
    [Serializable]
    internal sealed class BenchmarkConfiguration
    {
        public int ElementCount = 1_048_576;
        public int HoldoutElementCount = 1_000_003;
        public int WarmupBlocks = 32;
        public double MinimumWarmupSeconds = 1.0d;
        public int SamplesPerCandidate = 40;
        public int BoundarySamplesPerCandidate = 20;
        public int LifetimeTicks = 600;
        public double TargetBlockMilliseconds = 25.0d;
        public int MaximumTicksPerBlock = 256;
        public double MinimumImprovementPercent = 10.0d;
        public int BootstrapIterations = 4000;
        public double BootstrapConfidenceLevel = 0.95d;
        public string OutputDirectory;
        public bool QuitWhenComplete;
        public bool ShowGui = true;

        public static bool ShouldRun()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], "-dla-run", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static BenchmarkConfiguration FromCommandLine()
        {
            var configuration = new BenchmarkConfiguration();
            configuration.ElementCount = ReadInt("-dla-count", configuration.ElementCount, 1);
            configuration.HoldoutElementCount = ReadInt(
                "-dla-holdout-count",
                configuration.HoldoutElementCount,
                1);
            configuration.WarmupBlocks = ReadInt(
                "-dla-warmup-blocks",
                configuration.WarmupBlocks,
                1);
            configuration.SamplesPerCandidate = ReadInt(
                "-dla-samples",
                configuration.SamplesPerCandidate,
                3);
            configuration.BoundarySamplesPerCandidate = ReadInt(
                "-dla-boundary-samples",
                configuration.BoundarySamplesPerCandidate,
                3);
            configuration.LifetimeTicks = ReadInt(
                "-dla-lifetime-ticks",
                configuration.LifetimeTicks,
                1);
            configuration.MaximumTicksPerBlock = ReadInt(
                "-dla-max-ticks",
                configuration.MaximumTicksPerBlock,
                1);
            configuration.MinimumWarmupSeconds = ReadDouble(
                "-dla-min-warmup-seconds",
                configuration.MinimumWarmupSeconds,
                0d);
            configuration.TargetBlockMilliseconds = ReadDouble(
                "-dla-target-block-ms",
                configuration.TargetBlockMilliseconds,
                0.1d);
            configuration.MinimumImprovementPercent = ReadDouble(
                "-dla-min-improvement-percent",
                configuration.MinimumImprovementPercent,
                0d);
            configuration.BootstrapIterations = ReadInt(
                "-dla-bootstrap-iterations",
                configuration.BootstrapIterations,
                100);
            configuration.BootstrapConfidenceLevel = ReadDouble(
                "-dla-bootstrap-confidence",
                configuration.BootstrapConfidenceLevel,
                0.5d);
            configuration.BootstrapConfidenceLevel = Math.Min(
                0.999d,
                configuration.BootstrapConfidenceLevel);
            configuration.OutputDirectory = ReadString("-dla-output");
            configuration.QuitWhenComplete = HasFlag("-dla-quit") || Application.isBatchMode;
            configuration.ShowGui = !HasFlag("-dla-no-gui") && !Application.isBatchMode;

            if (string.IsNullOrWhiteSpace(configuration.OutputDirectory))
            {
                string run = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                configuration.OutputDirectory = Path.Combine(
                    Application.persistentDataPath,
                    "DataLayoutCalibrator",
                    run);
            }

            configuration.OutputDirectory = Path.GetFullPath(configuration.OutputDirectory);
            return configuration;
        }

        private static bool HasFlag(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static int ReadInt(string name, int fallback, int minimum)
        {
            string value = ReadString(name);
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? Math.Max(minimum, parsed)
                : fallback;
        }

        private static double ReadDouble(string name, double fallback, double minimum)
        {
            string value = ReadString(name);
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? Math.Max(minimum, parsed)
                : fallback;
        }

        private static string ReadString(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string prefix = name + "=";
            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return argument.Substring(prefix.Length).Trim('"');
                if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase) && i + 1 < arguments.Length)
                    return arguments[i + 1].Trim('"');
            }

            return null;
        }
    }
}
