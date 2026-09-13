# External workload ports — original preparation contract

September 10 follow-up: the [actual comparison report](../../../../Docs/ACTUAL_COMPARISON_REPORT_2026-09-10.md)
records a separately frozen headless IL2CPP/Burst host and Windows native adapters
for BabelStream and LLAMA code_comp. The preparation-only delivery described below,
including its Unmeasured labels, is historical; no prior number is relabelled as
new-source evidence. STREAM and HeCBench remain unexecuted. Raw upstream files and
the semantic contracts below remain unchanged; Windows adaptation and measured
scope are explicitly described in the dated report.

This delivery performed no benchmark, calibration, stress run, real clock/counter
collection, GPU dispatch or Player execution. The new assembly has no scenario
factory, timing loop or automatic registration. Its kernel APIs remain callable;
they contain no chat authorization or permanent performance-disable checks.
The external source tool is preparation-only: default hash verification, optional
object compilation. Native benchmark drivers are separate explicit entrypoints,
and were not executed during this delivery.

## Source identity and classification

`Upstream~/upstream-lock.json` records repository commits, original URLs and exact
SHA-256 for every retained file. The `~` directory is excluded from Unity import.
Raw upstream files and licenses are unmodified; upstream mains are never invoked.

| Source | Fixed identity | Category / use |
| --- | --- | --- |
| [BabelStream](https://github.com/UoB-HPC/BabelStream/tree/17ab377b0e919e14fd3df2b67268761fdac8abb3) | `17ab377b0e919e14fd3df2b67268761fdac8abb3` | External general bandwidth benchmark; double-precision operation port and original OpenMP sources |
| [STREAM](https://www.cs.virginia.edu/stream/) | Official `stream.c` revision 5.10, content SHA-256 in lock (website has no Git commit) | External general bandwidth benchmark; source/embedded license and object-compilation entry only |
| [LLAMA code_comp](https://github.com/alpaka-group/llama/tree/086e66e7565f677d6b3aff88542e28c7dd6d8228/examples/nbody_code_comp) | `086e66e7565f677d6b3aff88542e28c7dd6d8228` | External data-layout library's native n-body code-comparison example; **not a general standard benchmark suite** |
| [HeCBench stencil3d-omp](https://github.com/zjin-lcf/HeCBench/tree/7d2d3c567be522a2104065165de0a4a233a6ea1a/src/stencil3d-omp) | `7d2d3c567be522a2104065165de0a4a233a6ea1a` | Concrete stencil workload within an external heterogeneous benchmark collection; source/license/build preparation only, no Burst port claimed |

BabelStream/STREAM are streaming bandwidth and boundary controls. Their native
three-array SoA structure is not evidence for AoS superiority or general layout
selection. LLAMA supplies a different all-pairs access pattern. No matching
general standard suite has been established for this repository's exact particle
respawn or Transform TRS contract. The original demos remain repository workloads.
Google Benchmark, BenchmarkDotNet and Unity Performance Testing would only be
measurement frameworks; PRK is not used as a benchmark ranking suite here.

## BabelStream equivalence contract

The upstream default is **double**, 33,554,432 elements, 100 repetitions, classic
order Copy, Mul, Add, Triad, Dot. Initial `a/b/c` = `0.1/0.2/0.0`; scalar `0.4`.
The five passes and their blocking boundaries must remain separate. Nstream is
an optional upstream selection, not added to the default classic sequence.

The port uses `NativeArray<double>` and a concrete job per operation:
`c=a`, `b=scalar*c`, `c=a+b`, `a=b+scalar*c`, `sum=Σ(a*b)`; optional
`a+=b+scalar*c`. Arrays must have equal logical length and disjoint storage, and
the caller must complete or chain dependencies between passes. No job allocates.
The scalar gold recurrence and per-element symmetric relative checker follow
the locked `main.cpp`: machine epsilon times 100, or times 10,000,000 for Dot.
`Double.Epsilon` is deliberately not used (it is not machine epsilon).

Differences: C++/OpenMP -> C#/Burst; the contract Dot job uses ascending serial
summation instead of the native OpenMP reduction tree. Only classic double and
individual operations are prepared; isolated mode, float mode and upstream CLI
selection are not implemented. Small test counts are **correctness fixtures**,
not the default workload or bandwidth evidence. A future performance label must
say “C#/Burst variant of BabelStream”; it cannot claim original BabelStream or
STREAM results. The retained custom license requires clear labels for variants
and compliance with its run rules for native benchmark claims.

## LLAMA equivalence contract

The reference is the independent `examples/nbody_code_comp/nbody-AoS-baseline.cpp`;
AoS, SoA and AoSoA source files are retained for review/compilation. Defaults are
float, 65,536 particles, five steps, `timestep=0.0001`, `eps2=0.01`. Upstream uses
`default_random_engine` and normal N(0,1) for positions, N(0,1)/10 velocities,
N(0,1)/100 masses, including negative masses. C++ standard library random-engine
and normal-distribution outputs are implementation dependent: a future comparison
must freeze the canonical records generated by the exact native compiler/library
and feed identical records to both sides. The port intentionally takes records;
it does not impersonate the native RNG using a different language's generator.

`Fixtures~/llama-msvc-prefix17.json` contains the first 17 records from that exact
initializer call order using the recorded MSVC/STL versions. Floats are stored as
uint32 IEEE-754 bit patterns. `Tools/KernelContracts/llama_input_fixture.cpp` is a
bounded, input-only preparer: it contains no workload kernel, repeats, timer or
upstream main, refuses default invocation, and only accepts
`--prepare-17-records <output.json>`. It was compiled and executed solely to
prepare this small input prefix; **no native n-body workload was executed**.
`fixture-lock.json` binds the preparer source and fixture SHA-256. The correctness
model consumes these same bytes, plus clearly separate edge-case fixtures. The
17-record prefix is not a replacement for the 65,536-record upstream workload.

For every target `i`, visit source `j=0..N-1` (including self), square each
component of `pi.pos-pj.pos`, then use those **squared components both in the
distance and in the velocity increment**. This unusual upstream formula is
preserved; it is not replaced by another gravity model. Accumulation order and
`1/sqrt(distance^3)` remain unchanged. All velocities update before any positions
move. Masses and all canonical fields survive ingress/export; padding sources
are never used for interactions.

`LlamaNBodyUpdate4Job` broadcasts each source to four independent target lanes.
Each source block is loaded outside its lane loop. Source position/mass arrays
are read-only while velocity arrays are written, avoiding whole-record source
reads of concurrently modified velocities. The dependent move job supplies the
required global barrier. No horizontal reduction/reassociation is introduced.
Static work remains quadratic; ISA lowering and speed are **Unmeasured**.

Differences: C++ -> C#/Burst, native AoSoA width 16/alignment 64 -> four target
lanes with no base-alignment promise, distinct source/velocity storage, explicit
tails, job dependencies, and ordinary managed export. The separate upstream
`examples/nbody/nbody.cpp` is retained for discovery only: it uses another driver,
defaults and layout/ISA experiments, and is not silently substituted for code_comp.
The code_comp main returns success without a numerical checker. This port's
small scalar-vs-packed equivalence assertions are an **added local contract**,
not an upstream acceptance criterion. MPL-2.0 headers and license are retained;
`LlamaNBodyPort.cs` is a modified MPL-2.0 file. Do not relabel it under the
repository's general license when distributing source or compiled forms.

## Additional prepared native sources

STREAM keeps its 10,000,000-element / 10-iteration defaults, embedded custom
license and original validation routine. No cache-sizing calibration is run.
HeCBench stencil3d-omp keeps its mandatory `grid dimension` and `repeat` arguments,
float scalar type, tile sizes, stencil arithmetic, initialization and target
directives. Its local MIT license, copyright Lawrence Livermore National Security,
LLC, is retained separately from the collection license. No dimensions, repeats
or GPU fallback are invented here.
The retained main is not treated as proof of independent numerical correctness.

## Safe entries

From repository root:

```text
python Tools/KernelContracts/external_sources.py
python Tools/KernelContracts/external_sources.py --compile-objects llama-code-comp --compiler <C++20 compiler>
python Tools/KernelContracts/external_sources.py --compile-objects babel-omp --compiler <POSIX OpenMP C++ compiler>
python Tools/KernelContracts/external_sources.py --compile-objects stream --compiler <POSIX OpenMP compiler>
python Tools/KernelContracts/external_sources.py --compile-objects hec-stencil3d --compiler <OpenMP target-capable C++ compiler>
```

These entries emit object files only; they never link or execute native mains.
MSVC is supported for the three standalone LLAMA code_comp files. Its ordinary
`INCLUDE` environment is required. Other sources retain POSIX/OpenMP requirements;
no dummy clocks, missing OpenMP APIs or allocator shims replace upstream semantics.
Performance results for every new source identity: **未测量 / 待验证**.
