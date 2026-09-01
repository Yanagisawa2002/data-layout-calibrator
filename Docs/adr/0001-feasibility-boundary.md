# ADR 0001: Feasibility before source generation

Status: Accepted  
Date: 2026-09-01

## Decision

The first milestone uses handwritten, concrete Burst jobs for AoS, SoA, and AoSoA8. Source generation begins only if the concrete variants first prove a meaningful resident-pipeline difference in a release AOT Player and then retain value after necessary conversion/export costs are added.

## Why

Open generic jobs and generic scheduling have AOT limitations. A source generator also risks hiding abstraction overhead or changing the semantic contract. Concrete variants establish the performance ceiling and the parity contract first.

## Frozen feasibility gates

1. All layouts start from identical deterministic records.
2. Every measured variant produces the same quantized state hash and passes field-level tolerance checks.
3. Timing excludes initialization, conversion, visualization, hashing, and evidence serialization.
4. Selection minimizes P95 schedule-to-complete latency and compares against the best independently tuned AoS candidate for one frozen workload/count bucket.
5. The selected candidate must independently clear the same improvement threshold against the matching AoS candidate on an untouched seed/count holdout. Raw latency is never compared across different element counts.
6. The hot measurement path allocates no managed memory after warmup.
7. A result below 10% improvement over the best AoS batch candidate is reported as a negative feasibility result, not an optimization win.

## Interpretation boundary

AoSoA8 explicitly processes two `float4` lane groups per `Execute`, while AoS and SoA process one logical record per `Execute`. The experiment selects a concrete layout-plus-kernel candidate. It does not isolate layout as the sole causal variable.

## Non-goals

- Rewriting arbitrary C# or MonoBehaviour code.
- Replacing Burst, LLVM, the Job System, Entities, or Unity Collections.
- Automatically asserting unsafe aliasing contracts.
- Converting layouts every frame.
- Claiming that one profile transfers to another CPU, Burst version, workload, or dataset size.
