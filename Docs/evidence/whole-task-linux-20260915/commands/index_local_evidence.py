"""Derive inspectable acceptance metadata from the already verified archive."""
from pathlib import Path
import hashlib,json,shutil,tarfile,xml.etree.ElementTree as ET

root=Path(__file__).resolve().parents[2]
out=root/'Docs/evidence/whole-task-linux-20260915'
cases={}
contracts={}
with tarfile.open(out/'raw-evidence.tar.gz','r:gz') as archive:
    for member in archive:
        if member.name.startswith('native-build-04/') and member.name.endswith('.json') or member.name in (
            'sanitizer-03/diagnosis.json','sanitizer-03/command.json','sanitizer-03/run.log','sanitizer-01/run.log'):
            target=out/'receipts'/member.name
            target.parent.mkdir(parents=True,exist_ok=True)
            with target.open('xb') as stream:stream.write(archive.extractfile(member).read())
        if member.name.startswith('correctness-final-01/') and member.name.endswith('/result.json'):
            value=json.load(archive.extractfile(member))
            assert value['mode']=='correctness' and value['completed'] and not value['performanceEnvironmentEligible']
            caseid=value['case']['id']
            arm=member.name.split('/')[2]
            group=cases.setdefault(caseid,dict(case=value['case'],arms={}))
            group['arms'][arm]={k:value[k] for k in ('datasetHash','fullOutputHash','taskOutputHash','executableSha256','processIdentity')}
for group in cases.values():
    assert len(group['arms'])==8
    for key in ('datasetHash','fullOutputHash','taskOutputHash'):
        assert len({a[key] for a in group['arms'].values()})==1
for path in sorted((out/'receipts/native-build-04').glob('*/*-contract.json')):
    contracts[str(path.relative_to(out/'receipts'))]=json.load(open(path))
trx=next((out/'receipts/validation-04/dotnet-tests').glob('*.trx'))
counters=next(e for e in ET.parse(trx).getroot().iter() if e.tag.endswith('}Counters')).attrib
assert int(counters['total'])==95 and int(counters['passed'])==95
value=dict(finalCorrectnessCases=cases,completeApplications=sum(len(c['arms']) for c in cases.values()),
    completeHashParityPassed=True,allCorrectnessTimingsExcluded=True,storageAndBoundaryContracts=contracts,
    dotnetTestCounters=counters,source='Only final-source raw files; earlier passes do not substitute for current acceptance')
with (out/'acceptance.json').open('x') as stream:json.dump(value,stream,indent=2)
commands=out/'commands'; commands.mkdir()
paths=[p for p in (root/'Artifacts/whole-task').iterdir() if p.suffix in ('.sh','.py')]
for path in paths:shutil.copyfile(path,commands/path.name)
with (commands/'files.json').open('x') as stream:
    json.dump({p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in paths},stream,indent=2)
print(json.dumps(dict(finalApplications=value['completeApplications'],CSharpTests=counters,contracts=len(contracts),retainedCommandFiles=len(paths))))
