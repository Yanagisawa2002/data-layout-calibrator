"""Audit real grid provenance and copy locked decisions; never select or pool frame CIs."""
from __future__ import annotations
import argparse
import hashlib
import itertools
import json
from collections import Counter, defaultdict
from pathlib import Path

STATUS = {0: 'Invalid', 1: 'AoSFallback', 2: 'StatisticalGreyZone', 3: 'CredibleAdvantage', 4: 'HoldoutRejected'}


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def read(path: Path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def audit(root: Path) -> dict:
    declaration = read(root / 'grid-declaration.json')
    declared_hash = sha(root / 'grid-declaration.json')
    build_hash = sha(root / 'build-identity.json')
    expected = set(itertools.product(declaration['WorkerCounts'], declaration['ElementCounts'],
        declaration['ColdAccessEveryTicks'], declaration['LifetimeTicks']))
    if len(expected) != 24 or declaration['IndependentProcesses'] != 5:
        raise ValueError('Not the predeclared 24-cell, five-process grid')
    records, missing, hashes = [], [], {}
    processes, candidate_sets = set(), set()
    environments = set()
    declared_candidates = {candidate['CandidateId']: candidate for candidate in declaration['Candidates']}
    for process in range(1, 6):
        folder = root / f'run-{process:02d}'
        receipt_path = folder / 'receipt.json'
        if not receipt_path.exists():
            missing.append(f'run-{process:02d}: receipt unavailable')
            continue
        receipt = read(receipt_path)
        if (receipt['DeclarationSha256'] != declared_hash or receipt['BuildIdentity'] != build_hash or
                receipt['ScriptingBackend'] != 'IL2CPP' or receipt['DevelopmentBuild'] or not receipt['BurstEnabled'] or
                receipt['ProcessIndex'] != process):
            raise ValueError(f'{folder.name}: incompatible measurement identity/backend')
        # PID reuse is possible after process exit; identity includes launch timestamp.
        identity = (receipt['ProcessId'], receipt['StartUtc'])
        if identity in processes:
            raise ValueError('Repeated process evidence')
        processes.add(identity)
        candidate_sets.add(receipt['CandidateSetSha256'])
        environments.add((receipt['Processor'], receipt['OperatingSystem'], receipt['UnityVersion'], receipt['BurstIsaProbeMask']))
        measured_axes = set()
        hashes[str(receipt_path.relative_to(root))] = sha(receipt_path)
        for envelope_path in sorted(folder.glob('*-envelope.json')):
            envelope = read(envelope_path)
            if not envelope['FinalDecisionLocked'] or envelope['HoldoutCanRerank']:
                raise ValueError('Unlocked or retunable envelope')
            if envelope['CandidateSetHash'] != receipt['CandidateSetSha256']:
                raise ValueError('Candidate pool changed')
            identifier = envelope['EnvelopeId']
            frozen = read(folder / f'{identifier}-frozen.json')
            settings = read(folder / f'{identifier}-settings.json')
            if frozen['HoldoutWasRead'] or settings['CalibrationSeed'] == settings['HoldoutSeed']:
                raise ValueError('Holdout partition is not independent')
            for phase in ('Calibration', 'Holdout'):
                path = folder / (envelope[f'{phase}SourceArtifactId'] + '.json')
                if sha(path) != envelope[f'{phase}SourceArtifactSha256']:
                    raise ValueError(f'{path}: raw source hash mismatch')
                hashes[str(path.relative_to(root))] = sha(path)
            calibration = read(folder / f'{identifier}-calibration.json')
            holdout = read(folder / f'{identifier}-holdout.json')
            if len(envelope['Cells']) != 1 or len(frozen['Cells']) != 1:
                raise ValueError('Expected one measured cell per artifact')
            cell = envelope['Cells'][0]
            axis = cell['Axis']
            period = round(axis['HotToColdRatio'] / 1.4)
            key = (axis['WorkerCount'], axis['ElementCount'], period, axis['LifetimeTicks'])
            if key not in expected or abs(axis['HotToColdRatio'] - 1.4 * period) > 1e-9 or key in measured_axes:
                raise ValueError('Unexpected, relabeled or repeated axis')
            measured_axes.add(key)
            if axis['ExecutionPolicyId'] != 'FrameFaithful' or settings['ElementCount'] != key[1] or settings['HoldoutElementCount'] != key[1] or settings['LifetimeTicks'] != key[3]:
                raise ValueError('Axis/settings dimensions do not match')
            frozen_cell = frozen['Cells'][0]
            if frozen_cell['Axis'] != axis:
                raise ValueError('Frozen axis changed')
            if cell['FrozenCalibrationWinnerCandidateId'] != frozen_cell['FrozenCalibrationWinnerCandidateId']:
                raise ValueError('Holdout replaced frozen candidate')
            if {r['Candidate']['CandidateId'] for r in calibration['Results']} != set(declared_candidates):
                raise ValueError('Calibration did not measure the complete declared pool')
            for phase_index, raw in enumerate((calibration, holdout)):
                for result in raw['Results']:
                    if (result['Candidate'] != declared_candidates.get(result['Candidate']['CandidateId']) or
                            result['Phase'] != phase_index or result['ElementCount'] != axis['ElementCount'] or
                            result['BoundaryCost']['LifetimeTicks'] != axis['LifetimeTicks']):
                        raise ValueError('Raw result descriptor/phase/axis mismatch')
                    if (not result['Completed'] or not result['ParityPassed'] or
                            result['HotPathManagedAllocationBytes'] != 0 or result['BoundaryManagedAllocationBytes'] != 0):
                        missing.append(f'{identifier}: invalid parity/completion/allocation evidence')
                    for component, minimum in (('Resident', 40), ('Ingress', 20), ('Export', 20)):
                        sample_key = component + ('SamplesMillisecondsPerTick' if component == 'Resident' else 'SamplesMilliseconds')
                        if len(result[sample_key]) < minimum or len(result[component+'BlockIds']) != len(result[sample_key]):
                            raise ValueError('Insufficient or unpaired raw evidence')
            if holdout['Results']:
                if holdout['DatasetHash'] == calibration['DatasetHash']:
                    raise ValueError('Holdout reused calibration dataset')
                held_ids = {r['Candidate']['CandidateId'] for r in holdout['Results']}
                if held_ids != {cell['BaselineCandidateId'], cell['FrozenCalibrationWinnerCandidateId']}:
                    raise ValueError('Holdout measured an unfrozen pool')
            status = STATUS[cell['Status']]
            if status == 'CredibleAdvantage' and (not cell['HoldoutConfirmed'] or
                    cell['HoldoutConfidenceInterval']['LowerBoundPercent'] <= 0 or cell['HoldoutImprovementPercent'] < 10):
                raise ValueError('Credible advantage failed independent evidence gates')
            if status != 'CredibleAdvantage' and cell['SelectedCandidateId'] != cell['BaselineCandidateId']:
                raise ValueError('Nonadvantage cell did not fall back to AoS')
            records.append(dict(process=process, axis=axis, status=status, selected=cell['SelectedCandidateId'],
                calibrationInterval=cell['CalibrationConfidenceInterval'], holdoutInterval=cell['HoldoutConfidenceInterval'],
                breakEven=[dict(candidate=x['Candidate']['CandidateId'], estimate=x['BreakEven']) for x in cell['CandidateOutcomes']],
                source=str(envelope_path.relative_to(root))))
            hashes[str(envelope_path.relative_to(root))] = sha(envelope_path)
            for suffix in ('frozen', 'settings', 'axis'):
                path = folder / f'{identifier}-{suffix}.json'
                hashes[str(path.relative_to(root))] = sha(path)
        if measured_axes != expected or receipt['CompletedCells'] != 24 or receipt.get('Failure'):
            missing.append(f'{folder.name}: {len(measured_axes)}/24 cells; {receipt.get("Failure") or "incomplete grid"}')
    if len(candidate_sets) > 1:
        raise ValueError('Candidate sets differ across independent runs')
    if len(environments) > 1:
        raise ValueError('CPU/OS/Unity/ISA identity differs across independent runs')
    counts = Counter(r['status'] for r in records)
    valid = len(records) - counts['Invalid']
    stable = defaultdict(list)
    for record in records:
        a = record['axis']
        stable[(a['WorkerCount'], a['ElementCount'], a['HotToColdRatio'], a['LifetimeTicks'])].append(record)
    return dict(protocol='dlc.measured-envelope-summary.v1', status='complete' if not missing and len(records) == 120 else 'incomplete',
        evidenceScope='five same-device independent processes; each CI is within-process paired-block only',
        coverageDefinition='fraction of declared sampled cells; no interpolation to unsampled points',
        expectedCells=120, measuredCells=len(records), validCells=valid, statusCounts=dict(counts),
        credibleCoveragePercent=100 * counts['CredibleAdvantage'] / valid if valid else None,
        repeatability=[dict(axis=list(key), measuredProcesses=len(value),
            allFiveConfirmSameWinner=len(value) == 5 and all(r['status']=='CredibleAdvantage' for r in value) and len({r['selected'] for r in value})==1)
            for key, value in sorted(stable.items())],
        missingGates=missing, unmeasuredAxes=declaration['UnmeasuredAxes'], cells=records,
        declarationSha256=declared_hash, buildIdentitySha256=build_hash, sourceHashes=hashes)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('evidence', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    summary = audit(args.evidence)
    with args.output.open('x', encoding='utf-8') as stream:
        json.dump(summary, stream, indent=2, allow_nan=False)
    print(f"{summary['status']}: {summary['measuredCells']}/120 measured cells")
