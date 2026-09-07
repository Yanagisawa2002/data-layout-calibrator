param(
    [Parameter(Mandatory=$true)][ValidateSet('Matrix','SuiteQuick','SuiteFormal')][string]$Stage,
    [Parameter(Mandatory=$true)][string]$Player,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [string]$SerializationRunner='C:/Users/EdwinLiu/Documents/Codex/2026-09-07/w-m/work/optimization-vnext/Invoke-SerializedValidation.ps1'
)
$ErrorActionPreference='Stop'
$Player=(Resolve-Path -LiteralPath $Player).Path
$evidence=[IO.Path]::GetFullPath($EvidenceDirectory)
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(Test-Path -LiteralPath $evidence){throw 'Use a fresh evidence directory; prior attempts are immutable.'}
New-Item -ItemType Directory -Path $evidence | Out-Null
function Snapshot {
    [ordered]@{utc=[DateTime]::UtcNow.ToString('O'); powerPlan=(@(& powercfg.exe /getactivescheme) -join ' ');
        processes=@(Get-Process | ForEach-Object { $start=$null; try{$start=$_.StartTime.ToUniversalTime().ToString('O')}catch{};
            [ordered]@{id=$_.Id;name=$_.ProcessName;cpuSeconds=$_.CPU;startUtc=$start}});
        limits='External apps, affinity, caches, clocks and thermal state uncontrolled.'}
}
& $SerializationRunner -Action {
    $arguments=@('-batchmode','-nographics','-job-worker-count','7','-logFile', ('"'+(Join-Path $evidence 'player.log')+'"'))
    $record=[ordered]@{stage=$Stage;sourceCommit=(& git -C $repo rev-parse HEAD).Trim();sourceStatus=@(& git -C $repo status --porcelain);
        player=$Player;startedUtc=[DateTime]::UtcNow.ToString('O');exitCode=$null;result='running';error=$null}
    if($Stage -eq 'Matrix'){
        $arguments+=@('-dla-matrix-probe','-dla-matrix-probe-batches','64,256','-dla-matrix-receipt',('"'+(Join-Path $evidence 'matrix.json')+'"'))
    }else{
        $formal=$Stage -eq 'SuiteFormal'
        $count=if($formal){65536}else{4099}; $holdout=if($formal){65539}else{4103}
        $samples=if($formal){40}else{6}; $boundary=if($formal){20}else{4}; $bootstrap=if($formal){4000}else{200}
        $target=if($formal){2}else{0.2};$maxTicks=if($formal){64}else{8};$warmSeconds=if($formal){0.05}else{0}
        $arguments+=@('-dla-run','-dla-quit','-dla-count',$count,'-dla-holdout-count',$holdout,'-dla-samples',$samples,
            '-dla-boundary-samples',$boundary,'-dla-bootstrap-iterations',$bootstrap,'-dla-lifetime-ticks','256',
            '-dla-warmup-blocks','4','-dla-min-warmup-seconds',$warmSeconds,'-dla-target-block-ms',$target,'-dla-max-ticks',$maxTicks,
            '-dla-output',('"'+(Join-Path $evidence 'suite')+'"'))
    }
    $record.arguments=$arguments
    Snapshot | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'interference-before.json') -Encoding utf8NoBOM
    $build=Split-Path -Parent $Player
    @(Get-ChildItem -LiteralPath $build -File -Recurse | Where-Object{$_.Extension -in '.dll','.exe','.dat'} | Sort-Object FullName | ForEach-Object{
        [ordered]@{path=[IO.Path]::GetRelativePath($build,$_.FullName);bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
    }) | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'binary-hashes.json') -Encoding utf8NoBOM
    try {
        $process=Start-Process -FilePath $Player -ArgumentList $arguments -WindowStyle Hidden -PassThru
        $record.processId=$process.Id
        $process.WaitForExit();$record.exitCode=$process.ExitCode;$process.Dispose()
        if($record.exitCode -ne 0){throw "Player failed: $($record.exitCode)"}
        if($Stage -eq 'Matrix'){
            $receipt=Get-Content -LiteralPath (Join-Path $evidence 'matrix.json') -Raw | ConvertFrom-Json
            if($receipt.Checks -ne 2166 -or $receipt.DevelopmentBuild -or !$receipt.BurstEnabled -or @($receipt.Candidates).Count -ne 60){throw 'Incomplete matrix receipt.'}
        }else{
            $receipt=Get-Content -LiteralPath (Join-Path $evidence 'suite/calibration-suite.json') -Raw | ConvertFrom-Json
            if($receipt.Environment.BuildType -ne 'Release' -or $receipt.Environment.JobWorkerCount -ne 7){throw 'Build/worker identity mismatch.'}
            if($formal -and $receipt.Environment.ScriptingBackend -ne 'IL2CPP'){throw 'Formal suite requires IL2CPP.'}
            $ids=@($receipt.Scenarios.Scenario.ScenarioId | Sort-Object)
            if(($ids -join ',') -ne 'animation-state-v1,particle-integrate-v2,spatial-neighborhood-v1,transform-export-v1'){throw 'Registry workload set changed.'}
            $total=0
            foreach($profile in $receipt.Scenarios){
                if(!$profile.ManagedAllocationMeasurement -or $profile.ElementCount -ne $count -or $profile.HoldoutElementCount -ne $holdout -or
                    $profile.SamplesPerCandidate -ne $samples -or $profile.BoundarySamplesPerCandidate -ne $boundary -or $profile.BootstrapIterations -ne $bootstrap){throw 'Profile budget or allocation identity missing.'}
                $total+=@($profile.CalibrationResults).Count
                foreach($candidate in @($profile.CalibrationResults)+@($profile.HoldoutBaselineResult,$profile.HoldoutSelectedResult)){
                    if($null -eq $candidate){continue}
                    if(!$candidate.ParityPassed -or $candidate.HotPathManagedAllocationBytes -ne 0 -or $candidate.BoundaryManagedAllocationBytes -ne 0){throw 'Correctness/allocation gate failed.'}
                }
            }
            if($total -ne 64){throw 'Expected all64 default registry candidates.'}
        }
        $record.result='passed'
    }catch{$record.result='failed';$record.error=$_.Exception.Message;throw}
    finally{
        $record.completedUtc=[DateTime]::UtcNow.ToString('O')
        $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'invocation.json') -Encoding utf8NoBOM
        Snapshot | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'interference-after.json') -Encoding utf8NoBOM
    }
    "$Stage passed; evidence: $evidence"
}
