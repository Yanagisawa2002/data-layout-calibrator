"""Explicit foreground discovery, frozen calibration, or independent confirmation.

Run under exclusive_stage.py. There is no scheduler or automatic hardware queue.
All candidate choices are frozen by the actual C# package API before confirmation.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import statistics
import subprocess
import time
from prepare_sources import ROOT, HERE, digest
from build_native import NAMES
from workflow import run, case_key

VALIDATED = {}


def save(path, value):
    path.write_text(json.dumps(value, indent=2)+"\n", encoding="utf-8")


def utc():
    return datetime.now(timezone.utc).isoformat()


def frozen_files(build, selector, protocol):
    paths = list((ROOT/"Packages/com.yanagisawa.data-layout-calibrator/Runtime").glob("*.cs"))
    paths += [p for p in HERE.rglob("*") if p.is_file() and p.suffix in (".cpp", ".hpp", ".py", ".cs", ".csproj", ".json")
              and not any(x in p.parts for x in ("bin", "obj", "__pycache__"))]
    paths += [p for p in build.rglob("*") if p.is_file() and (p.suffix in (".exe", ".hpp", ".cpp", ".json") or p.name in ("simpleph", "storage_contract"))]
    paths += [p for p in selector.parent.glob("*") if p.is_file()]
    paths.append(protocol)
    return {str(p.resolve()): digest(p) for p in sorted(set(paths))}


def check_freeze(freeze):
    for path, value in freeze["files"].items():
        if digest(path) != value:
            raise ValueError(f"Frozen file changed: {path}")


def sample(record, candidate, pair, partition, fingerprint, contract):
    return dict(ProcessIdentity=record["processIdentity"], PairId=f"pair-{pair:02d}", CandidateId=candidate,
                PartitionId=partition, DatasetHash=record["datasetHash"], SourceFingerprint=fingerprint,
                ContractId=contract, Observed=True, Completed=True, ParityPassed=True,
                IncludesCompleteBoundary=True, EnvironmentQualified=record["performanceEnvironmentEligible"],
                Diagnostic=False, CompleteMilliseconds=record["completeTaskMs"])


def compare(records):
    if len({x["fullOutputHash"] for x in records}) != 1 or len({x["datasetHash"] for x in records}) != 1:
        raise ValueError("Complete input, checkpoint, VTU or consumed output parity failed")
    if all("outputDirectory" in x for x in records):
        base = Path(records[0]["outputDirectory"])
        paths = [p for p in records[0]["files"] if p.endswith(".vtu") or p in ("input.bin", "state.bin", "velocity-profile.csv")]
        for item in records[1:]:
            other = Path(item["outputDirectory"])
            for name in paths:
                if (base/name).read_bytes() != (other/name).read_bytes():
                    raise ValueError(f"Full persisted-byte mismatch: {other/name}")


def executable(build, candidate):
    return build/candidate/("simpleph.exe" if os.name == "nt" else "simpleph")


def call_selector(selector, request, output):
    command = ([shutil.which("dotnet"), str(selector)] if selector.suffix == ".dll" else [str(selector)])
    subprocess.run(command+[str(request), str(output)], check=True,
                   creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)


def require_qualified_performance(record):
    if record["mode"] == "performance" and not record["native"]["diagnostic"] and not record["performanceEnvironmentEligible"]:
        raise RuntimeError("During-task resource qualification failed; preserve the entire attempt without a performance claim")


def cohort(records, names, rounds, drift_limit):
    reasons = []
    if len(records) != len(names)*rounds or len({r["processIdentity"] for r in records}) != len(records):
        reasons.append("missing-or-reused-processes")
    if not all(r["performanceEnvironmentEligible"] for r in records):
        reasons.append("ineligible-environment")
    for name in names:
        arm = [r for r in records if r["role"] == name]
        if sorted(r["pair"] for r in arm) != list(range(rounds)):
            reasons.append("unbalanced-pairs/"+name)
        positions = [sum(r["position"] == p for r in arm) for p in range(len(names))]
        if len(set(positions)) != 1:
            reasons.append("unbalanced-order/"+name)
    baseline = sorted((r for r in records if r["role"] == "tuned-aos"), key=lambda r:r["pair"])
    drift = None
    if len(baseline) >= 6:
        midpoint = len(baseline)//2
        first = statistics.mean(r["completeTaskMs"] for r in baseline[:midpoint])
        last = statistics.mean(r["completeTaskMs"] for r in baseline[midpoint:])
        drift = 100*(last-first)/first
        if abs(drift) > drift_limit:
            reasons.append("fixed-baseline-drift")
    else:
        reasons.append("insufficient-drift-coverage")
    return dict(passed=not reasons, reasons=reasons, baselineHalfToHalfDriftPercent=drift,
                maximumAbsoluteDriftPercent=drift_limit, discardedProcesses=0)


def validate_protocol(protocol):
    candidates = protocol["selectorCandidates"]
    if len(set(candidates)) != len(candidates) or "tuned-aos" not in candidates or not set(candidates) <= set(NAMES):
        raise ValueError("Invalid frozen candidate set")
    if protocol["calibrationArms"] != candidates:
        raise ValueError("Calibration may only charge and rank its real frozen candidate set")
    fixed = protocol["confirmationFixedArms"]
    if fixed != NAMES:
        raise ValueError("Complete original/conventional/external confirmation coverage required")
    if (protocol["calibrationRounds"] < 6 or protocol["calibrationRounds"] % len(candidates) or
        protocol["confirmationRounds"] < 6 or protocol["confirmationRounds"] % (len(fixed)+1)):
        raise ValueError("Frozen rounds must cover balanced process positions and drift groups")
    if len(protocol["cells"]) != 5 or len({c["id"] for c in protocol["cells"]}) != 5:
        raise ValueError("Expected the five reviewed task scenarios")
    for cell in protocol["cells"]:
        a, b = cell["calibration"], cell["confirmation"]
        if a["resolution"] == b["resolution"] or a["re"] == b["re"] or a["phase"] == b["phase"]:
            raise ValueError("Confirmation must use independent resolution, Reynolds and input positions")
        for case in (a, b):
            if not (12 <= case["resolution"] <= 64 and 1 <= case["steps"] <= 4096 and case["exportEvery"] >= 1):
                raise ValueError("Task scale exceeds reviewed bounded scope")
    if not 0 < protocol["maximumBaselineDriftPercent"] <= 5:
        raise ValueError("Invalid preregistered drift gate")


def one(build, candidate, case, output, freeze=None, correctness=False):
    if freeze:
        check_freeze(freeze)
    snapshots=(case["steps"]+case["exportEvery"]-1)//case["exportEvery"]+1
    reserve=10*2**30+case["resolution"]*(case["resolution"]+8)*snapshots*1024+2**20
    if shutil.disk_usage(build).free < reserve:
        raise RuntimeError("Per-task worst-case output budget would cross the 10 GiB data reserve")
    print(json.dumps(dict(event="start", output=str(output), candidate=candidate, case=case, time=utc())), flush=True)
    validated = None if correctness else VALIDATED[case_key(case)][candidate]
    value = run(executable(build, candidate), case, output, correctness=correctness, validated=validated)
    require_qualified_performance(value)
    print(json.dumps(dict(event="finish", output=str(output), completeTaskMs=value["completeTaskMs"],
                          nativeMs=value["native"]["nativeLifecycleMs"], time=utc())), flush=True)
    return value


def main(args):
    global VALIDATED
    output, build = args.output.resolve(), args.build.resolve()
    output.mkdir(parents=True, exist_ok=False)
    if args.phase == "correctness":
        cases = json.loads(args.cases.read_text())
        receipts = {}
        for case in cases:
            records = []
            for name in NAMES+["diagnostic-original", "diagnostic-aos"]:
                value = one(build, name, case, output/case["id"]/name, correctness=True)
                value["role"] = name
                records.append(value)
            compare(records)
            receipts[case_key(case)] = {r["role"]: {k:r[k] for k in (
                "caseKey", "datasetHash", "taskOutputHash", "executableSha256", "outputDirectory")} for r in records}
            save(output/"receipts-progress.json", receipts)
        save(output/"completed.json", dict(completed=True, cases=receipts, mode="correctness-only; never selector timing"))
        return
    if not args.correctness:
        raise ValueError("Separate exact-input/output correctness receipts required")
    correct = json.loads((args.correctness/"completed.json").read_text())
    if correct.get("completed") is not True:
        raise ValueError("Correctness campaign incomplete")
    VALIDATED = correct["cases"]
    if args.phase == "discovery":
        cases = json.loads(args.cases.read_text()) if args.cases else [dict(id="discovery-small", resolution=13, steps=8, exportEvery=4, re=.1, phase=0),
                 dict(id="discovery-medium", resolution=24, steps=64, exportEvery=16, re=.1, phase=0),
                 dict(id="discovery-large", resolution=48, steps=64, exportEvery=16, re=.1, phase=0),
                 dict(id="discovery-export", resolution=24, steps=64, exportEvery=1, re=.1, phase=0)]
        records = []
        for case in cases:
            cell = []
            for name in NAMES:
                value = one(build, name, case, output/case["id"]/name)
                value["role"] = name; cell.append(value)
            compare(cell); records.extend(cell)
        for name in ("diagnostic-original", "diagnostic-aos"):
            one(build, name, cases[2], output/"diagnostics"/name)
        save(output/"summary.json", dict(status="complete", records=records, performanceInference="discovery-only"))
        return
    protocol = json.loads(args.protocol.read_text())
    validate_protocol(protocol)
    if os.name != "nt" and sorted(os.sched_getaffinity(0)) != protocol["cpuAffinity"]:
        raise ValueError("Actual CPU affinity differs from the frozen discovery/protocol resource")
    if not args.physics or not args.budget:
        raise ValueError("Formal phases require completed physical gates and measured matrix budget")
    budget=json.loads(args.budget.read_text())
    if not budget["withinReviewedLimits"]:
        raise ValueError("Formal matrix exceeds the reviewed time/disk budget")
    physical_files=[]
    for name in NAMES:
        path=args.physics/("physics-"+name+".json")
        physical=json.loads(path.read_text())
        if (not physical["passed"] or physical["executableSha256"] != digest(executable(build,name)) or
            physical["validatorSha256"] != digest(HERE/"physics_check.py")):
            raise ValueError("Physical qualification does not cover the current executable/validator")
        physical_files.append(path)
    if args.phase == "calibration":
        files = frozen_files(build, args.selector.resolve(), args.protocol.resolve())
        files[str((args.correctness/"completed.json").resolve())] = digest(args.correctness/"completed.json")
        for path in physical_files+[args.budget]:
            files[str(path.resolve())]=digest(path)
        source_identity = json.loads((ROOT/"SOURCE_IDENTITY.json").read_text()) if (ROOT/"SOURCE_IDENTITY.json").exists() else dict(
            baseHead=subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
            worktreeStatus=subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True))
        freeze = dict(schemaVersion=1, frozenUtc=utc(), sourceIdentity=source_identity, files=files,
                      protocolSha256=digest(args.protocol), scope="native-cpp-plus-csharp-timing-decision")
        fingerprint = hashlib.sha256(json.dumps(files, sort_keys=True).encode()).hexdigest()
        freeze["fingerprint"] = fingerprint; save(output/"freeze.json", freeze)
        decisions = {}
        for cell in protocol["cells"]:
            case = cell["calibration"]
            records, samples = [], []
            start = time.perf_counter()
            # Only real selectable candidates enter and are charged to calibration.
            # The original application remains a confirmation/diagnosis reference.
            names = protocol["calibrationArms"]
            for pair in range(protocol["calibrationRounds"]):
                order = names[pair % len(names):] + names[:pair % len(names)]
                current = []
                for position, name in enumerate(order):
                    value = one(build, name, case, output/cell["id"]/f"pair-{pair:02d}"/name, freeze)
                    value.update(pair=pair, position=position, role=name, partition="calibration", cellId=cell["id"])
                    current.append(value)
                compare(current)
                for value in current:
                    if value["role"] in protocol["selectorCandidates"]:
                        samples.append(sample(value, value["role"], pair, "calibration/"+cell["id"], fingerprint, cell["id"]))
                records.extend(current)
                save(output/cell["id"]/"records-progress.json", records)
            qualification = cohort(records, names, protocol["calibrationRounds"], protocol["maximumBaselineDriftPercent"])
            save(output/cell["id"]/"cohort.json", qualification)
            if not qualification["passed"]:
                raise RuntimeError("Whole calibration cohort failed; no records discarded and no selection frozen")
            request = dict(Samples=samples, CandidateIds=protocol["selectorCandidates"], BaselineId="tuned-aos", PartitionId="calibration/"+cell["id"])
            request_path = output/cell["id"]/"selector-request.json"
            decision_path = output/cell["id"]/"selector-decision.json"
            save(request_path, request)
            decision_start = time.perf_counter()
            call_selector(args.selector.resolve(), request_path, decision_path)
            decision_host_ms = (time.perf_counter()-decision_start)*1000
            charge_ms = (time.perf_counter()-start)*1000
            decision = json.loads(decision_path.read_text())
            selected = decision["decision"]["CandidateId"]
            if selected not in protocol["selectorCandidates"]:
                raise ValueError("Selector returned a nonfrozen candidate")
            decisions[cell["id"]] = dict(candidate=selected, decision=decision, decisionSha256=digest(decision_path),
                calibrationAndDecisionWallMs=charge_ms, decisionHostWallMs=decision_host_ms,
                sumCalibrationTaskMs=sum(r["completeTaskMs"] for r in records),
                calibrationDatasetHash=records[0]["datasetHash"])
            save(output/cell["id"]/"records.json", records)
            save(output/"decisions-progress.json", decisions)
        # The confirmation command refuses a partial calibration campaign.
        save(output/"frozen-decisions.json", dict(completed=True, frozenUtc=utc(), fingerprint=fingerprint, cells=decisions))
        return
    freeze = json.loads((args.calibration/"freeze.json").read_text())
    decisions_path = args.calibration/"frozen-decisions.json"
    decisions = json.loads(decisions_path.read_text())
    decision_hash = digest(decisions_path)
    if decisions.get("completed") is not True or decisions["fingerprint"] != freeze["fingerprint"]:
        raise ValueError("No completed frozen calibration")
    check_freeze(freeze)
    if digest(args.protocol) != freeze["protocolSha256"]:
        raise ValueError("Changed frozen protocol")
    for cell in protocol["cells"]:
        case = cell["confirmation"]
        records = []
        names = protocol["confirmationFixedArms"] + ["selected"]
        for pair in range(protocol["confirmationRounds"]):
            order = names[pair % len(names):] + names[:pair % len(names)]
            current = []
            for position, role in enumerate(order):
                if digest(decisions_path) != decision_hash:
                    raise ValueError("Frozen decision changed during confirmation")
                candidate = decisions["cells"][cell["id"]]["candidate"] if role == "selected" else role
                value = one(build, candidate, case, output/cell["id"]/f"pair-{pair:02d}"/role, freeze)
                if value["datasetHash"] == decisions["cells"][cell["id"]]["calibrationDatasetHash"]:
                    raise ValueError("Confirmation reused calibration input bytes")
                value.update(pair=pair, position=position, role=role, partition="confirmation", cellId=cell["id"],
                             selectionFrozenSha256=decision_hash, executedCandidate=candidate)
                current.append(value)
            compare(current); records.extend(current)
            save(output/cell["id"]/"records-progress.json", records)
        save(output/cell["id"]/"records.json", records)
        qualification = cohort(records, names, protocol["confirmationRounds"], protocol["maximumBaselineDriftPercent"])
        save(output/cell["id"]/"cohort.json", qualification)
        if not qualification["passed"]:
            raise RuntimeError("Whole confirmation cohort failed; retain all data without declaring a gain")
        frozen = decisions["cells"][cell["id"]]["decision"]["decision"]
        def confirmation_samples(role):
            return [sample(r, r["executedCandidate"], r["pair"], "confirmation/"+cell["id"], freeze["fingerprint"], cell["id"])
                    for r in records if r["role"] == role]
        request = dict(FrozenDecision=frozen, ConfirmationBaseline=confirmation_samples("tuned-aos"),
                       ConfirmationExecuted=confirmation_samples("selected"))
        request_path = output/cell["id"]/"confirmation-request.json"
        save(request_path, request)
        call_selector(args.selector.resolve(), request_path, output/cell["id"]/"confirmation-decision.json")
    save(output/"completed.json", dict(completed=True, completedUtc=utc(), selectionFrozenSha256=decision_hash, fingerprint=freeze["fingerprint"]))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--phase", choices=["correctness", "discovery", "calibration", "confirmation"], required=True)
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--protocol", type=Path)
    parser.add_argument("--selector", type=Path)
    parser.add_argument("--calibration", type=Path)
    parser.add_argument("--correctness", type=Path)
    parser.add_argument("--cases", type=Path)
    parser.add_argument("--physics", type=Path)
    parser.add_argument("--budget", type=Path)
    arguments = parser.parse_args()
    try:
        main(arguments)
    except Exception as error:
        if arguments.output.is_dir():
            save(arguments.output/"FAILED.json", dict(completed=False, error=str(error), phase=arguments.phase, time=utc(), retained=True))
        raise
