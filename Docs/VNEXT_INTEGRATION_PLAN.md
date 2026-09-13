# v0.4-v0.6 integration and release-gate plan

Status: integration candidate validated; release gates remain open

Date: 2026-09-02

Authoritative scope: [`ROADMAP_V0.4_TO_V0.6.md`](ROADMAP_V0.4_TO_V0.6.md)

Integration baseline: `644893990ed18e56619da8d2737e6b7592eb6080`

Integration branch: `codex/vnext-05-integration`

This plan coordinates implementation and verification; it does not expand the
roadmap or make a release claim. The repository remains proprietary and All
Rights Reserved under [`LICENSE`](../LICENSE), with the provenance boundary in
[`PROVENANCE.md`](../PROVENANCE.md). Historical evidence remains immutable.
No Editor test, synthetic fixture, repeated process, or checked-in artifact is
treated as new Unity Player, hardware-counter, ISA, device, or cross-device
evidence.

## Baseline inventory at `6448939`

| Surface | Current responsibility | Primary verification |
| --- | --- | --- |
| `Runtime/BenchmarkModels.cs` | schema-2 suite, scenario, candidate, result, decision, and environment DTOs | Unity EditMode JSON round trip |
| `Runtime/CalibrationProtocols.cs` | workload-neutral scenario, candidate, boundary, and parity interfaces | core reflection and Sample parity tests |
| `Runtime/ScenarioCalibrationEngine.cs` | preflight, randomized per-round measurement, allocation checks, calibration, and isolated holdout | Unity EditMode plus Release Player runs |
| `Runtime/BenchmarkStatistics.cs` and `LayoutSelection.cs` | descriptive statistics, independent composite-P95 bootstrap, threshold/tie logic, and tuned-AoS fallback | deterministic Unity EditMode tests |
| `Samples/ParticleIntegrate` | AoS, SoA, and AoSoA8 positive workload | awkward-tail parity and Player Burst AOT |
| `Samples/TransformExport` | AoS/SoA output-heavy negative control | parity and Player Burst AOT |
| `SourceGenerators~` / `SourceGenerators` | source and packaged DLL for direct-construction factory registration only | .NET generator tests and Unity compilation |
| `BenchmarkProject` | Unity 6000.5.3f1 Release Player build, AOT entrypoint check, run configuration, and artifact writer | EditMode, Mono Burst AOT, IL2CPP Burst AOT |
| `Tools/ResultRenderer` | read-only schema-2 heatmap/GIF/manifest generation from `FinalDecision` | Python contract tests |
| `Docs/evidence` | immutable raw suites and explicitly scoped same-device evidence | hashes, replay, and provenance review |

The current fixed suite schema is version 2. Its stable joins are
`ScenarioId`, `ContractVersion`, and `CandidateId`; the current candidate also
stores `LayoutId` and `LogicalBatchSize`. A renderer may copy a frozen decision
but may not become another selector.

## Ordered integration record

Roadmap order is a dependency order, including when implementation work occurs
in parallel.

| Order | Incoming immutable tip | Merge commit | Gate before next merge |
| --- | --- | --- | --- |
| 1 | `codex/v04-scientific-core` at `5cec65f` | `dfe240d` | Unity 64/64, renderer 13/13, generator 4/4 |
| 2 | `codex/advantage-envelope` at `13c8ebf` | `f0a1303` | combined Unity 83/83, renderer 22/22 |
| 3 | `codex/v05-generator-profiles` at `5639b9f` | `dd0a390` | generator 11/11, combined Unity 109/109; schema-3 `Regression` compatibility fixed in `b23cfeb` |
| 4 | `codex/v06-evidence-lab` at `7812d02` | `9c398aa` | combined Unity 133/133, Evidence Lab 22/22, manifest valid with 0 ready / 18 blocked |

Merge and review order was scientific core, advantage engine,
generator/profile work, then evidence lab. Each merge was pushed only after the
listed affected-layer gate passed. Integration-owned candidate/uncertainty/artifact
wiring followed in `135b90d` and passed Unity 139/139 plus renderer 25/25.

## Shared protocol freeze

- Checked-in schema-2 evidence is never rewritten. New model families are
  explicitly versioned, and a migration must preserve historical meaning.
- `CandidateId` is the canonical cross-artifact candidate key. Factor fields,
  envelope cells, fingerprints, and counters carry explicit stable IDs rather
  than parsing `DisplayName`.
- Scenario identity is `ScenarioId` plus `ContractVersion`. Reusable profiles
  additionally bind candidate/schema hashes, environment, build, worker, and
  calibration settings.
- `FinalDecision`, and the equivalent frozen envelope decision, remain the only
  decision authority. Presentation and evidence tooling only validate, replay,
  filter, or format them.
- Counter/provider failure is recorded but cannot change a calibration
  decision. Missing, corrupt, incompatible, tied, or insufficient evidence
  safely selects tuned AoS.
- Direct construction, deterministic ordering, and no runtime reflection remain
  AOT requirements. Generated storage never implies generated business kernels
  or compiler optimization.
- Root/package versioning, README, CHANGELOG, ADR, release notes, and historical
  evidence reconciliation are integration-owned to avoid conflicting claims.

[`ADR 0006`](adr/0006-vnext-integration-protocol.md) resolves the suite and
artifact schemas, canonical candidate bytes, uncertainty identifiers, decision
threshold, multiplicity, Pareto, regret, and external-envelope attachment.
Evidence Lab separately freezes process versus physical-device identity; it
does not invent an identity source or claim a configured device.

## Baseline verification

These checks ran from a clean clone at the integration baseline:

| Check | Result |
| --- | --- |
| `dotnet test ...SourceGenerator.Tests.csproj -c Release` | passed, 4/4 |
| `python -m unittest discover Tools/ResultRenderer/tests -v` | passed, 3/3 |
| Unity 6000.5.3f1 `-runTests -testPlatform EditMode` against `BenchmarkProject` | passed, 29/29 |

The initial inventory did not build or execute a Mono or IL2CPP Player and did
not collect performance or counter observations. Those are post-integration
gates, not inferred from the Editor results above.

## Release gate state

- Ordered branch integration: passed at the immutable tips and merge commits
  recorded above.
- Merged deterministic/schema suite: passed, currently Unity 139/139, generator
  11/11, fixed-result renderers 25/25, and Evidence Lab 22/22.
- Windows Mono Release + Burst AOT consumer: passed; required Burst library and
  job entrypoints were present. Tiny schema-3/profile/scaffold Player audit exited
  0 and is behavioral only, not performance evidence.
- Windows IL2CPP Release + Burst AOT consumer: passed after installing the Unity
  6000.5.3f1 Windows IL2CPP module; the required Burst library and job entrypoints
  were present. The tiny schema-3/profile/scaffold audit exited 0.
- Preregistered full-size IL2CPP evidence: passed in five sequential fresh Player
  processes. ParticleIntegrate optimized in 5/5, TransformExport retained tuned
  AoS in 5/5, every candidate passed parity, and measured resident/boundary
  managed allocation was zero. The retained evidence is descriptive across
  processes; no process-hierarchical aggregate CI was computed.
- Fresh fixed-result replay and renderer agreement: passed on the preregistered
  primary IL2CPP suite; the renderer input hash and every copied
  frozen-decision field matched.
- Counter-enabled supported-platform run: pending a real provider and platform.
- Real multi-device/ISA/workload matrix: pending available hardware; no substitute
  evidence will be manufactured.
- README, package documentation, ADR, CHANGELOG, release notes, sensitive-data,
  third-party authorization, and provenance review: passed for this integration
  candidate. Historical evidence and rights files are unchanged; vNext formal
  suites and preflight context are retained separately from the planning-only
  device matrix manifest.
- Merge to `main`, release tag, and GitHub Release: prohibited until every claimed
  gate actually passes and require a separate explicit release action.
