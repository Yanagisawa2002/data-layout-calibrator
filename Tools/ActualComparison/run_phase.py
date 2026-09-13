"""Finite explicit stages of the fixed protocol; each child owns the shared mutex.

No default run, background queue, calibration or algorithm selection. New attempt
directories are mandatory. This orchestrator must NOT itself be wrapped in the mutex.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'Artifacts/actual-20260910'
PLAYER = BASE/'player-il2cpp-02/DataLayoutCalibrator.exe'
NATIVE = dict(babel=BASE/'native-babel-01/babel_native.exe', llama=BASE/'native-llama-02/llama_native.exe')
FREEZE = BASE/'freeze-01.json'


def sha(p):
    with Path(p).open('rb') as f: return hashlib.file_digest(f,'sha256').hexdigest()


def stage(name, phase, command, growth=1):
    print('START '+name, flush=True)
    env = dict(os.environ, OMP_NUM_THREADS='8', OMP_DYNAMIC='FALSE')
    # Do not accidentally inherit an earlier experiment's binding strategy.
    if env.get('OMP_PROC_BIND') or env.get('OMP_PLACES'):
        raise RuntimeError('Unexpected inherited OpenMP placement settings')
    subprocess.run([sys.executable, str(ROOT/'Tools/ActualComparison/run_exclusive.py'),
                    '--phase', phase, '--output', str(BASE/name), '--estimated-growth-gib', str(growth),
                    '--timeout','1200','--',*map(str,command)],cwd=ROOT,env=env,check=True,
                   creationflags=subprocess.CREATE_NO_WINDOW)
    print('END '+name, flush=True)


def verify_freeze():
    lock = json.loads(FREEZE.read_text())
    for path, expected in lock['files'].items():
        if sha(ROOT/path) != expected: raise RuntimeError('Frozen identity changed: '+path)


def run(workload, arm, name, phase, seed=1):
    verify_freeze()
    prefix = BASE/name/'result'
    inp = BASE/f'prepare-seed{seed}'/'input.bin'
    if arm == 'burst':
        cmd = [PLAYER,'-batchmode','-nographics','-job-worker-count','7','-dla-external',workload,
               '-dla-external-output',prefix,'-logFile',BASE/name/'player.log']
        if workload == 'llama': cmd += ['-dla-external-input',inp]
    elif workload == 'babel': cmd = [NATIVE['babel'],'--run',prefix]
    else: cmd = [NATIVE['llama'],'--run',inp,prefix,arm]
    stage(name,phase,cmd,1 if workload=='babel' else .05)
    check = [sys.executable,ROOT/'Tools/ActualComparison/verify_output.py','--receipt',BASE/(name+'-check')/'correctness.json']
    if workload == 'babel': check += ['--babel',prefix]
    else: check += ['--llama',BASE/f'correct-llama-seed{seed}-AoS'/'result',prefix,inp]
    stage(name+'-check','correctness',check,.01)


def freeze():
    if FREEZE.exists(): raise RuntimeError('Freeze already exists')
    paths = []
    for pattern in ('Tools/ActualComparison/*.py','Tools/ActualComparison/*.cpp',
                    'Docs/ACTUAL_COMPARISON_PROTOCOL_2026-09-10.md'):
        paths += list(ROOT.glob(pattern))
    paths += [ROOT/'Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Upstream~/upstream-lock.json']
    # Bind every shipped Player file, not just its launcher.
    paths += [p for p in PLAYER.parent.rglob('*') if p.is_file()]
    for native in NATIVE.values(): paths += [native,native.parent/'build-command.json']
    for seed in (1,2): paths += [BASE/f'prepare-seed{seed}'/'input.bin']
    manifest = json.loads((PLAYER.parent/'source-identity.json').read_text())
    # Actual Unity build source hashes, including uncommitted host fixes.
    for entry in manifest['Inputs']:
        p=ROOT/entry['Path']
        if sha(p).upper()!=entry['Sha256']: raise RuntimeError('Build source changed: '+str(p))
        paths.append(p)
    data = dict(createdUtc=datetime.now(timezone.utc).isoformat(),
                sourceCommit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
                buildSourceCommit=manifest['SourceCommit'], buildGitStatus=manifest['GitStatus'],
                files={p.relative_to(ROOT).as_posix():sha(p) for p in sorted(set(paths))})
    FREEZE.write_text(json.dumps(data,indent=2))
    print('Frozen '+str(len(data['files']))+' source/input/build artifacts: '+sha(FREEZE))


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--phase',required=True,choices=['prepare','freeze','correctness','measurement'])
    parser.add_argument('--workload',choices=['babel','llama'])
    args=parser.parse_args()
    if args.phase=='prepare':
        for seed in (1,2): stage(f'prepare-seed{seed}','prepare',[NATIVE['llama'],'--prepare',BASE/f'prepare-seed{seed}'/'input.bin',seed],.01)
    elif args.phase=='freeze': freeze()
    elif not args.workload: parser.error('Explicit workload required')
    elif args.phase=='correctness':
        if args.workload=='babel':
            for arm in ('native','burst'): run('babel',arm,'correct-babel-'+arm,'correctness')
        else:
            for seed in (1,2):
                for arm in ('AoS','AoSoA16','burst'): run('llama',arm,f'correct-llama-seed{seed}-{arm}','correctness',seed)
    else:
        # Formal phase must not proceed merely because compilation succeeded.
        required = (['correct-babel-native','correct-babel-burst'] if args.workload=='babel' else
                    [f'correct-llama-seed{s}-{a}' for s in (1,2) for a in ('AoS','AoSoA16','burst')])
        for name in required:
            evidence=BASE/(name+'-check')/'correctness.json'
            if not json.loads(evidence.read_text())['passed']: raise RuntimeError('Correctness gate failed: '+name)
        orders = ([['native','burst'],['burst','native'],['native','burst'],['burst','native'],['native','burst']]
                  if args.workload=='babel' else [['AoS','AoSoA16','burst'],['AoSoA16','burst','AoS'],['burst','AoS','AoSoA16']])
        for round_index, arms in enumerate(orders,1):
            for arm in arms: run(args.workload,arm,f'measure-{args.workload}-r{round_index}-{arm}','measurement')
