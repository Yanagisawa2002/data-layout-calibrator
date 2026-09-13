# Optional Windows CPU evidence

`WindowsProcessCycleCounterProvider` implements the existing optional `ICounterProvider` with the documented Windows `QueryProcessCycleTime` API. It measures OS-accounted CPU cycles across **all threads of the current process**, including user and kernel execution. This includes Unity job workers and unrelated Unity threads. It is correlation evidence; it cannot attribute cycles exclusively to a Burst kernel, establish IPC, or substitute for retired-instruction PMU events. Do not divide by CPU frequency to infer elapsed time.

Microsoft API contract: [QueryProcessCycleTime](https://learn.microsoft.com/en-us/windows/win32/api/realtimeapiset/nf-realtimeapiset-queryprocesscycletime). No driver installation, elevation, ETW capture session, or global profiling state is required by this provider. Native failures retain Win32 numeric codes and messages. Missing APIs/platforms report unavailable; a mid-capture error is failed. The core no-provider path still runs the action exactly once and reports unavailable.

Instructions, cache references/misses and branch instructions/misses have explicit `Unavailable` entries with code `not-exposed-by-query-process-cycle-time`. Their numeric values are absent, not zero. A read-only `wpr -pmcsources` invocation records its actual exit/output separately; an inventory is not actual PMU evidence. Lack of an implemented PMU event source is not claimed to prove this CPU lacks hardware support or that a particular privilege was denied.

## Run and retain

First build the existing non-Development Windows Mono AOT or IL2CPP Release Player under the shared validation lock. The build checks both new workloads' literal Burst entrypoints and the ISA identity job, then writes `source-identity.json`: exact source/input file hashes, source commit plus dirty status, Unity version and package lock input. Keep that file beside the Player.

```powershell
& ./Tools/CpuCounters/Invoke-CounterEvidence.ps1 `
  -PlayerPath ./Builds/windows-x64/il2cpp-formal/DataLayoutCalibrator.exe `
  -OutputDirectory ./work/counters/attempt-01 `
  -SerializationScript '<control>/Invoke-SerializedValidation.ps1' `
  -Count 65536 -Ticks 64 -Pairs 12 `
  -Scenarios 'particle-integrate-v2,transform-export-v1,spatial-neighborhood-v1,animation-state-v1'
```

Each attempt requires a fresh directory. `-NoProvider` exercises the actual Release fallback, with an expected unavailable gate. No-provider timing measures the failure-isolating wrapper path, not a hardware sampler. Keep normal calibration timing collection counter-disabled.

The Player entrypoint is `-dla-counter-run`; do not combine with `-dla-run`. Direct flags: `-dla-counter-count`, `-dla-counter-ticks`, `-dla-counter-pairs`, `-dla-counter-scenarios`, `-dla-counter-candidates`, `-dla-counter-no-provider`, `-dla-output`, `-dla-quit`. The frozen candidate file is optional and has `SchemaVersion: 1` and `Scenarios: [{ ScenarioId, Candidates: [CandidateDescriptor, ...] }]`. Serialize full descriptors, including all four policies, using the actual matrix API; the runner validates and records canonical candidate hashes, file hash, and copies the input. Without a file it uses each registered factory's actual defaults. It never silently labels the default matrix as the expanded matrix.

For each candidate, the runner warms both paths, restores identical input before each arm, and alternates AB/BA by pair. Timing covers the **complete adapter including raw endpoint file persistence** as well as execution. Overhead therefore includes instrumentation allocation, validation, probing, hashing and file I/O. Reset, export/hash and final JSON writing are outside that timing. Negative/noisy deltas are retained. This is a practical end-to-end cost measurement, not an isolated syscall latency estimate; it makes no constant-overhead or performance-benefit promise. Boundary/resident allocation checks cover the workload without optional instrumentation.

`cpu-counter-evidence.json` stores every arm, order/pair, candidate definition, dataset hash, output hash, capture status, raw cycles, overhead summaries and the actual-counter gate. `raw/cycles-*.txt` preserves exact unsigned integer endpoints (the public raw numeric API uses double). Identity includes fresh run/process IDs, CPU model, OS, worker count, Release/backend, executable, UnityPlayer, implementation, Burst DLL, OS API libraries, and Mono workload assemblies. A Burst job reports dispatched SSE2/AVX2 target capabilities, **not** per-kernel instruction disassembly. Source/package/compiler settings are bound by the build source manifest; retain Burst debug manifests when auditing individual kernels. The Radeon GPU is not used to identify CPU layout winners.

An available provider with failed samples fails the actual-counter gate. Unavailable collection preserves the workload run and explicit unmet gate. Neither case is a successful actual-counter experiment. This diagnostic does not alter any layout selection or deployment profile.

## Additional workloads

- `spatial-neighborhood-v1`: a complete 27-cell grid neighborhood gather over shuffled record IDs. Positions have bounded jitter; radius is 0.32 and grid spacing 0.25, so adjacent cells cover every possible in-radius point. The job reads scattered positions/weights and writes neighbor count plus weight sum. Static broadphase construction is dataset setup; copying the full adjacency is ingress. Full original records and query output are exported. Independent small O(N²) tests validate completeness and boundary cells.
- `animation-state-v1`: contiguous phase/blend updates with a paused state and state transitions on phase wrap. Cold entity/metadata fields are retained in the full canonical export. Separate scalar state evolution tests validate repeated updates and paused records.

Both own generated AoS/SoA storage and codecs, with developer-authored Burst jobs. Eight candidates cross two layouts, two batch sizes (64/128), and FrameFaithful/DependencyChain execution. They do not claim AoSoA, aligned/padded, temporal reordering, or branchless implementations. Full field parity, deterministic hashes, changed seeds, non-multiple counts, disposal, and unsupported-policy rejection are tested. Runtime ABI is additive: two factory registrations and two deterministic dataset seed cases.

The worker's short validation is not the integrated formal matrix, five-process replication, PMU evidence or a new device-wide layout conclusion. The integration report must retain each outstanding gate until its actual run is complete.
