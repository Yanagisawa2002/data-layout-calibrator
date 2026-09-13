# Frozen actual comparison protocol — 2026-09-10

Prepared before any confirmatory timings in this resumed phase. Baseline is
`d998cb939654d3e23353ab9975e82e5c898b0a2e`; the run freeze manifest binds adapter,
host, source-lock, protocol and executable hashes. Build manifests retain the
actual dirty-tree identity; a later commit must not impersonate an earlier build.
Old `Artifacts/actual-20260909` failures remain intact. All attempts have new paths.

## Fixed comparisons and correctness gate

1. **BabelStream variants**: unmodified pinned OpenMP kernels and `run_all` / full
   `check_solution` included in a Windows MSVC adapter, versus existing C#/Burst
   SoA operations, including its **serial** contract Dot. Upstream commit
   `17ab377b0e919e14fd3df2b67268761fdac8abb3`. Defaults: 33,554,432 doubles per
   array, 100 Copy/Mul/Add/Triad/Dot iterations, constants 0.1/0.2/0 and scalar 0.4.
   Both initialize twice, matching the native constructor and driver. No extra
   kernel warmup; exclude iteration zero only from steady operation summaries.
   Every iteration remains in raw JSON and the complete lifetime observation.
   Native uses a real `_aligned_malloc`/`_aligned_free` platform pair at the
   original 2 MiB alignment. Raw upstream files are unchanged. This is a variant,
   with no claim of independently certified memory-bandwidth run-rule compliance
   (no array-size scaling study). Each output persists all 100,663,296 doubles.
   Full upstream symmetric relative tolerance: machine epsilon ×100 for arrays,
   ×10,000,000 for Dot. An independent offline checker consumes every output byte.
2. **LLAMA code_comp auxiliary example**, not a general benchmark: upstream
   `086e66e7565f677d6b3aff88542e28c7dd6d8228`. Native AoS and AoSoA16 versus the
   existing Burst packed4 port. Default 65,536 float particles, five steps,
   no extra warmup or discarded steps. Canonical 7-float records generated once
   with the upstream initializer, MSVC/STL `default_random_engine`, seed 1;
   all variants consume identical bytes. Keep ascending source order, squared
   component force, negative masses, self interactions, update/move barrier.
   Upstream supplies no checker: added local complete-field criterion is
   `abs(a-b) <= 1e-6 + 2e-5*max(abs(a),abs(b))`, all values finite, masses bit exact
   against input. Compare every candidate to the native AoS output. A separate
   seed 2 correctness input is an independent holdout, never a timing sample.

Before formal timings: fresh full-default correctness processes for every arm,
then offline checks. Failure bars that comparison; no tolerance relaxation or
algorithm tuning from measured results. Correctness-run timing files are retained
but excluded from formal statistics. Holdout must pass before LLAMA timing.

## Execution and statistics

One child at a time under `Local\CodexR9700VNextUnityGpu`, headless/hidden. Before
each stage: sanitized conflict inventory and disk budget; at least 20 GiB remains.
No cache eviction, priority, affinity, power or driver changes. Capture actual OS
exposure (the 9950X machine currently exposes eight logical CPUs), compiler flags,
Unity/package versions, executable/source/input/output hashes and all raw logs.

Native BabelStream: MSVC 14.51.36231, `/O2 /fp:strict /arch:AVX2 /std:c++20 /MD
/openmp`, `OMP_NUM_THREADS=8`, `OMP_DYNAMIC=FALSE`; runtime default placement,
no affinity. Native LLAMA: same flags without OpenMP, one caller thread; retained
GCC-specific pragmas ignored by MSVC are reported. Burst: Unity 6000.5.3f1,
IL2CPP Release, Burst AOT enabled, safety checks off, `-job-worker-count 7`, plus
participating main thread. These are different threading APIs, not proof of equal
CPU occupancy. Native reduction tree differs from serial Burst Dot. No GPU work.

BabelStream five paired fresh-process rounds, order native/Burst, Burst/native,
native/Burst, Burst/native, native/Burst. LLAMA three fresh-process rounds, cyclic
order AoS/AoSoA16/Burst4, AoSoA16/Burst4/AoS, Burst4/AoS/AoSoA16. No best-run picking.
Each arm's process summary is its mean operation time (Babel iterations 1..99) or
sum of the five whole steps (LLAMA). Report arithmetic means across processes,
paired differences with two-sided Student-t 95% CI (df=4 or 2), and ratios of
means as descriptive values only. Process pairs, not the 99 inner repetitions,
are the independent sample unit. CI crossing zero => inconclusive; a 2% practical
tie requires the entire CI within ±2% of the native mean; otherwise directional
improvement/regression. Small n, fixed order and machine context limit inference.
No P95 claim or deployment/profile selection is made from these observations.

## Boundaries and coverage

Operation time includes blocking OpenMP calls, or Schedule/Complete and job work;
it is not an isolated hardware kernel timer. Babel storage lifetime includes
allocation, two initializations, all 100 five-pass iterations, a full canonical
export, and disposal. LLAMA lifetime includes allocation, canonical ingress,
five steps, full export and disposal. Caller-owned input/output buffer allocation,
input generation, file I/O, correctness checking and process/Unity startup are
outside both lifetimes. Report them as **storage lifetimes**, not whole application
end-to-end time. No sum of component quantiles is a lifecycle percentile.
Export cadence is exactly one final export; no conclusion about other cadences.

Player invokes the real 1 MiB escaping positive/empty controls for
GC.GetAllocatedBytesForCurrentThread and records explicit availability. Regardless
of its outcome, workload allocation windows and worker/native coverage are absent:
allocation eligibility is Unknown; no profile or default candidate is published.

STREAM 5.10 remains a separately locked general suite but has no prepared matching
Windows/Burst driver. LLAMA SoA compiles but is outside this finite three-arm run.
HeCBench stencil3d has no matching port/OpenMP target runtime. These are explicitly
unexecuted, not replaced by synthetic workloads or LLAMA rankings.
