set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
"$task_root/env/python/bin/python3" - <<'PY'
import json,pathlib,datetime
root=pathlib.Path('/root/autodl-tmp/codex-whole-task-20260915/data-layout')
answer={'observedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat()}
for name in ['build-stage-05/started.txt','build-stage-05/shell.pid','build-stage-05/exit-code.txt']:
    path=root/name
    if path.exists(): answer[name]=path.read_text().strip()
path=root/'native-build-03/build-progress.json'
if path.exists(): answer['compiledVariants']=[x['candidate'] for x in json.loads(path.read_text())]
for name in ['validation-03/completed.json','validation-03/allocation-control.json','sanitizer-02/diagnosis.json','correctness-smoke-02/completed.json','correctness-discovery-01/completed.json','correctness-physics-01/completed.json','discovery-01/summary.json','protocol-01/budget.json']:
    path=root/name
    if path.exists():
        data=json.loads(path.read_text())
        answer[name]=data if 'budget' in name or 'allocation-control' in name or 'diagnosis' in name else {'completed':data.get('completed',data.get('status'))}
for name in ['smoke-resource-02','discovery-correctness-resource-01','physics-resource-01','discovery-resource-01']:
    path=root/name/'stage.json'
    if path.exists():
        data=json.loads(path.read_text()); answer[name]={k:data.get(k) for k in ['status','pid','childPid','exitCode','error']}
        if data['status']=='failed':
            p=root/name/'stderr.log'
            if p.exists(): answer[name]['stderr']=p.read_text()[-5000:]
answer['ownedKinds']=[]
for p in pathlib.Path('/proc').iterdir():
    if p.name.isdigit():
        try:
            comm=(p/'comm').read_text().strip()
            if comm in ['flock','g++','cc1plus','simpleph','simpleph-asan','dotnet']:
                answer['ownedKinds'].append({'pid':int(p.name),'comm':comm})
        except (FileNotFoundError,PermissionError): pass
print(json.dumps(answer,indent=2))
PY
