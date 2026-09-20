# Evidence status and claim boundaries

[Overview](../README.md) · [Engineering cases](ENGINEERING_CASE_STUDIES.md) ·
[Adoption guide](LAYOUT_ADOPTION.md)

Status checked on **2026-09-20**, against main
`c37cfca47ac240c90e5382310f339044e9c0f5b6` and the separately identified PR #10.
This page is an index of existing evidence, not a new performance run. A later
packaging or documentation commit is not the source identity of an earlier build.

## Read the evidence at the right level

| Record or surface | What it supports | What it does not support |
| --- | --- | --- |
| [September 15 Burst Dot](BABEL_DOT_REPORT_2026-09-15.md) | Six balanced process blocks; a parallel-reduction improvement versus original serial Burst, full-output checks and a named OpenMP comparison | General layout benefit, an overall native win, current-HEAD remeasurement or allocation-qualified deployment |
| [September 15 complete SPH task](FOCUSED_COMPLETE_TASK_2026-09-15.md) | 72 shared-host processes; buffer reuse improves complete caller latency; tuned AoS beats the tested custom layouts | Isolated-hardware results, Unity/Burst/GPU performance, selector calibration or payback |
| [September 10 external comparisons](ACTUAL_COMPARISON_REPORT_2026-09-10.md) | Frozen BabelStream variants and the named auxiliary LLAMA code_comp example, with their original environment and boundaries | The later September 15 machine or implementation; a universal library ranking |
| [CPU-only lifetime example](../Tools/Examples/LifetimeDecision/README.md) | Real API behavior on explicitly synthetic costs; conditional bounds and Unknown handling | A workload benchmark, measured speedup or deployment profile |
| [PR #10](https://github.com/Yanagisawa2002/data-layout-calibrator/pull/10) | Separate draft implementation and an honestly retained stopped experiment | Merged-main functionality or completed calibration, confirmation, selector allocation measurement or payback |
| [PR functional CI](../.github/workflows/non-unity-ci.yml) | Static metadata, compilation, allowlisted deterministic CPU correctness and offline source contracts | New Unity/Player/Burst performance, timed calibration or hardware-counter validation |

PR #10 was open, unmerged and draft at the check date, with head
`3dfb256ede835275e98d1f897e40756933852b86`. Four of twelve calibration processes
completed; the fifth did not launch because SMT sibling mean load was 12.987%,
above the fixed 10% gate. A Linux .NET current-thread allocation control passed;
that is not Unity IL2CPP, native or worker coverage. Refer to the PR for later status.

## Timing results and deployment eligibility are separate

The actual IL2CPP 1 MiB escaping-allocation positive control reported zero, so the
counter did not establish reliable zero-allocation observation. Worker/native
allocation-window coverage remains Unknown. Missing coverage must not be relabeled
as zero. This blocks allocation-qualified profiles, not every bounded timing claim.

Current-source performance is unmeasured **outside the specifically recorded
implementation/workload combinations**. Included records keep their actual build
manifests, dirty-source lists where applicable, hashes and environment. No new
default algorithm or deployment profile is promoted by this documentation.

Profitable automatic selection still requires actual calibration cost, a frozen
choice, independent selected executions and comparison against a reasonable fixed
baseline over the intended reuse lifetime. The completed SPH fixed-choice study
does not establish that additional result.

## Do not relabel the timing metric

```text
resident value = percentile95(block elapsed milliseconds / ticks in that block)
selection score = resident value + (ingress P95 + export P95) / lifetime ticks
```

The selection score is a sum of marginal quantiles. It is neither a measured
individual-tick P95 nor a measured complete-lifecycle P95. Adding a fixed boundary
P95 contribution to each resident block mean does not change that limitation.
Historical numeric fields and decisions retain their original values; newer
`TimingContract` metadata identifies the semantics.

The lifetime estimator gives conditional bounds from complete cost inputs and
independent paired processes. Missing costs/source identity or an uncertain savings
denominator yields Unknown. It assumes a supported constant-cost mapping and does
not manufacture an observed speedup. Periodic exports must follow the actual
cadence/boundary contract; see the [adoption guide](LAYOUT_ADOPTION.md).

## Historical records stay historical

| Retained record | Recorded implementation source | Interpretation |
| --- | --- | --- |
| [v0.3 formal runs](evidence/formal-il2cpp-2026-09-02/README.md) | `9df183942cd8dc8abfa05bd89f03d822c96c689e` | Original same-device selection-score observations; unchecked allocation zeros are unverified |
| [vNext formal runs](evidence/vnext-formal-il2cpp-2026-09-02/README.md) | `c84cf47b62f28b26c34d72acaf16ace23f674ddb` | Original paired-block score observations; historical allocation limitation applies |
| [September 7 integrated archive](evidence/optimization-vnext-2026-09-07/README.md) | `4ffa47271306e985d9cade5d77489bd172c0360f` | Original bounded envelope/search/counter experiments; controls cover only their stated thread scope |

[Historical allocation limitation](evidence/HISTORICAL_ALLOCATION_MEASUREMENT_LIMIT.md) ·
[September 8 repair contract](MEASUREMENT_REPAIR_2026-09-08.md) ·
[Historical portfolio figure](portfolio/README.md) ·
[Roadmap and release gates](ROADMAP_V0.4_TO_V0.6.md)

No raw JSON, archives, historical figures or numeric decisions were regenerated
for this presentation update.

## External workload terminology

BabelStream/STREAM are external bandwidth workload starting points. C#/Burst and
adapted Windows/native variants retain their port labels; they are not certified
original benchmark submissions. LLAMA is a layout library: name the concrete
example or application and mapping rather than claiming a general library score.
HeCBench must likewise be identified by concrete workload. Google Benchmark,
BenchmarkDotNet and Unity Performance Testing are measurement frameworks, not
standardized workloads; PRK is not a ranking suite.

[External semantic contract](../Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/CONTRACT.md) ·
[Upstream source lock](../Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Upstream~/upstream-lock.json) ·
[Kernel delivery contract](../Tools/KernelContracts/CONTRACT.md)

Other kernel increments and generated-storage candidates retain their individually
recorded registration and Unmeasured status. A measured external case is not
validation of every kernel or a release of the entire vNext foundation.
