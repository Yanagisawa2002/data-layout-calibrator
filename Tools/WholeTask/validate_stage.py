"""Explicit bounded compilation and functional verification; requires queue guard."""
import argparse
import json
import os
import shutil
from pathlib import Path
import subprocess
import sys
from prepare_sources import ROOT

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--include-renderer", action="store_true", help="Requires Pillow and the unchanged historical renderer fixture")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    dotnet = shutil.which("dotnet")
    if not dotnet:
        raise RuntimeError("dotnet is not available in this isolated environment")
    commands = [
        [dotnet, "build", "Tools/WholeTask/SelectorHost/SelectorHost.csproj", "-c", "Release", "--nologo", "--disable-build-servers"],
        [dotnet, "test", "Tools/FunctionalTests/FunctionalTests.csproj", "-c", "Release", "--nologo", "--disable-build-servers",
         "--results-directory", str(output/"dotnet-tests"), "--logger", "trx"],
        [sys.executable, "-m", "unittest", "discover", "-s", "Tools/WholeTask", "-p", "test_contract.py", "-v"],
        [sys.executable, "-m", "unittest", "discover", "-s", "Tools/CI", "-p", "test_functional_policy.py", "-v"],
    ]
    if args.include_renderer:
        commands.append([sys.executable, "-m", "unittest", "discover", "-s", "Tools/ResultRenderer/tests", "-p", "test_measurement_contract.py", "-v"])
    results = []
    env = dict(os.environ, MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    for i, command in enumerate(commands):
        with (output/f"step-{i:02d}.log").open("w", encoding="utf-8") as log:
            process = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, env=env)
        results.append(dict(command=command, exitCode=process.returncode))
        (output/"commands.json").write_text(json.dumps(results, indent=2)+"\n")
        if process.returncode:
            raise RuntimeError(f"Validation failed: {command}")
    (output/"completed.json").write_text(json.dumps(dict(completed=True, commands=results), indent=2)+"\n")
