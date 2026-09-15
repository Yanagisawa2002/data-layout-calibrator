set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
"$task_root/env/python/bin/python3" - <<'PY'
import json,pathlib
root=pathlib.Path('/root/autodl-tmp/codex-whole-task-20260915/data-layout')
for rel in ['discovery-resource-03/stderr.log','discovery-resource-03/stdout.log','discovery-stage-10/python-contracts.log']:
    p=root/rel
    print(json.dumps(dict(path=rel,lastLines=p.read_text().splitlines()[-8:])))
records=[]
for p in (root/'discovery-03').rglob('*.json'):
    try: r=json.loads(p.read_text())
    except (ValueError,UnicodeError): continue
    if 'cpuPreflight' in r or 'error' in r:
        records.append(dict(path=str(p.relative_to(root)),error=r.get('error'),mode=r.get('mode'),
            performanceEnvironmentEligible=r.get('performanceEnvironmentEligible'),
            preflightPassed=r.get('cpuPreflight',{}).get('passed'),
            preflightAggregate=r.get('cpuPreflight',{}).get('aggregate'),
            environment=r.get('cpuDuringTask',r.get('cpuTelemetry'))))
print(json.dumps(dict(discoveryRecords=records),indent=2))
sizes=[]
for p in sorted(root.iterdir()):
    if p.is_symlink(): continue
    if p.is_dir():
        files=[f for f in p.rglob('*') if f.is_file() and not f.is_symlink()]
        sizes.append(dict(path=p.name,files=len(files),bytes=sum(f.stat().st_size for f in files)))
    else: sizes.append(dict(path=p.name,files=1,bytes=p.stat().st_size))
print(json.dumps(dict(inventory=sizes),indent=2))
PY
