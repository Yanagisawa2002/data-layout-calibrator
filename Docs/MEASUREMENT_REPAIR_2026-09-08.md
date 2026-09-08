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
clock output remains `SyntheticFixture`. Production clock calls require authorization.

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
`BindAuthorizedContext` requires an intact fingerprint before new measurements.
No old samples were attached to a new build fingerprint or republished as new results.

## Execution restrictions and validation

`Tools/CI/validate_functional.py` uses an explicit .NET test allowlist and renderer DTO
fixtures. `--build-only` only builds. Historical `SearchComparisonTests` and native
counter tests are excluded because they read real clocks/counters even with fixture
workloads. The new PR workflow sets `DLC_FUNCTIONAL_ONLY=1`, which rejects measurement
permits, and never launches Unity, Player or external performance workloads.

Legacy mixed validation/Player/counter/search/envelope launchers now refuse execution
before dependency resolution or process/time collection. Unity benchmark Bootstrap
methods refuse the old run flags. The optional `Build-UnityCompileOnly.ps1` checks a
short isolated path and manifest, then allows headless Editor script compilation only;
it was not executed here. No drive, power, cache or window settings were changed.

Executed so far: portable Runtime compilation with no warnings/errors; 82 allowlisted
deterministic C# tests and six renderer-model tests. These counts cover synthetic
statistics, simulated time/counters, profile invalidation/serialization and read-only
historical DTO semantics, and are not benchmark evidence. Kernel integration and its
separate compile/functional/source-lock results are recorded below when complete.

No performance test/comparison, benchmark, calibration, autotuning, stress test,
profiling, real counter acquisition, recording, Unity scene or Player was run.
Future measurements require a **new explicit user authorization**, a reviewed host,
an intact current fingerprint and allocation coverage suitable for the chosen claim.
`MeasurementExecutionPermit.FromNewExplicitUserAuthorization` and the dormant engine/
collector APIs are preparation points, not authorization to execute this round.

## Historical source identity

Historical v0.3 formal measurements identify implementation
`9df183942cd8dc8abfa05bd89f03d822c96c689e`; vNext formal measurements identify
`c84cf47b62f28b26c34d72acaf16ace23f674ddb`; the September 7 integrated archive identifies
`4ffa47271306e985d9cade5d77489bd172c0360f`. Their raw hashes, JSON, binary records and
figures remain unchanged. See the root README for links and allocation qualifications.
