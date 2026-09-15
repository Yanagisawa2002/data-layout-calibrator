"""Package already completed Babel Dot evidence and generate its report offline."""
import argparse
from decimal import Decimal, localcontext
import hashlib
import json
import math
from pathlib import Path
import shutil
import statistics
import zipfile

from dot_experiment import OPS, ROOT, sha, write


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--artifacts', type=Path, required=True)
    p.add_argument('--outputs', type=Path, required=True)
    a = p.parse_args()
    r = a.artifacts.resolve()
    summary = json.loads((r/'summary.json').read_text())
    audit = json.loads((r/'audit.json').read_text())
    assert audit['passed']
    assert sha(r/'summary.json')==audit['summarySha256']
    frozen = json.loads((r/'freeze.json').read_text())
    destination = ROOT/'Docs/evidence/babel-dot-20260915'
    destination.mkdir(parents=True,exist_ok=False)
    # Independent arithmetic recheck with decimal variance, not the summarizer's
    # statistics.stdev. Also verify the stated t critical value integrates to .975.
    rows = summary['processRows']
    with localcontext() as ctx:
        ctx.prec=50
        for label, endpoints in summary['comparisons'].items():
            reference,candidate=label.split('-to-')
            for key,item in endpoints.items():
                ra=[Decimal(str(x[key])) for x in rows if x['arm']==reference]
                ca=[Decimal(str(x[key])) for x in rows if x['arm']==candidate]
                ds=[c-b for b,c in zip(ra,ca)]
                mean=sum(ds)/6
                half=Decimal('2.570581835636305')*(sum((d-mean)**2 for d in ds)/30).sqrt()
                assert abs(float(mean)-item['differenceMs'])<1e-9
                for expected,actual in zip((mean-half,mean+half),item['pairedDifference95CI']):
                    assert abs(float(expected)-actual)<1e-9
    t=2.570581835636305
    steps=10000; h=t/steps
    density=lambda x:8/(3*math.pi*math.sqrt(5))*(1+x*x/5)**-3
    integral=h/3*(density(0)+density(t)+sum((4 if i%2 else 2)*density(i*h) for i in range(1,steps)))
    assert abs(.5+integral-.975)<1e-12
    residuals=[]
    for timing in r.glob('*/result.json'):
        if not (timing.parent/'result.bin').exists(): continue
        d=json.loads(timing.read_text())
        values=[v['values'] if isinstance(v,dict) else v for v in d['operationMs']]
        timed=sum(sum(v) for v in values)+sum(d[k] for k in ('constructMs','initMs','exportMs','disposeMs'))
        residual=d['storageLifecycleMs']-timed
        assert residual>=-1e-6, 'Storage lifetime omitted timed components'
        residuals.append(residual)
    write(r/'statistics-crosscheck.json',dict(passed=True, pairedComparisons=30,
        decimalPrecision=50, maximumAllowedArithmeticDifferenceMs=1e-9,
        studentTCdfAtCriticalValue=.5+integral, lifetimeIncludesAll100TimedIterations=True,
        lifetimeMinusTimedComponentsMsRange=[min(residuals),max(residuals)]))
    # Compact receipts include every raw per-iteration timing. All 25 complete
    # byte outputs share one verified hash; include one gzip of those actual bytes.
    selected=[]
    for name in ('environment.json','logical-input.json','freeze.json','summary.json','audit.json','statistics-crosscheck.json','full-output.bin.gz'):
        selected.append(r/name)
    selected += list(r.glob('*-preflight.json'))
    for folder in r.iterdir():
        if folder.is_dir() and (folder/'invocation.json').exists():
            selected += [p for p in folder.iterdir() if p.is_file() and p.suffix in ('.json','.log','.txt')]
    selected += [r/'native/build-command.json', r/'player-02/source-identity.json', r/'unity/module-install.json', r/'toolchain/module.json']
    selected += list((r/'player-02').glob('*BurstDebugInformation*/**/lib_burst_generated.txt'))
    for source in selected:
        dest=destination/source.relative_to(r)
        dest.parent.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(source,dest)
    entries=[dict(path=p.relative_to(destination).as_posix(),bytes=p.stat().st_size,sha256=sha(p))
             for p in sorted(destination.rglob('*')) if p.is_file()]
    write(destination/'index.json',dict(files=entries, sourceArtifacts=str(r),
        fullUncompressedOutputsRetainedLocally=True, allFullOutputsShareIncludedGzip=True))
    means=summary['armMeans95CI']; comparisons=summary['comparisons']
    sp=comparisons['serial-to-parallel']; np=comparisons['native-to-parallel']
    fmt=lambda x:f'{x:,.3f}'
    span=lambda x:f'[{fmt(x[0])}, {fmt(x[1])}]'
    lines=['# BabelStream parallel Dot — measured engineering result, 2026-09-15','',
      f"The deterministic compensated parallel reduction cut Dot elapsed time by **{sp['Dot']['reductionPercent']:.2f}%** "
      f"and the complete 100-iteration storage lifetime by **{sp['storageLifecycleMs']['reductionPercent']:.2f}%** versus the original serial Burst path on this machine.", '',
      '**The no-regression goal was not fully met.** Copy, Mul and Triad slowed by 1.39%, 1.65% and 1.87%. '
      'Mul/Triad confidence intervals extend beyond the predeclared 2% practical band. '
      f"The optimized full lifetime remains **{-np['storageLifecycleMs']['reductionPercent']:.2f}% slower than native OpenMP**. "
      'Retain this as an explicit candidate; allocation eligibility remains **Unknown**.', '',
      '## Reproducible evidence','',
      '- [Frozen protocol](BABEL_DOT_PROTOCOL_2026-09-15.md), [step-by-step Windows reproduction](BABEL_DOT_REPRODUCE_WINDOWS.md) and [entrypoints](../Tools/ActualComparison/README.md).',
      '- [All process rows, means and paired intervals](evidence/babel-dot-20260915/summary.json).',
      '- [Completed audit](evidence/babel-dot-20260915/audit.json), [source/binary freeze](evidence/babel-dot-20260915/freeze.json), '
      '[environment](evidence/babel-dot-20260915/environment.json), [every retained file hash](evidence/babel-dot-20260915/index.json).',
      '- [Actual complete output, gzip](evidence/babel-dot-20260915/full-output.bin.gz): decompresses to 805,306,368 bytes. '
      'All 25 separately persisted outputs have this same verified SHA-256. Raw timing JSON and complete invocation/output logs for every attempt are in the evidence directory.', '',
      '## Implementation and boundaries','',
      'The original serial job is unchanged. A common Player selects `-dla-dot-mode serial|parallel`; the default retains historical serial behavior. '
      'The new API schedules 512 contiguous 65,536-element chunks, then one dependent merge job. '
      'Each chunk retains a double sum and Neumaier correction; the merge consumes both in ascending chunk order. '
      'Strict floating-point compilation, explicit final tails and exact caller-owned scratch sizing preserve the numerical contract. '
      'Scratch is 8,192 bytes, reused only after completion, completely assigned every call, and disposed by the caller.', '',
      'Dot time includes accumulator initialization, chunk writes, Schedule/Complete and the entire dependent merge. '
      'Scratch allocation/zero initialization and disposal are inside storage lifetime. '
      'The original upstream kernels, run_all, check_solution, constants and license remain unchanged. '
      'Both arms initialize twice, run all five operations for all 100 iterations, export all arrays once and dispose storage. '
      'Caller-owned export-buffer allocation, file I/O, numerical checking and process/Unity startup are outside lifetime for every arm. '
      'These are C#/Burst and Windows/OpenMP variants, not certified original BabelStream submissions; no array-size scaling study was performed.', '',
      '## New environment','',
      'Intel Core Ultra 7 265K, 20 exposed cores/20 logical processors, one CPU group and one NUMA node. '
      'Windows CPU sets report eight efficiency-class 1 cores and twelve class 0 cores; no affinity restrictions were added. '
      'Two 16 GiB DIMMs report 4,800 MT/s; OS-visible physical memory is 33,682,857,984 bytes. '
      'Windows 11 Home 10.0.26200. This is a separate environment from the historical September 10 machine.', '',
      'Native: MSVC toolset 14.44.35207 (compiler 19.44.35222), SDK 10.0.26100.0; '
      '`/O2 /fp:strict /arch:AVX2 /std:c++20 /EHsc /MD /openmp`, 20 maximum threads, dynamic threads disabled, default placement. '
      'Player: Unity 6000.5.9f1, IL2CPP Release, Burst 1.8.30 (LLVM 21), Collections 6.5.0, Mathematics 1.4.0; '
      '19 job workers plus possible main-thread participation in both arms. AOT manifests include SSE2 and AVX2 variants with safety checks off; '
      'runtime ISA dispatch was not separately traced. Every formal Player log confirms the Null graphics device. '
      'Different threading APIs do not establish equal core occupancy.', '',
      f"Formal five-second CPU preflight means ranged from {audit['formalCpuPreflightMeanPercentRange'][0]:.2f}% to {audit['formalCpuPreflightMeanPercentRange'][1]:.2f}%. "
      'All builds/workloads used the shared `Local\\CodexR9700VNextUnityGpu` mutex and preserved at least 20 GiB disk reserve. '
      'The HLSL Scan task deferred hardware work until closeout. No cache eviction, power/driver/priority/affinity changes or unrelated process termination was used.', '',
      '## Confirmatory results','',
      'Six balanced fresh-process blocks, three arms per block. Values are milliseconds; operation means use iterations 1–99, '
      'while lifetime contains all 100. Brackets below are two-sided process-level 95% Student-t confidence intervals (df=5).', '',
      '| Endpoint | Native OpenMP mean [95% CI] | Original Burst mean [95% CI] | Parallel Burst mean [95% CI] |',
      '| --- | ---: | ---: | ---: |']
    for key in OPS+['storageLifecycleMs']:
        cells=[f"{fmt(means[arm][key]['meanMs'])} {span(means[arm][key]['ci95'])}" for arm in ('native','serial','parallel')]
        lines.append('| '+('Storage lifetime, all 100' if key=='storageLifecycleMs' else key)+' | '+' | '.join(cells)+' |')
    lines += ['', '### Parallel minus original Burst: paired effects','',
      '| Endpoint | Paired difference ms [95% CI] | Time reduction | Interpretation |', '| --- | ---: | ---: | --- |']
    for key in OPS+['storageLifecycleMs','constructMs','initMs','exportMs','disposeMs']:
        d=sp[key]
        state=d['status']+('; within +/-2% practical band' if d['withinTwoPercent'] else '')
        lines.append(f"| {key} | {fmt(d['differenceMs'])} {span(d['pairedDifference95CI'])} | {d['reductionPercent']:.2f}% | {state} |")
    lines += ['', 'Copy/Mul/Add/Triad code and worker settings are identical between Burst arms. Their observed timing changes are still retained; '
      'the mechanism was not profiled. Copy is a statistically directional slowdown inside the 2% band; Add is within that band with a CI crossing zero. '
      'Mul and Triad do not clear the practical no-regression check. Export and disposal also slowed; their full costs remain in the improved lifetime.', '',
      '### Parallel minus native OpenMP','',
      '| Endpoint | Paired difference ms [95% CI] | Time reduction | State |','| --- | ---: | ---: | --- |']
    for key in OPS+['storageLifecycleMs']:
        d=np[key]
        lines.append(f"| {key} | {fmt(d['differenceMs'])} {span(d['pairedDifference95CI'])} | {d['reductionPercent']:.2f}% | {d['status']} |")
    lines += ['', 'The small Dot advantage over OpenMP is specific to these variants and six process blocks. '
      'The full optimized lifetime remains slower; there is no overall native-win claim. '
      'Intervals use process pairs, not 594 inner iterations as independent samples. '
      'Orders are fixed and balanced, sample size is small, and multiple endpoints have no multiplicity adjustment. '
      'Percent reductions are descriptive ratios of means. The JSON also rescales difference intervals by the observed reference mean; those are not independent ratio intervals.', '',
      '## Every formal process','',
      '| Block / position | Arm | Copy | Mul | Add | Triad | Dot | Storage lifetime |',
      '| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |']
    for row in rows:
        lines.append(f"| {row['block']} / {row['position']} | {row['arm']} | "+' | '.join(fmt(row[k]) for k in OPS+['storageLifecycleMs'])+' |')
    lines += ['', '## Correctness and audit','',
      '- 25 full-default processes: four discovery, three prerequisites, eighteen formal. Every process passed all 100,663,296 array values and final Dot; '
      'all 25 raw outputs were rehashed at closeout. Array bytes exactly equal the independent oracle. Upstream relative tolerances were not relaxed '
      '(machine epsilon ×100 arrays, ×10,000,000 Dot). The shared checker now explicitly rejects infinities as well as NaNs.',
      '- 28 additional actual-Burst fixtures at each of 1 and 19 workers, three repeated reductions per fixture; exact independent decimal-oracle results and identical repeated bits. '
      'Inputs cover zero/small counts, complete/partial chunks, signed nonuniform dyadics and cancellation within/across chunks. Scratch/output poisoning tests complete assignment. '
      'Nine controls per worker configuration cover invalid/nonfinite inputs, invalid counts, the original serial cancellation failure and two synthetic missing-contribution errors.',
      '- Original serial job text matches main after newline normalization. 640 frozen files and all build-manifest inputs match their recorded hashes; '
      'serial/partial/merge AOT entries are present. Recorded stages do not overlap; every formal exit code is zero. '
      'Independent decimal arithmetic recomputed all 30 paired intervals, and a numerical Student-t integration checked the critical value. '
      'Every lifetime is at least the sum of its 100 timed operation sets and recorded construction/init/export/disposal.',
      '- Repository functional validation: 82 NUnit tests, three CI-policy tests and six renderer-contract tests passed. '
      'All 22 upstream source hashes and the locked input fixture/preparer hashes passed. Static JSON/link and whitespace checks passed after staging the new artifacts.',
      '- The real 1 MiB escaping allocation positive control still reports zero in the IL2CPP Player. '
      '`allocationEligibility=Unknown`; there is no worker/native allocation-window coverage or deployment profile.', '',
      '## Failures and source identity','',
      'The initial clone hit Windows long-path checkout limits; repository-local long-path support completed checkout. '
      'The initial Player build compiled source but failed because the installed Editor lacked Windows IL2CPP support. '
      'Its exit-1 log is retained. A task-owned Editor copy on D: received the exact official matching module; Unity manifest integrity and Authenticode signature were verified, '
      'and the silent component installation exited zero. The second IL2CPP build succeeded. Existing Editor source files and other task checkouts were not edited. '
      'Native build warnings from retained upstream float instantiation remain in its raw log.', '',
      'Both build and freeze originally identify starting main `8e83a17338452c9d202f45ed553e0c807a7b8558` plus their actual dirty source lists and byte hashes. '
      'The delivery commit is a later packaging identity, not a backdated build claim. '
      'The original September 10 results are never mixed into these statistics.', '',
      '| Identity | SHA-256 |','| --- | --- |',
      f"| Formal freeze | `{audit['freezeSha256']}` |",
      f"| Complete initial logical a/b/c input (reconstructed, not runtime readback) | `{json.loads((r/'logical-input.json').read_text())['sha256']}` |",
      f"| Every full output, 805,306,368 bytes | `{audit['distinctFullOutputHashes'][0]}` |"]
    for label,suffix in [('Native executable','native/babel_native.exe'),('Player executable','player-02/DataLayoutCalibrator.exe'),
                         ('IL2CPP GameAssembly','player-02/GameAssembly.dll'),('Burst AOT library','DataLayoutCalibrator_Data/Plugins/x86_64/lib_burst_generated.dll')]:
        found=next(x for x in frozen['files'] if x['path'].replace('\\','/').endswith(suffix))
        lines.append(f"| {label} | `{found['sha256']}` |")
    cv=(f"Implemented a deterministic, compensated parallel reduction in Unity Burst for BabelStream's 33.6M-element double-precision Dot, "
        f"reducing Dot time by {sp['Dot']['reductionPercent']:.1f}% and 100-iteration storage-lifetime time by {sp['storageLifecycleMs']['reductionPercent']:.1f}% "
        "versus the original Burst implementation across six balanced process trials, with full-output validation and same-machine OpenMP comparison.")
    lines += ['', '## English resume candidate','', '> '+cv, '',
      'Use only with the original-Burst comparator and this measured workload. It does not claim a 21.7% improvement over OpenMP, '
      'no regressions, allocation qualification, cross-machine generality or hardware-counter evidence.', '',
      f'All original artifacts, including 25 uncompressed full outputs and both toolchains, remain at `{r}`. '
      'The checked-in gzip is an actual recorded output, with a verified decompressed hash matching every full-output receipt.', '']
    report=ROOT/'Docs/BABEL_DOT_REPORT_2026-09-15.md'
    report.write_text('\n'.join(lines),encoding='utf-8',newline='\n')
    a.outputs.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(report,a.outputs/report.name)
    (a.outputs/'resume-candidate.txt').write_text(cv+'\n',encoding='utf-8')
    with zipfile.ZipFile(a.outputs/'babel-dot-evidence.zip','x',compression=zipfile.ZIP_DEFLATED) as archive:
        for path in destination.rglob('*'):
            if path.is_file(): archive.write(path, 'evidence/'+path.relative_to(destination).as_posix())
        archive.write(report,report.name)
        archive.write(ROOT/'Docs/BABEL_DOT_PROTOCOL_2026-09-15.md','BABEL_DOT_PROTOCOL_2026-09-15.md')
    print(json.dumps(dict(report=str(report),evidenceFiles=len(entries),evidenceBytes=sum(e['bytes'] for e in entries),
                         archiveSha256=sha(a.outputs/'babel-dot-evidence.zip'))))


if __name__=='__main__': main()
