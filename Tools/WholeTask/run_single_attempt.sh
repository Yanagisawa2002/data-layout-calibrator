#!/usr/bin/env bash
# Foreground only, under the coordinator's actual flock and <=60-minute timeout.
# TASK_ROOT is a fresh task-owned directory containing source/ and env/dotnet/.
set -euo pipefail
: "${TASK_ROOT:?Set the task-owned root}"
export DOTNET_ROOT="$TASK_ROOT/env/dotnet"
export DOTNET_CLI_HOME="$TASK_ROOT/env/cli-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export MSBUILDDISABLENODEREUSE=1 DOTNET_PROCESSOR_COUNT=4
export OMP_NUM_THREADS=1 OMP_DYNAMIC=FALSE OPENBLAS_NUM_THREADS=1
export PATH="$DOTNET_ROOT:/root/miniconda3/bin:$PATH"
cd "$TASK_ROOT/source"
mkdir "$TASK_ROOT/validation"
trap 'code=$?; printf "%s\n" "$code" > "$TASK_ROOT/validation/exit-code.txt"; date -u +%FT%TZ > "$TASK_ROOT/validation/ended.txt"' EXIT
date -u +%FT%TZ > "$TASK_ROOT/validation/started.txt"
python -m unittest discover -s Tools/WholeTask -p 'test_*.py' -v > "$TASK_ROOT/validation/python-tests.log" 2>&1
dotnet build Tools/WholeTask/SelectorHost/SelectorHost.csproj -c Release --nologo --disable-build-servers -m:4 > "$TASK_ROOT/validation/selector-build.log" 2>&1
selector="$TASK_ROOT/source/Tools/WholeTask/SelectorHost/bin/Release/net10.0/SelectorHost.dll"
dotnet "$selector" --allocation-control "$TASK_ROOT/validation/allocation-control.json"
python - "$TASK_ROOT/validation/allocation-control.json" <<'PY'
import json, sys
r = json.load(open(sys.argv[1]))
c = r['actualDotNetCurrentThreadControl']
assert r['controlFailure'] is None and c['PositiveControlPassed'] and c['EmptyControlPassed']
assert c['ObservedPositiveBytes'] >= 1048576
PY
python -u Tools/WholeTask/build_native.py --output "$TASK_ROOT/build" --arms tuned-aos field-aosoa8 > "$TASK_ROOT/validation/native-build.log" 2>&1
python -u Tools/WholeTask/linux_stage.py --output "$TASK_ROOT/stage" --timeout 2700 --growth-gib 2 -- \
    python -u Tools/WholeTask/single_attempt.py --source "$TASK_ROOT/source" --build "$TASK_ROOT/build" \
    --selector "$selector" --output "$TASK_ROOT/result" \
    --protocol "$TASK_ROOT/source/Tools/WholeTask/single-attempt-protocol.json"
