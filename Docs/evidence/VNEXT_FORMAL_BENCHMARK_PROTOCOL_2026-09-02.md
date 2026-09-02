# Preregistered vNext formal benchmark protocol — 2026-09-02

Status: frozen before the first retained vNext formal launch

## Purpose

This protocol validates the changed vNext tree without borrowing the published
v0.3 result. It freezes one IL2CPP Release/Burst AOT Player, one configuration,
and the retention rule before any full-size vNext measurement is started.

The five launches are process replications on one physical device. They are not
five-device evidence, hardware-counter evidence, or a universal layout ranking.

## Frozen implementation and Player

- Source implementation commit:
  `c84cf47b62f28b26c34d72acaf16ace23f674ddb`
- Build entry point:
  `DataLayoutCalibratorBuild.BuildWindowsIl2CppFormal`
- Unity: `6000.5.3f1`
- Backend/build: Windows x64 IL2CPP, non-Development Release, Burst AOT
- Player executable SHA-256:
  `1B8448700944F099D1E11D6B28E2BCD151A59430BF4858C60596E66515FED6A7`
- `GameAssembly.dll` SHA-256:
  `337E43DE4FF59CE5A120B500305D9B848AC5F49A64239288F2DBA6DDC595B4C9`
- `global-metadata.dat` SHA-256:
  `7000B8FC2EA7ECBF25A2B45EC99026428A60F5A41325B84D7B9D9654F836DF0D`
- Burst library SHA-256:
  `3F3D31AD1834270189BD6D4D61E38A33F0238EF045E78D5E275D37225E17A1CE`

The executable stub happens to match the earlier Unity build's executable hash;
the IL2CPP code, metadata, and Burst hashes above bind this vNext Player rather
than treating that generic launcher hash as sufficient identity.

The protocol document itself is a documentation-only commit after the frozen
implementation. It does not change the measured Player.

## Frozen configuration

| Setting | Value |
| --- | ---: |
| Calibration element count | 1,048,576 |
| Untouched holdout count | 1,000,003 |
| Resident samples per candidate | 40 |
| Boundary samples per candidate | 20 |
| Lifetime ticks | 600 |
| Bootstrap iterations | 4,000 |
| Bootstrap confidence | 95% |
| Minimum accepted improvement | 10% |
| Minimum warmup blocks | 32 |
| Minimum warmup time | 1 second |
| Target resident block | 25 ms |
| Maximum ticks per block | 256 |
| Independent Player launches | 5 |

The machine's active Windows power plan at freeze time is Balanced. Each suite
must retain its exact OS, CPU, worker-count, graphics, backend, build type,
candidate results, raw samples, sampling design, and frozen decisions.

## Execution and retention

1. Run launches sequentially; never overlap two calibrator Players.
2. `run-01` is the primary result regardless of its rank among the five.
3. Retain runs 02–05 as robustness replications, including regressions, ties,
   failures, and wide intervals.
4. Do not rerun or delete a launch because its result is unfavorable.
5. A launch that crashes or fails to emit a complete schema-3 suite is retained
   as a failed launch and does not silently receive a replacement ordinal.
6. Record competing Unity/batch processes before each launch. An actively
   competing benchmark or batch test prevents an uncontended-performance claim.
7. The generated-storage/profile AOT probe is excluded from formal timing; it is
   validated separately by the tiny Player audit.
8. Renderers consume the fixed suite and may not reselect a candidate.

## Acceptance boundary

Every retained suite must be IL2CPP Release, use schema 3, pass typed parity for
every candidate, and report zero measured resident and boundary managed
allocation. Non-optimized decisions must select the tuned AoS baseline.

A performance statement may report the five-run descriptive range and a
process-hierarchical interval only when the corresponding evidence actually
supports it. It must not be generalized to another CPU, ISA, device, Unity
version, backend, workload, element count, or lifetime.
