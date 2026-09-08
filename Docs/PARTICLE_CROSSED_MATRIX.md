# Particle layout, kernel, and execution matrix

`ParticleCandidateMatrix.CreateCandidates()` is an explicit opt-in experiment with
120 implementations. The historical default remains 32 candidates. It preserves
all old candidate IDs, policies, and sort orders; generator adoption changes the
binary/source identity, so historical timing is not pooled with new runs.

| Layout | Scalar branched | Scalar branchless | Packed branchless | Record stride / hot block |
| --- | --- | --- | --- | --- |
| AoS | Yes | Yes | Unavailable | 48 bytes |
| SoA | Yes | Yes | Unavailable | separate field arrays |
| AoSoA4 | Yes | Yes | 4 lanes | 112 bytes + 20 cold bytes per logical record |
| AoSoA8 | Yes | Yes | 8 lanes | 224 bytes + 20 cold bytes per logical record |
| AoSoA16 | Yes | Yes | 16 lanes | 448 bytes + 20 cold bytes per logical record |
| AoSPadded64 | Yes | Yes | Unavailable | 64 bytes, 16 bytes trailing padding |

All 15 layout/kernel pairs cross `FrameFaithful` and `DependencyChain`, and logical
batch sizes 32/64/128/256. The 16 unpadded AoS controls remain eligible baselines;
the existing engine selects the fastest valid baseline rather than presuming a
winner. Padded AoS is an experimental control, not a weaker replacement baseline.
TransformExport retains its matched AoS/SoA matrix; it has no respawn branch to
meaningfully cross and no implementation of packed Particle-specific kernels.

`CreateCells()` exports the full 360-cell declared Cartesian grid, including 240
excluded combinations and reasons. Packed kernels require the exact hot-block
width; gathering another representation would add work and a new factor.
TemporalBlock is unimplemented under the frame-observable contract. An explicit
alignment request is rejected: NativeArray allocation does not promise the
requested cacheline base alignment. The padded control measures a 64-byte stride,
not a guaranteed aligned allocation. No unimplemented cell becomes a timing of zero.

Scalar means source-level operations per lane, not guaranteed scalar machine code;
Burst can vectorize or if-convert. Packed8/16 use two/four float4 groups, not a claim
of eight/sixteen-wide ISA instructions. AoSoA jobs schedule one complete hot block
and use max(1, logical batch / width); metadata records physical batch size and
records per schedule unit. Padded tail lanes execute and remain resident but are
never exported. Canonical cold rotation/category fields are preserved.

For interpretation, match kernel/execution/batch when comparing layouts, match
layout/execution/batch when comparing source control flow, and match
layout/kernel/batch when comparing execution topology. Compare differences in
matched contrasts to study interactions. An AoS-vs-AoSoA contrast still includes
the declared record-vs-block scheduling and hot/cold separation; do not claim an
isolated cache or SIMD cause without further controls and actual counter evidence.
Full generated-codec ingress/export and native resident bytes remain in the
existing amortized decision rule. Timing samples and held-out decisions must use
the exact frozen candidate-set SHA, source, compiler, binary, CPU, and worker count.

## Reproduction and gates

`ParticleMatrixValidation.RunOrThrow()` runs 2166 actual candidate checks:
all 120 candidates at counts 1/3/4/7/8/15/16/17/257, 97 ticks, twice from fresh
ingress, field parity against a separate scalar oracle, hashes against AoS,
plus empty storage lifetime/codec checks for six layouts. Calibration scenarios
intentionally reject zero elements. Tests verify old definition hashes, rejected
false metadata, and matrix cardinality. No steady-state measurements are inferred
from this correctness probe.

```powershell
./Tools/Validate-ParticleMatrix.ps1 `
  -UnityEditor 'C:/Program Files/Unity/Hub/Editor/6000.5.3f1/Editor/Unity.exe' `
  -SerializationRunner '<control>/Invoke-SerializedValidation.ps1' `
  -Backend mono -Batches '64,256'
```

This builds Release with Burst AOT and emits `matrix-receipt.json` (complete
`Candidates`, `CandidateSetSha256`, supported/excluded `Cells`) and identity hashes.
The selected export batches do not reduce correctness coverage. Use `-Backend
il2cpp` for the corresponding Release binary. The integrated search runner consumes
the wrapper and freezes execution-specific subsets. The envelope runner freezes
its declared subset after all workers merge.

Formal performance, allocation samples, measured envelope, five-process
replication, adaptive-versus-exhaustive regret, and optional real counters are
separate integration gates. Passing this probe does not complete them and does
not promote any candidate to a production default.
