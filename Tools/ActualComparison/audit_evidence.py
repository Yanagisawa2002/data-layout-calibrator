"""Verify stage receipts and bind immutable local evidence, including old failures."""
from datetime import datetime
import hashlib
import json
from pathlib import Path
import shutil
import sys

ROOT=Path(__file__).resolve().parents[2]
BASE=ROOT/'Artifacts/actual-20260910'
OUT=ROOT/'Docs/evidence/actual-20260910'


def sha(p):
    with p.open('rb') as f: return hashlib.file_digest(f,'sha256').hexdigest()


def main():
    # Exclude our own still-open wrapper receipt/logs from the immutable inventory.
    active=Path(sys.argv[1]).resolve()
    assert active.parent==BASE and active.name.startswith('evidence-audit-')
    stages=[]
    for p in sorted(BASE.glob('*/invocation.json')):
        if p.parent==active: continue
        r=json.loads(p.read_text())
        assert r['status'] in ('passed','failed') and r['mutexReleased'], str(p)
        assert r['freeGiBBefore']-r['estimatedGrowthGiB']>=20 and r['freeGiBAfter']>=20, str(p)
        assert not r['backgroundConflicts'], str(p)
        stages.append((r['startedUtc'],r['completedUtc'],p.parent.name,r['status']))
    stages.sort()
    for a,b in zip(stages,stages[1:]):
        assert datetime.fromisoformat(a[1])<=datetime.fromisoformat(b[0]), 'Overlapping explicit stages'
    checks=list(BASE.glob('*-check/correctness.json'))
    assert len(checks)==27 and all(json.loads(p.read_text())['passed'] for p in checks)
    assert len(list(BASE.glob('measure-*/result.json')))==19
    assert len(list(BASE.glob('correct-*/result.json')))==8
    player=list(BASE.glob('*-burst/result.json'))
    assert len(player)==11
    for p in player:
        r=json.loads(p.read_text())
        assert r['workers']==7 and r['allocationEligibility']=='Unknown'
        c=r['allocationCapability']
        assert c['Availability']==2 and c['ObservedPositiveBytes']==0 and not c['PositiveControlPassed']
    # This imports only hash checks; it never starts a child or workload.
    from run_phase import verify_freeze
    verify_freeze()
    OUT.mkdir(parents=True,exist_ok=False)
    for source,name in [(BASE/'freeze-01.json','freeze-01.json'),(BASE/'analysis-01/summary.json','summary.json'),
                        (BASE/'environment-final.json','environment.json')]:
        shutil.copyfile(source,OUT/name)
    entries=[]
    for folder in (ROOT/'Artifacts/actual-20260909',BASE):
        for p in sorted(folder.rglob('*')):
            if not p.is_file() or active in p.parents or p.name=='resume-outcome.json': continue
            entries.append(dict(path=p.relative_to(ROOT).as_posix(),bytes=p.stat().st_size,sha256=sha(p)))
    manifest=dict(schema=1,sourceCommit=json.loads((BASE/'freeze-01.json').read_text())['sourceCommit'],
                  scope='Local raw successful/failed attempts and binaries; excludes this audit open logs and final receipt',
                  serialStages=stages,formalProcesses=19,correctnessProcesses=8,fullOutputChecks=27,
                  playerUnavailablePositiveControls=11,files=entries)
    (OUT/'evidence-index.json').write_text(json.dumps(manifest,indent=2))
    print(json.dumps(dict(passed=True,stages=len(stages),files=len(entries),rawBytes=sum(e['bytes'] for e in entries),
                         indexSha256=sha(OUT/'evidence-index.json')),indent=2))


if __name__=='__main__': main()
