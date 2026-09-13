# Data Layout Calibrator

A Unity/Burst library for choosing data layouts with explicit conversion, resident
work, export, allocation coverage and independent holdout contracts.

**Choose a layout only when its complete cost repays within the application's
actual lifetime and export cadence.** Start with the [adoption guide](Docs/LAYOUT_ADOPTION.md)
and [CPU-only lifetime API example](Tools/Examples/LifetimeDecision/README.md).
The example uses explicitly synthetic costs to show finite conditional bounds
and Unknown outcomes; it produces no benchmark result or deployment profile.

This unreleased integration includes the local vNext implementation and the
2026-09-08 measurement-contract repairs. [September 10 actual external comparisons](Docs/ACTUAL_COMPARISON_REPORT_2026-09-10.md)
now cover frozen BabelStream variants and the auxiliary LLAMA code_comp example,
including full-output IL2CPP/Burst checks. They do not establish general layout
benefit or deployment eligibility: the actual 1 MiB allocation control failed,
and worker/native coverage remains Unknown. Other current-source performance is
Unmeasured / 待验证. Historical measurements retain their original source identities;
no new default algorithm is promoted.

## Delivered scope

- Factorized layout/kernel/batch/execution policies, paired block inference,
  same-device process hierarchy, frozen AoS/candidate holdout, generated storage,
  exact fingerprint profiles and advantage-envelope logic from local vNext.
- Injectable allocation capability with explicit availability, units and scope,
  1 MiB positive/empty controls, and complete observation-window gates. Unknown
  counters cannot pass as numeric zero. Current-thread managed observations do not
  certify workers or native allocations; the default required scope includes workers.
- Separate block-mean P95, component-P95 selection score, individual tick and full
  lifecycle interfaces. Historical numeric fields and decisions are preserved.
- Conservative lifetime cost bounds with complete setup/teardown costs and paired
  independent-process inputs. Missing costs, source identity or an uncertain savings
  denominator yield Unknown. The model does not manufacture a measured speedup.
- Explicit measurement commands separated from a PR workflow restricted to static,
  build and allowlisted deterministic CPU correctness checks.

[Repair contract and validation scope](Docs/MEASUREMENT_REPAIR_2026-09-08.md) ·
[Roadmap status](Docs/ROADMAP_V0.4_TO_V0.6.md) ·
[Package API](Packages/com.yanagisawa.data-layout-calibrator/README.md)

## Metric and selection contract

```text
resident value = percentile95(block elapsed milliseconds / ticks in that block)
selection score = resident value + (ingress P95 + export P95) / lifetime ticks
```

The selection score is a sum of marginal quantiles. It is neither a measured tick
P95 nor a measured lifecycle P95. Derived amortized samples add a fixed boundary
P95 contribution to every resident block mean. New additive `TimingContract` metadata
and renderer labels expose those semantics without changing historical numbers.

A non-AoS selection requires valid allocation coverage, parity, a practical score
gain and paired uncertainty gates; confirmation requires the exact frozen candidate
and independent dataset/partition on the same source fingerprint. Regression,
StatisticalTie and Inconclusive retain AoS. Missing baseline eligibility is Invalid.
The profile resolver invalidates changed workload, candidate/kernel binary, compiler,
device or calibration settings; it never remeasures on cache lookup.

## Safe local and PR checks

```powershell
python -m pip install -r Tools/ResultRenderer/requirements.txt
python Tools/CI/validate_functional.py --build-only
python Tools/CI/validate_functional.py
python Tools/CI/check_repository.py
python Tools/KernelContracts/external_sources.py
```

The allowlist excludes historical tests that read real counters or run timed
calibration even when their workloads are fixtures. No Unity, Player, benchmark,
autotuning or profiling is invoked by this entry or PR CI. Python tests operate on
DTOs and synthetic fixtures; they do not render recordings or collect measurements.

`Tools/CI/Build-UnityCompileOnly.ps1` is an optional headless Editor script-compilation
entry with short-path/dependency prechecks. It was not executed in this repair run.
Existing Player/performance scripts and `-dla-run`, `-dla-search-run`,
`-dla-envelope-run` and `-dla-counter-run` remain explicit measurement entrypoints.
They were not executed in this delivery. Source fingerprints and allocation
capability/coverage checks still apply. Player hosts bind their build manifest and binary identity automatically;
`-dla-allocation-scope current-thread-managed|all-managed|all` declares the required
coverage. Suite/search hosts default to current-thread managed coverage; envelope
runs retain their declared scope. Worker and native allocation freedom remain
Unknown unless the provider covers them.

## External workloads

BabelStream/STREAM are the external general bandwidth benchmark starting points.
LLAMA is an external layout algorithm library: its explicitly named nbody examples
are library-native workloads, not a general benchmark suite. HeCBench must be named
by concrete workload. Ports retain that label and cannot claim original benchmark
scores. Google Benchmark, BenchmarkDotNet and Unity Performance Testing are
measurement frameworks, not standardized workloads; PRK is not a ranking suite.
[Source/semantic contract](Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/CONTRACT.md),
[source lock](Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Upstream~/upstream-lock.json)
and [kernel delivery](Tools/KernelContracts/CONTRACT.md) accompany the code.
Implemented kernel increments include hot/cold AoS, block codecs/exports, four-record
TRS and LLAMA four-target n-body with source-block reuse and an update/move barrier.
All new candidates remain unregistered and Unmeasured.
This round prepares and compiles external code only; it executes no external benchmark.

## Historical evidence

| Retained record | Recorded implementation source | Interpretation |
| --- | --- | --- |
| [v0.3 formal runs](Docs/evidence/formal-il2cpp-2026-09-02/README.md) | `9df183942cd8dc8abfa05bd89f03d822c96c689e` | Original same-device selection-score observations; unchecked allocation zeros are unverified |
| [vNext formal runs](Docs/evidence/vnext-formal-il2cpp-2026-09-02/README.md) | `c84cf47b62f28b26c34d72acaf16ace23f674ddb` | Original paired-block score observations; historical allocation limitation applies |
| [2026-09-07 integrated archive](Docs/evidence/optimization-vnext-2026-09-07/README.md) | `4ffa47271306e985d9cade5d77489bd172c0360f` | Original bounded envelope/search/counter experiments; provider controls cover their stated thread scope |

[Historical allocation limitation](Docs/evidence/HISTORICAL_ALLOCATION_MEASUREMENT_LIMIT.md).
No raw JSON, archive or historical visualization has been regenerated for the new code.
The [engineering overview](Docs/portfolio/README.md) and its recorded figures describe
the historical implementation and results.

## Repository and license

The portable core is in `Packages/com.yanagisawa.data-layout-calibrator/Runtime`;
Samples contain workload kernels; `SourceGenerators~` holds the generator source;
`BenchmarkProject` is the Unity host; `Tools` contains build/functional and dormant
measurement tooling. The core knows no Particle workload types.

Canonical repository: [Yanagisawa2002/data-layout-calibrator](https://github.com/Yanagisawa2002/data-layout-calibrator).
[Citation](CITATION.cff), [authorship](AUTHORS.md), [provenance](PROVENANCE.md).
Copyright (c) 2026 Edwin Liu. The [limited benchmark reproduction permission](LICENSE)
permits benchmark reproduction and publication of results; it does not grant general
redistribution, sublicensing or product integration rights.
