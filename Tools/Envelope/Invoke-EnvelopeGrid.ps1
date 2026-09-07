param(
    [Parameter(Mandatory=$true)][string]$UnityPath,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$Repository = (Resolve-Path "$PSScriptRoot/../..").Path,
    [string]$ValidationLockScript,
    [switch]$LockAlreadyHeld
)
$ErrorActionPreference = 'Stop'

function Invoke-EnvelopeChild([string]$Executable, [string[]]$Arguments) {
    $quoted = @($Arguments | ForEach-Object { if ($_.Contains('"')) { throw 'Embedded quote in argument.' }; '"' + $_ + '"' })
    $child = Start-Process -FilePath $Executable -ArgumentList $quoted -WindowStyle Hidden -PassThru
    $child.WaitForExit()
    if ($child.ExitCode -ne 0) { throw "Child failed ($($child.ExitCode)): $Executable" }
}

$runGrid = {
    $repoPath = (Resolve-Path -LiteralPath $Repository).Path
    $outPath = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $outPath) { throw 'Output must be a new directory; evidence is immutable.' }
    $dirty = @(& git -C $repoPath status --porcelain)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'Commit the integrated source before formal measurement.' }
    New-Item -ItemType Directory -Path $outPath | Out-Null
    $sourceCommit = (& git -C $repoPath rev-parse HEAD).Trim()
    $project = Join-Path $repoPath 'BenchmarkProject'
    $declaration = Join-Path $outPath 'grid-declaration.json'
    $declareLog = Join-Path $outPath 'declare.log'
    $buildLog = Join-Path $outPath 'build.log'
    $buildDirectory = Join-Path $repoPath 'Builds/windows-x64/il2cpp-formal'
    $player = Join-Path $buildDirectory 'DataLayoutCalibrator.exe'
    $manifestPath = Join-Path $outPath 'build-identity.json'
    $orchestration = [ordered]@{ protocol='dlc.measured-envelope-grid.v1'; sourceCommit=$sourceCommit;
        startedUtc=[DateTime]::UtcNow.ToString('O'); completedRuns=0; failure=$null; runs=@() }
    try {
        Invoke-EnvelopeChild $UnityPath @('-batchmode','-nographics','-quit','-projectPath',$project,
            '-executeMethod','Yanagisawa.DataLayoutCalibrator.Benchmark.Editor.EnvelopeGridBuild.Declare',
            '-dla-grid-output',$declaration,'-logFile',$declareLog)
        if (-not (Test-Path -LiteralPath $declaration)) { throw 'Declaration generation produced no artifact.' }
        Invoke-EnvelopeChild $UnityPath @('-batchmode','-nographics','-quit','-projectPath',$project,
            '-executeMethod','Yanagisawa.DataLayoutCalibrator.Benchmark.Editor.DataLayoutCalibratorBuild.BuildWindowsIl2CppFormal',
            '-logFile',$buildLog)
        $binaries = @(Get-ChildItem -LiteralPath $buildDirectory -File -Recurse |
            Where-Object { $_.Extension -in '.dll','.exe','.dat' -or $_.Name -eq 'global-metadata.dat' } |
            Sort-Object FullName | ForEach-Object {
                [ordered]@{ path=$_.FullName.Substring($buildDirectory.Length).Replace('\','/');
                    bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
            })
        if (-not ($binaries | Where-Object { $_.path -match 'lib_burst_generated.dll$' })) {
            throw 'Actual Burst AOT binary is required.'
        }
        $identity = [ordered]@{
            sourceCommit=$sourceCommit; unityExecutable=$UnityPath;
            unitySha256=(Get-FileHash -LiteralPath $UnityPath).Hash;
            buildLogSha256=(Get-FileHash -LiteralPath $buildLog).Hash;
            packagesLockSha256=(Get-FileHash -LiteralPath (Join-Path $project 'Packages/packages-lock.json')).Hash;
            declarationSha256=(Get-FileHash -LiteralPath $declaration).Hash;
            cpu=@(Get-CimInstance Win32_Processor | Select-Object Name,ProcessorId,Architecture,NumberOfCores,NumberOfLogicalProcessors);
            isaNote='x64 build; actual runtime-dispatched Burst ISA is not independently attested; preserve Burst binary and compiler log';
            compilerEvidence='IL2CPP Release and Burst AOT configured by source build method; exact invocation and generated compiler trace in build.log';
            processInterference=@(Get-Process | Select-Object Id,ProcessName,CPU);
            binaries=$binaries
        }
        $identity | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
        $buildIdentity = (Get-FileHash -LiteralPath $manifestPath).Hash
        for ($processIndex=1; $processIndex -le 5; $processIndex++) {
            if ((& git -C $repoPath rev-parse HEAD).Trim() -ne $sourceCommit) { throw 'Source changed during formal grid.' }
            $runOutput = Join-Path $outPath ('run-{0:D2}' -f $processIndex)
            $runLog = Join-Path $outPath ('run-{0:D2}.log' -f $processIndex)
            $timer = [Diagnostics.Stopwatch]::StartNew()
            $run = [ordered]@{ processIndex=$processIndex; output=$runOutput; completed=$false; elapsedSeconds=0; failure=$null }
            try {
                Invoke-EnvelopeChild $player @('-batchmode','-nographics','-dla-envelope-run',$declaration,
                    '-dla-process-index',"$processIndex",'-dla-build-identity',$buildIdentity,
                    '-dla-output',$runOutput,'-logFile',$runLog)
                $receipt = Get-Content -Raw -LiteralPath (Join-Path $runOutput 'receipt.json') | ConvertFrom-Json
                if ($receipt.CompletedCells -ne 24 -or $receipt.Failure) { throw 'Incomplete formal process grid.' }
                $run.completed=$true
                $orchestration.completedRuns++
            }
            catch { $run.failure=$_.ToString() }
            finally { $run.elapsedSeconds=$timer.Elapsed.TotalSeconds; $orchestration.runs += $run }
        }
        if ($orchestration.completedRuns -ne 5) { throw 'One or more formal process runs failed; retained evidence is incomplete.' }
    }
    catch { $orchestration.failure=$_.ToString(); throw }
    finally {
        $orchestration.finishedUtc=[DateTime]::UtcNow.ToString('O')
        $orchestration | ConvertTo-Json -Depth 20 |
            Set-Content -LiteralPath (Join-Path $outPath 'orchestration.json') -Encoding utf8NoBOM
    }
}

if ($LockAlreadyHeld) { & $runGrid }
else {
    if (-not $ValidationLockScript) { throw 'Supply the shared validation lock script, or -LockAlreadyHeld under the coordinator lock.' }
    & $ValidationLockScript -Action $runGrid
}
