#!/usr/bin/env bash
# Run only through the coordinator's foreground SSH helper with --lock.
set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
task_stage="$task_root/environment-01"
mkdir "$task_stage"
exec > >(tee "$task_stage/setup.log") 2>&1
trap 'task_code=$?; date -u +%FT%TZ > "$task_stage/ended.txt"; printf "%s\n" "$task_code" > "$task_stage/exit-code.txt"' EXIT
printf '%s\n' "$$" > "$task_stage/shell.pid"
date -u +%FT%TZ > "$task_stage/started.txt"
task_free_kib=$(df -Pk "$task_root" | awk 'NR==2 {print $4}')
if (( task_free_kib < 13 * 1024 * 1024 )); then
    echo 'Need 3 GiB setup budget plus 10 GiB untouched reserve'; exit 1
fi
df -Pk "$task_root" /
cat /sys/fs/cgroup/cpu.max /sys/fs/cgroup/cpuset.cpus.effective /sys/fs/cgroup/memory.max
uname -a
lscpu > "$task_stage/lscpu.txt"
mkdir "$task_root/env"
mkdir "$task_root/env/dotnet-10.0.401"
cd "$task_stage"
curl --fail --location --retry 2 --max-time 900 --output python.tar.gz 'https://github.com/astral-sh/python-build-standalone/releases/download/20250818/cpython-3.12.11%2B20250818-x86_64-unknown-linux-gnu-install_only_stripped.tar.gz'
printf '%s\n' 'b5a4f189f25cbacba0f76c9bd6f3ea8c35d2064068aa74ccbb6863068caababd  python.tar.gz' | sha256sum --check
tar -xzf python.tar.gz -C "$task_root/env"
curl --fail --location --retry 2 --max-time 900 --output dotnet.tar.gz 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-linux-x64.tar.gz'
printf '%s\n' '51c8b999af9e8dd9998c9edc5944e19a90788862068acd38694e098889054ce8c23d4f0c5cccfa16bf187d044562359e5ee69a9f8ad0bbe913ba90311fbce25b  dotnet.tar.gz' | sha512sum --check
tar -xzf dotnet.tar.gz -C "$task_root/env/dotnet-10.0.401"
export DOTNET_ROOT="$task_root/env/dotnet-10.0.401"
export DOTNET_CLI_HOME="$task_root/env/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
"$task_root/env/python/bin/python3" --version
"$DOTNET_ROOT/dotnet" --info
du -sk "$task_root/env" "$task_stage"
df -Pk "$task_root"
