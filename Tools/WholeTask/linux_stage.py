"""Foreground Linux resource gate inside the coordinator's real SSH flock.

Never consults the Windows queue. The ancestor flock and its open lock inode
must be present; a JSON grant alone cannot launch a hardware stage.
"""
import argparse
from datetime import datetime, timezone
import fcntl
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import time


def save(path, value):
    path.write_text(json.dumps(value, indent=2)+"\n")


def lock_ancestor(path):
    expected = path.stat()
    pid = os.getpid()
    for _ in range(12):
        status = Path(f"/proc/{pid}/stat").read_text()
        comm = status.split("(",1)[1].rsplit(")",1)[0]
        if comm == "flock":
            for fd in Path(f"/proc/{pid}/fd").iterdir():
                try:
                    actual = fd.stat()
                    if (actual.st_dev, actual.st_ino) == (expected.st_dev, expected.st_ino):
                        with path.open("a") as probe:
                            try:
                                fcntl.flock(probe, fcntl.LOCK_EX|fcntl.LOCK_NB)
                            except BlockingIOError:
                                return dict(pid=pid, inode=actual.st_ino, path=str(path), held=True)
                            fcntl.flock(probe, fcntl.LOCK_UN)
                        raise RuntimeError("Ancestor opened the lock but it was not held")
                except FileNotFoundError:
                    pass
        pid = int(status.rsplit(") ",1)[1].split()[1])
        if pid <= 1:
            break
    raise RuntimeError("No actual ancestor flock for this host; launch with the authorized helper --lock")


def snapshot():
    return {p[0]: list(map(int,p[1:9])) for line in Path("/proc/stat").read_text().splitlines()
            if (p:=line.split()) and p[0].startswith("cpu")}


def choose_cpu():
    allowed = os.sched_getaffinity(0)
    a = snapshot(); time.sleep(1); b = snapshot()
    loads = {}
    for cpu in sorted(allowed):
        before, after = a[f"cpu{cpu}"], b[f"cpu{cpu}"]
        total = sum(after)-sum(before)
        loads[cpu] = 100*(total-(after[3]+after[4]-before[3]-before[4]))/total if total else 100
    # Choose the first qualifying physical core, not an application timing winner.
    for cpu in sorted(allowed):
        if cpu < 2:
            continue
        siblings = [int(x) for x in Path(f"/sys/devices/system/cpu/cpu{cpu}/topology/thread_siblings_list").read_text().strip().split(",")]
        if cpu == min(siblings) and all(loads.get(s,100) <= 10 for s in siblings):
            return dict(cpu=cpu, siblings=siblings, observedCpuPercent=loads,
                        rule="First allowed physical core >=2 with both SMT threads <=10% during a one-second preflight")
    raise RuntimeError("No quiet physical CPU and sibling available")


def main(args):
    args.output.mkdir(parents=True, exist_ok=False)
    receipt = dict(startedUtc=datetime.now(timezone.utc).isoformat(), pid=os.getpid(), command=args.command,
                   status="checking", performanceClaim=False)
    path = args.output/"stage.json"
    try:
        receipt["flock"] = lock_ancestor(args.lock)
        free = shutil.disk_usage(args.output).free
        receipt["availableBytes"] = free
        receipt["budgetBytes"] = int(args.growth_gib*2**30)
        if free < (args.growth_gib+10)*2**30:
            raise RuntimeError("Task budget plus 10 GiB data reserve not available")
        if args.cpu_file:
            cpu = json.loads(args.cpu_file.read_text())
            if cpu["cpu"] not in os.sched_getaffinity(0):
                raise RuntimeError("Frozen CPU no longer allowed")
        else:
            cpu = choose_cpu()
        receipt["cpuSelection"] = cpu
        save(args.output/"cpu-selection.json", cpu)
        os.sched_setaffinity(0, {cpu["cpu"]})
        from telemetry import CpuTelemetry
        receipt["preflight"] = CpuTelemetry().preflight()
        if not receipt["preflight"]["passed"]:
            raise RuntimeError("Linux CPU/cgroup preflight did not qualify")
        receipt["status"] = "running"; save(path,receipt)
        env = dict(os.environ, OMP_NUM_THREADS="1", OMP_DYNAMIC="FALSE", OPENBLAS_NUM_THREADS="1",
                   DOTNET_PROCESSOR_COUNT="1", MSBUILDDISABLENODEREUSE="1")
        command = args.command[1:] if args.command[:1] == ["--"] else args.command
        with (args.output/"stdout.log").open("w") as stdout, (args.output/"stderr.log").open("w") as stderr:
            child = subprocess.Popen(command, env=env, stdout=stdout, stderr=stderr, start_new_session=True)
            receipt["childPid"] = child.pid; save(path,receipt)
            try:
                receipt["exitCode"] = child.wait(timeout=args.timeout)
            except subprocess.TimeoutExpired:
                os.killpg(child.pid, signal.SIGTERM)
                try:
                    child.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    os.killpg(child.pid, signal.SIGKILL); child.wait()
                raise RuntimeError("Owned stage timed out; only its process group was stopped")
        receipt["status"] = "passed" if receipt["exitCode"] == 0 else "failed"
    except Exception as error:
        receipt["error"] = str(error); receipt["status"] = "failed"
        raise
    finally:
        receipt["endedUtc"] = datetime.now(timezone.utc).isoformat()
        save(path,receipt)
        print(json.dumps(receipt), flush=True)
    return receipt.get("exitCode",1)


if __name__ == "__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output",type=Path,required=True)
    parser.add_argument("--lock",type=Path,default=Path("/root/autodl-tmp/codex-whole-task-20260915/.hardware.lock"))
    parser.add_argument("--cpu-file",type=Path)
    parser.add_argument("--growth-gib",type=float,default=2)
    parser.add_argument("--timeout",type=int,default=3600)
    parser.add_argument("command",nargs=argparse.REMAINDER)
    raise SystemExit(main(parser.parse_args()))
