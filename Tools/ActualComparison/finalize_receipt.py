"""Write a final receipt after checking idle processes and releasing a probe mutex.

No build, benchmark, counter, background queue or user-process termination.
Run directly (not inside the mutex wrapper), after the final local commit.
"""
import argparse
import ctypes
from ctypes import wintypes
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

from run_exclusive import background_conflicts

ROOT=Path(__file__).resolve().parents[2]
BASE=ROOT/'Artifacts/actual-20260910'

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--write',required=True,type=Path)
    args=parser.parse_args()
    if args.write.resolve()!=BASE/'resume-outcome.json' or args.write.exists():
        raise RuntimeError('Expected a new resume-outcome.json at the explicit run location')
    lock=ctypes.WinDLL('kernel32',use_last_error=True)
    lock.CreateMutexW.argtypes=[ctypes.c_void_p,wintypes.BOOL,wintypes.LPCWSTR]
    lock.CreateMutexW.restype=wintypes.HANDLE
    lock.WaitForSingleObject.argtypes=[wintypes.HANDLE,wintypes.DWORD]
    lock.ReleaseMutex.argtypes=[wintypes.HANDLE]
    lock.CloseHandle.argtypes=[wintypes.HANDLE]
    handle=lock.CreateMutexW(None,False,'Local\\CodexR9700VNextUnityGpu')
    if not handle: raise ctypes.WinError(ctypes.get_last_error())
    wait=lock.WaitForSingleObject(handle,0)
    acquired=wait in (0,0x80)
    released=False
    try:
        conflicts=background_conflicts()
    finally:
        if acquired: released=bool(lock.ReleaseMutex(handle))
        lock.CloseHandle(handle)
    freeze=json.loads((BASE/'freeze-01.json').read_text())
    failures=[]
    for p in sorted(BASE.glob('*/invocation.json')):
        r=json.loads(p.read_text())
        if r['status']!='passed': failures.append(str(p.relative_to(ROOT)).replace('\\','/'))
    result=dict(status='partial',sourceCommit=freeze['sourceCommit'],
                finalCommit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
                buildSourceCommit=freeze['buildSourceCommit'],buildSourceIncludesRecordedDirtyFiles=True,
                completedComparisons=['BabelStream Windows/OpenMP variant vs C#/Burst serial-Dot variant: five process pairs',
                                      'LLAMA code_comp native AoS vs native AoSoA16: three paired rounds',
                                      'LLAMA code_comp native AoS vs C#/Burst packed4: three paired rounds, cross-thread/compiler comparison'],
                unexecutedComparisons=[dict(name='STREAM 5.10',reason='Pinned POSIX source only; no prepared matching Windows/Burst driver'),
                                       dict(name='HeCBench stencil3d-omp',reason='No matching port/OpenMP target runtime'),
                                       dict(name='LLAMA native SoA',reason='Compiled in adapter, outside finite three-arm protocol')],
                correctness=dict(fullOutputChecksPassed=27,fullDefaultCorrectnessProcesses=8,formalProcesses=19,
                                 babelArrayValuesPerRun=100663296,llamaFieldsPerRun=458752,
                                 llamaIndependentSeed2Holdout=True,llamaAllOutputsBitExact=True,
                                 cpuProtocolTestsPassed=82,cpuProtocolTestsFailed=0,pairedStatisticCasesPassed=5,
                                 pinnedUpstreamFilesVerified=22,playerAotAndRealOutputValidated=True,
                                 playerPositiveControlsUnavailable=11,observedPositiveBytes=0,
                                 allocationEligibility='Unknown',profilePublished=False),
                performanceEvidencePaths=['Docs/ACTUAL_COMPARISON_REPORT_2026-09-10.md',
                    'Docs/evidence/actual-20260910/evidence-index.json','Artifacts/actual-20260910/analysis-01/summary.json']+
                    [p.parent.relative_to(ROOT).as_posix() for p in sorted(BASE.glob('measure-*/result.json'))],
                preservedFailedAttemptReceipts=failures,
                priorFailuresPreserved='Artifacts/actual-20260909/',
                freeGiB=shutil.disk_usage(BASE).free/2**30,resourcesReleased=acquired and released and not conflicts,
                resourceCheck=dict(mutex='Local\\CodexR9700VNextUnityGpu',probeAcquired=acquired,
                                   probeReleased=released,activeConflictProcesses=conflicts),
                blockers=['Allocation positive control returned zero in actual IL2CPP; current-thread provider unavailable, worker/native coverage absent',
                          'STREAM and HeCBench matching adapters/toolchain not prepared; no results fabricated'],
                freezeSha256=hashlib.sha256((BASE/'freeze-01.json').read_bytes()).hexdigest(),
                evidenceIndexSha256=hashlib.sha256((ROOT/'Docs/evidence/actual-20260910/evidence-index.json').read_bytes()).hexdigest(),
                completedUtc=datetime.now(timezone.utc).isoformat(),pushed=False,performanceQueueCreated=False)
    if not result['resourcesReleased']: result['blockers'].append('Final resource probe could not establish an idle released machine')
    args.write.write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps(result,indent=2))
