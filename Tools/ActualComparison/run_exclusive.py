"""Run one explicit build/validation/measurement command under the shared machine mutex.

No implicit workload, unrelated process killing, affinity, priority, cache or power changes.
Each attempt has a new directory with its arguments, environment and complete output.
"""
import argparse
import ctypes
from ctypes import wintypes
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import shutil


def background_conflicts():
    # Never retain raw command lines: interactive Unity command lines can contain
    # Hub access tokens. Name, executable, PID and parent are sufficient to block.
    script = r"""@(Get-CimInstance Win32_Process | Where-Object {
      $_.Name -match '^(Unity|UnityShaderCompiler|UnityPackageManager|DataLayoutCalibrator|MSBuild|VBCSCompiler|cl|link|il2cpp|UnityLinker|bee_backend|llama_native|babel_native|stream_native)\.exe$'
    } | Select-Object ProcessId,ParentProcessId,Name,ExecutablePath) | ConvertTo-Json -Compress"""
    value = subprocess.check_output(['powershell.exe', '-NoProfile', '-NonInteractive', '-Command', script],
                                    creationflags=subprocess.CREATE_NO_WINDOW, text=True).strip()
    if not value:
        return []
    data = json.loads(value)
    return data if isinstance(data, list) else [data]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--phase', required=True, choices=['build', 'prepare', 'correctness', 'measurement'])
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--timeout', type=int, default=1800)
    parser.add_argument('--estimated-growth-gib', type=float, default=2)
    parser.add_argument('--minimum-free-gib', type=float, default=20)
    parser.add_argument('command', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ['--'] else args.command
    if not command or os.name != 'nt':
        parser.error('An explicit command on Windows is required.')
    args.output.mkdir(parents=True, exist_ok=False)
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.CreateMutexW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR]
    kernel.CreateMutexW.restype = wintypes.HANDLE
    kernel.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    kernel.ReleaseMutex.argtypes = [wintypes.HANDLE]
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    handle = kernel.CreateMutexW(None, False, 'Local\\CodexR9700VNextUnityGpu')
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    acquired = False
    receipt = dict(phase=args.phase, command=command, cwd=os.getcwd(),
                   environment={k: os.environ.get(k) for k in ('OMP_NUM_THREADS', 'OMP_DYNAMIC', 'OMP_PROC_BIND', 'OMP_PLACES')},
                   startedUtc=datetime.now(timezone.utc).isoformat(), status='starting',
                   processWallTimeIsLifecycleMeasurement=False)
    def save():
        (args.output / 'invocation.json').write_text(json.dumps(receipt, indent=2), encoding='utf-8')
    save()
    try:
        wait = kernel.WaitForSingleObject(handle, 0)
        if wait not in (0, 0x80):
            raise RuntimeError('Shared workload mutex is occupied; no child was started.')
        acquired = True
        receipt['freeGiBBefore'] = shutil.disk_usage(args.output).free / 2**30
        receipt['estimatedGrowthGiB'] = args.estimated_growth_gib
        receipt['minimumFreeGiB'] = args.minimum_free_gib
        if args.estimated_growth_gib < 0 or args.minimum_free_gib < 0:
            raise ValueError('Disk budgets must be nonnegative.')
        if receipt['freeGiBBefore'] - args.estimated_growth_gib < args.minimum_free_gib:
            raise RuntimeError('Stage disk budget would breach the free-space reserve; no child started.')
        receipt['backgroundConflicts'] = background_conflicts()
        if receipt['backgroundConflicts']:
            raise RuntimeError('Background Unity/compiler/workload processes are active; no child started.')
        with (args.output / 'stdout.txt').open('w', encoding='utf-8') as stdout, (args.output / 'stderr.txt').open('w', encoding='utf-8') as stderr:
            process = subprocess.Popen(command, stdout=stdout, stderr=stderr, creationflags=subprocess.CREATE_NO_WINDOW)
            receipt.update(pid=process.pid, status='running'); save()
            try:
                receipt['exitCode'] = process.wait(timeout=args.timeout)
            except subprocess.TimeoutExpired:
                # Only this wrapper's child and its descendants; never unrelated user processes.
                subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'], stdout=stderr, stderr=stderr,
                               creationflags=subprocess.CREATE_NO_WINDOW, check=False)
                process.wait()
                raise RuntimeError('Explicit command exceeded its recorded timeout.')
        receipt['status'] = 'passed' if receipt['exitCode'] == 0 else 'failed'
    except Exception as error:
        receipt.update(status='failed', error=str(error))
        raise
    finally:
        receipt['completedUtc'] = datetime.now(timezone.utc).isoformat()
        receipt['freeGiBAfter'] = shutil.disk_usage(args.output).free / 2**30
        if acquired:
            kernel.ReleaseMutex(handle)
        kernel.CloseHandle(handle)
        receipt['mutexReleased'] = acquired
        save()
        print(json.dumps(receipt), flush=True)
    return receipt['exitCode']


if __name__ == '__main__':
    raise SystemExit(main())
