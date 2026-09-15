"""Run one explicit foreground stage after predecessor handoffs and machine gates.

This never schedules a future run. Busy hardware/load/space ends this invocation
without launching its child. It never terminates unrelated processes.
"""
import argparse
import ctypes
from ctypes import wintypes
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import shutil
import subprocess

COORD = Path("D:/CodexWork/whole-task-validation-20260915/coordination")


def utc():
    return datetime.now(timezone.utc).isoformat()


def preflight():
    predecessors = {}
    for name in ("hlsl", "summit"):
        path = COORD / "handoff" / f"{name}.json"
        value = json.loads(path.read_text(encoding="utf-8-sig"))
        if value.get("terminal") is not True or value.get("hardwareReleased") is not True:
            raise RuntimeError(f"Predecessor {name} has not released hardware")
        predecessors[name] = value
    return predecessors


def ps(script):
    value = subprocess.check_output(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script],
        text=True, creationflags=subprocess.CREATE_NO_WINDOW).strip()
    return json.loads(value) if value else None


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--timeout", type=int, default=7200)
    parser.add_argument("--growth-gib", type=float, default=2)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if os.name != "nt" or not command:
        parser.error("Explicit Windows command required")
    args.output.mkdir(parents=True, exist_ok=False)
    receipt = dict(startedUtc=utc(), command=command, cwd=os.getcwd(), status="checking", mutexAcquired=False, mutexReleased=False)
    def save():
        (args.output / "invocation.json").write_text(json.dumps(receipt, indent=2)+"\n")
    save()
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateMutexW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR]
    kernel.CreateMutexW.restype = wintypes.HANDLE
    kernel.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    kernel.ReleaseMutex.argtypes = [wintypes.HANDLE]
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    handle = None
    try:
        receipt["predecessors"] = preflight()
        handle = kernel.CreateMutexW(None, False, "Local\\CodexR9700VNextUnityGpu")
        if not handle:
            raise ctypes.WinError(ctypes.get_last_error())
        if kernel.WaitForSingleObject(handle, 0) not in (0, 0x80):
            raise RuntimeError("Shared machine mutex is busy")
        receipt["mutexAcquired"] = True
        receipt["freeGiB"] = {d: shutil.disk_usage(d).free / 2**30 for d in ("C:/", "D:/")}
        if args.growth_gib < 0 or receipt["freeGiB"]["D:/"] - args.growth_gib < 20 or receipt["freeGiB"]["C:/"] < 10:
            raise RuntimeError("D: 20 GiB reserve after budget / C: 10 GiB OS reserve not satisfied")
        # Name/path only; never store raw command lines that may contain tokens.
        receipt["conflicts"] = ps(r"""@(Get-CimInstance Win32_Process | Where-Object {
          $_.Name -match '^(Unity|UnityShaderCompiler|UnityPackageManager|DataLayoutCalibrator|MSBuild|VBCSCompiler|cl|link|il2cpp|UnityLinker|bee_backend|llama_native|babel_native|simpleph|hlsl-kernel-pipeline|RTS)\.exe$'
        } | Select-Object ProcessId,ParentProcessId,Name,ExecutablePath) | ConvertTo-Json -Compress""")
        if receipt["conflicts"]:
            raise RuntimeError("Other build/workload processes are active")
        receipt["cpuPercent"] = ps(r"""@(Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 1 -MaxSamples 3 | ForEach-Object { $_.CounterSamples[0].CookedValue }) | ConvertTo-Json -Compress""")
        cpu = receipt["cpuPercent"]
        if not cpu or sum(cpu)/len(cpu) > 20 or max(cpu) > 30:
            raise RuntimeError("CPU preflight exceeds average 20% or peak 30%")
        gpu = subprocess.check_output(["nvidia-smi", "--query-gpu=name,utilization.gpu,memory.used,memory.total,temperature.gpu", "--format=csv,noheader,nounits"], text=True)
        receipt["gpu"] = gpu.strip()
        for line in gpu.strip().splitlines():
            if float(line.split(",")[1]) > 10:
                raise RuntimeError("GPU preflight exceeds 10%")
        receipt["status"] = "running"
        temporary = (args.output/"tmp").resolve(); temporary.mkdir()
        env = dict(os.environ, TMP=str(temporary), TEMP=str(temporary), DOTNET_CLI_TELEMETRY_OPTOUT="1",
                   MSBUILDDISABLENODEREUSE="1")
        with (args.output/"stdout.txt").open("w", encoding="utf-8") as stdout, (args.output/"stderr.txt").open("w", encoding="utf-8") as stderr:
            child = subprocess.Popen(command, stdout=stdout, stderr=stderr, env=env, creationflags=subprocess.CREATE_NO_WINDOW)
            receipt["pid"] = child.pid; save()
            try:
                receipt["exitCode"] = child.wait(timeout=args.timeout)
            except subprocess.TimeoutExpired:
                subprocess.run(["taskkill", "/PID", str(child.pid), "/T", "/F"], stdout=stderr, stderr=stderr,
                    creationflags=subprocess.CREATE_NO_WINDOW, check=False)
                child.wait()
                raise RuntimeError("Owned stage timed out; its process tree was stopped")
        receipt["status"] = "passed" if receipt["exitCode"] == 0 else "failed"
    except Exception as error:
        receipt["error"] = str(error); receipt["status"] = "failed"
        raise
    finally:
        receipt["completedUtc"] = utc()
        if receipt["mutexAcquired"]:
            if not kernel.ReleaseMutex(handle):
                raise ctypes.WinError(ctypes.get_last_error())
            receipt["mutexReleased"] = True
        if handle:
            kernel.CloseHandle(handle)
        save()
        print(json.dumps(receipt), flush=True)
    raise SystemExit(receipt.get("exitCode", 1))
