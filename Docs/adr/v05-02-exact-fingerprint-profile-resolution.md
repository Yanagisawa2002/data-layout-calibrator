# ADR v0.5-02: Exact deployment fingerprints and tuned-AoS fallback

Status: Accepted on feature branch

Date: 2026-09-02

## Context

A frozen layout decision is unsafe when its workload, candidates, binary, compiler, backend, hardware, worker topology, or calibration contract changes. A permissive cache can silently deploy a stale selection. Conversely, presentation or cache code must not reinterpret raw measurements and manufacture a new winner.

## Decision

Persist a strict fingerprint, opaque raw suite, frozen final decision, and provenance in a versioned deployment profile.

- Workload identity is workload/`ScenarioId` plus contract version.
- Candidate identity is canonical `CandidateId`; `DisplayName` is never a join key.
- The fingerprint binds record schema and candidate hashes, dependency/compiler versions, backend/build identity, explicit CPU/ISA/worker facts, binary hash, and key calibration settings.
- Inputs are explicit. The core never guesses CPU, ISA, backend, compiler, or build facts.
- The no-reflection profile codec performs direct fixed-order field parsing and validates document, raw-suite, decision, settings, and fingerprint SHA-256 values.
- The raw suite is audit material. The resolver consumes the frozen selected `CandidateId` and never reselects from raw samples.
- Exact match is the default.
- A compatible match requires an opt-in rule for one exact stored/expected fingerprint pair, enumerated allowed differences, and an evidence reference.
- Fundamental workload, record schema, candidate, backend, build, CPU/ISA/worker, binary, settings, or integrity differences cannot be waived by a compatible rule.
- Missing, corrupt, unsupported, incompatible, or unavailable selections return the caller-supplied tuned AoS candidate.

Profile document schema 1 migrates in memory to schema 2 by adding the decision hash. Migration preserves the raw suite and frozen decision. A changed record schema version/hash is not migrated into a reusable performance decision; it invalidates and requires calibration.

## Consequences

Cache misses and upgrades are safe but conservative. A patch-level toolchain change also falls back unless an exact-pair compatibility rule is supported by separately reviewed evidence.

The store retains provenance and raw material without giving storage or presentation code decision authority. Historical calibration suites do not need mutation and remain byte-for-byte unchanged.

Hosts must establish trustworthy capture for binary hash, build flags, CPU, and ISA before publishing a reusable profile. An unavailable fact is a reason not to cache, not a reason to invent a value.

## Verification boundary

Synthetic tests cover exact reuse, candidate/compiler/backend/settings/worker invalidation, missing/corrupt/unknown profiles, unavailable candidates, schema-1-to-2 document migration, record-schema hard invalidation, explicit compatibility pairs, atomic replacement, and raw-suite tamper detection. Synthetic names are labeled and are not Player, device, ISA, hardware-counter, or cross-device evidence. Release Player AOT status must be reported separately.

Copyright (c) 2026 Edwin Liu. All Rights Reserved.
