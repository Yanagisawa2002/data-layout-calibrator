# ADR 0002: Plugin calibration and immutable evidence boundary

Status: Accepted
Date: 2026-09-01

## Decision

Data Layout Calibrator is a workload-plugin pipeline, not a Particle tool and not an automatic code rewriter. The core owns measurement and selection. A plugin owns concrete data types, storage, Burst Jobs, ingress/export, and typed parity.

The primary metric includes resident execution plus full ingress/export amortized over an explicit lifetime. Selection requires a product threshold and a bootstrap confidence interval; statistical ties fall back to tuned AoS.

## Assembly boundary

- Core: Scenario/Candidate/Parity/BoundaryCost contracts, engine, statistics, profiles.
- Samples: concrete ParticleIntegrate and TransformExport Jobs and records.
- Benchmark: environment capture, AOT build guards, and fixed-result serialization.

No workload type may enter the core assembly. Runtime scheduling remains concrete so Burst AOT discovery never depends on reflection or open generic Jobs.

## Evidence boundary

The suite writer is the only component allowed to populate `FinalDecision`. Presentation tools consume a completed schema-2 result and are forbidden from invoking selection logic. This prevents a heatmap or GIF from silently choosing a more visually impressive result.

## Generator gate

A Source Generator begins only after both included workload contracts pass:

1. Mono Release Player with Burst AOT manifest verification;
2. Windows IL2CPP Release Player;
3. full parity, zero measured managed allocation, and valid fixed results.

Missing platform tooling is a failed prerequisite, not permission to weaken or skip the gate.
