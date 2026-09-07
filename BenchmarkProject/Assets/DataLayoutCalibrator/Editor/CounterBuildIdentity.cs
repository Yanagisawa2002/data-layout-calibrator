using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark.Editor
{
    internal static class CounterBuildIdentity
    {
        [Serializable] private sealed class Manifest
        {
            public int SchemaVersion = 1;
            public string SourceCommit, GitStatus, UnityVersion, CreatedUtc;
            public Entry[] Inputs;
        }
        [Serializable] private sealed class Entry { public string Path, Sha256; }
        public static void Write(string root, string output)
        {
            var inputs = new List<Entry>();
            foreach (string folder in new[] { "Packages", "BenchmarkProject/Assets", "BenchmarkProject/Packages", "BenchmarkProject/ProjectSettings" })
            foreach (string path in Directory.GetFiles(Path.Combine(root, folder), "*", SearchOption.AllDirectories))
            {
                string relative = path.Substring(root.Length + 1).Replace('\\', '/');
                if (relative.Contains("/bin/") || relative.Contains("/obj/")) continue;
                string ext = Path.GetExtension(path);
                if (ext != ".cs" && ext != ".dll" && ext != ".asmdef" && ext != ".json" && ext != ".asset" && ext != ".rsp") continue;
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(path))
                    inputs.Add(new Entry { Path = relative, Sha256 = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") });
            }
            inputs.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            File.WriteAllText(Path.Combine(output, "source-identity.json"), JsonUtility.ToJson(new Manifest {
                SourceCommit = Git(root, "rev-parse HEAD"), GitStatus = Git(root, "status --porcelain"),
                UnityVersion = Application.unityVersion, CreatedUtc = DateTime.UtcNow.ToString("O"), Inputs = inputs.ToArray() }, true));
        }
        private static string Git(string root, string arguments)
        {
            using (var process = Process.Start(new ProcessStartInfo("git", arguments) {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
            {
                string value = process.StandardOutput.ReadToEnd(); process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException("Build source identity git command failed.");
                return value.Trim();
            }
        }
    }
}
