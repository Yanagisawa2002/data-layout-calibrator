# Resident CPU batch: direct execution before automatic tuning

A functional, explicitly configured point-processing component. Load coordinates
once, repeatedly transform and reduce directly over resident storage, then export
only when the caller requests a due snapshot. No calibration engine is required.

**Status: implementation and deterministic functional tests, not a performance
result.** This first backend uses serial managed arrays and double precision. It
is not a Burst/Jobs, SIMD, GPU, point-cloud file reader, mesh-bounds, or automatic
layout optimizer. No new allocation-qualified deployment profile is produced.
The existing measured Dot/SPH records and their source identities are unchanged.

## Separate from the calibration core

The opt-in assembly lives in
[`Batch`](../Packages/com.yanagisawa.data-layout-calibrator/Batch/BatchMath.cs),
not in the workload-agnostic `Runtime` assembly. It references neither UnityEngine
nor the calibration core. The explicit .NET build targets `netstandard2.0` / C# 7.3;
examples and the existing functional test host target `net8.0`.

The sibling Unity asmdef has `autoReferenced=false` and `noEngineReferences=true`.
A Unity consumer must explicitly reference `Yanagisawa.DataLayoutCalibrator.Batch`.
That assembly definition is wiring, **not evidence of Unity import, IL2CPP, Burst
compilation or Player execution**. No such validation was performed for this module.
The existing generated NativeArray codecs remain separate; this backend does not
claim to validate them or adapt arbitrary annotated records.

## API and ownership

| Type | Responsibility |
| --- | --- |
| `BatchPoint3` | Three double coordinates; order is preserved. No IDs, normals or attributes are dropped implicitly. Callers retain any sidecar data. |
| `AffineTransform3` | Three matrix rows plus translation, applied to a point. No perspective divide. Default maps to the origin; use `Identity` for identity. |
| `ResidentPointBatch` | Explicit AoS or SoA owner, reusable capacity, load, transform, bounds reduction and full canonical export. |
| `ResidentBatchPlan` | Immutable caller choice of layout and export cadence. Not a selected/calibrated profile. |
| `ResidentTransformPipeline` | Fixed ordering: transform, reduce point bounds, caller-requested export. Both examples use this same class. |

`Load` copies a caller-specified range. It does not retain the input array, and no
internal array is exposed. `Transform` operates on **current** resident coordinates;
repeated calls compose rather than restart from original positions. `ReduceBounds`
reads that same storage without constructing an intermediate exported array.
Bounds describe points, not the spatial extent of meshes located at those points.
Empty bounds have `HasValue=false`; zero Min/Max values are not measured bounds.

Choose `PointStorageLayout.AoS` or `SoA` explicitly. AoS is the conservative API
default, not an experimentally selected winner. Both layouts execute the same
scalar affine arithmetic. There is no runtime layout switching, hidden benchmark,
worker creation, machine setting change or performance-counter observation.

### Capacity, failure and release

The owner holds two reusable coordinate buffers: committed data and staging.
A failed load or nonfinite transform output rejects the entire operation without
changing Count, Version or the previous coordinates. A successful operation swaps
buffers. Transform overflow is an error, not a silently accepted Infinity/NaN.
Coefficient validation occurs in the transform constructor; direct `Apply` can
still overflow, while the resident owner checks every output before committing.

This rollback policy costs memory: steady-state coordinate payload is
`2 * 3 * sizeof(double) * Capacity = 48 * Capacity` bytes for either layout. The
payload property is a calculation, not an allocation observation. It excludes
managed headers, caller buffers and old/new buffers temporarily coexisting during
`Reserve`. All six SoA arrays/two AoS arrays must be included in a real cost study.

Only construction and explicit growth via `Reserve` create backing buffers.
`Reserve` never shrinks, retains committed data and does not change Version.
`Load` never grows automatically. No measured zero-allocation claim follows from
these source-level allocation sites. Exception paths and example receipts can
allocate; allocator/JIT/runtime behavior is not qualified by the functional tests.

`Dispose` is idempotent and drops managed references. It is not an immediate GC,
native deallocation or secure wipe. Further operational calls throw. The owner and
pipeline are single-owner and not thread-safe; access and disposal must be serialized.

### Export semantics

With `ExportEverySteps=2`, periodic output is due after completed steps 2, 4, 6,
and so on. Zero disables periodic output. A final request can export at step zero,
so an input-only/empty task has an explicit final snapshot. Final requests do not
close the pipeline; they are output events, not a lifecycle state transition.

Call `ExportIfDue` after each step to observe periodic events; missed events are
not queued or replayed. A periodic and final request for the same committed version
export once. A failed destination-range check does not consume the opportunity.
When nothing is due, the destination is not inspected or written. A successful
`Load` resets cadence and export state; a failed load resets neither.

Exports copy exactly Count complete points into caller-owned storage. Consume
Count, not Capacity or the entire destination array. No partial-field projection,
zero-copy conversion or truncated correctness check is claimed.

## Runnable callers and validation

From the repository root:

```powershell
dotnet run --project Tools/Examples/ResidentBatch -c Release
python Tools/CI/validate_functional.py
python Tools/CI/check_repository.py
```

The [two caller examples](../Tools/Examples/ResidentBatch/Callers.cs) use small,
nonuniform **synthetic coordinates**, not company data, an actual Unity scene or
an imported scanner dataset. Scene anchors load once, apply three incremental
transforms, export at step two and finally at step three. Point-cloud coordinates
reuse capacity across two differently sized frames and apply two transforms per
frame before final export. Both downstream consumers use every exported point.
The functional receipt deliberately allocates a retained output copy outside the
pipeline; it is not a performance or zero-allocation receipt.

The [allowlisted tests](../Tools/ResidentBatch/Tests/ResidentBatchTests.cs) run both
layouts against independent decimal affine expectations on full nonuniform outputs,
including empty, singleton and non-round counts. Other checks cover ownership,
capacity growth, stale tails, nonfinite input, overflow rollback, export failures,
cadence, disposal and complete expected outputs for both callers. The example
entrypoint is also invoked by the test host. There are no stopwatches, hardware
counters, allocation-counter reads or workloads from the benchmark suite in these
tests. They are ordinary correctness fixtures, not performance measurements.

These examples demonstrate reuse of one narrow coordinate-processing API. They do
not establish general production adoption, measured speedup or profitable calibration.

## Next experiment, not an automatic next run

Before promoting a faster backend, compare this fixed complete pipeline with a
reasonable handwritten fixed baseline under identical semantics. Include both
buffers, reserve/load, all transform and reduction passes, actual export cadence,
result consumption and disposal. Separate setup from repeated use without hiding
setup; include small/short-lived/adverse cases and independent confirmation.

Only then consider a Burst/NativeArray adapter, finite candidate planning or a
fused transform-and-bounds candidate. Do not infer any of those benefits from this
implementation. The old `WholeTaskTimingDecision` remains a mutable DTO; this new
immutable fixed plan does not repair or replace its selection/confirmation contract.

The repository [LICENSE](../LICENSE) is unchanged. Technical reuse examples do not
grant additional product integration or redistribution rights.
