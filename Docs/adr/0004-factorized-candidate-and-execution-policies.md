# ADR 0004: Factorized candidate and execution policies

Status: Accepted for the v0.4 scientific-core slice
Date: 2026-09-02
Roadmap scope: Items 1 and 2 only

## Context

Schema 2 identifies a candidate by layout and logical batch size. That is sufficient to replay the v0.3 candidate pipelines, but it cannot distinguish a layout effect from a different kernel shape or execution topology. Particle AoSoA8, for example, changes both storage and control flow. Treating its result as a layout-only effect would overstate the evidence.

Candidate identity also crosses Unity results, CSV, render manifests, future profiles, and integration branches. A display label is not a stable join key.

Scenario identity is the pair `ScenarioId` and `ContractVersion`. Per-candidate results persist both fields so aggregation cannot silently combine revisions of a workload contract.

## Decision

`CandidateDescriptor.CandidateId` remains the canonical cross-artifact identity. The descriptor adds four serializable, explicit policy dimensions:

- `LayoutPolicy`: stable policy ID plus declared block width, alignment, and padding metadata.
- `KernelPolicy`: stable policy ID, control-flow classification, and vector width.
- `BatchPolicy`: stable policy ID and logical-record batch size.
- `ExecutionPolicy`: stable policy ID, topology, and temporal-block declaration.

The legacy `LayoutId` and `LogicalBatchSize` fields remain present and must agree with the new policies. They are compatibility fields, not an alternate identity. `DisplayName` remains presentation-only.

The included workloads implement both execution topologies that preserve their declared semantics:

- `FrameFaithful` schedules and completes one logical tick at a time.
- `DependencyChain` schedules each tick after the preceding handle and completes the chain once.

`TemporalBlock<K>` can be constructed only with an explicit declaration that the workload semantics permit reordering. Neither included workload makes that declaration, and both reject such candidates.

ParticleIntegrate adds a scalar branchless AoS kernel beside the existing scalar branched AoS kernel. Both use identical AoS storage, ingress, export, parity, lifetime, and logical work. This is a negative control for attributing branch removal to layout. The default Particle matrix crosses four implemented layout/kernel families, four batch sizes, and both execution topologies. TransformExport crosses its common full-matrix kernel, two layouts, four batch sizes, and both topologies.

## Compatibility and artifact versioning

This branch proposes top-level result schema 3 and policy sub-schema 1. Schema-2 JSON fields are retained. `CalibrationProfileMigration.UpgradeInMemory` fills missing policies without changing `CandidateId`; it never edits a source evidence file. Policy migration is defined only from sub-schema 0 to 1. Sub-schema 1 is validated without normalization, and every other version is rejected. Canonical candidate, layout, and policy IDs are non-empty and may not contain surrounding whitespace. Schema-3 profiles are validation-only inputs to the migration API, so missing factors or inconsistent compatibility fields are rejected rather than reconstructed. [`ADR 0006`](0006-vnext-integration-protocol.md) accepts schema 3 for the unreleased integration while leaving the package version unchanged.

The future profile-compatibility layer must additionally bind schema, candidate-set and binary hashes, environment, and calibration settings. That fingerprint/cache work remains roadmap item 6 and is not inferred from the scenario identity pair in this slice.

The fixed-result renderer accepts both schema 2 and the proposed schema 3. Schema-3 heatmap rows use layout, kernel, batch-policy, and execution identities, so candidates with the same legacy layout/batch coordinate cannot overwrite one another. Selection markers continue to come only from `FinalDecision`.

## Consequences

Candidate matrices are larger and formal Player runs cost more. In return, stored results can expose the measured factor combination without guessing from a candidate name. This slice does not claim a fully crossed causal design for every possible layout/kernel pairing, and it does not add AoSoA4, AoSoA16, or aligned/padded storage implementations. Those remain roadmap work after this protocol boundary is integrated.

The new branchless job adds a Burst AOT entrypoint requirement. EditMode parity can validate semantics, but only real non-Development Mono and IL2CPP Player builds can satisfy the corresponding AOT and performance-evidence gates.
