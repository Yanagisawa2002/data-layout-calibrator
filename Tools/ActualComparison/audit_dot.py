"""Offline closeout audit of the frozen Babel Dot experiment; no workload timing.

Run after measurement under the shared mutex, before releasing the hardware.
Checks every retained full-output byte by hash against its numerical receipt.
"""
import argparse
from datetime import datetime
import hashlib
import gzip
import json
from pathlib import Path
import re
import subprocess
import shutil

from dot_experiment import ORDERS, ROOT, sha, write


def serial_job(text):
    start = text.index('[BurstCompile] public struct BabelDotContractJob')
    end = text.index('\n    }', start) + len('\n    }')
    return text[start:end]


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--artifacts', type=Path, required=True)
    p.add_argument('--receipt', type=Path, required=True)
    p.add_argument('--compressed-output', type=Path)
    a = p.parse_args()
    r = a.artifacts.resolve()
    frozen = json.loads((r/'freeze.json').read_text())
    for item in frozen['files']:
        assert sha(item['path']) == item['sha256'], 'Changed frozen file: '+item['path']
    identities = list(r.glob('player-*/source-identity.json'))
    identity = json.loads(identities[-1].read_text())
    for item in identity['Inputs']:
        assert sha(ROOT/item['Path']).lower() == item['Sha256'].lower(), 'Build input changed: '+item['Path']
    player = identities[-1].parent
    aot_path = next(player.glob('*BurstDebugInformation*/**/lib_burst_generated.txt'))
    aot = aot_path.read_text()
    for job in ('BabelDotContractJob','BabelDotPartialJob','BabelDotMergeJob'):
        assert job in aot, 'AOT entry missing: '+job
    port = 'Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Runtime/BabelStreamPort.cs'
    original = subprocess.check_output(['git','show','8e83a17338452c9d202f45ed553e0c807a7b8558:'+port], cwd=ROOT, text=True)
    original_job = serial_job(original)
    assert original_job == serial_job((ROOT/port).read_text()), 'Original serial job changed'
    stages = []
    for path in r.glob('*/invocation.json'):
        if path.parent.name.startswith('audit-'): continue
        stage = json.loads(path.read_text())
        assert stage['status'] != 'running', 'Incomplete stage: '+str(path)
        assert stage['mutexReleased'], 'Mutex not released: '+str(path)
        assert not stage['backgroundConflicts']
        assert stage['freeGiBBefore']-stage['estimatedGrowthGiB'] >= stage['minimumFreeGiB']
        if stage['status'] != 'passed':
            assert path.parent.name == 'player-build-01' and stage['exitCode'] == 1
        else: assert stage['exitCode'] == 0
        stages.append((path.parent.name,stage))
    stages.sort(key=lambda x:x[1]['startedUtc'])
    for (_,first),(_,second) in zip(stages,stages[1:]):
        assert datetime.fromisoformat(first['completedUtc']) <= datetime.fromisoformat(second['startedUtc']), 'Stage overlap'
    measurement_names = [name for name,stage in stages if stage['phase']=='measurement']
    expected_order = [f'measure-{i:02d}-{arm}' for i,order in enumerate(ORDERS,1) for arm in order]
    assert measurement_names == expected_order
    freeze_sha = sha(r/'freeze.json')
    inputs, outputs, loads = [], [], []
    for name in expected_order:
        before = json.loads((r/(name+'-preflight.json')).read_text())
        assert before['freezeSha256'] == freeze_sha
        assert before['loads'][-1]['meanPercent'] <= 10
        loads.append(before['loads'][-1]['meanPercent'])
        inv = json.loads((r/name/'invocation.json').read_text())
        assert inv['environment']['OMP_NUM_THREADS']=='20' and inv['environment']['OMP_DYNAMIC']=='FALSE'
        result = json.loads((r/name/'result.json').read_text())
        assert result['count']==33554432 and result['iterations']==100
        if name.endswith('native'): assert result['threads']==20
        else:
            assert result['workers']==19 and result['allocationEligibility']=='Unknown' and result['fullArrayCheckPassed']
            assert 'Null' in (r/name/'player.log').read_text()
    for receipt in sorted(r.glob('*/checked.json')):
        checked = json.loads(receipt.read_text())
        assert checked['passed']
        binary = receipt.parent/'result.bin'
        if binary.exists():
            assert binary.stat().st_size == 805306368
            assert sha(binary)==checked['outputSha256'], 'Changed persisted array bytes'
            assert sha(receipt.parent/'result.json')==checked['timingSha256']
            outputs.append(dict(path=str(binary), bytes=binary.stat().st_size, sha256=checked['outputSha256']))
        else:
            assert sha(receipt.parent/'result.json')==checked['resultSha256']
    assert len(outputs)==25 # 4 discovery + 3 prerequisite + 18 formal
    one = json.loads((r/'functional-1/result.json').read_text())['dotCorrectness']
    many = json.loads((r/'functional-19/result.json').read_text())['dotCorrectness']
    assert one==many and one['passed'] and len(one['cases'])==28 and one['negativeControlsRejected']==9
    summary = json.loads((r/'summary.json').read_text())
    assert summary['processCount']==18 and summary['independentBlocks']==6
    compressed = None
    if a.compressed_output:
        assert len(set(x['sha256'] for x in outputs))==1
        exemplar = r/'measure-01-native/result.bin'
        with a.compressed_output.open('xb') as target:
            with gzip.GzipFile(fileobj=target, filename='', mode='wb', mtime=0) as zipped:
                with exemplar.open('rb') as source: shutil.copyfileobj(source, zipped, 1024*1024)
        with gzip.open(a.compressed_output,'rb') as source:
            decompressed = hashlib.file_digest(source,'sha256').hexdigest()
        assert decompressed==outputs[0]['sha256']
        compressed = dict(path=str(a.compressed_output), sha256=sha(a.compressed_output),
            bytes=a.compressed_output.stat().st_size, originalPath=str(exemplar),
            decompressedBytes=805306368, decompressedSha256=decompressed,
            appliesToAll25FullOutputsByVerifiedContentHash=True)
    write(a.receipt, dict(passed=True, frozenFilesChecked=len(frozen['files']),
        buildInputsChecked=len(identity['Inputs']), buildSourceCommit=identity['SourceCommit'],
        retainedBuildDirtyStatus=identity['GitStatus'], sourceIdentitySha256=sha(identities[-1]),
        serialJobIdenticalToMain=True, serialJobNormalizedUtf8Sha256=hashlib.sha256(original_job.encode()).hexdigest(),
        aotManifestSha256=sha(aot_path), freezeSha256=freeze_sha,
        completedStages=len(stages), noStageOverlap=True, allFormalExitCodesZero=True,
        formalCpuPreflightMeanPercentRange=[min(loads),max(loads)],
        formalProcesses=18, fullDefaultCheckedProcesses=len(outputs),
        checkedArrayValuesAcrossFullProcesses=100663296*len(outputs),
        distinctFullOutputHashes=sorted(set(x['sha256'] for x in outputs)),
        fullOutputFiles=outputs, compressedOutput=compressed, summarySha256=sha(r/'summary.json'), functionalCasesPerWorkerCount=28,
        workersChecked=[1,19], negativeControlsPerWorkerCount=9,
        allocationEligibility='Unknown', failuresRetained=['player-build-01: missing installed IL2CPP module']))
    print('PASS: frozen source/binaries, 25 complete outputs, original serial identity, AOT, balanced order, gates and exits.')


if __name__ == '__main__': main()
