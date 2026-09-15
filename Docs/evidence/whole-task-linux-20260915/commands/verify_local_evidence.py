"""Verify transferred evidence bytes, retaining a small inspectable index."""
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import tarfile

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Docs/evidence/whole-task-linux-20260915'
ARCHIVE=OUT/'raw-evidence.tar.gz'
EXPECTED='dbabf4cabbb9c73e8dccc3732e1bb99d6cc4e64d4d2ea87e620e3c56f36f6a1a'
def save(name,value):
    with (OUT/name).open('x',encoding='utf-8',newline='\n') as stream:
        json.dump(value,stream,indent=2);stream.write('\n')
actual=hashlib.sha256(ARCHIVE.read_bytes()).hexdigest()
assert actual==EXPECTED,(actual,EXPECTED)
with tarfile.open(ARCHIVE,'r:gz') as archive:
    members=archive.getmembers()
    assert all(m.isfile() and not Path(m.name).is_absolute() and '..' not in Path(m.name).parts for m in members)
    manifest=json.load(archive.extractfile('collection/files.json'))
    summary=json.load(archive.extractfile('collection/summary.json'))
    verified=0
    for member in members:
        if member.name in manifest['files']:
            raw=archive.extractfile(member).read()
            expected=manifest['files'][member.name]
            assert len(raw)==expected['bytes'],member.name
            assert hashlib.sha256(raw).hexdigest()==expected['sha256'],member.name
            verified+=1
    assert verified==len(manifest['files'])
    copied=[]
    prefixes=('validation-04/','discovery-stage-10/','correctness-stage-09/')
    for member in members:
        name=member.name
        if (name.startswith(prefixes) and '/tmp/' not in name or
            name in ('native-build-04/manifest.json','correctness-final-01/completed.json',
                'discovery-03/FAILED.json','discovery-03/diagnostics/diagnostic-aos/preflight.json',
                'resource-selection-03-policy.json','discovery-resource-03/stage.json') or
            name.startswith('correctness-final-01/physics-')):
            path=OUT/'receipts'/name
            path.parent.mkdir(parents=True,exist_ok=True)
            with path.open('xb') as stream:stream.write(archive.extractfile(member).read())
            copied.append(name)
    observations=[]
    for member in members:
        if member.name.startswith('discovery-03/discovery-') and member.name.endswith('/result.json'):
            record=json.load(archive.extractfile(member))
            observations.append(dict(path=member.name,case=record['case'],candidate=member.name.split('/')[2],
                completeTaskMs=record['completeTaskMs'],consumerMs=record['consumerMs'],
                processIdentity=record['processIdentity'],performanceEnvironmentEligible=record['performanceEnvironmentEligible'],
                taskOutputHash=record['taskOutputHash'],executableSha256=record['executableSha256']))
    assert len(observations)==24
    save('discovery-observations.json',dict(status='incomplete-discovery-protocol; individually qualified raw observations only',
        formalComparison=False,uncertainty=None,records=observations))
save('files.json',manifest)
save('summary.json',summary)
save('local-verification.json',dict(verifiedUtc=datetime.now(timezone.utc).isoformat(),
    archive=ARCHIVE.name,sha256=actual,bytes=ARCHIVE.stat().st_size,filesVerified=verified,
    sourceBytes=sum(v['bytes'] for v in manifest['files'].values()),copiedReceipts=copied,
    allEntryHashesVerified=True,root=str(ROOT)))
print(json.dumps(dict(sha256=actual,filesVerified=verified,bytes=ARCHIVE.stat().st_size,
    observations=len(observations),finalApplications=summary['finalCompleteApplications'])))
