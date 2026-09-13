# ADR 0006: vNext integration protocol

Status: Accepted for the unreleased integration foundation

Date: 2026-09-02

Integration baseline: `644893990ed18e56619da8d2737e6b7592eb6080`

This ADR freezes the shared boundaries required to compose the scientific core,
advantage envelope, generator/profile tooling, and evidence lab. It does not mark
roadmap v0.4, v0.5, or v0.6 complete and does not create new Player, device, ISA,
counter, or performance evidence.

## Decision

### Schema ownership

| Model family | Version | Rule |
| --- | ---: | --- |
| calibration suite and scenario profile | 3 | schema 2 remains immutable; migration is in memory only; schema 3 validates and never repairs |
| candidate policy and raw sample metadata | 1 | required for native schema-3 evidence |
| bootstrap interval | 1 | estimator kind and realized log-ratio provenance are explicit |
| advantage envelope / adaptive plan | 1 | only decision engine `1.0.0` is accepted |
| candidate definition / paired cost replicates | 1 | owned by this integration protocol |
| generator definition and record schemas | 1 | record schemas remain workload-owned |
| deployment fingerprint | 1 | exact-match reuse is the default |
| frozen deployment profile | 2 | schema 1 may migrate in memory |
| counter artifact | 1 | adjunct evidence only |
| validation manifest, plan, observation, report | 1 | planning and retained-artifact verification only |

Unknown versions fail closed. Historical schema-2 JSON files are never rewritten or
silently relabeled as paired evidence.

### Candidate identity and canonical bytes

`CandidateId` is the join key. `CandidateDefinitionSha256` binds that ID to all
semantic and stable-order fields, so two definitions cannot alias merely by reusing a
name. `DisplayName` is excluded because it is presentation-only.

`dlc.candidate-definition.v1` uses UTF-8 without a BOM. Every string is encoded as
`<UTF-8-byte-count>:<value>\n`; every integer is invariant decimal followed by LF;
every Boolean is `1\n` or `0\n`. Fields are emitted in this order:

1. schema identifier, canonical schema version, candidate policy schema version;
2. `CandidateId`, `LayoutId`, `LogicalBatchSize`, `IsBaseline`, `SortOrder`;
3. layout policy ID, block width, alignment bytes, padding bytes;
4. kernel policy ID, control-flow enum value, vector width;
5. batch policy ID and logical batch size; and
6. execution policy ID, topology enum value, temporal block ticks, and semantic
   reordering declaration.

The SHA-256 representation is exactly 64 uppercase hexadecimal characters. The
cross-language golden vector for the canonical test candidate is:

```text
9484C1C638CF82EB5D499BB5DDBEF86C2F7610B202C6BA323597D0CC3E69470F
```

`dlc.candidate-set.v1` sorts full definitions by ordinal `CandidateId`, rejects every
duplicate ID, prefixes each definition with its byte length, and hashes the complete
set. Input enumeration order therefore cannot change the digest.

### Scientific uncertainty reuse

The paired scientific bootstrap is the sole producer of aligned component-P95 draws.
`BootstrapAmortizedP95CostReplicates` and
`BootstrapAmortizedP95Improvement` use the same paired-block preparation, random
generator, seed normalization, and draw order. The integration adapter passes those
realized draws to the envelope; the envelope does not resample raw measurements.

Frozen uncertainty-method identifiers are:

- `dlc.paired-block-bootstrap-log-ratio.v1`;
- `dlc.process-hierarchical-bootstrap-log-ratio.v1`.

Only the first is wired to the single-Player adapter in this slice. Device hierarchy
is not implemented or implied.

Envelope quantiles are calculated in sorted
`log(candidate_amortized_p95 / baseline_amortized_p95)` space and transformed to
improvement percentages afterward. This exactly matches the scientific interval and
avoids a second, subtly different transformed-percentile estimator.

The decision rule is:

```text
point improvement >= minimum effect
AND confidence lower bound > 0
```

The lower confidence bound is not required to exceed the full minimum effect. Exact
threshold comparisons in adaptive screening use a scale-aware `1e-12` floating-point
tolerance; this prevents an analytically equal bound from being pruned after a
log/exp round trip.

### Multiplicity, Pareto, and regret

Multiplicity policy v1 is candidate selection on calibration evidence followed by
an independent holdout confirmation of only the frozen winner. No familywise or
false-discovery correction is claimed. The policy is recorded as such; a future
correction requires a versioned protocol change.

Pareto policy v1 uses strict dominance over point resident P95, total boundary P95,
and resident bytes. Equal points remain on the frontier. A candidate without valid
aligned quick uncertainty bypasses point-Pareto pruning and proceeds conservatively.
Uncertainty-aware component frontiers are deferred.

Adaptive regret is audit-only and never reads holdout data:

```text
100 * (adaptive winner cost - exhaustive best cost) / exhaustive best cost
```

Exact cost ties use canonical candidate order. Regret cannot authorize a weaker final
calibration or holdout gate.

### Artifact topology

An advantage envelope remains a separate immutable schema-1 artifact.
`ScenarioCalibrationProfile` schema 3 may contain an optional
`AdvantageEnvelopeArtifactReference` with:

- reference schema, artifact schema, artifact ID, and artifact SHA-256;
- decision-engine version;
- `ScenarioId + ContractVersion`;
- canonical candidate-set SHA-256; and
- scientific-envelope measurement-schema SHA-256.

The reference is accepted only for a locked envelope with
`HoldoutCanRerank = false`. It never embeds cells into the suite, replaces
`FinalDecision`, or allows a renderer to select again. Schema 2 cannot carry this
reference. The fixed-result renderer independently validates the same candidate hash
encoding and copies a valid reference into its manifest.

The measurement-schema digest is SHA-256 over this exact UTF-8 text, including
the final LF and no BOM:

```text
dlc.scientific-envelope-measurement.v1
layout-benchmark-sample-schema=1
candidate-definition-schema=1
components=resident-p95-ms-per-tick,ingress-p95-ms,export-p95-ms
replicate-alignment=paired-measurement-block
estimand=log(candidate-amortized-p95/baseline-amortized-p95)
```

### Failure, feasibility, and evidence ownership

The adapter requires callers to supply canonical evidence hashes and partition IDs.
It copies measured completion, parity, allocations, and resident bytes, but never
infers contract feasibility or memory-budget feasibility from latency. Those two
declarations remain host-owned and explicit.

Counter data is adjunct evidence. Disabled, unavailable, or failed providers cannot
change a calibration decision. Synthetic fixtures never count as Player, device,
ISA, counter, workload, or cross-device evidence. Imported observations count only
after locally retained identity, stream, and suite artifacts are rehashed and the
fixed decision validates.

### AOT, allocation, and release boundary

The bridge uses direct constructors and calls, deterministic arrays, and no runtime
reflection. Candidate hashing and bootstrap materialization are calibration/control
plane operations, not resident hot-path work. Existing zero-managed-allocation gates
remain mandatory for measured resident and boundary samples.

The package version remains `0.3.0-preview.1`. These additive APIs are an unreleased
vNext foundation. No tag, GitHub Release, or merge to `main` is authorized by this
ADR. A future release still requires all claimed Mono and IL2CPP consumer gates,
replay, documentation, provenance, and actual evidence appropriate to its claims.

## Consequences

- Scientific and envelope intervals now have one executable estimator definition.
- Candidate aliases, changed semantics under a reused ID, and mismatched external
  envelopes fail closed.
- Historical evidence and the published v0.3 claims remain distinguishable from
  unreleased foundation code.
- Multi-device statistics, real counter providers, completed candidate matrices, and
  release promotion remain explicit future work.

Copyright (c) 2026 Edwin Liu. All Rights Reserved.
