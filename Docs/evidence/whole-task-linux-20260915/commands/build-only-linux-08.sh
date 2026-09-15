set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
task_stage="$task_root/build-stage-08"
mkdir "$task_stage"
exec > >(tee "$task_stage/stage.log") 2>&1
trap 'task_code=$?; date -u +%FT%TZ > "$task_stage/ended.txt"; printf "%s\n" "$task_code" > "$task_stage/exit-code.txt"' EXIT
printf '%s\n' "$$" > "$task_stage/shell.pid"
date -u +%FT%TZ > "$task_stage/started.txt"
task_free_kib=$(df -Pk "$task_root" | awk 'NR==2 {print $4}')
(( task_free_kib >= 13 * 1024 * 1024 ))
export DOTNET_ROOT="$task_root/env/dotnet-10.0.401"
export PATH="$task_root/env/python/bin:$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME="$task_root/env/dotnet-home"
export NUGET_PACKAGES="$task_root/env/nuget-packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 MSBUILDDISABLENODEREUSE=1
export TMPDIR="$task_stage/tmp"
mkdir "$TMPDIR"
cd "$task_root/source-linux-07"
python3 Tools/WholeTask/build_native.py --output "$task_root/native-build-04"
python3 Tools/WholeTask/validate_stage.py --output "$task_root/validation-04"
task_selector=$(find Tools/WholeTask/SelectorHost/bin -name SelectorHost.dll | head -n 1)
dotnet "$task_selector" --allocation-control "$task_root/validation-04/allocation-control.json"
echo 'Build and deterministic contracts only; no SimplePH application or timing campaign launched.'
