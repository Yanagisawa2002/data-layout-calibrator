# vNext integration delivery report

Status: integration candidate; not released

Date: 2026-09-02

Branch: `codex/vnext-integration`

Package version: `0.3.0-preview.1` (unchanged)

This report covers repository integration and executable validation. It does not
promote v0.4, v0.5, or v0.6, replace published v0.3 evidence, or claim new
performance, IL2CPP, hardware-counter, ISA, device, or cross-device results.

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
| whitespace/conflict-marker check | passed |
| local Markdown-link check | passed; all 31 Markdown files resolved |
| sensitive-path/credential-pattern scan | passed; no additions found |
| rights, dependency, and historical-evidence review | passed; no rights-file or dependency change, and only the planning-only manifest is new under `Docs/evidence` |

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

The required merged-tree build was attempted and failed before IL2CPP compilation:

```text
Error building Player: Currently selected scripting backend (IL2CPP) is not installed.
```

Unity returned exit code 1. No current-tree IL2CPP binary or Burst AOT result exists,
so IL2CPP remains an explicit release blocker. Historical v0.3 IL2CPP artifacts do
not satisfy this changed-tree gate.

## Replay and presentation

The tiny Mono suite was replayed through fixed-result renderer `1.2.0`. The manifest
input SHA matched the suite bytes, and each scenario's status, baseline, selected,
best-measured candidate, and improvement value matched `FinalDecision` exactly.
The transient audit output remains under ignored `work/` during integration and is
not promoted to formal evidence.

## Roadmap and release gaps

The following prevent a v0.4-v0.6 release claim:

- missing AoSoA4/AoSoA16 and aligned/padded controls, and no completed crossed
  main-effect/interaction analysis;
- no formal measured advantage-envelope axis scan or adaptive-vs-exhaustive run;
- generated storage remains bounded scaffold coverage rather than production
  replacement of the benchmark storage paths;
- no merged-tree IL2CPP consumer build;
- no real counter provider, counter overhead experiment, or retained compiler
  mechanism artifact;
- 0 configured executable device requests, no registered physical-device identity,
  no new workload matrix, and no device-level hierarchical statistics; and
- no authorization to merge `main`, tag, or publish a GitHub Release.

The appropriate repository action is a draft pull request for review. A later
release must state exactly which remaining gates it actually satisfies.

## Provenance and rights

Historical evidence files are unchanged. No external source, vendor binary,
profiler capture, device identity, credential, or third-party code was added by this
integration. The repository remains proprietary and All Rights Reserved under
[`LICENSE`](../LICENSE) and [`PROVENANCE.md`](../PROVENANCE.md).

Copyright (c) 2026 Edwin Liu. All Rights Reserved.
