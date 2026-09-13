# Release Player allocation evidence

`allocation_profiler.cpp` is an independently written Windows x64 adapter to the
Mono and IL2CPP profiler C APIs in the installed Unity 6000.5.3f1 runtime. It does
not load a substitute runtime, patch instructions, scan private addresses, or
replace an existing profiler callback. Only exports from the already loaded
`mono-2.0-bdwgc.dll` or `GameAssembly.dll` are resolved.

Mono uses `mono_profiler_enable_allocations`, creates its own profiler handle,
and installs a `gc_allocation` callback. The installed Unity Boehm source has no
managed allocator stubs, checks its allocation-enabled flag in object, vector,
and string allocation paths, and allows instrumentation to be enabled. A runtime
that rejects enabling instrumentation is unsupported. IL2CPP uses its profiler
install/allocation/events APIs with `IL2CPP_PROFILE_ALLOCATIONS` on the newly
installed profiler; missing exports are unsupported. Integration must validate
the actual IL2CPP Release binary, not infer availability from header defaults.

Callbacks are entirely native. Per-thread windows count events and sum
`mono_object_get_size` / `il2cpp_object_get_size`. These are **runtime object-size
bytes**, including object metadata according to that API; they do not measure
collector slot rounding, reserved heap pages, or native allocations. A zero
event window establishes zero managed allocation on that thread. Overflow and
invalid/nested windows fail explicitly. Other threads are excluded; Burst AOT
evidence is independently required for the actual workload worker jobs.

The profiler is installed once during benchmark startup. Dispose closes a live
measurement window but does not remove runtime profiler registration or change
other profilers' flags. The adapter DLL is pinned until the current Player
process exits, preventing a dangling callback during shutdown. It is an optional
benchmark instrumentation component, not a production layout dependency.

The managed wrapper first compiles and executes its escaping allocation-control
function **before** native registration. The same non-inlined function then
allocates byte arrays of length 1 and 4096, an object, and a fresh string inside
the window. At least four allocation events and 4097 object-size bytes are
required, followed by a zero-event empty/reset control. Both controls are
repeated around actual workloads. No unavailable value is accepted as zero.

Build under the shared measurement lock:

```powershell
& ./Tools/AllocationRecorder/Build-AllocationProfiler.ps1 `
  -SerializationScript '<control>/Invoke-SerializedValidation.ps1'
```

This uses MSVC x64 Release `/O2 /MT /W4 /WX`, writes the Unity Win64-only plugin,
and retains `/Bv` compiler identity, source/binary/log SHA256 receipts in
`Artifacts/allocation-native-build`. Then run the existing
`Tools/Validation/Invoke-GeneratedWorkloadValidation.ps1` for the actual Mono and
IL2CPP Release gates. Unity's `GC.Alloc` recorder remains the first path when it
is available; Release platforms without either validated provider fail closed.

The first Mono Release `GC.GetAllocatedBytesForCurrentThread` control returned
zero; the subsequent `GC.Alloc` recorder was unavailable. Those failures are
retained. Neither a successful native compilation nor a positive control alone
is a completed steady-state workload gate.
