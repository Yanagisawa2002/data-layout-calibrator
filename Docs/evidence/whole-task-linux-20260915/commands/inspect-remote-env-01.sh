set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
date -u +%FT%TZ
cat "$task_root/environment-01/started.txt" "$task_root/environment-01/shell.pid"
ps -eo pid,ppid,comm | awk '$3 == "curl" || $3 == "bash" || $3 == "flock"'
ls -l /usr/bin/python* /opt/conda/bin/python* /root/miniconda3/bin/python* 2>/dev/null || true
ls -l "$task_root/environment-01"
curl --silent --show-error --head --connect-timeout 5 --max-time 10 'https://www.python.org/ftp/python/3.12.11/Python-3.12.11.tgz' || true
curl --silent --show-error --head --connect-timeout 5 --max-time 10 'https://dotnetcli.blob.core.windows.net/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-linux-x64.tar.gz' || true
