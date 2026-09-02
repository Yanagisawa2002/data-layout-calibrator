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
- Windows IL2CPP: blocked because the installed Editor lacks the scripting backend.

The Mono audit used intentionally tiny settings and is not performance evidence.
There is no new counter, ISA, device, or cross-device claim.

## Not release-complete

The remaining candidate/control matrix, production scaffold adoption, formal
envelope/adaptive measurements, IL2CPP validation, counter provider work, and real
multi-device/workload evidence are still pending. See
[`VNEXT_DELIVERY_REPORT.md`](VNEXT_DELIVERY_REPORT.md) and
[`ROADMAP_V0.4_TO_V0.6.md`](ROADMAP_V0.4_TO_V0.6.md).

Copyright (c) 2026 Edwin Liu. All Rights Reserved.
