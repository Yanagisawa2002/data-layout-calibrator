# vNext optimization integration and measured validation

Date: 2026-09-07. Status: implementation and the bounded validation programme completed locally; unreleased. Package version remains `0.3.0-preview.1`.

All five assigned workstreams are integrated and the real Mono/IL2CPP correctness, envelope, search-comparison and CPU-cycle experiments have executed. The measured results support conditional layout advantages, but do **not** support promoting the adaptive search or enabling the diagnostic counter by default. This is one Windows device, with five independent process launches where specified.

## Results that determine use

| Experiment | Observed result | Implication |
| --- | --- | --- |
| Envelope: five processes × 24 cells | 120 valid cells; 65 credible advantages, 21 AoS fallbacks, 14 statistical gray cells, 20 holdout rejections | Sampled credible coverage is 54.17%; retain the per-cell frozen decision |
| Envelope repeatability | 8/24 axes had some credible advantage in all five processes; 0/24 confirmed the same exact candidate in all five | No universal candidate or stable exact winner is established |
| Adaptive versus exhaustive: five processes × two execution policies | Median 55.83% fewer sample evaluations and 5.51% lower calibration wall cost; faster in 8/10 comparisons | Cost reduction alone is insufficient |
| Adaptive selection quality | 0/10 passed the preregistered 1% empirical-oracle regret limit; regret 15.30%–189.02%, median 106.91% | Adaptive remains experimental; no claim of exhaustive-equivalent quality or default promotion |
| Four-workload formal registry suite | All 64 candidates passed correctness/allocation; all four final decisions retained tuned AoS | Particle, Transform, SpatialNeighborhood and AnimationState all preserve fallback gates |
| CPU cycles | 768 real captures in 768 paired enabled/disabled comparisons, 1,536 rows; 0 failed/unavailable cycle captures | Actual whole-process CPU-cycle evidence and full-adapter overhead are available |
| No-provider check | 128 explicitly unavailable enabled arms, 0 fabricated captures | Missing data remains unavailable |

For the 65 accepted envelope cells only, held-out improvement had a descriptive peak of 66.68%, median 41.52%, and floor 16.61%; the smallest accepted confidence lower bound was 1.67%. These are **conditional accepted-cell summaries**, not an all-grid average benefit. There were 85 actually sampled holdout cells; the worst lower bound across all 85 was −166.27%, and rejected/noisy cells remain retained. The other 35 cells did not establish calibration advantage and did not consume a fabricated holdout. Every interval is within one process; frames are not pooled into a cross-process confidence interval.

The five envelope processes took approximately 119–129 seconds each. Axes were 4,096/65,536 records, lifetimes 1/16/256, real cold-field passes every 1/8 ticks, and actual Job worker counts 1/7, with FrameFaithful execution and batches 64/256. Rotation's four components and Category are accessed in layout-owned storage. Logical hot/cold ratios 1.4/11.2 describe demanded fields, not physical bandwidth or cache state. The preregistration and declaration retain all seeds, budgets and thresholds.

## Implemented scope and API changes

- ParticleIntegrate and TransformExport use generated production storage and codecs with concrete AOT boundary jobs. The generator additionally supports packed float4 AoSoA4/8/16 and a 64-byte padded record stride. It does not promise 64-byte base alignment or rewrite mathematical kernels.
- The crossed particle matrix declares 360 default combinations, implements 120, and retains reasons for 240 unsupported combinations. Formal batches 64/256 use 60 candidates across two execution policies, or 30 FrameFaithful candidates per envelope cell. Historical default particle candidates remain the original 32.
- Envelope calibration snapshots settings and persists the frozen winner before independent input/timing confirmation. Holdout can reject; it cannot rerank the candidate set.
- Search independently runs quick evaluation, planning, full adaptive calibration and full exhaustive calibration on identical frozen candidates. Common pilot cost is charged to both; quick/planning/persistence costs are included; both choices are persisted before either holdout. Exhaustive results are audit-only and cannot retune the adaptive run.
- `WindowsProcessCycleCounterProvider` supplies optional `QueryProcessCycleTime` observations with exact uint64 endpoints. It changes no permissions or OS trace session. The new spatial-neighborhood and animation-state workloads each add eight generated AoS/SoA candidates; the registry totals 64.
- `IManagedAllocationCounter` and the nonserialized `CalibrationRunSettings.AllocationCounter` permit validated runtime measurement without adding Unity references to the portable core. Profiles and raw envelope phases record `ManagedAllocationMeasurement`. Unity callers must inject a supported counter; the unsupported .NET default now rejects its positive control.

Sample migration is documented in [generated production storage](GENERATED_PRODUCTION_STORAGE.md). In particular, the removed public `ParticleAoSoA8Block` is replaced by `ParticleRecordGeneratedPackedAoSoA8Block`; facade NativeArray members are borrowed-view properties and Count is read-only. Owners allocate/dispose once; borrowed views must not be disposed independently. These unreleased sample API changes are explicit rather than silent source compatibility claims.

## Allocation evidence correction

The first integrated Mono Player proved that `GC.GetAllocatedBytesForCurrentThread` returned zero even for a retained 4,096-byte array. The next attempt proved the Release `GC.Alloc` recorder was unavailable. Neither zero was accepted. A small Win64 native plugin now registers a separate Mono/IL2CPP allocation profiler callback and counts current-thread events and object-size bytes. It leaves existing profiler registrations in place, uses thread-local counters, and pins its callback library until Player exit.

The same non-inlined array/object/string allocation callsite executes before registration and is then checked inside the measurement window. Positive controls recorded four events and 4,273 bytes on Mono / 4,277 bytes on IL2CPP, followed by zero-event empty/reset controls. Controls are repeated around actual workload measurement. The integrated generated workloads passed 1,056 cases per backend with zero ingress, execute and export allocation events, full parity, reset and disposal checks. Ordinary calibration, envelope, search and counter diagnostics use the same validated seam. Native allocation and unobserved worker-thread allocations are outside this main-thread measure; required Burst AOT job entrypoints are independently checked.

Historical Unity zero-allocation fields are consequently **unverified observations**. Their raw artifacts, selected decisions and hashes remain unchanged. See [the historical allocation limitation](evidence/HISTORICAL_ALLOCATION_MEASUREMENT_LIMIT.md). The old five-process timing hierarchy replays byte-for-byte, but cannot repair that old acceptance gate.

The historical policy timing estimate is 83.4249% improvement with a 95% process/paired-block interval [83.1251%, 84.0825%]. It weights processes equally and permits the selected candidate/tuned AoS to vary, so it estimates the recorded selection policy rather than one fixed candidate. It is not a new device-population interval, and its workload/count/lifetime differ from this bounded experiment.

## Factor controls and diagnostic cost

[The analysis summary](evidence/optimization-vnext-2026-09-07/analysis-summary.json) includes matched layout contrasts, layout × scalar-kernel interactions, scalar branch form, batch and execution contrasts. It first averages matched log ratios within each process and then reports descriptive median/min/max across five processes. These calibration-only contrasts neither replace final decisions nor claim multiplicity-adjusted or held-out factor effects. They use `particle-integrate-v2`, where cold fields are boundary work; the envelope uses separate scenarios with actual resident cold passes. Equal numeric hot/cold ratios do not make those workloads interchangeable.

For example, FrameFaithful AoSoA4 versus matched scalar AoS had median improvements of 36.44% with the branched kernel and 37.38% with the branchless kernel; SoA's corresponding medians were −0.03% and 8.12%. The padded64 scalar controls had negative median contrasts. This is evidence that kernel/layout conditions matter, not proof that a padded stride or a particular SIMD instruction always wins. Execution comparisons also include the recorded pilot-sized measurement block.

Full-adapter overhead includes Probe/Begin/Complete, validation and raw endpoint persistence, not just the native OS call. Median paired overhead percentages across candidates were:

| Workload | Median full-adapter overhead |
| --- | ---: |
| ParticleIntegrate | 473.31% |
| TransformExport | 286.42% |
| SpatialNeighborhood | 26.60% |
| AnimationState | 124.91% |

The provider therefore stays opt-in and does not participate in layout selection. Process cycles include all Unity/job/background threads and user/kernel accounting. Retired instructions, cache references/misses and branch instructions/misses are unavailable with no numeric value. No frequency, per-job attribution, PMU or causal cache-mechanism claim is made.

## Validation and identity

| Check | Result |
| --- | --- |
| Final Unity 6000.5.3f1 EditMode on measurement source | 198/198 passed, 0 skipped |
| Final portable scientific tests | 69/69 passed |
| Source generator | 14/14 passed; packaged generator binary retained |
| Python renderer / evidence / envelope / counters | 25/34/6/9 passed, 74 total |
| Mono and IL2CPP non-Development Release builds | Required original, expanded, cold-pass, ISA and additional-workload Burst AOT entrypoints passed |
| Actual generated production workloads | 1,056/1,056 on each backend |
| Expanded boundary/tail matrix | 2,166 checks on each backend |
| Final IL2CPP registry suite | 64 candidate results across four workloads; actual canonical parity and validated zero allocation |
| Envelope / search / counters | 120 cells / 10 comparisons / 768 captures independently audited |
| Final binary audit | All 71 manifest-bound files matched |

Final formal runtime source: `4ffa47271306e985d9cade5d77489bd172c0360f`. Documentation, analysis and evidence commits descend from that source; the local merge tip is reported separately in the handoff.

| Identity | SHA-256 |
| --- | --- |
| Build identity receipt | `189C5A180C3D982A44243B4629F7998BFF08B646CC1694EBC89D294F2851B2DC` |
| GameAssembly.dll | `93DCA08409BFBE1251949CFD9A3B20443BE79D029593F0A4140F6B7003F35689` |
| lib_burst_generated.dll | `1B7B01FA220ABC8001701D4025F9C85E77A8A29E3047C7145AB3F59C90500E1E` |
| DlcAllocationProfiler.dll | `1C4D75D854AB8B298194232F3DEF86C6E8C031579AD67FCEBE176051ACBF4234` |

The observed CPU brand is Ryzen 9 9950X, while Windows exposes eight logical processors and Unity reports a maximum of seven Job workers. The worker axis was corrected from 1/8 to 1/7 before formal measurement. Executed Burst probes reported SSE2/AVX2 capability; they do not attest every kernel's instruction mix. Resolved packages are Burst 1.8.29, Collections 6.5.0 and Mathematics 1.4.0; stripped runtime assembly-version fields are not substituted for package-lock identity. Native allocation adapter compiler identity is MSVC 19.51.36248, with retained `/Bv` output and source/binary hashes.

All Unity work, heavy builds and formal timing held the shared serialization mutex. User applications, Balanced power policy, clocks, thermals, affinity and caches remained uncontrolled; before/after snapshots are retained and may miss transient interference. Five processes are not five devices. No administrator elevation, global cache deletion, user-app termination, push, tag or release was performed by this integration task.

## Retention and remaining limits

[The evidence directory](evidence/optimization-vnext-2026-09-07/README.md) contains the complete byte-verified raw archive, file manifest, final summaries, build identity and replay instructions. Superseded source/binary attempts remain identified separately, including the unsupported allocation API, unavailable Release marker, empty-holdout wrapper rejection and IL2CPP `Process.MainModule` failure. The latter was fixed using the actual launch executable path and a run identity that does not depend on unsupported module/start-time enumeration.

There are no unexecuted bounded measurement gates in this integration. Optimization acceptance remains conditional: adaptive's regret gate failed, counters have material observer overhead, and exact envelope winners vary. Multi-device/ISA inference, PMU mechanisms, unsampled envelope interpolation, workload-general guarantees and release qualification remain future scope. Existing ordinary exhaustive selection, strict fingerprints and AoS fallback remain the production behavior.
