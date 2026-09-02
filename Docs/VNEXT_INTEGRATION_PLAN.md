# v0.4-v0.6 integration and release-gate plan

Status: integration baseline established

Date: 2026-09-02

Authoritative scope: [`ROADMAP_V0.4_TO_V0.6.md`](ROADMAP_V0.4_TO_V0.6.md)

Integration baseline: `644893990ed18e56619da8d2737e6b7592eb6080`

Integration branch: `codex/vnext-integration`

This plan coordinates implementation and verification; it does not expand the
roadmap or make a release claim. The repository remains proprietary and All
Rights Reserved under [`LICENSE`](../LICENSE), with the provenance boundary in
[`PROVENANCE.md`](../PROVENANCE.md). Historical evidence remains immutable.
No Editor test, synthetic fixture, repeated process, or checked-in artifact is
treated as new Unity Player, hardware-counter, ISA, device, or cross-device
evidence.

## Baseline inventory

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

## Ordered integration plan

Roadmap order is a dependency order, including when implementation work occurs
in parallel.

| Order | Roadmap item | Incoming branch | Integration gate before the next item |
| --- | --- | --- | --- |
| 1 | Layout x Kernel x Execution factor split | `codex/v04-scientific-core` | explicit stable factor IDs, branchless AoS negative control, AoSoA variants, canonical ingress/export/parity/lifetime contract, schema migration path |
| 2 | Paired / hierarchical statistics | `codex/v04-scientific-core` | deterministic blocked order and paired resampling tests; process/device levels represented honestly; calibration choices frozen before holdout |
| 3 | Lifetime break-even and advantage envelope | `codex/advantage-envelope` | deterministic break-even CI and immutable envelope whose decisions agree with its serialized cells |
| 4 | Adaptive elimination and Pareto frontier | `codex/advantage-envelope` | elimination audit trail, exhaustive-or-regret equivalence test, and independent finalist holdout |
| 5 | Storage / codec Source Generator scaffolds | `codex/v05-generator-profiles` | bounded versioned schema, diagnostics, deterministic output, two distinct consumers, no reflection or invented business semantics |
| 6 | Profile fingerprint, cache, and resolver | `codex/v05-generator-profiles` | exact-match default, explicit invalidation reasons, corrupt/missing/unsupported tuned-AoS fallback, AOT-safe frozen-profile resolution |
| 7 | Hardware counters and causal evidence levels | `codex/v06-evidence-lab` | optional failure-isolated provider, overhead/control metadata, raw and derived fields separated, no causal wording above the evidence level |
| 8 | Multi-device, multi-ISA, multi-workload validation | `codex/v06-evidence-lab` | device identity and independent-process hierarchy preserved; unsupported real-device matrix remains explicitly pending |

Merge and review order is the table order: scientific core, advantage engine,
generator/profile work, then evidence lab. Each merge is followed by the tests
for the affected layer before the next branch is applied.

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

The integrator will resolve the final suite schema number, canonical hash byte
encoding, process/device observation IDs, and whether envelope/profile/counter
artifacts are embedded or referenced after reviewing all four branch proposals.

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

- Automated baseline: passed for core/Samples EditMode, generator, and renderer.
- Merged deterministic/schema/replay suite: pending worker integration.
- Windows Mono Release + Burst AOT consumer: pending merged code.
- Windows IL2CPP Release + Burst AOT consumer: pending merged code.
- Fresh fixed-result replay and renderer agreement: pending the final schema.
- Counter-enabled supported-platform run: pending a real provider and platform.
- Real multi-device/ISA/workload matrix: pending available hardware; no substitute
  evidence will be manufactured.
- README, package documentation, ADR, CHANGELOG, release notes, sensitive-data,
  third-party authorization, and provenance review: pending final integration.
- Merge to `main`, release tag, and GitHub Release: prohibited until every claimed
  gate actually passes and require a separate explicit release action.
