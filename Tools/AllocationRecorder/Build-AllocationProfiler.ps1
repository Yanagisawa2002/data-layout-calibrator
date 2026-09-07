param(
    [Parameter(Mandatory=$true)][string]$SerializationScript,
    [string]$DeveloperCommand = 'C:/Program Files/Microsoft Visual Studio/18/Community/Common7/Tools/VsDevCmd.bat'
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifact = Join-Path $repository 'Artifacts/allocation-native-build'
$destination = Join-Path $repository 'BenchmarkProject/Assets/Plugins/x86_64'
New-Item -ItemType Directory -Force $artifact,$destination | Out-Null
$source = Join-Path $PSScriptRoot 'allocation_profiler.cpp'
$binary = Join-Path $destination 'DlcAllocationProfiler.dll'
$batch = Join-Path $artifact 'build.cmd'
@"
@echo off
call "$DeveloperCommand" -arch=x64 -host_arch=x64
if errorlevel 1 exit /b 1
where cl
cl /nologo /Bv /LD /O2 /W4 /WX /EHsc /std:c++17 /MT /Fo"$artifact/allocation_profiler.obj" "$source" /link /OUT:"$binary" /IMPLIB:"$artifact/DlcAllocationProfiler.lib" /INCREMENTAL:NO
exit /b %errorlevel%
"@ | Set-Content -LiteralPath $batch -Encoding ascii
& $SerializationScript -Action {
    & cmd.exe /d /c $batch *> (Join-Path $artifact 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "Allocation profiler compilation failed: $LASTEXITCODE" }
}
[ordered]@{
    SourceSHA256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash;
    BinarySHA256 = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash;
    CompilerIdentityLog = (Join-Path $artifact 'build.log');
    CompilerIdentityLogSHA256 = (Get-FileHash -LiteralPath (Join-Path $artifact 'build.log') -Algorithm SHA256).Hash;
    Flags = '/LD /O2 /W4 /WX /EHsc /std:c++17 /MT, Windows x64';
    RuntimeABI = 'Loaded mono-2.0-bdwgc.dll or GameAssembly.dll exports only; no runtime replacement';
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifact 'build-provenance.json') -Encoding utf8
