# v0.6 evidence-lab foundation

Status: implementable scaffold; no new observed hardware evidence
Counter artifact schema: 1
Validation manifest / plan / observation / report schemas: 1
Roadmap authority: `Docs/ROADMAP_V0.4_TO_V0.6.md` at commit
`644893990ed18e56619da8d2737e6b7592eb6080`

This foundation implements the interface and evidence boundaries for roadmap items
7 and 8. It does not claim that a Unity Player, hardware counter, CPU family, ISA,
device, or cross-device matrix was validated in the environment that produced this
change. Existing schema-2 suites remain immutable historical evidence.

The repository remains proprietary and All Rights Reserved under `LICENSE`. This
protocol adds no third-party sampler, counter implementation, captured binary, or
vendor artifact.

## Optional counter boundary

`ICounterProvider` is an adapter boundary for an OS, profiler, or vendor-supported
counter source. The package does not reimplement a sampler and this branch does not
provide an observed provider. A host may configure `UnavailableCounterProvider` to
record why a provider is missing.

Every capture is associated with:

- `ScenarioId` plus `ContractVersion`;
- canonical `CandidateDescriptor.CandidateId`, never `DisplayName`;
- candidate-schema, environment, and settings fingerprints;
- phase, round, element count, Player-process evidence ID, and stable device ID.

The result records one of four explicit states:

| State | Meaning | Effect on calibration |
| --- | --- | --- |
| `Disabled` | Collection was deliberately off | None |
| `Unavailable` | No provider, permission, platform, or facility | None |
| `Collected` | A provider returned origin-labelled values | None |
| `Failed` | Provider probe, begin, complete, or disposal failed | None |

`CounterCaptureRunner` executes the measured action exactly once when collection is
disabled, missing, unavailable, or failed before capture. Provider completion and
cleanup failures become counter-result data. An exception from the measured action
is rethrown and cannot be replaced by a provider cleanup exception. The counter
result is adjunct evidence and cannot modify `ScenarioCalibrationProfile.FinalDecision`.

Raw counters retain counter ID, value, unit, scale, provider identity/version,
provider artifact hash, and optional assembly/Inspector artifact provenance. Derived
metrics retain their formula and source counter IDs. A capture is labelled
`Observed` or `SyntheticFixture`; fixtures are for deterministic tests only.

A single capture is classified as correlation. Mechanism evidence requires an
independently hashed compiler/assembly artifact. A causal claim additionally requires
a preregistered controlled experiment. Neither promotion is performed automatically.

`CounterOverheadEstimator` evaluates paired enabled/disabled duration arrays using
deterministic medians. The provider or harness owns timing collection. Invalid or
missing arrays produce an explicit failed-overhead record instead of a fabricated
zero.

## Device, ISA, workload, and process boundary

The versioned manifest separates four identities:

1. A **device target** is a planned coverage requirement. It is not evidence.
2. A **registered device** is one stable `DeviceId` bound to CPU family, ISA,
   operating system, and environment fingerprint.
3. A **process request** is one planned independent Release Player launch on one
   registered device.
4. A **process observation** is the provenance record for one executed request.

Repeated processes increase process replication only. They never increase the
distinct device count. Synthetic fixture observations increase neither observed
process nor observed device coverage.

The protocol normalizes AMD and Intel targets as CPU families sharing `isaId =
x86_64`; ARM targets use `isaId = arm64`. This avoids treating vendor family names as
distinct instruction-set identifiers while preserving the roadmap's family coverage.

`Docs/evidence/device-isa-workload-validation-manifest-v1.json` is intentionally a
planning-only matrix:

- AMD/x86-64, Intel/x86-64, and ARM64 are planned targets, not registered devices.
- The two existing workloads are marked available but have no device-specific Player,
  candidate/schema hash, or settings fingerprint configured.
- Four additional workload/control contracts are marked planned, not implemented.
- `registeredDevices` is empty, so the deterministic plan has zero executable requests.

This state supports no new ISA, device, Player, counter, or cross-device claim.

## Runner and reporter gates

`Tools/EvidenceLab/evidence_lab.py` has four commands:

```powershell
python Tools/EvidenceLab/evidence_lab.py validate `
  Docs/evidence/device-isa-workload-validation-manifest-v1.json

python Tools/EvidenceLab/evidence_lab.py plan `
  Docs/evidence/device-isa-workload-validation-manifest-v1.json `
  --output work/device-validation-plan.json

python Tools/EvidenceLab/evidence_lab.py run `
  Docs/evidence/device-isa-workload-validation-manifest-v1.json `
  work/device-validation-plan.json REQUEST_ID `
  --output-directory work/process-01 `
  --confirm-device-id DEVICE_ID `
  --confirm-environment-fingerprint SHA256 `
  --origin observed --acknowledge-observed-evidence

python Tools/EvidenceLab/evidence_lab.py report `
  Docs/evidence/device-isa-workload-validation-manifest-v1.json `
  --observation work/process-01/REQUEST_ID-observation.json `
  --output work/device-validation-report.json
```

Before a request becomes executable, the manifest requires all of the following:

- an active target and an available registered device with a fingerprint;
- an implemented workload identified by `ScenarioId + ContractVersion`;
- candidate- and workload-schema SHA-256 values;
- a settings fingerprint and full source commit;
- a non-Development Release Player, backend, schema-2 suite version, and binary hash;
- an exact output path for the fixed suite artifact.

The runner first proves that the supplied plan exactly matches a fresh deterministic
expansion of the manifest. It launches without a shell, verifies the Player binary
hash, refuses to overwrite a pre-existing result or observation artifact, requires an
explicit matching device ID and environment fingerprint, and records the real process
ID, timestamps, timeout state, exit code, standard-stream hashes, and result-artifact
hash. A timeout, missing result, non-zero exit, malformed schema-2 suite, invalid frozen
decision, or mismatched scenario/backend is a failed process observation.

The reporter validates observations against deterministic requests. It reports
process and distinct-device counts separately, excludes synthetic fixtures from all
observed coverage, and states that cross-device hierarchical statistics were not
computed. It verifies the frozen-decision authority but never invokes selection or
derives a different winner.

## Remaining evidence gates

Roadmap completion still requires actual provider integration on a supported platform,
counter overhead controls, independent Release Player launches, registered hardware,
the additional workloads and negative control, device-specific envelopes, and a
separately reviewed hierarchical cross-device analysis. Missing hardware or tooling
must remain a recorded limitation rather than being replaced with fixture evidence.
