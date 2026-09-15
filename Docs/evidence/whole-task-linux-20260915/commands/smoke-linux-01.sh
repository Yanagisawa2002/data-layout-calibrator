set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
task_stage="$task_root/smoke-stage-01"
mkdir "$task_stage"
exec > >(tee "$task_stage/stage.log") 2>&1
trap 'task_code=$?; date -u +%FT%TZ > "$task_stage/ended.txt"; printf "%s\n" "$task_code" > "$task_stage/exit-code.txt"' EXIT
printf '%s\n' "$$" > "$task_stage/shell.pid"
date -u +%FT%TZ > "$task_stage/started.txt"
task_free_kib=$(df -Pk "$task_root" | awk 'NR==2 {print $4}')
(( task_free_kib >= 12 * 1024 * 1024 ))
cd "$task_root"
printf '%s\n' '298442d2e5f10ddca58c18a29395f3c6b17b7b8dbc7b664b26bbd217a5b1e8cd  source-linux-02.tar.gz' | sha256sum --check
mkdir source-linux-02
tar -xzf source-linux-02.tar.gz -C source-linux-02
export DOTNET_ROOT="$task_root/env/dotnet-10.0.401"
export PATH="$task_root/env/python/bin:$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME="$task_root/env/dotnet-home"
export NUGET_PACKAGES="$task_root/env/nuget-packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 MSBUILDDISABLENODEREUSE=1
export TMPDIR="$task_stage/tmp"
mkdir "$TMPDIR"
cd source-linux-02
python3 - <<'PY'
import hashlib,json,pathlib
old=json.load(open('../source-linux-01/SOURCE_IDENTITY.json'))['files']
new=json.load(open('SOURCE_IDENTITY.json'))['files']
assert all(hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()==h for p,h in new.items())
native=[p for p in old if p.startswith('Tools/WholeTask/Upstream~/') or p.endswith(('.cpp','.hpp')) or p in ['Tools/WholeTask/prepare_sources.py','Tools/WholeTask/build_native.py','Tools/WholeTask/upstream-lock.json']]
assert all(old[p]==new[p] for p in native)
pathlib.Path('../smoke-stage-01/build-reuse.json').write_text(json.dumps({'passed':True,'unchangedNativeFiles':len(native),'build':'native-build-01','sourceBundle':'source-linux-02'},indent=2))
print('Current native source identical to built source:',len(native))
PY
python3 Tools/WholeTask/validate_stage.py --output "$task_root/validation-02"
python3 Tools/WholeTask/linux_stage.py --output "$task_root/smoke-resource-01" --growth-gib 1 --timeout 600 -- python3 Tools/WholeTask/campaign.py --phase correctness --build "$task_root/native-build-01" --cases Tools/WholeTask/cases-smoke.json --output "$task_root/correctness-smoke-01"
