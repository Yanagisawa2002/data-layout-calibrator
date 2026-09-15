set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
task_stage="$task_root/evidence-closeout-01"
mkdir "$task_stage"
exec > >(tee "$task_stage/stage.log") 2>&1
trap 'task_code=$?; date -u +%FT%TZ > "$task_stage/ended.txt"; printf "%s\n" "$task_code" > "$task_stage/exit-code.txt"' EXIT
printf '%s\n' "$$" > "$task_stage/shell.pid"
date -u +%FT%TZ > "$task_stage/started.txt"
export PATH="$task_root/env/python/bin:$PATH"
python3 - <<'PY'
from datetime import datetime,timezone
import hashlib,json,os,pathlib,shutil,sys,tarfile
root=pathlib.Path('/root/autodl-tmp/codex-whole-task-20260915/data-layout')
out=root/'evidence-closeout-01'
sys.path.insert(0,str(root/'source-linux-08/Tools/WholeTask'))
from linux_stage import lock_ancestor
lock=lock_ancestor(root.parent/'.hardware.lock')
if shutil.disk_usage(root).free < 12*2**30: raise RuntimeError('Collection budget plus 10 GiB reserve unavailable')
failure=json.load(open(root/'discovery-03/diagnostics/diagnostic-aos/preflight.json'))
physics={p.stem:json.load(open(p)) for p in sorted((root/'correctness-final-01').glob('physics-*.json'))}
correct=json.load(open(root/'correctness-final-01/completed.json'))
window=json.load(open(root/'resource-selection-03.json'))
summary=dict(createdUtc=datetime.now(timezone.utc).isoformat(),performanceConclusion='NO-GO: incomplete resource-qualified discovery in this period; no claim of no algorithm benefit',
    formalCalibrationExecuted=False,independentConfirmationExecuted=False,selectedLayout=None,
    managedAllocationControl=json.load(open(root/'validation-04/allocation-control.json')),
    allocationEligibility='Unknown',finalCorrectnessCases=len(correct['cases']),
    finalCompleteApplications=sum(len(arms) for arms in correct['cases'].values()),
    allFinalCorrectnessApplicationsPerformanceEligible=False,
    longPhysics=physics,sanitizer=json.load(open(root/'sanitizer-03/diagnosis.json')),
    finalWindow=dict(qualified=sum(c['qualified'] for c in window['candidates']),candidates=len(window['candidates']),
        selected=window['selected'],cgroupBackgroundCores=window['cgroupBackgroundCores'],globalReasons=window['globalReasons']),
    failedDiagnosticPreflight=failure,
    qualifiedIndividualDiscoveryProcesses=sum(json.load(open(p))['performanceEnvironmentEligible'] for p in (root/'discovery-03').rglob('result.json')),
    completedDiscovery=False,lock=lock)
(out/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
selected=[]; excluded=[]
for path in sorted(root.rglob('*')):
    rel=path.relative_to(root); parts=rel.parts
    if not path.is_file() or path.is_symlink(): continue
    reason=None
    if parts[0]=='env': reason='isolated-toolchain-or-cache'
    elif parts[0]=='evidence-closeout-01': continue
    elif any(p in ('tmp','__pycache__','obj') for p in parts): reason='build-temporary-or-cache'
    elif path.name=='core' or path.name.startswith('core.') or path.suffix.lower() in ('.dmp','.mdmp'): reason='memory-dump-excluded'
    elif parts[0]=='environment-01' and path.name.endswith(('.tar.gz','.tar.xz','.zip')): reason='downloaded-toolchain-archive'
    elif parts[0].startswith('source-linux-') and path.name!='SOURCE_IDENTITY.json':
        if len(parts)==1: pass
        elif parts[0]=='source-linux-07' and 'SelectorHost' in parts and 'bin' in parts: pass
        else: reason='exact-source-retained-in-original-tar-bundle'
    if reason:
        if not reason.endswith('cache') and reason!='exact-source-retained-in-original-tar-bundle':
            excluded.append(dict(path=rel.as_posix(),reason=reason,bytes=path.stat().st_size))
        continue
    selected.append(path)
manifest=dict(schemaVersion=1,createdUtc=datetime.now(timezone.utc).isoformat(),lock=lock,
    policy='All project results/failures/logs, full state/VTU/CSV bytes, native binaries, source bundles/identities and final selector runtime. Exclude toolchains/caches, duplicate extracted sources and memory dumps. No task measurements omitted for speed.',
    files={p.relative_to(root).as_posix():dict(bytes=p.stat().st_size,sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in selected},
    excluded=excluded)
(out/'files.json').write_text(json.dumps(manifest,indent=2)+'\n')
archive=out/'raw-evidence.tar.gz'
with tarfile.open(archive,'x:gz',compresslevel=6) as target:
    for path in selected: target.add(path,arcname=path.relative_to(root).as_posix(),recursive=False)
    for name in ('files.json','summary.json'): target.add(out/name,arcname='collection/'+name,recursive=False)
with tarfile.open(archive,'r:gz') as check:
    count=0
    for member in check:
        if not member.isfile(): raise RuntimeError('Unexpected nonregular archive member')
        if member.name in manifest['files']:
            actual=hashlib.sha256(check.extractfile(member).read()).hexdigest()
            if actual!=manifest['files'][member.name]['sha256']: raise RuntimeError('Archive content hash mismatch')
            count+=1
    if count!=len(selected): raise RuntimeError('Archive file coverage mismatch')
receipt=dict(archive=archive.name,sha256=hashlib.sha256(archive.read_bytes()).hexdigest(),bytes=archive.stat().st_size,
    files=count,sourceBytes=sum(x['bytes'] for x in manifest['files'].values()),verified=True,
    minimumFreeDiskBytes=shutil.disk_usage(root).free)
(out/'archive.json').write_text(json.dumps(receipt,indent=2)+'\n')
print(json.dumps(dict(archive=receipt,diagnosticFailureAggregate=failure['aggregate'],
    diagnosticFailureIntervals=failure['intervals'],finalApplications=summary['finalCompleteApplications'],
    longPhysicsPassed=all(p['passed'] for p in physics.values()),formalCalibrationExecuted=False),indent=2))
PY
