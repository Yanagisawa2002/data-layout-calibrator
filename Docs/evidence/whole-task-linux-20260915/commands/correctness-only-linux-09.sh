set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
task_stage="$task_root/correctness-stage-09"
mkdir "$task_stage"
exec > >(tee "$task_stage/stage.log") 2>&1
trap 'task_code=$?; date -u +%FT%TZ > "$task_stage/ended.txt"; printf "%s\n" "$task_code" > "$task_stage/exit-code.txt"' EXIT
printf '%s\n' "$$" > "$task_stage/shell.pid"
date -u +%FT%TZ > "$task_stage/started.txt"
export DOTNET_ROOT="$task_root/env/dotnet-10.0.401"
export PATH="$task_root/env/python/bin:$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME="$task_root/env/dotnet-home"
export NUGET_PACKAGES="$task_root/env/nuget-packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 MSBUILDDISABLENODEREUSE=1
export OMP_NUM_THREADS=1 OMP_DYNAMIC=FALSE DOTNET_PROCESSOR_COUNT=1
cd "$task_root/source-linux-07"
python3 - <<'PY'
import json,os,pathlib,subprocess,sys,shutil,signal
root=pathlib.Path('/root/autodl-tmp/codex-whole-task-20260915/data-layout')
sys.path.insert(0,'Tools/WholeTask')
from linux_stage import lock_ancestor
from telemetry import CpuTelemetry
from physics_check import check
os.sched_setaffinity(0,{2})
receipt=dict(performanceEligible=False,pid=os.getpid(),affinity=sorted(os.sched_getaffinity(0)),
    lock=lock_ancestor(root.parent/'.hardware.lock'),preflightObservation=CpuTelemetry().preflight(),
    note='Explicit correctness-only authorization. Observed contention does not disqualify numerical equality; every timing is excluded from performance evidence.')
(root/'correctness-stage-09/resource.json').write_text(json.dumps(receipt,indent=2))
if shutil.disk_usage(root).free<12*2**30:raise RuntimeError('2 GiB budget plus 10 GiB reserve unavailable')
out=root/'sanitizer-03';out.mkdir()
command=json.load(open(root/'native-build-04/original-aos/command.json'))['command']
command[command.index('-O2')]='-O1';command[command.index('-o')+1]=str(out/'simpleph-asan')
command+=['-g','-fsanitize=address,undefined','-fno-omit-frame-pointer']
(out/'command.json').write_text(json.dumps(command,indent=2))
with (out/'build.log').open('w') as log:subprocess.run(command,cwd=out,stdout=log,stderr=subprocess.STDOUT,check=True)
with (out/'run.log').open('w') as log:
    process=subprocess.run([str(out/'simpleph-asan'),'13','8','4','0.13','0.25','correctness'],cwd=out,stdout=log,stderr=subprocess.STDOUT)
(out/'diagnosis.json').write_text(json.dumps(dict(exitCode=process.returncode,performanceEligible=False)))
if process.returncode:raise RuntimeError('Final-source sanitizer regression failed')
cases=json.load(open('Tools/WholeTask/cases-smoke.json'))+json.load(open('Tools/WholeTask/cases-discovery.json'))
cases.append(dict(id='physics-long',resolution=24,steps=4000,exportEvery=1000,re=.1,phase=0.))
casefile=root/'correctness-stage-09/cases.json';casefile.write_text(json.dumps(cases,indent=2))
command=[sys.executable,'Tools/WholeTask/campaign.py','--phase','correctness','--build',str(root/'native-build-04'),'--cases',str(casefile),'--output',str(root/'correctness-final-01')]
(root/'correctness-stage-09/campaign-command.json').write_text(json.dumps(command,indent=2))
child=subprocess.Popen(command,start_new_session=True)
(root/'correctness-stage-09/campaign.pid').write_text(str(child.pid))
try:code=child.wait(timeout=1800)
except subprocess.TimeoutExpired:
    os.killpg(child.pid,signal.SIGTERM)
    try:child.wait(timeout=10)
    except subprocess.TimeoutExpired:os.killpg(child.pid,signal.SIGKILL);child.wait()
    raise RuntimeError('Only owned correctness process group terminated on timeout')
if code:raise RuntimeError(f'Final-source complete-output check failed: {code}')
for name in ['original-aos','tuned-aos','field-soa','field-aosoa8','llama-soa','llama-aosoa8','diagnostic-original','diagnostic-aos']:
    value=check(root/'correctness-final-01/physics-long'/name)
    (root/'correctness-final-01'/('physics-'+name+'.json')).write_text(json.dumps(value,indent=2))
    if not value['passed']:raise RuntimeError('Final physical acceptance failed: '+name)
print('Final source: 56 complete application outputs matched; long physics and ASan/UBSan passed. Performance eligibility remains false for this stage.')
PY
