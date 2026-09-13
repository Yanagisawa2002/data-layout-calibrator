# Layout kernel changes and non-performance validation

Implementation baseline: origin/main `7a91236c3ed825b85ada6dca0b225289f150c41f`
plus local unpublished vNext `61f8afb`, merged in this isolated feature branch as
`a8ed2ef`. The original worktree and opt-next branches were read only. The vNext
branch already had branchless AoS, scalar/packed AoSoA4/8/16, crossed candidate
factors, generated storage and cold-access workloads. Those are reused.

## Concrete changes and attribution

| Change | Static basis / API | Factor and limits |
| --- | --- | --- |
| Hot/cold AoS | `ParticleHotColdStorage.ScheduleStep(dt, logicalBatch, branchless, dependency)`; step jobs contain only `NativeArray<ParticleHotRecord>` | New storage factor: contiguous 28-byte hot records with Rotation/Category in separate arrays. Branched and branchless step variants preserve the exact one-respawn rule. No speed claim. |
| Generated block codec | `Ingress` traverses physical blocks, and new `ExportBlock` loads the hot block once before reconstructing logical lanes | Removes repeated whole-block read/modify/write from generated ingress and per-record block loads from export at source level. Cold fields are copied, padding zeroed on ingress, and only logical lanes export. Schema metadata/field order are unchanged. |
| Scheduled block export | `ParticleBlockExportScheduler.Schedule(ref packed4/8/16, destination, logicalBatch, dependency)` | Explicit boundary factor. Existing record-level scheduled exports remain available for controlled comparisons. One job index owns one disjoint block range; logical batch / width is floored and clamped to one, matching vNext. |
| Transform TRS4 | `TransformPacked4Storage` plus `TransformPacked4IngressJob`/`TransformPacked4ExportJob` | Four independent entity components per `float4`; nine matrix-component vectors are computed before the bounded canonical export loop. Integer IDs/flags stay exact. Adds a compute-dense packed candidate absent from vNext. |
| External n-body | `LlamaNBodyPackedStorage.ScheduleStep` | Four target lanes, source-block reuse, read-only positions/masses during velocity update, then one dependent move pass. Exact source interaction order; no horizontal reduction. See external contract/license. |

All new candidates are **Unmeasured** and unregistered: no default candidate,
selector, profile/schema, public Runtime protocol or historical evidence file is
changed. The generated codec implementation changes are source-bound; prior
measurements remain historical results for their old source identity. Do not
attach a previous profile/result to this binary without its provenance checks.

For particle factor attribution, compare existing full AoS vs new hot/cold AoS
with the same scalar control flow, logical batch and completion topology. Compare
branched vs branchless within one storage. Compare AoSoA scalar vs packed within
one block width. Change record export vs block export as a separate boundary
factor. Existing packed kernels already contain real four-lane source arithmetic;
they are not rebranded as a new implementation. No allocator base alignment or
CPU instruction width is inferred from block width, stride or `float4` alone.

Every step API schedules exactly one canonical step. A frame-faithful caller
completes after every frame; a dependency-chain caller feeds each returned handle
to the next schedule and completes the final handle. There is no temporal fusion
or reordering of frame-observable work. Callers own native arrays and must finish
dependent jobs before reading, exporting, reingressing or disposing. Storage
struct copies borrow arrays and must not be disposed as independent owners.

## Audited safe validation surface

`KernelContracts.csproj` explicitly includes the actual affected sources and the
current generator. Its normal executable uses **real Unity mathematics** and a
small managed NativeArray/job-dependency model. The model reverses job-index order
in selected tests, defers actions until `Complete`, and uses integer model access
counts only. There are no real threads, clocks, performance counters, Burst runs,
Unity Player, profiler calls or native Unity allocations in this executable.
Counts are bounded to at most 17 records, with a few fixed correctness steps.

The model asserts all canonical particle fields, negative/zero/expired lifetime,
single respawn for large dt, untouched cold payloads (including NaN/signed-zero
bits), all packed widths/control flows, frame completion vs dependency chains,
zero-length storage, block tails, padding poison, mismatched boundary lengths,
TRS vs the actual `float4x4.TRS`, IDs/flags, BabelStream upstream gold/checker, and
LLAMA scalar equivalence on both edge cases and the frozen native RNG input prefix.
Model access counts verify the new codec's block ownership and hot-array loads;
they are not hardware traffic/performance measurements.

`RealUnityCompile=true` instead emits a **library only**, omits the model/runner,
and compiles the same production sources against real Unity assemblies. This is
a C# API/type compilation check, not Burst AOT/ISA or native lifetime validation.
Both paths rebuild the package's generator DLL from the modified generator source.

```powershell
$mathPath = 'C:/Program Files/Unity/Hub/Editor/6000.5.3f1/Editor/Data/Managed/UnityEngine'
dotnet build Tools/KernelContracts/KernelContracts.csproj -p:UnityManagedPath="$mathPath"
dotnet Tools/KernelContracts/bin/Debug/net8.0/KernelContracts.dll --functional-only
dotnet build Tools/KernelContracts/KernelContracts.csproj -p:RealUnityCompile=true -p:UnityManagedPath="$mathPath" -p:UnityScriptAssembliesPath='<existing project>/Library/ScriptAssemblies'
dotnet test Packages/com.yanagisawa.data-layout-calibrator/SourceGenerators~/Tests/Yanagisawa.DataLayoutCalibrator.SourceGenerator.Tests.csproj --filter FullyQualifiedName~DataLayoutScaffoldGeneratorTests
python Tools/KernelContracts/external_sources.py
```

The normal executable refuses an empty/unknown invocation, including performance
options. `external_sources.py` defaults to offline hash verification and only
offers object compilation. It has no run subcommand: native drivers are separate
measurement commands. Existing validation scripts can launch Players, calibration
or counters; none is called by the functional runner or PR CI.

The external input-only preparer is a separate MPL-2.0 file. It only generates
17 initial records; it cannot run n-body kernels or accept a benchmark-sized count.
The immutable fixture is already checked in, so normal validation does not need
to run it. Upstream sources, classification, licenses, input differences and future
build entries are in `Samples/ExternalWorkloads/CONTRACT.md`.

## Remaining validation

Burst AOT/ISA inspection, actual Unity worker scheduling, native allocator and
job-safety behavior, full upstream inputs/drivers, formal performance, cache/
bandwidth effects and allocation counters are **未测量 / 待验证**. No performance
number or default-algorithm promotion is justified by this delivery. Pure C#
compilation and a functional CPU model do not establish hardware SIMD lowering.
