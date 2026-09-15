"""One bounded real caller: qualify -> calibrate -> freeze -> consume -> account.

Uses the existing C# selector and native SPH paths without expanding discovery.
Launch under the real campaign flock and an outer process-group hard timeout.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import statistics
import time

from campaign import call_selector, cohort, compare, sample
from physics_check import check
from workflow import run, sha


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n")


def ledger(baseline, selected, upfront_ms, confirmed, same_candidate=False):
    """Only observed uses can claim recovery; extrapolation is separately named."""
    if (len(baseline) != len(selected) or not baseline or upfront_ms < 0 or
            any(not math.isfinite(x) or x <= 0 for x in baseline + selected) or
            not math.isfinite(upfront_ms)):
        raise ValueError("Incomplete finite cost evidence")
    observed = []
    for count in range(1, len(baseline) + 1):
        fixed = sum(baseline[:count])
        automatic = upfront_ms + sum(selected[:count])
        observed.append(dict(uses=count, fixedAosMs=fixed, automaticMs=automatic,
                             measuredCostRecovered=confirmed and not same_candidate and automatic < fixed))
    saving = statistics.mean(baseline) - statistics.mean(selected)
    modeled = math.ceil(upfront_ms / saving) if confirmed and not same_candidate and saving > 0 else None
    return dict(observedUses=observed, upfrontMs=upfront_ms,
                modeledBreakEvenUses=modeled,
                modeledBoundary="Conditional stationary mean extrapolation; never observed payback",
                confirmedTimingGain=confirmed, sameCandidate=same_candidate)


def configured_candidate(decision_path, expected_hash, fingerprint, arms):
    if sha(decision_path) != expected_hash:
        raise ValueError("Frozen selection changed")
    decision = json.loads(decision_path.read_text())["decision"]
    if decision["SourceFingerprint"] != fingerprint or decision["CandidateId"] not in arms:
        raise ValueError("Selection fingerprint/candidate mismatch")
    return decision["CandidateId"]


def main(args):
    protocol = json.loads(args.protocol.read_text())
    if protocol["arms"] != ["tuned-aos", "field-aosoa8"] or protocol["rounds"] != 6:
        raise ValueError("This entry implements exactly one two-arm six-pair protocol")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    arms = protocol["arms"]
    executables = {n: args.build.resolve() / n / "simpleph" for n in arms}
    binaries = {n: sha(p) for n, p in executables.items()}
    identity = {"executables": binaries, "selector": sha(args.selector), "protocol": sha(args.protocol),
                "runner": sha(Path(__file__)), "source": sha(args.source / "SOURCE_IDENTITY.json")}
    fingerprint = hashlib.sha256(json.dumps(identity, sort_keys=True).encode()).hexdigest()
    save(output / "freeze.json", dict(fingerprint=fingerprint, identity=identity, protocol=protocol,
         startedUtc=datetime.now(timezone.utc).isoformat(), affinity=sorted(os.sched_getaffinity(0))))
    records = []
    summary = dict(completed=False, outcome="FAILED", performanceClaim=False,
                   allocationEligibility="Unknown", unityIl2cpp="SKIPPED: Linux .NET is not Unity IL2CPP")
    started = time.perf_counter()

    def resources():
        if shutil.disk_usage(output).free < 30 * 2**30:
            raise RuntimeError("30 GiB disk reserve crossed")
        maximum = Path("/sys/fs/cgroup/memory.max").read_text().strip()
        if maximum != "max" and int(maximum) - int(Path("/sys/fs/cgroup/memory.current").read_text()) < 8 * 2**30:
            raise RuntimeError("8 GiB memory headroom crossed")
        if {n: sha(p) for n, p in executables.items()} != binaries:
            raise ValueError("Frozen native binary changed")
        if sha(args.selector) != identity["selector"]:
            raise ValueError("Frozen selector changed")

    def qualify(partition):
        values = {}
        for name in arms:
            resources()
            location = output / "correctness" / partition / name
            value = run(executables[name], protocol[partition], location, correctness=True)
            physical = check(location)
            save(location / "physics.json", physical)
            if not physical["passed"]:
                raise RuntimeError("Full-particle physical acceptance failed")
            values[name] = value
        compare(list(values.values()))
        return values

    def observe(partition, pair, position, role, validated, decision_path=None, decision_hash=None):
        start = time.perf_counter()
        resources()
        name = configured_candidate(decision_path, decision_hash, fingerprint, arms) if role == "selected" else role
        value = run(executables[name], protocol[partition], output / partition / f"pair-{pair:02d}" / role,
                    validated=validated[name])
        value.update(callerMs=(time.perf_counter() - start) * 1000, role=role, executedCandidate=name,
                     pair=pair, position=position, partition=partition)
        records.append(value)
        save(output / "records.json", records)
        print(json.dumps(dict(partition=partition, pair=pair, role=role, executed=name,
                              completeTaskMs=value["completeTaskMs"], callerMs=value["callerMs"],
                              environmentPassed=value["performanceEnvironmentEligible"])), flush=True)
        if not value["performanceEnvironmentEligible"]:
            raise RuntimeError("Per-task resource gate failed; no retry")
        return value

    def dto(value):
        result = sample(value, value["executedCandidate"], value["pair"], value["partition"], fingerprint, protocol["id"])
        result["CompleteMilliseconds"] = value["callerMs"]
        return result

    def select(request, name):
        request_path, decision_path = output / (name + "-request.json"), output / (name + "-decision.json")
        save(request_path, request)
        call_selector(args.selector, request_path, decision_path)
        result = json.loads(decision_path.read_text())
        control = result["actualDotNetCurrentThreadControl"]
        if result["controlFailure"] or not control["PositiveControlPassed"] or not control["EmptyControlPassed"]:
            raise RuntimeError("Actual .NET allocation positive/empty control failed")
        if result["decisionCurrentThreadManagedBytes"] is None:
            raise RuntimeError("Actual selector allocation measurement unavailable")
        return result, decision_path

    try:
        validated = qualify("calibration")
        for pair in range(protocol["rounds"]):
            order = arms[pair % 2:] + arms[:pair % 2]
            for position, name in enumerate(order):
                observe("calibration", pair, position, name, validated)
        calibration = list(records)
        # Cohort qualification uses the same complete monitored caller boundary.
        qualified = cohort([dict(r, completeTaskMs=r["callerMs"]) for r in calibration], arms, protocol["rounds"], protocol["maximumBaselineDriftPercent"])
        save(output / "calibration-cohort.json", qualified)
        if not qualified["passed"]:
            raise RuntimeError("Calibration cohort failed; no selection or retry")
        decision, decision_path = select(dict(Samples=[dto(r) for r in calibration], CandidateIds=arms,
                                             BaselineId="tuned-aos", PartitionId="calibration"), "calibration")
        upfront = (time.perf_counter() - started) * 1000
        summary.update(calibrationAndQualificationMs=upfront, selected=decision["decision"]["CandidateId"],
                       calibrationMeansMs={n: statistics.mean(r["callerMs"] for r in calibration if r["role"] == n) for n in arms},
                       decision=decision, calibrationTaskMs=sum(r["completeTaskMs"] for r in calibration))
        if not decision["decision"]["TimingGatePassed"]:
            summary.update(completed=True, outcome="NO_GO", reason="Calibration retained tuned AoS; stop before confirmation",
                           modeledBreakEvenUses=None, measuredPayback=False, confirmationExecuted=False)
            return
        decision_hash = sha(decision_path)
        validation_start = time.perf_counter()
        validated = qualify("confirmation")
        if validated["tuned-aos"]["datasetHash"] == calibration[0]["datasetHash"]:
            raise ValueError("Confirmation input reused calibration bytes")
        upfront += (time.perf_counter() - validation_start) * 1000
        roles = ["tuned-aos", "selected"]
        for pair in range(protocol["rounds"]):
            order = roles[pair % 2:] + roles[:pair % 2]
            for position, role in enumerate(order):
                observe("confirmation", pair, position, role, validated, decision_path, decision_hash)
        confirmation = [r for r in records if r["partition"] == "confirmation"]
        qualified = cohort([dict(r, completeTaskMs=r["callerMs"]) for r in confirmation], roles, protocol["rounds"], protocol["maximumBaselineDriftPercent"])
        save(output / "confirmation-cohort.json", qualified)
        if not qualified["passed"]:
            raise RuntimeError("Confirmation cohort failed; no claim or retry")
        validation_start = time.perf_counter()
        confirmation_result, _ = select(dict(FrozenDecision=decision["decision"],
            ConfirmationBaseline=[dto(r) for r in confirmation if r["role"] == "tuned-aos"],
            ConfirmationExecuted=[dto(r) for r in confirmation if r["role"] == "selected"]), "confirmation")
        upfront += (time.perf_counter() - validation_start) * 1000
        costs = ledger([r["callerMs"] for r in confirmation if r["role"] == "tuned-aos"],
                       [r["callerMs"] for r in confirmation if r["role"] == "selected"], upfront,
                       confirmation_result["decision"]["ConfirmedTimingGain"])
        save(output / "cost-ledger.json", costs)
        recovered = costs["observedUses"][-1]["measuredCostRecovered"]
        summary.update(completed=True, outcome="SUCCESS" if recovered else "NO_GO", measuredPayback=recovered,
                       confirmationExecuted=True, confirmation=confirmation_result, costs=costs,
                       reason="Observed complete caller cost, including all upfront calibration and qualification")
    except Exception as error:
        summary.update(reason=str(error))
        raise
    finally:
        summary.update(recordedProcesses=len(records), discardedProcesses=0,
                       routeWallMs=(time.perf_counter() - started) * 1000,
                       endedUtc=datetime.now(timezone.utc).isoformat())
        save(output / "summary.json", summary)
        print(json.dumps(summary), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("source", "build", "selector", "output", "protocol"):
        parser.add_argument("--" + name, type=Path, required=True)
    main(parser.parse_args())
