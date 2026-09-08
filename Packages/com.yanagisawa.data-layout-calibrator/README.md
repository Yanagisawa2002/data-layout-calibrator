# Data Layout Calibrator package

The package separates a workload-agnostic calibration core from concrete Samples. The core assembly contains protocol, measurement, statistics, selection, and serializable evidence types; it contains no Particle or Transform workload types.

## Plugin surface

Implement these four contracts in a separate assembly:

```csharp
public sealed class MyFactory : ICalibrationScenarioFactory
{
    public ScenarioDescriptor Descriptor => /* stable workload identity */;

    public ICalibrationScenario Create(
        int elementCount,
        uint seed,
        CandidateDescriptor[] candidates = null) => /* owned scenario */;
}

public sealed class MyCandidate : ICalibrationCandidate
{
    public CandidateDescriptor Descriptor { get; }
    public int ElementCount { get; }
    public long ResidentBytes { get; }
    public IBoundaryCost BoundaryCost { get; }
    public string ExportedStateHash { get; }

    public void Execute(int ticks, float fixedDeltaTime) { /* literal concrete Burst schedules */ }
    public void Dispose() { }
}
```

The scenario supplies its `IParityValidator`; each candidate supplies an `IBoundaryCost` that copies the full canonical input into candidate-owned storage and exports the full canonical result. Those operations must reuse preallocated storage.

Run a plugin with:

```csharp
ScenarioCalibrationProfile profile = ScenarioCalibrationEngine.Run(
    new MyFactory(),
    new CalibrationRunSettings
    {
        ElementCount = 1_048_576,
        HoldoutElementCount = 1_000_003,
        LifetimeTicks = 600,
    });
```

The engine measures ingress, resident execution, and export independently;
computes lifetime-amortized P95; applies deterministic confidence intervals;
falls back to the best AoS candidate on a statistical tie; and confirms an
optimization on holdout data. Published schema-2 artifacts use the historical
independent bootstrap. Native schema 3 uses explicit paired blocks and a
log-ratio estimator.

## AOT-safe registration

Register factories in the assembly that owns the benchmark host:

```csharp
using Yanagisawa.DataLayoutCalibrator;

[assembly: RegisterCalibrationScenarioFactory(typeof(MyFactory))]
```

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
