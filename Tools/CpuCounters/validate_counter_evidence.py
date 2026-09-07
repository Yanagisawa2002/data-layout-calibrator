#!/usr/bin/env python3
"""Validate retained CPU counter evidence without recomputing any layout decision."""
import argparse
import hashlib
import json
import math
import statistics
from pathlib import Path


def validate(path: Path, require_collected: bool = True) -> dict:
    document = json.loads(path.read_text(encoding="utf-8-sig"))
    if document.get("Failure"):
        raise ValueError("Player reported a failure: " + document["Failure"])
    if not document.get("ManagedAllocationMeasurement"):
        raise ValueError("Validated allocation provider identity is missing")
    identity = document["Identity"]
    if identity["BuildType"] != "Release" or not identity["Cpu"] or not identity["ProcessEvidenceId"]:
        raise ValueError("Missing Release/process/CPU identity")
    if not any(b["Path"].endswith("lib_burst_generated.dll") and len(b["Sha256"]) == 64 for b in identity["Binaries"]):
        raise ValueError("Missing Burst binary identity")
    metrics = {m["MetricId"]: m for m in document["Metrics"]}
    for name in ("retired-instructions", "cache-references", "cache-misses", "branch-instructions", "branch-misses"):
        if metrics[name]["Status"] != "Unavailable" or "Value" in metrics[name]:
            raise ValueError("Unsupported PMU metric must be unavailable with no numeric value")
    counts = {"collected": 0, "unavailable": 0, "failed": 0}
    pairs = {}
    timings = {}
    definitions = {}
    hashes = {}
    for row in document["Rows"]:
        key = row["ScenarioId"], row["CandidateId"]
        pair_key = key + (row["Pair"],)
        position = row["OrderPosition"]
        expected_on = (row["Pair"] % 2 == 0) == (position == 1)
        if position not in (0, 1) or row["Enabled"] != expected_on or not math.isfinite(row["EndToEndNanoseconds"]) or row["EndToEndNanoseconds"] < 0:
            raise ValueError("Invalid AB/BA order or timing")
        if position in pairs.setdefault(pair_key, set()):
            raise ValueError("Duplicate arm")
        pairs[pair_key].add(position)
        timings.setdefault(key, {}).setdefault(row["Pair"], {})[row["Enabled"]] = row["EndToEndNanoseconds"]
        if definitions.setdefault(key, row["CandidateDefinitionSha256"]) != row["CandidateDefinitionSha256"]:
            raise ValueError("Candidate identity changed within run")
        if hashes.setdefault(key, row["StateHash"]) != row["StateHash"]:
            raise ValueError("Provider arms changed canonical output")
        capture = row["Capture"]
        context = capture["Context"]
        if context["CandidateId"] != row["CandidateId"] or context["CandidateSchemaSha256"] != row["CandidateDefinitionSha256"] or context["RunId"] != identity["RunId"] or context["ProcessEvidenceId"] != identity["ProcessEvidenceId"]:
            raise ValueError("Capture context identity mismatch")
        if not row["Enabled"]:
            if capture["Status"] != 0 or capture.get("RawCounters"):
                raise ValueError("Disabled arm unexpectedly collected counters")
            continue
        if capture["Status"] == 2:
            counts["collected"] += 1
            if capture["Origin"] != 1 or capture["InterpretationLevel"] != 1:
                raise ValueError("Capture is not observed correlation evidence")
            raw = capture["RawCounters"]
            if len(raw) != 1 or raw[0]["CounterId"] != "windows-process-cpu-cycles":
                raise ValueError("Unexpected or substituted counter")
            artifact = capture["Artifacts"][0]
            # Relocated archives resolve only the original basename inside raw/.
            artifact_path = path.parent / "raw" / Path(artifact["ArtifactPath"].replace("\\", "/")).name
            payload = artifact_path.read_bytes()
            if hashlib.sha256(payload).hexdigest().upper() != artifact["ArtifactSha256"]:
                raise ValueError("Raw endpoint artifact hash mismatch")
            endpoints = dict(line.split("=", 1) for line in payload.decode("utf-8-sig").splitlines())
            start, end, delta = (int(endpoints[n]) for n in ("start", "end", "delta"))
            if min(start, end, delta) < 0 or end - start != delta or float(delta) != raw[0]["Value"]:
                raise ValueError("Raw endpoint arithmetic mismatch")
            if endpoints["candidate"] != row["CandidateId"] or endpoints["run"] != identity["RunId"]:
                raise ValueError("Raw endpoint identity mismatch")
        elif capture["Status"] == 1:
            counts["unavailable"] += 1
            if capture.get("RawCounters") or not capture["StatusCode"] or not capture["StatusReason"]:
                raise ValueError("Unavailable capture contains fabricated values or lacks reason")
        else:
            counts["failed"] += 1
    if not pairs or any(arms != {0, 1} for arms in pairs.values()):
        raise ValueError("Empty or incomplete paired evidence")
    if len(pairs) != len(definitions) * document["Pairs"]:
        raise ValueError("Incorrect number of pairs")
    for label, field in (("collected", "CollectedCaptures"), ("unavailable", "UnavailableCaptures"), ("failed", "FailedCaptures")):
        if counts[label] != document[field]:
            raise ValueError("Capture status totals mismatch")
    summaries = document["Summaries"]
    if len(summaries) != len(definitions):
        raise ValueError("Missing candidate summaries")
    seen_summaries = set()
    for summary in summaries:
        key = summary["ScenarioId"], summary["CandidateId"]
        if key not in timings or key in seen_summaries:
            raise ValueError("Missing or duplicate summary identity")
        seen_summaries.add(key)
        arms = [timings[key][i] for i in range(document["Pairs"])]
        off = statistics.median(a[False] for a in arms)
        on = statistics.median(a[True] for a in arms)
        added = statistics.median(a[True] - a[False] for a in arms)
        expected = {"DisabledMedianNanoseconds": off, "EnabledMedianNanoseconds": on,
                    "EstimatedAddedNanoseconds": added, "EstimatedOverheadPercent": added / off * 100 if off > 0 else 0}
        for field, value in expected.items():
            actual = summary["Overhead"].get(field)
            if actual is None or not math.isfinite(actual) or not math.isclose(actual, value, rel_tol=1e-9, abs_tol=1e-6):
                raise ValueError("Raw paired overhead replay mismatch: " + field)
        if not summary["ParityPassed"] or any(summary[k] != 0 for k in ("ResidentAllocationBytes", "IngressAllocationBytes", "ExportAllocationBytes")):
            raise ValueError("Workload parity/allocation gate failed")
        if summary["Overhead"]["Status"] != 1 or summary["Overhead"]["Repetitions"] != document["Pairs"]:
            raise ValueError("Missing measured on/off overhead")
    if counts["failed"] or (require_collected and (counts["unavailable"] or not counts["collected"])):
        raise ValueError("Actual counter gate unmet")
    expected_gate = "unavailable" if counts["unavailable"] else "passed-process-cycles-only"
    if document["ActualCounterGate"] != expected_gate:
        raise ValueError("Actual counter gate disagrees with raw evidence")
    return {"candidates": len(definitions), "pairs": len(pairs), **counts, "gate": expected_gate}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("evidence", type=Path)
    parser.add_argument("--allow-unavailable", action="store_true")
    args = parser.parse_args()
    print(json.dumps(validate(args.evidence, not args.allow_unavailable), indent=2))
