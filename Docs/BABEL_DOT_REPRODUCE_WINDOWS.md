# Reproduce the frozen parallel Dot candidate on Windows

Use the delivered branch in an independent checkout. The reported run used
`D:\CodexWork\babel-dot-20260915`; no other checkout is required. Reserve at
least 35 GiB plus a 20 GiB free-space floor for builds and all uncompressed
output copies. Python 3.11+, psutil, Unity 6000.5.9f1 with Windows IL2CPP support,
MSVC 14.44.35207 and Windows SDK 10.0.26100.0 were actually used. Other versions
produce a new environment, not the recorded binary identity.

Do not run another benchmark or rebuild concurrently. Coordinate the complete
measurement window with other tasks and use `Local\CodexR9700VNextUnityGpu`.
The wrapper fails before starting a child if a conflicting process or lock exists.
Keep every failed attempt; choose a new run directory rather than overwrite it.

## Build and verify

From the independent repository root in PowerShell:

```powershell
$repo = (Get-Location).Path
$run = Join-Path $repo 'Artifacts/dot-reproduction-01'
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe'
$vc = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207'
$native = Join-Path $run 'native/babel_native.exe'
$player = Join-Path $run 'player-02/DataLayoutCalibrator.exe'
python Tools/KernelContracts/external_sources.py
python Tools/ActualComparison/run_exclusive.py --phase build --output "$run/native-build-01" --estimated-growth-gib 1 -- python Tools/ActualComparison/build_msvc.py --target babel --output "$run/native" --vc-tools $vc
python Tools/ActualComparison/run_exclusive.py --phase build --output "$run/player-build-02" --estimated-growth-gib 12 --timeout 1800 -- $unity -batchmode -nographics -quit -projectPath "$repo/BenchmarkProject" -executeMethod Yanagisawa.DataLayoutCalibrator.Benchmark.Editor.DataLayoutCalibratorBuild.BuildWindowsIl2CppFormal -dla-build-output "$run/player-02" -logFile "$run/player-build-02/unity.log"
python Tools/ActualComparison/run_exclusive.py --phase build --output "$run/functional-ci" -- python Tools/CI/validate_functional.py
```

Stop if any command fails. Require the build's success receipt, `source-identity.json`
and actual Burst AOT entries. An exit-zero Editor import alone is insufficient.

The original machine lacked the Windows IL2CPP module. Its exact download URL and
integrity are retained in `evidence/babel-dot-20260915/toolchain/module.json`.
The optional preparation script copies the installed Editor into an explicitly
new task-owned destination and installs the matching signed module there:

```powershell
python Tools/ActualComparison/run_exclusive.py --phase build --output "$run/toolchain-prepare-01" --estimated-growth-gib 12 -- python Tools/ActualComparison/prepare_local_unity.py --installed-root 'C:\Program Files\Unity\Hub\Editor\6000.5.9f1' --destination "$run/unity" --module '<downloaded matching Windows IL2CPP module.exe>' --module-manifest '<matching module.json>'
$unity = "$run/unity/Editor/Unity.exe"
```

Verify the module's Authenticode signature before installation. This optional step
belongs before the Player build above. It records module SHA-256 and installation
exit state and does not require changing the installed Editor.

## Separate prerequisites, discovery and formal measurement

```powershell
$common = @('--artifacts', $run, '--player', $player, '--native', $native)
python Tools/ActualComparison/dot_experiment.py --phase environment @common
python Tools/ActualComparison/dot_experiment.py --phase functional @common --workers 1
python Tools/ActualComparison/dot_experiment.py --phase functional @common --workers 19
python Tools/ActualComparison/dot_experiment.py --phase discovery @common
python Tools/ActualComparison/dot_experiment.py --phase correctness @common
python Tools/ActualComparison/dot_experiment.py --phase freeze @common
python Tools/ActualComparison/dot_experiment.py --phase measurement @common
python Tools/ActualComparison/dot_experiment.py --phase summarize @common
python Tools/ActualComparison/run_exclusive.py --phase correctness --output "$run/audit-01" --estimated-growth-gib 1 -- python Tools/ActualComparison/audit_dot.py --artifacts $run --receipt "$run/audit.json" --compressed-output "$run/full-output.bin.gz"
```

These defaults reproduce the delivered 65,536-element chunk candidate: native
20 threads, both Burst arms 19 workers, fixed full 33,554,432-double arrays and
100 complete iterations. Do not tune during the formal phase. Discovery is
kept separate. Inspect the [protocol](BABEL_DOT_PROTOCOL_2026-09-15.md) before
running on another machine; any changed design requires a new frozen protocol.

The audit verifies recorded files from the build/freeze, all 25 complete outputs,
the paired order, exits, numerical receipts, unchanged serial job and the actual
compiled AOT entries. `summary.json` includes all per-process means and paired
confidence intervals; every `result.json` retains all 100 operation timings.
The raw `.bin` format is complete little-endian binary64 arrays A then B then C.
The checked gzip reproduces those actual bytes and is bound to every output by
SHA-256. Allocation eligibility remains Unknown regardless of the timing result.

`publish_dot_evidence.py` is the dated closeout packager for the original run;
it intentionally refuses an existing checked-in evidence directory. For a new
run, retain its new artifacts/summary/audit and use a distinct dated report.
