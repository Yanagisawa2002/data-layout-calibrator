# One Linux caller and calibration-cost attempt

## Frozen question

Can the existing AoSoA8 path replace **AoS with reusable neighbor buffers** and
repay calibration within six actual caller uses? This is one prospectively fixed
SPH case, two existing arms, six balanced calibration pairs and, only after a
passing calibration, six balanced confirmation pairs on different input bytes.
There is no discovery matrix, new layout, parameter scan or second attempt.

[Protocol](../Tools/WholeTask/single-attempt-protocol.json) ·
[Caller](../Tools/WholeTask/single_attempt.py) ·
[Explicit validation entry](../Tools/WholeTask/run_single_attempt.sh)

The caller invokes the existing native solver, reads all requested VTU snapshots
and writes/validates the velocity-profile CSV. It calls the actual package
`WholeTaskLayoutSelector.Select`, freezes its output, and resolves that exact
configuration before each selected use. Source/binary fingerprints and the
decision hash reject changes. Confirmation cannot change the selected layout.

## Costs and allocation coverage

Selection compares the complete **monitored caller wall time**, including resource
checks, native launch, input generation, conversion/ingress, simulation, exports,
disposal, XML/CSV consumption and byte verification. Native ingress and workflow
latencies remain descriptive subcosts. They are not added again to caller time.
Calibration correctness runs, full-particle physics checks, monitoring, allocation
controls, JSON and the .NET selector process are charged upfront. Confirmation
qualification and the confirmation API charge are also included before claiming
payback. Build and environment provisioning are separate experimental setup costs.
Durable filesystem flush and controller-interpreter startup are outside this boundary.

Only actual paired uses can establish measured recovery. A stationary-mean
break-even extrapolation, if available, is labeled modeled. A retained AoS,
unconfirmed gain or invalid evidence cannot produce a finite payback claim.

The host now records bytes allocated by the actual Select/Confirm call, using
the existing current-thread managed counter after its real escaping 1 MiB and
empty controls. This first-call window also includes timing calls and JIT effects;
JSON/controls are outside the allocation window and inside the caller time.
Native C++, worker-managed, Python and Unity IL2CPP allocation coverage remain
**Unknown**. This is not an IL2CPP counter repair or a qualified deployment profile.

## Stop and historical boundaries

Any failed build, numerical, allocation-control, environment or drift gate stops
the route. Calibration fallback ends it before confirmation. The run uses a real
exclusive file lock, at most four compilation workers, one workload CPU and a
60-minute outer hard timeout. It preserves every started observation.

The [earlier Linux NO-GO](WHOLE_TASK_LINUX_EXECUTION_2026-09-15.md),
[fixed-task AoS reuse result](FOCUSED_COMPLETE_TASK_2026-09-15.md), original
README speed claims and failed Unity allocation positive control keep their
original scope. This attempt does not supersede those measurements.

## Execution receipt

Pending the single frozen validation. A failed or skipped run will retain its
commands, source identity, raw outputs and evidence hashes here without a retry.
