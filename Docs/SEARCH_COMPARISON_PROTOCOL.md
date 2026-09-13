# Adaptive search and retained process replay

The new search runner measures adaptive elimination and exhaustive calibration
independently on the same frozen candidate descriptors and deterministic input.
It is opt-in. Ordinary suite execution, production defaults, historical raw files
and the preregistered `run-01` primary result are unchanged.

## Executable comparison

`ScenarioCalibrationEngine.RunSearchComparison(factory, settings, frozenCandidates,
axis, environmentFingerprint, adaptiveFirst, persist)` executes one scenario and
one execution-policy cell. `persist(name, object)` must create a new artifact and
return its actual uppercase file SHA-256. Settings and the input descriptor array
are copied at entry. Every persistence callback is guarded by a deterministic
public-field integrity fingerprint; payload mutation is rejected before execution
continues. This internal fingerprint is distinct from the actual file hash.

The common preflight and reference pilot determine ticks and warmup before either
method. Both methods are charged this measured shared cost and use identical
ticks/warmup. Adaptive cost includes independently sampled quick calibration,
quick evidence persistence, paired bootstrap/Pareto planning, plan persistence,
and independent full finalist calibration/selection/persistence. Exhaustive cost
includes independent full-pool calibration/selection/persistence. Wall durations
come from `Stopwatch`; no planned sample savings are presented as elapsed time.
The common final freeze checkpoint, audit, environment/input persistence and
holdout are outside the calibration-cost comparison; holdout durations are
reported separately. Per-candidate timing excludes serialization as before.

Actual evaluation count is resident + ingress + export sample units. It excludes
preflight, warmup, bootstrap draws and ticks within one resident sample; those are
included in the corresponding observed wall cost. The executed shortlist retains
**all** AoS controls even if the quick plan would eliminate one. This conservative
override is explicit in `ExecutedFinalistIds`; planned savings in the original
plan are not the executed count.

Both full calibration decisions and all raw arrays are persisted before any
holdout input is generated. The same existing `LayoutSelector` significance,
practical-improvement, parity and allocation gates apply to both methods. Holdout
only compares each frozen winner against its tuned AoS. A calibration fallback
does not invent a holdout pair. Exhaustive results are used only for the final
audit; they never feed the quick plan or adaptive choice. The report separates
best-in-shortlist oracle regret from the regret of the actual independently
selected adaptive result, evaluated on the exhaustive raw costs. This oracle is
an empirical calibration estimate, not an unknowable population optimum.

The additive `ScientificAdvantageEnvelopeAdapter.CreateCalibrationCell` optional
last argument `tunedBaselineCandidateId` supports full pools with multiple AoS
controls while preserving their original descriptor hashes. It requires an AoS
ID present in the pool. Omitting it retains the previous exactly-one-baseline
validation.

## Preregistered bounded formal experiment

The integration task owns execution after generator/matrix/counters merge and
Release IL2CPP/Burst AOT build. Required design:

| Dimension | Frozen value |
|---|---|
| Workload | ParticleIntegrate default access contract |
| Calibration / holdout records | 65,536 / 65,539 |
| Calibration / holdout input seeds | Existing distinct `ParticleDataSet` constants, saved in settings |
| Layout pool | Expanded matrix: AoS, SoA, AoSoA4/8/16, AoSPadded64; matched scalar and supported packed kernels |
| Batches | 64, 256; preserve every corresponding tuned AoS control |
| Execution | FrameFaithful and DependencyChain, independent frozen cells |
| Lifetime / workers | 256 ticks / 7 actual Unity Job workers |
| Hot/cold ratio | Default Particle contract: 28 resident hot bytes / 20 boundary-preserved cold bytes |
| Quick | 6 resident, 4 ingress, 4 export; 200 bootstrap draws |
| Full and holdout | 40 resident, 20 ingress, 20 export; 4,000 draws, 95% confidence |
| Practical / regret gates | 10% improvement / maximum 1% empirical actual-selection regret |
| Pilot | Target 2 ms, max 64 ticks; 4 warmup blocks, minimum 0.05 s |
| Repetition | Five fresh processes; adaptive-first on 1/3/5, exhaustive-first on 2/4 |
| Within-process ordering | Existing balanced blocks with saved fixed order and bootstrap seeds |

Export the matrix descriptor JSON from the integrated Player with
`-dla-matrix-probe -dla-matrix-probe-batches 64,256 -dla-matrix-receipt <candidate.json>`.
The wrapper must contain `CandidateSetSha256` and `Candidates`; additional probe
fields are allowed. The search runner verifies the full pool hash and computes
separate hashes after filtering each execution cell. This file is frozen before
the five launches; no outcomes may change its pool, budgets, seeds or method order.

Run in PowerShell 7 from the integrated repository:

```powershell
& ./Tools/EvidenceLab/Invoke-SearchComparison.ps1 `
  -Player '<integrated Release IL2CPP Player.exe>' `
  -CandidateFile '<frozen matrix descriptor JSON>' `
  -OutputRoot '<new retained search directory>' `
  -SourceCommit '<full integrated commit SHA>' `
  -BuildReceipt '<actual build/compiler/source receipt>' `
  -ValidationLockScript '<control>/Invoke-SerializedValidation.ps1'
python Tools/EvidenceLab/search_replay.py '<retained search directory>' --output '<new replay JSON>'
```

The runner writes preregistration before measurement, hashes actual Player DLLs,
executables and metadata, records actual host CPU/ISA capability (not emitted
Burst ISA), holds the shared validation lock across all processes, and retains
failed launch receipts. It neither edits caches nor changes power plans or other
apps. The build receipt must contain `sourceCommit` and `binaries: [{path, sha256}]`;
the script rejects a different commit or any actual binary absent from that table.
Source identity remains an integrator build declaration; hashing a binary alone
is not source attestation. Each launch also retains hashed before/after process
CPU/start-time and active-power-plan snapshots to expose interference. The default hard bound
is 900 seconds per Player, five Players, excluding builds and lock waits. This is
a timeout budget, not an observed runtime prediction.

`search_replay.py` validates the five process identities, Release/Burst/worker
receipts, method ordering, raw component costs, paired block completeness, actual
linked file hashes, original descriptors, common pilot, full/holdout sample
budgets, frozen selections, sample counts, wall-cost sums and both regret values.
Valid evidence may show no speedup, missed winners or rejected holdout. Replay
integrity success does not turn those outcomes into a performance acceptance.
Paired interval numerical inference remains covered by the scientific core tests;
the Python search replay checks retained interval settings and decision gates.

## Historical five-process selection-policy summary

The five retained runs select different tuned baselines, batch sizes and execution
policies. The strict runtime fixed-candidate hierarchy API correctly rejects that
combination. The new replay instead estimates the **frozen calibration-selection
procedure** on its untouched holdout, with equal process weight. It resamples
processes first, then paired blocks separately within resident/ingress/export;
baseline and candidate always share each block draw. It never pools all samples
as independent processes or devices.

```powershell
python Tools/EvidenceLab/process_hierarchy.py `
  Docs/evidence/vnext-formal-il2cpp-2026-09-02/formal-run-manifest.json `
  --output '<new summary JSON>' --iterations 4000 --seed 0xC2B2AE35
```

The retained [summary](evidence/vnext-formal-il2cpp-2026-09-02/process-policy-hierarchy-summary.json)
rehashes every raw suite and preflight and binds the analysis script hash. Its
point improvement is 83.4249%, with a 95% hierarchical interval [83.1251%, 84.0825%].
This is a post-hoc robustness analysis on one physical device; it does not replace
the primary run, establish a fixed-candidate effect, remove external interference,
or show cross-device performance. The negative-control TransformExport fallback
has no measured holdout; no interval is synthesized for it. Historical binary
hashes are explicitly copied provenance because the binaries are not retained in
this evidence directory.

## Validation and remaining gates

```powershell
python -m unittest discover -s Tools/EvidenceLab/tests -v
dotnet test Tools/ScientificTests/ScientificTests.csproj -c Release
```

The pure core fixture tests are synthetic control-flow/integrity tests, never
Unity performance evidence. Required final gates remain: integrated matrix hash
freeze; actual Release IL2CPP/Burst runner compilation and smoke; five paired
formal process launches; raw comparison replay; explicit measured time/count,
missed-winner/regret and holdout outcomes. Until these run, the new search
comparison is ready for integration, not measured complete.
