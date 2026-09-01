# Data Layout Autotuner

Data Layout Autotuner is a clean-room-style Unity/Burst feasibility project for selecting the fastest correct data layout and job scheduling parameters for an explicitly declared numeric pipeline.

The first milestone deliberately does **not** rewrite arbitrary C# and does **not** compete with the Burst compiler. It compares concrete AoS, SoA, and AoSoA8 implementations in an AOT Player, validates output parity, and emits a device- and workload-scoped profile.

## Direct objective

Reduce resident CPU pipeline latency or increase numeric-pipeline throughput without changing the input, algorithm, output, or workload size. Conversion and export costs are reported separately before any end-to-end claim.

## Feasibility scope

- Deterministic particle-update workload with hot and cold fields.
- Concrete, non-generic Burst jobs for AoS, SoA, and AoSoA8.
- Logical batch-size calibration.
- Quantized output hash and field-level parity checks.
- Warmup plus P50/P95/P99 Player measurements.
- Static JSON profile and CSV evidence.
- A Player overlay suitable for screen recording; a result-file-driven comparison view is the next milestone.

## Repository layout

```text
Packages/com.yanagisawa.data-layout-autotuner/  Reusable UPM package
BenchmarkProject/                              Standalone Unity validation project
Docs/adr/                                      Architecture decisions and go/no-go gates
```

## Evidence boundary

A visual comparison is illustrative only. Performance claims require a release AOT Player, identical inputs, output parity, repeated samples, a frozen selection objective, and confirmation on an untouched holdout run.

AoSoA8 changes both storage and kernel shape by executing eight logical records per job iteration. Its result is therefore a candidate-pipeline result, not a claim that layout alone caused the entire difference.

## Run it

The committed benchmark project uses Unity `6000.5.3f1`. From a shell with the Unity path adjusted for the local machine:

```powershell
Unity.exe -batchmode -projectPath BenchmarkProject -runTests -testPlatform EditMode -testResults editmode.xml

Unity.exe -batchmode -quit -projectPath BenchmarkProject `
  -executeMethod Yanagisawa.DataLayoutAutotuner.Benchmark.Editor.DataLayoutBenchmarkBuild.BuildWindowsMonoAotEvidence

Builds/windows-x64/mono-aot-evidence/DataLayoutBenchmark.exe `
  -batchmode -nographics -dla-run -dla-quit `
  -dla-count 1048576 -dla-holdout-count 1000003 `
  -dla-samples 30 -dla-output BenchmarkResults/run-01
```

The build fails if the Burst native library or any of the three concrete AOT entrypoints is absent. `profile.json`, `samples.csv`, and `summary.txt` are written only after parity validation.

## Status

Engineering feasibility is established for the synthetic resident `particle-integrate-v1` workload on the recorded Windows machine. See [the feasibility report](Docs/FEASIBILITY_RESULTS_2026-09-01.md). Source generation and a public/general performance claim remain gated.
