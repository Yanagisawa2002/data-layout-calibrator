"""One prospectively fixed, complete-task comparison using existing validated binaries."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import random
import statistics
import sys


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def percentile(values, p):
    index = (len(values) - 1) * p
    lo, hi = math.floor(index), math.ceil(index)
    return values[lo] + (values[hi] - values[lo]) * (index - lo)


def analyze(protocol, records):
    names = protocol["arms"]
    values = {n: [r["completeTaskMs"] for r in records if r["role"] == n] for n in names}
    if any(len(v) != protocol["rounds"] for v in values.values()):
        raise ValueError("Incomplete cohort")
    eligible = all(r["performanceEnvironmentEligible"] for r in records)
    baseline = values["tuned-aos"]
    half = len(baseline) // 2
    drift = 100 * (statistics.mean(baseline[half:]) / statistics.mean(baseline[:half]) - 1)
    gate = (eligible or not protocol.get("requireQuietCpu", True)) and abs(drift) <= protocol["maximumBaselineDriftPercent"]
    comparisons = []
    tests = len(protocol["candidates"]) * len(protocol["primaryComparators"])
    tail = protocol["familyAlpha"] / tests / 2
    rng = random.Random(protocol["bootstrapSeed"])
    indices = [[rng.randrange(len(baseline)) for _ in baseline]
               for _ in range(protocol["bootstrapReplicates"])]
    for candidate in protocol["candidates"]:
        a = values[candidate]
        for comparator in protocol["primaryComparators"]:
            b = values[comparator]
            ratios = sorted(sum(b[i] for i in sample) / sum(a[i] for i in sample) for sample in indices)
            ratio = statistics.mean(b) / statistics.mean(a)
            lower, upper = percentile(ratios, tail), percentile(ratios, 1 - tail)
            gain = 100 * (1 - 1 / ratio)
            comparisons.append(dict(candidate=candidate, comparator=comparator,
                                    speedup=ratio, latencyReductionPercent=gain,
                                    adjustedInterval=[lower, upper],
                                    passes=gate and lower > 1 and gain >= protocol["minimumGainPercent"]))
    summaries = {}
    for name in names:
        arm = [r for r in records if r["role"] == name]
        summaries[name] = dict(meanMs=statistics.mean(values[name]), medianMs=statistics.median(values[name]),
                               minMs=min(values[name]), maxMs=max(values[name]),
                               nativeProcessMeanMs=statistics.mean(r["nativeProcessWallMs"] for r in arm),
                               consumerMeanMs=statistics.mean(r["consumerMs"] for r in arm))
    winners = [n for n in protocol["candidates"] if all(c["passes"] for c in comparisons if c["candidate"] == n)]
    return dict(completed=True, qualifiedEnvironment=eligible, baselineDriftPercent=drift,
                measurementEnvironment="quiet-gated" if protocol.get("requireQuietCpu", True) else "shared-host-observed",
                quietGatePassedProcesses=sum(r["performanceEnvironmentEligible"] for r in records),
                cohortPasses=gate, arms=summaries, comparisons=comparisons,
                candidatesBeatingAllPrimaryComparators=winners, discardedProcesses=0,
                note="Measured complete caller latency; native and consumer partitions are descriptive, not summed percentiles. No selector calibration or reuse claim.")


def main(args):
    sys.path.insert(0, str(args.source / "Tools/WholeTask"))
    from workflow import run, case_key, sha
    from physics_check import check
    protocol = json.loads(args.protocol.read_text())
    names, rounds = protocol["arms"], protocol["rounds"]
    if rounds < 12 or rounds % len(names) or len(set(names)) != len(names):
        raise ValueError("Require at least 12 balanced rounds")
    args.output.mkdir(parents=True, exist_ok=False)
    correctness = json.loads((args.correctness / "completed.json").read_text())
    if correctness.get("completed") is not True:
        raise ValueError("Correctness campaign incomplete")
    receipts = correctness["cases"][case_key(protocol["case"])]
    physical = {}
    for name in names:
        if receipts[name]["executableSha256"] != sha(args.build / name / "simpleph"):
            raise ValueError("Changed executable")
        physical[name] = check(Path(receipts[name]["outputDirectory"]))
        if not physical[name]["passed"]:
            raise ValueError("Physical acceptance failed")
    rng = random.Random(protocol["orderSeed"])
    order = names.copy()
    rng.shuffle(order)
    offsets = list(range(len(names))) * (rounds // len(names))
    rng.shuffle(offsets)
    orders = [order[offset:] + order[:offset] for offset in offsets]
    save(args.output / "frozen.json", dict(protocol=protocol, orders=orders, physical=physical,
         startedUtc=datetime.now(timezone.utc).isoformat(), cpuAffinity=sorted(os.sched_getaffinity(0)),
         protocolSha256=sha(args.protocol), runnerSha256=sha(Path(__file__)),
         workflowSha256=sha(args.source / "Tools/WholeTask/workflow.py"),
         executableHashes={n: receipts[n]["executableSha256"] for n in names}))
    records = []
    try:
        for pair, sequence in enumerate(orders):
            for position, name in enumerate(sequence):
                result = run(args.build / name / "simpleph", protocol["case"],
                             args.output / f"round-{pair:02d}" / name, validated=receipts[name],
                             require_quiet_cpu=protocol.get("requireQuietCpu", True))
                result.update(role=name, pair=pair, position=position)
                records.append(result)
                save(args.output / "records.json", records)
                if protocol.get("requireQuietCpu", True) and not result["performanceEnvironmentEligible"]:
                    raise RuntimeError("Resource qualification failed; entire attempt retained, no retry")
            print(json.dumps(dict(completedRounds=pair + 1, plannedRounds=rounds)), flush=True)
        summary = analyze(protocol, records)
        save(args.output / "summary.json", summary)
        print(json.dumps(summary), flush=True)
    except Exception as error:
        save(args.output / "summary.json", dict(completed=False, startedAndRecorded=len(records),
             error=str(error), discardedProcesses=0, performanceClaim=False))
        raise


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("source", "build", "correctness", "output", "protocol"):
        parser.add_argument("--" + name, type=Path, required=True)
    main(parser.parse_args())
