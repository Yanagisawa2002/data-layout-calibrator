"""Explicit three-arm Babel Dot experiment. All execution uses the shared mutex.

Discovery and functional outputs are never included in confirmatory statistics.
Requires Python 3.11+ and psutil; uses only fresh process/attempt directories.
"""
import argparse
import ctypes
from ctypes import wintypes
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import statistics
import struct
import subprocess
import sys
import time

import psutil
from verify_output import babel, sha

ROOT = Path(__file__).resolve().parents[2]
TOOLS = ROOT/'Tools/ActualComparison'
OPS = ['Copy', 'Mul', 'Add', 'Triad', 'Dot']
# Every permutation: each arm occupies each position twice; each pair occurs in
# both orders three times. One sample is one fresh process, never an iteration.
ORDERS = [('native', 'serial', 'parallel'), ('serial', 'parallel', 'native'),
          ('parallel', 'native', 'serial'), ('native', 'parallel', 'serial'),
          ('parallel', 'serial', 'native'), ('serial', 'native', 'parallel')]


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('x', encoding='utf-8') as f:
        json.dump(value, f, indent=2, allow_nan=False)


def cpu_sets():
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    fn = kernel.GetSystemCpuSetInformation
    fn.argtypes = [ctypes.c_void_p, wintypes.ULONG, ctypes.POINTER(wintypes.ULONG), wintypes.HANDLE, wintypes.ULONG]
    length = wintypes.ULONG()
    fn(None, 0, ctypes.byref(length), None, 0)
    data = ctypes.create_string_buffer(length.value)
    if not fn(data, length, ctypes.byref(length), None, 0):
        raise ctypes.WinError(ctypes.get_last_error())
    rows, offset = [], 0
    while offset < length.value:
        size, kind = struct.unpack_from('<II', data, offset)
        if kind == 0:
            identifier, group, logical, core, cache, numa, efficiency, flags = struct.unpack_from('<IHBBBBBB', data, offset+8)
            rows.append(dict(id=identifier, group=group, logical=logical, core=core,
                             lastLevelCache=cache, numa=numa, efficiencyClass=efficiency, flags=flags))
        if size < 8: raise ValueError('Invalid CPU set size')
        offset += size
    return rows


def load_snapshot(seconds=5):
    before = {}
    for p in psutil.process_iter(['pid', 'name', 'cpu_times']):
        if p.pid:
            before[p.pid] = (p.info['name'], sum(p.info['cpu_times'][:2]))
    start = time.monotonic()
    samples = [psutil.cpu_percent(interval=1) for _ in range(seconds)]
    elapsed = time.monotonic() - start
    processes = []
    for p in psutil.process_iter(['pid', 'cpu_times']):
        if p.pid in before:
            name, cpu = before[p.pid]
            percent = 100 * (sum(p.info['cpu_times'][:2]) - cpu) / elapsed / psutil.cpu_count()
            if percent >= .1: processes.append(dict(pid=p.pid, name=name, machineCpuPercent=round(percent, 3)))
    return dict(utc=datetime.now(timezone.utc).isoformat(), samplesPercent=samples,
                meanPercent=statistics.mean(samples), availableMemory=psutil.virtual_memory().available,
                topProcesses=sorted(processes, key=lambda p: -p['machineCpuPercent'])[:12])


def command(a, arm, prefix, chunk=None, functional=False):
    if arm == 'native': return [str(a.native), '--run', str(prefix)]
    return [str(a.player), '-batchmode', '-nographics', '-job-worker-count', str(a.workers),
            '-dla-external', 'babel-dot-check' if functional else 'babel',
            '-dla-external-output', str(prefix), '-dla-dot-mode', arm,
            '-dla-dot-chunk', str(chunk or a.chunk), '-logFile', str(prefix.parent/'player.log')]


def verify_freeze(a):
    frozen = json.loads((a.artifacts/'freeze.json').read_text())
    assert frozen['workers'] == a.workers and frozen['nativeThreads'] == a.threads and frozen['chunk'] == a.chunk
    for row in frozen['files']:
        if sha(row['path']) != row['sha256']: raise RuntimeError('Frozen file changed: '+row['path'])
    return sha(a.artifacts/'freeze.json')


def execute(a, name, arm, phase, chunk=None, frozen=False, functional=False):
    attempt = a.artifacts/name
    if attempt.exists(): raise RuntimeError('Attempt already exists: '+str(attempt))
    freeze_hash = verify_freeze(a) if frozen else None
    # Gate before process creation, preserving every rejected snapshot. No run is
    # excluded based on its measured performance. A busy machine aborts this phase.
    loads = []
    for _ in range(12 if phase == 'measurement' else 1):
        loads.append(load_snapshot())
        if loads[-1]['meanPercent'] <= 10: break
    write(a.artifacts/(name+'-preflight.json'), dict(loads=loads, freezeSha256=freeze_hash))
    if phase == 'measurement' and loads[-1]['meanPercent'] > 10:
        raise RuntimeError('Predeclared CPU idle gate (>10% for all 12 windows); no process started')
    child = command(a, arm, attempt/'result', chunk, functional)
    env = dict(os.environ, OMP_NUM_THREADS=str(a.threads), OMP_DYNAMIC='FALSE')
    for k in ('OMP_PROC_BIND', 'OMP_PLACES'): env.pop(k, None)
    wrapper = [sys.executable, str(TOOLS/'run_exclusive.py'), '--phase', phase, '--output', str(attempt),
               '--estimated-growth-gib', '1', '--timeout', '600', '--'] + child
    subprocess.run(wrapper, cwd=ROOT, env=env, check=True)
    invocation = json.loads((attempt/'invocation.json').read_text())
    assert invocation['exitCode'] == 0 and invocation['mutexReleased']
    result = json.loads((attempt/'result.json').read_text(encoding='utf-8-sig'))
    if functional:
        assert result['dotCorrectness']['passed']
        write(attempt/'checked.json', dict(passed=True, resultSha256=sha(attempt/'result.json')))
    else:
        write(attempt/'checked.json', babel(attempt/'result'))
        assert len(result['operationMs']) == 5
        for values in result['operationMs']:
            values = values['values'] if isinstance(values, dict) else values
            assert len(values) == 100 and all(math.isfinite(v) and v > 0 for v in values)
        if arm != 'native':
            assert result['fullArrayCheckPassed'] and result['workers'] == a.workers
            assert result['allocationEligibility'] == 'Unknown' and result['dotMode'] == arm
        else: assert result['threads'] == a.threads
    write(attempt/'postflight.json', load_snapshot(seconds=1))


def freeze(a):
    for arm in ('native','serial','parallel'):
        assert json.loads((a.artifacts/('correctness-'+arm)/'checked.json').read_text())['passed']
    one = json.loads((a.artifacts/'functional-1/result.json').read_text())['dotCorrectness']
    many = json.loads((a.artifacts/f'functional-{a.workers}/result.json').read_text())['dotCorrectness']
    assert one['passed'] and many['passed'] and one == many
    paths = []
    for folder in ('Packages','BenchmarkProject/Assets','BenchmarkProject/Packages','BenchmarkProject/ProjectSettings','Tools/ActualComparison'):
        paths += [p for p in (ROOT/folder).rglob('*') if p.is_file() and not set(p.parts)&{'bin','obj','__pycache__','Artifacts'}]
    paths += list(p for p in a.player.parent.rglob('*') if p.is_file())
    paths += [a.native, a.artifacts/'environment.json', a.artifacts/'logical-input.json', ROOT/'Docs/BABEL_DOT_PROTOCOL_2026-09-15.md']
    files = [dict(path=str(p.resolve()), sha256=sha(p), bytes=p.stat().st_size) for p in sorted(set(paths))]
    write(a.artifacts/'freeze.json', dict(createdUtc=datetime.now(timezone.utc).isoformat(),
          sourceCommit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
          gitStatus=subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True),
          files=files, workers=a.workers, nativeThreads=a.threads, chunk=a.chunk, orders=ORDERS))


def comparisons(reference, candidate):
    diffs = [b-a for a,b in zip(reference,candidate)]
    # df=5, fixed six independent process blocks, two-sided 95% Student-t.
    half = 2.570581835636305 * statistics.stdev(diffs) / math.sqrt(6)
    ref, cand, delta = statistics.mean(reference), statistics.mean(candidate), statistics.mean(diffs)
    return dict(referenceMeanMs=ref, candidateMeanMs=cand, differenceMs=delta,
                pairedDifference95CI=[delta-half,delta+half], reductionPercent=100*(ref-cand)/ref,
                ciAsPercentOfReference=[-100*(delta+half)/ref,-100*(delta-half)/ref],
                withinTwoPercent=(delta-half >= -.02*ref and delta+half <= .02*ref),
                status='improvement' if delta+half < 0 else 'regression' if delta-half > 0 else 'inconclusive')


def summarize(a):
    rows, by_arm = [], {arm: [] for arm in ORDERS[0]}
    for block, order in enumerate(ORDERS, 1):
        for position, arm in enumerate(order, 1):
            path = a.artifacts/f'measure-{block:02d}-{arm}'
            data = json.loads((path/'result.json').read_text())
            check = json.loads((path/'checked.json').read_text())
            assert check['passed']
            vals = [v['values'] if isinstance(v,dict) else v for v in data['operationMs']]
            row = dict(block=block, position=position, arm=arm, path=str(path),
                       **{op:statistics.mean(v[1:]) for op,v in zip(OPS,vals)},
                       **{k:data[k] for k in ('constructMs','initMs','exportMs','disposeMs','storageLifecycleMs','sum')},
                       outputSha256=check['outputSha256'], dotRelativeError=check['dotRelativeError'])
            rows.append(row); by_arm[arm].append(row)
    results = {}
    for ref, cand in [('serial','parallel'), ('native','serial'), ('native','parallel')]:
        results[ref+'-to-'+cand] = {key:comparisons([r[key] for r in by_arm[ref]],[r[key] for r in by_arm[cand]])
                                   for key in OPS+['storageLifecycleMs','constructMs','initMs','exportMs','disposeMs']}
    write(a.artifacts/'summary.json', dict(processCount=len(rows), independentBlocks=6, processRows=rows,
          armMeans95CI={arm:{key:dict(meanMs=statistics.mean(r[key] for r in samples),
            ci95=[statistics.mean(r[key] for r in samples)+sign*2.570581835636305*statistics.stdev(r[key] for r in samples)/math.sqrt(6) for sign in (-1,1)])
            for key in OPS+['storageLifecycleMs']} for arm,samples in by_arm.items()},
          comparisons=results, allocationEligibility='Unknown',
          caveat='Small-sample paired-t estimates; six fixed balanced orders; no multiplicity adjustment. Percent CI rescales difference CI by observed reference mean, not an independent ratio CI.'))


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--phase', required=True, choices=['environment','functional','discovery','correctness','freeze','measurement','summarize'])
    p.add_argument('--artifacts', type=Path, required=True)
    p.add_argument('--player', type=Path, required=True)
    p.add_argument('--native', type=Path, required=True)
    p.add_argument('--workers', type=int, default=19)
    p.add_argument('--threads', type=int, default=20)
    p.add_argument('--chunk', type=int, default=65536)
    a = p.parse_args()
    a.artifacts, a.player, a.native = a.artifacts.resolve(), a.player.resolve(), a.native.resolve()
    if a.phase == 'environment':
        script = "@{cpu=Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed; os=Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber; memory=Get-CimInstance Win32_PhysicalMemory | Select-Object Capacity,Speed,ConfiguredClockSpeed} | ConvertTo-Json -Depth 5"
        hardware = json.loads(subprocess.check_output(['powershell.exe','-NoProfile','-Command',script], text=True, encoding='utf-8',errors='replace'))
        write(a.artifacts/'environment.json', dict(hardware=hardware,cpuSets=cpu_sets(),
              affinity=psutil.Process().cpu_affinity(), memoryBytes=psutil.virtual_memory().total,
              cpuLoad=load_snapshot(), python=sys.version, psutil=psutil.__version__,workers=a.workers,nativeThreads=a.threads,
              packages=json.loads((ROOT/'BenchmarkProject/Packages/packages-lock.json').read_text()),
              unity=(ROOT/'BenchmarkProject/ProjectSettings/ProjectVersion.txt').read_text(),
              nativeBuild=json.loads((a.native.parent/'build-command.json').read_text())))
        subprocess.run([sys.executable,str(TOOLS/'logical_babel_input.py'),str(a.artifacts/'logical-input.json')],check=True)
    elif a.phase == 'functional':
        execute(a, 'functional-'+str(a.workers), 'parallel', 'correctness', functional=True)
    elif a.phase == 'discovery':
        for chunk in (16384,65536,262144): execute(a,'discovery-'+str(chunk),'parallel','correctness',chunk=chunk)
        execute(a,'discovery-serial','serial','correctness')
    elif a.phase == 'correctness':
        for arm in ('native','serial','parallel'): execute(a,'correctness-'+arm,arm,'correctness')
    elif a.phase == 'freeze': freeze(a)
    elif a.phase == 'measurement':
        for block, order in enumerate(ORDERS,1):
            for arm in order: execute(a,f'measure-{block:02d}-{arm}',arm,'measurement',frozen=True)
    else: summarize(a)


if __name__ == '__main__': main()
