# Advantage envelope and adaptive elimination contract

Status: additive decision-engine foundation

Artifact schemas: `advantage-envelope` v1, `adaptive-elimination-plan` v1

Decision engine: `1.0.0`

This contract implements roadmap items 3 and 4 without changing historical
schema-2 calibration suites. It defines decision artifacts and testable
selection boundaries; it does not add Unity Player, device, ISA,
hardware-counter, or cross-device evidence. All checked-in examples used by the
new tests are labeled `synthetic-test-fixture`.

The repository remains All Rights Reserved under [`LICENSE`](../LICENSE), and
the existing provenance boundary in [`PROVENANCE.md`](../PROVENANCE.md) is
unchanged.

## Identity and compatibility

`CandidateId` is the only cross-artifact candidate join key. It is always
serialized explicitly and is never inferred from `DisplayName`. Envelope
candidate descriptors also carry explicit `LayoutPolicyId`, `KernelPolicyId`,
`BatchPolicyId`, `ExecutionPolicyId`, and logical batch size so a renderer or
consumer never has to reverse-engineer factor identity from a label.

Every request and frozen artifact binds:

- `ScenarioId` plus `ContractVersion`;
- `CandidateSetHash` and `MeasurementSchemaHash`;
- `EnvironmentFingerprint`;
- calibration and holdout settings hashes;
- source artifact IDs and hashes;
- an explicit evidence scope and uncertainty-method name.

Compatibility fields are copied into output. The decision engine does not
invent environment or evidence metadata.

## Decision-ready uncertainty

The engine accepts point P95 components and aligned `BootstrapCostReplicate`
arrays from the scientific statistics layer. A replicate contains an explicit
`ReplicateId` plus resident, ingress, and export P95 values. Baseline and
candidate arrays must have identical unique replicate IDs.

This boundary deliberately does not prescribe how replicates are produced.
Paired, process-hierarchical, and future device-hierarchical methods can feed
the engine without putting resampling design into the renderer or envelope
selector. Raw samples remain in the source artifact referenced by
`EvidenceHash`.

For lifetime `L > 0`:

```text
cost(candidate, L) = resident_p95(candidate)
                   + (ingress_p95(candidate) + export_p95(candidate)) / L
```

An improvement interval is calculated across aligned replicate costs. The
current selection rule matches the existing schema-2 rule:

1. the point improvement must meet `MinimumImprovementPercent`;
2. the confidence lower bound must be greater than zero;
3. every feasibility, parity, allocation, memory, sample-count, provenance,
   and uncertainty gate must pass.

Production defaults retain the existing 40 resident samples, 20 boundary
samples, and 4,000 bootstrap replicates for both calibration and holdout.
Smaller counts appear only in explicitly labeled synthetic unit fixtures.

If the point effect clears the minimum but its interval includes no advantage,
the cell is a `StatisticalGreyZone` and tuned AoS is selected. Other
non-winning valid cells are `AoSFallback`.

`BestMeasuredCandidateId`, `CalibrationImprovementPercent`, and
`CalibrationConfidenceInterval` describe the lowest-cost eligible calibration
point even when a gate forces AoS fallback. The frozen winner ID remains a
separate field, and final credible cells report the frozen winner's independent
holdout interval.

## Break-even regimes

With candidate-minus-baseline deltas `dr` for resident P95 and `db` for total
boundary P95, candidate advantage is `dr + db/L < 0`. The engine represents all
positive-lifetime cases explicitly:

- `EqualCosts`: `dr = 0` and `db = 0`;
- `CandidateAlwaysAdvantaged`: no worse in either component and strictly
  better in at least one;
- `CandidateNeverAdvantaged`: no better in either component and strictly worse
  in at least one;
- `CandidateWinsAboveLifetime`: `dr < 0`, `db > 0`, crossing at `db / -dr`;
- `CandidateWinsBelowLifetime`: `dr > 0`, `db < 0`, crossing at `-db / dr`.

The exact crossing is a tie. Aligned replicates produce regime counts. A
finite crossing receives a percentile interval only from same-direction
crossings, while `SameRegimePercent` and `MixedRegimes` make regime uncertainty
visible rather than hiding non-crossing replicates.

## Two-phase immutable holdout

`AdvantageEnvelopeEngine.Calibrate` has no holdout parameter and returns an
`advantage-envelope-calibration` artifact with `HoldoutWasRead = false`. It
freezes at most one non-AoS candidate per cell.

`ConfirmHoldout` accepts only tuned AoS and that exact frozen `CandidateId` for
each provisional advantage cell. Descriptors must retain the same explicit
factor IDs, the holdout partition must differ from calibration, and
candidate/schema/environment hashes must match. Holdout may confirm the frozen
candidate or fall back to tuned AoS; it cannot nominate a replacement.
Configured holdout sample minima cannot be lower than calibration minima.

The final schema-v1 `advantage-envelope` sets `FinalDecisionLocked = true` and
`HoldoutCanRerank = false`, and retains both holdout candidate evidence hashes.
Piecewise winner regions enumerate the exact sampled
lifetime points they cover, so gaps are not silently interpolated. The summary
reports all valid cells through credible coverage, peak/median/floor confirmed
improvement, and the worst confirmed confidence lower bound.

## Adaptive elimination

The schema-v1 adaptive plan records one disposition for every candidate:

1. feasibility screen: completion, contract, memory, parity, zero managed
   allocation, valid point components, partition, and evidence hash;
2. quick calibration: eliminate only when the optimistic improvement bound is
   below the frozen minimum effect; missing or unalignable uncertainty is
   retained conservatively;
3. strict Pareto frontier: resident P95, total boundary P95, and resident bytes;
4. finalists: tuned AoS is always protected, even if mathematically dominated.

A candidate without valid aligned quick uncertainty bypasses both the
optimistic-bound elimination and point-Pareto pruning.

Equality in all Pareto dimensions is not dominance. Each eliminated candidate
retains stage, reason, sample counts, confidence bounds, and dominator ID where
applicable, together with its evidence partition and source-evidence hash. The
plan also copies its source artifact ID and hash.

The plan copies the required full calibration and holdout sample/bootstrap
counts and marks `FinalEvidenceRequirementsUnchanged = true`. Reported sample
counts are validated so holdout requirements cannot be lower than full
calibration requirements. Reported sample
units are deterministic planned component counts, not measured wall-clock
speedups. `AuditAgainstExhaustive` is an audit-only counterfactual that reports
exact winner equivalence or selection regret from supplied full calibration
scores; it never consumes holdout evidence.

## Fixed-result renderer

[`render_advantage_envelope.py`](../Tools/ResultRenderer/render_advantage_envelope.py)
validates schema, candidate membership, fallback semantics, winner-region
coverage, and summary consistency. It copies `Status`, `SelectedCandidateId`,
and confidence fields from frozen cells. Candidate cost values are not read for
selection. Tests make a candidate arbitrarily faster and verify that the
rendered selection does not change.

```powershell
python Tools/ResultRenderer/render_advantage_envelope.py `
  path/to/advantage-envelope.json path/to/output-directory

python -m unittest discover Tools/ResultRenderer/tests -v
```

The output manifest retains the input SHA-256, compatibility hashes, evidence
scope, frozen decision policy, fixed summary, and exact cell decision snapshot
used by the PNG and GIF.

## Integration decisions still owned by the shared protocol

- Freeze the exact paired/hierarchical bootstrap producer and its serialized
  uncertainty-method identifier. The envelope already consumes aligned
  replicate IDs and does not need to change when that choice is made.
- Decide whether a future protocol raises the confidence gate from “lower bound
  above zero” to “lower bound above the full minimum effect.” Schema v1 records
  both point threshold and interval, so the distinction remains auditable.
- Freeze any multiplicity/selection-regret control applied when many candidates
  share a cell. The engine records per-candidate intervals and a single frozen
  winner but does not invent an unapproved correction.
- Decide how the additive envelope reference is attached to the eventual
  top-level suite schema. Historical schema-2 evidence must not be rewritten.
- Freeze whether strict Pareto decisions will use point P95 components or
  uncertainty-aware component bounds after the scientific-core statistics API
  lands.
