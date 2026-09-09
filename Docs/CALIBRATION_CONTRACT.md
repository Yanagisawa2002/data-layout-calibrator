# Data Layout Calibrator contract

Published status: protocol/result schema 2 (`v0.3.0-preview.1`)

Unreleased integration status: additive schema 3 foundation

Historical schema-2 artifacts remain immutable. Schema 2 migrates only in
memory; native schema 3 is validation-only and rejects missing or unknown
scientific metadata rather than repairing it.

## Plugin boundary

The core engine may depend only on:

| Protocol | Responsibility |
|---|---|
| `ICalibrationScenarioFactory` | Stable workload identity and deterministic scenario creation |
| `ICalibrationScenario` | Canonical input, candidates, reference index, dataset hash, parity validator |
| `ICalibrationCandidate` | One concrete layout/batch and literal AOT-visible execution sites |
| `IBoundaryCost` | Full ingress and full export using preallocated storage |
| `IParityValidator` | Typed field-level comparison of canonical exports |

The core assembly must expose no workload record, storage, or Job type. A reflection test fails if a core type name contains `Particle`; Samples compile into their own asmdefs.

Factory discovery is explicit and compile-time only. A host assembly applies `RegisterCalibrationScenarioFactoryAttribute`; the packaged Source Generator validates the type and emits direct constructor calls in deterministic fully-qualified-name order. Runtime reflection, `Activator`, and open-generic Job discovery are outside the contract.

## Included scenario contracts

### particle-integrate-v2

The canonical 48-byte record contains hot `Position`, `Velocity`, and `Lifetime`, plus cold `Rotation` and `Category`. Every candidate applies identical acceleration, damping, integration, lifetime, and respawn rules.

- Layouts: AoS, SoA, AoSoA8.
- Logical batches: 32, 64, 128, 256.
- Ingress: full canonical record array into persistent candidate storage.
- Resident operation: integrate all hot fields for one logical tick.
- Export: full candidate state back to canonical records, including cold fields.
- Parity: absolute tolerance `1e-5` for hot floating fields; exact cold fields; matching quantized state hash.

AoSoA8 changes both storage and kernel shape by processing eight lanes per Job iteration. Results are candidate-pipeline comparisons, not proof that layout alone caused the difference.

### transform-export-v1

The canonical record contains position, rotation, scale, entity identity, and flags. Each tick writes a full `float4x4` LocalToWorld matrix plus identity and flags.

- Layouts: AoS, SoA.
- Logical batches: 32, 64, 128, 256.
- Ingress: full canonical transform records into persistent candidate storage.
- Resident operation: full transform export into candidate-owned output.
- Export: full output copied to the canonical consumer buffer.
- Parity: matrix tolerance `1e-5`; exact identity/flags; matching quantized output hash.

This output-heavy workload is the negative control: the framework must be able to report “use AoS,” rather than manufacture a winner.

## Measurement protocol

1. A 4,099-element, 256-tick Player preflight executes every candidate and requires parity.
2. The AoS reference probe chooses one common ticks-per-block duration.
3. Ingress, resident, and export have separate warmup and repeated measurements.
4. Candidate order is deterministically shuffled each round.
5. All candidates use the same element count, tick count, sample count, and declared lifetime.
6. Any measured managed allocation in resident or boundary samples makes a candidate ineligible. The current integration additionally requires a positive-control-validated capability covering the declared allocation scope and complete observation windows; unavailable counters and unobserved worker/native scopes cannot pass as zero.
7. Hashing, parity scans, dataset creation, serialization, and visualization remain outside timing.

The primary metric is a component-quantile selection score:

```text
component_p95_score_ms_per_tick = p95(block_elapsed_ms / ticks)
                                 + (ingress_p95_ms + export_p95_ms) / lifetime_ticks
```

Existing artifacts retain their historical `amortized_p95_ms_per_tick` field and
values. This sum is neither an observed per-tick/whole-lifecycle P95 nor a guaranteed
bound on those percentiles. Resident samples plus the fixed boundary score are
also retained for inspection. Construction/disposal costs are excluded from this
engine metric. The [lifetime adoption guide](LAYOUT_ADOPTION.md) explains how to
retain complete ownership costs and the application's actual export cadence in
a separate cost model or complete-lifecycle observation.

## Selection protocol

- Baseline: lowest component-quantile selection score among valid AoS batches.
- Point gate: non-AoS improvement must be at least 10%.
- Published schema-2 significance gate: independent non-parametric bootstrap of
  the composite P95 metric; 4,000 iterations and 95% confidence by default.
- Native schema-3 significance gate: paired measurement-block bootstrap of
  `log(candidate / baseline)` with explicit block/order metadata; 4,000
  iterations and 95% confidence by default. Process hierarchy is represented
  separately and same-device processes are never called multiple devices.
- Schema-3 outcome: an interval wholly below zero is `Regression`; an interval
  spanning zero is `StatisticalTie`; a positive interval whose point estimate
  misses the practical threshold is `Inconclusive`. Every case selects AoS.
- Holdout: an optimized candidate must repeat point and significance gates on a new seed and non-eight-divisible count.
- Any parity, allocation, count, or raw-sample failure falls back to AoS or invalidates the result.

## Evidence and presentation protocol

`calibration-suite.json` is the immutable input to any heatmap, dashboard, or GIF. Presentation code may choose axes, formatting, and annotations, but it may not call the selector, recompute `FinalDecision`, substitute a different candidate, or combine incompatible runs.

The published schema-2 gate was satisfied on 2026-09-02 for both included
workloads in Mono and IL2CPP Release Players with Burst AOT. That evidence does
not validate the unreleased schema-3 implementation. The fixed-result renderer
records the input SHA-256 and copied decision fields in a manifest; a regression
test changes candidate measurements and verifies that the displayed selection
remains the one stored in `FinalDecision`.

## Unreleased schema-3 integration

Schema 3 adds explicit layout/kernel/batch/execution policies, paired sample
metadata, estimator provenance, stable `Inconclusive`, `StatisticalTie`, and
`Regression` states, and optional external advantage-envelope attachment.
`CandidateDefinitionProtocol` binds a canonical `CandidateId` to the full
semantic definition. `ScientificAdvantageEnvelopeAdapter` passes the exact
paired component-P95 bootstrap draws into the envelope; it requires explicit
contract and memory-feasibility declarations and never infers them from timing.

An envelope reference is valid only when its artifact SHA, schema, engine,
scenario contract, candidate set, and measurement schema match. It remains
external and cannot replace `FinalDecision`. The exact shared rules are frozen
in [`ADR 0006`](adr/0006-vnext-integration-protocol.md).

## Validated managed allocation measurement (2026-09-07)

`CalibrationRunSettings.AllocationCounter` injects a synchronous `IManagedAllocationCounter`.
Use one counter for a sequential calibration run; implementations own their window state
and must not be shared by concurrent runs. The engine calls `Validate` around measurement
phases and brackets actual actions with `Begin`/`End`; unknown or negative observations
must throw. `End` returns exact managed bytes, or zero inferred from a validated zero-event
window. Do not substitute heap-live-size differences or unsupported API zeros.

The portable default validates a real positive allocation and an empty control. Unity
runtimes that do not implement the .NET thread-allocation API must supply a supported
counter. The benchmark's reference adapter uses the separately registered native
Mono/IL2CPP profiler fallback described in [the allocation recorder](../Tools/AllocationRecorder/README.md).
It measures main-thread object-size bytes; native memory and worker-thread allocation
are outside that scope. The optional OS CPU-cycle provider is a different diagnostic
and may be absent without inventing CPU counter values.

New scenario profiles and raw envelope phases retain `ManagedAllocationMeasurement`.
Historical raw zeros remain immutable but unverified as explained in
[the historical correction](evidence/HISTORICAL_ALLOCATION_MEASUREMENT_LIMIT.md).
