# Understand the lifetime decision contract

This console example calls the package's actual `ConservativeLifetimeEnvelope`
API using explicitly synthetic costs. It shows unknown evidence, a conditional
break-even range and rejection of incomplete boundary costs. No candidate,
benchmark, Unity scene, real timer or allocation counter runs.

With the .NET 10 SDK installed, from the repository root:

```powershell
dotnet run --project Tools/Examples/LifetimeDecision -c Release
```

The example links the portable runtime source, with no copied estimator or
engine stubs or third-party package dependencies. It targets `net10.0`; this
does not change the Unity package's framework compatibility. Its numbers are
invented solely to explain the API; they are not
external benchmark inputs, measured gains or a deployment recommendation.
It emits no profile and establishes no allocation eligibility.

The three JSON lines have statuses `Unknown`, `FiniteSustainedBreakEven`, and
`Unknown`. The middle result retains `SyntheticFixture` origin; the two rejected
results have `Unknown` result origin. All bounds are conditional model outputs
in lifetime ticks. The fictional uppercase SHA-256-shaped string only satisfies
the estimator's identity format; it is not a validated deployment fingerprint.

This example assumes exactly **one terminal export and no recurring exports**.
The four one-time cost components are construction, ingress, that terminal
export, and disposal. Resident scores exclude export in this illustration.
`Estimate` has no cadence parameter: changing a label or multiplying one export
P95 cannot adapt these inputs to an every-frame or every-k-ticks consumer. Measure
the coupled work at the actual cadence and validate any linear model separately;
otherwise retain Unknown for that application's decision.

An application supplies independently collected paired process costs, exact
source/candidate identities and all construction/ingress/export/disposal costs.
Keep `Unknown` when coverage is incomplete. A finite modeled lifetime interval
still needs independent workload confirmation and the real selection/profile
contracts before a layout can be deployed.

[CalibrationHostExample.cs](CalibrationHostExample.cs) is also compiled by this
project, but never called by `Program.cs`. It demonstrates the actual host wiring:
`BindSourceContext` validates a full fingerprint, the caller supplies a capability-aware
counter and required scope, then `ScenarioCalibrationEngine.Run` performs measurement.
Supply a real scenario factory and fingerprint from the application's observed
workload/device/compiler/candidates/settings; do not pass the synthetic costs or
fictional identity from this console example. An unavailable counter or insufficient
scope is rejected by the engine. Do not reduce the declared scope to obtain eligibility.

[Adoption guide](../../../Docs/LAYOUT_ADOPTION.md).
