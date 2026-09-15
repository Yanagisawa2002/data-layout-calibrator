# Whole-task layout validation: Linux execution record

## Outcome

**Numerical acceptance passed. Performance validation is NO-GO for this time
period because the required discovery protocol did not complete.** This is a
resource/coverage result; it does not establish that layouts have no benefit.
Formal calibration, a selected-layout application, independent confirmation,
confidence intervals and measured calibration payback were **not executed**.
Deployment and whole-task allocation eligibility remain **Unknown**.

The final discovery executed all 24 ordinary application observations, each
with passing output and resource gates. The following required diagnostic
preflight failed on SMT sibling load. Those 24 observations remain individually
qualified raw discovery data; they are not discarded or promoted to a completed
comparison. There is no retry, new core selection or inferred winning layout.

### Verified delivery

- GCC built all eight application executables, including two separate diagnostic
  builds; six storage contracts passed ten cases each. Six cell-grid contracts
  checked 13 particles, two reuse passes and eight invalid-input rejections each.
- Final-source correctness completed **56/56 applications: seven cases across
  eight executables**. Complete input/state, VTU and consumed CSV bytes matched
  within each case. These correctness-mode timings are all ineligible.
- All eight long physical checks passed: 4,000 SPH steps, 25.0868 simulated
  seconds, 576 fluid particles and relative full-velocity L2 **0.00886420**,
  below the declared 0.3 threshold after 24.7597 relaxation times. This is the
  declared adapted-application acceptance, not a rerun of upstream Python tests.
- The final failing-input ASan/UBSan regression passed. The earlier sanitizer
  failure and the intermediate incorrect boundary fixture remain in evidence.
- **95/95 C# functional tests** passed on .NET; the final controller's **7/7
  Python contracts** and the CI policy checks passed. The separate unchanged
  renderer's six contract tests passed locally; it was not installed remotely.
- The actual .NET current-thread 1 MiB allocation control observed **1,048,600
  bytes**, with the empty control passing. This does not cover worker/native
  allocation or repair the separate historical Unity control.

### Final comparison status

All arms share the same SPH algorithms, double precision, compilation flags,
thread budget, output contract and common correctness repairs. Index-buffer
reuse is shared by every tuned arm, so it cannot be presented as layout gain.

| Arm | Storage / maintenance | Final complete-output cases | Completed discovery observations | Confirmed task gain / selection payback |
| --- | --- | --- | --- | --- |
| `original-aos` | Original AoS allocation lifecycle | 7/7 | 4, individually qualified | unavailable |
| `tuned-aos` | AoS with reusable index/neighbor buffers | 7/7 | 4, individually qualified | unavailable |
| `field-soa` | Field-SoA with the same buffer reuse | 7/7 | 4, individually qualified | unavailable |
| `field-aosoa8` | Field-AoSoA8 with the same buffer reuse | 7/7 | 4, individually qualified | unavailable |
| `llama-soa` | Actual pinned LLAMA MultiBlobSoA | 7/7 | 4, individually qualified | unavailable |
| `llama-aosoa8` | Actual pinned LLAMA AoSoA8 | 7/7 | 4, individually qualified | unavailable |
| Selected strategy | Actual C# API implemented and functionally tested | Formal application not run | Not run | unavailable |

The two instrumented builds also passed all seven correctness cases. They are
diagnostics and never candidates for ranking. Each ordinary discovery cell/arm
has only one process, in discovery order; there is no balanced confirmatory
replication or process-level uncertainty estimate. Raw latencies are retained
in [discovery-observations.json](evidence/whole-task-linux-20260915/discovery-observations.json),
without a speedup table or a layout recommendation.

The executed inputs cover 240, 273, 768 and 2,688 total particles; a perturbed
13-resolution tail; 2/8/64-step short invocations; every-step export; and the
4,000-step physical check. The planned five-cell independent confirmation
matrix and its 740 formal processes were not launched. Its budget generator,
source/input freeze and complete-cost analyzer are implemented but not validated
by a completed application calibration/confirmation run.

## Resource and toolchain identity

- Remote workspace: `/root/autodl-tmp/codex-whole-task-20260915/data-layout`.
- Intel Xeon Platinum 8470Q; 208 host logical CPUs, two NUMA nodes. The cgroup
  quota is **25 core-time units**, not 208 dedicated cores. Memory limit: 90 GiB.
- Formal CPU stages pin one logical CPU, check its SMT sibling, account for
  cgroup usage/throttling, and retain at least 10 GiB free on the data volume.
- Every remote heavy stage is inside the coordinator SSH helper's actual
  foreground `flock`. `linux_stage.py` additionally verifies the ancestor lock
  descriptor and contention, then records load, space, CPU and owned processes.
  The original Windows predecessor/mutex queue continues to govern Windows.
- Isolated Python 3.12.11 from the [Astral 20250818 release](https://github.com/astral-sh/python-build-standalone/releases/tag/20250818),
  archive SHA-256 `b5a4f189f25cbacba0f76c9bd6f3ea8c35d2064068aa74ccbb6863068caababd`.
- Isolated .NET SDK 10.0.401 / runtime 10.0.12 from [Microsoft's release metadata](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json),
  archive SHA-512 `51c8b999af9e8dd9998c9edc5944e19a90788862068acd38694e098889054ce8c23d4f0c5cccfa16bf187d044562359e5ee69a9f8ad0bbe913ba90311fbce25b`.
- GCC 11.4: common `-O2 -std=c++20 -fopenmp -fno-fast-math -ffp-contract=off
  -march=x86-64 -mavx2 -mtune=generic`; one OpenMP thread in all arms.

The GPU is not used. These observations do not validate Unity/Burst, AOT,
Player deployment, GPU performance, or the earlier Windows measurements.

## Failures retained and shared correctness repairs

1. The first source hash check raced an incomplete SFTP transfer and aborted
   before extraction or compilation. `build-stage-01` is retained. A completed
   transfer and a new stage passed the archive hash before compiling.
2. `validation-01` completed the native build, storage contracts, C# build and
   94 then-current functional tests, but the unchanged renderer tests could not
   import Pillow. Its six contract tests subsequently passed locally using the
   existing Python/Pillow and immutable historical fixture. The remote core
   validation avoids installing an unrelated renderer or uploading old results.
3. The small full application case matched across all eight executables. The
   legal 13-resolution perturbed case failed in original AoS. `sanitizer-01`
  identified a heap-buffer-overflow at `CellGrid::build` insertion; the original
  application has the same unguarded indexing in lookup.
4. The first new neighbor fixture used coordinates at +/-L, outside this
   application's centered +/-L/2 domain. Its generic modulo-distance reference
   differed from the upstream single-period distance convention. That failed
   `native-build-02` contract is retained. The regression now uses the caller's
   actual boundaries and explicitly rejects values outside the supported domain.

For a tiny negative coordinate, `fmod(x,L)+L` can round to exactly `L`; dividing
by the cell width then yields the cell count rather than a valid index. The
mechanical adaptation now clamps insertion and lookup indices in **all arms**,
including the original AoS lifecycle. Geometry and particle coordinates are
checked for validity and the centered periodic domain before conversion/OpenMP
so the clamp cannot conceal NaN, infinity or out-of-domain coordinates. The original failed binary is diagnostic evidence, never a timing
baseline. No upstream source bytes in `Upstream~` are edited.

The new cell-grid contract compares complete neighbor membership and
multiplicity to a direct distance reference for tiny negative coordinates,
zero, exact periodic boundaries, adjacent representable values and a 13-particle
tail. It repeats after reuse and checks that invalid coordinates/geometry fail.
The actual failing application input remains an ASan/UBSan regression.

## Timing and evidence boundary

Correctness and performance use the same native executable with an explicit
mode. Only correctness writes full-precision `input.bin`/`state.bin`. A timing
run requires a separate full-output receipt for its exact case and executable;
it verifies all required VTU/CSV bytes against that receipt after timing. Input
hashes originate from the actual canonical correctness output, not a renamed
iteration or a synthetic input label.

The complete task includes blocking native-process startup/completion, input
generation, storage allocation/conversion, all real SPH steps and neighbor
maintenance, periodic and terminal VTU output, storage disposal, and the actual
VTU-to-velocity-profile CSV consumer. Global caches are not flushed; ordinary
file writes do not promise durable-device flush.

Linux `/proc/stat` has scheduler-tick resolution. A minimum final 250 ms monitor
context avoids labeling a zero-tick short interval as idle. This post-task
observation is outside the complete-task latency and remains charged to the
actual calibration caller's total cost. Measurements retain process-level
uncertainty and whole-cohort failure rules; ticks are not independent processes.

The C# selector's rejected best candidate is recorded separately from a
baseline fallback. A fallback reports zero recommended gain. The current-thread
allocation control is separate from unknown native/worker allocation scope.

## Resource failure and retained attempts

### Resource qualification is not an algorithm result

The first performance discovery stopped at a per-task gate: CPU2's SMT sibling
106 averaged 19.48% busy over that preflight, above the 10% sibling threshold.
The entire incomplete discovery is retained, without a performance conclusion.

A subsequent 30-second resource-only precheck required **both** SMT threads to
stay at or below 10% in **every** one-second interval; 0/101 candidate physical
cores passed that stricter rule. This differs from the original formal gate:
target CPU mean <=15%, target intervals <=25%, sibling aggregate mean <=10%,
container background <=0.25 core and no cgroup throttling.

Re-evaluating the same saved observations found 18/101 satisfying only the
original CPU/SMT portion. That window did not record cgroup usage/throttling, so
complete original-rule qualification is unavailable. Neither the original
failed precheck nor the partial diagnostic is relabeled as a performance pass.
This does not establish that the server has no suitable core, or that layouts
have no benefit. Numerical correctness proceeds separately under an actual
lock with `performanceEligible=false`; contention does not change its acceptance
criteria. No performance is launched from the diagnostic reassessment.

The final, explicitly authorized 30-second window recorded all 31 raw CPU and
cgroup snapshots. Its complete original policy was saved before sampling:
target mean <=15%, every target interval <=25%, sibling aggregate <=10%, cgroup
background <=0.25 core, no new throttling, stable quota/cpuset, and memory/disk
budgets. It excluded physical CPUs 0/1/2 and chose the first qualified physical
CPU by ascending identifier, using resource counts only. **96/101 qualified;
CPU3 with sibling107 was fixed for the entire discovery batch.** Background
cgroup usage was 0.0326937 core, with no new throttling. Full raw samples and
all candidate rejection reasons are in `resource-selection-03.json` in the
archive; the [pre-sampling policy](evidence/whole-task-linux-20260915/receipts/resource-selection-03-policy.json)
is also available directly.

`discovery-03` failed at 09:10:23 UTC, after its 24 ordinary observations and
one separate diagnostic. The required `diagnostic-aos` native child **did not
start**. Its [preflight receipt](evidence/whole-task-linux-20260915/receipts/discovery-03/diagnostics/diagnostic-aos/preflight.json)
records the exact failure:

| Field | Observed | Gate | Result |
| --- | ---: | ---: | --- |
| CPU107 sibling aggregate busy | 23.6842105% | <=10% | failed |
| CPU3 target aggregate busy | 0% | <=15% | passed |
| CPU3 target intervals | 0%, 0%, 0% | each <=25% | passed |
| cgroup background | 0.02134795 core | <=0.25 core | passed |
| New throttled periods / microseconds | 0 / 0 | 0 / 0 | passed |

Sibling interval values were 0%, 23.0769231% and 48%; the decision used the
predeclared aggregate threshold. No failure is hidden by the fact that the
preceding 24 applications qualified. The controller retained `FAILED.json`,
never emitted a complete discovery summary, and did not generate a protocol or
start formal calibration. The coordinator's final-attempt stop rule ended
performance attempts for this period.

## Exact source and evidence

The source remains based on `a235eed2748045f37e1da2d9d77f80d2489ef466` in an
independent `codex/` branch. Final native build `native-build-04` and C# tests
`validation-04` used `source-linux-07`, including explicit centered-domain
rejection. `correctness-final-01` and `sanitizer-03` validate those exact binaries.
`source-linux-08` changed only the Python during-task qualification guard, its
negative test and README. All 536 source files were verified, and unchanged
native/C#/workflow/physics bytes were checked before reusing the binaries and
receipts. The guard's seven tests passed remotely. Later edits are reporting and
evidence closeout only; source bundles preserve the exact tested bytes regardless
of Git checkout newline conversion.

| Source bundle | SHA-256 |
| --- | --- |
| `source-linux-07.tar.gz` | `1dd9efee820299f2a04dab7cb1ff678fc9baf560d1111478a4c04997b8014221` |
| `source-linux-08.tar.gz` | `db8a92ea04ec283b2e05e0bebd5d60a004293192ae768b1ad6037632839571c7` |

The [raw evidence archive](evidence/whole-task-linux-20260915/raw-evidence.tar.gz)
contains **3,787 files / 199,480,501 uncompressed bytes**, including every retained
application attempt, full checkpoints and VTU/CSV output, all failures and
telemetry, native binaries, exact source bundles, compiler commands, source
adaptation diffs and the final selector runtime. Its compressed size is
28,853,815 bytes and SHA-256 is
`dbabf4cabbb9c73e8dccc3732e1bb99d6cc4e64d4d2ea87e620e3c56f36f6a1a`.
The [per-file manifest](evidence/whole-task-linux-20260915/files.json) and
[local verification](evidence/whole-task-linux-20260915/local-verification.json)
confirm every transferred file hash. Toolchain downloads/caches, duplicate
extracted source trees and memory dumps are excluded; no application timing
or failed output is omitted. Upstream licenses remain with their source.

The [evidence index](evidence/whole-task-linux-20260915/README.md) lists the
inspectable receipts and retained shell entry points. Correctness compares full
persisted bytes as well as hashes. Earlier source passes are kept as history
and do not substitute for the final binary acceptance.

## Hardware release and limitations

[Release verification](evidence/whole-task-linux-20260915/release.json) at
09:15:47 UTC found no task-owned survivor; all 194 recorded process references
were absent. The actual hardware-lock inode `31152802564` was acquired
nonblocking and released. Stage completion identities and synchronous build
command exits are retained. Individual historical compiler subprocess PIDs
were not separately persisted; this coverage limit is explicit in the receipt.

The data disk had **52,166,291,456 bytes / 48.5836 GiB free**, above the 10 GiB
reserve. Source evidence and isolated Python/.NET dependencies remain on the
server for coordinated read-only reuse. No artifacts were deleted and the server
was not shut down. Hardware-terminal handoff was written after the complete
archive passed local verification; no further hardware stage is scheduled here.

Confirmed layout gains, end-to-end selection payback, native/worker allocation,
GPU time, hardware counters, Unity/Burst/AOT compatibility and Player deployment
are unavailable or Unknown as applicable. The existing production selector and
deployment defaults are unchanged. Resuming performance work requires a new
coordinated resource grant and the full frozen protocol; this run cannot fill
those missing results.

## Resume candidate statement

> Built a reproducible SPH application harness comparing AoS, field-SoA,
> field-AoSoA and actual LLAMA layouts across full simulation and VTU-consumer
> lifecycles; implemented a C# whole-task selector, verified 56 complete-output
> runs plus analytical and sanitizer checks, and enforced resource gates that
> prevented incomplete discovery data from becoming a performance claim.

This engineering/correctness statement is supported. A quantified layout
speedup or profitable adaptive selection is not supported by this run.
