# Formal IL2CPP evidence — 2026-09-02

This directory retains all five process launches required by the
[preregistered protocol](../FORMAL_BENCHMARK_PROTOCOL_2026-09-02.md). `run-01`
was fixed as the primary result before execution; runs 02–05 are robustness
replications. Every complete suite is retained, including the visibly wider
confidence interval in run 04.

## ParticleIntegrate holdout

| Run | Tuned AoS | Frozen selection | Lower P95 | 95% bootstrap CI | Reciprocal-throughput equivalent |
|---|---|---|---:|---:|---:|
| run-01, primary | `AoS-b64` | `SoA-b128` | 70.70% | [65.32%, 79.37%] | 3.41x |
| run-02 | `AoS-b256` | `SoA-b128` | 68.71% | [67.06%, 73.20%] | 3.20x |
| run-03 | `AoS-b64` | `AoSoA8-b64` | 74.81% | [72.82%, 76.55%] | 3.97x |
| run-04 | `AoS-b64` | `AoSoA8-b256` | 68.39% | [40.07%, 78.77%] | 3.16x |
| run-05 | `AoS-b32` | `AoSoA8-b256` | 66.11% | [62.90%, 69.59%] | 2.95x |

Across the five launches, holdout P95 reduction ranged from 66.11% to 74.81%,
with a 68.71% median. The worst per-run 95% lower confidence bound was 40.07%.
This range is descriptive; no hierarchical cross-run confidence interval has
been computed.

The selected layout and tuned AoS batch varied between launches. That variation
is retained because the product under test is the gated selection pipeline, not
a claim that one concrete layout is universally optimal.

![Preregistered primary-run heatmap](../../assets/formal-il2cpp-2026-09-02/data-layout-calibrator-heatmap.png)

![Preregistered primary-run comparison](../../assets/formal-il2cpp-2026-09-02/data-layout-calibrator-comparison.gif)

## Correctness and negative control

- ParticleIntegrate selected a non-AoS candidate in 5/5 launches and repeated
  the selection gates on each untouched holdout.
- TransformExport retained its tuned AoS candidate in 5/5 launches.
- All retained candidate results passed typed parity.
- Resident, ingress, and export measurements recorded 0 managed allocation.

These are five fresh Player processes on one recorded Windows/CPU environment,
not evidence across five devices. Each JSON suite contains the environment,
configuration, raw samples, candidate results, and frozen final decisions. The
hashes and selected fields are repeated in
[`formal-run-manifest.json`](formal-run-manifest.json).

After the frozen Player runs, release preparation corrected the external
renderer and future plain-text summaries from “X% faster” to the mathematically
precise “X% lower amortized P95.” The latency formula, JSON fields, workload,
candidate code, timing, decisions, retained Player, and every numeric result are
unchanged.
