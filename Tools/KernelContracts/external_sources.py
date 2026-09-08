"""Verify pinned upstream bytes; optionally compile objects. Never link or run workloads."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = ROOT / "Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Upstream~"


def verify():
    lock = json.loads((UPSTREAM / "upstream-lock.json").read_text(encoding="utf-8"))
    count = 0
    for source in lock["sources"]:
        for item in source["files"]:
            path = UPSTREAM / source["name"] / item["path"]
            if not path.resolve().is_relative_to(UPSTREAM.resolve()):
                raise ValueError("Source path escapes pinned directory")
            if hashlib.sha256(path.read_bytes()).hexdigest() != item["sha256"]:
                raise ValueError("Upstream content changed: " + str(path))
            count += 1
    print(f"PASS: {count} pinned upstream files. No upstream entrypoint executed.")
    fixture_root = UPSTREAM.parent / "Fixtures~"
    fixture = json.loads((fixture_root / "fixture-lock.json").read_text(encoding="utf-8"))
    for path, expected in [(fixture_root / "llama-msvc-prefix17.json", fixture["fixtureSha256"]),
                           (ROOT / fixture["preparerSource"], fixture["preparerSourceSha256"])]:
        if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise ValueError("Input fixture/preparer identity mismatch: " + str(path))
    print("PASS: native RNG prefix fixture and its input-only preparer source hashes.")


def compile_objects(family, compiler):
    output = ROOT / "Tools/KernelContracts/Artifacts/ExternalObjects" / family
    output.mkdir(parents=True, exist_ok=True)
    msvc = Path(compiler).name.lower() in ("cl", "cl.exe")
    if msvc and family != "llama-code-comp":
        raise ValueError("Unmodified sources require a POSIX/OpenMP compiler. MSVC is supported here only for LLAMA code_comp; no timing/platform shims are substituted.")
    flags = []
    if family == "llama-code-comp":
        folder = UPSTREAM / "llama/examples/nbody_code_comp"
        sources = [folder / f"nbody-{layout}.cpp" for layout in ("AoS-baseline", "SoA", "AoSoA")]
        flags = ["/std:c++20", "/EHsc"] if msvc else ["-std=c++20"]
    elif family == "babel-omp":
        folder = UPSTREAM / "BabelStream/src"
        sources = [folder / "main.cpp", folder / "omp/OMPStream.cpp"]
        flags = ["-std=c++17", "-fopenmp", "-DOMP", "-I" + str(folder), "-I" + str(folder / "omp")]
    elif family == "stream":
        sources = [UPSTREAM / "STREAM/stream.c"]
        flags = ["-x", "c", "-std=c11", "-fopenmp"]
    else:
        sources = [UPSTREAM / "HeCBench/src/stencil3d-omp/main.cpp"]
        flags = ["-std=c++17", "-fopenmp"]
    for source in sources:
        obj = output / (source.stem + (".obj" if msvc else ".o"))
        command = ([compiler, "/nologo", "/c"] + flags + [str(source), "/Fo" + str(obj)] if msvc
                   else [compiler, "-c"] + flags + [str(source), "-o", str(obj)])
        subprocess.run(command, check=True)
    print(f"PASS: {len(sources)} translation units compiled to objects only. No executable linked or run. Performance Unmeasured.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compile-objects", choices=["llama-code-comp", "babel-omp", "stream", "hec-stencil3d"])
    parser.add_argument("--compiler", help="Compiler executable; MSVC callers must supply the normal INCLUDE environment.")
    parser.add_argument("--run", action="store_true", help="Always refused; requires new explicit user authorization and a reviewed runner change.")
    args = parser.parse_args()
    if args.run:
        parser.error("Performance execution is disabled; new explicit user authorization and a reviewed runner change are required.")
    verify()
    if args.compile_objects:
        if not args.compiler:
            parser.error("--compile-objects requires --compiler")
        compile_objects(args.compile_objects, args.compiler)


if __name__ == "__main__":
    main()
