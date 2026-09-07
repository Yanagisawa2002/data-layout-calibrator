# Measured envelope grid v1

This is an executable protocol, not a claim that the new grid has been measured.
Formal completion requires the integrated generator and expanded matrix, a successful
Release IL2CPP/Burst AOT build, five fresh Player processes and 120 retained cell
decisions. Worker implementation/tests do not satisfy those gates.

The frozen grid has counts 4,096 and 65,536; lifetime amortization contexts 1, 16
and 256 ticks; cold passes every 1 and 8 ticks; and Job System worker counts 1
and 7. Each process runs all 24 cells. Each cell measures all 30 expanded
FrameFaithful candidates: six layouts crossed with scalar branched/branchless
kernels and batches 64/256, plus the matching packed kernel for AoSoA4/8/16.
The fastest valid measured AoS becomes the reference; all AoS controls remain in
the raw evidence. Historical default candidates cannot satisfy the declaration.
The Player starts with `-job-worker-count 7` and verifies the entire worker axis
against JobWorkerMaximumCount before any cell is measured. Receipts retain startup
worker count, maximum and OS-exposed logical processors. This host currently exposes
8 logical processors despite its 9950X brand; the 7-worker point shares the exposed processors
with the main thread and user applications. No full-chip core-count
capacity is inferred from the processor name.

Cold work reads and writes all four Rotation components and Category in the
candidate's actual persistent layout storage. The separately scheduled pass is
included in resident timing. Its cadence survives partial Execute calls and resets
on ingress. The logical hot/cold byte ratios are 28/(20/period), or 1.4 and 11.2;
these describe demanded field work, not physical bytes transferred, bandwidth,
or cache-cold state. The cold mutation is a deterministic field-update workload,
not a quaternion-normalized physical rotation simulation. The default scenario
remains unchanged; each new access mode has its own scenario contract ID.

Costs use the existing scientific estimator: resident component P95 plus full
ingress and export component P95 divided by lifetime. Each cell is newly sampled.
The lifetime axis is an amortization context, not the P95 of directly measured
finite-lifetime transactions; the sum of component P95s is not claimed to equal
transaction P95. No unmeasured integer lifetime is silently interpolated.

The policy retains 40 paired resident blocks, 20 ingress and export blocks, 4,000
aligned bootstrap replicates, 95% intervals, and a 10% point improvement gate
with confidence lower bound above zero. Warmup is 32 blocks/minimum 0.1 seconds;
target block duration is 2 ms, capped at 256 ticks. Actual ticks and warmup are
retained. A block's candidate order uses the scientific balanced Latin square.
Estimated grid duration is 15Ã¢â‚¬â€œ45 minutes on this Ryzen 9 9950X, excluding build;
actual durations may exceed that under application interference.

For process p in 1..5 and cell c in 0..23, offset=p*1000+c. Calibration seed is
0xA5110000+offset, holdout seed 0xD84F0000+offset, order seed 0xA3410000+offset,
and bootstrap seed 0xB5290000+offset. Holdout uses the same element count, a fresh
dataset seed and fresh timing samples. The calibration-only engine freezes one
winner and writes its artifact with CreateNew before measuring only that winner
and its tuned AoS reference. Holdout never nominates a replacement. Cells without
a credible calibration winner explicitly record that holdout was not measured.

Raw phase arrays, block IDs, positions, full descriptors, dataset hashes, frozen
decisions, component/break-even intervals and seeds are retained. Derived 4,000
draw arrays are transient and reproducible, avoiding multi-gigabyte duplication.
The pure core accepts an artifact codec; it keeps its engine-free assembly.

Run after integration and a clean local commit, using the coordinator's shared lock:

```powershell
& 'C:/path/to/Invoke-SerializedValidation.ps1' -Action {
  & ./Tools/Envelope/Invoke-EnvelopeGrid.ps1 `
    -UnityPath 'C:/Program Files/Unity/Hub/Editor/6000.5.3f1/Editor/Unity.exe' `
    -OutputDirectory 'C:/path/to/new-envelope-evidence' -LockAlreadyHeld
}
python Tools/Envelope/summarize_envelope.py C:/path/to/new-envelope-evidence C:/path/to/new-summary.json
python -m unittest discover Tools/Envelope/tests -v
```

Without an outer coordinator lock, pass `-ValidationLockScript` instead. All
children are awaited inside the lock. The script generates the full declaration
through the integrated matrix's Editor API, builds Release IL2CPP and verifies a
Burst AOT DLL exists. Reflection is confined to Editor declaration export;
Player factories, hot jobs and cold jobs have concrete AOT scheduling sites.
Output directories and artifact files cannot be overwritten. Failed process
runs retain logs/receipts and the script continues the remaining declared runs.

To verify the same binary before measuring, first call the script with
`-PrepareOnly` into a new preparation directory. Run the integrated generated,
matrix and counter correctness probes against that produced Player. Then call
it into another new directory with `-UseExistingBuild -ExistingBuildIdentity
<preparation>/build-identity.json -DeclarationPath <preparation>/grid-declaration.json`.
This skips rebuilding and validates current source commit, package lock, Unity,
declaration and every actual binary hash against the preserved preparation
identity. Keep the preparation directory (including its compiler log) with the
formal evidence. Both calls require the shared lock. Start-Process -Wait keeps
the process tree inside its lifetime.

The build identity hashes actual Player/Burst/native/metadata binaries, Unity,
package lock, compiler build log and source commit. Player receipts capture CPU,
OS, Unity/assembly versions, actual worker counts in cell axes and a separately
executed Burst SSE2/AVX2 dispatch probe. The probe is not an instruction audit of
every kernel. Raw binary/compiler artifacts remain necessary for exact auditing.
No device claim is inferred from the GPU. Clocks, caches, affinity, thermal state
and user application interference remain uncontrolled and are recorded as limits.
Read-only process ID/name/cumulative CPU/start-time and active power-plan snapshots
surround each process. Clock and thermal readings are explicitly unavailable;
the snapshots cannot exclude short interference during a run.

The summary copies fixed decisions, reports credible/gray/fallback/rejected
coverage over sampled points and five-process winner repeatability. It preserves
each process's paired-block confidence intervals; it does not pool frame samples
or manufacture a cross-process interval. Search's independent process-hierarchy
analysis has its own estimator and artifacts. All unmeasured workloads, execution
topologies, devices, worker counts, sizes and access frequencies remain explicit.
