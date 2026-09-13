# Non-performance validation receipt — 2026-09-08

Branch: `codex/repair-20260908-layout-kernels`. Basis: `a8ed2ef`, the isolated
merge of origin `7a91236` with the local unpublished vNext implementation `61f8afb`.
This receipt concerns the kernel/codec/external-port increment, not the historical
performance data in the inherited baseline. New performance status: **Unmeasured**.

| Check actually executed | Result |
| --- | --- |
| Build explicit kernel source list + updated generator, .NET CPU model | PASS, no C# warnings/errors |
| `KernelContracts.dll --functional-only` | PASS, **31,155** deterministic assertions; real Unity mathematics, managed array/job model, maximum 17 records |
| Build same production sources as library with `RealUnityCompile=true`, `UNITY_5_3_OR_NEWER`, actual Unity assemblies | PASS, no C# warnings/errors; no Unity process launched |
| Filtered `DataLayoutScaffoldGeneratorTests` | PASS, **10** pure Roslyn source-generation/compiler tests |
| `external_sources.py` | PASS, **22** unmodified source/license files match pinned SHA-256; fixture/preparer hashes match |
| MSVC C++20 object-only compile of LLAMA code_comp AoS, SoA, AoSoA sources | PASS, **3** objects; expected C4068 warnings for unmodified GCC `ivdep` pragmas on MSVC, no executable linked/run |
| `llama_input_fixture.cpp --prepare-17-records ...` | Input preparation only; emitted 17 initial records using the upstream initializer order, no n-body kernel/driver/timer present |
| Default model executable invocation and external tool `--run` | Both refused with exit code 2 before work |

The C# checks used .NET SDK 10.0.302, targeting .NET 8 with major-version
roll-forward. Actual references came from Unity 6000.5.3f1
`Editor/Data/Managed/UnityEngine` and the original repository's pre-existing
`BenchmarkProject/Library/ScriptAssemblies`. These paths were read only. The
input preparer records MSVC 195136248 / STL 145.202604 in the fixture. Reference
binary hashes are retained in `reference-identities.json`; those binaries are
not copied into this repository.

Source-level model counters are simulated integer reads/writes and Schedule/
Complete calls. No real performance durations, bandwidth, CPU/GPU counters,
profiling traces, calibration or tuning results were collected. Build/test-tool
ordinary completion output is not treated as performance evidence.

Not executed: Unity Editor/Player, EditMode execution, Burst AOT or ISA inspection,
native job-safety/lifetime testing, actual worker scheduling, any GPU operation,
full external benchmark driver, BabelStream OpenMP object build, STREAM object
build, or HeCBench target object build. The latter three require a suitable
POSIX/OpenMP compiler and remain prepared entries only. The BabelStream and
LLAMA C#/Burst sources passed actual Unity-reference C# compilation; that does
not establish Burst lowering or original-native output/performance equivalence.

Safe reproducible commands and source/semantic limitations are in `CONTRACT.md`
and the external sample's `CONTRACT.md`. Measurement entrypoints are separate from these functional checks. None is scheduled.

The subsequent integration correction removes the product-level authorization/
permanent-denial code and its one mirrored assertion. The 31,155 count above is
this earlier receipt; the corrected functional model contains 31,154 assertions.
No kernel computation or upstream bytes changed in that correction.
The integration task rebuilt that corrected Release model and executed its
`--functional-only` path: all 31,154 assertions passed. Offline source/fixture hashes
also passed again. No native benchmark entrypoint, clock or hardware counter ran.
