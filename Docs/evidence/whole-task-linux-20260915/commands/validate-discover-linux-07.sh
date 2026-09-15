set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
task_stage="$task_root/build-stage-07"
mkdir "$task_stage"
exec > >(tee "$task_stage/stage.log") 2>&1
trap 'task_code=$?; date -u +%FT%TZ > "$task_stage/ended.txt"; printf "%s\n" "$task_code" > "$task_stage/exit-code.txt"' EXIT
printf '%s\n' "$$" > "$task_stage/shell.pid"
date -u +%FT%TZ > "$task_stage/started.txt"
task_free_kib=$(df -Pk "$task_root" | awk 'NR==2 {print $4}')
(( task_free_kib >= 13 * 1024 * 1024 ))
cd "$task_root"
printf '%s\n' '1dd9efee820299f2a04dab7cb1ff678fc9baf560d1111478a4c04997b8014221  source-linux-07.tar.gz' | sha256sum --check
mkdir source-linux-07
tar -xzf source-linux-07.tar.gz -C source-linux-07
export DOTNET_ROOT="$task_root/env/dotnet-10.0.401"
export PATH="$task_root/env/python/bin:$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME="$task_root/env/dotnet-home"
export NUGET_PACKAGES="$task_root/env/nuget-packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 MSBUILDDISABLENODEREUSE=1
export TMPDIR="$task_stage/tmp"
mkdir "$TMPDIR"
cd source-linux-07
python3 -c 'import json,hashlib,pathlib; m=json.load(open("SOURCE_IDENTITY.json")); assert all(hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()==h for p,h in m["files"].items()); print("Source bundle verified:",len(m["files"]))'
python3 - <<'PY_RESOURCE'
import json,os,pathlib,time
root=pathlib.Path('/root/autodl-tmp/codex-whole-task-20260915/data-layout')
def snapshot():
    return {p[0]:list(map(int,p[1:9])) for line in pathlib.Path('/proc/stat').read_text().splitlines() if (p:=line.split()) and p[0].startswith('cpu')}
samples=[snapshot()]
for _ in range(30):
    time.sleep(1);samples.append(snapshot())
allowed=sorted(os.sched_getaffinity(0)); loads={}
for cpu in allowed:
    values=[]
    for a,b in zip(samples,samples[1:]):
        before,after=a[f'cpu{cpu}'],b[f'cpu{cpu}'];total=sum(after)-sum(before)
        values.append(100*(total-(after[3]+after[4]-before[3]-before[4]))/total if total else 100)
    loads[cpu]=values
selected=None; candidates=[]
for cpu in allowed:
    if cpu<=2:continue
    siblings=[int(x) for x in pathlib.Path(f'/sys/devices/system/cpu/cpu{cpu}/topology/thread_siblings_list').read_text().strip().split(',')]
    if cpu!=min(siblings):continue
    violations=[dict(logicalCpu=s,interval=i,percent=x) for s in siblings for i,x in enumerate(loads[s]) if x>10]
    candidates.append(dict(physicalCpu=cpu,siblings=siblings,qualified=not violations,violations=violations))
    if selected is None and not violations:
        selected=dict(cpu=cpu,siblings=siblings)
receipt=dict(selected=selected,candidates=candidates,perCpuPercent=loads,samples=samples,excludedPhysicalCpus=[0,1,2],
    rule='First allowed physical core >=3 with both SMT siblings <=10 percent in every one-second interval for thirty seconds. CPU2 excluded solely due to discovery-01 observed SMT interference; CPU0/1 avoided as housekeeping. No application timings enter this choice; no candidate reselection within the batch.')
(root/'resource-selection-02.json').write_text(json.dumps(receipt,indent=2))
if not selected:raise RuntimeError('No stable quiet physical core; hardware qualification unavailable')
selected['resourceSelectionEvidence']=str(root/'resource-selection-02.json')
(root/'cpu-selection-02.json').write_text(json.dumps(selected,indent=2))
print(json.dumps(selected))

PY_RESOURCE
python3 Tools/WholeTask/build_native.py --output "$task_root/native-build-04"
python3 Tools/WholeTask/validate_stage.py --output "$task_root/validation-04"
task_selector=$(find Tools/WholeTask/SelectorHost/bin -name SelectorHost.dll | head -n 1)
dotnet "$task_selector" --allocation-control "$task_root/validation-04/allocation-control.json"
python3 - <<'PY'
import json,pathlib,subprocess
root=pathlib.Path('/root/autodl-tmp/codex-whole-task-20260915/data-layout')
out=root/'sanitizer-03'; out.mkdir()
command=json.load(open(root/'native-build-04/original-aos/command.json'))['command']
command[command.index('-O2')]='-O1'; command[command.index('-o')+1]=str(out/'simpleph-asan')
command += ['-g','-fsanitize=address,undefined','-fno-omit-frame-pointer']
(out/'command.json').write_text(json.dumps(command,indent=2))
with (out/'build.log').open('w') as log:
    subprocess.run(command,cwd=out,stdout=log,stderr=subprocess.STDOUT,check=True)
with (out/'run.log').open('w') as log:
    result=subprocess.run([str(out/'simpleph-asan'),'13','8','4','0.13','0.25','correctness'],cwd=out,stdout=log,stderr=subprocess.STDOUT)
(out/'diagnosis.json').write_text(json.dumps({'exitCode':result.returncode,'performanceEligible':False}))
if result.returncode: raise RuntimeError('Sanitizer regression failed; retain all evidence')
print('ASan/UBSan regression completed without a report')
PY
task_cpu="$task_root/cpu-selection-02.json"
python3 Tools/WholeTask/linux_stage.py --output "$task_root/smoke-resource-03" --cpu-file "$task_cpu" --growth-gib 1 --timeout 600 -- python3 Tools/WholeTask/campaign.py --phase correctness --build "$task_root/native-build-04" --cases Tools/WholeTask/cases-smoke.json --output "$task_root/correctness-smoke-03"
python3 Tools/WholeTask/linux_stage.py --output "$task_root/discovery-correctness-resource-02" --cpu-file "$task_cpu" --growth-gib 1 --timeout 1200 -- python3 Tools/WholeTask/campaign.py --phase correctness --build "$task_root/native-build-04" --cases Tools/WholeTask/cases-discovery.json --output "$task_root/correctness-discovery-02"
cat > "$task_root/physics-cases-02.json" <<'JSON'
[{"id":"physics-long","resolution":24,"steps":4000,"exportEvery":1000,"re":0.1,"phase":0.0}]
JSON
python3 Tools/WholeTask/linux_stage.py --output "$task_root/physics-resource-02" --cpu-file "$task_cpu" --growth-gib 1 --timeout 1800 -- python3 Tools/WholeTask/campaign.py --phase correctness --build "$task_root/native-build-04" --cases "$task_root/physics-cases-02.json" --output "$task_root/correctness-physics-02"
for task_name in original-aos tuned-aos field-soa field-aosoa8 llama-soa llama-aosoa8 diagnostic-original diagnostic-aos; do
    python3 Tools/WholeTask/physics_check.py --directory "$task_root/correctness-physics-02/physics-long/$task_name" --output "$task_root/correctness-physics-02/physics-$task_name.json"
done
python3 Tools/WholeTask/linux_stage.py --output "$task_root/discovery-resource-02" --cpu-file "$task_cpu" --growth-gib 1 --timeout 1200 -- python3 Tools/WholeTask/campaign.py --phase discovery --build "$task_root/native-build-04" --cases Tools/WholeTask/cases-discovery.json --correctness "$task_root/correctness-discovery-02" --output "$task_root/discovery-02"
python3 Tools/WholeTask/make_protocol.py --discovery "$task_root/discovery-02" --output "$task_root/protocol-02"




