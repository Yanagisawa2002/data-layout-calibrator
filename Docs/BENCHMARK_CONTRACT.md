# Benchmark contract: particle-integrate-v1

Status: Implemented feasibility contract  
Schema: 1

## Logical state

Every candidate preserves the same 48-byte logical record:

| Field | Type | Role |
|---|---|---|
| Position | `float3` | hot, read/write |
| Velocity | `float3` | hot, read/write |
| Lifetime | `float` | hot, read/write |
| Rotation | `quaternion` | cold, preserved |
| Category | `int` | cold, preserved |

The deterministic step applies the same acceleration, damping, integration, lifetime decrement, and respawn rule. AoS, SoA, and AoSoA8 are created from one canonical dataset.

## Search space

- Layout: AoS, SoA, AoSoA8.
- Logical batch size: 32, 64, 128, 256 records.
- AoSoA8 maps logical batch size to physical block batch size by dividing by eight.
- The baseline is the lowest-P95 AoS candidate, not a fixed or deliberately weak batch.

## Timing boundary

One recorded value is milliseconds per logical tick. A timed block wraps:

```text
timestamp start
  managed layout switch
  Schedule concrete Burst job
  Complete
  repeat for the fixed ticks-per-block count
timestamp end
```

Allocation, initial packing, reset, parity comparison, hashing, rendering, logging, and serialization are outside the primary timing boundary. The current conclusion therefore applies only when the chosen layout remains resident across ticks.

## Measurement protocol

- Release standalone Player; Burst must report enabled.
- The build must contain a non-empty `lib_burst_generated.dll` and all three job entrypoints.
- An AoS probe doubles ticks per block until the block reaches the target duration.
- Every candidate uses the same ticks per block and exact warmup step count.
- Candidate order is deterministically shuffled each measurement round.
- Selection minimizes P95 and rejects parity failures or any measured hot-path managed allocation.
- The selected candidate must again beat the matching tuned AoS candidate by at least 10% on a new seed and non-eight-divisible holdout count.

## Correctness protocol

- Player preflight: 4,099 records for 256 steps.
- Tests cover counts 1, 7, 8, 9, and 4,099 for tail handling.
- Hot floating fields use an absolute tolerance of `1e-5`.
- Cold fields must be exact and unchanged.
- A quantized state hash is an additional signal, never the sole parity proof.

## Known omissions before a general claim

- A common render/export consumer.
- Separately optimized ingress and full egress measurement.
- Confidence intervals and noise-aware tie handling.
- Multiple representative kernels and working-set sizes.
- IL2CPP validation.
