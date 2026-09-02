# Unreleased vNext integration notes

These notes describe a review candidate on `codex/vnext-integration`. They are not a
release announcement. The package remains `0.3.0-preview.1`; no tag or GitHub Release
has been created.

## What is integrated

- Schema-3 factorized candidate definitions and fail-closed scientific validation.
- Paired-block log-ratio bootstrap and same-device process hierarchy.
- Immutable advantage-envelope, break-even, Pareto, adaptive elimination, and
  regret-audit foundations.
- Canonical candidate-definition/set hashes and exact scientific-to-envelope
  replicate reuse.
- Generated storage/codec scaffolds for the two sample record shapes.
- Strict deployment fingerprint, profile codec/store, and tuned-AoS fallback.
- Optional counter boundary and deterministic device/ISA/workload evidence planning.
- Fixed-result renderers that preserve frozen decisions and validate external
  envelope references.

## Compatibility

- Schema 2 is never rewritten; migration is in memory only.
- Native schema 3 rejects unknown versions and incomplete paired metadata.
- `CandidateId` remains the join key and is bound to the full semantic definition.
- Only `Optimized` may select a non-baseline deployment candidate.
- The default reusable profile path remains exact fingerprint match.

## Validation summary

- Unity EditMode: 139/139.
- Source Generator: 11/11.
- Result renderers: 25/25.
- Evidence Lab: 22/22; checked-in plan remains 0 executable / 18 blocked.
- Windows Mono Release + Burst AOT: build and tiny behavioral audit passed.
- Windows IL2CPP Release + Burst AOT: build and tiny behavioral audit passed.
- Preregistered full-size IL2CPP evidence: 5/5 fresh processes complete; all 240
  calibration and 10 Particle holdout candidate results passed parity with zero
  measured resident/boundary managed allocation.
- ParticleIntegrate: optimized in 5/5 processes. The primary holdout result was
  83.57% lower amortized P95 than tuned AoS, with a per-Player 95% CI of
  [83.14%, 84.57%]; the five-run descriptive range was 82.96%–83.63%.
- TransformExport negative control: tuned AoS retained in 5/5 processes.

The tiny Mono/IL2CPP audits used intentionally small settings and are not
performance evidence. The formal evidence is same-device process replication;
there is no new counter, causal-mechanism, cross-ISA, or cross-device claim.

## Not release-complete

The remaining candidate/control matrix, production scaffold adoption, formal
envelope/adaptive measurements, counter provider work, process-hierarchical
aggregation, and real multi-device/workload evidence are still pending. See
[`VNEXT_DELIVERY_REPORT.md`](VNEXT_DELIVERY_REPORT.md) and
[`ROADMAP_V0.4_TO_V0.6.md`](ROADMAP_V0.4_TO_V0.6.md).

Copyright (c) 2026 Edwin Liu. All Rights Reserved.
