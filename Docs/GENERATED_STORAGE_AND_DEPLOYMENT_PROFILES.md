# Generated storage and deployment profiles

Status: v0.5 tooling slice

Authoritative roadmap: `Docs/ROADMAP_V0.4_TO_V0.6.md`, items 5 and 6

Evidence status: implementation, deterministic tests, and tiny Mono/IL2CPP AOT behavioral probes passed; no performance, hardware-counter, ISA, or cross-device claim

## Boundary of the generator

`GenerateDataLayoutAttribute` opts a record into generator definition version 1. Every instance field must have an explicit contiguous `DataLayoutFieldAttribute` order and a Hot or Cold temperature. The record also declares a stable schema ID, record schema version, minimum declared-compatible version, and an AoSoA width of 4, 8, or 16.

For an accepted record, the generator emits:

- an AoS `NativeArray<Record>` storage;
- one `NativeArray<T>` per field for SoA;
- an AoSoA hot-block struct plus cold side arrays;
- deterministic allocate, direct ingress, direct export, read, write, and dispose methods;
- direct codec overloads for each storage;
- a parity field map with stable field order, name, type, temperature, and value semantics; and
- schema metadata and a SHA-256 over the explicit schema definition.

The generated codec is mechanical. It does not emit Jobs, schedule work, transform arbitrary kernels, infer tolerances, decide parity, choose a layout, invoke Burst, or claim to replace Burst optimization. Workload authors continue to write concrete AOT-visible Jobs and literal schedule sites. The existing benchmark candidates and handwritten kernels are unchanged.

Definition version 1 accepts flat writable fields containing bounded numeric scalars or an allowlisted unmanaged `Unity.Mathematics` vector, matrix, or quaternion. It rejects:

| Diagnostic | Rejection |
| --- | --- |
| `DLCGEN100` | Invalid schema ID/version/range, unsupported definition version, or AoSoA width |
| `DLCGEN101` | Generic, nested, ref-like, inaccessible, property-bearing, empty, or no-hot-field record |
| `DLCGEN102` | Reference, nested record, ownership-bearing, readonly, fixed-buffer, inaccessible, or non-allowlisted field |
| `DLCGEN103` | Missing, negative, duplicate, or non-contiguous explicit field order |
| `DLCGEN104` | Explicit offsets, aliasing, custom pack, custom size, or non-default struct layout |
| `DLCGEN105` | Unknown temperature or non-value field semantics |
| `DLCGEN106` | Duplicate schema ID in one compilation |

`MinimumCompatibleSchemaVersion` is metadata asserted by the record author. It does not synthesize a semantic migration. A changed record schema version or hash is a hard deployment-profile invalidation until a new calibration is frozen.

The Particle and Transform Export Samples declare independent schemas and exercise generated AoS, SoA, and AoSoA boundary round trips. Their generated scaffolds are consumer examples; their benchmark kernels remain handwritten.

## Strict deployment fingerprint

`CalibrationProfileFingerprintBuilder` receives every value explicitly. It performs no hardware or ISA discovery and therefore cannot invent unavailable environment facts. Its SHA-256 binds:

- `ScenarioId`/workload ID and contract version;
- record schema ID, version, and generated schema hash;
- an order-independent hash of canonical candidate definitions keyed by stable `CandidateId`;
- Unity, Burst, Collections, and Mathematics versions;
- scripting backend, build target, architecture, and canonical build flags;
- operating system, CPU description, explicit ISA description, logical processor count, and Job worker count;
- Player/binary SHA-256; and
- an order-independent hash of key calibration settings.

Hosts must capture and pass accurate values or decline to create a reusable profile. Synthetic unit fixtures use visibly synthetic values and are not retained as evidence.

## Frozen profile cache

A schema-2 `FrozenDeploymentProfile` contains three authoritative payloads:

1. the strict fingerprint;
2. the frozen final decision, keyed only by canonical baseline and selected `CandidateId`; and
3. the opaque raw suite plus run/repository/commit/evidence-scope provenance.

The raw suite is retained for audit and replay. Neither the codec, store, nor resolver recomputes a winner from it. `FinalDecision` remains authoritative.

`FrozenDeploymentProfileFactory.Create` is a trusted capture boundary. Its caller must copy the decision from the authoritative `ScenarioCalibrationProfile.FinalDecision`; it must not construct a replacement decision from display names, renderer output, or an independent interpretation of samples. The raw suite is deliberately opaque here. The document, raw-suite, decision, and fingerprint hashes detect later mutation, but they do not authenticate the caller or prove that arbitrary raw-suite bytes semantically produced the supplied decision.

Only an `Optimized` frozen decision may select a candidate other than its baseline. `Inconclusive`, `StatisticalTie`, and `Invalid` decisions remain cacheable for audit and deterministic reuse only when their selected `CandidateId` equals their baseline `CandidateId`. The rule is expressed as “non-Optimized selects baseline,” so newly supported non-optimized statuses inherit the same conservative gate.

`FrozenDeploymentProfileCodec` is a fixed-order Base64 field codec with a document SHA-256, raw-suite SHA-256, decision SHA-256, and fingerprint SHA-256. It uses direct field assignment without reflection or runtime type discovery. Schema-1 profile documents migrate in memory to schema 2 by adding and validating the frozen-decision hash; the raw suite and selected candidate are preserved unchanged. Unknown schemas are unsupported, not guessed.

`FileFrozenDeploymentProfileStore` hashes logical keys before forming paths and replaces cache entries through a same-directory temporary file. It returns Missing, Corrupt, UnsupportedSchema, or StorageError rather than treating a failed read as a decision.

## Resolver and fallback

Exact fingerprint equality is the default and trusted path. A compatible match requires all of the following:

- the caller opts into compatible matches;
- a rule names the exact stored and expected fingerprint SHA-256 pair;
- the rule lists every allowed differing dimension; and
- the rule includes an evidence reference.

Workload, contract, record schema, candidate set, backend, target, architecture, build flags, operating system, CPU, ISA, processor/worker counts, binary, calibration settings, integrity, baseline, and selected-candidate availability are never compatible in this slice. An explicit rule cannot override those hard gates.

Missing, damaged, unsupported, incompatible, or unauthorized profiles resolve to the caller-supplied tuned AoS `CandidateId`. The caller must obtain that ID from the current candidate set; the resolver never infers identity from `DisplayName`.

## AOT consumer probe

`V05GeneratedScaffoldAotProbe` is linked into the benchmark Player. Only when `-dla-v05-aot-probe` is passed does it directly construct generated Particle/Transform storage, round-trip small canonical buffers, and encode/decode/resolve a clearly labeled synthetic profile. Keeping the probe behind a separate flag prevents it from warming or perturbing ordinary calibration runs. Tiny non-Development Mono and IL2CPP Players have executed this probe successfully, establishing AOT reachability for the frozen tree. The probe records no performance suite and makes no performance, hardware-counter, ISA, or cross-device claim.

## Integrated version assignments

| Model family | Version |
| --- | ---: |
| Generator attribute definition | 1 |
| Particle record schema | 1 |
| Transform record schema | 1 |
| Deployment fingerprint | 1 |
| Frozen deployment profile document | 2 (schema 1 migratable in memory) |

The top-level scientific suite is schema 3. Historical schema-2 calibration
suites and evidence remain byte-for-byte untouched and migrate only in memory.
The published package version remains `0.3.0-preview.1`; this tooling is an
unreleased vNext foundation, not a package release.

## Resolved and remaining integration decisions

- [`ADR 0006`](adr/0006-vnext-integration-protocol.md) freezes
  `dlc.candidate-definition.v1`, its uppercase SHA-256 representation, and an
  order-independent candidate-set encoding over all stable
  layout/kernel/batch/execution fields.
- Choose authoritative Player-side sources for binary hash, build flags, CPU, and ISA. If a value cannot be captured accurately, the host must not publish a reusable fingerprint.
- Decide governance for explicit compatibility-pair evidence. Exact-only should remain the release default.
- Decide when a changed record schema has earned a new calibration; the generator deliberately does not authorize reuse across record schema version/hash changes.

Copyright (c) 2026 Edwin Liu. All Rights Reserved.
