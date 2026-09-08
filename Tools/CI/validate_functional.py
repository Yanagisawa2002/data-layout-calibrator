#!/usr/bin/env python3
"""Explicitly allowlisted CPU correctness/build entry. Never launches Unity or a workload."""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
PROJECT = "Tools/FunctionalTests/FunctionalTests.csproj"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build-only", action="store_true")
    args = parser.parse_args()
    if sys.version_info < (3, 10) or not shutil.which("dotnet"):
        parser.error("Python >=3.10 and .NET SDK >=8 are required; no Unity/Player fallback is permitted.")
    env = dict(os.environ, DLC_FUNCTIONAL_ONLY="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    # No wildcard test collection: historical suites include real timestamp/counter reads.
    commands = [["dotnet", "build", PROJECT, "-c", "Release", "--nologo"]]
    if not args.build_only:
        commands += [[sys.executable, "-m", "unittest", "discover", "-s", "Tools/CI", "-p", "test_functional_policy.py", "-v"],
                     ["dotnet", "test", PROJECT, "-c", "Release", "--no-build", "--nologo"],
                     [sys.executable, "-m", "unittest", "discover", "-s", "Tools/ResultRenderer/tests",
                      "-p", "test_measurement_contract.py", "-v"]]
    for command in commands:
        subprocess.run(command, cwd=ROOT, env=env, check=True)
    print("Build-only checks passed." if args.build_only else "Allowlisted functional checks passed. Performance: Unmeasured.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
