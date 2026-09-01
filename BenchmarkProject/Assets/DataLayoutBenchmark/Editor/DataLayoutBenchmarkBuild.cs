using System;
using System.IO;
using Unity.Burst;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Yanagisawa.DataLayoutAutotuner.Benchmark.Editor
{
    /// <summary>
    /// Reproducible Windows player builds for the standalone benchmark harness.
    /// Invoke a public, parameterless method with Unity's -executeMethod argument.
    /// </summary>
    public static class DataLayoutBenchmarkBuild
    {
        private const string SceneAssetPath =
            "Assets/DataLayoutBenchmark/Generated/DataLayoutBenchmark.unity";

        private const string ExecutableName = "DataLayoutBenchmark.exe";
        private const string BackendArgument = "-dla-backend";

        [MenuItem("Tools/Data Layout Autotuner/Generate Benchmark Scene")]
        public static void GenerateBenchmarkScene()
        {
            string sceneDirectory = Path.GetDirectoryName(SceneAssetPath);
            if (string.IsNullOrEmpty(sceneDirectory))
            {
                throw new InvalidOperationException("Benchmark scene path has no parent directory.");
            }

            Directory.CreateDirectory(sceneDirectory);

            Scene benchmarkScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            var marker = new GameObject("Data Layout Benchmark (runtime bootstrap)");
            SceneManager.MoveGameObjectToScene(marker, benchmarkScene);

            if (!EditorSceneManager.SaveScene(benchmarkScene, SceneAssetPath, true))
            {
                throw new InvalidOperationException(
                    $"Unity could not save the benchmark scene at '{SceneAssetPath}'.");
            }

            AssetDatabase.ImportAsset(SceneAssetPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            Debug.Log($"Generated benchmark scene: {SceneAssetPath}");
        }

        [MenuItem("Tools/Data Layout Autotuner/Build Windows x64/Mono Smoke")]
        public static void BuildWindowsMonoSmoke()
        {
            BuildWindowsX64(ScriptingImplementation.Mono2x, "mono-smoke");
        }

        public static void BuildWindowsMonoAotEvidence()
        {
            BuildWindowsX64(ScriptingImplementation.Mono2x, "mono-aot-evidence");
        }

        [MenuItem("Tools/Data Layout Autotuner/Build Windows x64/IL2CPP Formal")]
        public static void BuildWindowsIl2CppFormal()
        {
            BuildWindowsX64(ScriptingImplementation.IL2CPP, "il2cpp-formal");
        }

        /// <summary>
        /// Command-line dispatcher. Pass -dla-backend mono or -dla-backend il2cpp.
        /// The default is il2cpp so an omitted argument cannot silently produce smoke evidence.
        /// </summary>
        public static void BuildWindowsFromCommandLine()
        {
            string backend = ReadCommandLineValue(BackendArgument) ?? "il2cpp";
            switch (backend.Trim().ToLowerInvariant())
            {
                case "mono":
                case "mono2x":
                case "smoke":
                    BuildWindowsMonoSmoke();
                    return;

                case "il2cpp":
                case "formal":
                    BuildWindowsIl2CppFormal();
                    return;

                default:
                    throw new ArgumentException(
                        $"Unsupported {BackendArgument} value '{backend}'. Expected 'mono' or 'il2cpp'.");
            }
        }

        private static void BuildWindowsX64(ScriptingImplementation backend, string outputLabel)
        {
            const BuildTarget target = BuildTarget.StandaloneWindows64;
            const BuildTargetGroup targetGroup = BuildTargetGroup.Standalone;
            NamedBuildTarget namedTarget = NamedBuildTarget.Standalone;

            if (!BuildPipeline.IsBuildTargetSupported(targetGroup, target))
            {
                throw new InvalidOperationException(
                    "Windows x64 build support is not installed for this Unity Editor.");
            }

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, target))
            {
                throw new InvalidOperationException("Unity failed to switch to Windows x64.");
            }

            ConfigureReleasePlayer(target, namedTarget, backend);
            ConfigureBurstForWindows();
            GenerateBenchmarkScene();

            string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string outputDirectory = Path.Combine(
                repositoryRoot,
                "Builds",
                "windows-x64",
                outputLabel);
            string executablePath = Path.Combine(outputDirectory, ExecutableName);
            Directory.CreateDirectory(outputDirectory);

            var buildOptions = new BuildPlayerOptions
            {
                scenes = new[] { SceneAssetPath },
                locationPathName = executablePath,
                target = target,
                targetGroup = targetGroup,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Windows x64 {outputLabel} build failed: result={summary.result}, " +
                    $"errors={summary.totalErrors}, warnings={summary.totalWarnings}, " +
                    $"output='{summary.outputPath}'.");
            }

            VerifyBurstAotArtifacts(outputDirectory);

            Debug.Log(
                $"Windows x64 {outputLabel} build succeeded: '{summary.outputPath}', " +
                $"size={summary.totalSize} bytes, duration={summary.totalTime}.");
        }

        private static void ConfigureReleasePlayer(
            BuildTarget target,
            NamedBuildTarget namedTarget,
            ScriptingImplementation backend)
        {
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.waitForManagedDebugger = false;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = false;

            PlayerSettings.SetScriptingBackend(namedTarget, backend);
            if (backend == ScriptingImplementation.IL2CPP)
            {
                PlayerSettings.SetIl2CppCompilerConfiguration(
                    namedTarget,
                    Il2CppCompilerConfiguration.Release);
            }

            PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
            PlayerSettings.SetGraphicsAPIs(target, new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.graphicsJobs = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.usePlayerLog = true;

            GraphicsDeviceType[] graphicsApis = PlayerSettings.GetGraphicsAPIs(target);
            if (PlayerSettings.GetUseDefaultGraphicsAPIs(target) ||
                graphicsApis.Length != 1 ||
                graphicsApis[0] != GraphicsDeviceType.Direct3D11)
            {
                throw new InvalidOperationException("Failed to enforce D3D11-only player graphics API.");
            }

            if (PlayerSettings.graphicsJobs)
            {
                throw new InvalidOperationException("Failed to disable Graphics Jobs.");
            }
        }

        private static void ConfigureBurstForWindows()
        {
            BurstCompiler.Options.EnableBurstCompilation = true;
            BurstCompiler.Options.EnableBurstSafetyChecks = false;
            BurstCompiler.Options.ForceEnableBurstSafetyChecks = false;
            BurstCompiler.Options.EnableBurstDebug = false;
            BurstCompiler.Options.EnableBurstCompileSynchronously = true;

            if (!BurstCompiler.Options.EnableBurstCompilation)
            {
                throw new InvalidOperationException("Failed to enable Burst compilation.");
            }
        }

        private static void VerifyBurstAotArtifacts(string outputDirectory)
        {
            string libraryPath = Path.Combine(
                outputDirectory,
                "DataLayoutBenchmark_Data",
                "Plugins",
                "x86_64",
                "lib_burst_generated.dll");
            if (!File.Exists(libraryPath) || new FileInfo(libraryPath).Length == 0)
            {
                throw new InvalidOperationException(
                    "The build succeeded without a non-empty Burst AOT library.");
            }

            string[] debugDirectories = Directory.GetDirectories(
                outputDirectory,
                "*BurstDebugInformation*",
                SearchOption.TopDirectoryOnly);
            if (debugDirectories.Length == 0)
                throw new InvalidOperationException("Burst AOT debug manifest was not emitted.");

            string[] manifests = Directory.GetFiles(
                debugDirectories[0],
                "lib_burst_generated.txt",
                SearchOption.AllDirectories);
            if (manifests.Length == 0)
                throw new InvalidOperationException("Burst AOT entrypoint manifest was not emitted.");

            string manifest = File.ReadAllText(manifests[0]);
            string[] requiredEntrypoints =
            {
                "ParticleAoSStepJob",
                "ParticleSoAStepJob",
                "ParticleAoSoA8StepJob",
            };
            for (int i = 0; i < requiredEntrypoints.Length; i++)
            {
                if (!manifest.Contains(requiredEntrypoints[i]))
                {
                    throw new InvalidOperationException(
                        $"Burst AOT manifest is missing {requiredEntrypoints[i]}.");
                }
            }
        }

        private static string ReadCommandLineValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
