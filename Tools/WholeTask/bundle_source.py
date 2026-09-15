"""Export only this project's source and tests; no credentials, git DB, caches or old artifacts."""
import argparse
import json
from pathlib import Path
import subprocess
import tarfile
from prepare_sources import ROOT, HERE, digest


def bundle(output):
    output.parent.mkdir(parents=True, exist_ok=True)
    if output.exists():
        raise FileExistsError(output)
    prefixes = [ROOT/"Packages/com.yanagisawa.data-layout-calibrator", ROOT/"Tools/WholeTask",
                ROOT/"Tools/FunctionalTests", ROOT/"Tools/CI", ROOT/"Tools/ResultRenderer", ROOT/".github"]
    paths = []
    for prefix in prefixes:
        paths += [p for p in prefix.rglob("*") if p.is_file() and not any(
            x in p.parts for x in ("bin", "obj", "__pycache__", ".pytest_cache"))]
    paths += [p for p in (ROOT/"README.md", ROOT/"LICENSE", ROOT/"THIRD_PARTY_NOTICES.md") if p.is_file()]
    manifest = dict(baseHead=subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                    worktreeStatus=subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True),
                    files={p.relative_to(ROOT).as_posix(): digest(p) for p in sorted(set(paths))})
    identity = output.with_suffix(".manifest.json")
    identity.write_text(json.dumps(manifest, indent=2)+"\n", encoding="utf-8")
    with tarfile.open(output, "w:gz") as archive:
        for p in sorted(set(paths)):
            archive.add(p, arcname=p.relative_to(ROOT).as_posix(), recursive=False)
        archive.add(identity, arcname="SOURCE_IDENTITY.json", recursive=False)
    print(json.dumps(dict(bundle=str(output), sha256=digest(output), bytes=output.stat().st_size,
                          sourceFiles=len(manifest["files"])), indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    bundle(parser.parse_args().output.resolve())
