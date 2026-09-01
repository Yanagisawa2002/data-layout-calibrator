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

The engine measures ingress, resident execution, and export independently; computes lifetime-amortized P95; applies deterministic bootstrap confidence intervals; falls back to the best AoS candidate on a statistical tie; and confirms an optimization on holdout data.

## AOT-safe registration

Register factories in the assembly that owns the benchmark host:

```csharp
using Yanagisawa.DataLayoutCalibrator;

[assembly: RegisterCalibrationScenarioFactory(typeof(MyFactory))]
```

The packaged Roslyn Source Generator validates each registration and emits a strongly typed `GeneratedCalibrationScenarioRegistry.CreateFactories()` method containing direct constructor calls. It uses no reflection, `Activator`, open generic Job discovery, or player-linker preservation rules. Generated entries are sorted by fully qualified type name, so registration order is deterministic.

`DLCGEN001` rejects abstract, generic, inaccessible, non-factory, or non-default-constructible types. `DLCGEN002` rejects duplicate registrations. The generator deliberately stops at factory registration: it does not invent layouts, rewrite workload code, or claim to tune arbitrary structs.

Generator source is retained under `SourceGenerators~`; the UPM package distributes the compiled analyzer under `SourceGenerators`. Rebuild and test it with:

```powershell
dotnet test SourceGenerators~/Tests/Yanagisawa.DataLayoutCalibrator.SourceGenerator.Tests.csproj -c Release
```

## Included Samples

- `Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate`
- `Yanagisawa.DataLayoutCalibrator.Samples.TransformExport`

They are reference plugins and AOT evidence, not dependencies of the core assembly. Both now pass Mono and IL2CPP Release Player validation with Burst AOT.
