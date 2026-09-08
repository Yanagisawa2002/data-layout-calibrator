param(
    [Parameter(Mandatory = $true)][string]$PlayerPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$SerializationScript,
    [int]$Count = 4099,
    [int]$Ticks = 32,
    [int]$Pairs = 8,
    [string]$Scenarios = 'spatial-neighborhood-v1,animation-state-v1',
    [string]$CandidateFile,
    [switch]$NoProvider
)
throw 'Legacy performance/Player orchestration is disabled. Use Tools/CI/validate_functional.py; re-enabling measurement requires NEW explicit user authorization and a reviewed launcher.'
$ErrorActionPreference = 'Stop'
$counterPlayer = (Resolve-Path -LiteralPath $PlayerPath).Path
$counterOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $counterOutput) { throw 'Use a fresh output directory for each retained attempt.' }
New-Item -ItemType Directory -Path $counterOutput | Out-Null
function Write-CounterInterference([string]$Path) {
    [ordered]@{utc=[DateTime]::UtcNow.ToString('O');powerPlan=(@(& powercfg.exe /getactivescheme) -join ' ');
        processes=@(Get-Process | ForEach-Object { $start=$null;try{$start=$_.StartTime.ToUniversalTime().ToString('O')}catch{};
            [ordered]@{id=$_.Id;name=$_.ProcessName;cpuSeconds=$_.CPU;startUtc=$start}});
        limits='Read-only snapshots; transient interference, clocks, affinity, caches and thermal state uncontrolled.'
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}
& $SerializationScript -Action {
    # Read-only capability query. It does not start/stop an ETW session or enable privileges.
    $wpr = Get-Command wpr.exe -ErrorAction SilentlyContinue
    $pmu = [ordered]@{ Status = 'Unavailable'; Code = 'wpr-not-installed'; ExitCode = $null; Scope = 'PMU source inventory only; not captured PMU values and not proof of event attribution.' }
    if ($wpr) {
        $wprOutput = & $wpr.Source -pmcsources 2>&1 | Out-String
        $pmu.ExitCode = $LASTEXITCODE
        $pmu.Status = if ($LASTEXITCODE -eq 0) { 'InventoryOnly' } else { 'Unavailable' }
        $pmu.Code = if ($LASTEXITCODE -eq 0) { 'source-list-only-no-pmu-provider' } else { 'wpr-pmcsources-failed' }
        [IO.File]::WriteAllText((Join-Path $counterOutput 'wpr-pmcsources.txt'), $wprOutput)
    }
    $pmu | ConvertTo-Json | Set-Content (Join-Path $counterOutput 'pmu-availability.json')
    $counterArgs = @('-batchmode', '-nographics', '-job-worker-count', '7', '-dla-counter-run', '-dla-quit',
        '-dla-counter-count', $Count, '-dla-counter-ticks', $Ticks, '-dla-counter-pairs', $Pairs,
        '-dla-counter-scenarios', $Scenarios, '-dla-output', ('"' + $counterOutput + '"'),
        '-logFile', ('"' + (Join-Path $counterOutput 'player.log') + '"'))
    if ($NoProvider) { $counterArgs += '-dla-counter-no-provider' }
    if ($CandidateFile) { $counterArgs += @('-dla-counter-candidates', ('"' + (Resolve-Path -LiteralPath $CandidateFile).Path + '"')) }
    [ordered]@{ Player = $counterPlayer; Arguments = $counterArgs; Count = $Count; Ticks = $Ticks; Pairs = $Pairs;
        Scenarios = $Scenarios; CreatedUtc = [DateTime]::UtcNow.ToString('O');
        Interference = 'Shared validation mutex held; external user applications, CPU clocks, affinity and thermal state uncontrolled.'
    } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $counterOutput 'invocation.json')
    Write-CounterInterference (Join-Path $counterOutput 'interference-before.json')
    $counterProcess = Start-Process -FilePath $counterPlayer -ArgumentList $counterArgs -WindowStyle Hidden -PassThru
    $counterProcess.WaitForExit()
    $counterExit = $counterProcess.ExitCode
    Write-CounterInterference (Join-Path $counterOutput 'interference-after.json')
    [ordered]@{processId=$counterProcess.Id;exitCode=$counterExit;completedUtc=[DateTime]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content (Join-Path $counterOutput 'process-receipt.json')
    $counterProcess.Dispose()
    [IO.File]::WriteAllText((Join-Path $counterOutput 'exit-code.txt'), [string]$counterExit)
    $evidencePath = Join-Path $counterOutput 'cpu-counter-evidence.json'
    if (!(Test-Path -LiteralPath $evidencePath)) { throw "Player exited $counterExit without counter evidence. See player.log." }
    $counterEvidence = Get-Content -Raw -LiteralPath $evidencePath | ConvertFrom-Json
    $counterEvidence | Select-Object ActualCounterGate, CollectedCaptures, UnavailableCaptures, FailedCaptures, Failure
    $artifactHashes = Get-ChildItem -LiteralPath $counterOutput -File -Recurse | ForEach-Object {
        [ordered]@{ Path = $_.FullName.Substring($counterOutput.Length + 1).Replace('\', '/'); Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
    $artifactHashes | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $counterOutput 'artifact-hashes.json')
    if ($counterExit -ne 0 -or $counterEvidence.Failure) { throw "Counter Player failed ($counterExit); all artifacts retained." }
    if (!$NoProvider -and $counterEvidence.ActualCounterGate -ne 'passed-process-cycles-only') {
        throw "Actual-counter gate unmet: $($counterEvidence.ActualCounterGate). Exact status retained."
    }
    if ($NoProvider -and $counterEvidence.ActualCounterGate -ne 'unavailable') { throw 'No-provider fallback did not report unavailable.' }
}
