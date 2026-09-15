# BabelStream parallel Dot: new-machine protocol, 2026-09-15

Starting main: `8e83a17338452c9d202f45ed553e0c807a7b8558`, refreshed before work.
Task-owned branch: `codex/babelstream-parallel-dot-20260915`.
This protocol and all measured inputs/executables are hashed before formal runs.
The September 10 data is historical and is never pooled with this machine.

## Implementation and independent exploration

Retain the original `BabelDotContractJob` as the serial baseline. One common
IL2CPP Player selects serial or parallel Dot at runtime; Copy/Mul/Add/Triad,
export, initialization, thread policy and build flags are shared. There is no
layout change and no deployment/default-candidate promotion.

The candidate computes contiguous fixed-size chunks in `IJobParallelFor` and
merges partials in ascending chunk index in a dependent `IJob`. Each partial has
a binary64 sum and Neumaier compensation; both components enter the merge.
Strict floating-point mode prevents reassociation of compensation expressions.
Chunk boundaries depend only on length and chunk size, not worker count or
scheduling order. Last chunks cover every remaining element; empty input is zero.
All arrays are caller owned, disjoint, kept alive until completion, and disposed
by the owner. Scratch has exactly ceil(N/chunk) entries and is fully overwritten
on each call. An outstanding reduction must complete before scratch is reused.

Discovery uses full defaults with chunks 16,384 / 65,536 / 262,144, then serial;
one fresh process each, all retained and excluded from formal inference.
Start from 65,536; select a stable practical size from this bounded exploration,
or report failure. No candidate tuning after the formal freeze.

Functional fixtures run actual Burst jobs in the Player with 1 and 19 workers.
An independent decimal oracle exactly sums dyadic products. Exact binary64
equality is required for these fixtures (no adjustable tolerance). Cases cover
zero/one/small counts, exact and partial chunks, nonuniform signed inputs,
large cancellation within and across chunks, poisoned scratch, repeated calls
and deterministic bits. Negative controls include deliberately omitted work
and the serial job's loss of a cancellation residual. These adversarial cases
are additional contracts; the serial baseline still uses unchanged upstream
acceptance for the benchmark. Nonfinite values must never pass that checker.

## Hardware, tools and prerequisites

New machine: Intel Core Ultra 7 265K, 20 OS-exposed cores/logical processors,
approximately 32 GB RAM. Record Windows CPU sets (core/efficiency/cache/NUMA),
affinity, RAM speed/capacity, actual OS and background load in environment.json.
No affinity, power, driver, priority, remote-server or user-process changes.

Native: installed MSVC 14.44.35207 toolset, Windows SDK 10.0.26100.0,
`/O2 /fp:strict /arch:AVX2 /std:c++20 /EHsc /MD /openmp`.
`OMP_NUM_THREADS=20`, `OMP_DYNAMIC=FALSE`; default placement.
Unity 6000.5.9f1, IL2CPP Release, Burst 1.8.30, Collections 6.5.0,
Mathematics 1.4.0. Both Burst arms use `-job-worker-count 19`, with possible main
thread participation; different APIs do not guarantee equal CPU occupancy.
The Player runs headless with the Null graphics device, no GPU workload.

The installed Editor lacks Windows IL2CPP support. Prepare a task-owned Editor
copy plus its matching official signed module; record download identity and
installation exit state. Preserve the original failed build. Require completed
Player build and AOT manifest entries for serial, partial and merge jobs.

Before formal runs, every arm must pass a fresh full-default process and the
independent persisted-byte checker. Failure bars formal measurement. Preserve
all failed attempts; no tolerance relaxation. Validate pinned upstream hashes.

## Fixed workload and timing boundaries

Unchanged pinned BabelStream source: `17ab377b0e919e14fd3df2b67268761fdac8abb3`.
33,554,432 doubles per array; 100 iterations, Copy/Mul/Add/Triad/Dot order;
initial values 0.1/0.2/0 and scalar 0.4. Both initialize twice. Retain upstream
native kernels, run_all and check_solution, license and variant label. This is
not a certified original BabelStream submission or an array-size scaling study.

Every Dot timer includes scheduling, local accumulator initialization, all chunk
work, writing partials, dependent merge and completion. Scratch allocation/zero
initialization happens once inside construction; disposal is inside lifetime.
Every iteration overwrites every partial, so no extra per-call clear is omitted.
Serial timing still includes Schedule/Complete and its original ascending loop.

Store all 100 timings per operation. Operation summaries exclude only iteration
zero. Storage lifetime includes allocation, two initializations, all 100 complete
iterations, one full canonical export and disposal. Caller-owned output buffers,
process/Unity startup, file I/O and correctness checks are outside this boundary
for all arms. Do not call this whole-application end-to-end time.

Every process persists and checks all 100,663,296 doubles and final Dot. Preserve
upstream symmetric relative limits: epsilon*100 for arrays and epsilon*10,000,000
for Dot. Save raw bytes, SHA-256, independent checker receipt and exit code.
Record fixed logical input hash separately as reconstructed identity, not a
runtime input readback. Keep `allocationEligibility=Unknown`: even a passing
thread-local allocation control does not cover worker/native workload allocation.

## Formal execution and statistics

Use the existing `Local\CodexR9700VNextUnityGpu` mutex for each build or workload.
Coordinate with the HLSL Scan task: BabelStream has priority. One child at a time;
no other benchmark or rebuild may overlap. Existing conflict detection and a
20 GiB free-disk reserve apply. Before each formal process, sample machine CPU
load for five seconds; require mean <=10%. Retry at most 12 windows before
aborting the phase with retained snapshots, without starting a timed process.
The gate is independent of performance outcomes. Preserve post-run load too.

Six independent fresh-process blocks, one process per arm in each block:

| Block | First | Second | Third |
| --- | --- | --- | --- |
| 1 | Native | Serial Burst | Parallel Burst |
| 2 | Serial Burst | Parallel Burst | Native |
| 3 | Parallel Burst | Native | Serial Burst |
| 4 | Native | Parallel Burst | Serial Burst |
| 5 | Parallel Burst | Serial Burst | Native |
| 6 | Serial Burst | Native | Parallel Burst |

Each arm appears twice in each position. Each pair has both orders three times.
Do not discard/select processes based on timing. Verify the frozen source,
protocol, environment, input identity and all shipped binary hashes before each.

Report each process, arithmetic means and paired differences with Student-t 95%
CI (six blocks, df=5, t=2.570581835636305). Independent units are processes, not
the 99 inner iterations. Compare parallel vs serial, and both vs native for Dot,
storage lifetime and all four other operations. A difference CI wholly below
zero is improvement, wholly above zero regression, otherwise inconclusive.
Also report whether the entire CI lies within +/-2% of reference as practical
equivalence; absence of significance does not prove absence of regression.
Percent reduction is 100*(reference mean - candidate mean)/reference mean.
A CI expressed in percent rescales the difference CI by the observed reference
mean; it is not a separately estimated ratio CI. No multiplicity adjustment,
population-wide, P95, hardware-counter or general CPU performance claim.

## Candidate freeze

Selected chunk: **65,536**, 512 partial entries (8,192 bytes) at the fixed workload.
This retains the starting design; the three discovery sizes were close, with
Dot process means 8.190001 / 8.145458 / 8.369871 ms respectively. Serial discovery
was 29.193871 ms. All four passed complete persisted-output checks. These are
single exploratory processes, not inferential results or a proven optimum.
The implementation did not change after these observations. Formal freeze.json
is permitted only after all three additional full-default prerequisites pass.
The recorded Player build source identity retains its actual pre-commit dirty
tree and hashes; no later commit is presented as the build's original HEAD.
