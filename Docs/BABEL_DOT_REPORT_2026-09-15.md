# BabelStream parallel Dot — measured engineering result, 2026-09-15

The deterministic compensated parallel reduction cut Dot elapsed time by **71.80%** and the complete 100-iteration storage lifetime by **21.67%** versus the original serial Burst path on this machine.

**The no-regression goal was not fully met.** Copy, Mul and Triad slowed by 1.39%, 1.65% and 1.87%. Mul/Triad confidence intervals extend beyond the predeclared 2% practical band. The optimized full lifetime remains **2.53% slower than native OpenMP**. Retain this as an explicit candidate; allocation eligibility remains **Unknown**.

## Reproducible evidence

- [Frozen protocol](BABEL_DOT_PROTOCOL_2026-09-15.md), [step-by-step Windows reproduction](BABEL_DOT_REPRODUCE_WINDOWS.md) and [entrypoints](../Tools/ActualComparison/README.md).
- [All process rows, means and paired intervals](evidence/babel-dot-20260915/summary.json).
- [Completed audit](evidence/babel-dot-20260915/audit.json), [source/binary freeze](evidence/babel-dot-20260915/freeze.json), [environment](evidence/babel-dot-20260915/environment.json), [every retained file hash](evidence/babel-dot-20260915/index.json).
- [Actual complete output, gzip](evidence/babel-dot-20260915/full-output.bin.gz): decompresses to 805,306,368 bytes. All 25 separately persisted outputs have this same verified SHA-256. Raw timing JSON and complete invocation/output logs for every attempt are in the evidence directory.

## Implementation and boundaries

The original serial job is unchanged. A common Player selects `-dla-dot-mode serial|parallel`; the default retains historical serial behavior. The new API schedules 512 contiguous 65,536-element chunks, then one dependent merge job. Each chunk retains a double sum and Neumaier correction; the merge consumes both in ascending chunk order. Strict floating-point compilation, explicit final tails and exact caller-owned scratch sizing preserve the numerical contract. Scratch is 8,192 bytes, reused only after completion, completely assigned every call, and disposed by the caller.

Dot time includes accumulator initialization, chunk writes, Schedule/Complete and the entire dependent merge. Scratch allocation/zero initialization and disposal are inside storage lifetime. The original upstream kernels, run_all, check_solution, constants and license remain unchanged. Both arms initialize twice, run all five operations for all 100 iterations, export all arrays once and dispose storage. Caller-owned export-buffer allocation, file I/O, numerical checking and process/Unity startup are outside lifetime for every arm. These are C#/Burst and Windows/OpenMP variants, not certified original BabelStream submissions; no array-size scaling study was performed.

## New environment

Intel Core Ultra 7 265K, 20 exposed cores/20 logical processors, one CPU group and one NUMA node. Windows CPU sets report eight efficiency-class 1 cores and twelve class 0 cores; no affinity restrictions were added. Two 16 GiB DIMMs report 4,800 MT/s; OS-visible physical memory is 33,682,857,984 bytes. Windows 11 Home 10.0.26200. This is a separate environment from the historical September 10 machine.

Native: MSVC toolset 14.44.35207 (compiler 19.44.35222), SDK 10.0.26100.0; `/O2 /fp:strict /arch:AVX2 /std:c++20 /EHsc /MD /openmp`, 20 maximum threads, dynamic threads disabled, default placement. Player: Unity 6000.5.9f1, IL2CPP Release, Burst 1.8.30 (LLVM 21), Collections 6.5.0, Mathematics 1.4.0; 19 job workers plus possible main-thread participation in both arms. AOT manifests include SSE2 and AVX2 variants with safety checks off; runtime ISA dispatch was not separately traced. Every formal Player log confirms the Null graphics device. Different threading APIs do not establish equal core occupancy.

Formal five-second CPU preflight means ranged from 1.92% to 5.08%. All builds/workloads used the shared `Local\CodexR9700VNextUnityGpu` mutex and preserved at least 20 GiB disk reserve. The HLSL Scan task deferred hardware work until closeout. No cache eviction, power/driver/priority/affinity changes or unrelated process termination was used.

## Confirmatory results

Six balanced fresh-process blocks, three arms per block. Values are milliseconds; operation means use iterations 1–99, while lifetime contains all 100. Brackets below are two-sided process-level 95% Student-t confidence intervals (df=5).

| Endpoint | Native OpenMP mean [95% CI] | Original Burst mean [95% CI] | Parallel Burst mean [95% CI] |
| --- | ---: | ---: | ---: |
| Copy | 9.212 [9.168, 9.255] | 12.695 [12.672, 12.717] | 12.871 [12.803, 12.938] |
| Mul | 13.722 [13.650, 13.794] | 13.394 [13.378, 13.410] | 13.615 [13.546, 13.683] |
| Add | 18.548 [18.458, 18.638] | 17.586 [17.533, 17.640] | 17.598 [17.554, 17.642] |
| Triad | 18.404 [18.342, 18.466] | 17.349 [17.317, 17.381] | 17.674 [17.574, 17.774] |
| Dot | 8.424 [8.381, 8.467] | 29.238 [27.824, 30.651] | 8.244 [8.145, 8.343] |
| Storage lifetime, all 100 | 7,139.055 [7,118.485, 7,159.626] | 9,344.922 [9,224.837, 9,465.007] | 7,319.567 [7,276.983, 7,362.151] |

### Parallel minus original Burst: paired effects

| Endpoint | Paired difference ms [95% CI] | Time reduction | Interpretation |
| --- | ---: | ---: | --- |
| Copy | 0.176 [0.105, 0.247] | -1.39% | regression; within +/-2% practical band |
| Mul | 0.220 [0.145, 0.296] | -1.65% | regression |
| Add | 0.011 [-0.082, 0.104] | -0.06% | inconclusive; within +/-2% practical band |
| Triad | 0.325 [0.228, 0.422] | -1.87% | regression |
| Dot | -20.994 [-22.502, -19.486] | 71.80% | improvement |
| storageLifecycleMs | -2,025.355 [-2,174.951, -1,875.759] | 21.67% | improvement |
| constructMs | -6.134 [-27.973, 15.704] | 3.01% | inconclusive |
| initMs | 0.019 [-0.549, 0.586] | -0.07% | inconclusive |
| exportMs | 2.716 [1.901, 3.532] | -7.27% | regression |
| disposeMs | 6.924 [1.085, 12.763] | -15.03% | regression |

Copy/Mul/Add/Triad code and worker settings are identical between Burst arms. Their observed timing changes are still retained; the mechanism was not profiled. Copy is a statistically directional slowdown inside the 2% band; Add is within that band with a CI crossing zero. Mul and Triad do not clear the practical no-regression check. Export and disposal also slowed; their full costs remain in the improved lifetime.

### Parallel minus native OpenMP

| Endpoint | Paired difference ms [95% CI] | Time reduction | State |
| --- | ---: | ---: | --- |
| Copy | 3.659 [3.562, 3.756] | -39.72% | regression |
| Mul | -0.108 [-0.198, -0.017] | 0.78% | improvement |
| Add | -0.951 [-1.072, -0.829] | 5.12% | improvement |
| Triad | -0.730 [-0.847, -0.614] | 3.97% | improvement |
| Dot | -0.180 [-0.305, -0.055] | 2.14% | improvement |
| storageLifecycleMs | 180.512 [132.190, 228.833] | -2.53% | regression |

The small Dot advantage over OpenMP is specific to these variants and six process blocks. The full optimized lifetime remains slower; there is no overall native-win claim. Intervals use process pairs, not 594 inner iterations as independent samples. Orders are fixed and balanced, sample size is small, and multiple endpoints have no multiplicity adjustment. Percent reductions are descriptive ratios of means. The JSON also rescales difference intervals by the observed reference mean; those are not independent ratio intervals.

## Every formal process

| Block / position | Arm | Copy | Mul | Add | Triad | Dot | Storage lifetime |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 / 1 | native | 9.210 | 13.677 | 18.547 | 18.387 | 8.421 | 7,137.832 |
| 1 / 2 | serial | 12.721 | 13.412 | 17.614 | 17.359 | 29.754 | 9,389.689 |
| 1 / 3 | parallel | 12.802 | 13.492 | 17.578 | 17.500 | 8.174 | 7,261.145 |
| 2 / 1 | serial | 12.687 | 13.401 | 17.669 | 17.348 | 29.850 | 9,368.042 |
| 2 / 2 | parallel | 12.862 | 13.631 | 17.562 | 17.738 | 8.185 | 7,300.993 |
| 2 / 3 | native | 9.190 | 13.705 | 18.542 | 18.366 | 8.406 | 7,118.696 |
| 3 / 1 | parallel | 12.993 | 13.680 | 17.660 | 17.762 | 8.428 | 7,376.432 |
| 3 / 2 | native | 9.171 | 13.678 | 18.428 | 18.358 | 8.378 | 7,146.753 |
| 3 / 3 | serial | 12.708 | 13.400 | 17.525 | 17.403 | 26.490 | 9,115.038 |
| 4 / 1 | native | 9.183 | 13.672 | 18.525 | 18.364 | 8.397 | 7,116.800 |
| 4 / 2 | parallel | 12.859 | 13.615 | 17.576 | 17.668 | 8.223 | 7,346.849 |
| 4 / 3 | serial | 12.703 | 13.367 | 17.566 | 17.328 | 29.818 | 9,427.947 |
| 5 / 1 | parallel | 12.864 | 13.652 | 17.642 | 17.650 | 8.249 | 7,331.344 |
| 5 / 2 | serial | 12.660 | 13.392 | 17.551 | 17.316 | 29.794 | 9,390.440 |
| 5 / 3 | native | 9.231 | 13.751 | 18.552 | 18.445 | 8.450 | 7,144.931 |
| 6 / 1 | serial | 12.688 | 13.394 | 17.593 | 17.340 | 29.722 | 9,378.376 |
| 6 / 2 | native | 9.285 | 13.849 | 18.695 | 18.506 | 8.491 | 7,169.321 |
| 6 / 3 | parallel | 12.843 | 13.617 | 17.568 | 17.724 | 8.204 | 7,300.640 |

## Correctness and audit

- 25 full-default processes: four discovery, three prerequisites, eighteen formal. Every process passed all 100,663,296 array values and final Dot; all 25 raw outputs were rehashed at closeout. Array bytes exactly equal the independent oracle. Upstream relative tolerances were not relaxed (machine epsilon ×100 arrays, ×10,000,000 Dot). The shared checker now explicitly rejects infinities as well as NaNs.
- 28 additional actual-Burst fixtures at each of 1 and 19 workers, three repeated reductions per fixture; exact independent decimal-oracle results and identical repeated bits. Inputs cover zero/small counts, complete/partial chunks, signed nonuniform dyadics and cancellation within/across chunks. Scratch/output poisoning tests complete assignment. Nine controls per worker configuration cover invalid/nonfinite inputs, invalid counts, the original serial cancellation failure and two synthetic missing-contribution errors.
- Original serial job text matches main after newline normalization. 640 frozen files and all build-manifest inputs match their recorded hashes; serial/partial/merge AOT entries are present. Recorded stages do not overlap; every formal exit code is zero. Independent decimal arithmetic recomputed all 30 paired intervals, and a numerical Student-t integration checked the critical value. Every lifetime is at least the sum of its 100 timed operation sets and recorded construction/init/export/disposal.
- Repository functional validation: 82 NUnit tests, three CI-policy tests and six renderer-contract tests passed. All 22 upstream source hashes and the locked input fixture/preparer hashes passed. Static JSON/link and whitespace checks passed after staging the new artifacts.
- The real 1 MiB escaping allocation positive control still reports zero in the IL2CPP Player. `allocationEligibility=Unknown`; there is no worker/native allocation-window coverage or deployment profile.

## Failures and source identity

The initial clone hit Windows long-path checkout limits; repository-local long-path support completed checkout. The initial Player build compiled source but failed because the installed Editor lacked Windows IL2CPP support. Its exit-1 log is retained. A task-owned Editor copy on D: received the exact official matching module; Unity manifest integrity and Authenticode signature were verified, and the silent component installation exited zero. The second IL2CPP build succeeded. Existing Editor source files and other task checkouts were not edited. Native build warnings from retained upstream float instantiation remain in its raw log.

Both build and freeze originally identify starting main `8e83a17338452c9d202f45ed553e0c807a7b8558` plus their actual dirty source lists and byte hashes. The delivery commit is a later packaging identity, not a backdated build claim. The original September 10 results are never mixed into these statistics.

| Identity | SHA-256 |
| --- | --- |
| Formal freeze | `30698e2706b4203b9a9aa9654f538b248bc15916449ece02a0f5aad57438c426` |
| Complete initial logical a/b/c input (reconstructed, not runtime readback) | `f677b824e509ce0301b9ed28207490b0d72e5ad724da35700893b940af2245b9` |
| Every full output, 805,306,368 bytes | `614d49280be7b0bd0efdd4685cc80e67728434687f30b3a7ad0f231c646f33d3` |
| Native executable | `7d64a0dfb12af418d0c67ee7f437f53f2ca72b4c4ea030bbf58364bae2d7b9ac` |
| Player executable | `f95ae8934e9bf68761aaf83b82e9faf641666b6b1fc923eef4f54336d07ea28c` |
| IL2CPP GameAssembly | `0de33a393daeedec0602310ee3f6c9bd6dff9042e14ce1a41da119295ddc02d7` |
| Burst AOT library | `602434821b072c940b71a1a576c0f25b503f6a1e7abaebe80ad2f03b038d7a01` |

## English resume candidate

> Implemented a deterministic, compensated parallel reduction in Unity Burst for BabelStream's 33.6M-element double-precision Dot, reducing Dot time by 71.8% and 100-iteration storage-lifetime time by 21.7% versus the original Burst implementation across six balanced process trials, with full-output validation and same-machine OpenMP comparison.

Use only with the original-Burst comparator and this measured workload. It does not claim a 21.7% improvement over OpenMP, no regressions, allocation qualification, cross-machine generality or hardware-counter evidence.

All original artifacts, including 25 uncompressed full outputs and both toolchains, remain at `D:\CodexWork\babel-dot-20260915\Artifacts\dot-20260915`. The checked-in gzip is an actual recorded output, with a verified decompressed hash matching every full-output receipt.
