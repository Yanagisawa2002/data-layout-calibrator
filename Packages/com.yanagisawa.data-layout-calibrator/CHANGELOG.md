# Changelog

## 0.3.0-preview.1 — 2026-09-02

- Added an AOT-safe Scenario Registry Source Generator driven by explicit assembly attributes.
- Added compile-time diagnostics for invalid and duplicate factory registrations, plus deterministic factory ordering.
- Verified the generated registry with four Roslyn tests, 29 Unity EditMode tests, and both included workloads in Windows Mono and IL2CPP Release Players with Burst AOT.
- Added an immutable IL2CPP evidence snapshot and a standalone fixed-result heatmap/GIF renderer with provenance manifest.
- Added renderer regression tests proving that changed measurements cannot replace the candidate stored in `FinalDecision`.
- Added a preregistered five-launch, full-size IL2CPP evidence set using 40/20 samples and 4,000 bootstrap iterations; all raw suites are retained.

## 0.2.0-preview.1 — 2026-09-01

- Renamed the product, package, assemblies, build output, and result schema to Data Layout Calibrator.
- Moved ParticleIntegrate into an independent Sample assembly; the core assembly contains no Particle types.
- Added public Scenario, Candidate, Parity, and BoundaryCost plugin contracts.
- Added the TransformExport negative-control Sample.
- Added allocation-free full ingress/export measurement and explicit lifetime amortization.
- Added deterministic non-parametric bootstrap confidence intervals and AoS fallback for statistical ties.
- Added a schema-2 fixed suite result consumed by future presentation tooling.
- Verified both workloads in a Windows Mono Release Player with Burst AOT; Windows IL2CPP remains gated on the missing editor module.

## 0.1.0-preview.1 — 2026-09-01

- Added concrete AoS, SoA, and AoSoA8 particle layout domains.
- Added three non-generic Burst `IJobParallelFor` entrypoints.
- Added deterministic datasets, field-level parity, and quantized state hashes.
- Added P50/P95/P99/MAD statistics, AoS-best selection, and untouched holdout confirmation.
- Added versioned JsonUtility profile models.
