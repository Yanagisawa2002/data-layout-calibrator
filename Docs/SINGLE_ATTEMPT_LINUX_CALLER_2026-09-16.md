# One Linux caller and calibration-cost attempt

**FAILED / performance NO-GO.** The only run stopped before its fifth calibration
process because the fixed CPU's SMT sibling exceeded the existing quiet gate.
There was no retry, core reselection or replacement observation. Selection,
confirmation, actual selector allocation bytes and calibration payback remain
**unavailable**. The candidate is retained for review as a Draft delivery.

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

- Base: `c37cfca47ac240c90e5382310f339044e9c0f5b6` (fresh GitHub main).
- Frozen implementation: `59756d909902fbbc3920959dde874dedef49a3d7`, clean checkout.
- Run ID: `single-attempt-linux-caller-20260916`.
- Remote validation: 2026-09-15 **18:05:43–18:06:38 UTC**, exit **1**.
- Ubuntu 22.04, Xeon Platinum 8470Q, 25-core CPU quota, 90 GiB memory quota,
  GCC 11.4.0, Python 3.12.3, .NET SDK 10.0.401 / runtime 10.0.12. GPU unused.
- No owned processes remained; the exclusive lock was released. The server was
  left running for the queue coordinator.

| Check or stage | Observed result |
| --- | --- |
| Local functional tests | 95 passed; exit 0 |
| Python protocol/ledger/config tests | 11 passed; exit 0 |
| Selector host build | Passed, 7 DTO-field warnings, 0 errors |
| Native storage checks | 10 cases per arm, both passed |
| Cell-grid contracts | Both passed: 13 particles, 2 reuse cases, 8 invalid cases each |
| Full-output correctness and physics | Both arms passed; 4,000 steps, 576 fluid particles, full velocity L2 0.0088641974 < 0.3 |
| .NET allocation probe | 1,048,600 bytes observed for 1 MiB positive control; empty control passed |
| Calibration | 4/12 planned processes completed; fifth rejected before native launch |
| Select / configured application / confirmation / payback | Not executed / unavailable |
| Actual Select/Confirm allocation bytes | Unavailable; instrumentation compiled but API window not exercised |
| Unity IL2CPP / native allocation qualification | Skipped on Linux / Unknown |

The blocking preflight observed **12.987% SMT sibling mean load**, above the
frozen 10% limit. The selected CPU's background mean was 8.219%, container
background 0.02635 cores, and throttled periods zero. Only the sibling gate
failed. The initial core selection and all four completed process gates passed;
this does not qualify an incomplete cohort for a speed comparison.

The partial route consumed 31,670.60 ms including correctness/physics and
monitoring. This is a failed-attempt cost, not a completed calibration-cost
estimate. No break-even number or winning layout is inferred from four samples.

| Order | Existing arm | Complete workflow ms | Monitored caller ms |
| --- | --- | ---: | ---: |
| 1 | tuned AoS | 4,296.733 | 5,061.227 |
| 2 | field AoSoA8 | 4,404.707 | 5,170.038 |
| 3 | field AoSoA8 | 4,387.814 | 5,153.219 |
| 4 | tuned AoS | 4,380.985 | 5,143.884 |

These are retained individual observations, **not an accepted comparison**.
The existing recommendation to keep AoS plus reusable buffers remains based on
the earlier fixed-task evidence. No runtime/package default was changed.

### Evidence

[Machine-readable receipt and hashes](evidence/single-attempt-linux-20260916/receipt.json) ·
[Failure summary](evidence/single-attempt-linux-20260916/summary.json) ·
[Failed preflight](evidence/single-attempt-linux-20260916/failed-preflight.json) ·
[Allocation control](evidence/single-attempt-linux-20260916/allocation-control.json) ·
[Source manifest](evidence/single-attempt-linux-20260916/source-manifest.json) ·
[Raw evidence archive](evidence/single-attempt-linux-20260916/raw-evidence.tar.gz)

Archive SHA-256:
`66fd4a13456159b573c0a8820905e4a1a8d9bce7cdcccbcfb7becdd3dac7bfcc`.
It retains the exact source bundle, compiled binaries, compiler commands/logs,
all input/checkpoint/VTU/CSV outputs, telemetry and exit receipts. Download
verification checked 70 persisted-file hashes across six completed application
records and both executable hashes. Environment setup and failed preflight
inspection logs remain in the local queue receipt; no credentials are published.

Any future attempt requires separate authorization. This PR contains no resumed
experiment, automatic merge or release.
