"""Pinned, source-only application/library preparation; never launches a workload.

Downloaded files stay byte-identical in Upstream~. Mechanical adaptations go to a
fresh artifact directory, with a complete patch and hash manifest for review.
"""
import argparse
import difflib
import hashlib
import json
from pathlib import Path
import re
import subprocess
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
SOURCES = {
    "SimplePH": ("rostanda/SimplePH", "b4d7512ed06873ac188b20ebf1639f1c4cf83afc"),
    "llama": ("alpaka-group/llama", "086e66e7565f677d6b3aff88542e28c7dd6d8228"),
    "mp11": ("boostorg/mp11", "1caff7ffa929662f57fac1dbea24c3d2d95205f6"),
    "container_hash": ("boostorg/container_hash", "89e5b98f6bc05841a21069d76cc5adcbee62b9cc"),
    "describe": ("boostorg/describe", "3da6a1e295689612a5ba53e906ad036f84729ede"),
    "config": ("boostorg/config", "5e98c6ddd4d58311b52e8f0fdde38c80c04f70ae"),
    "assert": ("boostorg/assert", "2242cfb58269c03328957860006076386e7fa356"),
}


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def fetch():
    records = []
    for name, (repo, commit) in SOURCES.items():
        tree = json.loads(subprocess.check_output(
            ["gh", "api", f"repos/{repo}/git/trees/{commit}?recursive=1"], text=True))
        for item in tree["tree"]:
            file = item["path"]
            take = (file.startswith("src/") or file.startswith("python/run_") or
                    file.startswith("python/plot_") or file.startswith("tests/test_") or
                    file == "README.md") if name == "SimplePH" else file.startswith("include/")
            if item["type"] != "blob" or not (take or file in ("LICENSE", "LICENSE.md", "LICENSE_1_0.txt")):
                continue
            path = HERE / "Upstream~" / name / file
            url = f"https://raw.githubusercontent.com/{repo}/{commit}/{file}"
            if not path.exists():
                path.parent.mkdir(parents=True, exist_ok=True)
                with urllib.request.urlopen(url, timeout=30) as response:
                    path.write_bytes(response.read())
            # Git blob identity verifies both newly fetched and already present files.
            data = path.read_bytes()
            blob = hashlib.sha1(f"blob {len(data)}\0".encode() + data).hexdigest()
            if blob != item["sha"]:
                raise ValueError(f"Upstream bytes differ: {path}")
            records.append(dict(path=str(path.relative_to(ROOT)).replace("\\", "/"),
                                source=url, gitBlob=blob, sha256=digest(path), bytes=len(data)))
    target = HERE / "upstream-lock.json"
    value = dict(schemaVersion=1, repositories=SOURCES, files=records)
    if target.exists():
        previous = json.loads(target.read_text())
        if any(record not in records for record in previous["files"]):
            raise ValueError("Previously locked source differs; preserve and investigate it.")
    target.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def replace_once(text, old, new, expected=1):
    if text.count(old) != expected:
        raise ValueError(f"Source drift: expected {expected} occurrences of {old!r}")
    return text.replace(old, new)


def prepare(out):
    lock = json.loads((HERE / "upstream-lock.json").read_text())
    for item in lock["files"]:
        if digest(ROOT / item["path"]) != item["sha256"]:
            raise ValueError(f"Hash mismatch: {item['path']}")
    out.mkdir(parents=True, exist_ok=False)
    source = HERE / "Upstream~" / "SimplePH" / "src"
    changes = []
    for path in sorted(source.rglob("*")):
        if path.suffix not in (".hpp", ".cpp") or path.name == "bindings.cpp":
            continue
        old = path.read_text(encoding="utf-8")
        new = old.replace("std::vector<Particle>", "ParticleStorage")
        new = re.sub(r"const Particle\s*&", "const auto&", new)
        new = re.sub(r"(?<!\w)Particle\s*&", "auto&&", new)
        new = new.replace("auto &p : particles", "auto &&p : particles")
        # Preserve the application's canonical owning input type. Assignment pays
        # for the selected layout's allocation and complete conversion.
        new = new.replace("const ParticleStorage &particles_", "const std::vector<Particle> &particles_")
        if path.name == "particle.hpp":
            new += '\n#include "layout_storage.hpp"\n'
        if path.name == "solver.hpp":
            # Upstream passes these uninitialized members to EOS/calculators in
            # its constructor. Deterministic zero initialization in EVERY arm;
            # the public setters still establish actual physical parameters.
            for field in ("c", "dt", "mu", "rho0", "rho_fluct"):
                new = replace_once(new, f"double {field};", f"double {field} = 0.0;")
            new = replace_once(new, "int damp_timesteps;", "int damp_timesteps = 0;")
            new = replace_once(new, "    // getter functions", "    double task_dt() const { return dt; }\n    // getter functions")
        if path.name == "solver.cpp":
            new = '#include "task_trace.hpp"\n' + new
            new = replace_once(new, "        step_times.push_back(step_time);", "        whole_task_ticks.push_back(step_time * 1000.0);\n        step_times.push_back(step_time);")
            # Separate diagnostic binary. In formal builds these scopes compile
            # to the original statements without a clock or run-time branch.
            for statement, label in (
                ("integrator->step1(particles, fluid_indices, accel, dt, Lx, Ly);", "integrate1"),
                ("cell_grid.update_neighbors(particles, neighbors);", "neighbors"),
                ("density_calculator.compute_summation(particles, fluid_indices, neighbors, Lx, Ly);", "density"),
                ("pressure_calculator.compute(particles, fluid_indices, options.use_negative_pressure_truncation);", "pressure"),
                ("boundary_calculator.compute(particles, boundary_indices, neighbors, eos, b_eff, rho0);", "boundary"),
                ("force_calculator.compute(particles, fluid_indices, neighbors, options, mu, b_eff, dx0, c, accel);", "force"),
                ("integrator->step2(particles, fluid_indices, accel, dt, Lx, Ly);", "integrate2"),
            ):
                new = replace_once(new, statement, f'TASK_PHASE("{label}", {statement})')
        if path.name == "cell_grid.hpp":
            new = replace_once(new, "private:\n", "private:\n    std::vector<std::vector<std::vector<int>>> reusable_local_cells;\n")
        if path.name == "cell_grid.cpp":
            # Shared upstream correctness fix. A tiny negative coordinate can
            # round to exactly L after fmod(x,L)+L, producing index == count.
            # Clamp the integer cell in BOTH insertion and lookup, in every arm.
            new = "#include <algorithm>\n#include <stdexcept>\n" + new
            new = replace_once(new, "    // number cells", """    if (!(rcut > 0 && Lx > 0 && Ly > 0) || !std::isfinite(rcut) || !std::isfinite(Lx) || !std::isfinite(Ly))
        throw std::invalid_argument("Invalid periodic grid geometry");
    // number cells""")
            new = replace_once(new, "    const int N = particles.size();", """    const int N = particles.size();
    // Validate before OpenMP and before converting a coordinate to an integer.
    // Clamping must never disguise NaN/Inf or undefined float-to-int conversion.
    for (const auto& particle : particles)
        if (!std::isfinite(particle.x[0]) || !std::isfinite(particle.x[1]) ||
            std::abs(particle.x[0]) > Lx / 2 || std::abs(particle.x[1]) > Ly / 2)
            throw std::invalid_argument("Coordinate outside the finite centered periodic task domain");""")
            new = replace_once(new, "int cx = int(xp / hx);", "int cx = std::clamp(int(xp / hx), 0, nx - 1);", expected=2)
            new = replace_once(new, "int cy = int(yp / hy);", "int cy = std::clamp(int(yp / hy), 0, ny - 1);", expected=2)
            old_alloc = "    std::vector<std::vector<std::vector<int>>> local_cells(nThreads, std::vector<std::vector<int>>(nCells));"
            new_alloc = """#if LAYOUT_KIND == 0
    std::vector<std::vector<std::vector<int>>> local_cells(nThreads, std::vector<std::vector<int>>(nCells));
#else
    if (reusable_local_cells.size() != static_cast<size_t>(nThreads))
        reusable_local_cells.resize(nThreads);
    for (auto& thread_cells : reusable_local_cells) {
        thread_cells.resize(nCells);
        for (auto& cell : thread_cells) cell.clear();
    }
    auto& local_cells = reusable_local_cells;
#endif"""
            new = replace_once(new, old_alloc, new_alloc)
            new = replace_once(new, "    neighbors.clear();\n    neighbors.resize(particles.size());", """#if LAYOUT_KIND == 0
    neighbors.clear();
    neighbors.resize(particles.size());
#else
    neighbors.resize(particles.size());
    for (auto& neighbor : neighbors) neighbor.clear();
#endif""")
            # MSVC's OpenMP 2.0 requires signed induction. Same iteration order.
            new = replace_once(new, "for (size_t i = 0; i < particles.size(); ++i)",
                               "for (int i = 0; i < static_cast<int>(particles.size()); ++i)")
        if path.name == "vtk_writer.cpp":
            new = replace_once(new, "        return;", '        throw std::runtime_error("VTU output could not be opened");')
            new = replace_once(new, "    f.close();", '    f.close();\n    if (!f) throw std::runtime_error("VTU output write failed");')
        if path.name == "pressure_calculator.cpp":
            new = "#include <algorithm>\n" + new
        target = out / path.relative_to(source)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(new, encoding="utf-8")
        changes.extend(difflib.unified_diff(old.splitlines(True), new.splitlines(True),
                                           fromfile=str(path.relative_to(ROOT)), tofile=str(target)))
    (out / "adaptation.patch").write_text("".join(changes), encoding="utf-8")
    (out / "adaptation-manifest.json").write_text(json.dumps({
        "upstreamLockSha256": digest(HERE / "upstream-lock.json"),
        "generatorSha256": digest(__file__),
        "files": {str(p.relative_to(out)): digest(p) for p in sorted(out.rglob("*")) if p.is_file()},
    }, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fetch", action="store_true")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if args.fetch:
        fetch()
    if args.output:
        prepare(args.output.resolve())
