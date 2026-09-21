# Data Layout Calibrator

**CPU performance engineering in Unity/Burst: deterministic parallel reduction,
complete-lifecycle measurement, and evidence-based data-layout decisions.**

This repository combines a workload-agnostic layout-calibration library with
measured engineering case studies. The question is not simply whether SoA beats
AoS: **does a change repay its conversion, ownership and export costs for the
actual application?** Sometimes the right answer is to keep AoS.

[Engineering case studies](Docs/ENGINEERING_CASE_STUDIES.md) ·
[Evidence and limitations](Docs/EVIDENCE_STATUS.md) ·
[Resident batch API](Docs/RESIDENT_BATCH.md) ·
[Adoption guide](Docs/LAYOUT_ADOPTION.md) ·
[Package API](Packages/com.yanagisawa.data-layout-calibrator/README.md)

## Two measured results, two different decisions

| Case | Recorded result | Engineering decision |
| --- | --- | --- |
| **Parallel Burst Dot** | Dot: **29.238 → 8.244 ms**, 71.8% lower time. The 100-iteration storage lifetime: **9,344.922 → 7,319.567 ms**, 21.7% lower time, versus the original serial Burst path. | Keep the compensated parallel reduction as an explicit candidate. The optimized lifetime is still **2.53% slower than native OpenMP**; do not claim an overall native win. |
| **Complete SPH application task** | Reusing cell/neighbor buffers: **5.126 → 4.319 s**, 15.7% lower mean caller latency. Custom SoA and AoSoA8 are **2.75% and 1.97% slower** than that strengthened AoS baseline. | **Keep AoS plus reusable buffers** for this task. Do not attribute the buffer-reuse gain to layout switching. |

**Scope matters.** Dot used six balanced three-arm process blocks on a Core Ultra
7 265K; its storage lifetime excludes process/Unity startup and external output
checking. SPH used 72 native processes on a shared Xeon host, including process
launch, simulation, exports and result consumption. These are different machines,
workloads and timing boundaries, not a combined benchmark or a GPU result.

The [Dot report](Docs/BABEL_DOT_REPORT_2026-09-15.md) and
[SPH report](Docs/FOCUSED_COMPLETE_TASK_2026-09-15.md) retain complete-output checks,
comparators, uncertainty, regressions and source/binary identities. Their numbers
are frozen run records, **not a new measurement of the current checkout**.

## What is implemented

The portable core separates candidate layout/kernel/batch/execution policies,
measurement contracts, statistics, selection and evidence from workload-specific
code. Integration surfaces include explicit scenario registration, generated
storage/codec scaffolds, source/device/compiler fingerprints, holdout confirmation
and conservative lifetime-cost inference.

For a new workload, the caller supplies concrete candidate implementations,
canonical input/output conversion and parity validation. The library automates
measurement and bounded decisions; it does **not** automatically rewrite arbitrary
application structs or invent optimized kernels.

Two implementation starting points:

- [BabelStreamPort.cs](Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Runtime/BabelStreamPort.cs): caller-owned scratch, chunked Neumaier-compensated sums and a dependent ordered merge.
- [WholeTaskLayoutSelector.cs](Packages/com.yanagisawa.data-layout-calibrator/Runtime/WholeTaskLayoutSelector.cs): separate complete-task calibration and confirmation, paired process evidence and timing-only recommendations.

### Direct resident-batch execution — functional, not benchmarked

The opt-in [resident batch component](Docs/RESIDENT_BATCH.md) adds an independently
buildable, serial managed CPU path: explicit AoS/SoA storage, reusable capacity,
affine point transforms, resident point-bounds reduction and cadence-aware exports.
[Scene-anchor and point-cloud coordinate callers](Tools/Examples/ResidentBatch/README.md)
reuse the same pipeline with synthetic inputs. No calibration is needed to call it.
It has deterministic functional tests, not a measured speedup, Unity/Burst/IL2CPP
validation or zero-allocation certification. Existing core decisions are unchanged.

## Current status

This is an **unreleased integration / engineering portfolio**, not a certified
deployment optimizer. Included performance records support only their named
implementations, inputs and environments. No new default algorithm is promoted.

The IL2CPP allocation positive control failed; worker/native allocation coverage
and allocation-qualified deployment remain **Unknown**. That does not erase the
recorded timing results, but it prevents a zero-allocation or deployment claim.
Profitable automatic selection, including calibration cost and independent reuse,
has **not been demonstrated**. See the [status matrix](Docs/EVIDENCE_STATUS.md),
including the separately identified unfinished PR #10 experiment.

## Start without running a benchmark

Prerequisites: Python 3.10+ and a .NET SDK capable of building the repository's
`net8.0` functional projects. From the repository root:

```powershell
python -m pip install -r Tools/ResultRenderer/requirements.txt
python Tools/CI/validate_functional.py --build-only
python Tools/CI/validate_functional.py
python Tools/CI/check_repository.py
python Tools/KernelContracts/external_sources.py
```

These are static, compilation and allowlisted deterministic CPU checks. They do
not launch Unity, a Player, timed calibration, autotuning or hardware profiling.
Passing PR CI is not new Burst/IL2CPP performance or deployment evidence.

The [CPU-only lifetime example](Tools/Examples/LifetimeDecision/README.md) uses
**synthetic costs** to demonstrate finite conditional bounds and Unknown outcomes.
It produces no benchmark result or deployment profile. For a real integration,
start with the [adoption guide](Docs/LAYOUT_ADOPTION.md) and provide an actual host.

## Reproduce measurements explicitly

Use the [Windows Dot reproduction guide](Docs/BABEL_DOT_REPRODUCE_WINDOWS.md)
or the protocol and runner linked from the
[complete SPH task report](Docs/FOCUSED_COMPLETE_TASK_2026-09-15.md). These are
opt-in measurement workflows with their own environment and correctness gates,
not part of the safe checks above. Preserve each run's source and binary identity.

Older selection-score figures remain available through the
[historical portfolio page](Docs/portfolio/README.md). They are not measured tick
P95 or full-lifecycle P95; [metric definitions](Docs/EVIDENCE_STATUS.md) explain
why those quantities must remain separate.

## Repository map

| Path | Responsibility |
| --- | --- |
| `Packages/com.yanagisawa.data-layout-calibrator/Runtime` | Workload-agnostic protocol, measurement, selection and evidence core |
| `Packages/com.yanagisawa.data-layout-calibrator/Batch` | Opt-in managed point-storage and fixed resident pipeline; independent of the calibration core |
| `Packages/com.yanagisawa.data-layout-calibrator/Samples` | Concrete workloads and candidate kernels |
| `Packages/com.yanagisawa.data-layout-calibrator/SourceGenerators~` | Generator source and tests |
| `BenchmarkProject` | Unity benchmark host; explicit measurement entrypoints |
| `Tools` | Build, functional validation, analysis and opt-in measurement tooling |
| `Docs/evidence` | Historical raw records and run-specific evidence |

## Authorship and permissions

[Citation](CITATION.cff) · [Authorship](AUTHORS.md) · [Provenance](PROVENANCE.md)

Copyright (c) 2026 Edwin Liu. The [limited benchmark reproduction permission](LICENSE)
permits benchmark reproduction and publication of results; it does not grant general
redistribution, sublicensing or product integration rights. Technical adoption
examples do not expand those permissions.
