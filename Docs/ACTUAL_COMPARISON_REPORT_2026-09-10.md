# Data Layout actual external comparisons — 2026-09-10

The resumed phase completed **BabelStream variant comparisons** and **LLAMA
code_comp auxiliary comparisons**, including actual IL2CPP/Burst execution and
complete output checks. BabelStream's current Burst port was slower for Copy,
Dot and the observed storage lifetime. LLAMA packed4 was much faster than the
single-thread MSVC reference, but that comparison changes compiler, threading
and layout together. No default candidate or deployment profile was promoted.

Overall status: **partial**. The finite frozen comparisons below finished; STREAM,
HeCBench and allocation-qualified deployment evidence remain incomplete.

## Identity and evidence

- Worktree: `work/wt/layout-integration`, branch
  `codex/repair-20260908-layout-integration`. Starting HEAD:
  `d998cb939654d3e23353ab9975e82e5c898b0a2e`.
- Adapter/protocol source commit:
  `36f84d7ef398520d0901737f80f4f6abfcefe4d6`.
  The Player was built immediately before that commit. Its original manifest
  honestly records `d998cb9` **plus dirty source files**, with their complete
  hashes. The pre-run freeze checked those hashes against the committed adapter
  tree; it does not rewrite the build's source identity to a later commit.
- [Fixed protocol](ACTUAL_COMPARISON_PROTOCOL_2026-09-10.md),
  [599-file source/input/executable freeze](evidence/actual-20260910/freeze-01.json),
  [paired numerical results and every process summary](evidence/actual-20260910/summary.json),
  [machine/toolchain identity](evidence/actual-20260910/environment.json),
  [616-file raw evidence index](evidence/actual-20260910/evidence-index.json).
- Freeze SHA-256:
  `8192c892cd7396265c6c9030dba6f57920a7b3eba2b7da1387899a5f487b0862`.
  Evidence-index SHA-256:
  `93e864c97e00bdf1cb4c5955b6c5dfe3dccaeb4aa0f783f47d59879d85b4bd9d`.
  About 10.10 GB (decimal) of original data/build artifacts remain local, under
  `Artifacts/actual-20260910/` and the untouched `Artifacts/actual-20260909/`.
  Only manifests and compact summaries are committed; no binary cache/dependency
  is added. The index explicitly excludes its own still-open audit logs and the
  final receipt, avoiding recursive or mutable hashes.

Machine: Windows 11 Pro 10.0.26200, reported Ryzen 9 9950X, **eight exposed logical
CPUs/eight exposed cores**, 66,158,055,424 bytes physical RAM. Native: MSVC
14.51.36231, `/O2 /fp:strict /arch:AVX2 /std:c++20 /EHsc /MD`; Babel adds `/openmp`,
configured maximum eight threads, dynamic threads disabled. LLAMA native has one
caller thread. Player: Unity 6000.5.3f1, IL2CPP Release, Burst 1.8.29, resolved
Collections 6.5.0 and Mathematics 1.4.0, seven job workers plus participating main
thread, Burst AOT enabled/safety checks off. Requested manifest dependency minima
are not substituted for these actually resolved versions. Player logs confirm
the Null graphics device. No GPU workload, counter profiling, power/priority/
affinity modification or cache clearing was performed.

## BabelStream: general external benchmark, explicitly labelled variants

Source: [BabelStream commit 17ab377](https://github.com/UoB-HPC/BabelStream/tree/17ab377b0e919e14fd3df2b67268761fdac8abb3),
custom upstream license/run rules retained. Default 33,554,432 doubles per array,
100 iterations, classic Copy/Mul/Add/Triad/Dot. Native includes unchanged upstream
kernels, `run_all` and `check_solution`; the Windows allocation adapter preserves
2 MiB alignment with a real matching allocation/free pair. The port calls the
existing concrete Burst jobs. Both initialize twice, matching the native driver.
Burst Dot is serial; native Dot uses OpenMP reduction. This is not a comparison
between two layouts, nor a certified original BabelStream submission. There was
no array-size scaling study to independently certify the memory-bandwidth run rules.

Five fresh-process pairs, in the predeclared alternating order. Values below are
means of process means, excluding only iteration zero for operation summaries;
all 100 iterations remain in raw data and lifetime time. Units: **milliseconds**.
Difference is Burst minus native; positive means slower. Confidence intervals use
the five independent process pairs, not 495 inner iterations.

| Scope | Native mean | Burst mean | Difference, paired 95% CI | State |
| --- | ---: | ---: | ---: | --- |
| Copy | 9.205 | 11.321 | +2.116 [1.992, 2.240] | regression |
| Mul | 11.489 | 11.599 | +0.110 [-0.031, 0.252] | inconclusive |
| Add | 15.658 | 15.709 | +0.052 [-0.024, 0.128] | tie within 2% |
| Triad | 15.651 | 15.619 | -0.032 [-0.097, 0.034] | tie within 2% |
| Dot | 8.849 | 12.459 | +3.610 [3.501, 3.719] | regression |
| Storage lifetime, all 100 iterations | 6240.146 | 6840.995 | +600.849 [534.748, 666.950] | regression |

The current Burst port's storage lifetime was about **9.63% longer** in this
environment. Construction, explicit initialization, export and disposal are also
reported separately in the summary. These observations do not identify a hardware
mechanism; no profiling or kernel change was made during formal measurement.

Correctness: two prerequisite runs and ten formal runs each validated **all
100,663,296 array values and Dot**. Complete array bytes match the scalar oracle
and have identical SHA-256 across all runs:
`614d49280be7b0bd0efdd4685cc80e67728434687f30b3a7ad0f231c646f33d3`.
The native and Burst full checkers and independent persisted-byte checker passed
the unchanged upstream relative criteria. Different reduction order gives different
Dot bits, within the upstream epsilon ×10,000,000 tolerance. Inputs are the fixed
upstream uniform constants, fully specified by the frozen source/protocol; no
repository synthetic demo is substituted.

[Logical input identity](evidence/actual-20260910/babel-logical-input.json):
`f677b824e509ce0301b9ed28207490b0d72e5ad724da35700893b940af2245b9`, for complete
initial a/b/c arrays in little-endian binary64 order. This digest was reconstructed
offline from the already frozen constants and count after the run; it is explicitly
not an additional runtime input readback or a changed/prepared workload.

## LLAMA code_comp: auxiliary external layout-library example

Source: [LLAMA commit 086e66e](https://github.com/alpaka-group/llama/tree/086e66e7565f677d6b3aff88542e28c7dd6d8228/examples/nbody_code_comp),
MPL-2.0 retained. This is **not a general standard benchmark suite**. Use the
default 65,536 particles/five steps and original squared-component force, including
negative masses and self interactions. The complete initializer output, generated
by the identified MSVC/STL, is shared byte-for-byte with Burst. Seed 1 input hash:
`57db2049e77efd63506a10f0d6222c62ebc24396ca325654a0fff00a954db22f`;
independent seed 2 holdout hash:
`2395cc7fa1530d0e089d7fb47907990424a2911213e921840d001bd17d8406a7`.
Seed 2 was used only for correctness, excluded from timing inference.

Three fresh-process rounds in the fixed cyclic order. No added warmup or discarded
step. Milliseconds; differences relative to native AoS. Whole-step sums and actual
storage lifetime observations are distinct fields in the complete summary.

| Storage lifetime | Mean ms | Candidate minus native AoS, paired 95% CI |
| --- | ---: | ---: |
| Native AoS, one thread | 42401.355 | reference |
| Native AoSoA16, one thread | 41387.743 | -1013.612 [-1294.884, -732.340] |
| C#/Burst packed4, seven workers plus main | 1381.841 | -41019.514 [-41174.254, -40864.774] |

Within MSVC, AoSoA16 reduced this storage lifetime by approximately **2.39%**.
The descriptive native-AoS/Burst ratio is **30.68**, but it combines parallelism,
code generation, storage and API differences. It is not a 30.68× layout-only
optimization claim, an equal-thread-count comparison, or a general deployment
advantage. GCC-specific native pragmas are ignored by MSVC (C4068 preserved in
the build log); no fake GCC vectorization is claimed.

All six prerequisite/holdout runs and nine formal runs passed complete comparison
of **458,752 fields** and unchanged mass bits against input. The two seeds' complete
outputs were actually bit-identical across these variants; the predeclared local
tolerance was not relaxed. Output seed 1 hash:
`d14f1a937695dff7ee0c5e223e06ddb0714f53057438b6b2b0531936c7c9bf29`;
seed 2: `df952e60df0bd541d4452c0166aa6fd369843d1d71e5dbde2a79203ed0c37543`.
This complete parity checker is an added local contract; upstream code_comp has
no numerical acceptance checker.

## Scope, failures and validation

Storage lifetime includes allocation/ingress or initialization, all required work,
one complete final export and disposal. **Caller-owned buffer allocation, input
generation, file I/O, correctness checks and Unity/process startup are excluded**.
These are not whole-application end-to-end results. There is no recurring-export
claim, lifecycle P95, sum-of-P95 reinterpretation or break-even deployment claim.
No best process was selected. The Student-t intervals are small-sample estimates
under independence/paired-difference assumptions; the cyclic orders are fixed,
not random, and multiple endpoints have no multiplicity adjustment.

The actual Player reproduced the allocation-counter problem in all **11 Player
runs**: a 1 MiB escaping allocation returned zero. `ThreadManagedAllocationCounter`
rejected its positive control, persisted `Unavailable` with current-thread-only
scope, and retained `allocationEligibility=Unknown`. No workload allocation
windows or worker/native allocation coverage were inferred, and no profile was
issued. Faster timing cannot overcome this missing eligibility evidence.

- Real native Babel and LLAMA builds passed; the latter fixes the earlier `::move`
  namespace/linkage adaptation. Compiler warnings remain in the evidence index.
- The first resumed Unity import failed at host access to an internal hash helper.
  Fixed with the same strict UTF-8/SHA-256 semantics inside the host assembly.
  A first Player build then caught the new NativeArray `using`-variable write;
  an explicit writable alias fixed compilation while the owner retains disposal.
  Both new failures remain under `import-01` and `player-build-01`.
- `player-build-02` completed IL2CPP Release and required actual Burst AOT entries
  for every external job. Subsequent full-output Player executions passed. No
  incomplete package cache or compile-only artifact was accepted as Player proof.
- 22 pinned upstream source hashes and the input-only fixture lock passed.
  The allowlisted offline CPU project passed **82/82 tests**, with no restore.
- 19 formal processes, eight correctness processes, 27 full-output checks; all
  passed. Evidence audit checked 63 completed preceding stages, no overlap,
  matching source/input/binary freeze, mutex release and reserve compliance.
  Old ENOSPC/import/native failures from September 9 were neither deleted nor
  overwritten. Approximately 50.42 GiB remained after validation.

Unexecuted: **STREAM 5.10** (locked source, POSIX driver and no prepared matching
Windows/Burst port; no result claimed); **HeCBench stencil3d-omp** (locked concrete
workload, no matching port/OpenMP target runtime); **LLAMA native SoA** (compiles
in the adapter, outside the finite three-arm protocol). No other repository or
HLSL run was started. Device/compiler/export-cadence sweeps and allocation-qualified
full deployment selection remain untested. The next useful work is a separately
frozen Babel Dot/thread-policy improvement and a suitable worker/native allocation
provider, followed by new complete correctness gates before any new comparison.

See [reproduction entries](../Tools/ActualComparison/README.md). No automatic
performance queue, upstream message, push, release or default-branch integration
was created. The final local receipt is
`Artifacts/actual-20260910/resume-outcome.json`.
