set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
cat "$task_root/build-stage-02/started.txt" "$task_root/build-stage-02/ended.txt" "$task_root/build-stage-02/exit-code.txt"
cat "$task_root/validation-01/commands.json"
tail -n 100 "$task_root/validation-01/step-04.log"
tail -n 15 "$task_root/validation-01/step-01.log"
cat "$task_root/native-build-01/build-manifest.json"
for task_result in "$task_root"/native-build-01/*/storage-contract.json; do cat "$task_result"; done
cat "$task_root/environment-01/started.txt" "$task_root/environment-01/ended.txt" "$task_root/environment-01/exit-code.txt"
cat "$task_root/environment-01/lscpu.txt"
