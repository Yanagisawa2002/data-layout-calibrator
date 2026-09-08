# Retained optimization integration evidence

All bounded experiments completed on source `4ffa47271306e985d9cade5d77489bd172c0360f`.
See [the engineering report](../../OPTIMIZATION_VNEXT_REPORT_2026-09-07.md) for conclusions,
compatibility changes, negative findings and limits.

- [Full raw archive](integration-raw.zip): 2,139 byte-verified files, 120,354,831 raw bytes,
  10,040,346 compressed bytes. Includes successful, superseded and failed retained attempts.
- [Archive/file SHA-256 manifest](archive-manifest.json) and [relocated replay verification](retention-verification.json).
- [Envelope decisions and interval/break-even details](envelope-summary.json).
- [Search integrity/cost/regret replay](search-replay.json).
- [Descriptive results and matched factor contrasts](analysis-summary.json).
- [Final build identity](build-identity.json), [binary audit](binary-audit.json),
  and [executed grid declaration](grid-declaration.json).

Archive SHA-256: `352E1DE751FA7AD3686E0FCEA466F8E0C0519FFAB078314711E9FB598273FD5F`.

## Accepted final paths inside the archive

| Gate | Archive path |
| --- | --- |
| Final 198/198 Unity tests | `editmode-final/` |
| Final 69/69 scientific and 74 Python tests | `tests-final/` |
| Generator 14/14 | `tests/generator.trx` |
| Mono build / generated / matrix | `mono-build-attempt-02/`, `generated-player-attempt-02/`, `mono-matrix-attempt-02/` |
| Mono actual 64-candidate correctness/allocation | `mono-suite-attempt-02/`; see `corrected-receipt-audit.json` for the wrapper-only rejection |
| Final IL2CPP build / generated / matrix | `il2cpp-prepare-attempt-02/`, `generated-il2cpp-attempt-02/`, `il2cpp-matrix-attempt-02/` |
| Final four-workload formal registry suite | `il2cpp-suite-formal-attempt-02/` |
| Actual cycles and paired overhead | `counter-formal-attempt-02/` |
| Explicit missing-provider fallback | `counter-no-provider-attempt-01/` |
| Five-process search comparison | `search-formal-attempt-01/` |
| Five-process 24-cell envelope | `envelope-formal-attempt-01/` |

The archive also contains superseded builds/suites and failures; their existence does
not add successful replications to the final experiments. Source and binary identities
in each receipt distinguish them. The worker's native allocation compilation and original
counter failures are additionally retained in
[the allocation correction evidence](../allocation-counter-correction-2026-09-07/summary.json).
Player/GameAssembly/UnityPlayer binaries remain in the ignored local Builds directory;
the archive retains their identities and logs, not redistributable Player binaries.
The native allocation adapter DLL is retained as source-controlled benchmark instrumentation.
Builds configured the scripting backend explicitly; the build-time source identity records
that ProjectSettings override, which was restored afterwards without changing binaries.

## Offline replay

From the repository root, choose a fresh extraction/output directory:

```powershell
python Tools/Integration/archive_evidence.py --verify Docs/evidence/optimization-vnext-2026-09-07/archive-manifest.json
python -m zipfile -e Docs/evidence/optimization-vnext-2026-09-07/integration-raw.zip Artifacts/integration-replay
python Tools/Envelope/summarize_envelope.py Artifacts/integration-replay/envelope-formal-attempt-01 Artifacts/envelope-replayed.json
python Tools/EvidenceLab/search_replay.py Artifacts/integration-replay/search-formal-attempt-01 --output Artifacts/search-replayed.json
python Tools/CpuCounters/validate_counter_evidence.py Artifacts/integration-replay/counter-formal-attempt-02/cpu-counter-evidence.json
python Tools/CpuCounters/validate_counter_evidence.py Artifacts/integration-replay/counter-no-provider-attempt-01/cpu-counter-evidence.json --allow-unavailable
python Tools/Integration/analyze_layout_evidence.py Artifacts/integration-replay Artifacts/analysis-replayed.json
python Tools/EvidenceLab/process_hierarchy.py Docs/evidence/vnext-formal-il2cpp-2026-09-02/formal-run-manifest.json --output Artifacts/historical-policy-replayed.json
```

Hash checks preserve source bytes; they do not convert observed correlation to causal
hardware evidence, repair historical allocation observations, or make adaptive's failed
regret gate pass. Replays may report their new filesystem location in descriptive output;
the referenced raw artifact hashes remain identical.

## Fresh execution

Use the build/player helpers under the shared serialization runner, with fresh output
directories. `Tools/Envelope/Invoke-EnvelopeGrid.ps1 -PrepareOnly` freezes a clean current
source/build/declaration; then `-UseExistingBuild` verifies its identity before the five-run
grid. The search helper takes the actual matrix receipt and the same build identity.
The 2026-09-07 [preregistered protocol](../OPTIMIZATION_VNEXT_PROTOCOL_2026-09-07.md)
retains exact counts, warmup, samples, confidence, effect threshold and method order.
Do not label a newly built binary as the retained measured build merely because its
source revision matches; binaries and actual new runs must be recorded separately.
