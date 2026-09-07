param(
    [Parameter(Mandatory = $true)][string]$UnityEditor,
    [Parameter(Mandatory = $true)][string]$SerializationScript,
    [ValidateSet('Mono', 'IL2CPP')][string[]]$Backends = @('Mono', 'IL2CPP'),
    [string]$OutputDirectory,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repository 'Artifacts/generated-workloads' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$sourceCommit = (& git -C $repository rev-parse HEAD).Trim()
$sourceStatus = @(& git -C $repository status --porcelain)
$sourceHashes = @(& git -C $repository ls-files '*.cs' '*.dll' '*.asmdef' 'BenchmarkProject/Packages/*.json' 'BenchmarkProject/ProjectSettings/*' |
    ForEach-Object { [ordered]@{ Path = $_; SHA256 = (Get-FileHash -LiteralPath (Join-Path $repository $_) -Algorithm SHA256).Hash } })
$compilerRoot = Join-Path (Split-Path -Parent $UnityEditor) 'Data/DotNetSdk/sdk'
$compilerHashes = @(Get-ChildItem -LiteralPath $compilerRoot -Recurse -Filter csc.dll -File |
    ForEach-Object { [ordered]@{ Path = $_.FullName; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })

foreach ($backend in $Backends) {
    $runDirectory = Join-Path $OutputDirectory $backend.ToLowerInvariant()
    New-Item -ItemType Directory -Force $runDirectory | Out-Null
    & $SerializationScript -Action {
        $label = if ($backend -eq 'Mono') { 'mono-aot-evidence' } else { 'il2cpp-formal' }
        $method = if ($backend -eq 'Mono') { 'BuildWindowsMonoAotEvidence' } else { 'BuildWindowsIl2CppFormal' }
        if (!$SkipBuild) {
            $project = Join-Path $repository 'BenchmarkProject'
            $buildLog = Join-Path $runDirectory 'build.log'
            $editorProcess = Start-Process -FilePath $UnityEditor -WindowStyle Hidden -PassThru -Wait -ArgumentList @(
                '-batchmode', '-nographics', '-quit', '-projectPath', "`"$project`"",
                '-executeMethod', "Yanagisawa.DataLayoutCalibrator.Benchmark.Editor.DataLayoutCalibratorBuild.$method",
                '-logFile', "`"$buildLog`"")
            if ($editorProcess.ExitCode -ne 0) { throw "$backend Unity build failed: $($editorProcess.ExitCode)" }
        }
        $buildDirectory = Join-Path $repository "Builds/windows-x64/$label"
        $player = Join-Path $buildDirectory 'DataLayoutCalibrator.exe'
        $burst = Join-Path $buildDirectory 'DataLayoutCalibrator_Data/Plugins/x86_64/lib_burst_generated.dll'
        if (!(Test-Path -LiteralPath $player) -or !(Test-Path -LiteralPath $burst)) { throw 'Missing Player or Burst AOT binary.' }
        $manifests = @(Get-ChildItem -LiteralPath $buildDirectory -Recurse -Filter lib_burst_generated.txt)
        if (!$manifests.Count) { throw 'Missing Burst AOT manifest.' }
        $manifest = Get-Content -Raw -LiteralPath $manifests[0].FullName
        # Generated codecs inline into these explicit, AOT-visible boundary jobs.
        $required = @('ParticleAoSStepJob', 'ParticleAoSBranchlessStepJob', 'ParticleSoAStepJob',
            'ParticleAoSoA8StepJob', 'ParticleSoAIngressJob', 'ParticleSoAExportJob',
            'ParticleAoSoA8IngressJob', 'ParticleAoSoA8ExportJob', 'TransformSoAIngressJob',
            'TransformAoSExportJob', 'TransformSoAExportJob')
        foreach ($entry in $required) { if (!$manifest.Contains($entry)) { throw "Missing Burst AOT entrypoint: $entry" } }
        Copy-Item -LiteralPath $manifests[0].FullName -Destination (Join-Path $runDirectory 'burst-entrypoints.txt')
        $playerLog = Join-Path $runDirectory 'player.log'
        $started = [DateTime]::UtcNow.ToString('O')
        $process = Start-Process -FilePath $player -WindowStyle Hidden -PassThru -Wait -ArgumentList @(
            '-batchmode', '-nographics', '-dla-generated-workloads', '-dla-output', "`"$runDirectory`"",
            '-logFile', "`"$playerLog`"", '-job-worker-count', '8')
        $hashes = @(Get-ChildItem -LiteralPath $buildDirectory -Recurse -File |
            Where-Object { $_.Extension -in @('.dll', '.exe') } |
            ForEach-Object { [ordered]@{ Path = $_.FullName.Substring($buildDirectory.Length + 1); SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
        [ordered]@{
            SourceCommit = $sourceCommit; SourceStatusBeforeBuild = $sourceStatus; Backend = $backend;
            SourceFilesBeforeBuild = $sourceHashes; CSharpCompilerHashes = $compilerHashes;
            UnityEditor = $UnityEditor; UnityEditorSHA256 = (Get-FileHash -LiteralPath $UnityEditor -Algorithm SHA256).Hash;
            PlayerProcessId = $process.Id; StartedUtc = $started; CompletedUtc = [DateTime]::UtcNow.ToString('O');
            ExitCode = $process.ExitCode; BinaryHashes = $hashes;
            BurstManifestSHA256 = (Get-FileHash -LiteralPath $manifests[0].FullName -Algorithm SHA256).Hash;
            ResolvedPackageLock = (Get-Content -Raw -LiteralPath (Join-Path $repository 'BenchmarkProject/Packages/packages-lock.json') | ConvertFrom-Json);
            ConfiguredBurstTargets = (Get-Content -Raw -LiteralPath (Join-Path $repository 'BenchmarkProject/ProjectSettings/BurstAotSettings_StandaloneWindows.json') | ConvertFrom-Json);
            ActualBurstDispatchedIsa = $null;
            IsaEvidenceLimit = 'Configured targets and emitted entrypoint manifest retained; dynamic dispatch ISA not observed by this allocation validator';
            AllocationScope = 'Main thread after warmup; worker jobs independently required in Burst AOT manifest';
            MeasurementScope = 'Correctness and allocation validation only; no performance inference'
        } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $runDirectory 'provenance.json') -Encoding utf8
        if ($process.ExitCode -ne 0) { throw "$backend workload Player failed: $($process.ExitCode)" }
        $receipt = Get-Content -Raw -LiteralPath (Join-Path $runDirectory 'generated-workload-validation.json') | ConvertFrom-Json
        if (!$receipt.Passed -or !$receipt.Release -or !$receipt.BurstEnabled -or $receipt.Backend -ne $backend) { throw 'Invalid workload validation receipt.' }
        Write-Output "${backend}: passed $($receipt.Candidates.Count) actual workload candidate/count/seed cases."
    }
}
