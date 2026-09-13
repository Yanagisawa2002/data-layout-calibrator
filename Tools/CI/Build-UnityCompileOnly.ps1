param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [string]$ProjectPath = (Join-Path $PSScriptRoot '../../BenchmarkProject'),
    [Parameter(Mandatory=$true)][string]$LogPath
)
$ErrorActionPreference = 'Stop'
$compiler = (Resolve-Path -LiteralPath $UnityEditor).Path
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
$log = [IO.Path]::GetFullPath($LogPath)
if ($project.Length -gt 140) { throw 'Unity dependency paths need a shorter isolated worktree. No worktree is moved or removed automatically.' }
if (-not (Test-Path -LiteralPath (Join-Path $project 'Packages/manifest.json'))) { throw 'Missing Unity dependency manifest.' }
if (Test-Path -LiteralPath $log) { throw 'Choose a new compile log path; old logs are retained.' }
# Editor import/script compilation only: no executeMethod, runTests, Player build or scene launch.
$argsList = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'), '-logFile', ('"' + $log + '"'))
$build = Start-Process -FilePath $compiler -ArgumentList $argsList -WindowStyle Hidden -PassThru -Wait
if ($build.ExitCode -ne 0) { throw "Unity compilation failed with exit code $($build.ExitCode)." }
if (-not (Test-Path -LiteralPath $log)) { throw 'Unity did not write a compilation log.' }
if (Select-String -LiteralPath $log -Pattern 'error CS\d+|Scripts have compiler errors' -Quiet) { throw 'Unity reported C# compilation errors.' }
Write-Output 'Headless Editor compilation complete. No Player or measurement was run.'
