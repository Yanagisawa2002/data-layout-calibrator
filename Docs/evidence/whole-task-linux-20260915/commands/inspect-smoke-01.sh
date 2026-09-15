set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
cat "$task_root/smoke-resource-01/stderr.log"
tail -n 12 "$task_root/smoke-resource-01/stdout.log"
find "$task_root/correctness-smoke-01" -name stderr.txt -exec sh -c 'echo "$1"; cat "$1"' sh {} \;
cat "$task_root/validation-02/completed.json"
