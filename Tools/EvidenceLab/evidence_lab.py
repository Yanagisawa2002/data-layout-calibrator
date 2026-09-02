from __future__ import annotations

import argparse
import copy
import hashlib
import json
import re
import subprocess
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable


MANIFEST_SCHEMA_VERSION = 1
PLAN_SCHEMA_VERSION = 1
OBSERVATION_SCHEMA_VERSION = 1
REPORT_SCHEMA_VERSION = 1
DEVICE_IDENTITY_ATTESTATION_SCHEMA_VERSION = 1
ARTIFACT_VERIFICATION_POLICY_VERSION = 1
HEX_40 = re.compile(r"^[0-9a-fA-F]{40}$")
HEX_64 = re.compile(r"^[0-9a-fA-F]{64}$")


class EvidenceLabError(ValueError):
    pass


class ArtifactUnavailableError(EvidenceLabError):
    pass


class ArtifactVerificationError(EvidenceLabError):
    pass


def canonical_json(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest().upper()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def sha256_json(value: Any) -> str:
    return sha256_bytes(canonical_json(value).encode("utf-8"))


def _require_mapping(value: Any, label: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise EvidenceLabError(f"{label} must be an object.")
    return value


def _require_list(value: Any, label: str) -> list[Any]:
    if not isinstance(value, list):
        raise EvidenceLabError(f"{label} must be an array.")
    return value


def _require_string(value: Any, label: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise EvidenceLabError(f"{label} must be a non-empty string.")
    return value


def _require_int(value: Any, label: str, minimum: int = 0) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value < minimum:
        raise EvidenceLabError(f"{label} must be an integer >= {minimum}.")
    return value


def _require_sha256(value: Any, label: str) -> str:
    text = _require_string(value, label)
    if not HEX_64.fullmatch(text):
        raise EvidenceLabError(f"{label} must be a 64-character SHA-256 value.")
    return text.upper()


def _unique_by(items: Iterable[dict[str, Any]], key: str, label: str) -> None:
    seen: set[str] = set()
    for item in items:
        identifier = _require_string(item.get(key), f"{label}.{key}")
        if identifier in seen:
            raise EvidenceLabError(f"Duplicate {label} {key}: {identifier}")
        seen.add(identifier)


def validate_manifest(manifest: dict[str, Any]) -> None:
    _require_mapping(manifest, "manifest")
    if manifest.get("schemaVersion") != MANIFEST_SCHEMA_VERSION:
        raise EvidenceLabError(
            f"Unsupported manifest schemaVersion {manifest.get('schemaVersion')!r}; "
            f"expected {MANIFEST_SCHEMA_VERSION}."
        )
    _require_string(manifest.get("manifestId"), "manifest.manifestId")
    roadmap_commit = _require_string(manifest.get("roadmapCommit"), "manifest.roadmapCommit")
    if not HEX_40.fullmatch(roadmap_commit):
        raise EvidenceLabError("manifest.roadmapCommit must be a full 40-character commit SHA.")

    policy = _require_mapping(manifest.get("evidencePolicy"), "manifest.evidencePolicy")
    expected_policy = {
        "candidateJoinKey": "CandidateDescriptor.CandidateId",
        "scenarioIdentity": ["ScenarioId", "ContractVersion"],
        "frozenDecisionAuthority": "ScenarioCalibrationProfile.FinalDecision",
        "syntheticFixtureCountsAsObserved": False,
        "processCountsAsDevice": False,
    }
    for key, expected in expected_policy.items():
        if policy.get(key) != expected:
            raise EvidenceLabError(
                f"manifest.evidencePolicy.{key} must be {expected!r}."
            )

    targets = [
        _require_mapping(item, f"manifest.deviceTargets[{index}]")
        for index, item in enumerate(
            _require_list(manifest.get("deviceTargets"), "manifest.deviceTargets")
        )
    ]
    devices = [
        _require_mapping(item, f"manifest.registeredDevices[{index}]")
        for index, item in enumerate(
            _require_list(manifest.get("registeredDevices"), "manifest.registeredDevices")
        )
    ]
    workloads = [
        _require_mapping(item, f"manifest.workloads[{index}]")
        for index, item in enumerate(
            _require_list(manifest.get("workloads"), "manifest.workloads")
        )
    ]
    matrix = [
        _require_mapping(item, f"manifest.matrix[{index}]")
        for index, item in enumerate(_require_list(manifest.get("matrix"), "manifest.matrix"))
    ]
    if not targets:
        raise EvidenceLabError("manifest.deviceTargets must not be empty.")
    if not workloads:
        raise EvidenceLabError("manifest.workloads must not be empty.")
    if not matrix:
        raise EvidenceLabError("manifest.matrix must not be empty.")

    _unique_by(targets, "targetId", "device target")
    _unique_by(devices, "deviceId", "registered device")
    _unique_by(workloads, "workloadId", "workload")
    target_ids = {item["targetId"] for item in targets}
    workload_ids = {item["workloadId"] for item in workloads}

    for target in targets:
        target_id = target["targetId"]
        _require_string(target.get("cpuFamily"), f"device target {target_id}.cpuFamily")
        _require_string(target.get("isaId"), f"device target {target_id}.isaId")
        operating_systems = _require_list(
            target.get("operatingSystems"), f"device target {target_id}.operatingSystems"
        )
        if not operating_systems:
            raise EvidenceLabError(
                f"device target {target_id}.operatingSystems must not be empty."
            )
        for operating_system in operating_systems:
            _require_string(operating_system, f"device target {target_id}.operatingSystems[]")
        if target.get("status") not in {"planned", "active", "retired"}:
            raise EvidenceLabError(
                f"device target {target_id}.status must be planned, active, or retired."
            )

    seen_device_identity_hashes: set[str] = set()
    for device in devices:
        device_id = device["deviceId"]
        target_id = _require_string(device.get("targetId"), f"device {device_id}.targetId")
        if target_id not in target_ids:
            raise EvidenceLabError(f"device {device_id} references unknown target {target_id}.")
        _require_string(device.get("cpuFamily"), f"device {device_id}.cpuFamily")
        _require_string(device.get("isaId"), f"device {device_id}.isaId")
        _require_string(device.get("operatingSystem"), f"device {device_id}.operatingSystem")
        if device.get("status") not in {"available", "unavailable", "retired"}:
            raise EvidenceLabError(
                f"device {device_id}.status must be available, unavailable, or retired."
            )
        _require_sha256(
            device.get("environmentFingerprintSha256"),
            f"device {device_id}.environmentFingerprintSha256",
        )
        device_identity = _require_sha256(
            device.get("deviceIdentitySha256"),
            f"device {device_id}.deviceIdentitySha256",
        )
        if device_identity in seen_device_identity_hashes:
            raise EvidenceLabError(
                f"Duplicate device identity hash for registered device {device_id}."
            )
        seen_device_identity_hashes.add(device_identity)
        attestation = _require_mapping(
            device.get("deviceIdentityAttestation"),
            f"device {device_id}.deviceIdentityAttestation",
        )
        if attestation.get("schemaVersion") != DEVICE_IDENTITY_ATTESTATION_SCHEMA_VERSION:
            raise EvidenceLabError(
                f"device {device_id}.deviceIdentityAttestation.schemaVersion must be "
                f"{DEVICE_IDENTITY_ATTESTATION_SCHEMA_VERSION}."
            )
        if attestation.get("evidenceOrigin") not in {"observed", "synthetic-fixture"}:
            raise EvidenceLabError(
                f"device {device_id}.deviceIdentityAttestation.evidenceOrigin is invalid."
            )
        _require_string(
            attestation.get("artifactPath"),
            f"device {device_id}.deviceIdentityAttestation.artifactPath",
        )
        _require_sha256(
            attestation.get("artifactSha256"),
            f"device {device_id}.deviceIdentityAttestation.artifactSha256",
        )

    for workload in workloads:
        workload_id = workload["workloadId"]
        scenario_id = _require_string(
            workload.get("scenarioId"), f"workload {workload_id}.scenarioId"
        )
        if scenario_id != workload_id:
            raise EvidenceLabError(
                f"workload {workload_id}.scenarioId must equal its canonical workloadId."
            )
        _require_int(workload.get("contractVersion"), f"workload {workload_id}.contractVersion", 1)
        _require_string(workload.get("accessPattern"), f"workload {workload_id}.accessPattern")
        if not isinstance(workload.get("negativeControl"), bool):
            raise EvidenceLabError(f"workload {workload_id}.negativeControl must be boolean.")
        if workload.get("implementationStatus") not in {"available", "planned", "retired"}:
            raise EvidenceLabError(
                f"workload {workload_id}.implementationStatus must be available, planned, or retired."
            )
        execution = _require_mapping(
            workload.get("execution"), f"workload {workload_id}.execution"
        )
        if execution.get("status") not in {"configured", "unconfigured"}:
            raise EvidenceLabError(
                f"workload {workload_id}.execution.status must be configured or unconfigured."
            )
        if execution.get("status") == "configured":
            _validate_configured_execution(workload_id, workload, execution)

    seen_matrix: set[tuple[str, str]] = set()
    for entry in matrix:
        target_id = _require_string(entry.get("targetId"), "matrix.targetId")
        workload_id = _require_string(entry.get("workloadId"), "matrix.workloadId")
        if target_id not in target_ids:
            raise EvidenceLabError(f"matrix references unknown target {target_id}.")
        if workload_id not in workload_ids:
            raise EvidenceLabError(f"matrix references unknown workload {workload_id}.")
        key = (target_id, workload_id)
        if key in seen_matrix:
            raise EvidenceLabError(
                f"Duplicate matrix entry for target {target_id} and workload {workload_id}."
            )
        seen_matrix.add(key)
        _require_int(
            entry.get("requiredIndependentProcesses"),
            f"matrix {target_id}/{workload_id}.requiredIndependentProcesses",
            2,
        )


def _validate_configured_execution(
    workload_id: str, workload: dict[str, Any], execution: dict[str, Any]
) -> None:
    _require_sha256(
        workload.get("workloadSchemaSha256"),
        f"workload {workload_id}.workloadSchemaSha256",
    )
    _require_sha256(
        workload.get("candidateSchemaSha256"),
        f"workload {workload_id}.candidateSchemaSha256",
    )
    _require_string(execution.get("executable"), f"workload {workload_id}.execution.executable")
    arguments = _require_list(
        execution.get("arguments"), f"workload {workload_id}.execution.arguments"
    )
    for argument in arguments:
        if not isinstance(argument, str):
            raise EvidenceLabError(
                f"workload {workload_id}.execution.arguments must contain only strings."
            )
    _require_string(
        execution.get("workingDirectory"),
        f"workload {workload_id}.execution.workingDirectory",
    )
    _require_string(
        execution.get("resultArtifact"),
        f"workload {workload_id}.execution.resultArtifact",
    )
    _require_int(
        execution.get("timeoutSeconds"),
        f"workload {workload_id}.execution.timeoutSeconds",
        1,
    )
    _require_sha256(
        execution.get("settingsFingerprintSha256"),
        f"workload {workload_id}.execution.settingsFingerprintSha256",
    )
    player = _require_mapping(execution.get("player"), f"workload {workload_id}.execution.player")
    if player.get("buildType") != "Release" or player.get("developmentBuild") is not False:
        raise EvidenceLabError(
            f"workload {workload_id} must use a non-Development Release Player."
        )
    _require_string(player.get("backend"), f"workload {workload_id}.execution.player.backend")
    _require_sha256(
        player.get("binarySha256"), f"workload {workload_id}.execution.player.binarySha256"
    )
    source_commit = _require_string(
        player.get("sourceCommit"), f"workload {workload_id}.execution.player.sourceCommit"
    )
    if not HEX_40.fullmatch(source_commit):
        raise EvidenceLabError(
            f"workload {workload_id}.execution.player.sourceCommit must be a full commit SHA."
        )
    if player.get("suiteSchemaVersion") != 2:
        raise EvidenceLabError(
            f"workload {workload_id}.execution.player.suiteSchemaVersion must be 2."
        )


def build_plan(manifest: dict[str, Any]) -> dict[str, Any]:
    validate_manifest(manifest)
    manifest_hash = sha256_json(manifest)
    targets = {item["targetId"]: item for item in manifest["deviceTargets"]}
    workloads = {item["workloadId"]: item for item in manifest["workloads"]}
    devices_by_target: dict[str, list[dict[str, Any]]] = {}
    for device in manifest["registeredDevices"]:
        if device["status"] == "available":
            devices_by_target.setdefault(device["targetId"], []).append(device)
    for devices in devices_by_target.values():
        devices.sort(key=lambda item: item["deviceId"])

    requests: list[dict[str, Any]] = []
    blocked: list[dict[str, Any]] = []
    ordered_matrix = sorted(
        manifest["matrix"], key=lambda item: (item["targetId"], item["workloadId"])
    )
    for entry in ordered_matrix:
        target = targets[entry["targetId"]]
        workload = workloads[entry["workloadId"]]
        devices = devices_by_target.get(entry["targetId"], [])
        base_reasons: list[str] = []
        if target["status"] != "active":
            base_reasons.append("device-target-not-active")
        if workload["implementationStatus"] != "available":
            base_reasons.append("workload-not-implemented")
        if workload["execution"]["status"] != "configured":
            base_reasons.append("release-player-execution-not-configured")
        if not devices:
            blocked.append(
                {
                    "targetId": entry["targetId"],
                    "deviceId": None,
                    "workloadId": entry["workloadId"],
                    "requiredIndependentProcesses": entry["requiredIndependentProcesses"],
                    "reasons": sorted(set(base_reasons + ["no-registered-device"])),
                }
            )
            continue

        for device in devices:
            reasons = list(base_reasons)
            if device["cpuFamily"] != target["cpuFamily"]:
                reasons.append("registered-cpu-family-mismatch")
            if device["isaId"] != target["isaId"]:
                reasons.append("registered-isa-mismatch")
            if device["operatingSystem"] not in target["operatingSystems"]:
                reasons.append("registered-operating-system-mismatch")
            if reasons:
                blocked.append(
                    {
                        "targetId": entry["targetId"],
                        "deviceId": device["deviceId"],
                        "workloadId": entry["workloadId"],
                        "requiredIndependentProcesses": entry[
                            "requiredIndependentProcesses"
                        ],
                        "reasons": sorted(set(reasons)),
                    }
                )
                continue

            for launch_ordinal in range(1, entry["requiredIndependentProcesses"] + 1):
                identity = (
                    f"{manifest_hash}|{entry['targetId']}|{device['deviceId']}|"
                    f"{device['deviceIdentitySha256']}|{entry['workloadId']}|"
                    f"{launch_ordinal}"
                )
                requests.append(
                    {
                        "requestId": hashlib.sha256(identity.encode("utf-8")).hexdigest()[:24],
                        "targetId": entry["targetId"],
                        "deviceId": device["deviceId"],
                        "deviceIdentitySha256": device[
                            "deviceIdentitySha256"
                        ].upper(),
                        "deviceIdentityAttestation": copy.deepcopy(
                            device["deviceIdentityAttestation"]
                        ),
                        "cpuFamily": device["cpuFamily"],
                        "isaId": device["isaId"],
                        "operatingSystem": device["operatingSystem"],
                        "environmentFingerprintSha256": device[
                            "environmentFingerprintSha256"
                        ].upper(),
                        "workloadId": entry["workloadId"],
                        "scenarioId": workload["scenarioId"],
                        "contractVersion": workload["contractVersion"],
                        "workloadSchemaSha256": workload["workloadSchemaSha256"].upper(),
                        "candidateSchemaSha256": workload["candidateSchemaSha256"].upper(),
                        "launchOrdinal": launch_ordinal,
                        "execution": copy.deepcopy(workload["execution"]),
                    }
                )

    requests.sort(
        key=lambda item: (
            item["targetId"],
            item["deviceId"],
            item["workloadId"],
            item["launchOrdinal"],
        )
    )
    blocked.sort(
        key=lambda item: (
            item["targetId"],
            item["deviceId"] or "",
            item["workloadId"],
        )
    )
    return {
        "schemaVersion": PLAN_SCHEMA_VERSION,
        "manifestId": manifest["manifestId"],
        "sourceManifestSha256": manifest_hash,
        "candidateJoinKey": "CandidateDescriptor.CandidateId",
        "frozenDecisionAuthority": "ScenarioCalibrationProfile.FinalDecision",
        "requests": requests,
        "blockedMatrixEntries": blocked,
        "summary": {
            "readyProcessRequestCount": len(requests),
            "blockedMatrixEntryCount": len(blocked),
            "registeredDeviceCount": len(manifest["registeredDevices"]),
        },
    }


def validate_plan_against_manifest(
    plan: dict[str, Any], manifest: dict[str, Any]
) -> None:
    expected = build_plan(manifest)
    if plan != expected:
        raise EvidenceLabError(
            "Run plan does not exactly match the deterministic plan for its manifest."
        )


def _verified_local_file(
    path_value: Any, expected_sha256: Any, label: str
) -> Path:
    if not isinstance(path_value, str) or not path_value.strip():
        raise ArtifactUnavailableError(f"No local path is retained for {label}.")
    expected = _require_sha256(expected_sha256, f"{label} SHA-256")
    path = Path(path_value).resolve()
    if not path.is_file():
        raise ArtifactUnavailableError(f"Retained {label} is unavailable at {path}.")
    actual = sha256_file(path)
    if actual != expected:
        raise ArtifactVerificationError(
            f"Retained {label} SHA-256 mismatch: expected {expected}, got {actual}."
        )
    return path


def validate_device_identity_attestation(
    attestation: dict[str, Any], request: dict[str, Any]
) -> dict[str, Any]:
    _require_mapping(attestation, "device identity attestation")
    if attestation.get("schemaVersion") != DEVICE_IDENTITY_ATTESTATION_SCHEMA_VERSION:
        raise ArtifactVerificationError(
            "Device identity attestation has an unsupported schemaVersion."
        )
    reference = request["deviceIdentityAttestation"]
    if attestation.get("evidenceOrigin") != reference["evidenceOrigin"]:
        raise ArtifactVerificationError(
            "Device identity attestation evidence origin does not match registration."
        )
    attested_identity = _require_sha256(
        attestation.get("deviceIdentitySha256"),
        "device identity attestation.deviceIdentitySha256",
    )
    if attested_identity != request["deviceIdentitySha256"]:
        raise ArtifactVerificationError(
            "Device identity attestation hash does not match the registered physical identity."
        )
    _require_string(
        attestation.get("attestationMethod"),
        "device identity attestation.attestationMethod",
    )
    _require_string(
        attestation.get("sourceReference"),
        "device identity attestation.sourceReference",
    )
    _require_string(
        attestation.get("capturedUtc"),
        "device identity attestation.capturedUtc",
    )
    return attestation


def verify_device_identity_attestation(request: dict[str, Any]) -> dict[str, Any]:
    reference = _require_mapping(
        request.get("deviceIdentityAttestation"),
        "process request.deviceIdentityAttestation",
    )
    path = _verified_local_file(
        reference.get("artifactPath"),
        reference.get("artifactSha256"),
        "device identity attestation",
    )
    try:
        value = load_json(path)
    except (OSError, json.JSONDecodeError) as exception:
        raise ArtifactVerificationError(
            f"Device identity attestation is not valid JSON: {exception}"
        ) from exception
    try:
        return validate_device_identity_attestation(value, request)
    except EvidenceLabError as exception:
        if isinstance(exception, ArtifactVerificationError):
            raise
        raise ArtifactVerificationError(str(exception)) from exception


def validate_fixed_suite(
    suite: dict[str, Any], request: dict[str, Any]
) -> dict[str, Any]:
    _require_mapping(suite, "calibration suite")
    if suite.get("SchemaVersion") != 2:
        raise EvidenceLabError("Result artifact must be a schema-2 calibration suite.")
    run_id = _require_string(suite.get("RunId"), "calibration suite.RunId")
    environment = _require_mapping(
        suite.get("Environment"), "calibration suite.Environment"
    )
    if environment.get("BuildType") != "Release":
        raise EvidenceLabError("Result artifact is not from a Release Player.")
    if environment.get("ScriptingBackend") != request["execution"]["player"]["backend"]:
        raise EvidenceLabError(
            "Result artifact scripting backend does not match the process request."
        )
    scenarios = _require_list(suite.get("Scenarios"), "calibration suite.Scenarios")
    matches: list[dict[str, Any]] = []
    for index, scenario_value in enumerate(scenarios):
        scenario = _require_mapping(
            scenario_value, f"calibration suite.Scenarios[{index}]"
        )
        descriptor = _require_mapping(
            scenario.get("Scenario"), f"calibration suite.Scenarios[{index}].Scenario"
        )
        if (
            descriptor.get("ScenarioId") == request["scenarioId"]
            and descriptor.get("ContractVersion") == request["contractVersion"]
        ):
            matches.append(scenario)
    if len(matches) != 1:
        raise EvidenceLabError(
            "Result artifact must contain exactly one matching ScenarioId + ContractVersion."
        )

    scenario = matches[0]
    decision = _require_mapping(
        scenario.get("FinalDecision"), "matching scenario.FinalDecision"
    )
    status = _require_int(decision.get("Status"), "matching scenario.FinalDecision.Status", 0)
    if status == 0:
        raise EvidenceLabError("Matching scenario has an Invalid frozen decision.")

    def decision_candidate_id(field: str) -> str:
        candidate = _require_mapping(
            decision.get(field), f"matching scenario.FinalDecision.{field}"
        )
        return _require_string(
            candidate.get("CandidateId"),
            f"matching scenario.FinalDecision.{field}.CandidateId",
        )

    baseline_id = decision_candidate_id("BaselineCandidate")
    selected_id = decision_candidate_id("SelectedCandidate")
    best_id = decision_candidate_id("BestMeasuredCandidate")
    optimized_status = 2
    if status != optimized_status and selected_id != baseline_id:
        raise EvidenceLabError(
            "A non-Optimized frozen decision must select its baseline CandidateId."
        )
    calibration_results = _require_list(
        scenario.get("CalibrationResults"), "matching scenario.CalibrationResults"
    )
    result_candidate_ids: set[str] = set()
    for index, result_value in enumerate(calibration_results):
        result = _require_mapping(
            result_value, f"matching scenario.CalibrationResults[{index}]"
        )
        candidate = _require_mapping(
            result.get("Candidate"),
            f"matching scenario.CalibrationResults[{index}].Candidate",
        )
        result_candidate_ids.add(
            _require_string(
                candidate.get("CandidateId"),
                f"matching scenario.CalibrationResults[{index}].Candidate.CandidateId",
            )
        )
    for label, candidate_id in (
        ("baseline", baseline_id),
        ("selected", selected_id),
        ("best measured", best_id),
    ):
        if candidate_id not in result_candidate_ids:
            raise EvidenceLabError(
                f"Frozen {label} CandidateId {candidate_id!r} is absent from CalibrationResults."
            )
    return {
        "authority": "ScenarioCalibrationProfile.FinalDecision",
        "runId": run_id,
        "scenarioId": request["scenarioId"],
        "contractVersion": request["contractVersion"],
        "status": status,
        "baselineCandidateId": baseline_id,
        "selectedCandidateId": selected_id,
        "bestMeasuredCandidateId": best_id,
        "reselectionPerformed": False,
    }


def _expand_token(value: str, output_directory: Path, request_id: str) -> str:
    return value.replace("{outputDirectory}", str(output_directory)).replace(
        "{requestId}", request_id
    )


def run_request(
    plan: dict[str, Any],
    request_id: str,
    output_directory: Path,
    confirmed_device_id: str,
    confirmed_device_identity: str,
    confirmed_environment_fingerprint: str,
    origin: str,
    acknowledge_observed: bool,
) -> dict[str, Any]:
    if plan.get("schemaVersion") != PLAN_SCHEMA_VERSION:
        raise EvidenceLabError("Unsupported run-plan schemaVersion.")
    if origin not in {"observed", "synthetic-fixture"}:
        raise EvidenceLabError("origin must be observed or synthetic-fixture.")
    if origin == "observed" and not acknowledge_observed:
        raise EvidenceLabError(
            "Observed execution requires --acknowledge-observed-evidence."
        )
    matches = [item for item in plan.get("requests", []) if item.get("requestId") == request_id]
    if len(matches) != 1:
        raise EvidenceLabError(f"Expected exactly one ready request {request_id!r}.")
    request = matches[0]
    if confirmed_device_id != request["deviceId"]:
        raise EvidenceLabError("Confirmed device ID does not match the run request.")
    confirmed_identity = _require_sha256(
        confirmed_device_identity, "confirmed device identity"
    )
    if confirmed_identity != request["deviceIdentitySha256"]:
        raise EvidenceLabError(
            "Confirmed physical-device identity does not match the run request."
        )
    identity_attestation = verify_device_identity_attestation(request)
    if identity_attestation["evidenceOrigin"] != origin:
        raise EvidenceLabError(
            "Device identity attestation origin must match the process evidence origin."
        )
    confirmed_fingerprint = _require_sha256(
        confirmed_environment_fingerprint, "confirmed environment fingerprint"
    )
    if confirmed_fingerprint != request["environmentFingerprintSha256"].upper():
        raise EvidenceLabError(
            "Confirmed environment fingerprint does not match the registered device."
        )

    output_directory = output_directory.resolve()
    output_directory.mkdir(parents=True, exist_ok=True)
    execution = request["execution"]
    working_directory = Path(
        _expand_token(execution["workingDirectory"], output_directory, request_id)
    ).resolve()
    if not working_directory.is_dir():
        raise EvidenceLabError(
            f"Configured working directory does not exist: {working_directory}"
        )
    executable = Path(
        _expand_token(execution["executable"], output_directory, request_id)
    )
    if not executable.is_absolute():
        executable = working_directory / executable
    executable = executable.resolve()
    if not executable.is_file():
        raise EvidenceLabError(f"Configured Player executable does not exist: {executable}")
    actual_binary_hash = sha256_file(executable)
    expected_binary_hash = execution["player"]["binarySha256"].upper()
    if actual_binary_hash != expected_binary_hash:
        raise EvidenceLabError(
            f"Player SHA-256 mismatch: expected {expected_binary_hash}, got {actual_binary_hash}."
        )
    arguments = [
        _expand_token(argument, output_directory, request_id)
        for argument in execution["arguments"]
    ]
    result_path = Path(
        _expand_token(execution["resultArtifact"], output_directory, request_id)
    )
    if not result_path.is_absolute():
        result_path = working_directory / result_path
    result_path = result_path.resolve()
    stdout_path = output_directory / f"{request_id}-stdout.bin"
    stderr_path = output_directory / f"{request_id}-stderr.bin"
    observation_path = output_directory / f"{request_id}-observation.json"
    for label, path in (
        ("result artifact", result_path),
        ("stdout artifact", stdout_path),
        ("stderr artifact", stderr_path),
        ("observation artifact", observation_path),
    ):
        if path.exists():
            raise EvidenceLabError(
                f"Refusing to overwrite pre-existing {label}: {path}"
            )

    started = datetime.now(timezone.utc)
    process = subprocess.Popen(
        [str(executable), *arguments],
        cwd=str(working_directory),
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    timed_out = False
    try:
        stdout, stderr = process.communicate(timeout=execution["timeoutSeconds"])
    except subprocess.TimeoutExpired:
        timed_out = True
        process.kill()
        stdout, stderr = process.communicate()
    finished = datetime.now(timezone.utc)
    stdout_path.write_bytes(stdout)
    stderr_path.write_bytes(stderr)

    result_exists = result_path.is_file()
    failure_code = None
    failure_detail = None
    frozen_decision = None
    if timed_out:
        failure_code = "player-timeout"
    elif process.returncode != 0:
        failure_code = "player-exit-nonzero"
    elif not result_exists:
        failure_code = "result-artifact-missing"
    else:
        try:
            frozen_decision = validate_fixed_suite(load_json(result_path), request)
        except (EvidenceLabError, OSError, json.JSONDecodeError) as exception:
            failure_code = "invalid-fixed-suite-artifact"
            failure_detail = str(exception)
    succeeded = failure_code is None

    process_evidence_id = f"process-{request_id}-{process.pid}"
    observation = {
        "schemaVersion": OBSERVATION_SCHEMA_VERSION,
        "manifestId": plan["manifestId"],
        "sourceManifestSha256": plan["sourceManifestSha256"],
        "requestId": request_id,
        "observationId": hashlib.sha256(
            f"{process_evidence_id}|{started.isoformat()}".encode("utf-8")
        ).hexdigest()[:24],
        "evidenceOrigin": origin,
        "status": "succeeded" if succeeded else "failed",
        "failureCode": failure_code,
        "failureDetail": failure_detail,
        "targetId": request["targetId"],
        "deviceId": request["deviceId"],
        "deviceIdentitySha256": request["deviceIdentitySha256"],
        "deviceIdentityAttestation": request["deviceIdentityAttestation"],
        "cpuFamily": request["cpuFamily"],
        "isaId": request["isaId"],
        "operatingSystem": request["operatingSystem"],
        "environmentFingerprintSha256": confirmed_fingerprint,
        "workloadId": request["workloadId"],
        "scenarioId": request["scenarioId"],
        "contractVersion": request["contractVersion"],
        "workloadSchemaSha256": request["workloadSchemaSha256"],
        "candidateSchemaSha256": request["candidateSchemaSha256"],
        "settingsFingerprintSha256": execution["settingsFingerprintSha256"].upper(),
        "launchOrdinal": request["launchOrdinal"],
        "process": {
            "processEvidenceId": process_evidence_id,
            "processId": process.pid,
            "startedUtc": started.isoformat().replace("+00:00", "Z"),
            "finishedUtc": finished.isoformat().replace("+00:00", "Z"),
            "exitCode": process.returncode,
            "timedOut": timed_out,
        },
        "player": {
            **execution["player"],
            "binarySha256": actual_binary_hash,
        },
        "artifacts": {
            "stdoutArtifactPath": str(stdout_path),
            "stdoutSha256": sha256_file(stdout_path),
            "stderrArtifactPath": str(stderr_path),
            "stderrSha256": sha256_file(stderr_path),
            "resultArtifactPath": str(result_path),
            "resultArtifactSha256": sha256_file(result_path) if result_exists else None,
            "frozenDecisionAuthority": "ScenarioCalibrationProfile.FinalDecision",
            "frozenDecision": frozen_decision,
        },
        "counterEvidence": {
            "status": "unavailable",
            "artifacts": [],
            "reason": "No counter artifact was configured for this process request.",
        },
        "deviceConfirmation": {
            "method": "explicit-label-physical-identity-and-environment-fingerprint",
            "confirmedDeviceId": confirmed_device_id,
            "confirmedDeviceIdentitySha256": confirmed_identity,
        },
    }
    write_json(observation_path, observation)
    return observation


def validate_observation(
    observation: dict[str, Any], plan: dict[str, Any]
) -> dict[str, Any]:
    _require_mapping(observation, "observation")
    if observation.get("schemaVersion") != OBSERVATION_SCHEMA_VERSION:
        raise EvidenceLabError("Unsupported observation schemaVersion.")
    if observation.get("manifestId") != plan["manifestId"]:
        raise EvidenceLabError("Observation manifestId does not match the plan.")
    if observation.get("sourceManifestSha256") != plan["sourceManifestSha256"]:
        raise EvidenceLabError("Observation sourceManifestSha256 does not match the plan.")
    request_id = _require_string(observation.get("requestId"), "observation.requestId")
    request_map = {item["requestId"]: item for item in plan["requests"]}
    if request_id not in request_map:
        raise EvidenceLabError(f"Observation references unknown ready request {request_id}.")
    request = request_map[request_id]
    matching_fields = (
        "targetId",
        "deviceId",
        "deviceIdentitySha256",
        "cpuFamily",
        "isaId",
        "operatingSystem",
        "environmentFingerprintSha256",
        "workloadId",
        "scenarioId",
        "contractVersion",
        "workloadSchemaSha256",
        "candidateSchemaSha256",
        "launchOrdinal",
    )
    for field in matching_fields:
        if observation.get(field) != request.get(field):
            raise EvidenceLabError(
                f"Observation {request_id} field {field} does not match its request."
            )
    if observation.get("deviceIdentityAttestation") != request[
        "deviceIdentityAttestation"
    ]:
        raise EvidenceLabError(
            f"Observation {request_id} device identity attestation does not match."
        )
    if observation.get("settingsFingerprintSha256") != request["execution"][
        "settingsFingerprintSha256"
    ].upper():
        raise EvidenceLabError(
            f"Observation {request_id} settings fingerprint does not match its request."
        )
    if observation.get("evidenceOrigin") not in {"observed", "synthetic-fixture"}:
        raise EvidenceLabError(
            f"Observation {request_id} has an unsupported evidenceOrigin."
        )
    if observation.get("status") not in {"succeeded", "failed"}:
        raise EvidenceLabError(f"Observation {request_id} has an unsupported status.")
    process = _require_mapping(observation.get("process"), f"observation {request_id}.process")
    _require_string(
        process.get("processEvidenceId"), f"observation {request_id}.process.processEvidenceId"
    )
    _require_int(process.get("processId"), f"observation {request_id}.process.processId", 1)
    _require_int(process.get("exitCode"), f"observation {request_id}.process.exitCode", -2147483648)
    if not isinstance(process.get("timedOut"), bool):
        raise EvidenceLabError(
            f"observation {request_id}.process.timedOut must be boolean."
        )
    player = _require_mapping(observation.get("player"), f"observation {request_id}.player")
    expected_player = request["execution"]["player"]
    for field in (
        "buildType",
        "developmentBuild",
        "backend",
        "binarySha256",
        "sourceCommit",
        "suiteSchemaVersion",
    ):
        expected = (
            expected_player[field].upper()
            if field == "binarySha256"
            else expected_player[field]
        )
        if player.get(field) != expected:
            raise EvidenceLabError(
                f"Observation {request_id} Player field {field} does not match its request."
            )
    if player.get("buildType") != "Release" or player.get("developmentBuild") is not False:
        raise EvidenceLabError(f"Observation {request_id} is not a Release Player result.")
    artifacts = _require_mapping(
        observation.get("artifacts"), f"observation {request_id}.artifacts"
    )
    _require_sha256(
        artifacts.get("stdoutSha256"),
        f"observation {request_id}.artifacts.stdoutSha256",
    )
    _require_sha256(
        artifacts.get("stderrSha256"),
        f"observation {request_id}.artifacts.stderrSha256",
    )
    for field in (
        "stdoutArtifactPath",
        "stderrArtifactPath",
        "resultArtifactPath",
    ):
        value = artifacts.get(field)
        if value is not None and (not isinstance(value, str) or not value.strip()):
            raise EvidenceLabError(
                f"Observation {request_id} artifact path {field} must be non-empty."
            )
    if artifacts.get("frozenDecisionAuthority") != "ScenarioCalibrationProfile.FinalDecision":
        raise EvidenceLabError(
            f"Observation {request_id} does not retain the frozen decision authority."
        )
    result_hash = artifacts.get("resultArtifactSha256")
    if result_hash is not None:
        _require_sha256(
            result_hash,
            f"observation {request_id}.artifacts.resultArtifactSha256",
        )
    if observation["status"] == "succeeded":
        if result_hash is None:
            raise EvidenceLabError(
                f"Observation {request_id} succeeded without a result artifact hash."
            )
        if process["exitCode"] != 0:
            raise EvidenceLabError(
                f"Observation {request_id} cannot succeed with a non-zero process exit code."
            )
        if process["timedOut"]:
            raise EvidenceLabError(
                f"Observation {request_id} cannot succeed after a process timeout."
            )
        frozen = _require_mapping(
            artifacts.get("frozenDecision"),
            f"observation {request_id}.artifacts.frozenDecision",
        )
        if frozen.get("authority") != "ScenarioCalibrationProfile.FinalDecision":
            raise EvidenceLabError(
                f"Observation {request_id} has an invalid frozen decision authority."
            )
        if frozen.get("scenarioId") != request["scenarioId"] or frozen.get(
            "contractVersion"
        ) != request["contractVersion"]:
            raise EvidenceLabError(
                f"Observation {request_id} frozen scenario identity does not match."
            )
        for field in (
            "baselineCandidateId",
            "selectedCandidateId",
            "bestMeasuredCandidateId",
        ):
            _require_string(
                frozen.get(field),
                f"observation {request_id}.artifacts.frozenDecision.{field}",
            )
        if frozen.get("reselectionPerformed") is not False:
            raise EvidenceLabError(
                f"Observation {request_id} must not reselect the frozen decision."
            )
    confirmation = _require_mapping(
        observation.get("deviceConfirmation"),
        f"observation {request_id}.deviceConfirmation",
    )
    if confirmation.get("confirmedDeviceId") != request["deviceId"] or confirmation.get(
        "confirmedDeviceIdentitySha256"
    ) != request["deviceIdentitySha256"]:
        raise EvidenceLabError(
            f"Observation {request_id} device confirmation does not match its request."
        )
    return request


def verify_observation_artifacts(
    observation: dict[str, Any], request: dict[str, Any]
) -> dict[str, Any]:
    identity_attestation = verify_device_identity_attestation(request)
    if identity_attestation["evidenceOrigin"] != observation["evidenceOrigin"]:
        raise ArtifactVerificationError(
            "Device identity attestation origin does not match process evidence origin."
        )
    artifacts = observation["artifacts"]
    stdout_path = _verified_local_file(
        artifacts.get("stdoutArtifactPath"),
        artifacts.get("stdoutSha256"),
        "process stdout artifact",
    )
    stderr_path = _verified_local_file(
        artifacts.get("stderrArtifactPath"),
        artifacts.get("stderrSha256"),
        "process stderr artifact",
    )
    suite_path = _verified_local_file(
        artifacts.get("resultArtifactPath"),
        artifacts.get("resultArtifactSha256"),
        "fixed-suite result artifact",
    )
    try:
        suite = load_json(suite_path)
    except (OSError, json.JSONDecodeError) as exception:
        raise ArtifactVerificationError(
            f"Fixed-suite artifact is not valid JSON: {exception}"
        ) from exception
    try:
        frozen = validate_fixed_suite(suite, request)
    except EvidenceLabError as exception:
        raise ArtifactVerificationError(
            f"Fixed-suite validation failed: {exception}"
        ) from exception
    if artifacts.get("frozenDecision") != frozen:
        raise ArtifactVerificationError(
            "Copied frozen decision does not exactly match the retained fixed suite."
        )
    return {
        "status": "verified",
        "deviceIdentitySha256": request["deviceIdentitySha256"],
        "deviceIdentityAttestationOrigin": identity_attestation["evidenceOrigin"],
        "stdoutSha256": sha256_file(stdout_path),
        "stderrSha256": sha256_file(stderr_path),
        "resultArtifactSha256": sha256_file(suite_path),
        "frozenDecision": frozen,
    }


def calculate_matrix_coverage(
    manifest: dict[str, Any], process_observations: list[dict[str, Any]]
) -> list[dict[str, Any]]:
    """Pure grouping helper; callers decide which evidence origins are admissible."""
    rows: list[dict[str, Any]] = []
    for entry in sorted(
        manifest["matrix"], key=lambda item: (item["targetId"], item["workloadId"])
    ):
        matching = [
            observation
            for observation in process_observations
            if observation["targetId"] == entry["targetId"]
            and observation["workloadId"] == entry["workloadId"]
        ]
        device_identities = sorted(
            {item["deviceIdentitySha256"] for item in matching}
        )
        processes_by_device = {
            identity: sum(
                1
                for item in matching
                if item["deviceIdentitySha256"] == identity
            )
            for identity in device_identities
        }
        qualified_devices = sorted(
            identity
            for identity, process_count in processes_by_device.items()
            if process_count >= entry["requiredIndependentProcesses"]
        )
        rows.append(
            {
                "targetId": entry["targetId"],
                "workloadId": entry["workloadId"],
                "requiredIndependentProcesses": entry["requiredIndependentProcesses"],
                "processCount": len(matching),
                "distinctDeviceCount": len(device_identities),
                "qualifiedDeviceCount": len(qualified_devices),
                "status": "covered" if qualified_devices else "missing",
            }
        )
    return rows


def build_report(
    manifest: dict[str, Any], observations: list[dict[str, Any]]
) -> dict[str, Any]:
    plan = build_plan(manifest)
    seen_requests: set[str] = set()
    seen_observation_ids: set[str] = set()
    seen_process_evidence_ids: set[str] = set()
    accepted_observed: list[dict[str, Any]] = []
    verified_synthetic: list[dict[str, Any]] = []
    pending_verification: list[dict[str, Any]] = []
    rejected_verification: list[dict[str, Any]] = []
    failed: list[dict[str, Any]] = []
    for observation in sorted(observations, key=lambda item: item.get("requestId", "")):
        request = validate_observation(observation, plan)
        request_id = observation["requestId"]
        observation_id = _require_string(
            observation.get("observationId"), f"observation {request_id}.observationId"
        )
        if observation_id in seen_observation_ids:
            raise EvidenceLabError(f"Duplicate observationId {observation_id}.")
        seen_observation_ids.add(observation_id)
        if request_id in seen_requests:
            raise EvidenceLabError(f"Duplicate observation for request {request_id}.")
        seen_requests.add(request_id)
        process_evidence_id = observation["process"]["processEvidenceId"]
        if process_evidence_id in seen_process_evidence_ids:
            raise EvidenceLabError(
                f"Duplicate processEvidenceId {process_evidence_id}; independent requests "
                "must represent independent Player launches."
            )
        seen_process_evidence_ids.add(process_evidence_id)
        if observation["status"] != "succeeded":
            failed.append(observation)
            continue
        try:
            verify_observation_artifacts(observation, request)
        except (ArtifactUnavailableError, OSError) as exception:
            pending_verification.append(
                {
                    "observationId": observation_id,
                    "requestId": request_id,
                    "evidenceOrigin": observation["evidenceOrigin"],
                    "reason": str(exception),
                }
            )
            continue
        except (ArtifactVerificationError, EvidenceLabError) as exception:
            rejected_verification.append(
                {
                    "observationId": observation_id,
                    "requestId": request_id,
                    "evidenceOrigin": observation["evidenceOrigin"],
                    "reason": str(exception),
                }
            )
            continue
        if observation["evidenceOrigin"] == "synthetic-fixture":
            verified_synthetic.append(observation)
        else:
            accepted_observed.append(observation)

    generic_matrix_rows = calculate_matrix_coverage(manifest, accepted_observed)
    matrix_rows = [
        {
            "targetId": row["targetId"],
            "workloadId": row["workloadId"],
            "requiredIndependentProcesses": row["requiredIndependentProcesses"],
            "acceptedObservedProcessCount": row["processCount"],
            "acceptedObservedDeviceCount": row["distinctDeviceCount"],
            "qualifiedDeviceCount": row["qualifiedDeviceCount"],
            "status": row["status"],
        }
        for row in generic_matrix_rows
    ]

    device_summaries: list[dict[str, Any]] = []
    for identity in sorted(
        {item["deviceIdentitySha256"] for item in accepted_observed}
    ):
        matching = [
            item
            for item in accepted_observed
            if item["deviceIdentitySha256"] == identity
        ]
        device_summaries.append(
            {
                "deviceIdentitySha256": identity,
                "deviceIds": sorted({item["deviceId"] for item in matching}),
                "cpuFamily": matching[0]["cpuFamily"],
                "isaId": matching[0]["isaId"],
                "operatingSystem": matching[0]["operatingSystem"],
                "acceptedObservedProcessCount": len(matching),
                "processEvidenceIds": sorted(
                    item["process"]["processEvidenceId"] for item in matching
                ),
                "workloadIds": sorted({item["workloadId"] for item in matching}),
            }
        )

    pending_observed_count = sum(
        1 for item in pending_verification if item["evidenceOrigin"] == "observed"
    )
    pending_synthetic_count = sum(
        1
        for item in pending_verification
        if item["evidenceOrigin"] == "synthetic-fixture"
    )
    rejected_observed_count = sum(
        1 for item in rejected_verification if item["evidenceOrigin"] == "observed"
    )
    rejected_synthetic_count = sum(
        1
        for item in rejected_verification
        if item["evidenceOrigin"] == "synthetic-fixture"
    )
    if not accepted_observed and pending_observed_count > 0:
        scope_status = "pending-unverified-observed-evidence"
    elif not accepted_observed and rejected_observed_count > 0:
        scope_status = "rejected-observed-evidence"
    elif not accepted_observed:
        scope_status = "no-observed-evidence"
    elif all(row["status"] == "covered" for row in matrix_rows):
        scope_status = "manifest-targets-covered"
    else:
        scope_status = "partial-observed-evidence"

    normalized_observations = sorted(
        observations,
        key=lambda item: (item.get("requestId", ""), item.get("observationId", "")),
    )
    report_identity = {
        "artifactVerificationPolicyVersion": ARTIFACT_VERIFICATION_POLICY_VERSION,
        "manifestSha256": plan["sourceManifestSha256"],
        "observationSha256": sha256_json(normalized_observations),
    }
    failed_observed_count = sum(
        1 for item in failed if item["evidenceOrigin"] == "observed"
    )
    failed_synthetic_count = sum(
        1 for item in failed if item["evidenceOrigin"] == "synthetic-fixture"
    )
    return {
        "schemaVersion": REPORT_SCHEMA_VERSION,
        "reportId": sha256_json(report_identity)[:24],
        "manifestId": manifest["manifestId"],
        "sourceManifestSha256": plan["sourceManifestSha256"],
        "scopeStatus": scope_status,
        "selectionPolicy": {
            "candidateJoinKey": "CandidateDescriptor.CandidateId",
            "frozenDecisionAuthority": "ScenarioCalibrationProfile.FinalDecision",
            "reselectionPerformed": False,
        },
        "coverageSummary": {
            "acceptedObservedProcessCount": len(accepted_observed),
            "acceptedObservedDeviceCount": len(device_summaries),
            "acceptedObservedIsaIds": sorted(
                {item["isaId"] for item in accepted_observed}
            ),
            "acceptedObservedWorkloadIds": sorted(
                {item["workloadId"] for item in accepted_observed}
            ),
            "verifiedSyntheticFixtureSucceededProcessCount": len(verified_synthetic),
            "pendingUnverifiedObservedProcessCount": pending_observed_count,
            "pendingUnverifiedSyntheticFixtureProcessCount": pending_synthetic_count,
            "rejectedObservedProcessCount": rejected_observed_count,
            "rejectedSyntheticFixtureProcessCount": rejected_synthetic_count,
            "failedObservedProcessCount": failed_observed_count,
            "failedSyntheticFixtureProcessCount": failed_synthetic_count,
            "unsubmittedProcessRequestCount": len(plan["requests"]) - len(seen_requests),
            "blockedMatrixEntryCount": len(plan["blockedMatrixEntries"]),
        },
        "processVsDevice": {
            "processEvidenceUnit": "one independent Player launch",
            "deviceEvidenceUnit": "one verified deviceIdentitySha256",
            "deviceIdRole": "human-readable registration label only",
            "processesCountAsDevices": False,
        },
        "deviceSummaries": device_summaries,
        "matrixCoverage": matrix_rows,
        "syntheticFixtureSummary": {
            "verifiedSucceededProcessCount": len(verified_synthetic),
            "pendingUnverifiedProcessCount": pending_synthetic_count,
            "rejectedProcessCount": rejected_synthetic_count,
            "failedProcessCount": failed_synthetic_count,
            "countsTowardObservedCoverage": False,
            "verifiedObservationIds": sorted(
                item["observationId"] for item in verified_synthetic
            ),
        },
        "artifactVerification": {
            "policyVersion": ARTIFACT_VERIFICATION_POLICY_VERSION,
            "acceptedObservedProcessCount": len(accepted_observed),
            "verifiedSyntheticFixtureProcessCount": len(verified_synthetic),
            "pendingUnverified": pending_verification,
            "rejected": rejected_verification,
        },
        "crossDeviceStatistics": {
            "distinctObservedDeviceCount": len(device_summaries),
            "hierarchicalConfidenceIntervalComputed": False,
            "status": "not-computed",
        },
        "limitations": _limitations(
            scope_status,
            plan,
            device_summaries,
            len(pending_verification),
            len(rejected_verification),
        ),
    }


def _limitations(
    scope_status: str,
    plan: dict[str, Any],
    device_summaries: list[dict[str, Any]],
    pending_verification_count: int,
    rejected_verification_count: int,
) -> list[str]:
    limitations = [
        "This report validates provenance and matrix coverage; it does not reselect a layout.",
        "A process launch is not a device, and repeated processes do not increase device count.",
        "Device counts group verified stable identity hashes; DeviceId is only a label.",
        "No cross-device hierarchical confidence interval is computed by this scaffold.",
    ]
    if scope_status == "no-observed-evidence":
        limitations.append("No observed Release Player evidence was supplied.")
    elif scope_status == "pending-unverified-observed-evidence":
        limitations.append(
            "Observed claims were supplied, but none had locally verifiable retained artifacts."
        )
    elif scope_status == "rejected-observed-evidence":
        limitations.append(
            "Observed claims were supplied, but retained-artifact verification rejected all of them."
        )
    if plan["blockedMatrixEntries"]:
        limitations.append("One or more matrix entries are blocked by missing implementation or device setup.")
    if pending_verification_count:
        limitations.append(
            "One or more imported observations are pending because retained local artifacts could not be verified."
        )
    if rejected_verification_count:
        limitations.append(
            "One or more observations were rejected after retained-artifact verification failed."
        )
    if len(device_summaries) < 2:
        limitations.append("Fewer than two observed devices are present; no cross-device claim is supported.")
    return limitations


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as stream:
        return json.load(stream)


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


def load_observations(paths: Iterable[Path]) -> list[dict[str, Any]]:
    observations: list[dict[str, Any]] = []
    for path in paths:
        value = load_json(path)
        if isinstance(value, dict) and "observations" in value:
            observations.extend(_require_list(value["observations"], f"{path}.observations"))
        else:
            observations.append(_require_mapping(value, str(path)))
    return observations


def create_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Versioned device/ISA/workload evidence planning and reporting."
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    validate_parser = subparsers.add_parser("validate", help="Validate a manifest.")
    validate_parser.add_argument("manifest", type=Path)

    plan_parser = subparsers.add_parser("plan", help="Build a deterministic process run plan.")
    plan_parser.add_argument("manifest", type=Path)
    plan_parser.add_argument("--output", required=True, type=Path)

    run_parser = subparsers.add_parser("run", help="Execute one ready Release Player request.")
    run_parser.add_argument("manifest", type=Path)
    run_parser.add_argument("plan", type=Path)
    run_parser.add_argument("request_id")
    run_parser.add_argument("--output-directory", required=True, type=Path)
    run_parser.add_argument("--confirm-device-id", required=True)
    run_parser.add_argument("--confirm-device-identity", required=True)
    run_parser.add_argument("--confirm-environment-fingerprint", required=True)
    run_parser.add_argument(
        "--origin", required=True, choices=("observed", "synthetic-fixture")
    )
    run_parser.add_argument("--acknowledge-observed-evidence", action="store_true")

    report_parser = subparsers.add_parser(
        "report", help="Build a coverage report without reselecting decisions."
    )
    report_parser.add_argument("manifest", type=Path)
    report_parser.add_argument("--observation", action="append", type=Path, default=[])
    report_parser.add_argument("--output", required=True, type=Path)
    return parser


def main() -> int:
    parser = create_parser()
    arguments = parser.parse_args()
    try:
        if arguments.command == "validate":
            manifest = load_json(arguments.manifest)
            validate_manifest(manifest)
            print(
                f"Valid manifest schema {MANIFEST_SCHEMA_VERSION}: "
                f"{manifest['manifestId']} ({sha256_json(manifest)})"
            )
        elif arguments.command == "plan":
            plan = build_plan(load_json(arguments.manifest))
            write_json(arguments.output, plan)
            print(
                f"Wrote {len(plan['requests'])} ready process requests and "
                f"{len(plan['blockedMatrixEntries'])} blocked matrix entries."
            )
        elif arguments.command == "run":
            manifest = load_json(arguments.manifest)
            plan = load_json(arguments.plan)
            validate_plan_against_manifest(plan, manifest)
            observation = run_request(
                plan,
                arguments.request_id,
                arguments.output_directory,
                arguments.confirm_device_id,
                arguments.confirm_device_identity,
                arguments.confirm_environment_fingerprint,
                arguments.origin,
                arguments.acknowledge_observed_evidence,
            )
            print(
                f"Process observation {observation['observationId']}: "
                f"{observation['status']} ({observation['evidenceOrigin']})."
            )
        elif arguments.command == "report":
            report = build_report(
                load_json(arguments.manifest),
                load_observations(arguments.observation),
            )
            write_json(arguments.output, report)
            print(
                f"Report {report['reportId']}: {report['scopeStatus']}; "
                f"{report['coverageSummary']['acceptedObservedProcessCount']} observed processes, "
                f"{report['coverageSummary']['acceptedObservedDeviceCount']} observed devices."
            )
        else:
            parser.error("Unknown command.")
    except (EvidenceLabError, OSError, json.JSONDecodeError) as exception:
        parser.error(str(exception))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
