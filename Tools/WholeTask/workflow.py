"""One complete SimplePH invocation: native solver -> VTU velocity-profile consumer.

This launches an explicit executable only. The campaign mutex belongs to the
foreground parent. Each call uses a new output directory and retains every file.
"""
import argparse
import csv
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess
import struct
import time
import xml.etree.ElementTree as ET
from telemetry import CpuTelemetry


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def validate_checkpoint(path, native):
    data = path.read_bytes()
    offset = 0
    count, res = native["count"], native["resolution"]
    rows = count // res
    dx = .1 / res
    mass = 1000.0 * (dx * dx)
    fluid = 0
    max_speed = 0.0
    for i in range(count):
        if offset + 60 > len(data):
            raise ValueError("Truncated canonical record")
        x, y, vx, vy, rho, pressure, m, kind = struct.unpack_from("<7dI", data, offset)
        offset += 60
        if not all(math.isfinite(v) for v in (x, y, vx, vy, rho, pressure, m)) or rho <= 0 or m != mass:
            raise ValueError("Nonfinite state, nonpositive density or changed exact mass")
        initial_y = -(rows*dx)/2 + dx/2 + (i % rows)*dx
        expected_kind = int(initial_y > .05 or initial_y < -.05)
        if kind != expected_kind or not (-.05 <= x < .05) or not (-rows*dx/2 <= y < rows*dx/2):
            raise ValueError("Particle type/order or periodic boundary violation")
        for field, width in enumerate((1, 2, 2, 2, 2)):
            if offset >= len(data):
                raise ValueError("Truncated optional field")
            present = data[offset]
            offset += 1
            if present not in (0, 1) or present != int(field == 4 and kind == 1):
                raise ValueError("Changed optional field engagement")
            if present:
                if offset+width*8 > len(data):
                    raise ValueError("Truncated optional payload")
                values = struct.unpack_from("<"+"d"*width, data, offset)
                offset += width*8
                if not all(math.isfinite(v) for v in values):
                    raise ValueError("Nonfinite optional payload")
        fluid += kind == 0
        max_speed = max(max_speed, math.hypot(vx, vy))
    if offset != len(data) or fluid != res*res:
        raise ValueError("Canonical tail/count mismatch")
    return dict(records=count, bytes=len(data), allFieldsFinite=True, exactMassAndTypePreserved=True,
                optionalEngagementPreserved=True, periodicPositionsValid=True, maxSpeed=max_speed)


def consume(directory, native):
    """Portable version of the upstream VTU -> velocity-profile consumer.

    Reads the actual exported fields, writes a per-row velocity profile, volume
    flux, kinetic energy and transient analytical L2 diagnostics. XML parsing,
    profile calculation and CSV output are inside caller-visible timing.
    """
    snapshots = sorted((directory / "frames").glob("*.vtu"))
    expected_steps = list(range(0, native["steps"], native["exportEvery"])) + [native["steps"]]
    if len(snapshots) != len(expected_steps):
        raise ValueError("Missing or unexpected VTU snapshots")
    summaries = []
    count, res = native["count"], native["resolution"]
    rows = count // res
    dx = 0.1 / res
    mass = 1000.0 * (dx * dx)
    with (directory / "velocity-profile.csv").open("w", newline="") as stream:
        writer = csv.writer(stream)
        writer.writerow(["step", "time", "row", "type", "mean_y", "mean_vx", "mean_vy", "mean_rho", "analytical_vx"])
        for file, step in zip(snapshots, expected_steps):
            if int(file.stem.split("_")[-1]) != step:
                raise ValueError("VTU step/phase mismatch")
            root = ET.parse(file).getroot()
            piece = root.find("./UnstructuredGrid/Piece")
            if piece is None or int(piece.attrib["NumberOfPoints"]) != count or int(piece.attrib["NumberOfCells"]) != count:
                raise ValueError("VTU shape mismatch")
            fields = {x.attrib["Name"]: [float(v) for v in x.text.split()] for x in piece.findall("./PointData/DataArray")}
            positions = [float(v) for v in piece.find("./Points/DataArray").text.split()]
            if set(fields) != {"p", "rho", "type", "v", "vf"} or len(positions) != count * 3:
                raise ValueError("Missing VTU fields")
            for key, values in fields.items():
                if len(values) != count * (3 if key in ("v", "vf") else 1) or any(not math.isfinite(x) for x in values):
                    raise ValueError("Invalid VTU data")
            if any(not math.isfinite(x) for x in positions) or min(fields["rho"]) <= 0 or not set(fields["type"]) <= {0, 1}:
                raise ValueError("Invalid physical state")
            cells = {a.attrib["Name"]: [int(x) for x in a.text.split()] for a in piece.findall("./Cells/DataArray")}
            if cells != {"connectivity": list(range(count)), "offsets": list(range(1, count + 1)), "types": [1] * count}:
                raise ValueError("VTU topology/order changed")
            t = step * native["dt"]
            body = 24.0 * native["re"] / (1000.0**2 * 0.1**3)
            nu, height = 0.001, 0.1
            error2 = 0.0
            exact2 = 0.0
            kinetic = 0.0
            mean_velocity = 0.0
            fluid_count = 0
            for row in range(rows):
                indices = [i * rows + row for i in range(res)]
                y = math.fsum(positions[3*i+1] for i in indices) / res
                u = math.fsum(fields["v"][3*i] for i in indices) / res
                v = math.fsum(fields["v"][3*i+1] for i in indices) / res
                density = math.fsum(fields["rho"][i] for i in indices) / res
                kind = fields["type"][indices[0]]
                analytical = 0.0
                if kind == 0:
                    z = y + height / 2
                    analytical = 4 * body * height**2 / (nu * math.pi**3) * math.fsum(
                        math.sin(k*math.pi*z/height) / k**3 * (-math.expm1(-nu*(k*math.pi/height)**2*t))
                        for k in range(1, 80, 2))
                    error2 += (u - analytical)**2
                    exact2 += analytical**2
                    for i in indices:
                        ux, uy = fields["v"][3*i:3*i+2]
                        kinetic += 0.5 * mass * (ux*ux + uy*uy)
                        mean_velocity += ux
                        fluid_count += 1
                writer.writerow([step, t, row, int(kind), y, u, v, density, analytical])
            summaries.append(dict(step=step, time=t, fluidParticles=fluid_count,
                                  meanFluidVx=mean_velocity/fluid_count,
                                  volumeFluxPerDepth=mean_velocity/fluid_count * height,
                                  kineticEnergy=kinetic,
                                  relativeAnalyticalL2=math.sqrt(error2/exact2) if exact2 > 0 else None,
                                  minimumDensity=min(fields["rho"]), maximumDensity=max(fields["rho"])))
    return summaries


def case_key(case):
    return hashlib.sha256(json.dumps({k: case[k] for k in ("resolution", "steps", "exportEvery", "re", "phase")},
                                    sort_keys=True).encode()).hexdigest()


def run(exe, case, output, correctness=False, validated=None):
    if not correctness:
        if not validated or validated["caseKey"] != case_key(case) or validated["executableSha256"] != sha(exe):
            raise ValueError("Performance run requires complete-output correctness for this exact case and executable")
    output.mkdir(parents=True, exist_ok=False)
    mode = "correctness" if correctness else "performance"
    command = [str(exe), str(case["resolution"]), str(case["steps"]), str(case["exportEvery"]), str(case["re"]), str(case["phase"]), mode]
    (output / "command.json").write_text(json.dumps(command, indent=2)+"\n")
    monitor = CpuTelemetry()
    preflight = monitor.preflight()
    (output / "preflight.json").write_text(json.dumps(preflight, indent=2)+"\n")
    if not correctness and not preflight["passed"]:
        raise RuntimeError(f"Per-task CPU preflight failed; no native child started. Retained at {output}")
    monitor.start()
    start = time.perf_counter()
    started_ns = time.time_ns()
    with (output / "stdout.txt").open("w") as stdout, (output / "stderr.txt").open("w") as stderr:
        child = subprocess.Popen(command, cwd=output, stdout=stdout, stderr=stderr,
                                 creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
        monitor.attach(child)
        # Timeout is handled by the foreground campaign's owned process tree.
        # Blocking wait observes actual completion without timed-wait polling
        # latency. The outer foreground stage owns the timeout/process group.
        code = child.wait()
    if code:
        telemetry = monitor.finish()
        (output / "telemetry-failed.json").write_text(json.dumps(telemetry, indent=2)+"\n")
        raise RuntimeError(f"Native task failed ({code}); retained at {output}")
    native_end = time.perf_counter()
    native = json.loads((output / "native-result.json").read_text())
    summaries = consume(output, native)
    end = time.perf_counter()
    telemetry = monitor.finish()
    result = dict(schemaVersion=1, case=case, native=native, summaries=summaries, outputDirectory=str(output),
                  processIdentity=f"{child.pid}:{started_ns}",
                  completeTaskMs=(end-start)*1000,
                  nativeProcessWallMs=(native_end-start)*1000,
                  consumerMs=(end-native_end)*1000,
                  mode=mode, completed=True, includesCompleteBoundary=True,
                  cpuPreflight=preflight, cpuDuring=telemetry,
                  performanceEnvironmentEligible=not correctness and not native["diagnostic"] and preflight["passed"] and telemetry["passed"],
                  allocationEligibility="Unknown", nativeAllocationBytes=None,
                  workerManagedAllocationBytes=None, gpuMs=None,
                  boundary="native process launch, input generation, ownership, all solver steps/index maintenance, periodic and terminal VTU, disposal, XML profile consumption and CSV write; OS durable flush and experiment-controller startup excluded; correctness-only checkpoints never enter eligible timing")
    # Complete persisted-byte audit is outside the performance window. Hashes
    # cover ALL requested snapshots, complete-precision state, and consumed output.
    result["canonicalValidation"] = validate_checkpoint(output/"state.bin", native) if correctness else dict(
        status="separate-correctness-mode", receipt=validated)
    result["files"] = {str(p.relative_to(output)): dict(sha256=sha(p), bytes=p.stat().st_size)
                       for p in sorted(output.rglob("*")) if p.is_file()}
    result["datasetHash"] = result["files"]["input.bin"]["sha256"] if correctness else validated["datasetHash"]
    result["fullOutputHash"] = hashlib.sha256(json.dumps({p: r["sha256"] for p, r in result["files"].items()
        if p.endswith(".vtu") or p in ("state.bin", "velocity-profile.csv")}, sort_keys=True).encode()).hexdigest()
    result["taskOutputHash"] = hashlib.sha256(json.dumps({p: r["sha256"] for p, r in result["files"].items()
        if p.endswith(".vtu") or p == "velocity-profile.csv"}, sort_keys=True).encode()).hexdigest()
    result["caseKey"] = case_key(case)
    result["executableSha256"] = sha(exe)
    if not correctness:
        if (output/"input.bin").exists() or (output/"state.bin").exists():
            raise ValueError("Correctness-only I/O leaked into performance mode")
        if result["taskOutputHash"] != validated["taskOutputHash"]:
            raise ValueError("Performance output differs from separately verified complete task")
    (output / "result.json").write_text(json.dumps(result, indent=2)+"\n")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--executable", type=Path, required=True)
    parser.add_argument("--case", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--correctness", action="store_true")
    parser.add_argument("--validated", type=Path)
    args = parser.parse_args()
    run(args.executable.resolve(), json.loads(args.case.read_text()), args.output.resolve(), args.correctness,
        json.loads(args.validated.read_text()) if args.validated else None)
