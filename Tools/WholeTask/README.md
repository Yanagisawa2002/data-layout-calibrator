# Whole-task layout adoption

This implements a connected path from a pinned public application through complete
lifecycle observations to the package's C# selector and independent confirmation.
The September 15 run passed numerical acceptance, but discovery stopped at a
required resource gate; formal selection and confirmation were not executed.
Read [the execution outcome](../../Docs/WHOLE_TASK_LINUX_EXECUTION_2026-09-15.md) and
[the source audit and boundaries](../../Docs/WHOLE_TASK_AUDIT_2026-09-15.md).
The study is native C++ plus C# decision logic; it is not Unity Player evidence.

Source preparation (small source files, no builds):

```powershell
python Tools/WholeTask/prepare_sources.py --fetch
```

Windows heavy stages require the campaign's predecessors and shared hardware
mutex. Linux uses its own explicit host grant and actual foreground `flock`.
Use a fresh output name for every attempt, for example on Windows:

```powershell
python Tools/WholeTask/exclusive_stage.py --output Artifacts/whole-task/stage-build-01 -- python Tools/WholeTask/build_native.py --output Artifacts/whole-task/build-01
```

The source generator verifies the exact public Git blobs/SHA-256 lock before
adapting source. Each build retains compiler command, full output, common source
patch, executable identities and all failures. Never edit `Upstream~` or overwrite
an artifact directory. See the [Linux execution record](../../Docs/WHOLE_TASK_LINUX_EXECUTION_2026-09-15.md)
for toolchain identities, shared correctness repairs and evidence boundaries.

## Linux stages and task contract

The native application and .NET host run without a graphics backend. A Linux
build uses the same GCC optimization/precision/OpenMP configuration for every
layout. `linux_stage.py` verifies an actual ancestor `flock`, reserves 10 GiB
on the data volume, and pins the campaign and all children to its recorded CPU.
Host logical CPU count is never substituted for the container CPU quota.

In order: compile and functional tests, separate correctness mode, long
Poiseuille physics check, independent discovery, measured time/disk budget,
formal input correctness, frozen calibration, independent confirmation and
total-cost analysis. `campaign.py` accepts `--phase correctness|discovery|
calibration|confirmation`. Timing requires a completed `--correctness` receipt
for the identical case and binary; formal phases additionally require the
`--physics` gates, `--budget`, `--protocol` and `--selector` DLL/executable.

The task delivers every scheduled VTU plus **one terminal VTU after the final
step**, then reads the real files to produce a velocity-profile CSV. The terminal
snapshot extends upstream's periodic, before-step export contract to provide
the final result of a finite invocation. Its cost is included equally in every
arm. Full-precision binary checkpoints exist only in separate correctness mode.

`make_protocol.py` fixes five reviewed scenarios and balanced fresh-process
orders, using completed discovery only to forecast time/disk requirements.
Failed environment or baseline-drift cohorts are retained in full and cannot
be trimmed. A selected strategy is executed as an additional real process;
confirmation never substitutes the fastest fixed-arm observation. The measured
first-use and reuse totals add the actual calibration and decision-host wall
time. Modeled break-even counts are labeled as extrapolations.

## Files

- `prepare_sources.py`: pinned application and actual LLAMA/Boost source, explicit common adaptations.
- `layout_storage.hpp`: complete field-preserving storage bridge with original/tuned AoS, field-SoA, field-AoSoA8 and actual LLAMA mappings.
- `simpleph_task.cpp`: public Poiseuille setup calling the upstream solver and writer.
- `task_trace.hpp`: direct tick observations; phase instrumentation compiled only in separate diagnostics.
- `workflow.py`: complete invocation and real VTU profile consumer.
- `SelectorHost`: .NET host that invokes the new package API, retaining the actual 1 MiB current-thread control separately from unknown native eligibility.
- `exclusive_stage.py`: foreground predecessor/mutex/load/disk guard; it never schedules a deferred run.
- `linux_stage.py` / `telemetry.py`: actual Linux lock/affinity/cgroup/SMT resource gates, retaining all observations.
- `cell_grid_contract.cpp`: supported-domain periodic insertion/lookup and invalid-input regression, shared across layouts.
- `physics_check.py`: full-particle velocity comparison against the transient analytical Poiseuille solution.
- `make_protocol.py` / `analyze_campaign.py`: budgeted five-scenario protocol and paired uncertainty/complete selection costs.

The original `LayoutSelector` component-score API is unchanged. The additive
`WholeTaskLayoutSelector` requires balanced, observed full-task process evidence,
valid parity, fixed source/input/boundary and at least three independent pairs.
It returns a provisional timing recommendation only. The experiment must freeze
and execute it before inspecting confirmation outcomes.
