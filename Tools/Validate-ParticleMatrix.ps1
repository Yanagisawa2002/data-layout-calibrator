param(
    [Parameter(Mandatory = $true)][string]$UnityEditor,
    [Parameter(Mandatory = $true)][string]$SerializationRunner,
    [string]$OutputDirectory,
    [ValidateSet('mono', 'il2cpp')][string]$Backend = 'mono',
    [string]$Batches = '64,256'
)
throw 'Legacy performance/Player orchestration is disabled. Use Tools/CI/validate_functional.py; re-enabling measurement requires NEW explicit user authorization and a reviewed launcher.'
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $taskRoot 'work/matrix-validation' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
function Invoke-MatrixProcess([string]$FilePath, [string[]]$Arguments) {
    $taskProcess = Start-Process -FilePath $FilePath -WindowStyle Hidden -PassThru -Wait -ArgumentList $Arguments
    if ($taskProcess.ExitCode -ne 0) { throw "$FilePath exited $($taskProcess.ExitCode); inspect $OutputDirectory." }
}
& $SerializationRunner -Action {
    $taskMethod = if ($Backend -eq 'mono') { 'BuildWindowsMonoAotEvidence' } else { 'BuildWindowsIl2CppFormal' }
    $taskLabel = if ($Backend -eq 'mono') { 'mono-aot-evidence' } else { 'il2cpp-formal' }
    Invoke-MatrixProcess $UnityEditor @('-batchmode', '-quit', '-nographics', '-projectPath', "`"$taskRoot/BenchmarkProject`"", '-executeMethod', "Yanagisawa.DataLayoutCalibrator.Benchmark.Editor.DataLayoutCalibratorBuild.$taskMethod", '-logFile', "`"$OutputDirectory/build.log`"")
    $taskBinary = Join-Path $taskRoot "Builds/windows-x64/$taskLabel/DataLayoutCalibrator.exe"
    $taskBurst = Join-Path $taskRoot "Builds/windows-x64/$taskLabel/DataLayoutCalibrator_Data/Plugins/x86_64/lib_burst_generated.dll"
    Invoke-MatrixProcess $taskBinary @('-batchmode', '-nographics', '-dla-matrix-probe', '-dla-matrix-probe-batches', $Batches, '-dla-matrix-receipt', "`"$OutputDirectory/matrix-receipt.json`"", '-logFile', "`"$OutputDirectory/player.log`"")
    if (-not (Test-Path -LiteralPath "$OutputDirectory/matrix-receipt.json")) { throw 'Player did not write a correctness receipt.' }
    $taskReceipt = Get-Content -LiteralPath "$OutputDirectory/matrix-receipt.json" -Raw | ConvertFrom-Json
    if ($taskReceipt.Checks -ne 2166 -or $taskReceipt.DevelopmentBuild -or -not $taskReceipt.BurstEnabled) { throw 'Player correctness gate failed.' }
    $taskIdentity = [ordered]@{
        kind = 'bounded-release-candidate-correctness-not-performance'
        sourceCommit = (& git -C $taskRoot rev-parse HEAD)
        sourceStatus = @(& git -C $taskRoot status --porcelain)
        backend = $Backend
        utc = [DateTime]::UtcNow.ToString('o')
        editor = $UnityEditor
        processor = (Get-CimInstance Win32_Processor | Select-Object Name, Manufacturer, ProcessorId, NumberOfCores, NumberOfLogicalProcessors)
        binarySha256 = (Get-FileHash -LiteralPath $taskBinary -Algorithm SHA256).Hash
        burstSha256 = (Get-FileHash -LiteralPath $taskBurst -Algorithm SHA256).Hash
        receiptSha256 = (Get-FileHash -LiteralPath "$OutputDirectory/matrix-receipt.json" -Algorithm SHA256).Hash
        limitations = @('Mono is correctness evidence only; formal gate requires IL2CPP Release.', 'Enabled Burst and nonempty AOT binary are recorded; no claim of per-job ISA disassembly.', 'No timing, hardware counters, winner, or causal estimate is produced.')
    }
    $taskIdentity | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$OutputDirectory/identity.json" -Encoding utf8
}
