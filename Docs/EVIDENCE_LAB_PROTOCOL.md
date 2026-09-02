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

Every capture that can reach `Collected` is fail-closed and associated with:

- `ScenarioId` plus `ContractVersion`;
- canonical `CandidateDescriptor.CandidateId`, never `DisplayName`;
- canonical candidate-schema, physical-device-identity, environment, and settings
  SHA-256 fingerprints;
- phase, round, element count, Player-process evidence ID, and stable device ID.

Provider availability defaults to `Unknown`, not `Available`. Before collection is
accepted, the runner validates the provider identity, version, mechanism, artifact
hash, declared counter IDs, raw and derived values, artifact provenance, and overhead
metadata. Any invalid metadata records `Failed`, runs the measured action exactly
once, and retains no partial counter values.

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

An observed capture is classified as correlation. A synthetic fixture always has
`CounterInterpretationLevel.None`; it cannot acquire an evidence interpretation level.
Mechanism evidence requires an independently hashed compiler/assembly artifact. A
causal claim additionally requires a preregistered controlled experiment. Neither
promotion is performed automatically.

`CounterOverheadEstimator` evaluates paired enabled/disabled duration arrays using
deterministic medians. The provider or harness owns timing collection. Invalid or
missing arrays produce an explicit failed-overhead record instead of a fabricated
zero.

## Device, ISA, workload, and process boundary

The versioned manifest separates these identities:

1. A **device target** is a planned coverage requirement. It is not evidence.
2. A **physical-device identity** is a stable `deviceIdentitySha256` with a retained,
   hashed attestation artifact. It is deliberately separate from the environment or
   build fingerprint.
3. A **registered device label** is a human-readable `DeviceId` bound to the physical
   identity, CPU family, ISA, operating system, and environment fingerprint.
4. A **process request** is one planned independent Release Player launch on one
   registered device.
5. A **process observation** is the provenance record for one executed request.

Repeated processes increase process replication only. They never increase the
distinct physical-device count. Duplicate physical identity hashes are rejected even
when their `DeviceId` labels differ. Coverage groups the identity hash, not the label.
Synthetic fixture observations increase neither observed process nor observed device
coverage.

The tool does not derive a physical identity and does not prescribe a hardware serial,
inventory system, or vendor identifier. Registration must supply a privacy-reviewed
external identity source as a versioned JSON attestation containing the identity hash,
evidence origin, capture method, source reference, and capture time. The manifest
retains the attestation path and SHA-256. An environment fingerprint is never accepted
as a substitute for physical identity.

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
  --confirm-device-identity SHA256 `
  --confirm-environment-fingerprint SHA256 `
  --origin observed --acknowledge-observed-evidence

python Tools/EvidenceLab/evidence_lab.py report `
  Docs/evidence/device-isa-workload-validation-manifest-v1.json `
  --observation work/process-01/REQUEST_ID-observation.json `
  --output work/device-validation-report.json
```

Before a request becomes executable, the manifest requires all of the following:

- an active target and an available registered device with distinct physical-identity
  and environment fingerprints plus a retained identity attestation;
- an implemented workload identified by `ScenarioId + ContractVersion`;
- candidate- and workload-schema SHA-256 values;
- a settings fingerprint and full source commit;
- a non-Development Release Player, backend, declared suite schema version 2 or 3,
  and binary hash;
- an exact output path for the fixed suite artifact.

The runner first proves that the supplied plan exactly matches a fresh deterministic
expansion of the manifest. It launches without a shell, verifies the Player binary
hash, refuses to overwrite a pre-existing result or observation artifact, requires an
explicit matching device label, physical-identity hash, and environment fingerprint,
and records the real process ID, timestamps, timeout state, exit code, retained
standard-stream paths/hashes, and fixed-suite path/hash. A timeout, missing result,
non-zero exit, malformed schema-2/schema-3 suite, invalid frozen decision, or mismatched
scenario/backend is a failed process observation.

The reporter validates observations against deterministic requests. Before a
successful observation can contribute to observed coverage, it re-hashes the retained
device-identity attestation, stdout, stderr, and fixed-suite artifacts, re-parses the
retained suite at its request-declared schema, and re-runs frozen-decision validation.
Missing local artifacts make an
imported observation `pending-unverified`; hash, suite, or copied-decision mismatches
make it rejected. Neither category contributes to process, device, ISA, workload, or
matrix coverage.

The configured request may declare suite schema 2 or 3, and the retained suite must
match it exactly. Schema 2 accepts only statuses 0 through 3; schema 3 accepts only
statuses 0 through 4. Status 0 is invalid evidence in both schemas, and unknown future
integers are rejected. Status 2 (`Optimized`) is the only status allowed to select a
non-baseline candidate; Inconclusive, StatisticalTie, and schema-3 Regression must
select `BaselineCandidate`.

This compatibility gate does not rewrite historical schema-2 suites, promote them to
schema 3, or claim that any schema-3 Release Player, device, ISA, or counter evidence
was collected by this change.

The reporter groups verified physical identity hashes, retains `DeviceId` only as a
label, excludes synthetic fixtures from all observed coverage, and states that
cross-device hierarchical statistics were not computed. It never invokes selection or
derives a different winner.

This is a local retained-artifact verification boundary, not a remote signature or
hardware-root-of-trust protocol. The manifest itself must come from a reviewed trusted
source. Portable bundles and signed remote attestations remain integration-owned;
without locally verifiable retained artifacts, imported observations stay pending.

## Remaining evidence gates

Roadmap completion still requires actual provider integration on a supported platform,
counter overhead controls, independent Release Player launches, registered hardware,
the additional workloads and negative control, device-specific envelopes, and a
separately reviewed hierarchical cross-device analysis. Missing hardware or tooling
must remain a recorded limitation rather than being replaced with fixture evidence.
