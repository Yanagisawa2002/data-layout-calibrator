# Data Layout Calibrator package

[Project overview](../../README.md) ·
[Engineering case studies](../../Docs/ENGINEERING_CASE_STUDIES.md) ·
[Evidence status](../../Docs/EVIDENCE_STATUS.md) ·
[Adoption guide](../../Docs/LAYOUT_ADOPTION.md)

This package is an unreleased integration foundation. The repository includes
named measured cases: [parallel Burst Dot](../../Docs/BABEL_DOT_REPORT_2026-09-15.md)
and a [complete native SPH task](../../Docs/FOCUSED_COMPLETE_TASK_2026-09-15.md),
in addition to the [September 10 comparisons](../../Docs/ACTUAL_COMPARISON_REPORT_2026-09-10.md).
They do not certify every package path or establish profitable automatic selection.
The native SPH study is not Unity/Burst or generated-storage validation.

Allocation-qualified deployment remains Unknown: actual IL2CPP positive controls
failed, and current-thread observations do not certify worker/native scope. The
September 8 integration checkpoint was unmeasured; that historical statement must
not be used to erase or generalize the later named results. Historical P95 fields
retain their original component-quantile selection-score semantics, not per-tick
or whole-lifecycle percentiles. See the
[measurement repair contract](../../Docs/MEASUREMENT_REPAIR_2026-09-08.md).

The package separates a workload-agnostic calibration core from concrete Samples. The core assembly contains protocol, measurement, statistics, selection, and serializable evidence types; it contains no Particle or Transform workload types.

## Plugin surface

Implement the [public contracts](Runtime/CalibrationProtocols.cs) in the assembly
that owns the workload:

| Contract | Implementation responsibility |
| --- | --- |
| `ICalibrationScenarioFactory` | Stable workload descriptor; create the requested input and frozen candidates. |
| `ICalibrationScenario` | Own canonical input, candidate instances, dataset hash and reference index. |
| `ICalibrationCandidate` | Concrete execution sites, full canonical export hash, storage lifetime and disposal. |
| `IBoundaryCost` | Reusable full ingress/export into preallocated buffers. |
| `IParityValidator` | Compare all canonical output fields, including tails and payloads. |

The scenario supplies its `IParityValidator`; each candidate supplies an `IBoundaryCost` that copies the full canonical input into candidate-owned storage and exports the full canonical result. Those operations must reuse preallocated storage.

The [compiled host example](../../Tools/Examples/LifetimeDecision/CalibrationHostExample.cs)
accepts a real factory, `CalibrationRunSettings`, a full
`CalibrationProfileFingerprint`, a capability-aware allocation counter, and the
application's required `AllocationScope`. It calls `BindSourceContext`, assigns
the counter and invokes `ScenarioCalibrationEngine.Run`. The host owns the counter.
Missing/corrupt source identity and unavailable or insufficient allocation
coverage cannot produce an eligible profile. Choose the required scope from the
application's contract, not from whichever counter happens to be available.

The engine measures ingress, resident execution, and export independently;
computes a lifetime-amortized component-quantile selection score; applies deterministic confidence intervals;
falls back to the best AoS candidate on a statistical tie; and confirms an
optimization on holdout data. Published schema-2 artifacts use the historical
independent bootstrap. Native schema 3 uses explicit paired blocks and a
log-ratio estimator.

That score combines the P95 of resident block means with amortized boundary
P95 values. It is not measured per-tick or whole-lifecycle P95. Running the
host method performs real workload measurements; the separate
[lifetime API example](../../Tools/Examples/LifetimeDecision/README.md) performs
only cost inference on explicitly synthetic inputs. See the
[adoption guide](../../Docs/LAYOUT_ADOPTION.md) for export cadence, complete
boundary costs, allocation coverage and independent deployment confirmation.

## Complete-task timing API

[WholeTaskLayoutSelector](Runtime/WholeTaskLayoutSelector.cs) is an additive,
timing-only path over complete application-process observations. `Select` uses
calibration data; `Confirm` checks the previously chosen candidate on separate
input/process identities. It does not certify allocation coverage or payback.
`WholeTaskTimingDecision` is a mutable DTO: the host must preserve its returned
candidate, threshold and identity fields unchanged between selection and
confirmation. The API's word "frozen" is a caller contract, not type-enforced
immutability or a tamper-evident seal.

## AOT-safe registration

Register factories in the assembly that owns the benchmark host:

```csharp
using Yanagisawa.DataLayoutCalibrator;
using Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate;

[assembly: RegisterCalibrationScenarioFactory(typeof(ParticleIntegrateScenarioFactory))]
```

This registers an existing sample factory; reference its sample assembly in the
host's asmdef. The repository's [registration file](../../BenchmarkProject/Assets/DataLayoutCalibrator/Runtime/RegisteredScenarioFactories.cs)
shows the actual host declarations. The portable cost-only console example does
not load Unity samples or register a workload.

The packaged Roslyn Source Generator validates each registration and emits a strongly typed `GeneratedCalibrationScenarioRegistry.CreateFactories()` method containing direct constructor calls. It uses no reflection, `Activator`, open generic Job discovery, or player-linker preservation rules. Generated entries are sorted by fully qualified type name, so registration order is deterministic.

`DLCGEN001` rejects abstract, generic, inaccessible, non-factory, or
non-default-constructible types. `DLCGEN002` rejects duplicate registrations.
Factory registration remains deliberately narrow: it does not discover types at
runtime, rewrite workload code, or claim to tune arbitrary structs.

Generator source is retained under `SourceGenerators~`; the UPM package distributes the compiled analyzer under `SourceGenerators`. From the repository root, rebuild and test it with:

```powershell
dotnet test Packages/com.yanagisawa.data-layout-calibrator/SourceGenerators~/Tests/Yanagisawa.DataLayoutCalibrator.SourceGenerator.Tests.csproj -c Release
```

## Included Samples

- `Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate`
- `Yanagisawa.DataLayoutCalibrator.Samples.TransformExport`

They are reference plugins, not dependencies of the core assembly. The published
v0.3 versions passed Mono and IL2CPP Release Player validation with Burst AOT;
that historical evidence does not automatically validate unreleased vNext code.

## Unreleased vNext foundation

The current integration adds, without changing the package version:

- explicit layout, kernel, batch, and execution candidate policies;
- paired-block and same-device process-hierarchical statistics;
- immutable advantage-envelope, break-even, Pareto, and adaptive audit models;
- bounded generated AoS/SoA/AoSoA storage and codec scaffolds for explicitly
  annotated records;
- exact deployment fingerprint, cache, codec, and tuned-AoS fallback APIs; and
- optional counter and evidence-lab boundaries that contain no real provider or
  hardware observation.

`CandidateDefinitionProtocol` supplies the canonical full-definition hash.
`ScientificAdvantageEnvelopeAdapter` reuses the exact paired bootstrap draws and
requires host-supplied evidence hashes, partitions, contract feasibility, and
memory feasibility. A schema-3 `ScenarioCalibrationProfile` can reference a
locked external envelope, but that reference never replaces `FinalDecision`.

These APIs are a tested integration foundation, not a v0.4/v0.5/v0.6 release.
AoSoA4/AoSoA16 and aligned/padded causal controls, production generated-storage
adoption, real counter providers, real device/ISA matrices, and merged-tree
IL2CPP validation remain release gates. See
[`ADR 0006`](../../Docs/adr/0006-vnext-integration-protocol.md).
