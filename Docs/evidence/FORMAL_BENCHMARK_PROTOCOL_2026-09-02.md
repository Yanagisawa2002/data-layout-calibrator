# Preregistered formal benchmark protocol — 2026-09-02

## Purpose

This protocol was committed before the retained formal runs. It prevents
post-hoc selection of a favorable process launch and upgrades the short backend
integration gate with five independent, full-size IL2CPP Player launches.

## Frozen implementation

- Source commit used to build the Player:
  `9df183942cd8dc8abfa05bd89f03d822c96c689e`
- Player SHA-256:
  `1B8448700944F099D1E11D6B28E2BCD151A59430BF4858C60596E66515FED6A7`
- Burst library SHA-256:
  `6C7DEF5529937750E0FEF754ED6A56C780CCAC72A050E772EA3BFBB9AE9B6B26`
- Player: Windows x64, IL2CPP Release, Burst AOT, headless

Documentation and release-metadata commits after the frozen implementation do
not alter the measured Player.

## Frozen configuration

| Setting | Value |
|---|---:|
| Calibration element count | 1,048,576 |
| Untouched holdout count | 1,000,003 |
| Resident samples per candidate | 40 |
| Boundary samples per candidate | 20 |
| Lifetime ticks | 600 |
| Bootstrap iterations | 4,000 |
| Bootstrap confidence | 95% |
| Minimum accepted improvement | 10% |
| Independent Player launches | 5 |

All other warmup and target-block settings retain the defaults compiled into
`BenchmarkConfiguration`: 32 minimum warmup blocks, at least one warmup second,
a 25 ms target measurement block, and at most 256 ticks per block.

## Selection and retention rule

1. Launches run sequentially so they do not compete for CPU resources.
2. `run-01` is the preregistered primary result regardless of its ranking among
   the five launches.
3. `run-02` through `run-05` are robustness replications and are all retained.
4. No failed, losing, or inconclusive run may be deleted from the retained set.
5. The renderer may visualize a frozen `FinalDecision`; it may not recompute or
   replace that decision.
6. Each retained suite is identified by SHA-256 and its complete environment,
   raw candidate samples, calibration decision, and holdout decision remain in
   the JSON evidence.

The five launches are replications, not five independent hardware platforms.
Claims remain scoped to the recorded device, operating system, Unity version,
backend, workload, and configuration.
