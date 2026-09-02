# ADR v0.5-01: Bounded generated storage and codec scaffolds

Status: Accepted on feature branch

Date: 2026-09-02

## Context

ADR 0003 limited the first Source Generator to factory registration because generating arbitrary workload semantics or rewriting kernels would overpromise correctness and optimization. Roadmap item 5 asks for a later, narrower extension: remove mechanical storage and boundary-codec boilerplate while keeping semantic code developer-authored.

## Decision

Extend the Source Generator only for an explicit versioned flat-record schema.

- Opt-in is per struct through `GenerateDataLayoutAttribute` and an explicit `DataLayoutFieldAttribute` on every instance field.
- Accepted types are writable numeric scalars and a bounded allowlist of unmanaged `Unity.Mathematics` values.
- Generated artifacts are AoS, SoA, hot-block/cold-side-array AoSoA, ingress/export, disposal, schema hash, and parity field-map scaffolds.
- Output order and schema hashing are ordinal and deterministic.
- Generated construction and field access are direct. Reflection, `Activator`, open generic discovery, runtime type lookup, and generated schedule sites are absent.
- Properties, nested records, references, explicit offsets, aliases, custom pack/size, fixed buffers, unsupported alignment, and non-value semantics produce compile-time diagnostics.
- The generator does not emit or rewrite a workload kernel, declare a tolerance, decide semantic parity, choose a candidate, or claim to replace Burst.

`MinimumCompatibleSchemaVersion` is author-declared metadata over the generated field map. It is not an automatic record migration and grants no permission to reuse a performance profile.

## Consequences

Workloads can reuse mechanical storage and codec shapes without hiding their Burst Jobs or schedules. AoSoA output is intentionally a scaffold: workload authors can consume its public blocks from explicit Jobs, but the generator does not assert that the shape is beneficial.

The allowlist excludes otherwise unmanaged domain structs. Such fields may have aliasing, ownership, packing, or semantic rules the generator cannot prove; those workloads retain handwritten storage.

This ADR narrows and supersedes only ADR 0003's statement that all candidate storage must remain handwritten. ADR 0003's direct-construction AOT and no-arbitrary-optimization boundaries remain in force.

## Verification boundary

Roslyn tests compile generated output for two structurally different synthetic workload schemas, compare deterministic output, and exercise every diagnostic family. Unity Sample tests round-trip actual Particle and Transform records. Release Player build/run status must be reported separately and is not implied by this ADR.

Copyright (c) 2026 Edwin Liu. All Rights Reserved.
