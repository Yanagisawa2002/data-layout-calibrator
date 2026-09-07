param(
    [Parameter(Mandatory=$true)][string]$Player,
    [Parameter(Mandatory=$true)][string]$CandidateFile,
    [Parameter(Mandatory=$true)][string]$OutputRoot,
    [Parameter(Mandatory=$true)][string]$SourceCommit,
    [Parameter(Mandatory=$true)][string]$BuildReceipt,
    [Parameter(Mandatory=$true)][string]$ValidationLockScript,
    [int]$TimeoutSeconds = 900
)
$ErrorActionPreference = 'Stop'
$Player = (Resolve-Path -LiteralPath $Player).Path
$CandidateFile = (Resolve-Path -LiteralPath $CandidateFile).Path
$BuildReceipt = (Resolve-Path -LiteralPath $BuildReceipt).Path
$ValidationLockScript = (Resolve-Path -LiteralPath $ValidationLockScript).Path
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) { throw 'Choose a new output root; retained runs are immutable.' }
if ($SourceCommit -notmatch '^[0-9a-fA-F]{40}$') { throw 'Supply the full integrated source commit.' }
if ($TimeoutSeconds -lt 1) { throw 'Timeout must be positive.' }
$buildIdentity = Get-Content -Raw -LiteralPath $BuildReceipt | ConvertFrom-Json
if ($buildIdentity.sourceCommit -ne $SourceCommit) { throw 'Build receipt sourceCommit does not match integrated source.' }
if (@($buildIdentity.binaries).Count -eq 0) { throw 'Build receipt must bind the binary path/sha256 table.' }
$candidateInput = Get-Content -Raw -LiteralPath $CandidateFile | ConvertFrom-Json
if (@($candidateInput.Candidates).Count -lt 2) { throw 'Frozen candidate file is empty.' }
$batches = @($candidateInput.Candidates.LogicalBatchSize | Sort-Object -Unique)
if (($batches -join ',') -ne '64,256') { throw 'Preregistered search uses batches 64 and 256.' }
$layouts = @($candidateInput.Candidates.LayoutId | Sort-Object -Unique)
foreach ($requiredLayout in @('AoS', 'SoA', 'AoSoA4', 'AoSoA8', 'AoSoA16', 'AoSPadded64')) {
    if ($requiredLayout -notin $layouts) { throw "Expanded layout missing: $requiredLayout" }
}
New-Item -ItemType Directory -Path $OutputRoot | Out-Null
function Write-NewJson([string]$Path, $Value) {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 100))
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew)
    try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
}
$playerDirectory = Split-Path -Parent $Player
$binaryFiles = @(Get-ChildItem -LiteralPath $playerDirectory -Recurse -File |
    Where-Object { $_.Extension -in '.exe', '.dll', '.dat' } | Sort-Object FullName |
    ForEach-Object { @{ path=$_.FullName.Substring($playerDirectory.Length + 1); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
foreach ($binary in $binaryFiles) {
    $bound = @($buildIdentity.binaries | Where-Object { $_.path -eq $binary.path -and $_.sha256 -eq $binary.sha256 })
    if ($bound.Count -ne 1) { throw "Build receipt does not bind actual binary: $($binary.path)" }
}
function Get-InterferenceSnapshot {
    $rows = @(Get-Process | ForEach-Object {
        $processStart = $null; $cpuSeconds = $null
        try { $processStart = $_.StartTime.ToUniversalTime().ToString('O'); $cpuSeconds = $_.TotalProcessorTime.TotalSeconds } catch { }
        @{processId=$_.Id; name=$_.ProcessName; startedUtc=$processStart; cpuSeconds=$cpuSeconds}
    })
    @{createdUtc=[DateTime]::UtcNow.ToString('O'); processes=$rows
      activePowerPlan=(@(& powercfg.exe /getactivescheme) -join ' ')
      control='Read-only snapshot; temperature, frequency and external apps are uncontrolled. CPU deltas require matching process ID and start time.'}
}
$isa = @{}
foreach ($name in @('Sse2','Sse41','Avx','Avx2','Fma','Avx512F')) {
    $type = [Type]::GetType("System.Runtime.Intrinsics.X86.$name, System.Private.CoreLib")
    $isa[$name] = if ($null -eq $type) { $null } else { $type.GetProperty('IsSupported').GetValue($null) }
}
$plan = @{
    schemaVersion=1; artifactType='search-comparison-preregistration'; createdUtc=[DateTime]::UtcNow.ToString('O')
    sourceCommit=$SourceCommit; sourceIdentityStatus='integrator declaration linked to separately hashed build receipt'
    buildReceipt=$BuildReceipt; buildReceiptSha256=(Get-FileHash -LiteralPath $BuildReceipt -Algorithm SHA256).Hash
    player=$Player; binaries=$binaryFiles; candidates=$CandidateFile
    candidateFileSha256=(Get-FileHash -LiteralPath $CandidateFile -Algorithm SHA256).Hash
    candidateSetSha256=$candidateInput.CandidateSetSha256
    processor=@(Get-CimInstance Win32_Processor | Select-Object Name,Manufacturer,ProcessorId,NumberOfCores,NumberOfLogicalProcessors)
    isa=$isa; isaScope='actual host CLR ISA capability; not proof of Burst emitted instructions'
    policy=@{count=65536; holdoutCount=65539; lifetime=256; workers=8; batches=@(64,256)
        quickResident=6; quickBoundary=4; quickBootstrap=200; fullResident=40; fullBoundary=20; fullBootstrap=4000
        holdoutResident=40; holdoutBoundary=20; holdoutBootstrap=4000; confidence=.95; improvementPercent=10
        maximumAllowedRegretPercent=1; targetBlockMs=2; maxTicks=64; warmupBlocks=4; minWarmupSeconds=.05
        processCount=5; order=@('adaptive-first','exhaustive-first','adaptive-first','exhaustive-first','adaptive-first')}
    limitations=@('Fresh processes on one device, not five devices.', 'No global cache, clock, power plan or external application changes.',
        'Quick/planning/full wall times are actual observed costs; common pilot is charged to both methods.',
        'Holdout can confirm or reject frozen choices; results never retune elimination or method order.')
}
Write-NewJson (Join-Path $OutputRoot 'preregistration.json') $plan
& $ValidationLockScript -Action {
    $allPassed = $true
    for ($index=0; $index -lt 5; $index++) {
        # Prevent binary/candidate changes between the predeclared launches.
        if ((Get-FileHash -LiteralPath $CandidateFile -Algorithm SHA256).Hash -ne $plan.candidateFileSha256) { throw 'Candidate file changed.' }
        foreach ($binary in $binaryFiles) {
            if ((Get-FileHash -LiteralPath (Join-Path $playerDirectory $binary.path) -Algorithm SHA256).Hash -ne $binary.sha256) { throw 'Player binary changed.' }
        }
        $run = 'run-{0:d2}' -f ($index+1)
        $directory = Join-Path $OutputRoot $run
        $arguments = @('-batchmode','-nographics','-job-worker-count','8','-dla-search-run',
            '-dla-search-candidates',$CandidateFile,'-dla-search-order',$plan.policy.order[$index],
            '-dla-count','65536','-dla-holdout-count','65539','-dla-lifetime-ticks','256',
            '-dla-samples','40','-dla-boundary-samples','20','-dla-bootstrap-iterations','4000',
            '-dla-target-block-ms','2','-dla-max-ticks','64','-dla-warmup-blocks','4',
            '-dla-min-warmup-seconds','0.05','-dla-output',$directory,'-logFile',(Join-Path $OutputRoot "$run-player.log"))
        $startInfo = [Diagnostics.ProcessStartInfo]::new($Player)
        $startInfo.UseShellExecute = $false; $startInfo.CreateNoWindow = $true
        foreach ($argument in $arguments) { $startInfo.ArgumentList.Add($argument) }
        Write-NewJson (Join-Path $OutputRoot "$run-preflight.json") (Get-InterferenceSnapshot)
        $started = [DateTime]::UtcNow
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $process = [Diagnostics.Process]::Start($startInfo)
        $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
        $timer.Stop()
        Write-NewJson (Join-Path $OutputRoot "$run-postflight.json") (Get-InterferenceSnapshot)
        $artifacts = @(Get-ChildItem -LiteralPath $directory -Recurse -File -ErrorAction SilentlyContinue |
            Sort-Object FullName | ForEach-Object { @{ path=$_.FullName; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
        $receipt = @{run=$run; processId=$process.Id; startedUtc=$started.ToString('O'); exitCode=$process.ExitCode
            timedOut=$timedOut; elapsedSeconds=$timer.Elapsed.TotalSeconds; order=$plan.policy.order[$index]
            arguments=$arguments; artifacts=$artifacts; status='observed-unvalidated'
            preflightSha256=(Get-FileHash -LiteralPath (Join-Path $OutputRoot "$run-preflight.json") -Algorithm SHA256).Hash
            postflightSha256=(Get-FileHash -LiteralPath (Join-Path $OutputRoot "$run-postflight.json") -Algorithm SHA256).Hash}
        if ($timedOut -or $process.ExitCode -ne 0) { $receipt.status='failed-retained'; $allPassed=$false }
        Write-NewJson (Join-Path $OutputRoot "$run-receipt.json") $receipt
        $process.Dispose()
    }
    if (-not $allPassed) { throw 'One or more search launches failed; all receipts retained. Replay gate remains unmet.' }
}
