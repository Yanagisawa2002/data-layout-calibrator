set -euo pipefail
task_pid=2424
task_parent=2410
if [[ "$(ps -p "$task_pid" -o ppid= | tr -d ' ')" == "$task_parent" && "$(ps -p "$task_pid" -o comm= | tr -d ' ')" == curl ]]; then
    kill -TERM "$task_pid"
    echo 'Stopped only environment-01 owned stalled curl; its parent trap retains failure evidence.'
else
    echo 'Owned download identity no longer matches; no signal sent.'
fi
