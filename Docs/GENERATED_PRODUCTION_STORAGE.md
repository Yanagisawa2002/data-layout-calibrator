# Generated storage in production workloads

ParticleIntegrate and TransformExport now allocate their formal candidate storage
through the record source generator. This applies to the existing candidate set;
it does not promote any newly generated layout to a measured production default.

Particle AoS/SoA/AoSoA8 compatibility facades own generated storage. Transform's
AoS candidate owns `TransformRecordGeneratedAoSStorage`; its SoA facade owns
`TransformRecordGeneratedSoAStorage`. Explicit Burst boundary jobs assemble
borrowed storage views and call generated `WriteRecord`, `ReadRecord`, or
`IngressBlock`. Thus both hot and cold field mappings have one schema-owned
implementation. Scheduling and mathematical kernels remain developer-owned.
Transform's TRS output is a derived workload result; its handwritten mathematical
export job and final native bulk copy intentionally remain explicit.

AoS boundaries retain native bulk copies. Particle's generated packed width-8
block keeps the historical 224-byte sequence of component `float4`s, including
the original field ordering. Ingress clears the tail block and writes only
logical lanes. Kernel math, scalar branches/selects, logical batch conversion,
execution policy, full canonical boundaries, and original candidate IDs are
unchanged. New binaries require new evidence; old timing is not reclassified.

## New generated contracts

`GenerateDataLayoutAttribute.GeneratePackedFloat4 = true` additionally emits
`RecordGeneratedPackedAoSoA4Storage`, `...8Storage`, and `...16Storage`, and their
`...Block` types. Only `float` and `float3` hot fields are accepted. Other supported
flat types can remain cold side arrays. For a float3 Position the block exposes
`PositionX0`, `PositionY0`, `PositionZ0`, with group suffixes through width/4 - 1.
Scalar Lifetime becomes `Lifetime0`, etc. Public `Read_Position` and
`Write_Position(ref block, lane, value)` helpers support developer scalar kernels.

Storage exposes `HotBlocks`, `Cold_<FieldName>`, `Count`, `BlockCount`, `Allocate`,
`FromRecords`, `ReadRecord`, `WriteRecord`, `Ingress`, `Export`, and `Dispose`.
`IngressBlock(blockIndex, source)` is a job-level codec: callers supply a valid
block index and a source matching logical Count; one scheduled job iteration
must own each complete block. Schedule over BlockCount, not Count. ReadRecord and
WriteRecord are unchecked low-level accessors outside Unity container checks;
validated public scenario boundaries enforce logical lengths.

`PaddedRecordSize = 64` emits `RecordGeneratedPadded64Record { Record Value; }`
with sequential Size=64, and owning `RecordGeneratedPadded64Storage`. Allocation
verifies the actual native element size equals 64 and rejects an oversized record.
The additional stride is a padded control, **not** a claim of cache-line-aligned
base allocation. All arrays retain Unity NativeArray's actual allocation contract;
no custom 64-byte base alignment is promised. Unsupported padding values and hot
packed field types produce DLCGEN104. Opt-in settings enter the schema hash;
schemas without the options retain their previous hash.

## API migration and ownership

This is a source/binary API change for sample storage consumers, not a fully
source-compatible facade. The old public `ParticleAoSoA8Block` is removed; use
`ParticleRecordGeneratedPackedAoSoA8Block` (or a local C# using alias). Facade
NativeArray fields are now borrowed-view properties, and Count is read-only.
For an indexed write take a local NativeArray view first or use the generated
storage field. NativeArray properties do not transfer ownership. Assigning views
or manually constructing generated storage is reserved for borrowed job adapters;
never dispose such borrowed adapters.

Allocate/FromRecords create one owning value. Dispose that original owner only,
after completing all jobs. Disposal clears its arrays and Count; repeated disposal
of the same owner is inert. A copied struct does not acquire independent ownership
or become invalidated automatically: disposing either copy then using/disposing
the other is unsupported. Multi-array allocation cleans up on exceptions; full
FromRecords ingress failures also dispose the partial owner. Zero-length generated
storage is supported; formal scenarios continue to require positive workloads.

## Verification and evidence boundaries

The source generator tests compile emitted code and check deterministic output.
Editor tests check canonical fields, packed widths, zero/partial/full blocks,
historical width-8 offsets, invalid lengths, and owner disposal. Existing formal
scenario tests still run actual developer kernels and compare canonical outputs.

The Player flag `-dla-generated-workloads -dla-output <directory>` independently
runs the actual generated factory registry, selecting ParticleIntegrate and
TransformExport with every currently registered default candidate. It covers
11 counts around widths 4/8/16 plus 4099, each with two deterministic seeds.
After 16 warmups it measures main-thread managed allocation events separately for
16 ingress calls, 16 executions of 3 ticks, and 16 exports. A `ProfilerRecorder`
for `GC.Alloc` records individual events on the current thread, without frame
aggregation or ring-buffer overwrite. Small-object and 4096-byte-array controls
must produce at least two events; an empty/reset control must produce zero.
Controls run before the workload, after each cell, and at the end. Unavailable
recorders, failed controls, and full buffers reject the gate. The gate uses event
count, not a heap-size difference. Byte totals are only reported if Unity declares
the metric's unit as Bytes; otherwise the byte field is -1 (unavailable).
It checks parity,
48-tick reset/reexecution equivalence, duplicate disposal, and rejection of
disposed access. Hashing, validation, JSON, and scenario setup are outside the
allocation windows. Worker kernels must be present in the Burst AOT manifest;
the main-thread counter alone is not evidence about arbitrary managed workers.

```powershell
& ./Tools/Validation/Invoke-GeneratedWorkloadValidation.ps1 `
  -UnityEditor 'C:/Program Files/Unity/Hub/Editor/6000.5.3f1/Editor/Unity.exe' `
  -SerializationScript '<control>/Invoke-SerializedValidation.ps1'
```

The runner builds and executes Mono and IL2CPP Release Players while holding the
shared lock, retaining receipts, logs, AOT entrypoint manifests, source status,
process identity, and hashes of executable/DLL artifacts. Generated codec methods
inline into the existing boundary Job entrypoints, so those containing Job names
remain the required manifest entries. A method-name reachability probe alone is
insufficient; the workload receipt must also pass.

These checks do not prove a performance benefit, measured advantage envelope,
adaptive search quality, or hardware-counter behavior. Formal comparison on the
integrated, frozen candidate set remains a separate integration gate.

Worker validation retained in
[validation-summary.json](evidence/generated-production-storage-2026-09-07/validation-summary.json):
14 generator tests and 158 Unity EditMode tests passed. This is ready for
integration, not a completed Player allocation/AOT or performance gate. The
summary identifies the tested revision and the final constructor/provenance
changes that still require the integrated validation run.

### Allocation-counter correction after the first integrated Mono run

The original `GC.GetAllocatedBytesForCurrentThread` positive control correctly
rejected the first real Mono Release run. Unity 6000.5.3f1's bundled
`external/mono/mono/metadata/boehm-gc.c` returns zero in that function;
`libil2cpp/icalls/mscorlib/System/GC.cpp` marks it not implemented. Consequently,
old zero readings from this API are not evidence of zero allocation on these
backends. They are retained as historical observations, not upgraded to passes.

The replacement follows Unity's documented
[current-thread GC.Alloc recorder pattern](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread.html).
[ProfilerRecorder supports Release Players](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html),
but this does not guarantee that a particular marker exists in every Player.
The positive/empty controls remain mandatory on both Mono and IL2CPP, and the
Release gate stays unresolved until they and actual workload observations pass.
The engine-neutral formal calibration engine must use an independently validated
injected provider as well; fixing this validator does not repair its older raw
counter calls. Integration owns that injection and rejects unsupported counters.
