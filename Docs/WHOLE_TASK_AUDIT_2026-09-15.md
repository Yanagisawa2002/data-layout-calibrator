# Complete layout lifecycle: source audit and task boundary

Status at source preparation: **unmeasured**. This document records the audit and
planned boundary, not a successful experiment or deployment decision.

This is the historical Windows-first preparation snapshot. The subsequent
[Linux execution report](WHOLE_TASK_LINUX_EXECUTION_2026-09-15.md) records the
actual toolchain, correctness-only checkpoint boundary, repaired indexing,
completed validation and incomplete discovery outcome.

## Source and actual caller

The base is data-layout-calibrator main
`a235eed2748045f37e1da2d9d77f80d2489ef466` (PR 8 merged), in the independent
`codex/layout-whole-task-20260915` branch. The prior BabelStream checkout and
all of its evidence are read-only. No company SUMMIT source or assets enter this
work. No changes to the resume, website or LinkedIn are part of this branch.

The repository's own callers are benchmark hosts and sample scenarios.
`Tools/Examples/LifetimeDecision/Program.cs` supplies explicitly synthetic costs;
its `CalibrationHostExample.Run` is compiled but not invoked. The particle and
transform scenarios have canonical buffers and generated storage, but no shipping
application consuming a complete simulation. BabelStream and LLAMA code_comp are
benchmarks; their earlier cross-thread/compiler results cannot answer this task.

The selected public application is [SimplePH at b4d7512](https://github.com/rostanda/SimplePH/tree/b4d7512ed06873ac188b20ebf1639f1c4cf83afc),
MIT, copyright Daniel Rostan. Its `python/run_poiseuille_flow.py` configures a
pressure-driven channel flow with a lattice of fluid and boundary particles,
Wendland C4 kernel, Tait equation of state and velocity Verlet integration.
Resolution, Reynolds number and step count are real application inputs.
`Solver::run` periodically calls `VTKWriter::write` before the current step.
The exported pressure, density, type, position and velocity fields feed
`python/plot_poiseuille_flow.py` and VTU readers such as ParaView.

The complete source snapshot and per-file Git blob/SHA-256 identities are in
[upstream-lock.json](../Tools/WholeTask/upstream-lock.json). Downloaded upstream
bytes remain unchanged. [prepare_sources.py](../Tools/WholeTask/prepare_sources.py)
generates a separate adapted tree plus a complete unified diff and hash manifest.

## Audited implementation

| Area | Confirmed behavior | Consequence |
| --- | --- | --- |
| Adoption | `ICalibrationScenarioFactory` creates candidates; `IBoundaryCost` defines ingress/export. The old engine models resident blocks plus separate boundary scores. | Periodic exports must be part of the measured task, not added as marginal P95s. |
| Storage generation | `ProductionStorageEmitter` emits Unity NativeArray owners and codecs. Packed float4 supports the declared hot field types. Kernels/scheduling remain handwritten. | Native C++ field-storage adapters in this study do not validate Unity generated codecs or Burst AOT. |
| Selection | `LayoutSelector` ranks component-P95 scores, rejects insufficient allocation scope, and confirms a frozen candidate on holdout. | Complete application observations need an explicit additive API. Existing score semantics and gates remain unchanged. |
| Lifetime | `LifecycleCollector` supports ingress, ticks, one terminal export and optionally construction/disposal. | It has no explicit periodic-output consumer boundary. Do not relabel its old records. |
| Allocation | The real Unity 1 MiB control historically failed. `ThreadManagedAllocationCounter` rejects zero/unavailable controls. | A .NET current-thread control is specific to .NET; native/worker/Unity scope remains Unknown. |
| Application updates | `Solver::step` performs integrate1, cell-grid and neighbor rebuild, density, pressure, ghost boundary update, forces and integrate2. | Layout affects multiple actual processing passes and index construction. |
| Application maintenance | `CellGrid::update_neighbors` clears the outer neighbor vector each tick. `build` allocates nested per-thread cell vectors each tick. | Reuse these buffers in every tuned arm; compare layout gains against tuned AoS. |
| Application initialization | Solver constructor passes uninitialized scalar members into EOS/calculators. | Initialize these scalars to zero identically in every arm before public setters establish physical parameters. This is a common correctness repair, not a layout optimization. |
| Export errors | Upstream VTU writer prints and returns on open failure. | The adapter throws on failed open/write; incomplete outputs cannot become passing observations. |

## One connected experiment

The C++ host translates the public Python input setup and calls the actual
`Solver::run`/VTU writer. The same adapted solver is compiled for each storage
type. All hot math, branch policy, neighbor order, precision, index structures
and one-thread resource budget are shared. Both x/y and vx/vy remain upstream
`std::array<double,2>` leaf fields: these are **field-SoA/field-AoSoA**, not a
claim that every scalar component has its own array. Optional fields and their
engagement flags are preserved, including currently disabled features.

Arms: original AoS application storage; tuned AoS with reusable index buffers;
handwritten field-SoA and field-AoSoA8; actual pinned LLAMA `MultiBlobSoA` and
`AoSoA<8>` mappings. LLAMA calls `allocView` and its real reference mapping;
the old handwritten `nbody_code_comp` files are not an external-library stand-in.
LLAMA/MPL-2.0 and the pinned Boost headers/BSL-1.0 retain their notices.

The Python workflow executes the selected binary in a fresh directory, consumes
all actual VTU snapshots, and writes row velocity profiles, volume flow, kinetic
energy and analytical diagnostics. This is a headless adaptation of the upstream
profile consumer, using the standard-library XML parser. It does not measure a
ParaView UI or claim a validated replacement for scientific CFD software.

The additive package API `WholeTaskLayoutSelector.Select` receives only the
predeclared calibration partition. It ranks mean complete-task wall times with
paired fresh-process bootstrap uncertainty and a 2% practical threshold. It
keeps tuned AoS on ties/inconclusive evidence. It returns a **timing-only**
recommendation with `AllocationEligibility=Unknown`; no deployment profile.
The host freezes the decision before executing independent inputs. The
experiment executes a separate selected-strategy process, even if it selected
the same binary as a fixed baseline. Confirmation timings are never re-ranked to
change that recommendation. The measured oracle is a separate descriptive row.
`WholeTaskLayoutSelector.Confirm` accepts only that frozen candidate and separate
baseline executions; it rejects reused calibration input/process identities,
unpaired observations and unqualified environments.

## Cost, correctness and limits

Primary boundary: native process launch; canonical input generation and input
checkpoint; solver construction; complete layout allocation/conversion; all
steps and cell-index maintenance; periodic VTU writes; terminal VTU/checkpoint;
disposal; XML result consumption and velocity-profile CSV output. A terminal
snapshot and full-precision input/final checkpoints are explicit additions to
the public CLI contract, common to every arm. Writes complete to the OS cache;
durable media flush is not promised. Instrumentation JSON/hashing and outer
controller startup are outside the task timer. The native interval and consumer
cost are also retained, without treating either as the full task.

The selector campaign additionally charges the complete calibration campaign and
decision-host wall time. Show first-use and measured reuse, not an uncharged
steady-state winner. Any long-run extrapolation must be labeled a model.

Parity covers complete high-precision particle fields, optional engagement,
every scheduled VTU field/topology record, and consumed profile output. Store
full files and hashes; no checksum-only correctness. Preserve all failed builds,
runs and numerical mismatches. Physical diagnostics are separate from equality
with the upstream algorithm; matching outputs do not establish physical accuracy.

Record individual step intervals including scheduled exports directly; do not
sum component P95s. Inner steps are not independent repetitions. Process-level
uncertainty uses paired processes. Tail percentiles from a few complete-task
processes are descriptive only; hardware counters, GPU time and deployment
allocation coverage stay unavailable/Unknown unless actually measured.

The finite matrix is frozen after source/correctness/phase-cost discovery. It must
include small/medium/large particle counts, short lifetimes and high export
frequency, with a matched adverse lifetime/cadence case. Confirmation uses new
resolution/initial coordinates and Reynolds input. This study is native MSVC
C++ plus the real C# decision layer on one machine; Unity/Burst Player performance,
default promotion and portable hardware claims are out of scope.

## Coordination

All local compilation, correctness/performance runs and invasive diagnostics
wait for both hlsl and summit terminal/hardwareReleased handoffs, then independently
take `Local\CodexR9700VNextUnityGpu`. Each foreground stage checks the current
CPU/GPU load, competing processes and disk reserve. No OS queue, driver-cache
clearing, machine setting changes or unrelated process termination is used.

Per-task preflight observes three 250 ms CPU intervals, requiring average <=15%
and every interval <=25%. During the native task and consumer, low-frequency
Win32 total/owned-process CPU accounting records other activity; background mean
<=15% and each observed interval <=25% are required. Failed/unknown coverage is
not eligible. No monitoring cost is subtracted from elapsed time. The final
freeze additionally sets a fixed-baseline drift rule; qualification is assessed
for whole cohorts, never by deleting slow processes.
