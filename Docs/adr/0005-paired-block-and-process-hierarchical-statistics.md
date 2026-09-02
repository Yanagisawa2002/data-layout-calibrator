# ADR 0005: Paired blocked and process-hierarchical statistics

Status: Accepted for the v0.4 scientific-core slice
Date: 2026-09-02
Roadmap scope: Items 1 and 2 only

## Context

Schema 2 measures every candidate once per shuffled round, so samples already share a round-level timing context, but its bootstrap resamples baseline and candidate arrays independently. That discards the pairing and can mistake common drift for candidate uncertainty. It also has no explicit process/device evidence scope, and holdout fallback does not distinguish a supported regression from an unresolved tie.

## Decision

Every ingress, resident, and export round is a complete measurement block. The default order is a seeded balanced Latin square. Each block contains every candidate exactly once; each complete candidate-count cycle balances order position. With two candidates, adjacent blocks are AB then BA and therefore form an ABBA sequence across their boundary. Seeded randomized complete blocks remain an explicit supported alternative.

Each result records block IDs and within-block order positions separately for resident, ingress, and export samples. Baseline and candidate samples are joined by block ID, never by `DisplayName` or measurement magnitude.

The single-Player confidence interval uses a paired block bootstrap of:

```text
log(candidate amortized P95 / baseline amortized P95)
```

Resident, ingress, and export blocks are paired within their respective measurement series. Percentage improvement is a presentation transform of the log-ratio interval. The seed, estimand, resampling unit, log bounds, and percentage bounds are persisted in bootstrap sub-schema 1.

For multiple independent Player launches, `BootstrapProcessHierarchy` resamples Player processes and then paired blocks inside each sampled process. It requires at least two unique process IDs and one common, explicit device ID. It rejects multiple device IDs rather than representing same-machine process replication as cross-device evidence. A device-level bootstrap is not implemented in this slice.

Paired and process-hierarchical comparisons fail closed unless `ScenarioId` and `ContractVersion`, phase, element count, ticks per sample, lifetime, and canonical candidate definitions agree. The process layer additionally requires those settings to agree across launches; the future fingerprint layer will extend this to binary, environment, and full calibration-setting hashes.

Calibration freezes the tuned AoS baseline and one selected candidate. `HoldoutIsolation.Freeze` emits only those two canonical candidate identities. Holdout uses fixed calibration ticks and warmup settings and never calls calibration selection again.

Decision states are stable and distinct:

- `Optimized`: the frozen candidate clears the practical-effect threshold and has a paired interval above zero.
- `StatisticalTie`: the paired interval includes zero; tuned AoS is selected.
- `Inconclusive`: evidence is invalid/missing or a distinguishable gain is below the practical threshold; tuned AoS is selected.
- `Regression`: the holdout interval is entirely below zero improvement; tuned AoS is selected.
- `Invalid`: no valid tuned AoS or comparison contract exists.

Decisions record measured selection regret and the multiplicity strategy. The current strategy selects a calibration winner and requires confirmation on an untouched holdout dataset; it does not present all calibration winner comparisons as independent confirmatory tests.

## Schema-2 migration

This branch proposes top-level schema 3 plus sampling/bootstrap sub-schema 1. Migration is additive and in memory:

- Schema-2 sample-array indices are reconstructed as block IDs because the schema-2 engine contract stored one value per candidate per shuffled round at that index.
- Historical within-block order was not serialized, so migrated order positions are `-1` (unknown).
- Historical confidence intervals retain their stored percentage point and bounds and are marked `LegacyIndependentPercent`. They explicitly set `HasLogRatioEstimate` to false and keep the log-ratio fields at zero, so migration cannot make an independent percent interval appear to contain a realized log-ratio estimate. Migration does not relabel them paired, invent a bootstrap seed, or recompute checked-in evidence.
- Historical evidence files remain byte-for-byte unchanged.

Only schema-2 profiles may be reconstructed. Migration marks their sampling design with `ReconstructedFromSchema2`; this preserves legitimately absent historical phase/count fields as unknown instead of inventing values. A native schema-3 profile is validated without mutation: it must already declare sample sub-schema 1, policy sub-schema 1, a non-empty calibration result set with matching calibration phase and element count, complete block/order metadata for every recorded sample series, consistent scenario identity, and an estimator marker matching its interval fields. Any holdout results must match the holdout phase/count and the exact baseline and selected-winner descriptors frozen by `CalibrationDecision`; a fallback `FinalDecision` may still select the baseline. Unknown nested versions and partially present metadata fail closed. Sampling designs must explicitly declare calibration tuning, no holdout retuning, and their uncertainty interpretation.

## Consequences

Common per-block drift is retained in the comparison rather than inflated into independent-candidate noise. Process-level intervals have an explicit evidence scope. Corrupt or mismatched block identities fail closed to an inconclusive AoS fallback in selection code.

Synthetic unit fixtures exercise deterministic resampling but are not Player, device, ISA, hardware-counter, or cross-device evidence. Formal claims still require fresh non-Development Release Player runs with captured backend and environment metadata.

Reports label a realized paired single-Player interval, a process-to-block hierarchical interval, or descriptive-only measurements with no inferential interval as distinct uncertainty presentations. Merely configuring bootstrap iterations does not imply that a decision produced a confidence interval.
