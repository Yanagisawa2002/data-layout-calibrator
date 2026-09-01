# Data Layout Calibrator

Data Layout Calibrator is a reusable Unity/Burst calibration pipeline for comparing concrete data-layout implementations under one declared semantic contract. It does not rewrite arbitrary project code and does not claim to outsmart Burst. A workload plugin supplies concrete AOT-visible candidates; the core measures them, rejects incorrect or allocating variants, and falls back to AoS unless the evidence clears every gate.

The repository is independent from any product project and remains privately licensed.

## What is reusable

The core UPM assembly contains no Particle types. It exposes four plugin boundaries:

- `ICalibrationScenarioFactory` / `ICalibrationScenario`: deterministic input, candidate set, and workload identity.
- `ICalibrationCandidate`: one concrete layout plus literal Burst schedule sites.
- `IParityValidator`: typed, field-level comparison of canonical exports.
- `IBoundaryCost`: allocation-free full ingress and export operations.

`ScenarioCalibrationEngine` drives any implementation of those contracts. Workload code lives in separate Sample assemblies:

- `particle-integrate-v2`: AoS, SoA, and explicit eight-lane AoSoA8; batch 32/64/128/256.
- `transform-export-v1`: AoS and SoA full matrix export; deliberately retained as a negative control.

## Frozen decision rule

The primary value for each candidate is:

```text
amortized P95 ms/tick = resident P95
                      + (ingress P95 + export P95) / declared lifetime ticks
```

The baseline is the fastest valid AoS batch, not a deliberately weak default. A non-AoS candidate is selected only when it:

1. passes field-level parity and state-hash checks;
2. allocates 0 managed bytes in resident, ingress, and export samples;
3. improves amortized P95 by at least 10%;
4. has a 95% non-parametric bootstrap confidence interval whose lower bound is above 0%; and
5. repeats those gates on an untouched seed and count holdout.

An insignificant difference is recorded as `StatisticalTie` and selects AoS. A sub-threshold point estimate is `Inconclusive` and also selects AoS.

## Repository layout

```text
Packages/com.yanagisawa.data-layout-calibrator/
  Runtime/                         workload-agnostic protocol, engine, statistics
  Samples/ParticleIntegrate/       particle plugin and tests
  Samples/TransformExport/         negative-control plugin and tests
BenchmarkProject/                  standalone Release Player and evidence writer
Docs/                              contracts, ADRs, validation status
```

## Build and run

The committed validation project uses Unity `6000.5.3f1`.

```powershell
Unity.exe -batchmode -projectPath BenchmarkProject `
  -runTests -testPlatform EditMode -testResults editmode.xml

Unity.exe -batchmode -quit -projectPath BenchmarkProject `
  -executeMethod Yanagisawa.DataLayoutCalibrator.Benchmark.Editor.DataLayoutCalibratorBuild.BuildWindowsMonoAotEvidence

Builds/windows-x64/mono-aot-evidence/DataLayoutCalibrator.exe `
  -batchmode -nographics -dla-run -dla-quit `
  -dla-count 1048576 -dla-holdout-count 1000003 `
  -dla-samples 40 -dla-boundary-samples 20 `
  -dla-lifetime-ticks 600 -dla-bootstrap-iterations 4000 `
  -dla-output CalibrationResults/run-01
```

The build fails unless the Burst library contains all ParticleIntegrate and TransformExport job entrypoints. A successful run writes:

- `calibration-suite.json`: immutable suite result and final decisions;
- `<scenario>/profile.json`: scenario-scoped fixed result;
- `<scenario>/samples.csv`: recorded ingress/resident/export samples;
- summaries stating the exact measurement and presentation contract.

Future heatmaps and GIFs must read `calibration-suite.json`. They may format or filter it, but may not recompute or replace `FinalDecision`.

## Current gate

Mono Release + Burst AOT, both workload protocols, parity, boundary accounting, bootstrap selection, and the negative control are verified. Windows IL2CPP support is not installed in the local Unity editors, so the formal IL2CPP build fails before compilation. Per the gate, no Source Generator or result GIF has been implemented yet. See [validation status](Docs/FEASIBILITY_RESULTS_2026-09-01.md) and the [calibration contract](Docs/CALIBRATION_CONTRACT.md).
