# Resident batch callers

Two runnable, synthetic-coordinate callers reuse the same serial managed CPU
pipeline with either explicit AoS or SoA storage. No calibration, benchmark,
performance counters, Unity, Player, GPU or external dataset is invoked.

From the repository root, with an SDK able to build the net8.0 example:

```powershell
dotnet run --project Tools/Examples/ResidentBatch -c Release
```

After the functional-only banner, the four receipts report:

```text
AoS: scene-anchor coordinates; exports=2; consumed=10; final-count=5; retained-capacity=8
AoS: point-cloud coordinate frames; exports=2; consumed=8; final-count=3; retained-capacity=7
SoA: scene-anchor coordinates; exports=2; consumed=10; final-count=5; retained-capacity=8
SoA: point-cloud coordinate frames; exports=2; consumed=8; final-count=3; retained-capacity=7
```

These counts are functional expectations, not timings. Scene anchors exercise
repeated transforms and periodic/final export; point-cloud frames reload a shorter
second frame without growing storage or consuming a stale tail. Both callers are
in [Callers.cs](Callers.cs); full-coordinate assertions are in the
[functional tests](../../ResidentBatch/Tests/ResidentBatchTests.cs).

The module can also be built without the examples or calibration core:

```powershell
dotnet build Tools/ResidentBatch/ResidentBatch.csproj -c Release
```

See the [API, ownership, memory and validation contract](../../../Docs/RESIDENT_BATCH.md).
The data is synthetic, the backend is managed and serial, and performance plus
Unity/Burst/IL2CPP deployment remain unmeasured. LICENSE is unchanged.
