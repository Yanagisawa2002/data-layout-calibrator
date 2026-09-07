param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('EditMode', 'Mono', 'IL2CPP')][string]$Stage,
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.5.3f1/Editor/Unity.exe',
    [string]$SerializationRunner = 'C:/Users/EdwinLiu/Documents/Codex/2026-09-07/w-m/work/optimization-vnext/Invoke-SerializedValidation.ps1',
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
$Unity = (Resolve-Path -LiteralPath $Unity).Path
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath (Join-Path $evidence 'invocation.json')) {
    throw 'Evidence directory already contains an invocation; use a fresh attempt directory.'
}
New-Item -ItemType Directory -Path $evidence -Force | Out-Null

function Get-ProcessSnapshot {
    @(Get-Process | ForEach-Object {
        $observedProcess = $_
        try {
            [ordered]@{ id=$observedProcess.Id; name=$observedProcess.ProcessName; cpuSeconds=$observedProcess.CPU;
                startUtc=$observedProcess.StartTime.ToUniversalTime().ToString('O') }
        } catch {
            [ordered]@{ id=$observedProcess.Id; name=$observedProcess.ProcessName; unavailable=$_.Exception.Message }
        }
    })
}

& $SerializationRunner -Action {
    $arguments = @('-batchmode', '-nographics', '-projectPath',
        (Join-Path $repository 'BenchmarkProject'), '-logFile', (Join-Path $evidence 'unity.log'))
    if ($Stage -eq 'EditMode') {
        $arguments += @('-runTests', '-testPlatform', 'EditMode', '-testResults',
            (Join-Path $evidence 'editmode.xml'))
    } else {
        $method = if ($Stage -eq 'Mono') { 'BuildWindowsMonoAotEvidence' } else { 'BuildWindowsIl2CppFormal' }
        $arguments += @('-quit', '-executeMethod',
            ('Yanagisawa.DataLayoutCalibrator.Benchmark.Editor.DataLayoutCalibratorBuild.' + $method))
    }
    $record = [ordered]@{
        schemaVersion=1; stage=$Stage; repository=$repository;
        sourceCommit=(& git -C $repository rev-parse HEAD | Out-String).Trim();
        sourceStatus=(& git -C $repository status --porcelain | Out-String).Trim();
        executable=$Unity; executableSha256=(Get-FileHash -LiteralPath $Unity -Algorithm SHA256).Hash;
        arguments=$arguments; startedUtc=[DateTime]::UtcNow.ToString('O');
        mutex='Local\CodexR9700VNextUnityGpu'; processId=$null; exitCode=$null;
        completedUtc=$null; elapsedSeconds=$null; result='running'; error=$null
    }
    Get-ProcessSnapshot | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'processes-before.json') -Encoding utf8
    $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'invocation.json') -Encoding utf8
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        $quotedArguments = @($arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' })
        $process = Start-Process -FilePath $Unity -ArgumentList $quotedArguments -WorkingDirectory $repository -WindowStyle Hidden -PassThru
        $record.processId = $process.Id
        $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'invocation.json') -Encoding utf8
        # Track observed descendants while their parents still exist. Waiting only
        # on Unity lets Roslyn escape the mutex; Start-Process -Wait can instead
        # retain a job after Unity exits. Explicit ownership also permits a normal
        # shutdown of our own compiler pipe without touching a pre-existing server.
        $owned = @{}
        $owned[$process.Id] = [ordered]@{ id=$process.Id; parent=$PID; name='Unity.exe'; creation=$process.StartTime; shutdownRequested=$false }
        do {
            $snapshot = @(Get-CimInstance Win32_Process -Property ProcessId,ParentProcessId,CreationDate,Name)
            do {
                $discovered = $false
                foreach ($child in $snapshot) {
                    $childId = [int]$child.ProcessId
                    $parentId = [int]$child.ParentProcessId
                    if (-not $owned.ContainsKey($childId) -and $owned.ContainsKey($parentId) -and
                        $child.CreationDate -ge $owned[$parentId].creation) {
                        $owned[$childId] = [ordered]@{ id=$childId; parent=$parentId; name=$child.Name; creation=$child.CreationDate; shutdownRequested=$false }
                        $discovered = $true
                    }
                }
            } while ($discovered)
            $process.Refresh()
            if ($process.HasExited) {
                foreach ($child in $snapshot) {
                    $childId = [int]$child.ProcessId
                    if (-not $owned.ContainsKey($childId) -or $owned[$childId].shutdownRequested -or
                        $child.Name -ne 'dotnet.exe' -or $child.CreationDate -ne $owned[$childId].creation) { continue }
                    $details = Get-CimInstance Win32_Process -Filter "ProcessId=$childId"
                    if ($details.ExecutablePath -like ((Split-Path $Unity) + '\Data\DotNetSdk\*') -and
                        $details.CommandLine -match 'exec\s+"([^"]+VBCSCompiler\.dll)"\s+"?-pipename:([^"\s]+)') {
                        $compilerDll = $Matches[1]; $compilerPipe = $Matches[2]
                        $owned[$childId].shutdownRequested = $true
                        $owned[$childId].compilerPipe = $compilerPipe
                        $cleanup = Start-Process -FilePath $details.ExecutablePath -ArgumentList @('exec', ('"' + $compilerDll + '"'), ('-pipename:' + $compilerPipe), '-shutdown') -WindowStyle Hidden -PassThru
                        $cleanup.WaitForExit()
                        $owned[$childId].shutdownExitCode = $cleanup.ExitCode
                        $cleanup.Dispose()
                    }
                }
            }
            $liveOwned = @($snapshot | Where-Object { $owned.ContainsKey([int]$_.ProcessId) -and $_.CreationDate -eq $owned[[int]$_.ProcessId].creation })
            if ($liveOwned.Count -gt 0) { Start-Sleep -Milliseconds 500 }
        } while (-not $process.HasExited -or $liveOwned.Count -gt 0)
        $process.WaitForExit()
        @($owned.Values) | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'owned-processes.json') -Encoding utf8
        $record.exitCode = $process.ExitCode
        $process.Dispose()
        if ($record.exitCode -ne 0) { throw "Unity exited $($record.exitCode); inspect unity.log." }
        if ($Stage -eq 'EditMode') {
            [xml]$testResult = Get-Content -LiteralPath (Join-Path $evidence 'editmode.xml') -Raw
            if ($testResult.'test-run'.result -ne 'Passed' -or [int]$testResult.'test-run'.failed -ne 0) {
                throw 'EditMode XML does not report a passing test run.'
            }
        } else {
            $label = if ($Stage -eq 'Mono') { 'mono-aot-evidence' } else { 'il2cpp-formal' }
            $buildDirectory = Join-Path $repository ('Builds/windows-x64/' + $label)
            $artifacts = @(Get-ChildItem -LiteralPath $buildDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
                [ordered]@{ path=[IO.Path]::GetRelativePath($buildDirectory, $_.FullName);
                    bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
            })
            $artifacts | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'build-artifacts.json') -Encoding utf8
        }
        $record.result = 'passed'
    } catch {
        $record.result = 'failed'
        $record.error = $_.Exception.Message
        throw
    } finally {
        $timer.Stop()
        $record.elapsedSeconds = $timer.Elapsed.TotalSeconds
        $record.completedUtc = [DateTime]::UtcNow.ToString('O')
        $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'invocation.json') -Encoding utf8
        Get-ProcessSnapshot | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'processes-after.json') -Encoding utf8
    }
    Write-Output "$Stage passed in $([Math]::Round($record.elapsedSeconds, 1)) seconds. Evidence: $evidence"
}
