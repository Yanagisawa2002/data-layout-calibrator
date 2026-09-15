set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
task_stage="$task_root/build-stage-04"
mkdir "$task_stage"
exec > >(tee "$task_stage/stage.log") 2>&1
trap 'task_code=$?; date -u +%FT%TZ > "$task_stage/ended.txt"; printf "%s\n" "$task_code" > "$task_stage/exit-code.txt"' EXIT
printf '%s\n' "$$" > "$task_stage/shell.pid"
date -u +%FT%TZ > "$task_stage/started.txt"
task_free_kib=$(df -Pk "$task_root" | awk 'NR==2 {print $4}')
(( task_free_kib >= 13 * 1024 * 1024 ))
cd "$task_root"
printf '%s\n' '0b5921dd3d64fb0fe928d209cb6f4e87ce3e53c3b34dbaa71e76bba1119603a0  source-linux-04.tar.gz' | sha256sum --check
mkdir source-linux-04
tar -xzf source-linux-04.tar.gz -C source-linux-04
export DOTNET_ROOT="$task_root/env/dotnet-10.0.401"
export PATH="$task_root/env/python/bin:$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME="$task_root/env/dotnet-home"
export NUGET_PACKAGES="$task_root/env/nuget-packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 MSBUILDDISABLENODEREUSE=1
export TMPDIR="$task_stage/tmp"
mkdir "$TMPDIR"
cd source-linux-04
python3 -c 'import json,hashlib,pathlib; m=json.load(open("SOURCE_IDENTITY.json")); assert all(hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()==h for p,h in m["files"].items()); print("Source bundle verified:",len(m["files"]))'
python3 Tools/WholeTask/build_native.py --output "$task_root/native-build-02"
python3 Tools/WholeTask/validate_stage.py --output "$task_root/validation-03"
task_selector=$(find Tools/WholeTask/SelectorHost/bin -name SelectorHost.dll | head -n 1)
dotnet "$task_selector" --allocation-control "$task_root/validation-03/allocation-control.json"
python3 - <<'PY'
import json,pathlib,subprocess
root=pathlib.Path('/root/autodl-tmp/codex-whole-task-20260915/data-layout')
out=root/'sanitizer-02'; out.mkdir()
command=json.load(open(root/'native-build-02/original-aos/command.json'))['command']
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
task_cpu="$task_root/smoke-resource-01/cpu-selection.json"
python3 Tools/WholeTask/linux_stage.py --output "$task_root/smoke-resource-02" --cpu-file "$task_cpu" --growth-gib 1 --timeout 600 -- python3 Tools/WholeTask/campaign.py --phase correctness --build "$task_root/native-build-02" --cases Tools/WholeTask/cases-smoke.json --output "$task_root/correctness-smoke-02"
python3 Tools/WholeTask/linux_stage.py --output "$task_root/discovery-correctness-resource-01" --cpu-file "$task_cpu" --growth-gib 1 --timeout 1200 -- python3 Tools/WholeTask/campaign.py --phase correctness --build "$task_root/native-build-02" --cases Tools/WholeTask/cases-discovery.json --output "$task_root/correctness-discovery-01"
cat > "$task_root/physics-cases-01.json" <<'JSON'
[{"id":"physics-long","resolution":24,"steps":4000,"exportEvery":1000,"re":0.1,"phase":0.0}]
JSON
python3 Tools/WholeTask/linux_stage.py --output "$task_root/physics-resource-01" --cpu-file "$task_cpu" --growth-gib 1 --timeout 1800 -- python3 Tools/WholeTask/campaign.py --phase correctness --build "$task_root/native-build-02" --cases "$task_root/physics-cases-01.json" --output "$task_root/correctness-physics-01"
for task_name in original-aos tuned-aos field-soa field-aosoa8 llama-soa llama-aosoa8 diagnostic-original diagnostic-aos; do
    python3 Tools/WholeTask/physics_check.py --directory "$task_root/correctness-physics-01/physics-long/$task_name" --output "$task_root/correctness-physics-01/physics-$task_name.json"
done
python3 Tools/WholeTask/linux_stage.py --output "$task_root/discovery-resource-01" --cpu-file "$task_cpu" --growth-gib 1 --timeout 1200 -- python3 Tools/WholeTask/campaign.py --phase discovery --build "$task_root/native-build-02" --cases Tools/WholeTask/cases-discovery.json --correctness "$task_root/correctness-discovery-01" --output "$task_root/discovery-01"
python3 Tools/WholeTask/make_protocol.py --discovery "$task_root/discovery-01" --output "$task_root/protocol-01"

