"""Offline process-paired analysis of the frozen run; never executes a workload."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import statistics

TC = {3: 4.302652729696142, 5: 2.7764451051977987}


def paired(reference, candidate):
    assert len(reference) == len(candidate) and len(reference) in TC
    d = [b-a for a,b in zip(reference,candidate)]
    m = statistics.mean(d)
    h = TC[len(d)] * statistics.stdev(d)/math.sqrt(len(d))
    a, b = statistics.mean(reference), statistics.mean(candidate)
    lo, hi = m-h, m+h
    state = ('tie' if lo >= -.02*a and hi <= .02*a else
             'improvement' if hi < 0 else 'regression' if lo > 0 else 'inconclusive')
    return dict(processPairs=len(d), referenceMeanMs=a, candidateMeanMs=b,
                candidateMinusReferenceMs=m, difference95CI=[lo,hi],
                referenceOverCandidateRatio=a/b, status=state,
                referenceProcessesMs=reference, candidateProcessesMs=candidate)


def read(base, name):
    receipt=json.loads((base/(name+'-check')/'correctness.json').read_text())
    invoke=json.loads((base/name/'invocation.json').read_text())
    assert receipt['passed'] and invoke['status']=='passed' and invoke['exitCode']==0 and invoke['mutexReleased']
    p=base/name/'result.json'
    if 'timingSha256' in receipt:
        assert hashlib.sha256(p.read_bytes()).hexdigest()==receipt['timingSha256']
    return json.loads(p.read_text())


def summarize(base):
    results = {}
    babel={a:[read(base,f'measure-babel-r{i}-{a}') for i in range(1,6)] for a in ('native','burst')}
    for op_index, op in enumerate(('Copy','Mul','Add','Triad','Dot')):
        def means(arm):
            values=[]
            for row in babel[arm]:
                inner=row['operationMs'][op_index]
                if isinstance(inner,dict): inner=inner['values']
                assert len(inner)==100
                values.append(statistics.mean(inner[1:]))
            return values
        results['BabelStream/'+op]=paired(means('native'),means('burst'))
    results['BabelStream/storageLifecycle']=paired(*[[r['storageLifecycleMs'] for r in babel[a]] for a in ('native','burst')])
    for boundary in ('constructMs','initMs','exportMs','disposeMs'):
        results['BabelStream/'+boundary]=paired(*[[r[boundary] for r in babel[a]] for a in ('native','burst')])
    llama={a:[read(base,f'measure-llama-r{i}-{a}') for i in range(1,4)] for a in ('AoS','AoSoA16','burst')}
    for arm in ('AoSoA16','burst'):
        results['LLAMA/'+arm+'/fiveSteps']=paired(*[[sum(r['wholeStepMs']) for r in llama[a]] for a in ('AoS',arm)])
        results['LLAMA/'+arm+'/storageLifecycle']=paired(*[[r['storageLifecycleMs'] for r in llama[a]] for a in ('AoS',arm)])
    return dict(unit='ms', independentUnit='fresh process pair', innerBabelIterationZeroExcluded=True,
                estimator='mean paired difference; two-sided Student-t 95% CI; 2% equivalence margin',
                comparisons=results, allocationEligibility='Unknown', profilePublished=False)


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--base',required=True,type=Path)
    p.add_argument('--output',required=True,type=Path)
    args=p.parse_args()
    if args.output.exists(): raise RuntimeError('Output already exists')
    result=summarize(args.base)
    args.output.write_text(json.dumps(result,indent=2))
    for name,r in result['comparisons'].items():
        print(f"{name}: {r['referenceMeanMs']:.6f} -> {r['candidateMeanMs']:.6f} ms; difference CI {r['difference95CI']}; {r['status']}")
