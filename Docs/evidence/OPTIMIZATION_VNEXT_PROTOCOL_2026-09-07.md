# Integrated vNext validation protocol â€” 2026-09-07

This protocol is declared before integrated formal measurements. The execution
manifest written before the first launch will additionally freeze the integrated
implementation commit, Player/IL2CPP metadata/Burst hashes, exact candidate
descriptors and their canonical set hashes. A generic Unity executable hash alone
does not identify the measured implementation.

## Scope and retention

The only device is the local Ryzen 9 9950X Windows host. Formal measurements use
Unity 6000.5.3f1, non-Development Windows x64 IL2CPP Release and Burst AOT. Mono
Release AOT is an additional correctness gate. This is not a cross-device or
cross-ISA experiment. No result becomes a production default merely by entering
the candidate matrix.

All Unity/build/Player/timing work holds the shared
`Local\CodexR9700VNextUnityGpu` mutex until its child processes exit. No global
cache deletion, elevation, user-process termination or detached benchmark is
permitted. Retain competing-process observations, exact commands, failures,
unavailable metrics and every launch ordinal. Do not replace a launch because
its performance result is unfavorable. Source repairs invalidate the previous
binary identity and require a separately identified attempt.

## Acceptance gates

1. Generated ParticleIntegrate and TransformExport storage/codec paths execute
   in actual Mono and IL2CPP workloads with canonical parity, complete boundary
   operations, zero measured steady-state managed allocation and correct lifetime.
2. Expanded AoS/SoA/AoSoA4/8/16/padded controls pass boundary/tail correctness,
   explicit kernel/execution factor checks and Burst AOT reachability. Preserve
   matched AoS controls; document unsupported combinations.
3. Execute the measured envelope below, including independent held-out input and
   newly sampled timing after the candidate selection is frozen.
4. Execute adaptive and exhaustive calibration on identical frozen sets. Include
   quick search, planning and full evaluation in search cost; separately report
   common setup and holdout costs. Compare decisions, regret and evaluation count.
5. Replay the five retained historical processes using process-level resampling
   and paired blocks within each process. Varying selected candidates define a
   calibration-policy estimand, not a fixed-candidate or device population.
6. Execute both new access-pattern workloads and actual optional CPU-cycle
   capture, including provider-disabled and provider-enabled overhead controls.
   Missing instructions/cache/branch PMU data remain unavailable. Process CPU
   cycles are not instructions, frequency, wall-clock time, or isolated kernel PMU
   counts and do not alone establish a causal mechanism.

## Envelope matrix

Five fresh sequential processes, each with 24 cells:

| Axis | Declared values |
| --- | --- |
| Calibration records | 4,096; 65,536 |
| Lifetime ticks | 1; 16; 256 |
| Actual field access | Observable cold pass every 1 or 8 resident ticks |
| Unity Job workers | 1; 7 |
| Execution | FrameFaithful |
| Logical batch | 64; 256 |
| Layout/kernel | All expanded layouts and matched scalar/packed controls |

Cold-field access must change executable observable work. Keep full ingress and
export in amortized P95 and preserve the fastest valid measured AoS control in
each cell. Report per-process uncertainty, gray regions, fallback and break-even
intervals. No interpolation turns unmeasured points into measured coverage.
The Rotation (four components) and Category cold pass is observable. The logical hot/cold byte ratios are 1.4 and 11.2, from 28/(20/period); they are not measured bandwidth or a cache-state assertion. Use 40 resident and 20 samples for each boundary, 4,000 bootstrap iterations, 32 warmup blocks/minimum 0.1 seconds, a 2 ms target block and maximum 256 ticks. Holdout retains each cell count but uses a distinct input seed and fresh timing samples.
Input seeds/counts, sampling settings and raw-artifact structure are additionally
bound by the worker protocol and execution manifest before launch.

## Search matrix

Five fresh sequential processes, count 65,536, independent holdout count 65,539,
lifetime 256, seven workers, batches 64/256, all expanded layout/kernel controls.
FrameFaithful and DependencyChain are separate cells with the same candidate set
for both search methods. Runs 1/3/5 execute adaptive first; runs 2/4 execute
exhaustive first. Freeze both calibration selections before either holdout.

Quick evaluation uses six resident samples, four boundary samples and 200
bootstrap iterations. Full evaluation and holdout use 40/20 samples and 4,000
iterations at 95% confidence. The improvement threshold is 10%; the declared
regret limit is 1%. Target block duration is 2 ms, maximum 64 ticks; warmup is at
least four blocks and 0.05 seconds. Record the common block-sizing cost and actual
block size. Distinct declared calibration and holdout dataset seeds remain fixed
across process replications, whose timing samples are independent launches.

An unsuccessful elimination or a slower adaptive run is a valid result, not a
reason to change thresholds, discard evidence or claim a gain. Additional broad
historical/compiler-version comparisons are follow-up work, not a substitute
for these bounded required experiments.

## Counter and workload checks

The integrated IL2CPP counter run uses 65,536 records, 64 ticks per action and
12 paired enabled/disabled repetitions per registered candidate in all four
workloads: ParticleIntegrate, TransformExport, SpatialNeighborhood and
AnimationState. Freeze the actually executed registry candidate definitions.
Alternate AB/BA within each candidate and reset identical input before each arm.
Measure the complete adapter overhead, including raw endpoint persistence, and
retain noisy or negative paired deltas. This diagnostic provider does not feed
the envelope or search decisions. A separate short no-provider run validates
fallback, while the main correctness/calibration suite runs with counters off.

One bounded integrated registry suite executes all four workloads with counters
off: 65,536 calibration / 65,539 held-out records, lifetime 256, seven workers,
40 resident / 20 boundary samples, 4,000 bootstrap iterations, 95% confidence,
10% improvement gate, target 2 ms, maximum 64 ticks, warmup four blocks/minimum
0.05 seconds. It retains every default registry candidate and the independent
holdout decision. This single-process suite validates the new workloads and
the existing negative control; it is not a new five-process performance claim.


## Pre-measurement environment correction

The integrated Mono Release correctness run on 2026-09-07 observed seven actual
Unity Job workers despite requesting eight. The OS exposes eight logical CPUs;
the processor brand string does not establish the available worker count. Before
any formal measurement, the worker axis was therefore changed from 1/8 to 1/7,
and the search/counter/main-suite launch count to seven. All other axes, budgets,
and thresholds remain fixed. Formal receipts must match the corrected count.

The same correctness run rejected the managed allocation counter because its
4096-byte positive control returned zero. No allocation pass is inferred from
that attempt. A validated provider is required before formal evidence is accepted.
