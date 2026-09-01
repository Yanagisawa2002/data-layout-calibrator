# Feasibility results — 2026-09-01

## Decision

**Engineering Go; product/generator gate remains closed.**

The concrete approach is real: all three jobs are present in the standalone Burst AOT manifest, output parity holds, the hot path allocates zero managed bytes, and AoSoA8 wins the frozen resident workload in three independent Player processes. This is enough to continue building the calibrator protocol. It is not enough to publish a general Unity performance claim or start a source generator.

## Environment

- Windows 11, AMD Ryzen 9 9950X, 7 Unity job workers.
- Unity 6000.5.3f1.
- Burst 1.8.29; Collections 6.5.0; Mathematics 1.4.0.
- Windows x64, Mono scripting backend, non-Development Release Player, Burst AOT.
- D3D11-only build configuration, Graphics Jobs off; benchmark executed headless.
- Calibration count 1,048,576; holdout count 1,000,003; 30 interleaved samples per candidate.

Windows IL2CPP support is not installed for any local Unity editor, so the build guard rejected the IL2CPP attempt. This is an explicit remaining gate, not silently substituted evidence.

## Repeated-process results

| Process | Calibration selection | Tuned AoS baseline | Calibration P95 gain | Holdout P95: AoS → selected | Holdout gain |
|---|---|---|---:|---:|---:|
| 1 | AoSoA8, batch 128 | AoS, batch 256 | 81.6% | 0.8594 ms → 0.1735 ms | 79.8% |
| 2 | AoSoA8, batch 128 | AoS, batch 128 | 80.7% | 0.7809 ms → 0.1677 ms | 78.5% |
| 3 | AoSoA8, batch 256 | AoS, batch 32 | 79.9% | 0.8078 ms → 0.1704 ms | 78.9% |

All measured candidates reported parity pass, identical per-run state hashes, 0 hot-path managed allocation bytes, and approximately 48 MiB resident storage. The selected layout is stable; the exact batch and best AoS batch are not stable across processes.

## What the number means

This large difference is plausible for the deliberately hot/cold workload but must be described precisely. AoS copy-modify-write touches a 48-byte logical record. SoA touches only the hot arrays. AoSoA8 additionally performs explicit eight-lane work through two `float4` groups per job iteration. The headline is therefore a **candidate pipeline** comparison, not a pure “layout alone” comparison.

The faster candidates also showed higher per-run MAD than the desired final evidence threshold. Cross-process P95 is stable, but confidence intervals and a noise-aware tie policy are not implemented yet.

## Gates before source generation

1. Add a common bounds/metadata/export consumer and retain the improvement.
2. Measure optimized ingress, full egress, resident bytes, and amortized lifetime cost.
3. Add bootstrap confidence intervals and avoid selecting a batch from statistical ties.
4. Repeat on IL2CPP Release and at least one second CPU/workload profile.
5. Build the result-file-driven heatmap/GIF view only from runs that pass those gates.

Raw local artifacts are under `Artifacts/aot-run-{1,2,3}` and are intentionally ignored until the schema and evidence policy are finalized.
