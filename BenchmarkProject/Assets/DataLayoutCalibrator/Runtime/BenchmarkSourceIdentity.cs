using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark
{
    /// <summary>Build/data provenance for explicit Player runs. No clock, allocation
    /// counter or job dispatch is used to establish identity.</summary>
    internal static class BenchmarkSourceIdentity
    {
        internal static void Bind(CalibrationRunSettings settings, ScenarioDescriptor scenario)
        {
            string executable = Path.GetFullPath(Environment.GetCommandLineArgs()[0]);
            string root = Path.GetDirectoryName(executable);
            string manifest = Path.Combine(root, "source-identity.json");
            if (!File.Exists(manifest))
                throw new InvalidOperationException("Missing source-identity.json from the Player build; rebuild with DataLayoutCalibratorBuild to retain source provenance.");

            var paths = new List<string> { executable, manifest };
            paths.AddRange(Directory.GetFiles(root, "*.dll", SearchOption.TopDirectoryOnly));
            foreach (string path in Directory.GetFiles(Application.dataPath, "*.dll", SearchOption.AllDirectories))
                paths.Add(path);
            foreach (string path in Directory.GetFiles(Application.dataPath, "*.dat", SearchOption.AllDirectories))
                paths.Add(path);
            paths.Sort(StringComparer.Ordinal);
            var identitySettings = JsonUtility.FromJson<CalibrationRunSettings>(JsonUtility.ToJson(settings));
            identitySettings.SourceFingerprint = null; // Rebinding must not hash the preceding fingerprint.
            var canonical = new StringBuilder("dlc-player-source-v1\n");
            foreach (string path in paths)
                canonical.Append(path.Substring(root.Length).Replace('\\', '/')).Append('=')
                    .Append(WindowsProcessCycleCounterProvider.HashFile(path)).Append('\n');
            canonical.Append(Application.unityVersion).Append('\n').Append(SystemInfo.operatingSystem).Append('\n')
                .Append(SystemInfo.processorType).Append('\n').Append(SystemInfo.processorCount).Append('\n')
                .Append(JobsUtility.JobWorkerCount).Append('\n').Append(scenario.ScenarioId).Append('\n')
                .Append(scenario.ContractVersion).Append('\n').Append(JsonUtility.ToJson(identitySettings));
            settings.SourceFingerprint = CandidateDefinitionProtocol.ComputeSha256Utf8(canonical.ToString());
        }
    }
}
