# Choose a layout for the data's actual lifetime

The library answers a bounded question: does a particular layout and kernel repay
its conversion and ownership costs under the application's actual workload,
export cadence and data lifetime? The tuned AoS path remains a meaningful
reference. Layout name alone does not establish a better choice.

## Start with the contract, then collect evidence

The [CPU-only API example](../Tools/Examples/LifetimeDecision/README.md) exercises
the real lifetime estimator without executing a workload. It illustrates
`Unknown`, conditional break-even bounds and missing boundary costs. Every
numeric input is labeled synthetic; none is a new benchmark result.

For actual adoption, retain the following information together. The cost estimator
does not verify parity, allocation capability, holdout or deployability for you:

| Question | Required evidence or behavior |
|---|---|
| Are outputs equivalent? | Full canonical input/output parity, including tails and arbitrary supported fields. |
| What changed? | Separate layout, scalar/SIMD kernel, branch policy, batching, execution backend and thread configuration. |
| Does conversion repay? | Construction, ingress, resident work, export and disposal for the same declared lifetime/cadence. |
| What does P95 mean? | Preserve block-mean, individual-tick, whole-lifecycle and selection-score labels. |
| Is zero allocation observable? | Positive controls, availability and full required thread/native scope; otherwise Unknown. |
| Is the result deployable? | Frozen candidate and independent holdout, compatible source/device/compiler/settings and the profile resolver's actual decision. |

## Keep a score separate from a measured percentile

`P95(block elapsed / ticks)` summarizes block means. Adding
`(ingress P95 + export P95) / lifetime` produces a component-quantile selection
score. It does not produce the P95 of complete ticks or lifecycles. Use actual
tick/lifecycle observations for those percentiles; preserve their observation
origin and coverage.

`ConservativeLifetimeEnvelope.Estimate` models conditional linear costs from
independent paired processes. A finite range is conditional on those inputs and
constant-cost assumptions. An unavailable or uncertain resident-savings
denominator remains Unknown. This estimate is not a deployment profile.

## Match export cadence and lifetime before choosing

A one-export-at-disposal experiment does not describe an application exporting
every frame. Collect the actual recurring export work in the declared workload
and fingerprint it accordingly; do not multiply marginal P95 values and call
their sum a measured lifecycle P95. Short-lived data may never repay setup even
when its resident kernel is faster. Retain AoS on a tie, regression or
inconclusive comparison; missing baseline eligibility is an invalid comparison.

The current estimator accepts a one-time cost and a constant resident cost score
per tick; it has no export-frequency argument. The teaching example assumes one
terminal export, included in one-time costs, and none during residency. For periodic
exports, the phase and number of exports can change with lifetime. A model built
from complete cadence periods applies only where that period/phase and linearity
assumption hold; it does not cover arbitrary lifetimes automatically. Record cadence
and boundary definitions in the workload contract and fingerprint settings, avoid
double-counting exports in both cost terms, and measure full lifecycles when the
linear mapping is unsupported. Keep that application's selection Unknown meanwhile.

[Calibration contract](CALIBRATION_CONTRACT.md) defines the selection protocol.
The [package API](../Packages/com.yanagisawa.data-layout-calibrator/README.md)
describes registration and normal runtime calibration. The new cost example
does not bypass either contract or change their defaults.
Its [compiled host-wiring example](../Tools/Examples/LifetimeDecision/CalibrationHostExample.cs)
shows the real source-fingerprint and allocation-provider binding. That method is
not invoked by the cost-only console program; a real host must provide its own
complete factory and observed context before calling the measurement engine.

## Evidence that can support this claim

Use [BabelStream/STREAM and named external workload ports](../Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/CONTRACT.md)
for their declared questions and preserved semantics. Bandwidth alone does not
prove a complete lifecycle win. Cross-language C++/Burst differences cannot be
attributed entirely to layout.

Current integration source performance and actual worker/native allocation
coverage remain Unmeasured. The attempted September 9 Unity import failed with
insufficient disk space before a formal comparison. Historical figures retain
their original source and metric definitions; see the
[historical allocation limitation](evidence/HISTORICAL_ALLOCATION_MEASUREMENT_LIMIT.md).
