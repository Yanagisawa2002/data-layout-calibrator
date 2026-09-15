set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
task_stage="$task_root/discovery-stage-10"
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
export OMP_NUM_THREADS=1 OMP_DYNAMIC=FALSE DOTNET_PROCESSOR_COUNT=1 OPENBLAS_NUM_THREADS=1
ulimit -c 0
cd "$task_root"
printf '%s\n' 'db8a92ea04ec283b2e05e0bebd5d60a004293192ae768b1ad6037632839571c7  source-linux-08.tar.gz' | sha256sum --check
mkdir source-linux-08
tar -xzf source-linux-08.tar.gz -C source-linux-08
cd source-linux-08
python3 - <<'PY'
import hashlib,json,pathlib,sys
root=pathlib.Path('/root/autodl-tmp/codex-whole-task-20260915/data-layout')
sys.path.insert(0,'Tools/WholeTask')
from linux_stage import lock_ancestor
lock=lock_ancestor(root.parent/'.hardware.lock')
a=json.load(open(root/'source-linux-07/SOURCE_IDENTITY.json'))
b=json.load(open('SOURCE_IDENTITY.json'))
assert all(hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()==h for p,h in b['files'].items())
changed=[p for p in sorted(set(a['files'])|set(b['files'])) if a['files'].get(p)!=b['files'].get(p)]
assert changed==['Tools/WholeTask/README.md','Tools/WholeTask/campaign.py','Tools/WholeTask/test_contract.py'],changed
assert all(hashlib.sha256((root/'source-linux-07'/p).read_bytes()).hexdigest()==h for p,h in a['files'].items())
assert json.load(open(root/'correctness-final-01/completed.json'))['completed'] is True
receipt=dict(lock=lock,sourceFilesVerified=len(b['files']),changedFromValidatedSource=changed,
    nativeBuild=str(root/'native-build-04'),nativeCodeIdentical=True,selectorCodeIdentical=True,
    workflowAndPhysicsIdentical=True,correctness=str(root/'correctness-final-01'),
    rationale='Only performance qualification guard, its negative test, and README changed. Reuse exact validated native binaries and exact correctness receipts; this controller now rejects any during-task resource failure in discovery.')
(root/'discovery-stage-10/source-reuse.json').write_text(json.dumps(receipt,indent=2))
print(json.dumps(receipt))
PY
python3 -m unittest discover -s Tools/WholeTask -p test_contract.py -v > "$task_stage/python-contracts.log" 2>&1
python3 "$task_root/select-resource-03.py"
python3 Tools/WholeTask/linux_stage.py --output "$task_root/discovery-resource-03" --cpu-file "$task_root/cpu-selection-03.json" --growth-gib 2 --timeout 1200 -- python3 Tools/WholeTask/campaign.py --phase discovery --build "$task_root/native-build-04" --cases Tools/WholeTask/cases-discovery.json --correctness "$task_root/correctness-final-01" --output "$task_root/discovery-03"
python3 Tools/WholeTask/make_protocol.py --discovery "$task_root/discovery-03" --output "$task_root/protocol-03"
