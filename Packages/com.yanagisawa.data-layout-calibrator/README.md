> September 10 follow-up: [actual frozen external comparisons](../../Docs/ACTUAL_COMPARISON_REPORT_2026-09-10.md)
> cover BabelStream variants and LLAMA code_comp, including complete IL2CPP/Burst
> outputs. They do not establish allocation-qualified deployment or other workloads'
> performance. The historical September 8 repair scope below is retained.
>
> 2026-09-08 integration: performance at that checkpoint was **Unmeasured**. Measurement entrypoints
> remain explicitly callable with an exact source fingerprint. Allocation
> eligibility requires available, scoped, positively controlled observations; default
> current-thread counters cannot certify worker/native allocations. Historical P95
> fields retain their original numbers and describe a component-P95 selection score,
> not a per-tick or whole-lifecycle percentile. See the
> [repair contract](../../Docs/MEASUREMENT_REPAIR_2026-09-08.md).

# Data Layout Calibrator package

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

Generator source is retained under `SourceGenerators~`; the UPM package distributes the compiled analyzer under `SourceGenerators`. Rebuild and test it with:

```powershell
dotnet test SourceGenerators~/Tests/Yanagisawa.DataLayoutCalibrator.SourceGenerator.Tests.csproj -c Release
```

## Included Samples

- `Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate`
- `Yanagisawa.DataLayoutCalibrator.Samples.TransformExport`

They are reference plugins, not dependencies of the core assembly. The published
v0.3 versions passed Mono and IL2CPP Release Player validation with Burst AOT;
that historical evidence does not automatically validate unreleased vNext code.

## Unreleased vNext foundation

The current integration branch adds, without changing the package version:

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
