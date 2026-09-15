# Complete SPH task: buffer reuse helps; layout switching does not

The focused comparison completed **72 fresh native processes**, with all outputs
matching the separately validated complete simulation and VTU/CSV results.
For this task, reusing neighbor buffers reduced mean caller latency by **15.7%**.
Both custom layouts were slower than the strengthened conventional AoS baseline.
Stop further layout-selector investment for this particular workload.

## Actual task and external comparisons

SimplePH Poiseuille simulation: 768 particles (576 fluid), 4,000 steps, 25.0868
simulated seconds, and five VTU snapshots consumed into a velocity-profile CSV.
Each measurement includes native process launch/completion, canonical input
creation, allocation/conversion, solver and neighbor maintenance, periodic and
terminal export, cleanup, XML reading, profile calculation and CSV output.
Ordinary buffered writes are included; durable disk flush is outside the contract.

All arms use the same double-precision solver, numerical ordering, GCC 11.4
flags and one OpenMP thread, pinned to CPU 96 on the rented Xeon Platinum 8470Q
host. **The RTX 5090 is not used.** Sources and common correctness adaptations
are described in the [source audit](WHOLE_TASK_AUDIT_2026-09-15.md) and
[earlier execution record](WHOLE_TASK_LINUX_EXECUTION_2026-09-15.md).
LLAMA arms instantiate the actual pinned external library, rather than copies
of the custom containers. These are layout comparisons within the same SPH
application, not a ranking of every fluid solver or every LLAMA configuration.

| Implementation | Mean complete task | Median complete task | Interpretation |
| --- | ---: | ---: | --- |
| Original SimplePH AoS lifecycle | 5.126 s | 5.125 s | External application reference, with shared correctness fixes |
| AoS with buffer reuse | **4.319 s** | **4.310 s** | Best measured fixed choice; 15.7% lower mean latency than original |
| Custom field-SoA | 4.438 s | 4.431 s | 2.75% slower than strengthened AoS |
| Custom field-AoSoA8 | 4.405 s | 4.396 s | 1.97% slower than strengthened AoS |
| LLAMA MultiBlobSoA | 4.493 s | 4.440 s | External layout reference |
| LLAMA AoSoA8 | 4.646 s | 4.634 s | External layout reference |

The source contrast isolates the buffer-lifecycle change: original and tuned
AoS both store `std::vector<Particle>`; the tuned arm retains local cell and
neighbor-list capacity between steps. All custom and LLAMA layouts receive
the same reuse improvement. Thus their improvement over original AoS cannot
be attributed entirely to layout. On this input, conversion and result
consumption are already included, and switching away from tuned AoS adds cost.

## Uncertainty and scope

Each arm ran 12 times in prospectively randomized cyclic orders, covering each
position twice. Every sample is retained. The paired bootstrap uses 20,000
resamples; the six declared custom-layout comparisons use Bonferroni-adjusted
two-sided intervals (99.167% per comparison, 95% family coverage under the
resampling assumptions).

Custom SoA versus tuned AoS has speedup **0.9732x [0.9668, 0.9813]**;
custom AoSoA8 versus tuned AoS has **0.9806x [0.9744, 0.9880]**. Both regress.
Against matching LLAMA layouts, custom SoA reduces mean latency by **1.21%**
(below the declared 2% practical threshold), and custom AoSoA8 by **5.20%**.
That external win does not make AoSoA8 preferable to tuned AoS for this task.

This is **observed shared-host performance**, not isolated-hardware evidence.
Sixteen of 72 processes missed an original quiet-CPU preflight or during-run
threshold; none were removed or replaced. Tuned-AoS half-to-half drift was
**1.03%**, below the prospectively specified 5% limit. Bootstrap intervals are
conditional on this session and do not establish other-input, other-machine,
Unity/Burst or GPU performance. No selector calibration or payback was measured.
The existing full-particle analytical check was rechecked for all six exact
binaries: relative velocity L2 **0.0088642**, below the declared 0.3 threshold.

The preceding quiet-only attempt stopped after four completed processes when
the fifth preflight detected CPU/SMT interference. It remains failed and is
preserved separately. Before the complete cohort began, the environmental
question was explicitly changed to shared-host observation; task, binaries and
candidate set stayed fixed. None of those four timings entered the 72 samples.

## Decision and reproducibility

Keep **AoS plus reusable neighbor buffers** for this workload. It demonstrates
a concrete improvement over the external application's original lifecycle;
the LLAMA comparison establishes honest external positioning. Do not claim a
profitable layout selector or a general layout speedup, and do not extend this
single case into a larger campaign automatically.

The [protocol](../Tools/WholeTask/focused-shared-protocol.json),
[runner](../Tools/WholeTask/focused_fixed_task.py),
[machine-readable result](evidence/focused-full-task-20260915/summary.json) and
[raw outputs](evidence/focused-full-task-20260915/raw-results.tar.gz) are retained.
The archive includes both attempts, exact executed runner versions, frozen
orders, telemetry and all emitted task files. SHA-256:
`aa8e348794ae8badb3e2bf6be7b823a893b047467b1e87f947bcf2b0928af67b`.
Exact pre-existing binaries and compiler commands remain in the earlier linked
execution archive. The complete cohort ran from 11:37:08 to 11:43:43 UTC.

Validation: seven existing offline protocol tests passed; synthetic paired
speedup, equal-latency and environment-label controls passed; the shared-host
option leaves the existing quiet-only workflow default unchanged. No recurring
automation or other project was resumed.
