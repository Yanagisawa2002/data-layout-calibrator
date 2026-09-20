# Engineering case studies

[Repository overview](../README.md) · [Evidence status](EVIDENCE_STATUS.md) ·
[Adoption guide](LAYOUT_ADOPTION.md)

These are two recorded CPU engineering results, not a claim that one layout is
universally faster. Each case keeps its own environment, baseline and timing
boundary. No measurements were collected for this presentation update.

## 1. Parallelize a serial Burst reduction without discarding the numerical contract

### Problem and implementation

The original C#/Burst BabelStream Dot used one serial job. At the recorded
33,554,432-element double-precision input, it was a substantial component of the
100-iteration storage lifetime. The change is **parallel reduction**, not a data
layout change or proof of a universally optimal SIMD kernel.

The explicit candidate partitions the input into 512 contiguous chunks of 65,536
elements. Each chunk computes a double sum and Neumaier correction. A dependent
merge job consumes both components in ascending chunk order; it does not discard
the correction before the final merge. Strict floating-point compilation, final
tails and exact caller-owned scratch sizing are part of the implementation.
The 8,192-byte scratch is completely assigned per call and reused only after
completion. The original serial default remains available and unchanged.

Inspect [BabelStreamPort.cs](../Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Runtime/BabelStreamPort.cs)
and [actual-Burst correctness fixtures](../BenchmarkProject/Assets/DataLayoutCalibrator/Runtime/BabelDotCorrectness.cs).

### Recorded comparison

Core Ultra 7 265K; Unity IL2CPP/Burst and a same-machine Windows/OpenMP variant.
Six balanced fresh-process blocks contain three arms each (18 formal processes).
Operation means use iterations 1–99; storage lifetime contains all 100 iterations.

| Endpoint | Original serial Burst | Parallel Burst | Native OpenMP |
| --- | ---: | ---: | ---: |
| Dot mean | 29.238 ms | 8.244 ms | 8.424 ms |
| 100-iteration storage lifetime mean | 9,344.922 ms | 7,319.567 ms | 7,139.055 ms |

Relative to original Burst, Dot time fell **71.8%** and storage-lifetime time fell
**21.7%**. The optimized lifetime remained **2.53% slower than OpenMP**. A small
Dot-only advantage over that native variant is not an overall native win.

Dot includes chunk/merge work and Schedule/Complete. Storage lifetime additionally
includes scratch allocation/initialization and disposal, array storage, setup,
all iterations and one complete array export. Caller-owned export-buffer allocation,
file I/O, numerical checking and process/Unity startup are outside that boundary.
Do not call this complete application wall-clock latency.

### Validation, tradeoffs and decision

The retained audit reports full checks of all array values and final Dot across
25 full-default processes, including discovery and prerequisite checks. Additional
actual-Burst fixtures cover cancellation, small inputs, chunk tails, scratch
poisoning and repeated results at two worker counts. Array equality and Dot's
numerical tolerance are distinct checks; cross-platform bitwise reproducibility
is not established.

Copy, Mul and Triad regressed by 1.39%, 1.65% and 1.87% versus original Burst.
Mul/Triad intervals did not satisfy the predeclared practical no-regression check;
export and disposal also slowed. These costs remain included in the improved
lifetime. Actual ISA dispatch, equal core occupancy and the mechanism of those
regressions were not established. Allocation eligibility remains Unknown.

**Decision:** retain the parallel path as an explicit candidate and report both
kernel and storage-lifetime effects. Do not promote a deployment profile or claim
a general layout gain.

[Full report and confidence intervals](BABEL_DOT_REPORT_2026-09-15.md) ·
[Protocol](BABEL_DOT_PROTOCOL_2026-09-15.md) ·
[Process rows](evidence/babel-dot-20260915/summary.json) ·
[Windows reproduction](BABEL_DOT_REPRODUCE_WINDOWS.md)

## 2. Improve a complete SPH task, then reject an unhelpful layout change

### Problem and controlled comparison

A faster isolated particle loop would not establish a faster application. This
case uses a pinned SimplePH Poiseuille task: 768 particles, including 576 fluid
particles, 4,000 steps, five VTU snapshots and a velocity-profile CSV consumer.

The baseline was strengthened before attributing any benefit to layout. Original
and tuned AoS both retain `std::vector<Particle>`; the tuned arm reuses cell and
neighbor buffers between steps. All custom and actual LLAMA layouts receive that
same reuse improvement, shared correctness repairs, double-precision solver,
numerical ordering, compiler flags and one OpenMP thread.

Inspect the [source and task-boundary audit](WHOLE_TASK_AUDIT_2026-09-15.md).
The native C++ storage adapters do not validate Unity generated storage or Burst
performance, and the host's GPU is not used.

### Recorded complete-task comparison

72 fresh native processes, 12 per arm, on a shared Xeon Platinum 8470Q host.
The task boundary includes native process launch/completion, input creation,
allocation/conversion, simulation, neighbor maintenance, periodic/terminal export,
cleanup, XML result consumption and CSV output. Durable disk flush is excluded.

| Implementation | Mean complete-task time | Meaning |
| --- | ---: | --- |
| Original AoS lifecycle, with shared correctness repairs | 5.126 s | External application reference |
| AoS plus reusable buffers | 4.319 s | 15.7% lower latency than original |
| Custom field-SoA | 4.438 s | 2.75% slower than tuned AoS |
| Custom field-AoSoA8 | 4.405 s | 1.97% slower than tuned AoS |
| Actual LLAMA MultiBlobSoA | 4.493 s | External layout reference |
| Actual LLAMA AoSoA8 | 4.646 s | External layout reference |

Custom AoSoA8 reduces mean latency by 5.20% versus its matching LLAMA arm, but it
still loses to tuned AoS. An external-library comparison is not a reason to choose
a slower application implementation.

### Validation, tradeoffs and decision

All outputs matched the separately validated complete simulation and consumer
results. Orders were prospectively balanced; the six declared custom-layout
comparisons used adjusted paired-bootstrap intervals. Every sample was retained,
including 16 that missed an original quiet-CPU threshold. This is **shared-host
observed performance**, not isolated-hardware or cross-machine evidence.

The earlier quiet-only failure is a separate record and is not mixed into this
cohort. No selector calibration cost, selected execution or payback was measured
in the completed fixed-choice comparison.

**Decision:** keep AoS plus reusable buffers for this input. The demonstrated
improvement comes from buffer lifecycle, not changing away from AoS. Stop expanding
the selector experiment for this particular workload without a new application need.

[Full report and adjusted intervals](FOCUSED_COMPLETE_TASK_2026-09-15.md) ·
[Machine-readable summary](evidence/focused-full-task-20260915/summary.json) ·
[Earlier execution and failures](WHOLE_TASK_LINUX_EXECUTION_2026-09-15.md)

## What the two cases establish

They demonstrate parallel work decomposition with numerical constraints, complete
cost accounting, a strengthened conventional baseline, and a decision to reject
an unprofitable optimization. They do **not** demonstrate profitable automatic
calibration, general SoA/AoSoA superiority, GPU optimization, or allocation-qualified
deployment. See the [evidence status](EVIDENCE_STATUS.md) before reusing a claim.
