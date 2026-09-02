from __future__ import annotations

import copy
import json
import sys
import tempfile
import unittest
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from evidence_lab import (  # noqa: E402
    EvidenceLabError,
    build_plan,
    build_report,
    calculate_matrix_coverage,
    run_request,
    sha256_file,
    validate_fixed_suite,
    validate_manifest,
    validate_plan_against_manifest,
)


# Every manifest, process, device, suite, counter state, and hash in this file is a
# synthetic fixture. None is observed hardware, ISA, Unity Player, or counter evidence.


def configured_manifest() -> dict:
    return {
        "schemaVersion": 1,
        "manifestId": "synthetic-fixture-manifest",
        "roadmapCommit": "1" * 40,
        "evidencePolicy": {
            "candidateJoinKey": "CandidateDescriptor.CandidateId",
            "scenarioIdentity": ["ScenarioId", "ContractVersion"],
            "frozenDecisionAuthority": "ScenarioCalibrationProfile.FinalDecision",
            "syntheticFixtureCountsAsObserved": False,
            "processCountsAsDevice": False,
        },
        "deviceTargets": [
            {
                "targetId": "fixture-target",
                "cpuFamily": "fixture-cpu-family",
                "isaId": "fixture-isa",
                "operatingSystems": ["fixture-os"],
                "status": "active",
            }
        ],
        "registeredDevices": [
            {
                "deviceId": "fixture-device-01",
                "targetId": "fixture-target",
                "cpuFamily": "fixture-cpu-family",
                "isaId": "fixture-isa",
                "operatingSystem": "fixture-os",
                "environmentFingerprintSha256": "A" * 64,
                "deviceIdentitySha256": "8" * 64,
                "deviceIdentityAttestation": {
                    "schemaVersion": 1,
                    "evidenceOrigin": "synthetic-fixture",
                    "artifactPath": "fixture-only/missing-device-attestation.json",
                    "artifactSha256": "7" * 64,
                },
                "status": "available",
            }
        ],
        "workloads": [
            {
                "workloadId": "fixture-workload-v3",
                "scenarioId": "fixture-workload-v3",
                "contractVersion": 3,
                "accessPattern": "synthetic-fixture-only",
                "negativeControl": True,
                "implementationStatus": "available",
                "workloadSchemaSha256": "B" * 64,
                "candidateSchemaSha256": "C" * 64,
                "execution": {
                    "status": "configured",
                    "executable": "fixture-player-never-executed",
                    "arguments": ["--fixture", "{outputDirectory}"],
                    "workingDirectory": ".",
                    "resultArtifact": "{outputDirectory}/calibration-suite.json",
                    "timeoutSeconds": 300,
                    "settingsFingerprintSha256": "D" * 64,
                    "player": {
                        "buildType": "Release",
                        "developmentBuild": False,
                        "backend": "fixture-backend",
                        "binarySha256": "E" * 64,
                        "sourceCommit": "2" * 40,
                        "suiteSchemaVersion": 2,
                    },
                },
            }
        ],
        "matrix": [
            {
                "targetId": "fixture-target",
                "workloadId": "fixture-workload-v3",
                "requiredIndependentProcesses": 2,
            }
        ],
    }


def synthetic_observation_for(request: dict, plan: dict, process_id: int) -> dict:
    return {
        "schemaVersion": 1,
        "manifestId": plan["manifestId"],
        "sourceManifestSha256": plan["sourceManifestSha256"],
        "requestId": request["requestId"],
        "observationId": (
            f"fixture-observation-{request['deviceId']}-{request['launchOrdinal']}"
        ),
        "evidenceOrigin": "synthetic-fixture",
        "status": "succeeded",
        "failureCode": None,
        "targetId": request["targetId"],
        "deviceId": request["deviceId"],
        "deviceIdentitySha256": request["deviceIdentitySha256"],
        "deviceIdentityAttestation": request["deviceIdentityAttestation"],
        "cpuFamily": request["cpuFamily"],
        "isaId": request["isaId"],
        "operatingSystem": request["operatingSystem"],
        "environmentFingerprintSha256": request["environmentFingerprintSha256"],
        "workloadId": request["workloadId"],
        "scenarioId": request["scenarioId"],
        "contractVersion": request["contractVersion"],
        "workloadSchemaSha256": request["workloadSchemaSha256"],
        "candidateSchemaSha256": request["candidateSchemaSha256"],
        "settingsFingerprintSha256": request["execution"][
            "settingsFingerprintSha256"
        ].upper(),
        "launchOrdinal": request["launchOrdinal"],
        "process": {
            "processEvidenceId": (
                f"fixture-process-{request['deviceId']}-{request['launchOrdinal']}"
            ),
            "processId": process_id,
            "startedUtc": "2026-09-02T00:00:00Z",
            "finishedUtc": "2026-09-02T00:00:01Z",
            "exitCode": 0,
            "timedOut": False,
        },
        "player": {
            **request["execution"]["player"],
            "binarySha256": request["execution"]["player"]["binarySha256"].upper(),
        },
        "artifacts": {
            "stdoutArtifactPath": "fixture-only/missing-stdout.bin",
            "stdoutSha256": "4" * 64,
            "stderrArtifactPath": "fixture-only/missing-stderr.bin",
            "stderrSha256": "5" * 64,
            "resultArtifactPath": "fixture-only/missing-calibration-suite.json",
            "resultArtifactSha256": "F" * 64,
            "frozenDecisionAuthority": "ScenarioCalibrationProfile.FinalDecision",
            "frozenDecision": {
                "authority": "ScenarioCalibrationProfile.FinalDecision",
                "runId": "synthetic-fixture-run",
                "scenarioId": request["scenarioId"],
                "contractVersion": request["contractVersion"],
                "status": 1,
                "baselineCandidateId": "fixture-aos-b64",
                "selectedCandidateId": "fixture-aos-b64",
                "bestMeasuredCandidateId": "fixture-soa-b64",
                "reselectionPerformed": False,
            },
        },
        "counterEvidence": {
            "status": "unavailable",
            "artifacts": [],
            "reason": "Synthetic fixture has no observed counters.",
        },
        "deviceConfirmation": {
            "method": "synthetic-fixture-only",
            "confirmedDeviceId": request["deviceId"],
            "confirmedDeviceIdentitySha256": request["deviceIdentitySha256"],
        },
    }


def synthetic_fixed_suite(request: dict) -> dict:
    def candidate(candidate_id: str) -> dict:
        return {"Candidate": {"CandidateId": candidate_id}}

    return {
        "SchemaVersion": 2,
        "RunId": "synthetic-fixture-run",
        "Environment": {
            "BuildType": "Release",
            "ScriptingBackend": "fixture-backend",
        },
        "Scenarios": [
            {
                "Scenario": {
                    "ScenarioId": request["scenarioId"],
                    "ContractVersion": request["contractVersion"],
                },
                "FinalDecision": {
                    "Status": 1,
                    "BaselineCandidate": {"CandidateId": "fixture-aos-b64"},
                    "SelectedCandidate": {"CandidateId": "fixture-aos-b64"},
                    "BestMeasuredCandidate": {"CandidateId": "fixture-soa-b64"},
                },
                "CalibrationResults": [
                    candidate("fixture-aos-b64"),
                    candidate("fixture-soa-b64"),
                ],
            }
        ],
    }


class EvidenceLabTests(unittest.TestCase):
    def test_plan_is_deterministic_and_expands_processes_under_one_device(self) -> None:
        manifest = configured_manifest()

        first = build_plan(manifest)
        second = build_plan(copy.deepcopy(manifest))

        self.assertEqual(first, second)
        self.assertEqual(2, len(first["requests"]))
        self.assertEqual(
            {"fixture-device-01"},
            {request["deviceId"] for request in first["requests"]},
        )
        self.assertEqual(
            [1, 2], [request["launchOrdinal"] for request in first["requests"]]
        )
        self.assertNotEqual(
            first["requests"][0]["requestId"], first["requests"][1]["requestId"]
        )

    def test_tampered_plan_is_rejected_before_execution(self) -> None:
        manifest = configured_manifest()
        plan = build_plan(manifest)
        plan["requests"][0]["execution"]["arguments"].append("--tampered")

        with self.assertRaisesRegex(EvidenceLabError, "does not exactly match"):
            validate_plan_against_manifest(plan, manifest)

    def test_fixed_suite_validation_copies_candidate_ids_without_reselection(self) -> None:
        manifest = configured_manifest()
        request = build_plan(manifest)["requests"][0]

        frozen = validate_fixed_suite(synthetic_fixed_suite(request), request)

        self.assertEqual("fixture-aos-b64", frozen["selectedCandidateId"])
        self.assertEqual("fixture-soa-b64", frozen["bestMeasuredCandidateId"])
        self.assertFalse(frozen["reselectionPerformed"])

    def test_fixed_suite_rejects_decision_candidate_absent_from_results(self) -> None:
        manifest = configured_manifest()
        request = build_plan(manifest)["requests"][0]
        suite = synthetic_fixed_suite(request)
        suite["Scenarios"][0]["FinalDecision"]["Status"] = 2
        suite["Scenarios"][0]["FinalDecision"]["SelectedCandidate"][
            "CandidateId"
        ] = "fixture-invented-candidate"

        with self.assertRaisesRegex(EvidenceLabError, "absent from CalibrationResults"):
            validate_fixed_suite(suite, request)

    def test_fixed_suite_rejects_inconclusive_non_baseline_selection(self) -> None:
        manifest = configured_manifest()
        request = build_plan(manifest)["requests"][0]
        suite = synthetic_fixed_suite(request)
        suite["Scenarios"][0]["FinalDecision"]["Status"] = 1
        suite["Scenarios"][0]["FinalDecision"]["SelectedCandidate"][
            "CandidateId"
        ] = "fixture-soa-b64"

        with self.assertRaisesRegex(EvidenceLabError, "non-Optimized"):
            validate_fixed_suite(suite, request)

    def test_fixed_suite_rejects_statistical_tie_non_baseline_selection(self) -> None:
        manifest = configured_manifest()
        request = build_plan(manifest)["requests"][0]
        suite = synthetic_fixed_suite(request)
        suite["Scenarios"][0]["FinalDecision"]["Status"] = 3
        suite["Scenarios"][0]["FinalDecision"]["SelectedCandidate"][
            "CandidateId"
        ] = "fixture-soa-b64"

        with self.assertRaisesRegex(EvidenceLabError, "non-Optimized"):
            validate_fixed_suite(suite, request)

    def test_future_non_optimized_status_is_safe_when_it_selects_baseline(self) -> None:
        manifest = configured_manifest()
        request = build_plan(manifest)["requests"][0]
        suite = synthetic_fixed_suite(request)
        suite["Scenarios"][0]["FinalDecision"]["Status"] = 4

        frozen = validate_fixed_suite(suite, request)

        self.assertEqual(4, frozen["status"])
        self.assertEqual(frozen["baselineCandidateId"], frozen["selectedCandidateId"])

    def test_runner_records_fixture_process_and_report_excludes_it(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            output = root / "output"
            manifest = configured_manifest()
            identity_attestation = {
                "schemaVersion": 1,
                "evidenceOrigin": "synthetic-fixture",
                "deviceIdentitySha256": "8" * 64,
                "attestationMethod": "synthetic-fixture-only",
                "sourceReference": "unit-test-generated-fixture",
                "capturedUtc": "2026-09-02T00:00:00Z",
            }
            identity_path = root / "device-identity-attestation.json"
            identity_path.write_text(
                json.dumps(identity_attestation), encoding="utf-8"
            )
            identity_reference = manifest["registeredDevices"][0][
                "deviceIdentityAttestation"
            ]
            identity_reference["artifactPath"] = str(identity_path)
            identity_reference["artifactSha256"] = sha256_file(identity_path)
            execution = manifest["workloads"][0]["execution"]
            execution["executable"] = sys.executable
            execution["workingDirectory"] = str(root)
            execution["player"]["binarySha256"] = sha256_file(Path(sys.executable))
            suite_request = {
                "scenarioId": "fixture-workload-v3",
                "contractVersion": 3,
                "execution": {"player": {"backend": "fixture-backend"}},
            }
            suite_json = json.dumps(synthetic_fixed_suite(suite_request))
            execution["arguments"] = [
                "-c",
                (
                    "from pathlib import Path; "
                    f"Path(r'{{outputDirectory}}/calibration-suite.json').write_text("
                    f"{suite_json!r}, encoding='utf-8')"
                ),
            ]
            plan = build_plan(manifest)
            request = plan["requests"][0]

            observation = run_request(
                plan,
                request["requestId"],
                output,
                "fixture-device-01",
                "8" * 64,
                "A" * 64,
                "synthetic-fixture",
                False,
            )
            report = build_report(manifest, [observation])

            self.assertEqual("succeeded", observation["status"])
            self.assertEqual("synthetic-fixture", observation["evidenceOrigin"])
            self.assertEqual(
                "fixture-aos-b64",
                observation["artifacts"]["frozenDecision"]["selectedCandidateId"],
            )
            self.assertEqual("no-observed-evidence", report["scopeStatus"])
            self.assertEqual(0, report["coverageSummary"]["acceptedObservedDeviceCount"])
            self.assertEqual(
                1,
                report["coverageSummary"][
                    "verifiedSyntheticFixtureSucceededProcessCount"
                ],
            )
            Path(observation["artifacts"]["resultArtifactPath"]).write_text(
                "{}", encoding="utf-8"
            )
            tampered_report = build_report(manifest, [observation])
            self.assertEqual(
                1,
                tampered_report["coverageSummary"][
                    "rejectedSyntheticFixtureProcessCount"
                ],
            )
            self.assertEqual(
                0,
                tampered_report["coverageSummary"][
                    "verifiedSyntheticFixtureSucceededProcessCount"
                ],
            )

    def test_matrix_grouping_keeps_two_fixture_processes_under_one_device(self) -> None:
        manifest = configured_manifest()
        plan = build_plan(manifest)
        observations = [
            synthetic_observation_for(request, plan, 1000 + index)
            for index, request in enumerate(plan["requests"])
        ]

        coverage = calculate_matrix_coverage(manifest, observations)

        self.assertEqual(2, coverage[0]["processCount"])
        self.assertEqual(1, coverage[0]["distinctDeviceCount"])
        self.assertEqual(1, coverage[0]["qualifiedDeviceCount"])
        self.assertEqual("covered", coverage[0]["status"])

    def test_synthetic_fixtures_never_count_as_observed_scope(self) -> None:
        manifest = configured_manifest()
        plan = build_plan(manifest)
        observations = [
            synthetic_observation_for(request, plan, 2000 + index)
            for index, request in enumerate(plan["requests"])
        ]

        report = build_report(manifest, observations)
        repeated_report = build_report(copy.deepcopy(manifest), copy.deepcopy(observations))

        self.assertEqual(report, repeated_report)
        self.assertEqual("no-observed-evidence", report["scopeStatus"])
        self.assertEqual(0, report["coverageSummary"]["acceptedObservedProcessCount"])
        self.assertEqual(0, report["coverageSummary"]["acceptedObservedDeviceCount"])
        self.assertEqual(
            0,
            report["coverageSummary"][
                "verifiedSyntheticFixtureSucceededProcessCount"
            ],
        )
        self.assertEqual(
            2,
            report["coverageSummary"][
                "pendingUnverifiedSyntheticFixtureProcessCount"
            ],
        )
        self.assertEqual([], report["deviceSummaries"])
        self.assertEqual("missing", report["matrixCoverage"][0]["status"])

    def test_observation_device_mismatch_is_rejected(self) -> None:
        manifest = configured_manifest()
        plan = build_plan(manifest)
        observation = synthetic_observation_for(plan["requests"][0], plan, 3000)
        observation["deviceId"] = "invented-second-device"

        with self.assertRaisesRegex(EvidenceLabError, "deviceId"):
            build_report(manifest, [observation])

    def test_observation_with_malformed_artifact_hash_is_rejected(self) -> None:
        manifest = configured_manifest()
        plan = build_plan(manifest)
        observation = synthetic_observation_for(plan["requests"][0], plan, 3500)
        observation["artifacts"]["stdoutSha256"] = "not-a-sha256"

        with self.assertRaisesRegex(EvidenceLabError, "64-character SHA-256"):
            build_report(manifest, [observation])

    def test_duplicate_process_observation_is_rejected(self) -> None:
        manifest = configured_manifest()
        plan = build_plan(manifest)
        observation = synthetic_observation_for(plan["requests"][0], plan, 4000)

        with self.assertRaisesRegex(EvidenceLabError, "Duplicate observation"):
            build_report(manifest, [observation, copy.deepcopy(observation)])

    def test_one_process_on_each_of_two_devices_does_not_meet_per_device_replication(self) -> None:
        manifest = configured_manifest()
        second_device = copy.deepcopy(manifest["registeredDevices"][0])
        second_device["deviceId"] = "fixture-device-02"
        second_device["environmentFingerprintSha256"] = "9" * 64
        second_device["deviceIdentitySha256"] = "6" * 64
        second_device["deviceIdentityAttestation"][
            "artifactPath"
        ] = "fixture-only/missing-device-attestation-02.json"
        second_device["deviceIdentityAttestation"]["artifactSha256"] = "5" * 64
        manifest["registeredDevices"].append(second_device)
        plan = build_plan(manifest)
        first_launch_per_device = [
            request for request in plan["requests"] if request["launchOrdinal"] == 1
        ]
        observations = [
            synthetic_observation_for(request, plan, 5000 + index)
            for index, request in enumerate(first_launch_per_device)
        ]

        coverage = calculate_matrix_coverage(manifest, observations)

        self.assertEqual(2, coverage[0]["processCount"])
        self.assertEqual(2, coverage[0]["distinctDeviceCount"])
        self.assertEqual(0, coverage[0]["qualifiedDeviceCount"])
        self.assertEqual("missing", coverage[0]["status"])

    def test_duplicate_physical_device_identity_registration_is_rejected(self) -> None:
        manifest = configured_manifest()
        duplicate = copy.deepcopy(manifest["registeredDevices"][0])
        duplicate["deviceId"] = "different-label-same-physical-device"
        manifest["registeredDevices"].append(duplicate)

        with self.assertRaisesRegex(EvidenceLabError, "Duplicate device identity hash"):
            validate_manifest(manifest)

    def test_same_process_evidence_id_cannot_satisfy_two_requests(self) -> None:
        manifest = configured_manifest()
        plan = build_plan(manifest)
        observations = [
            synthetic_observation_for(request, plan, 6000 + index)
            for index, request in enumerate(plan["requests"])
        ]
        observations[1]["process"]["processEvidenceId"] = observations[0]["process"][
            "processEvidenceId"
        ]

        with self.assertRaisesRegex(EvidenceLabError, "Duplicate processEvidenceId"):
            build_report(manifest, observations)

    def test_development_player_configuration_is_rejected(self) -> None:
        manifest = configured_manifest()
        manifest["workloads"][0]["execution"]["player"]["developmentBuild"] = True

        with self.assertRaisesRegex(EvidenceLabError, "non-Development Release Player"):
            validate_manifest(manifest)

    def test_repository_manifest_is_planning_only_with_no_observed_evidence(self) -> None:
        repository_root = Path(__file__).resolve().parents[3]
        path = (
            repository_root
            / "Docs"
            / "evidence"
            / "device-isa-workload-validation-manifest-v1.json"
        )
        manifest = json.loads(path.read_text(encoding="utf-8"))

        plan = build_plan(manifest)
        report = build_report(manifest, [])

        self.assertEqual(0, len(plan["requests"]))
        self.assertGreater(len(plan["blockedMatrixEntries"]), 0)
        self.assertEqual("no-observed-evidence", report["scopeStatus"])
        self.assertEqual([], report["coverageSummary"]["acceptedObservedIsaIds"])
        self.assertEqual([], report["coverageSummary"]["acceptedObservedWorkloadIds"])
        self.assertFalse(report["selectionPolicy"]["reselectionPerformed"])


if __name__ == "__main__":
    unittest.main()
