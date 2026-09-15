set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
cat "$task_root/correctness-physics-01/physics-original-aos.json"
cat "$task_root/discovery-01/discovery-medium/tuned-aos/preflight.json"
tail -n 4 "$task_root/validation-03/step-01.log"
cat "$task_root/build-stage-05/ended.txt"
