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

## Included Samples

- `Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate`
- `Yanagisawa.DataLayoutCalibrator.Samples.TransformExport`

They are reference plugins and AOT evidence, not dependencies of the core assembly. Source generation is intentionally absent until both workloads pass Mono Burst AOT and Windows IL2CPP Release validation.
