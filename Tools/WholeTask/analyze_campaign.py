"""Paired process uncertainty and fully charged, actually executed selected policy."""
import argparse
import json
import math
from pathlib import Path
import random
import statistics
from campaign import check_freeze, digest, validate_protocol


def interval(baseline, selected, iterations=10000):
    if len(baseline) != len(selected) or len(baseline) < 6:
        raise ValueError("Paired process uncertainty requires equal independent samples")
    rng=random.Random(0x71A94035)
    gains=[]
    for _ in range(iterations):
        picks=[rng.randrange(len(baseline)) for _ in baseline]
        b=sum(baseline[i] for i in picks); s=sum(selected[i] for i in picks)
        gains.append(100*(b-s)/b)
    gains.sort()
    return dict(improvementPercent=100*(sum(baseline)-sum(selected))/sum(baseline),
                lower95=gains[int(.025*(iterations-1))],upper95=gains[int(.975*(iterations-1))],
                lower99=gains[int(.005*(iterations-1))],upper99=gains[int(.995*(iterations-1))],
                independentPairs=len(baseline),method="paired fresh-process bootstrap of ratio of means; fixed seed; no tick resampling")


def costs(baseline, selected, calibration_ms):
    if len(baseline) != len(selected) or not baseline or calibration_ms < 0:
        raise ValueError("Missing measured use or calibration cost")
    rows=[]
    for uses in sorted({1,len(baseline)}):
        fixed=sum(baseline[:uses]); chosen=calibration_ms+sum(selected[:uses])
        rows.append(dict(actualUses=uses,fixedTaskMs=fixed,calibrationAndDecisionMs=calibration_ms,
                         actualSelectedTaskMs=sum(selected[:uses]),selectedTotalMs=chosen,
                         netSavingsMs=fixed-chosen,netImprovementPercent=100*(fixed-chosen)/fixed,
                         costRecovered=chosen<fixed))
    saving=statistics.mean(baseline)-statistics.mean(selected)
    return dict(measured=rows,modeledBreakEvenCalls=math.ceil(calibration_ms/saving) if saving>0 else None,
                modelNote="Break-even is an extrapolation only; actual use totals above are measured executions.")


def summarize(args):
    protocol=json.loads(args.protocol.read_text()); validate_protocol(protocol)
    freeze=json.loads((args.calibration/"freeze.json").read_text()); check_freeze(freeze)
    decisions_path=args.calibration/"frozen-decisions.json"
    decisions=json.loads(decisions_path.read_text())
    completion=json.loads((args.confirmation/"completed.json").read_text())
    if not completion["completed"] or completion["selectionFrozenSha256"] != digest(decisions_path) or digest(args.protocol) != freeze["protocolSha256"]:
        raise ValueError("Incomplete confirmation or changed frozen decision/protocol")
    report=dict(schemaVersion=1,sourceFingerprint=freeze["fingerprint"],cells=[],scope="this CPU/Linux/native application and C# selection host only",
                allocationEligibility="Unknown",deploymentEligibility="Unknown",gpuTimeMs=None)
    suite={name:[0.0]*protocol["confirmationRounds"] for name in protocol["confirmationFixedArms"]+["selected"]}
    suite_calibration=0.0
    for cell in protocol["cells"]:
        cell_id=cell["id"]
        qualification=json.loads((args.confirmation/cell_id/"cohort.json").read_text())
        if not qualification["passed"]:
            raise ValueError("Entire cohort ineligible; no performance summary may select a subset")
        records=json.loads((args.confirmation/cell_id/"records.json").read_text())
        values={name:[r["completeTaskMs"] for r in sorted(records,key=lambda r:r["pair"]) if r["role"]==name] for name in suite}
        if not all(len(v)==protocol["confirmationRounds"] for v in values.values()):
            raise ValueError("Missing fixed or selected execution")
        decision=decisions["cells"][cell_id]
        charge=decision["calibrationAndDecisionWallMs"]; suite_calibration+=charge
        for name,series in values.items():
            suite[name]=[a+b for a,b in zip(suite[name],series)]
        fixed={name:dict(meanCompleteTaskMs=statistics.mean(series),processStdDevMs=statistics.stdev(series),
                        independentProcesses=len(series)) for name,series in values.items()}
        comparisons={name:interval(values[name],values["selected"]) for name in protocol["confirmationFixedArms"]}
        timing=comparisons["tuned-aos"]
        confirmed=json.loads((args.confirmation/cell_id/"confirmation-decision.json").read_text())
        report["cells"].append(dict(id=cell_id,case=cell["confirmation"],frozenCandidate=decision["candidate"],
            fixedAndSelected=fixed,selectedVersusFixed=comparisons,cohort=qualification,
            apiConfirmation=confirmed,calibrationAndDecisionWallMs=charge,
            decisionHostWallMs=decision["decisionHostWallMs"],
            totalCost=costs(values["tuned-aos"],values["selected"],charge),
            familyAdjustedTimingGate=timing["lower99"]>0 and timing["improvementPercent"]>=2 and confirmed["decision"]["ConfirmedTimingGain"],
            confidenceNote="95% intervals are pointwise; cell conclusions use 99% bounds for five scenario comparisons (Bonferroni)."))
    report["equalWeightTaskSuite"]=dict(taskCallsPerRepetition=len(protocol["cells"]),
        selectedVersusFixed={name:interval(suite[name],suite["selected"]) for name in protocol["confirmationFixedArms"]},
        totalCost=costs(suite["tuned-aos"],suite["selected"],suite_calibration))
    report["costAccountingNote"]="Calibration charge is the actual wall time of the complete implemented calibration caller, including all candidate calls, per-task gates/audits, and decision-host startup/control. Discovery, external descriptive arms, separate correctness, confirmation analysis and experimental cold-controller startup are not charged to deployment; no measured calibration overhead is subtracted. Selected calls are extra real processes, never a hindsight fixed-arm result."
    args.output.mkdir(parents=True,exist_ok=False)
    (args.output/"summary.json").write_text(json.dumps(report,indent=2)+"\n")
    lines=["# Complete-task layout results", "", "CPU/Linux native SimplePH + actual C# timing selector; allocation/deployment remain Unknown.", "",
           "| Scenario | Frozen candidate | Tuned AoS mean ms | Executed selected mean ms | Selected gain %, 95% CI | Net cost recovered at measured reuse |",
           "|---|---|---:|---:|---|---|"]
    for cell in report["cells"]:
        comp=cell["selectedVersusFixed"]["tuned-aos"]; means=cell["fixedAndSelected"]
        lines.append(f'| {cell["id"]} | {cell["frozenCandidate"]} | {means["tuned-aos"]["meanCompleteTaskMs"]:.3f} | {means["selected"]["meanCompleteTaskMs"]:.3f} | {comp["improvementPercent"]:.2f} [{comp["lower95"]:.2f}, {comp["upper95"]:.2f}] | {cell["totalCost"]["measured"][-1]["costRecovered"]} |')
    lines += ["",report["costAccountingNote"],"", "All arms, pointwise/family-adjusted uncertainty and first-use/actual-reuse totals are in summary.json. Modeled break-even is not an observed payoff."]
    (args.output/"results.md").write_text("\n".join(lines)+"\n")
    print(json.dumps(dict(completed=True,output=str(args.output))))


if __name__ == "__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ("protocol","calibration","confirmation","output"):
        parser.add_argument("--"+name,type=Path,required=True)
    summarize(parser.parse_args())
