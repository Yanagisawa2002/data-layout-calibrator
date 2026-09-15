"""Create the five reviewed cells and budget them using completed discovery only.

Lifetimes are specified here before confirmation. This never chooses cases from
layout gains. A budget excess stops; it does not shrink a failed formal campaign.
"""
import argparse
import json
from pathlib import Path
from build_native import NAMES
from campaign import validate_protocol


def make(discovery):
    summary=json.loads((discovery/"summary.json").read_text())
    if summary["status"] != "complete" or summary["performanceInference"] != "discovery-only":
        raise ValueError("Completed separate discovery required")
    records=summary["records"]
    cells=[]
    for name,res,steps,every,source,scale in [
        ("small-short",12,8,4,"discovery-small",1),
        ("medium-long",24,256,64,"discovery-medium",4),
        ("large-long",48,256,64,"discovery-large",4),
        ("medium-every-step",24,256,1,"discovery-export",4),
        ("large-short",48,8,4,"discovery-large",1)]:
        def case(partition,resolution,re,phase):
            return dict(id=name+"-"+partition,resolution=resolution,steps=steps,exportEvery=every,re=re,phase=phase)
        matches=[r for r in records if r["case"]["id"]==source]
        if len(matches)!=len(NAMES):
            raise ValueError("Missing discovery baseline or external arm")
        # Conservative step scaling also scales fixed I/O, and a 1.5 margin
        # covers the held-out shape. All candidate observations enter the bound.
        worst_ms=max(r["completeTaskMs"] for r in matches)*scale*1.5
        worst_bytes=max(sum(f["bytes"] for f in r["files"].values()) for r in matches)*scale*1.5
        cells.append(dict(id=name,calibration=case("calibration",res,.1,0.),confirmation=case("confirmation",res+1,.13,.25),
                          budgetPerProcessMs=worst_ms+1100,budgetPerProcessBytes=int(worst_bytes+20000),discoveryCostSource=source))
    protocol=dict(schemaVersion=1,cells=cells,selectorCandidates=NAMES[1:],calibrationArms=NAMES[1:],
                  cpuAffinity=records[0]["cpuPreflight"].get("affinity"),
                  confirmationFixedArms=NAMES,calibrationRounds=10,confirmationRounds=14,
                  maximumBaselineDriftPercent=5,minimumPracticalImprovementPercent=2,
                  primaryMetric="caller-visible complete task milliseconds; ratio of paired fresh-process means",
                  taskBoundary="native process startup + input generation + ownership/conversion + all SPH steps and index maintenance + periodic and terminal VTU + storage disposal + real VTU/velocity-profile CSV consumer; correctness-only checkpoints excluded",
                  resourcePolicy="one fixed logical CPU and quiet SMT sibling; cgroup quota accounted separately; real host flock; no throttling; 10 GiB disk reserve",
                  failurePolicy="Stop and retain entire cohort on parity/source/shape failure, unbalanced/missing process, failed environment or >5% absolute baseline first-half/second-half drift. No slow-run exclusions.",
                  uncertainty="10000 paired bootstrap draws; pointwise 95% intervals; individual five-cell claims require 99% bounds; no tick-as-process inference",
                  cachePolicy="fresh process and output directory; ordinary OS caches retained; no global cache flush; first use refers to selector cost, not OS cold-cache claims",
                  inputPolicy="exact canonical input hashes come from separate full-precision correctness runs of the identical executable; independent confirmation changes resolution/Re/positions and never ranks choices",
                  maximumFormalSeconds=3600,maximumFormalGiB=16)
    validate_protocol(protocol)
    calls=len(protocol["calibrationArms"])*10+(len(NAMES)+1)*14
    budget=dict(formalProcesses=calls*len(cells),seconds=sum(c["budgetPerProcessMs"]*calls for c in cells)/1000+60,
                bytes=sum(c["budgetPerProcessBytes"]*calls for c in cells),
                note="Measured worst discovery arm, conservative step/heldout scaling, 1.1 s per process for qualification/audits, plus 60 s control reserve. Separate correctness/build outputs are already charged against current free space, not hidden in this forecast.")
    budget["GiB"]=budget["bytes"]/2**30
    budget["withinReviewedLimits"]=budget["seconds"]<=protocol["maximumFormalSeconds"] and budget["GiB"]<=protocol["maximumFormalGiB"]
    return protocol,budget


if __name__ == "__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--discovery",type=Path,required=True)
    parser.add_argument("--output",type=Path,required=True)
    args=parser.parse_args()
    protocol,budget=make(args.discovery)
    args.output.mkdir(parents=True,exist_ok=False)
    (args.output/"protocol.json").write_text(json.dumps(protocol,indent=2)+"\n")
    (args.output/"budget.json").write_text(json.dumps(budget,indent=2)+"\n")
    cases=[cell[partition] for cell in protocol["cells"] for partition in ("calibration","confirmation")]
    (args.output/"correctness-cases.json").write_text(json.dumps(cases,indent=2)+"\n")
    print(json.dumps(budget,indent=2))
    raise SystemExit(0 if budget["withinReviewedLimits"] else 1)
