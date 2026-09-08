"""Descriptive matched-factor readout of independently validated frozen evidence.
No selection, interval replacement, pooling of frames, or default promotion.
"""
import argparse
import hashlib
import json
import math
import statistics
from pathlib import Path


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def distribution(values):
    return {'count': len(values), 'median': statistics.median(values), 'minimum': min(values), 'maximum': max(values)}


def summarize(root):
    replay = read(root / 'search-formal-replay.json')
    comparisons = replay['comparisons']
    costs = [100 * (1 - r['adaptiveCalibrationMilliseconds'] / r['exhaustiveCalibrationMilliseconds']) for r in comparisons]
    evaluations = [100 * (1 - r['adaptiveEvaluations'] / r['exhaustiveEvaluations']) for r in comparisons]
    sources, data = {}, {}
    for run in range(1, 6):
        for execution in ('FrameFaithful', 'DependencyChain'):
            path = root / 'search-formal-attempt-01' / f'run-{run:02}' / execution / 'exhaustive-calibration.json'
            profile = read(path)
            assert profile['ElementCount'] == 65536 and profile['LifetimeTicks'] == 256
            rows = {}
            for result in profile['CalibrationResults']:
                d = result['Candidate']
                key = d['LayoutId'], d['Kernel']['PolicyId'], d['LogicalBatchSize']
                assert key not in rows and result['Completed'] and result['ParityPassed']
                rows[key] = result['AmortizedLatency']['P95Milliseconds']
                assert math.isfinite(rows[key]) and rows[key] > 0
            assert len(rows) == 30
            data[run, execution] = rows
            sources[str(path.relative_to(root))] = sha(path)
    contrasts = []
    for execution in ('FrameFaithful', 'DependencyChain'):
        for layout in ('SoA', 'AoSoA4', 'AoSoA8', 'AoSoA16', 'AoSPadded64'):
            for kernel in ('ScalarBranched', 'ScalarBranchless'):
                process_ratios = []
                for run in range(1, 6):
                    rows = data[run, execution]
                    logs = [math.log(rows[layout, kernel, b] / rows['AoS', kernel, b]) for b in (64, 256)]
                    process_ratios.append(math.exp(statistics.mean(logs)))
                contrasts.append({'factor': 'layout versus matched AoS', 'execution': execution,
                    'layout': layout, 'kernel': kernel, 'batches': [64, 256],
                    'improvementPercent': distribution([100 * (1 - ratio) for ratio in process_ratios])})
        for layout in ('SoA', 'AoSoA4', 'AoSoA8', 'AoSoA16', 'AoSPadded64'):
            process_ratios = []
            for run in range(1, 6):
                rows = data[run, execution]
                logs = [math.log((rows[layout, 'ScalarBranchless', b] / rows['AoS', 'ScalarBranchless', b]) /
                    (rows[layout, 'ScalarBranched', b] / rows['AoS', 'ScalarBranched', b])) for b in (64, 256)]
                process_ratios.append(math.exp(statistics.mean(logs)))
            contrasts.append({'factor': 'layout x scalar-kernel interaction', 'execution': execution, 'layout': layout,
                'meaning': 'Positive values mean branchless strengthened this layout relative to matched AoS; calibration-only descriptive ratio of ratios.',
                'improvementPercent': distribution([100 * (1 - ratio) for ratio in process_ratios])})
        process_ratios = []
        for run in range(1, 6):
            rows = data[run, execution]
            logs = [math.log(rows[l, 'ScalarBranchless', b] / rows[l, 'ScalarBranched', b])
                for l in ('AoS', 'SoA', 'AoSoA4', 'AoSoA8', 'AoSoA16', 'AoSPadded64') for b in (64, 256)]
            process_ratios.append(math.exp(statistics.mean(logs)))
        contrasts.append({'factor': 'scalar branchless versus scalar branched', 'execution': execution,
            'improvementPercent': distribution([100 * (1 - ratio) for ratio in process_ratios])})
        process_ratios = []
        for run in range(1, 6):
            rows = data[run, execution]
            logs = [math.log(value / rows[l, k, 64]) for (l, k, b), value in rows.items() if b == 256]
            assert len(logs) == 15
            process_ratios.append(math.exp(statistics.mean(logs)))
        contrasts.append({'factor': 'batch256 versus batch64', 'execution': execution,
            'improvementPercent': distribution([100 * (1 - ratio) for ratio in process_ratios])})
    process_ratios = []
    for run in range(1, 6):
        a, b = data[run, 'FrameFaithful'], data[run, 'DependencyChain']
        assert set(a) == set(b)
        process_ratios.append(math.exp(statistics.mean(math.log(b[k] / a[k]) for k in a)))
    contrasts.append({'factor': 'DependencyChain versus FrameFaithful',
        'improvementPercent': distribution([100 * (1 - ratio) for ratio in process_ratios])})
    counter = read(root / 'counter-formal-attempt-02' / 'cpu-counter-evidence.json')
    by_workload = {}
    for item in counter['Summaries']:
        by_workload.setdefault(item['ScenarioId'], []).append(item['Overhead']['EstimatedOverheadPercent'])
    envelope = read(root / 'envelope-formal-summary.json')
    held = [c for c in envelope['cells'] if c['holdoutInterval']['ReplicateCount']]
    credible = [c for c in held if c['status'] == 'CredibleAdvantage']
    assert envelope['status'] == 'complete' and envelope['measuredCells'] == envelope['validCells'] == 120
    return {'schemaVersion': 1, 'analysisScriptSha256': sha(Path(__file__)),
        'envelope': {'statusCounts': envelope['statusCounts'], 'coveragePercent': envelope['credibleCoveragePercent'],
            'measuredHoldoutCells': len(held), 'credibleHoldoutEffectPercent': distribution([c['holdoutInterval']['PointEstimatePercent'] for c in credible]),
            'credibleWorstLowerBoundPercent': min(c['holdoutInterval']['LowerBoundPercent'] for c in credible),
            'allHoldoutWorstLowerBoundPercent': min(c['holdoutInterval']['LowerBoundPercent'] for c in held),
            'axesAllFiveConfirmSameWinner': sum(r['allFiveConfirmSameWinner'] for r in envelope['repeatability']),
            'axesAllFiveHaveCredibleAdvantage': sum(all(c['status']=='CredibleAdvantage' for c in envelope['cells'] if
                [c['axis'][k] for k in ('WorkerCount','ElementCount','HotToColdRatio','LifetimeTicks')]==r['axis']) for r in envelope['repeatability'])}, 'scope': 'same-device descriptive process summaries, not new confidence intervals or decision rules',
        'sourceCommit': read(root / 'il2cpp-prepare-attempt-02' / 'build-identity.json')['sourceCommit'],
        'search': {'comparisons': len(comparisons), 'costReductionPercent': distribution(costs),
            'evaluationReductionPercent': distribution(evaluations),
            'regretPercent': distribution([r['actualSelectionOracleRegretPercent'] for r in comparisons]),
            'regretGatePassCount': sum(r['regretGatePassed'] for r in comparisons),
            'fasterCount': sum(v > 0 for v in costs), 'defaultPromotion': False},
        'matchedFactorContrasts': contrasts,
        'contrastMethod': 'Within each process, equal-weight mean log cost ratios across the explicitly matched conditions; report five process improvements as descriptive median/min/max. Layout contrasts hold kernel and execution fixed and match batches. No tuned-AoS selection is replaced. Execution contrast includes each cell\'s actual pilot-sized block.',
        'contrastLimits': 'Calibration-only exploration; multiple comparisons, no multiplicity-adjusted inference or held-out factor effects. Changing layout includes its storage/boundary costs; no claim of isolated CPU instruction or cache causality.',
        'counterOverheadPercentByWorkload': {k: distribution(v) for k, v in sorted(by_workload.items())},
        'sources': sources,
        'inputSha256': {name: sha(root/name) for name in ('search-formal-replay.json', 'counter-formal-attempt-02/cpu-counter-evidence.json', 'envelope-formal-summary.json')}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    if args.output.exists():
        raise ValueError('Use a fresh output path.')
    args.output.write_text(json.dumps(summarize(args.root), indent=2, allow_nan=False) + '\n', encoding='utf-8', newline='\n')
