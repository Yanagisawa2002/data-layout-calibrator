"""Build all storage arms from identical solver/math source and compiler options."""
import argparse
import json
import os
import platform
import shutil
from pathlib import Path
import subprocess
from prepare_sources import HERE, prepare, digest

NAMES = ["original-aos", "tuned-aos", "field-soa", "field-aosoa8", "llama-soa", "llama-aosoa8"]
VC = Path("C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/VC/Tools/MSVC/14.44.35207")
KIT = Path("C:/Program Files (x86)/Windows Kits/10")


def build(out, arms=None):
    if arms is not None and (not arms or len(set(arms)) != len(arms) or not set(arms) <= set(NAMES)):
        raise ValueError("Invalid explicit native build arms")
    windows = os.name == "nt"
    compiler = VC/"bin/Hostx64/x64/cl.exe" if windows else Path(shutil.which(os.environ.get("CXX", "g++")) or "/missing-compiler")
    if not compiler.is_file():
        raise RuntimeError(f"Compiler missing: {compiler}")
    out.mkdir(parents=True, exist_ok=False)
    adapted = out / "adapted"
    prepare(adapted)
    env = dict(os.environ)
    if windows:
        env["INCLUDE"] = ";".join(map(str, [VC/"include"] + [KIT/"Include/10.0.26100.0"/n for n in ("ucrt", "shared", "um", "winrt")]))
        env["LIB"] = ";".join(map(str, [VC/"lib/x64", KIT/"Lib/10.0.26100.0/ucrt/x64", KIT/"Lib/10.0.26100.0/um/x64"]))
        env["PATH"] = str(VC/"bin/Hostx64/x64") + ";" + env["PATH"]
    else:
        (out/"compiler-version.txt").write_text(subprocess.check_output([str(compiler), "--version"], text=True))
    suffix = ".exe" if windows else ""
    includes = [HERE] + sorted(p for p in (HERE/"Upstream~").glob("*/include"))
    includes += sorted(p for p in adapted.rglob("*") if p.is_dir())
    sources = [HERE/"simpleph_task.cpp"] + sorted(adapted.rglob("*.cpp"))
    results = []
    variants = [(n, k, 0) for k, n in enumerate(NAMES) if arms is None or n in arms]
    if arms is None:
        variants += [("diagnostic-aos", 1, 1), ("diagnostic-original", 0, 1)]
    for name, kind, diag in variants:
        target = out / name
        target.mkdir()
        common = [str(compiler), "/nologo", "/O2", "/fp:strict", "/arch:AVX2", "/std:c++20",
                   "/EHsc", "/MD", "/openmp", "/permissive-", "/Zc:__cplusplus", "/D_USE_MATH_DEFINES",
                   f"/DLAYOUT_KIND={kind}", f"/DTASK_PROFILE={diag}"] if windows else [
                   str(compiler), "-O2", "-std=c++20", "-fopenmp", "-fno-fast-math", "-ffp-contract=off",
                   "-march=x86-64", "-mavx2", "-mtune=generic", f"-DLAYOUT_KIND={kind}", f"-DTASK_PROFILE={diag}"]
        common += [("/I" if windows else "-I")+str(p) for p in includes]
        executable = target/("simpleph"+suffix)
        command = common + (["/Fe:"+str(executable)] if windows else ["-o", str(executable)]) + list(map(str, sources))
        (target/"command.json").write_text(json.dumps(dict(command=command, INCLUDE=env.get("INCLUDE"), LIB=env.get("LIB"),
                                                        compilerSha256=digest(compiler)), indent=2))
        with (target/"build.log").open("w") as log:
            subprocess.run(command, cwd=target, env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
        if not diag:
            contract = target/("storage_contract"+suffix)
            contract_command = common + (["/Fe:"+str(contract)] if windows else ["-o", str(contract)]) + [str(HERE/"storage_contract.cpp")]
            (target/"storage-command.json").write_text(json.dumps(contract_command, indent=2)+"\n")
            with (target/"storage-build.log").open("w") as log:
                subprocess.run(contract_command, cwd=target, env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
            with (target/"storage-contract.json").open("w") as log:
                subprocess.run([str(contract)], cwd=target, stdout=log, stderr=subprocess.STDOUT, check=True)
            grid_contract = target/("cell_grid_contract"+suffix)
            grid_command = common + (["/Fe:"+str(grid_contract)] if windows else ["-o", str(grid_contract)])
            grid_command += [str(HERE/"cell_grid_contract.cpp"), str(adapted/"neighbor/cell_grid.cpp")]
            (target/"cell-grid-command.json").write_text(json.dumps(grid_command, indent=2)+"\n")
            with (target/"cell-grid-build.log").open("w") as log:
                subprocess.run(grid_command, cwd=target, env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
            with (target/"cell-grid-contract.json").open("w") as log:
                subprocess.run([str(grid_contract)], cwd=target, stdout=log, stderr=subprocess.STDOUT, check=True)
        results.append(dict(candidate=name, executable=str(executable), sha256=digest(executable)))
        (out/"build-progress.json").write_text(json.dumps(results, indent=2)+"\n")
    (out/"build-manifest.json").write_text(json.dumps(dict(compiler=str(compiler), compilerSha256=digest(compiler),
        platform=platform.platform(), adaptedManifestSha256=digest(adapted/"adaptation-manifest.json"), variants=results), indent=2)+"\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--arms", nargs="+", choices=NAMES, help="Build only this frozen subset; omit for all arms and diagnostics")
    args = parser.parse_args()
    build(args.output.resolve(), args.arms)
