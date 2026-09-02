# vNext integration delivery report

Status: integration candidate; not released

Date: 2026-09-02

Branch: `codex/vnext-05-integration`

Package version: `0.3.0-preview.1` (unchanged)

This report covers repository integration and executable validation. It does not
promote v0.4, v0.5, or v0.6 or replace published v0.3 evidence. New performance
evidence is scoped to five processes on one recorded Windows/CPU/IL2CPP
configuration; it makes no hardware-counter, causal-mechanism, cross-ISA, or
cross-device claim.

## Ordered branch record

| Order | Branch | Immutable tip | Merge commit | Integration gate |
| --- | --- | --- | --- | --- |
| 1 | `codex/v04-scientific-core` | `5cec65fffa919801308c1a0a861b352274342b09` | `dfe240d` | Unity 64/64; renderer 13/13; generator 4/4 |
| 2 | `codex/advantage-envelope` | `13c8ebf9cde1cd440f9e897e4651fe8b43975beb` | `f0a1303` | combined Unity 83/83; renderer 22/22 |
| 3 | `codex/v05-generator-profiles` | `5639b9f8e0cac8c83f4cf7888abb3782fbd289eb` | `dd0a390` | generator 11/11; combined Unity 109/109 |
| 4 | `codex/v06-evidence-lab` | `7812d020733c4a250da0f336d2cd3385264db254` | `9c398aa` | combined Unity 133/133; Evidence Lab 22/22; manifest 0 ready / 18 blocked |

The v0.5 merge exposed one cross-version enum mismatch. Commit `b23cfeb` makes
schema-3 `Regression` a supported frozen deployment status while retaining the
invariant that every non-`Optimized` status selects its baseline.

## Integration-owned protocol

Commit `135b90d` adds the executable bridge frozen by
[`ADR 0006`](adr/0006-vnext-integration-protocol.md):

- canonical UTF-8 full candidate-definition and order-independent candidate-set
  hashes, with one C#/Python golden vector;
- exact paired scientific component-P95 bootstrap draws exposed to the envelope;
- envelope quantiles computed in the same log-ratio space as the scientific core;
- explicit evidence partition/hash plus host-owned contract and memory feasibility;
- scale-aware equality at the adaptive minimum-effect threshold;
- optional external `AdvantageEnvelopeArtifactReference` bound to artifact,
  schema, engine, scenario contract, candidate set, and measurement schema; and
- renderer validation/copying of that reference without reading cells or changing
  `FinalDecision`.

No runtime reflection or open-generic discovery was introduced. Hashing and
replicate materialization are calibration/control-plane operations, outside measured
resident and boundary hot paths.

## Final deterministic validation

| Check | Result |
| --- | --- |
| Unity 6000.5.3f1 EditMode | passed, 139/139 |
| Source Generator Release tests | passed, 11/11 |
| fixed-result and advantage-envelope renderer tests | passed, 25/25 |
| Evidence Lab tests | passed, 22/22 |
| Evidence Lab checked-in manifest validation | passed; schema 1 |
| Evidence Lab deterministic plan | passed; 0 executable requests, 18 blocked matrix entries |
| GitHub non-Unity CI | passed; repository-static, Python tools, and Source Generator jobs |
| whitespace/conflict-marker check | passed |
| local Markdown-link check | passed |
| sensitive-path/credential-pattern scan | passed; no additions found |
| rights, dependency, and historical-evidence review | passed; no rights-file or dependency change, and vNext evidence is isolated from immutable v0.3 evidence |

Synthetic fixtures used by unit tests remain labelled synthetic and contribute no
observed coverage.

## Player and AOT validation

### Windows x64 Mono

A non-Development Mono Player build succeeded with Burst AOT. The build gate found a
non-empty `lib_burst_generated.dll`, a Burst entrypoint manifest, and every required
ParticleIntegrate/TransformExport job entrypoint, including the branchless AoS
control.

A deliberately tiny opt-in behavioral audit then exited 0. It exercised the v0.5
generated Particle AoSoA and Transform SoA round trips plus frozen-profile
codec/resolution. Its schema-3 suite contained 48 candidate results across the two
existing scenarios; all completed, passed parity, and recorded zero resident and
boundary managed allocation. Both final decisions conservatively retained AoS.

The run used tiny counts, three resident/boundary samples, 100 bootstrap iterations,
and one warmup block. It is a compilation, AOT reachability, schema, parity,
allocation, and failure-path audit only. Its timings are not retained or cited as
performance evidence.

### Windows x64 IL2CPP

After installing Windows Build Support (IL2CPP) for Unity 6000.5.3f1, the
non-Development IL2CPP Player build completed with exit code 0. The build gate
verified non-empty IL2CPP code and metadata, a non-empty Burst library, the Burst
entrypoint manifest, and all required ParticleIntegrate/TransformExport jobs.

The tiny opt-in IL2CPP behavioral audit also exited 0. It exercised the same
generated-storage and frozen-profile AOT probe as Mono and produced a valid
schema-3 two-scenario suite. Both tiny-run decisions conservatively retained AoS;
the tiny timings are not retained as performance evidence.

The frozen formal Player was then executed in five sequential, fresh processes
under the [preregistered protocol](evidence/VNEXT_FORMAL_BENCHMARK_PROTOCOL_2026-09-02.md).
Every process used 1,048,576 calibration records, a 1,000,003-record untouched
holdout, 40 resident samples, 20 boundary samples, 600 lifetime ticks, and 4,000
paired-block bootstrap iterations.

The fixed primary run measured ParticleIntegrate at 83.57% lower holdout
amortized P95 than its tuned AoS baseline, with a single-Player 95% paired-block
CI of [83.14%, 84.57%]. Across all five retained runs, the reduction ranged from
82.96% to 83.63% (median 83.57%); the worst per-run lower confidence bound was
82.45%. ParticleIntegrate selected `AoSoA8 / PackedBranchless8` in 5/5 launches,
while TransformExport retained tuned AoS in 5/5.

All 240 calibration results and 10 Particle holdout results completed, passed
typed parity, and recorded zero resident/boundary managed allocation. The suite
hashes, preflight context, raw arrays, exact decisions, and descriptive manifest
are retained under
[`evidence/vnext-formal-il2cpp-2026-09-02`](evidence/vnext-formal-il2cpp-2026-09-02/README.md).
No cross-process hierarchical interval was computed, so the five-run range is
reported descriptively.

## Replay and presentation

The preregistered primary IL2CPP suite was replayed through fixed-result renderer
`1.2.0`. The manifest input SHA matched the suite bytes, and each scenario's
status, baseline, selected, best-measured candidate, improvement, confidence, and
evidence-scope fields matched `FinalDecision` exactly. The checked-in PNG/GIF and
renderer manifest are linked from the retained evidence README; the renderer did
not rank or reselect candidates.

## Roadmap and release gaps

The following prevent a v0.4-v0.6 release claim:

- missing AoSoA4/AoSoA16 and aligned/padded controls, and no completed crossed
  main-effect/interaction analysis;
- no formal measured advantage-envelope axis scan or adaptive-vs-exhaustive run;
- generated storage remains bounded scaffold coverage rather than production
  replacement of the benchmark storage paths;
- no real counter provider, counter overhead experiment, or retained compiler
  mechanism artifact;
- the retained five-process evidence has no process-level aggregate CI; the
  planning matrix still has 0 configured executable requests, no registered
  physical-device identity, no new workload matrix, and no device-level
  hierarchical statistics; and
- no authorization to merge `main`, tag, or publish a GitHub Release.

The appropriate repository action is a draft pull request for review. A later
release must state exactly which remaining gates it actually satisfies.

## Provenance and rights

Historical evidence files are unchanged. The new vNext evidence contains suite
JSON, preflight metadata, and renderer output but no Player binary, external
source, profiler capture, credential, or third-party code. The repository remains
proprietary and All Rights Reserved under
[`LICENSE`](../LICENSE) and [`PROVENANCE.md`](../PROVENANCE.md).

Copyright (c) 2026 Edwin Liu. All Rights Reserved.
