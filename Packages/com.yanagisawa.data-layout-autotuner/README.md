# Data Layout Autotuner package

This package contains the reusable runtime, calibration, parity, and profile types for the feasibility milestone.

The package currently exposes a synthetic particle pipeline only. It is intentionally concrete so that generated abstractions can later be compared against a known Burst/AOT ceiling.

```csharp
using NativeArray<ParticleRecord> source = ParticleDataSet.Create(
    count,
    ParticleDataSet.CalibrationSeed,
    Allocator.TempJob);

using ParticleLayoutDomain domain = ParticleLayoutDomain.Create(
    LayoutKind.AoSoA8,
    logicalBatchSize: 128,
    source,
    Allocator.Persistent);

domain.Schedule(1f / 60f).Complete();
```

The selected profile is scoped to one workload, count bucket, CPU, Unity/Burst package set, build configuration, and worker count. Do not reuse it across fingerprints without recalibration.
