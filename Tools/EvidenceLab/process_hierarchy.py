#!/usr/bin/env python3
"""Replay retained selection-policy holdouts; never rewrite a primary decision.

The estimand permits each process to select a different candidate on calibration.
It is NOT a fixed-candidate comparison (the strict runtime API remains unchanged).
Only the standard library is used; xorshift32 and linear quantiles are specified.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def quantile(values, q):
    values = sorted(values)
    rank = (len(values) - 1) * q
    lo = int(rank)
    hi = min(lo + 1, len(values) - 1)
    return values[lo] + (values[hi] - values[lo]) * (rank - lo)


class Random:
    def __init__(self, seed):
        require(type(seed) is int and 0 < seed <= 0xFFFFFFFF, "Seed must be nonzero uint32")
        self.state = seed

    def index(self, n):
        x = self.state
        x ^= (x << 13) & 0xFFFFFFFF
        x ^= x >> 17
        x ^= (x << 5) & 0xFFFFFFFF
        self.state = x & 0xFFFFFFFF
        return self.state % n


COMPONENTS = (("Resident", "ResidentSamplesMillisecondsPerTick"),
              ("Ingress", "IngressSamplesMilliseconds"),
              ("Export", "ExportSamplesMilliseconds"))


def prepare_pair(baseline, candidate, lifetime):
    for result in (baseline, candidate):
        require(result["SampleSchemaVersion"] == 1 and result["Phase"] == 1,
                "Only schema-1 untouched holdout samples are accepted")
        require(result["Completed"] and result["ParityPassed"] and result["Parity"]["Passed"],
                "Failed or incomplete candidate")
        require(result["HotPathManagedAllocationBytes"] == 0 and
                result["BoundaryManagedAllocationBytes"] == 0, "Managed allocation gate failed")
        require(result["BoundaryCost"]["LifetimeTicks"] == lifetime and lifetime > 0,
                "Lifetime mismatch")
    for key in ("ScenarioId", "ScenarioContractVersion", "ElementCount", "StepsPerSample"):
        require(baseline[key] == candidate[key], "Paired settings mismatch: " + key)
    require(baseline["Candidate"]["IsBaseline"], "Reference is not AoS")
    pairs = []
    for prefix, field in COMPONENTS:
        maps = []
        positions = []
        for result in (baseline, candidate):
            values, ids = result[field], result[prefix + "BlockIds"]
            order = result[prefix + "OrderPositions"]
            require(len(values) >= 3 and len(values) == len(ids) == len(order), "Incomplete paired blocks")
            require(all(type(i) is int and i >= 0 for i in ids) and len(set(ids)) == len(ids),
                    "Duplicate/invalid block IDs")
            require(all(math.isfinite(v) and v >= 0 for v in values), "Invalid raw timing")
            require(all(type(i) is int and i in (0, 1) for i in order), "Invalid paired order position")
            maps.append(dict(zip(ids, values)))
            positions.append(dict(zip(ids, order)))
        require(maps[0].keys() == maps[1].keys(), "Unpaired block IDs")
        ids = sorted(maps[0])
        require(all(positions[0][i] != positions[1][i] for i in ids), "Overlapping paired positions")
        pairs.append([(maps[0][i], maps[1][i]) for i in ids])
    return pairs


def log_ratio(pairs, lifetime, random=None):
    costs = [0.0, 0.0]
    for component, rows in enumerate(pairs):
        # One block draw is shared by baseline/candidate; components are separate blocks.
        chosen = rows if random is None else [rows[random.index(len(rows))] for _ in rows]
        for side in (0, 1):
            costs[side] += quantile([r[side] for r in chosen], .95) / (1 if component == 0 else lifetime)
    require(min(costs) > 0, "Nonpositive composite cost")
    return math.log(costs[1] / costs[0])


def bootstrap(processes, lifetime, iterations=4000, seed=0xC2B2AE35, confidence=.95):
    require(len(processes) >= 2, "At least two independent processes required")
    require(type(iterations) is int and iterations >= 100 and .5 < confidence < 1, "Invalid bootstrap settings")
    random = Random(seed)
    point = sum(log_ratio(p, lifetime) for p in processes) / len(processes)
    estimates = []
    for _ in range(iterations):
        estimates.append(sum(log_ratio(processes[random.index(len(processes))], lifetime, random)
                             for _ in processes) / len(processes))
    tail = (1 - confidence) / 2
    convert = lambda x: 100 * (1 - math.exp(x))
    return {"pointImprovementPercent": convert(point),
            "lowerImprovementPercent": convert(quantile(estimates, 1 - tail)),
            "upperImprovementPercent": convert(quantile(estimates, tail)),
            "meanProcessLogRatio": point, "confidenceLevel": confidence,
            "iterations": iterations, "seed": seed, "rng": "xorshift32-modulo-v1",
            "quantile": "linear interpolation at (n-1)*p",
            "resamplingUnit": "Player process, then paired measurement block within each component",
            "processWeight": "equal, regardless of within-process sample count"}


def load_retained(manifest_path, scenario_id):
    manifest = read(manifest_path)
    require(manifest["retainedRunCount"] == 5 and len(manifest["runs"]) == 5,
            "This replay requires all five retained launches")
    require(manifest["primaryRun"] == "run-01", "Historical primary must remain run-01")
    seen_runs, seen_ids, seen_hashes = set(), set(), set()
    processes, provenance, selected = [], [], []
    settings = environment = None
    for run in manifest["runs"]:
        path = manifest_path.parent / run["file"]
        digest = sha(path)
        require(digest == run["sha256"].upper(), "Raw suite hash mismatch: " + run["run"])
        require(digest not in seen_hashes and run["run"] not in seen_runs, "Duplicate launch/artifact")
        suite = read(path)
        require(suite["SchemaVersion"] == 3 and suite["RunId"] == run["runId"], "Suite identity mismatch")
        require(suite["RunId"] not in seen_ids, "Duplicate process RunId")
        seen_runs.add(run["run"]); seen_ids.add(suite["RunId"]); seen_hashes.add(digest)
        current_environment = suite["Environment"]
        require(current_environment["BuildType"] == "Release" and
                current_environment["ScriptingBackend"] == "IL2CPP", "Formal build gate failed")
        if environment is None:
            environment = current_environment
        require(environment == current_environment, "Process environment mismatch")
        matches = [s for s in suite["Scenarios"] if s["Scenario"]["ScenarioId"] == scenario_id]
        require(len(matches) == 1, "Missing/duplicate scenario")
        s = matches[0]
        current_settings = {key: s[key] for key in ("Scenario", "ElementCount", "HoldoutElementCount",
            "CalibrationSeed", "HoldoutSeed", "FixedDeltaTime", "SamplesPerCandidate",
            "BoundarySamplesPerCandidate", "LifetimeTicks", "CandidateOrderSeed", "BootstrapIterations",
            "BootstrapConfidenceLevel", "MinimumImprovementPercent", "CalibrationDatasetHash", "HoldoutDatasetHash")}
        if settings is None:
            settings = current_settings
        require(settings == current_settings, "Frozen selection-policy settings mismatch")
        require(s["CalibrationSeed"] != s["HoldoutSeed"] and
                s["CalibrationDatasetHash"] != s["HoldoutDatasetHash"], "Holdout reused for calibration")
        require(s["SamplingDesign"]["PairingUnit"] == "complete measurement block" and
                s["SamplingDesign"]["CalibrationTunesCandidates"] and
                not s["SamplingDesign"]["HoldoutRetuningPermitted"], "Invalid isolation/pairing protocol")
        b, c = s["HoldoutBaselineResult"], s["HoldoutSelectedResult"]
        require(b is not None and c is not None and b.get("Completed") and c.get("Completed"),
                "No measured holdout pair: cannot synthesize fallback holdout evidence")
        decision = s["CalibrationDecision"]
        require(decision["Status"] == 2 and b["Candidate"] == decision["BaselineCandidate"] and
                c["Candidate"] == decision["SelectedCandidate"], "Holdout changed frozen selection")
        pair = prepare_pair(b, c, s["LifetimeTicks"])
        raw_improvement = 100 * (1 - math.exp(log_ratio(pair, s["LifetimeTicks"])))
        require(math.isclose(raw_improvement, s["FinalDecision"]["ImprovementPercent"], abs_tol=1e-8),
                "Stored decision disagrees with raw holdout")
        preflight = manifest_path.parent / run["preflightFile"]
        require(sha(preflight) == run["preflightSha256"].upper(), "Preflight hash mismatch")
        processes.append(pair)
        selected.append({"run": run["run"], "baselineId": b["Candidate"]["CandidateId"],
                         "selectedId": c["Candidate"]["CandidateId"],
                         "rawImprovementPercent": raw_improvement,
                         "finalDecision": s["FinalDecision"], "stepsPerSample": b["StepsPerSample"]})
        provenance.append({"run": run["run"], "runId": suite["RunId"], "file": run["file"],
                           "sha256": digest, "preflightFile": run["preflightFile"],
                           "preflightSha256": sha(preflight)})
    return manifest, settings, environment, processes, selected, provenance


def summarize(manifest_path, scenario_id, iterations=4000, seed=0xC2B2AE35):
    manifest, settings, environment, processes, selected, provenance = load_retained(manifest_path, scenario_id)
    return {"schemaVersion": 1, "artifactType": "retained-process-selection-policy-summary",
            "analysisRole": "post-hoc robustness summary; does not change preregistered primary",
            "estimand": "equal-process mean log ratio of calibration-selected candidate vs calibration-tuned AoS on untouched holdout",
            "fixedCandidateClaim": False, "primaryRun": manifest["primaryRun"],
            "deviceCount": 1, "deviceIdentityScope": "same physical device declared by historical protocol; CPU model is not a unique device ID",
            "processCount": len(processes), "processesCountAsDevices": False,
            "sourceManifestSha256": sha(manifest_path), "analysisScriptSha256": sha(Path(__file__)),
            "recordedBinaryHashes": manifest["playerArtifacts"],
            "binaryHashVerification": "manifest provenance only; binaries not retained here",
            "environment": environment, "settings": settings, "runs": provenance,
            "perProcessSelections": selected,
            "hierarchicalInterval": bootstrap(processes, settings["LifetimeTicks"], iterations, seed),
            "limitations": ["Five processes on one device; no device-population inference.",
                "Winner and tuned AoS vary by process: this estimates the frozen selection procedure, not one fixed layout/kernel.",
                "Adaptive ticks per measurement block may vary by process; each process retains its own measured block normalization.",
                "Bootstrap describes retained process/block variability; it does not control thermal, clock or external-app interference.",
                "TransformExport fallback has no measured holdout and receives no fabricated interval."]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--scenario", default="particle-integrate-v2")
    parser.add_argument("--iterations", type=int, default=4000)
    parser.add_argument("--seed", type=lambda s: int(s, 0), default=0xC2B2AE35)
    args = parser.parse_args()
    require(args.output.resolve() != args.manifest.resolve() and not args.output.exists(),
            "Output must be a new artifact; retained evidence cannot be overwritten")
    report = summarize(args.manifest, args.scenario, args.iterations, args.seed)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print(json.dumps(report["hierarchicalInterval"], indent=2))


if __name__ == "__main__":
    main()
