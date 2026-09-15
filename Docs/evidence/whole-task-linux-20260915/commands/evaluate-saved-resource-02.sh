set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
"$task_root/env/python/bin/python3" - <<'PY'
import json,pathlib,hashlib
root=pathlib.Path('/root/autodl-tmp/codex-whole-task-20260915/data-layout')
source=root/'resource-selection-02.json'; data=json.loads(source.read_text())
rows=[]
for item in data['candidates']:
    cpu=item['physicalCpu']; siblings=[s for s in item['siblings'] if s!=cpu]
    def metrics(c):
        a=data['samples'][0][f'cpu{c}']; b=data['samples'][-1][f'cpu{c}']
        total=sum(b)-sum(a); busy=total-(b[3]+b[4]-a[3]-a[4])
        values=data['perCpuPercent'][str(c)]
        return dict(meanPercent=100*busy/total,maxIntervalPercent=max(values),minimumIntervalPercent=min(values))
    target=metrics(cpu); sibling=[dict(cpu=s,**metrics(s)) for s in siblings]
    partial=target['meanPercent']<=15 and target['maxIntervalPercent']<=25 and all(s['meanPercent']<=10 for s in sibling)
    rows.append(dict(physicalCpu=cpu,strictPrecheckPassed=item['qualified'],originalCpuSmtConditionsPassed=partial,
                     target=target,siblings=sibling))
result=dict(sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(),newResourceObservation=False,
    strictPrecheckQualified=sum(r['strictPrecheckPassed'] for r in rows),candidatePhysicalCores=len(rows),
    originalCpuSmtPartialQualified=sum(r['originalCpuSmtConditionsPassed'] for r in rows),
    originalFullQualification=None,
    note='Diagnostic re-evaluation of the same retained 30-second /proc/stat window only. Original target CPU mean<=15%, interval<=25%, sibling aggregate mean<=10%. That window did not record cgroup background/throttling, so full original-rule qualification is unavailable. The original stricter precheck remains FAILED. No CPU selected and no performance launched from this diagnostic.',
    rows=rows)
path=root/'resource-selection-02-original-rule-diagnostic.json'
if path.exists():raise FileExistsError(path)
path.write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps({k:v for k,v in result.items() if k!='rows'},indent=2))
print(json.dumps({'firstCpuSmtEligibleRows':[r for r in rows if r['originalCpuSmtConditionsPassed']][:5]},indent=2))
PY
