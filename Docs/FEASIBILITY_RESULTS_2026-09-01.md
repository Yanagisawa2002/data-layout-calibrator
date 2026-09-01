# Validation status — 2026-09-01

> Historical gate snapshot. The missing IL2CPP prerequisite was resolved and the roadmap completed on 2026-09-02; see [final validation results](VALIDATION_RESULTS_2026-09-02.md).

## Decision

**Reusable calibration pipeline: engineering Go. Source Generator and presentation milestone: correctly gated.**

The v2 pipeline now proves that the core is workload-agnostic, measures full boundaries, can accept a real winner, and can return AoS for a negative control. It does not yet satisfy the required Windows IL2CPP gate because that Unity editor module is absent.

## Environment

- Windows 11, AMD Ryzen 9 9950X, 7 Unity Job workers.
- Unity 6000.5.3f1.
- Burst 1.8.29; resolved Collections 6.5.0; Mathematics 1.4.0.
- Windows x64 Mono scripting backend, non-Development Release Player, Burst AOT.
- D3D11-only build, Graphics Jobs off; benchmark executed headless.

## Verified implementation

| Roadmap item | Status | Evidence |
|---|---|---|
| Data Layout Calibrator naming | Complete | Package ID, assemblies, UI, executable, output schema, docs |
| Particle moved to Samples | Complete | Separate Sample asmdef; reflection test confirms no Particle type in core DLL |
| Scenario/Candidate/Parity/BoundaryCost protocols | Complete | Public core interfaces; both Samples implement the same engine contract |
| TransformExport negative control | Complete | Eight candidates, field-level parity, AoS fallback |
| Full ingress/export/lifetime amortization | Complete | Separate raw samples and composite schema-2 metric |
| Bootstrap CI/statistical tie | Complete | Deterministic bootstrap tests; `StatisticalTie` selects AoS |
| Mono Burst AOT + IL2CPP before generator | Partial | Mono passes; IL2CPP module missing; generator not started |
| Fixed-result heatmap/GIF | Not started by design | Waiting for prior gate; result file already freezes `FinalDecision` |

## Mono Release Player evidence

The Player build succeeded and emitted:

- `DataLayoutCalibrator.exe` in the Mono evidence build;
- a non-empty `lib_burst_generated.dll`;
- all ten required resident and boundary Job entrypoints for ParticleIntegrate and TransformExport.

A 65,536-record integration run used 7 resident samples, 5 boundary samples, 600 lifetime ticks, and 500 bootstrap iterations. This short run validates behavior; it is not the final performance claim.

| Scenario | Calibration/holdout result | Parity | Managed allocation |
|---|---|---:|---:|
| Particle Integrate | AoSoA8-b256 accepted against AoS-b64; holdout amortized-P95 gain 49.6%, 95% CI [37.8%, 53.6%] | all 12 pass | 0 B resident, 0 B boundary |
| Transform Export (negative control) | tuned AoS-b64 is also best measured; `Inconclusive`, select AoS-b64 | all 8 pass | 0 B resident, 0 B boundary |

This is the important negative-control behavior: the calibrator does not force a non-AoS recommendation when the declared product threshold is not met.

The final EditMode suite passes 29/29 tests. The compiled core assembly references only `netstandard`, exposes `CandidateDescriptor` for arbitrary plugin-defined layout IDs, and contains zero Particle types.

## IL2CPP gate evidence

The formal Windows IL2CPP build was attempted after the two-workload Mono pass. Unity exited with code 1 and reported:

```text
Error building Player: Currently selected scripting backend (IL2CPP) is not installed.
```

All locally installed editors (`6000.4.10f1`, `6000.5.2f1`, `6000.5.3f1`) have Windows support directories but no Windows IL2CPP Player variation. This is an environment prerequisite failure, not a code or parity failure, and it must not be replaced by Mono evidence.

## Next authorized sequence

1. Install Windows Build Support (IL2CPP) for Unity 6000.5.3f1.
2. Re-run `DataLayoutCalibratorBuild.BuildWindowsIl2CppFormal`.
3. Run both workloads from that IL2CPP Release Player and compare the fixed suite schema with Mono.
4. Only after both pass, implement the Source Generator.
5. Only after generator parity/AOT verification, build a heatmap and GIF renderer that reads the fixed suite result without reselecting candidates.
