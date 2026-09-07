#!/usr/bin/env python3
"""Independent integrity, cost, pool, isolation and regret replay for search artifacts."""
import argparse
import json
import math
from pathlib import Path
from process_hierarchy import COMPONENTS, quantile, read, require, sha


def close(actual, expected, name):
    require(math.isfinite(actual) and math.isclose(actual, expected, rel_tol=1e-10, abs_tol=1e-9),
            "Recomputed value mismatch: " + name)


def cost(result, profile, phase):
    require(result["SampleSchemaVersion"] == 1 and result["Phase"] == phase, "Sample phase/schema mismatch")
    require(result["Completed"] and result["ParityPassed"] and result["Parity"]["Passed"], "Failed candidate evidence")
    require(result["HotPathManagedAllocationBytes"] == 0 and result["BoundaryManagedAllocationBytes"] == 0,
            "Allocation gate failed")
    require(result["ElementCount"] == profile["ElementCount" if phase == 0 else "HoldoutElementCount"], "Element-count mismatch")
    require(result["BoundaryCost"]["LifetimeTicks"] == profile["LifetimeTicks"], "Lifetime mismatch")
    total = 0
    for index, (prefix, field) in enumerate(COMPONENTS):
        values, ids = result[field], result[prefix + "BlockIds"]
        expected = profile["SamplesPerCandidate" if index == 0 else "BoundarySamplesPerCandidate"]
        require(len(values) == len(ids) == len(result[prefix + "OrderPositions"]) == expected,
                "Final evidence sample gate changed")
        require(len(set(ids)) == len(ids) and all(type(i) is int and i >= 0 for i in ids), "Invalid block IDs")
        require(all(math.isfinite(v) and v >= 0 for v in values), "Invalid sample")
        total += quantile(values, .95) / (1 if index == 0 else profile["LifetimeTicks"])
    require(total > 0, "Nonpositive cost")
    close(result["AmortizedLatency"]["P95Milliseconds"], total, "raw component P95")
    return total


def profile_costs(profile):
    costs = {}
    results = profile["CalibrationResults"]
    for result in results:
        candidate_id = result["Candidate"]["CandidateId"]
        require(candidate_id not in costs, "Duplicate candidate")
        costs[candidate_id] = cost(result, profile, 0)
    # Entire candidate block must be present exactly once at every measurement position.
    for prefix, _ in COMPONENTS:
        rows = {}
        for result in results:
            for block, position in zip(result[prefix + "BlockIds"], result[prefix + "OrderPositions"]):
                rows.setdefault(block, []).append(position)
        require(all(sorted(row) == list(range(len(results))) for row in rows.values()), "Incomplete/overlapping paired blocks")
    return costs


def verify_holdout(profile, frozen):
    require(profile["CalibrationDecision"] == frozen["CalibrationDecision"] and
            profile["CalibrationResults"] == frozen["CalibrationResults"], "Calibration changed after freeze")
    require(profile["CalibrationSeed"] != profile["HoldoutSeed"], "Holdout seed reused")
    calibration, final = profile["CalibrationDecision"], profile["FinalDecision"]
    b, c = profile["HoldoutBaselineResult"], profile["HoldoutSelectedResult"]
    if calibration["Status"] != 2:
        require(not (b and b.get("Completed")) and not (c and c.get("Completed")), "Fallback holdout was unexpectedly retuned")
        require(final == calibration, "Fallback decision changed")
        return
    require(b["Candidate"] == calibration["BaselineCandidate"] and c["Candidate"] == calibration["SelectedCandidate"],
            "Holdout candidate substitution")
    require(profile["HoldoutDatasetHash"] and profile["HoldoutDatasetHash"] != profile["CalibrationDatasetHash"], "Holdout data reused")
    improvement = 100 * (1 - cost(c, profile, 1) / cost(b, profile, 1))
    close(final["ImprovementPercent"], improvement, "holdout improvement")
    require(final["SelectedCandidate"] in (b["Candidate"], c["Candidate"]), "Holdout reranked candidates")
    interval = final["ImprovementConfidenceInterval"]
    require(interval["Iterations"] == profile["BootstrapIterations"] and
            interval["ConfidenceLevel"] == profile["BootstrapConfidenceLevel"], "Holdout uncertainty gate changed")
    passes = improvement >= profile["MinimumImprovementPercent"] and interval["LowerBoundPercent"] > 0
    require((final["Status"] == 2) == passes, "Final gate contradicts retained interval/point estimate")


def validate_comparison(path):
    result = read(path)
    environment_path = path.parent.parent / "environment.json"
    require(sha(environment_path) == result["EnvironmentFingerprint"], "Environment fingerprint mismatch")
    require(result["SchemaVersion"] == 1 and result["BothSelectionsFrozenBeforeHoldout"] and
            result["FinalEvidenceRequirementsUnchanged"], "Missing search protocol gates")
    linked = {}
    for stem, field in (("settings", "SettingsSha256"), ("quick", "QuickSha256"),
                        ("adaptive-calibration", "AdaptiveCalibrationSha256"),
                        ("exhaustive-calibration", "ExhaustiveCalibrationSha256"),
                        ("frozen-selections", "FrozenSelectionsSha256"),
                        ("adaptive-final", "AdaptiveFinalSha256"), ("exhaustive-final", "ExhaustiveFinalSha256")):
        artifact = path.parent / (stem + ".json")
        require(sha(artifact) == result[field], "Actual artifact hash mismatch: " + stem)
        linked[stem] = read(artifact)
    frozen = linked["frozen-selections"]
    settings = linked["settings"]
    require(result["Quick"] == linked["quick"] and result["Adaptive"] == linked["adaptive-final"] and
            result["Exhaustive"] == linked["exhaustive-final"], "Embedded evidence mismatch")
    require(frozen["Adaptive"] == linked["adaptive-calibration"] and
            frozen["Exhaustive"] == linked["exhaustive-calibration"], "Checkpoint did not bind both calibration choices")
    for name in ("Adaptive", "Exhaustive"):
        require(not frozen[name]["HoldoutDatasetHash"], "Holdout appeared before freeze")
        for field in ("SamplesPerCandidate", "BoundarySamplesPerCandidate", "BootstrapIterations",
                      "BootstrapConfidenceLevel", "MinimumImprovementPercent", "LifetimeTicks", "CalibrationSeed", "HoldoutSeed"):
            require(result[name][field] == settings[field], "Unequal final evidence settings: " + field)
        verify_holdout(result[name], frozen[name])
    quick = profile_costs(result["Quick"])
    adaptive = profile_costs(result["Adaptive"])
    exhaustive = profile_costs(result["Exhaustive"])
    require(set(quick) == set(exhaustive), "Quick and exhaustive candidate pools differ")
    require(set(adaptive) == set(result["ExecutedFinalistIds"]) and set(adaptive) <= set(exhaustive), "Executed shortlist mismatch")
    descriptors = {r["Candidate"]["CandidateId"]: r["Candidate"] for r in result["Exhaustive"]["CalibrationResults"]}
    for name in ("Quick", "Adaptive"):
        for row in result[name]["CalibrationResults"]:
            require(row["Candidate"] == descriptors[row["Candidate"]["CandidateId"]], "Candidate definition changed")
        require(result[name]["CalibrationDatasetHash"] == result["Exhaustive"]["CalibrationDatasetHash"], "Calibration data changed")
        require(result[name]["TicksPerBlock"] == result["Exhaustive"]["TicksPerBlock"] and
                result[name]["WarmupBlocks"] == result["Exhaustive"]["WarmupBlocks"], "Common pilot changed")
    require(all(key in adaptive for key, d in descriptors.items() if d["IsBaseline"]), "Tuned AoS controls were eliminated")
    count = lambda profile: sum(len(r[field]) for r in profile["CalibrationResults"] for _, field in COMPONENTS)
    close(result["AdaptiveComponentEvaluationCount"], count(result["Quick"]) + count(result["Adaptive"]), "adaptive evaluation count")
    close(result["ExhaustiveComponentEvaluationCount"], count(result["Exhaustive"]), "exhaustive evaluation count")
    close(result["AdaptiveCalibrationMilliseconds"], sum(result[k] for k in
          ("SharedPreflightMilliseconds", "QuickMilliseconds", "PlanningMilliseconds", "AdaptiveFullMilliseconds")), "adaptive wall cost")
    close(result["ExhaustiveCalibrationMilliseconds"], result["SharedPreflightMilliseconds"] + result["ExhaustiveFullMilliseconds"], "exhaustive wall cost")
    require(all(math.isfinite(v) and v >= 0 for k, v in result.items() if k.endswith("Milliseconds")), "Invalid wall clock")
    best_id = min(exhaustive, key=exhaustive.get)
    require(result["ExhaustiveWinnerEliminated"] == (best_id not in adaptive), "Missed winner flag mismatch")
    best = min(exhaustive.values())
    shortlist_regret = 100 * (min(exhaustive[k] for k in adaptive) / best - 1)
    selected = result["Adaptive"]["CalibrationDecision"]["SelectedCandidate"]["CandidateId"]
    regret = 100 * (exhaustive[selected] / best - 1)
    close(result["ShortlistOracleRegretPercent"], shortlist_regret, "shortlist regret")
    close(result["ActualAdaptiveSelectionOracleRegretPercent"], regret, "actual selected regret")
    require(result["RegretGatePassed"] == (regret <= result["MaximumAllowedRegretPercent"]), "Regret gate mismatch")
    return {"file": str(path), "sha256": sha(path), "integrityReplay": "passed",
            "adaptiveCalibrationMilliseconds": result["AdaptiveCalibrationMilliseconds"],
            "exhaustiveCalibrationMilliseconds": result["ExhaustiveCalibrationMilliseconds"],
            "adaptiveEvaluations": result["AdaptiveComponentEvaluationCount"],
            "exhaustiveEvaluations": result["ExhaustiveComponentEvaluationCount"],
            "actualSelectionOracleRegretPercent": regret, "regretGatePassed": result["RegretGatePassed"],
            "adaptiveHoldoutStatus": result["Adaptive"]["FinalDecision"]["Status"],
            "exhaustiveHoldoutStatus": result["Exhaustive"]["FinalDecision"]["Status"],
            "limitations": "Integrity/point-cost/decision-gate replay; paired interval numerical estimator covered by independent scientific unit tests. Performance acceptance is separate from valid evidence."}


def validate_formal_root(root):
    registration = read(root / "preregistration.json")
    require(registration["policy"]["processCount"] == 5, "Five processes must be preregistered")
    reports, seen_ids = [], set()
    shared_environment = None
    for i in range(5):
        run = "run-%02d" % (i + 1)
        directory = root / run
        receipt = read(root / (run + "-receipt.json"))
        require(receipt["exitCode"] == 0 and not receipt["timedOut"], "Failed launch retained; formal gate unmet")
        for phase in ("preflight", "postflight"):
            require(sha(root / (run + "-" + phase + ".json")) == receipt[phase + "Sha256"], "Interference snapshot hash mismatch")
        environment = read(directory / "environment.json")
        current_environment = {k: environment[k] for k in ("Processor", "OperatingSystem", "UnityVersion", "WorkerCount", "Backend", "Development", "BurstEnabled")}
        if shared_environment is None: shared_environment = current_environment
        require(shared_environment == current_environment, "Cross-process environment changed")
        require(environment["RunId"] not in seen_ids, "Duplicate process identity")
        seen_ids.add(environment["RunId"])
        require(environment["Backend"] == "IL2CPP" and not environment["Development"] and environment["BurstEnabled"],
                "Formal Release IL2CPP/Burst gate unmet")
        require(environment["WorkerCount"] == 7 and environment["CandidateFileSha256"] == registration["candidateFileSha256"],
                "Executed worker/candidate identity mismatch")
        paths = sorted(directory.glob("*/comparison.json"))
        require(len(paths) == 2, "Missing execution cell")
        require({p.parent.name for p in paths} == {"FrameFaithful", "DependencyChain"}, "Wrong execution cells")
        for path in paths:
            result = read(path)
            require(result["AdaptiveFirst"] == (i % 2 == 0), "Preregistered AB/BA order changed")
            for profile in (result["Adaptive"], result["Exhaustive"]):
                for field, expected in (("ElementCount", 65536), ("HoldoutElementCount", 65539),
                    ("LifetimeTicks", 256), ("SamplesPerCandidate", 40), ("BoundarySamplesPerCandidate", 20),
                    ("BootstrapIterations", 4000), ("BootstrapConfidenceLevel", .95), ("MinimumImprovementPercent", 10)):
                    require(profile[field] == expected, "Formal evidence budget mismatch: " + field)
            reports.append(validate_comparison(path))
    return {"schemaVersion": 1, "processCount": 5, "deviceCount": 1, "comparisons": reports}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    require(not args.output.exists(), "Output already exists")
    report = validate_formal_root(args.root)
    args.output.write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8", newline="\n")
    print("Replayed 10 retained comparisons; inspect regret and holdout gates separately.")
