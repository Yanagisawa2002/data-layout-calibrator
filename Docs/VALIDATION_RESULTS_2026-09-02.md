# Validation results — 2026-09-02

## Decision

**The planned reusable pipeline is implemented and the full gate passes.**

Data Layout Calibrator now has a workload-agnostic core, two independent workload plugins, full boundary accounting, conservative statistical selection, AOT-safe generated registration, and a fixed-result presentation tool. It remains a concrete candidate calibrator, not an arbitrary-code layout rewriter.

## Environment and protocol

- Windows 11 x64; AMD Ryzen 9 9950X; 7 Unity Job workers.
- Unity 6000.5.3f1; Burst 1.8.29; resolved Collections 6.5.0; Mathematics 1.4.0.
- Windows Mono and IL2CPP scripting backends, non-Development Release Players, Burst AOT.
- D3D11-only build, Graphics Jobs off; players executed headless.
- Integration gate: 65,536 calibration records, 65,521 untouched holdout records, 7 resident samples, 5 boundary samples, 600 lifetime ticks, and 500 bootstrap iterations.

This short run validates the complete mechanism and backend compatibility. It is device-scoped evidence, not a universal ranking or a final product performance claim.

## Roadmap completion

| Item | Result |
|---|---|
| Data Layout Calibrator naming | Complete |
| Particle workload isolated in Samples | Complete; core contains no Particle types |
| Scenario/Candidate/Parity/BoundaryCost protocols | Complete; both workloads use the same engine |
| TransformExport negative control | Complete; retains tuned AoS |
| Full ingress/export/lifetime amortization | Complete; separately sampled and serialized |
| Bootstrap confidence and AoS tie fallback | Complete; deterministic tests pass |
| Mono Burst AOT and IL2CPP before Source Generator | Complete; both passed before generator work began |
| AOT-safe Source Generator | Complete; explicit attributes and direct constructor calls |
| Fixed-result heatmap/GIF | Complete; renderer cannot reselect |

## Backend evidence after Source Generator

| Gate | Mono Release | IL2CPP Release |
|---|---:|---:|
| Player build | Pass | Pass |
| Burst AOT library | Pass | Pass |
| Required resident/boundary entrypoints | Pass | Pass |
| Workload scenarios discovered by generated registry | 2 | 2 |
| Calibration candidates completed/parity valid | 20/20 | 20/20 |
| Managed allocation | 0 B resident and boundary | 0 B resident and boundary |
| Suite SHA-256 | `C20C3D32444B979F34D9F736CAE2C0312658D57605B4388F2A401D235A139313` | `85FAC20CDF81EBA674A3A736340CFCBEEB88EEF99CD1F5ECC776EE0215E53D78` |
| Burst DLL SHA-256 | `929DF55D8235D17ACA84CBD19C27D1E84DEAF1232C10A8F49A48EAB76485F4B1` | `6C7DEF5529937750E0FEF754ED6A56C780CCAC72A050E772EA3BFBB9AE9B6B26` |

The IL2CPP prerequisite gate was also run before the Source Generator was implemented. That pre-generator player completed both workloads and produced a valid suite, satisfying the ordering constraint rather than retroactively assuming it.

## Frozen IL2CPP decisions

| Scenario | Frozen final decision | Holdout evidence |
|---|---|---:|
| Particle Integrate | Select `AoSoA8-b128` against tuned `AoS-b256` | 34.15% lower amortized P95; 95% bootstrap CI [33.21%, 35.25%] |
| Transform Export (negative control) | Retain `AoS-b256` | Best valid result did not clear the product threshold |

All 12 Particle and 8 TransformExport calibration candidates completed, passed typed parity, and measured 0 managed bytes in resident and boundary paths. The checked-in immutable source is [`evidence/il2cpp-release-calibration-suite.json`](evidence/il2cpp-release-calibration-suite.json).

## Source Generator evidence

The generator is compiled for .NET Standard 2.0 and distributed as a Unity `RoslynAnalyzer`. The benchmark assembly registers both factories with assembly attributes; generated code constructs them directly and deterministically.

- 4/4 standalone Roslyn tests pass: deterministic output, duplicate diagnostic, invalid-factory diagnostic, and no-registration behavior.
- 29/29 Unity EditMode tests pass after analyzer import.
- Both generated workloads execute under Mono and IL2CPP AOT Players.
- The generated path contains no reflection or `Activator`.

## Presentation evidence

The standalone renderer consumes only the immutable suite and emits:

- [`assets/data-layout-calibrator-heatmap.png`](assets/data-layout-calibrator-heatmap.png)
- [`assets/data-layout-calibrator-comparison.gif`](assets/data-layout-calibrator-comparison.gif)
- [`assets/data-layout-calibrator-render-manifest.json`](assets/data-layout-calibrator-render-manifest.json)

The manifest repeats the source hash and frozen candidate IDs. The 3/3 renderer tests include an adversarial case that changes a non-selected candidate to an unrealistically low latency; the selected marker remains the candidate stored in `FinalDecision`.

## Test summary

- Unity EditMode: 29 passed, 0 failed.
- Source Generator: 4 passed, 0 failed.
- Fixed-result renderer: 3 passed, 0 failed.
- Mono Release Player: exit 0 and complete result.
- IL2CPP Release Player: exit 0 and complete result.

## Preregistered full-size IL2CPP replications

After the backend integration gate, a protocol was committed before running
five full-size Player processes. The frozen configuration used 1,048,576
calibration records, 1,000,003 untouched holdout records, 40 resident samples,
20 boundary samples, 600 lifetime ticks, and 4,000 bootstrap iterations.

The preregistered `run-01` primary result selected `SoA-b128` against tuned
`AoS-b64` and reduced holdout amortized P95 by 70.70%, with a 95% bootstrap CI
of [65.32%, 79.37%]. Across all five retained launches:

- ParticleIntegrate selected a gated non-AoS result in 5/5 runs.
- Holdout P95 reduction ranged from 66.11% to 74.81% (median 68.71%).
- The worst per-run confidence lower bound was 40.07%.
- TransformExport retained tuned AoS in 5/5 runs.
- All retained results passed parity and recorded 0 managed allocation.

The selected Particle layout varied between SoA and AoSoA8, and `run-04`
retained a wider interval. Both facts remain visible rather than being removed
after measurement. The protocol, all suites, hashes, and a descriptive manifest
are under [`evidence/formal-il2cpp-2026-09-02`](evidence/formal-il2cpp-2026-09-02/README.md).
No hierarchical cross-run confidence interval or cross-device claim is made.
