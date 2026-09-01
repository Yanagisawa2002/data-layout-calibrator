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

An assembly-level registration attribute and packaged Roslyn Source Generator create the runtime factory registry as direct constructor calls. The generator is AOT-safe and intentionally narrow: it removes hand-maintained registration without attempting to synthesize layouts or rewrite workload code.

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
  SourceGenerators/                packaged Roslyn analyzer DLL
  SourceGenerators~/               generator source and Roslyn tests
BenchmarkProject/                  standalone Release Player and evidence writer
Tools/ResultRenderer/               fixed-result PNG/GIF renderer and tests
Docs/                              contracts, ADRs, fixed evidence, rendered assets
```

## Build and run

The committed validation project uses Unity `6000.5.3f1`.

```powershell
Unity.exe -batchmode -projectPath BenchmarkProject `
  -runTests -testPlatform EditMode -testResults editmode.xml

Unity.exe -batchmode -quit -projectPath BenchmarkProject `
  -executeMethod Yanagisawa.DataLayoutCalibrator.Benchmark.Editor.DataLayoutCalibratorBuild.BuildWindowsMonoAotEvidence

Unity.exe -batchmode -quit -projectPath BenchmarkProject `
  -executeMethod Yanagisawa.DataLayoutCalibrator.Benchmark.Editor.DataLayoutCalibratorBuild.BuildWindowsIl2CppFormal

Builds/windows-x64/mono-aot-evidence/DataLayoutCalibrator.exe `
  -batchmode -nographics -dla-run -dla-quit `
  -dla-count 1048576 -dla-holdout-count 1000003 `
  -dla-samples 40 -dla-boundary-samples 20 `
  -dla-lifetime-ticks 600 -dla-bootstrap-iterations 4000 `
  -dla-output CalibrationResults/run-01

dotnet test Packages/com.yanagisawa.data-layout-calibrator/SourceGenerators~/Tests/Yanagisawa.DataLayoutCalibrator.SourceGenerator.Tests.csproj -c Release

python -m unittest discover Tools/ResultRenderer/tests -v
python Tools/ResultRenderer/render_results.py `
  Docs/evidence/il2cpp-release-calibration-suite.json Docs/assets
```

The build fails unless the Burst library contains all ParticleIntegrate and TransformExport job entrypoints. A successful run writes:

- `calibration-suite.json`: immutable suite result and final decisions;
- `<scenario>/profile.json`: scenario-scoped fixed result;
- `<scenario>/samples.csv`: recorded ingress/resident/export samples;
- summaries stating the exact measurement and presentation contract.

Heatmaps and GIFs read `calibration-suite.json`. They may format or filter it, but may not recompute or replace `FinalDecision`. The renderer writes a provenance manifest containing the input SHA-256 and exact copied decision fields.

## Current gate

The full roadmap gate is complete: 29/29 Unity EditMode tests, 4/4 generator tests, 3/3 renderer tests, Mono Release + Burst AOT, and IL2CPP Release + Burst AOT. Both workload plugins pass parity and zero-allocation gates in both Players.

The checked-in IL2CPP integration result is deliberately a short behavioral gate, not a universal hardware performance claim. On this run, ParticleIntegrate selected `AoSoA8-b128` with a 34.15% holdout amortized-P95 improvement; TransformExport retained `AoS-b256`, demonstrating the negative control. The immutable result SHA-256 is `85FAC20CDF81EBA674A3A736340CFCBEEB88EEF99CD1F5ECC776EE0215E53D78`.

![IL2CPP fixed-result heatmap](Docs/assets/data-layout-calibrator-heatmap.png)

![AoS baseline to frozen decision](Docs/assets/data-layout-calibrator-comparison.gif)

See the [final validation evidence](Docs/VALIDATION_RESULTS_2026-09-02.md), [calibration contract](Docs/CALIBRATION_CONTRACT.md), and [fixed-result renderer contract](Tools/ResultRenderer/README.md).
