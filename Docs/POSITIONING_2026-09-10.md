# Lifetime-aware layout adoption — 2026-09-10

The product's adoption question is whether a concrete layout/kernel repays its
complete cost within the application's actual lifetime and export cadence,
with equivalent outputs, adequate allocation coverage and independent confirmation.
This update makes that question usable through a compiled example and consistent
documentation. It introduces no algorithm, candidate promotion or performance result.
Baseline: `1e7141a9b41d8415951270e8f1bbf9a3374905d3`, on the existing isolated
`codex/repair-20260908-layout-integration` branch.

## Changes delivered

- Root and package READMEs, the portfolio entry and the existing roadmap point to
  [the adoption guide](LAYOUT_ADOPTION.md). Historical figure values and decisions
  remain tied to their original sources.
- [The console example](../Tools/Examples/LifetimeDecision/README.md) links the real
  portable Runtime sources and calls `ConservativeLifetimeEnvelope.Estimate`.
  It exercises absent evidence, complete synthetic costs, and incomplete boundary
  costs; it checks the result origin, process association and ordered finite bounds.
- Corrected the fictional input fingerprint to uppercase hexadecimal, as required
  by `CandidateDefinitionProtocol.IsCanonicalSha256`. The previous lowercase value
  would reject the nominally complete example as Unknown. Format validity still
  does not make this invented string deployment provenance.
- Added the example project to the `.gitignore` exceptions so a checkout includes
  its build definition. It targets `net10.0`, has no third-party package references
  and does not change the Unity package's target or dependencies.
- [The host example](../Tools/Examples/LifetimeDecision/CalibrationHostExample.cs)
  compiles in the same project. It binds a caller-supplied, intact full fingerprint
  and required allocation scope, injects the caller's capability-aware counter, and
  calls the real calibration engine with a real scenario factory. It contains no
  algorithm copy or empty implementation. The console program never calls this
  measurement method; this pass validated its compilation only.
- Replaced incomplete package README plugin stubs and the unbound measurement
  invocation with actual interface references and the compiled host entry. AOT
  registration now names the existing `ParticleIntegrateScenarioFactory` and links
  the real host registration file.
- Updated [the calibration contract](CALIBRATION_CONTRACT.md) and package README
  to distinguish a component-quantile selection score from per-tick and complete
  lifecycle percentiles, and to require positive controls, adequate allocation
  scope and complete observation windows.

## Cost boundary and evidence semantics

The example assumes **one terminal export and no recurring exports**. Its invented
one-time costs explicitly sum construction, ingress, that export and disposal.
The resident score covers resident ticks under this assumption. The estimator
has no export-cadence parameter and cannot infer cadence from the completeness
flag or the fingerprint.

Every-frame or periodic exports require observations of the actual coupled work
and a matching workload/settings identity. A constant per-tick model derived
from complete cadence periods does not automatically cover different terminal
phases or arbitrary lifetimes. Avoid double-counting exports in both cost terms;
retain Unknown when a supported mapping is missing. A sum of component P95s is
neither an observed whole-lifecycle P95 nor a guaranteed bound on it.

All example inputs are synthetic, including process IDs and the fingerprint.
The estimator's completeness flag is a caller assertion, not verification of
workload correctness, allocation eligibility, independent holdout or deployment
compatibility. The JSON output explicitly records synthetic/Unknown origin,
the export assumption, no emitted profile and no established allocation eligibility.

## Validation executed

Host: existing .NET SDK `10.0.302`, runtime `10.0.10`. Only this example project
was restored and incrementally compiled; no dependency or runtime was downloaded.

```powershell
dotnet restore Tools/Examples/LifetimeDecision/LifetimeDecision.csproj --source C:/Users/EdwinLiu/.nuget/packages -p:NuGetAudit=false --disable-parallel --nologo
dotnet build Tools/Examples/LifetimeDecision/LifetimeDecision.csproj -c Release --no-restore --nologo -maxcpucount:1 -nodeReuse:false
dotnet Tools/Examples/LifetimeDecision/bin/Release/net10.0/LifetimeDecision.dll
```

| Check | Result |
| --- | --- |
| Restore using only the existing local package cache and installed SDK packs | Passed |
| Example, host entry and linked portable Runtime compilation | Passed; 0 warnings, 0 errors |
| Missing cost evidence | `Unknown`, no finite bounds |
| Complete synthetic paired costs | `FiniteSustainedBreakEven`; `SyntheticFixture` origin, five synthetic pairs, ordered positive bounds |
| Missing boundary-cost completeness | `Unknown`, no finite bounds |
| Repository metadata and local Markdown links | Passed; 62 JSON/asmdef files and 52 Markdown files |
| Staged whitespace and scope checks | Passed; only adoption/example documentation, example sources/project and the project ignore exception |
| Runtime, kernels, source generator and historical evidence compared with baseline | Unchanged |

These are deterministic API correctness checks, not workload measurements.
The unchanged 82-test protocol suite was not rerun; its earlier receipt is retained
in [the integration report](MEASUREMENT_REPAIR_2026-09-08.md).

## Remaining validation and preserved work

No Unity/Player/GPU/native warmup, benchmark, calibration, real counter acquisition,
full rebuild or package creation ran. The measurement host method's actual execution,
Unity/Burst/IL2CPP behavior, real periodic-export lifecycles and allocation coverage
remain unvalidated for this source. Current performance remains Unmeasured.

The September 9 ENOSPC import failure and native adapter build failure are retained
under `Artifacts/actual-20260909/`. The untracked `Tools/ActualComparison/` work and
its logs are preserved and excluded from this commit. No cache, old worktree,
original evidence or user data was deleted. The performance queue remains paused.
