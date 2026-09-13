# Evidence Lab

This tool validates a versioned device/ISA/workload manifest, expands deterministic
independent Player-process requests, records one explicitly confirmed execution, and
reports provenance coverage without reselecting a layout.

It uses only the Python standard library. Run its deterministic fixture tests with:

```powershell
python -m unittest discover Tools/EvidenceLab/tests -v
```

The checked-in manifest is planning-only. Running `plan` against it returns blocked
matrix entries and no executable requests because it has no registered devices or
configured Release Player artifacts. That result is intentional.

## Evidence rules

- `CandidateDescriptor.CandidateId` is the cross-artifact candidate key.
- Workloads bind `ScenarioId + ContractVersion`, workload/candidate schema hashes,
  settings, source commit, Player binary, backend, device, and environment.
- One request and observation represent one Player process. A verified
  `deviceIdentitySha256` represents one physical device; `DeviceId` is only a label.
- Physical identities require retained, hashed attestations from an explicitly named
  external source. Environment/build fingerprints are not device identities, and the
  tool invents no identity source.
- Observed coverage requires local re-verification of identity, stdout, stderr, and
  fixed-suite artifacts. Missing artifacts remain pending; mismatches are rejected.
- `synthetic-fixture` observations are test inputs and never count as observed process,
  device, ISA, or workload coverage.
- The reporter retains `ScenarioCalibrationProfile.FinalDecision` as authority and
  performs no selection.
- Cross-device hierarchical confidence intervals are outside this scaffold and are
  always reported as not computed.

The complete protocol and command examples are in
`Docs/EVIDENCE_LAB_PROTOCOL.md`. The project is proprietary and All Rights Reserved;
see the repository `LICENSE`.
