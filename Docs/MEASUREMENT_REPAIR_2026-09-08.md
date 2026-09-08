# 2026-09-08 Data Layout integration repair

Current performance: **Unmeasured / 未测量 / 待验证**. This delivery implements
code and deterministic correctness contracts. It produces no new performance result.

The isolated branch starts at `origin/main` `7a91236c3ed825b85ada6dca0b225289f150c41f`.
Merge `0b12cc6` preserves the original worktree's complete local vNext implementation
at `61f8afb` alongside current origin documentation and benchmark-reproduction license.
Other worktrees and historical raw data were not modified. Paired bootstrap,
factor policies, generated storage, envelopes, adaptive search and profile integrity
were already implemented there; they are retained, not claimed as newly invented.

## Allocation eligibility

`IAllocationCounterCapabilities` extends the source-compatible old counter interface.
Its capability carries schema, Availability (Unknown/Available/Unavailable), Scope,
provider, unit and positive/empty controls. Legacy providers without the extension
cannot certify zero. The .NET provider accepts injected reads/control actions for
deterministic tests, requires an escaping 1 MiB positive control and empty control,
rejects backward/unavailable values and cross-thread windows, and does not clamp a
failure to zero. The Unity adapter declares current-thread managed event coverage;
its native implementation is **not** native-allocation coverage.

Every newly measured result stores a snapshot of capability, required scope and
complete-window status. Both selector and envelope reject missing/inadequate coverage.
Default required coverage includes current-thread and worker managed allocations;
the portable and existing Unity providers only establish current-thread coverage.
A future host must either supply a worker-capable validated provider or explicitly
narrow the claim. Native allocation freedom always needs separate scope/evidence.
Parity or source inspection cannot substitute for an unavailable observation.

Only injected counters were exercised here. The actual .NET/Unity positive controls,
GC markers and native profiler were not run. The reported SUMMIT 1 MiB zero-counter
failure is covered by a deterministic mock that returns zero for every read.

## Statistics and schema

`Latency.P95Milliseconds` is P95 over block means (`block elapsed / tick count`).
`AmortizedLatency.P95Milliseconds` is the existing selection score:
block-mean P95 + (ingress P95 + export P95) / lifetime. Its derived sample array adds
the same boundary-P95 contribution to every block mean. These are not individual-tick
or complete-lifecycle quantiles; summing marginal P95s does not supply a lifecycle
95% probability bound.

Additive `TimingContract` schema 1 describes those exact semantics. Migration annotates
in memory, validates unknown descriptors and preserves numerical fields, bootstrap
results and FinalDecision. Old files are not rewritten, and old allocation zeros
remain Unknown. The renderer labels scores accurately, preserves historical field
names/values for downstream compatibility, and exposes allocation scope in the model
and provenance manifest. It does not rerun the selector.

`LifecycleCollector.Collect` accepts a preallocated identity-bearing buffer and an
injectable clock for ingress/Execute(1)/export. `CollectOwned` additionally encloses
candidate construction and disposal. Timestamp frequency is attached to each record;
source, process, partition, candidate, dataset and lifecycle IDs remain associated.
Incomplete, negative, inconsistent, duplicate and mixed-source observations are
rejected; interrupted capture cannot publish zero as complete. Instrumentation is
inside the complete window and is not subtracted or claimed cost-free. Synthetic
clock output remains `SyntheticFixture`. Production clocks are explicitly supplied by callers without a chat credential.

The selector retains existing paired block/log-ratio and process hierarchy math.
New source-aware pairs require the same dataset, partition and source fingerprint;
process pooling rejects source/compiler/workload mismatch. Settings are snapshotted
before work. Holdout uses frozen full candidate descriptors, a different dataset seed,
different dataset hash and independent partition under the same source identity.
Optimized, Regression, StatisticalTie and Inconclusive have distinct functional tests.
Missing valid allocation/source evidence cannot become an optimization.

## Lifetime and deployment

`ConservativeLifetimeEnvelope.Estimate` consumes paired independent-process cost
records including construction/ingress/export/disposal. It bootstraps whole processes
and uses conservative simultaneous marginal bounds for additional one-time cost and
resident savings. The practical improvement threshold is part of the equation.
Fewer than three processes, incomplete costs, changed identities, nonfinite values
or a savings denominator that overlaps zero produce Unknown. It never reports a
finite break-even interval after dropping unbounded bootstrap draws.

These are conditional linear **cost-score** estimates, not measured lifecycle P95s
or proof of gains beyond sampled workloads. Existing envelope break-even values keep
their original model and schema; they are not rewritten as this new estimate.
The exact existing deployment fingerprint binds workload/schema, candidate definitions,
Unity/Burst/dependencies, architecture/device/workers, flags and binary/kernel hash.
`BindSourceContext` accepts an intact fingerprint and declared allocation scope; it has no authorization parameter.
No old samples were attached to a new build fingerprint or republished as new results.

## Execution restrictions and validation

`Tools/CI/validate_functional.py` uses an explicit .NET test allowlist and renderer DTO
fixtures. `--build-only` only builds. Historical `SearchComparisonTests` and native
counter tests are excluded because they read real clocks/counters even with fixture
workloads. The PR workflow calls only the allowlisted functional commands and never
launches Unity, Player or external performance workloads. Production code does not
inspect a CI/Codex authorization environment variable.

Existing validation/Player/counter/search/envelope launchers are explicit measurement
commands and remain functional. Their production Bootstrap methods retain their
original named run flags. They were not invoked in this delivery. The optional
`Build-UnityCompileOnly.ps1` checks a short isolated path and manifest before headless
Editor compilation; it was not executed here. No drive, power, cache or window
settings were changed.

Executed on the integrated tree: portable Runtime compilation with no warnings/errors; 82 allowlisted
deterministic C# tests and six renderer-model tests. These counts cover synthetic
statistics, simulated time/counters, profile invalidation/serialization and read-only
historical DTO semantics, and are not benchmark evidence. Kernel integration and its separate compile/functional/source-lock results are recorded below.

No performance test/comparison, benchmark, calibration, autotuning, stress test,
profiling, real counter acquisition, recording, Unity scene or Player was run.
Existing measurement APIs are callable without chat credentials, Codex context or
source edits. Source identity, allocation capability, scope and statistical checks
remain enforced. Restoring a path is not a record of executing it: all real
measurement paths remain unexecuted during this repair.

## Historical source identity

Historical v0.3 formal measurements identify implementation
`9df183942cd8dc8abfa05bd89f03d822c96c689e`; vNext formal measurements identify
`c84cf47b62f28b26c34d72acaf16ace23f674ddb`; the September 7 integrated archive identifies
`4ffa47271306e985d9cade5d77489bd172c0360f`. Their raw hashes, JSON, binary records and
figures remain unchanged. See the root README for links and allocation qualifications.


## Completed kernel integration and final checks

The independent kernel feature `fe61d721e6b4c0a8fe250e43fec3f8c5974dc7da` was
cherry-picked as `04e234c`, after allocation/statistical repair `314d719` and
compile-only support `6223cb2`. This is completed integration, not a pending task.
The new hot/cold AoS, block codec/exports, packed TRS and external n-body sources
are detailed in [the kernel contract](../Tools/KernelContracts/CONTRACT.md).
New layout candidates remain opt-in/unregistered and Unmeasured. The generator
codec changes are compiled into the package analyzer; they carry a new source/binary
identity and do not inherit old performance results.

| Check executed on this integration worktree | Result |
| --- | --- |
| Portable Runtime build and allowlisted deterministic C# tests | PASS, 82 tests, no build warnings/errors |
| Renderer DTO/semantic tests | PASS, 6 tests; no GIF/video generation |
| Static functional-command/CI/allowlist tests | PASS, 3 tests |
| Kernel contracts with managed scheduling/arrays and actual Unity mathematics | PASS, 31,154 bounded assertions after entrypoint correction; one denial-only assertion removed from the previous 31,155 receipt |
| Filtered DataLayoutScaffoldGeneratorTests | PASS, 10 pure Roslyn/compiler tests |
| All Runtime/Samples/host/Editor/test C# sources, actual installed Unity references | PASS under Editor, Mono and IL2CPP symbols including UNITY_5_3_OR_NEWER; library only, no Editor/Player/Burst launch |
| Offline upstream file and fixture/preparer source verification | PASS, 22 pinned source/license files plus input hashes |
| Python compile checks, PowerShell parsing, JSON/Markdown metadata and integration diff whitespace | PASS |

The broad Unity-reference compile has three Editor CS0649 warnings (two JSON-filled
legacy DTO fields and the Editor-excluded native flag), and two in each non-Editor
symbol configuration. They are documented rather than hidden. This is a .NET C#
API/type check using Unity references, not a Unity Editor build, IL2CPP conversion,
Burst AOT validation, native job-safety test or true parallel-worker execution.
`Tools/FunctionalTests/UnityCompileOnly.csproj` is a library project; its test sources
are compiled but never executed. `Tools/KernelContracts/VALIDATION.md` additionally
retains the sibling's object-only LLAMA compile and bounded input preparation receipt.

```powershell
$unityMath = 'C:/Program Files/Unity/Hub/Editor/6000.5.3f1/Editor/Data/Managed/UnityEngine'
$unityPackages = 'C:/Users/EdwinLiu/Downloads/data-layout-calibrator/BenchmarkProject/Library/ScriptAssemblies'
dotnet build Tools/FunctionalTests/UnityCompileOnly.csproj -c Release -p:UnityCompileFlavor=Editor -p:UnityManagedPath="$unityMath" -p:UnityScriptAssembliesPath="$unityPackages"
# Mono and IL2CPP values compile other conditional branches, still a library only.
```

PR CI also verifies the offline source lock and runs the filtered generator suite.
Hosted CI does not have licensed Unity assemblies, so the explicit Unity-reference
and real-mathematics kernel checks above were performed locally. No all-tests Unity
collection or old mixed measurement project was executed.

## External locks and prepared entries

| Source | Exact upstream identity | Delivery and boundary |
| --- | --- | --- |
| BabelStream | `17ab377b0e919e14fd3df2b67268761fdac8abb3` | Unmodified OpenMP sources/license; double C#/Burst operation/checker **port**; original default driver unexecuted |
| STREAM 5.10 | SHA-256 `a52bae5e175bea3f7832112af9c085adab47117f7d2ce219165379849231692b` | Official website has no Git commit; exact stream.c with embedded license retained; object-build entry only |
| LLAMA nbody_code_comp | `086e66e7565f677d6b3aff88542e28c7dd6d8228` | MPL-2.0 library-native example, separate MPL-2.0 C#/Burst port; squared-component formula and source order retained |
| HeCBench stencil3d-omp | `7d2d3c567be522a2104065165de0a4a233a6ea1a` | Concrete workload/local MIT license retained; source/object-build entry only |

The [lock](../Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Upstream~/upstream-lock.json),
[port/default/validation contract](../Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/CONTRACT.md)
and [third-party notices](../THIRD_PARTY_NOTICES.md) are part of this delivery.
BabelStream/STREAM supply bandwidth workloads. No matching general standard suite
was established for the repository's exact particle respawn or Transform TRS semantics.
LLAMA code_comp remains a library-native example, and its local small equivalence
assertions are not an upstream numerical acceptance criterion or native score.

The following entries are prepared and **were not run** on this integration task:
`external_sources.py --compile-objects babel-omp --compiler <POSIX OpenMP C++ compiler>`,
`--compile-objects stream --compiler <POSIX OpenMP compiler>` and
`--compile-objects hec-stencil3d --compiler <OpenMP target compiler>`.
They compile original sources to objects only. All benchmark mains, full upstream
workloads, real allocation controls, hardware counters, timing, profiling and Player
execution remain unexecuted. Performance conclusions for this integrated source
are **Unmeasured / 待验证**. Nothing is scheduled for later execution.


## Entrypoint correction following final review

Commit `4d1621e` was superseded by the entrypoint restoration patch on the same
isolated integration branch. The session's restriction on **our execution** does not
become an authorization system in a reusable product. Removed the permission/permit
SDK types, authorization parameters, unconditional Bootstrap failures, unconditional
script-header throws and BabelStream's denial-only method. Old run flags and named
measurement scripts retain their functions. External source preparation keeps hash
verification as its default and object compilation as its only execution action;
its artificial `--run` denial switch was removed rather than inventing a new driver.

Player hosts now populate the previously unconnected source fingerprint from the
build's retained source manifest, actual binaries, device and workload/settings
identity. The envelope host reuses its existing build/environment identity. This
occurs outside measurement windows. Public SDK callers may supply `SourceFingerprint`
or use `BindSourceContext`; missing/corrupt provenance is still rejected. There is no
permission token, environment authorization or chat-context dependency.

The suite/search hosts' `-dla-allocation-scope` option defaults to their legacy
`current-thread-managed` claim. `all-managed` or `all` explicitly requires worker
and/or native coverage; insufficient or unavailable providers still fail closed.
The envelope host preserves the grid's declared scope unless the option explicitly
overrides it, and persists that choice with the cell settings. The SDK's conservative
scope default is unchanged. Declaring current-thread scope
never claims zero allocations on workers or in native memory. Existing counters,
positive controls, statistics, source association, profile invalidation and
Unmeasured candidate states were not relaxed.

The functional regression replaces denial-mirroring assertions with checks of the
actual command plan and CI/test allowlist. Real-clock/counter tests remain Explicit.
No restored Player, calibration, warmup, counter or external benchmark path was run.

Validation repeated after this correction: the portable build (zero warnings/errors),
82 Runtime tests including valid/corrupt source-context binding, six renderer tests,
three command-plan/CI allowlist tests, and 31,154 bounded kernel assertions passed.
The complete Unity-reference library compilation passed again in Editor, Mono and
IL2CPP symbol configurations with the same three/two/two existing CS0649 warnings.
Offline source/fixture hashes, Python compilation and PowerShell source parsing
passed. Source-generator code and pinned upstream bytes were unchanged; its earlier
ten-test filtered validation receipt remains recorded above. No timings from test
runner/build diagnostics are used as performance evidence.
