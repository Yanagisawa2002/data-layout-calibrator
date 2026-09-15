set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
task_stage="$task_root/sanitizer-01"
mkdir "$task_stage"
exec > >(tee "$task_stage/stage.log") 2>&1
trap 'task_code=$?; date -u +%FT%TZ > "$task_stage/ended.txt"; printf "%s\n" "$task_code" > "$task_stage/exit-code.txt"' EXIT
printf '%s\n' "$$" > "$task_stage/shell.pid"
date -u +%FT%TZ > "$task_stage/started.txt"
task_free_kib=$(df -Pk "$task_root" | awk 'NR==2 {print $4}')
(( task_free_kib >= 11 * 1024 * 1024 ))
"$task_root/env/python/bin/python3" - <<'PY'
import json,pathlib,subprocess
root=pathlib.Path('/root/autodl-tmp/codex-whole-task-20260915/data-layout')
out=root/'sanitizer-01'
command=json.load(open(root/'native-build-01/original-aos/command.json'))['command']
command[command.index('-O2')]='-O1'
command[command.index('-o')+1]=str(out/'simpleph-asan')
command += ['-g','-fsanitize=address,undefined','-fno-omit-frame-pointer']
(out/'command.json').write_text(json.dumps(command,indent=2))
with (out/'build.log').open('w') as log:
    subprocess.run(command,cwd=out,stdout=log,stderr=subprocess.STDOUT,check=True)
with (out/'run.log').open('w') as log:
    result=subprocess.run([str(out/'simpleph-asan'),'13','8','4','0.13','0.25','correctness'],cwd=out,stdout=log,stderr=subprocess.STDOUT)
(out/'diagnosis.json').write_text(json.dumps({'exitCode':result.returncode,'performanceEligible':False}))
print((out/'run.log').read_text()[:16000])
PY
