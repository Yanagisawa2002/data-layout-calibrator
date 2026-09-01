# ADR 0003: AOT-safe registration and fixed-result presentation

Status: Accepted
Date: 2026-09-02

## Context

Hand-maintained factory arrays are easy to forget, while runtime reflection and `Activator` introduce avoidable AOT/linker risk. At the other extreme, a generator that invents layouts or rewrites arbitrary workloads would overpromise semantics it cannot prove.

Presentation has a parallel risk: a heatmap can silently choose the fastest-looking cell even when the calibrated decision fell back to AoS because of parity, allocation, threshold, bootstrap, or holdout gates.

## Decision

The Source Generator owns factory registration only.

- A host assembly explicitly applies `RegisterCalibrationScenarioFactoryAttribute` once per factory.
- The generator requires a concrete, non-generic, accessible class implementing `ICalibrationScenarioFactory` with an accessible parameterless constructor.
- `DLCGEN001` rejects invalid factories and `DLCGEN002` rejects duplicates.
- Valid factories are sorted by fully qualified type name and emitted as direct `new` expressions in `GeneratedCalibrationScenarioRegistry`.
- Candidate data types, storage, Jobs, parity, and boundary operations remain handwritten plugin code with literal Burst schedule sites.

The renderer owns presentation only.

- Its only data input is a completed schema-2 suite result.
- Candidate measurements may control cell labels, bar lengths, and color intensity.
- Baseline, selected, and best markers, improvement, and confidence intervals are copied from `FinalDecision`.
- A manifest records the input SHA-256 and copied decision fields.
- A regression test makes another candidate arbitrarily faster and verifies that the displayed selected ID does not change.

## Consequences

Registration is reusable, deterministic, linker-independent, and compatible with Mono and IL2CPP AOT. The generator does not remove the need to author concrete workload candidates, which preserves semantic honesty and Burst discoverability.

Visuals are reproducible from a fixed evidence file and cannot become a second selector. They can still derive visual scales and format units, but cannot manufacture a different recommendation.

## Verification

- Source Generator: 4 Roslyn tests.
- Unity integration: 29 EditMode tests.
- Runtime: both Samples completed in Mono and IL2CPP Release Players with Burst AOT.
- Renderer: 3 tests plus visual inspection of the PNG and the GIF first/final frames.
