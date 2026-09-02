#!/usr/bin/env python3
"""Render a frozen schema-v1 Data Layout Calibrator advantage envelope.

The renderer validates agreement between frozen cell decisions, winner regions,
and summary values. It never evaluates candidate costs, confidence gates, Pareto
dominance, or any selection rule.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from collections import defaultdict
from pathlib import Path
from typing import Any, Iterable

from PIL import Image, ImageDraw, ImageFont


RENDERER_VERSION = "1.0.0"
SUPPORTED_DECISION_ENGINE_VERSION = "1.0.0"
UPPERCASE_HEX = frozenset("0123456789ABCDEF")

INK = (29, 40, 53)
MUTED = (91, 105, 121)
QUIET = (218, 225, 232)
PANEL = (247, 249, 252)
WHITE = (255, 255, 255)
BLUE = (37, 105, 151)
BLUE_DARK = (18, 67, 103)
GOLD = (176, 123, 28)
GOLD_LIGHT = (250, 241, 217)
GREY = (226, 229, 234)
GREY_DARK = (99, 106, 116)
RED = (169, 69, 63)
RED_LIGHT = (249, 229, 226)

STATUS_NAMES = {
    0: "INVALID",
    1: "TUNED AOS FALLBACK",
    2: "STATISTICAL GREY ZONE",
    3: "CREDIBLE ADVANTAGE",
    4: "HOLDOUT REJECTED",
}

STATUS_COLORS = {
    0: (GREY, GREY_DARK),
    1: (GOLD_LIGHT, INK),
    2: (GREY, INK),
    3: (BLUE, WHITE),
    4: (RED_LIGHT, RED),
}


class EnvelopeRenderContractError(ValueError):
    """Raised when a frozen envelope is incomplete or internally inconsistent."""


def _required(mapping: dict[str, Any], key: str, context: str) -> Any:
    if not isinstance(mapping, dict):
        raise EnvelopeRenderContractError(f"{context} must be an object.")
    if key not in mapping:
        raise EnvelopeRenderContractError(f"{context} is missing '{key}'.")
    return mapping[key]


def _non_empty_string(mapping: dict[str, Any], key: str, context: str) -> str:
    value = _required(mapping, key, context)
    if not isinstance(value, str) or not value:
        raise EnvelopeRenderContractError(f"{context}.{key} must be a non-empty string.")
    return value


def _string(mapping: dict[str, Any], key: str, context: str) -> str:
    value = _required(mapping, key, context)
    if not isinstance(value, str):
        raise EnvelopeRenderContractError(f"{context}.{key} must be a string.")
    return value


def _canonical_sha256(value: str, context: str) -> str:
    if len(value) != 64 or any(
        character not in UPPERCASE_HEX for character in value
    ):
        raise EnvelopeRenderContractError(
            f"{context} must contain exactly 64 uppercase hexadecimal characters."
        )
    return value


def _sha256(mapping: dict[str, Any], key: str, context: str) -> str:
    return _canonical_sha256(
        _non_empty_string(mapping, key, context), f"{context}.{key}"
    )


def _optional_sha256(mapping: dict[str, Any], key: str, context: str) -> str:
    value = _string(mapping, key, context)
    if value:
        _canonical_sha256(value, f"{context}.{key}")
    return value


def _number(mapping: dict[str, Any], key: str, context: str) -> float:
    value = _required(mapping, key, context)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise EnvelopeRenderContractError(f"{context}.{key} must be numeric.")
    result = float(value)
    if not math.isfinite(result):
        raise EnvelopeRenderContractError(f"{context}.{key} must be finite.")
    return result


def _integer(mapping: dict[str, Any], key: str, context: str) -> int:
    value = _required(mapping, key, context)
    if isinstance(value, bool) or not isinstance(value, int):
        raise EnvelopeRenderContractError(f"{context}.{key} must be an integer.")
    return value


def _font(size: int, *, bold: bool = False, mono: bool = False) -> ImageFont.FreeTypeFont:
    if mono:
        names = [
            "C:/Windows/Fonts/consola.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
            "DejaVuSansMono.ttf",
        ]
    elif bold:
        names = [
            "C:/Windows/Fonts/seguisb.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
            "DejaVuSans-Bold.ttf",
        ]
    else:
        names = [
            "C:/Windows/Fonts/segoeui.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "DejaVuSans.ttf",
        ]
    for name in names:
        try:
            return ImageFont.truetype(name, size=size)
        except OSError:
            continue
    raise EnvelopeRenderContractError("No supported TrueType font was found.")


def load_envelope(path: Path) -> tuple[dict[str, Any], str]:
    payload = path.read_bytes()
    try:
        envelope = json.loads(payload.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exception:
        raise EnvelopeRenderContractError(
            f"'{path}' is not a valid UTF-8 JSON envelope."
        ) from exception
    if not isinstance(envelope, dict):
        raise EnvelopeRenderContractError("Envelope root must be a JSON object.")
    return envelope, hashlib.sha256(payload).hexdigest().upper()


def _axis(axis: dict[str, Any], context: str) -> dict[str, Any]:
    element_count = _integer(axis, "ElementCount", context)
    lifetime = _integer(axis, "LifetimeTicks", context)
    hot_to_cold = _number(axis, "HotToColdRatio", context)
    workers = _integer(axis, "WorkerCount", context)
    execution = _non_empty_string(axis, "ExecutionPolicyId", context)
    if element_count <= 0 or lifetime <= 0 or hot_to_cold < 0 or workers <= 0:
        raise EnvelopeRenderContractError(f"{context} contains invalid axis values.")
    return {
        "ElementCount": element_count,
        "LifetimeTicks": lifetime,
        "HotToColdRatio": hot_to_cold,
        "WorkerCount": workers,
        "ExecutionPolicyId": execution,
    }


def _axis_key(axis: dict[str, Any]) -> tuple[str, int, float, int, int]:
    return (
        axis["ExecutionPolicyId"],
        axis["WorkerCount"],
        axis["HotToColdRatio"],
        axis["ElementCount"],
        axis["LifetimeTicks"],
    )


def _candidate_id(candidate: dict[str, Any], context: str) -> str:
    identifier = _non_empty_string(candidate, "CandidateId", context)
    # Explicit factor IDs are required, but they never participate in selecting
    # or inferring CandidateId.
    _non_empty_string(candidate, "LayoutPolicyId", context)
    _non_empty_string(candidate, "KernelPolicyId", context)
    _non_empty_string(candidate, "BatchPolicyId", context)
    _non_empty_string(candidate, "ExecutionPolicyId", context)
    if _integer(candidate, "LogicalBatchSize", context) <= 0:
        raise EnvelopeRenderContractError(
            f"{context}.LogicalBatchSize must be positive."
        )
    _integer(candidate, "SortOrder", context)
    baseline = _required(candidate, "IsTunedAoSBaseline", context)
    if not isinstance(baseline, bool):
        raise EnvelopeRenderContractError(
            f"{context}.IsTunedAoSBaseline must be boolean."
        )
    return identifier


def _close(left: float, right: float) -> bool:
    return math.isclose(left, right, rel_tol=1e-9, abs_tol=1e-9)


def _percentile_of_sorted(values: list[float], percentile: float) -> float:
    if len(values) == 1:
        return values[0]
    rank = (len(values) - 1) * percentile
    lower = int(rank)
    upper = min(lower + 1, len(values) - 1)
    fraction = rank - lower
    return values[lower] * (1.0 - fraction) + values[upper] * fraction


def build_render_model(envelope: dict[str, Any]) -> dict[str, Any]:
    """Copy frozen decisions into a render-only model without ranking candidates."""

    schema = _integer(envelope, "SchemaVersion", "envelope")
    if schema != 1:
        raise EnvelopeRenderContractError(
            f"Unsupported envelope schema {schema!r}; expected schema 1."
        )
    decision_engine_version = _non_empty_string(
        envelope, "DecisionEngineVersion", "envelope"
    )
    if decision_engine_version != SUPPORTED_DECISION_ENGINE_VERSION:
        raise EnvelopeRenderContractError(
            "Unsupported DecisionEngineVersion "
            f"{decision_engine_version!r}; expected {SUPPORTED_DECISION_ENGINE_VERSION!r}."
        )
    if _non_empty_string(envelope, "ArtifactType", "envelope") != "advantage-envelope":
        raise EnvelopeRenderContractError("ArtifactType must be 'advantage-envelope'.")
    if _required(envelope, "FinalDecisionLocked", "envelope") is not True:
        raise EnvelopeRenderContractError("Envelope decisions are not marked frozen.")
    if _required(envelope, "HoldoutCanRerank", "envelope") is not False:
        raise EnvelopeRenderContractError("Envelope incorrectly permits holdout reranking.")

    raw_cells = _required(envelope, "Cells", "envelope")
    if not isinstance(raw_cells, list) or not raw_cells:
        raise EnvelopeRenderContractError("Envelope must contain at least one frozen cell.")

    cells: list[dict[str, Any]] = []
    cell_by_key: dict[tuple[str, int, float, int, int], dict[str, Any]] = {}
    for index, raw in enumerate(raw_cells):
        context = f"envelope.Cells[{index}]"
        axis = _axis(_required(raw, "Axis", context), f"{context}.Axis")
        key = _axis_key(axis)
        if key in cell_by_key:
            raise EnvelopeRenderContractError(f"{context} duplicates an envelope axis.")

        outcomes = _required(raw, "CandidateOutcomes", context)
        if not isinstance(outcomes, list) or not outcomes:
            raise EnvelopeRenderContractError(f"{context}.CandidateOutcomes must not be empty.")
        candidate_ids: set[str] = set()
        baseline_roles: dict[str, bool] = {}
        for outcome_index, outcome in enumerate(outcomes):
            outcome_context = f"{context}.CandidateOutcomes[{outcome_index}]"
            _sha256(outcome, "SourceEvidenceHash", outcome_context)
            candidate = _required(outcome, "Candidate", outcome_context)
            identifier = _candidate_id(candidate, f"{outcome_context}.Candidate")
            if candidate["ExecutionPolicyId"] != axis["ExecutionPolicyId"]:
                raise EnvelopeRenderContractError(
                    f"{outcome_context} execution policy differs from the cell axis."
                )
            if identifier in candidate_ids:
                raise EnvelopeRenderContractError(
                    f"{context} contains duplicate CandidateId '{identifier}'."
                )
            candidate_ids.add(identifier)
            baseline_roles[identifier] = bool(candidate["IsTunedAoSBaseline"])

        baseline = _non_empty_string(raw, "BaselineCandidateId", context)
        best = _non_empty_string(raw, "BestMeasuredCandidateId", context)
        frozen = _non_empty_string(raw, "FrozenCalibrationWinnerCandidateId", context)
        selected = _non_empty_string(raw, "SelectedCandidateId", context)
        for role, identifier in (
            ("baseline", baseline),
            ("best measured", best),
            ("frozen calibration winner", frozen),
            ("selected", selected),
        ):
            if identifier not in candidate_ids:
                raise EnvelopeRenderContractError(
                    f"{context} {role} CandidateId '{identifier}' is absent from CandidateOutcomes."
                )
        if sum(baseline_roles.values()) != 1 or not baseline_roles[baseline]:
            raise EnvelopeRenderContractError(
                f"{context} must identify exactly one explicitly marked tuned AoS baseline."
            )

        status = _integer(raw, "Status", context)
        if status not in STATUS_NAMES:
            raise EnvelopeRenderContractError(f"{context} has unknown status {status}.")
        holdout_confirmed = _required(raw, "HoldoutConfirmed", context)
        if not isinstance(holdout_confirmed, bool):
            raise EnvelopeRenderContractError(f"{context}.HoldoutConfirmed must be boolean.")
        calibration_partition = _string(raw, "CalibrationPartitionId", context)
        holdout_partition = _string(raw, "HoldoutPartitionId", context)
        holdout_baseline_hash = _optional_sha256(
            raw, "HoldoutBaselineEvidenceHash", context
        )
        holdout_candidate_hash = _optional_sha256(
            raw, "HoldoutCandidateEvidenceHash", context
        )
        if status != 0 and not calibration_partition:
            raise EnvelopeRenderContractError(
                f"{context} valid decision is missing its calibration partition."
            )
        if status == 3:
            if (
                not holdout_confirmed
                or selected != frozen
                or selected == baseline
                or not holdout_partition
                or holdout_partition == calibration_partition
                or not holdout_baseline_hash
                or not holdout_candidate_hash
            ):
                raise EnvelopeRenderContractError(
                    f"{context} credible advantage does not select its holdout-confirmed frozen winner."
                )
            displayed_improvement = _number(raw, "HoldoutImprovementPercent", context)
            confidence = _required(raw, "HoldoutConfidenceInterval", context)
        else:
            if holdout_confirmed or selected != baseline:
                raise EnvelopeRenderContractError(
                    f"{context} fallback state must select tuned AoS without holdout confirmation."
                )
            holdout_confidence = _required(raw, "HoldoutConfidenceInterval", context)
            if status == 4 and _integer(
                holdout_confidence,
                "ReplicateCount",
                f"{context}.HoldoutConfidenceInterval",
            ) > 0:
                displayed_improvement = _number(raw, "HoldoutImprovementPercent", context)
                confidence = holdout_confidence
            else:
                displayed_improvement = _number(
                    raw, "CalibrationImprovementPercent", context
                )
                confidence = _required(raw, "CalibrationConfidenceInterval", context)

        cell = {
            **axis,
            "Status": status,
            "StatusName": STATUS_NAMES[status],
            "CalibrationPartitionId": calibration_partition,
            "HoldoutPartitionId": holdout_partition,
            "HoldoutBaselineEvidenceHash": holdout_baseline_hash,
            "HoldoutCandidateEvidenceHash": holdout_candidate_hash,
            "BaselineCandidateId": baseline,
            "BestMeasuredCandidateId": best,
            "FrozenCalibrationWinnerCandidateId": frozen,
            "SelectedCandidateId": selected,
            "HoldoutConfirmed": holdout_confirmed,
            "ImprovementPercent": displayed_improvement,
            "ConfidenceReplicateCount": _integer(
                confidence, "ReplicateCount", f"{context}.confidence"
            ),
            "ConfidenceLevel": _number(
                confidence, "ConfidenceLevel", f"{context}.confidence"
            ),
            "ConfidencePointEstimatePercent": _number(
                confidence, "PointEstimatePercent", f"{context}.confidence"
            ),
            "ConfidenceLowerPercent": _number(
                confidence, "LowerBoundPercent", f"{context}.confidence"
            ),
            "ConfidenceUpperPercent": _number(
                confidence, "UpperBoundPercent", f"{context}.confidence"
            ),
            "Reason": _non_empty_string(raw, "Reason", context),
        }
        cells.append(cell)
        cell_by_key[key] = cell

    cells.sort(key=_axis_key)
    _validate_regions(envelope, cell_by_key)
    summary = _validate_summary(envelope, cells)
    raw_policy = _required(envelope, "Policy", "envelope")
    policy = {
        "MinimumImprovementPercent": _number(
            raw_policy, "MinimumImprovementPercent", "envelope.Policy"
        ),
        "ConfidenceLevel": _number(
            raw_policy, "ConfidenceLevel", "envelope.Policy"
        ),
        "MinimumBootstrapReplicates": _integer(
            raw_policy, "MinimumBootstrapReplicates", "envelope.Policy"
        ),
        "MinimumCalibrationResidentSamples": _integer(
            raw_policy, "MinimumCalibrationResidentSamples", "envelope.Policy"
        ),
        "MinimumCalibrationBoundarySamples": _integer(
            raw_policy, "MinimumCalibrationBoundarySamples", "envelope.Policy"
        ),
        "MinimumHoldoutResidentSamples": _integer(
            raw_policy, "MinimumHoldoutResidentSamples", "envelope.Policy"
        ),
        "MinimumHoldoutBoundarySamples": _integer(
            raw_policy, "MinimumHoldoutBoundarySamples", "envelope.Policy"
        ),
    }
    if (
        policy["MinimumImprovementPercent"] < 0
        or not 0 < policy["ConfidenceLevel"] < 1
        or policy["MinimumBootstrapReplicates"] < 100
        or policy["MinimumCalibrationResidentSamples"] <= 0
        or policy["MinimumCalibrationBoundarySamples"] <= 0
        or policy["MinimumHoldoutResidentSamples"]
        < policy["MinimumCalibrationResidentSamples"]
        or policy["MinimumHoldoutBoundarySamples"]
        < policy["MinimumCalibrationBoundarySamples"]
    ):
        raise EnvelopeRenderContractError("envelope.Policy contains invalid or weakened gates.")

    contract_version = _integer(envelope, "ContractVersion", "envelope")
    if contract_version <= 0:
        raise EnvelopeRenderContractError("envelope.ContractVersion must be positive.")
    for cell in cells:
        if cell["Status"] != 0 and (
            cell["ConfidenceReplicateCount"] < policy["MinimumBootstrapReplicates"]
            or not _close(cell["ConfidenceLevel"], policy["ConfidenceLevel"])
            or not _close(
                cell["ConfidencePointEstimatePercent"], cell["ImprovementPercent"]
            )
            or cell["ConfidenceLowerPercent"] > cell["ConfidenceUpperPercent"]
        ):
            raise EnvelopeRenderContractError(
                "Frozen cell confidence fields disagree with envelope.Policy or each other."
            )

    return {
        "SchemaVersion": schema,
        "DecisionEngineVersion": decision_engine_version,
        "EnvelopeId": _non_empty_string(envelope, "EnvelopeId", "envelope"),
        "CreatedUtcIso8601": _non_empty_string(
            envelope, "CreatedUtcIso8601", "envelope"
        ),
        "ScenarioId": _non_empty_string(envelope, "ScenarioId", "envelope"),
        "ContractVersion": contract_version,
        "CandidateSetHash": _sha256(envelope, "CandidateSetHash", "envelope"),
        "MeasurementSchemaHash": _sha256(
            envelope, "MeasurementSchemaHash", "envelope"
        ),
        "EnvironmentFingerprint": _sha256(
            envelope, "EnvironmentFingerprint", "envelope"
        ),
        "CalibrationSettingsHash": _sha256(
            envelope, "CalibrationSettingsHash", "envelope"
        ),
        "HoldoutSettingsHash": _sha256(
            envelope, "HoldoutSettingsHash", "envelope"
        ),
        "CalibrationSourceArtifactId": _non_empty_string(
            envelope, "CalibrationSourceArtifactId", "envelope"
        ),
        "CalibrationSourceArtifactSha256": _sha256(
            envelope, "CalibrationSourceArtifactSha256", "envelope"
        ),
        "HoldoutSourceArtifactId": _non_empty_string(
            envelope, "HoldoutSourceArtifactId", "envelope"
        ),
        "HoldoutSourceArtifactSha256": _sha256(
            envelope, "HoldoutSourceArtifactSha256", "envelope"
        ),
        "EvidenceScope": _non_empty_string(envelope, "EvidenceScope", "envelope"),
        "CalibrationUncertaintyMethod": _non_empty_string(
            envelope, "CalibrationUncertaintyMethod", "envelope"
        ),
        "HoldoutUncertaintyMethod": _non_empty_string(
            envelope, "HoldoutUncertaintyMethod", "envelope"
        ),
        "Policy": policy,
        "Cells": cells,
        "Summary": summary,
    }


def _validate_regions(
    envelope: dict[str, Any],
    cell_by_key: dict[tuple[str, int, float, int, int], dict[str, Any]],
) -> None:
    regions = _required(envelope, "WinnerRegions", "envelope")
    if not isinstance(regions, list) or not regions:
        raise EnvelopeRenderContractError("Envelope WinnerRegions must not be empty.")
    covered: set[tuple[str, int, float, int, int]] = set()
    for index, region in enumerate(regions):
        context = f"envelope.WinnerRegions[{index}]"
        execution = _non_empty_string(region, "ExecutionPolicyId", context)
        workers = _integer(region, "WorkerCount", context)
        ratio = _number(region, "HotToColdRatio", context)
        elements = _integer(region, "ElementCount", context)
        status = _integer(region, "Status", context)
        selected = _non_empty_string(region, "SelectedCandidateId", context)
        sampled = _required(region, "SampledLifetimeTicks", context)
        if not isinstance(sampled, list) or not sampled:
            raise EnvelopeRenderContractError(f"{context}.SampledLifetimeTicks must not be empty.")
        if any(isinstance(value, bool) or not isinstance(value, int) or value <= 0 for value in sampled):
            raise EnvelopeRenderContractError(f"{context} contains invalid sampled lifetimes.")
        if sampled != sorted(sampled) or len(sampled) != len(set(sampled)):
            raise EnvelopeRenderContractError(f"{context} sampled lifetimes must be unique and sorted.")
        if _integer(region, "MinimumSampledLifetimeTicks", context) != sampled[0] or _integer(
            region, "MaximumSampledLifetimeTicks", context
        ) != sampled[-1]:
            raise EnvelopeRenderContractError(f"{context} lifetime bounds disagree with its samples.")

        for lifetime in sampled:
            key = (execution, workers, ratio, elements, lifetime)
            if key in covered:
                raise EnvelopeRenderContractError(f"{context} covers a cell more than once.")
            if key not in cell_by_key:
                raise EnvelopeRenderContractError(f"{context} refers to a missing envelope cell.")
            cell = cell_by_key[key]
            if cell["Status"] != status or cell["SelectedCandidateId"] != selected:
                raise EnvelopeRenderContractError(
                    f"{context} disagrees with the frozen cell decision."
                )
            covered.add(key)
    if covered != set(cell_by_key):
        raise EnvelopeRenderContractError("WinnerRegions do not cover every frozen cell exactly once.")


def _validate_summary(
    envelope: dict[str, Any], cells: list[dict[str, Any]]
) -> dict[str, Any]:
    raw = _required(envelope, "Summary", "envelope")
    counts = {
        "TotalCellCount": len(cells),
        "ValidCellCount": sum(cell["Status"] != 0 for cell in cells),
        "CredibleAdvantageCellCount": sum(cell["Status"] == 3 for cell in cells),
        "StatisticalGreyCellCount": sum(cell["Status"] == 2 for cell in cells),
        "AoSFallbackCellCount": sum(cell["Status"] == 1 for cell in cells),
        "HoldoutRejectedCellCount": sum(cell["Status"] == 4 for cell in cells),
    }
    for field, expected in counts.items():
        if _integer(raw, field, "envelope.Summary") != expected:
            raise EnvelopeRenderContractError(
                f"envelope.Summary.{field} disagrees with frozen cells."
            )

    coverage = 0.0
    if counts["ValidCellCount"]:
        coverage = counts["CredibleAdvantageCellCount"] * 100.0 / counts["ValidCellCount"]
    if not _close(
        _number(raw, "CredibleCoveragePercent", "envelope.Summary"), coverage
    ):
        raise EnvelopeRenderContractError(
            "envelope.Summary.CredibleCoveragePercent disagrees with frozen cells."
        )

    credible = [cell for cell in cells if cell["Status"] == 3]
    expected_metrics = {
        "PeakConfirmedImprovementPercent": 0.0,
        "MedianConfirmedImprovementPercent": 0.0,
        "FloorConfirmedImprovementPercent": 0.0,
        "WorstConfirmedConfidenceLowerBoundPercent": 0.0,
    }
    if credible:
        improvements = sorted(cell["ImprovementPercent"] for cell in credible)
        lower_bounds = sorted(cell["ConfidenceLowerPercent"] for cell in credible)
        expected_metrics = {
            "PeakConfirmedImprovementPercent": improvements[-1],
            "MedianConfirmedImprovementPercent": _percentile_of_sorted(
                improvements, 0.5
            ),
            "FloorConfirmedImprovementPercent": improvements[0],
            "WorstConfirmedConfidenceLowerBoundPercent": lower_bounds[0],
        }
    for field, expected in expected_metrics.items():
        if not _close(_number(raw, field, "envelope.Summary"), expected):
            raise EnvelopeRenderContractError(
                f"envelope.Summary.{field} disagrees with frozen cells."
            )

    return {
        **counts,
        "CredibleCoveragePercent": coverage,
        **expected_metrics,
    }


def decision_snapshot(model: dict[str, Any]) -> list[dict[str, Any]]:
    """Copy decision fields for the manifest without examining candidate costs."""

    return [
        {
            "ExecutionPolicyId": cell["ExecutionPolicyId"],
            "WorkerCount": cell["WorkerCount"],
            "HotToColdRatio": cell["HotToColdRatio"],
            "ElementCount": cell["ElementCount"],
            "LifetimeTicks": cell["LifetimeTicks"],
            "Status": cell["Status"],
            "StatusName": cell["StatusName"],
            "CalibrationPartitionId": cell["CalibrationPartitionId"],
            "HoldoutPartitionId": cell["HoldoutPartitionId"],
            "HoldoutBaselineEvidenceHash": cell["HoldoutBaselineEvidenceHash"],
            "HoldoutCandidateEvidenceHash": cell["HoldoutCandidateEvidenceHash"],
            "BaselineCandidateId": cell["BaselineCandidateId"],
            "BestMeasuredCandidateId": cell["BestMeasuredCandidateId"],
            "FrozenCalibrationWinnerCandidateId": cell[
                "FrozenCalibrationWinnerCandidateId"
            ],
            "SelectedCandidateId": cell["SelectedCandidateId"],
            "HoldoutConfirmed": cell["HoldoutConfirmed"],
            "ImprovementPercent": cell["ImprovementPercent"],
            "ConfidenceReplicateCount": cell["ConfidenceReplicateCount"],
            "ConfidenceLevel": cell["ConfidenceLevel"],
            "ConfidencePointEstimatePercent": cell[
                "ConfidencePointEstimatePercent"
            ],
            "ConfidenceLowerPercent": cell["ConfidenceLowerPercent"],
            "ConfidenceUpperPercent": cell["ConfidenceUpperPercent"],
        }
        for cell in model["Cells"]
    ]


def _text_width(draw: ImageDraw.ImageDraw, text: str, font: ImageFont.FreeTypeFont) -> int:
    bounds = draw.textbbox((0, 0), text, font=font)
    return bounds[2] - bounds[0]


def _draw_centered(
    draw: ImageDraw.ImageDraw,
    bounds: tuple[int, int, int, int],
    text: str,
    font: ImageFont.FreeTypeFont,
    fill: tuple[int, int, int],
) -> None:
    text_bounds = draw.textbbox((0, 0), text, font=font)
    width = text_bounds[2] - text_bounds[0]
    height = text_bounds[3] - text_bounds[1]
    x = bounds[0] + (bounds[2] - bounds[0] - width) / 2
    y = bounds[1] + (bounds[3] - bounds[1] - height) / 2 - text_bounds[1]
    draw.text((x, y), text, font=font, fill=fill)


def _group_cells(model: dict[str, Any]) -> list[tuple[tuple[str, int, float], list[dict[str, Any]]]]:
    groups: dict[tuple[str, int, float], list[dict[str, Any]]] = defaultdict(list)
    for cell in model["Cells"]:
        groups[
            (
                cell["ExecutionPolicyId"],
                cell["WorkerCount"],
                cell["HotToColdRatio"],
            )
        ].append(cell)
    return sorted(groups.items())


def _render_heatmap_image(
    model: dict[str, Any], input_hash: str, highlighted_lifetime: int | None = None
) -> Image.Image:
    groups = _group_cells(model)
    lifetimes = sorted({cell["LifetimeTicks"] for cell in model["Cells"]})
    elements = sorted({cell["ElementCount"] for cell in model["Cells"]})
    cell_width = 190
    label_width = 235
    grid_width = max(1, len(lifetimes)) * cell_width
    width = max(1080, label_width + grid_width + 100)
    row_height = 104
    panel_heights = [105 + len({cell["ElementCount"] for cell in cells}) * row_height for _, cells in groups]
    height = 190 + sum(panel_heights) + 88

    image = Image.new("RGB", (width, height), WHITE)
    draw = ImageDraw.Draw(image)
    title_font = _font(36, bold=True)
    subtitle_font = _font(17)
    panel_font = _font(21, bold=True)
    axis_font = _font(14, bold=True)
    value_font = _font(17, bold=True, mono=True)
    small_font = _font(12)
    footer_font = _font(12, mono=True)

    draw.text((48, 34), "Frozen lifetime advantage envelope", font=title_font, fill=INK)
    subtitle = (
        f"{model['ScenarioId']} contract {model['ContractVersion']} · {model['EvidenceScope']} · "
        "all winners copied from immutable cell decisions"
    )
    draw.text((50, 88), subtitle, font=subtitle_font, fill=MUTED)
    coverage = model["Summary"]["CredibleCoveragePercent"]
    draw.text(
        (50, 122),
        f"credible coverage {coverage:.1f}% · blue = holdout-confirmed · gold/grey/red = tuned AoS",
        font=subtitle_font,
        fill=MUTED,
    )

    y = 160
    for (execution, workers, ratio), group_cells in groups:
        group_elements = sorted({cell["ElementCount"] for cell in group_cells})
        group_lifetimes = sorted({cell["LifetimeTicks"] for cell in group_cells})
        panel_height = 105 + len(group_elements) * row_height
        draw.rounded_rectangle(
            (30, y, width - 30, y + panel_height - 12),
            radius=16,
            fill=PANEL,
            outline=QUIET,
            width=2,
        )
        draw.text(
            (52, y + 18),
            f"{execution} · workers {workers} · hot:cold {ratio:g}",
            font=panel_font,
            fill=INK,
        )
        grid_left = label_width
        header_y = y + 58
        draw.text((52, header_y + 17), "ELEMENTS / LIFETIME", font=axis_font, fill=MUTED)
        for column, lifetime in enumerate(group_lifetimes):
            bounds = (
                grid_left + column * cell_width,
                header_y,
                grid_left + (column + 1) * cell_width - 8,
                header_y + 48,
            )
            color = BLUE_DARK if highlighted_lifetime == lifetime else MUTED
            _draw_centered(draw, bounds, f"{lifetime:,} ticks", axis_font, color)
            if highlighted_lifetime == lifetime:
                draw.line((bounds[0] + 15, bounds[3] - 4, bounds[2] - 15, bounds[3] - 4), fill=BLUE_DARK, width=4)

        by_coordinate = {
            (cell["ElementCount"], cell["LifetimeTicks"]): cell
            for cell in group_cells
        }
        for row, element_count in enumerate(group_elements):
            row_y = header_y + 48 + row * row_height
            draw.text((52, row_y + 37), f"{element_count:,}", font=panel_font, fill=INK)
            for column, lifetime in enumerate(group_lifetimes):
                left = grid_left + column * cell_width
                bounds = (left, row_y, left + cell_width - 8, row_y + row_height - 8)
                cell = by_coordinate.get((element_count, lifetime))
                if cell is None:
                    draw.rounded_rectangle(bounds, radius=10, fill=GREY, outline=QUIET)
                    _draw_centered(draw, bounds, "NOT SCANNED", small_font, MUTED)
                    continue
                fill, text_color = STATUS_COLORS[cell["Status"]]
                draw.rounded_rectangle(bounds, radius=10, fill=fill, outline=WHITE, width=2)
                if highlighted_lifetime == lifetime:
                    draw.rounded_rectangle(bounds, radius=10, outline=BLUE_DARK, width=5)
                _draw_centered(
                    draw,
                    (bounds[0] + 4, bounds[1] + 8, bounds[2] - 4, bounds[1] + 37),
                    cell["StatusName"],
                    small_font,
                    text_color,
                )
                _draw_centered(
                    draw,
                    (bounds[0] + 4, bounds[1] + 34, bounds[2] - 4, bounds[1] + 70),
                    cell["SelectedCandidateId"],
                    value_font,
                    text_color,
                )
                improvement = cell["ImprovementPercent"]
                _draw_centered(
                    draw,
                    (bounds[0] + 4, bounds[1] + 68, bounds[2] - 4, bounds[3] - 4),
                    f"{improvement:+.2f}% · CI low {cell['ConfidenceLowerPercent']:+.2f}%",
                    small_font,
                    text_color,
                )
        y += panel_height

    footer_y = height - 58
    draw.line((48, footer_y - 10, width - 48, footer_y - 10), fill=QUIET, width=2)
    draw.text(
        (48, footer_y),
        f"Envelope {model['EnvelopeId']} · input SHA256 {input_hash[:16]}… · renderer validates but never selects",
        font=footer_font,
        fill=MUTED,
    )
    return image


def render_heatmap(model: dict[str, Any], input_hash: str, output_path: Path) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    _render_heatmap_image(model, input_hash).save(output_path, format="PNG", optimize=True)


def render_lifetime_gif(model: dict[str, Any], input_hash: str, output_path: Path) -> None:
    lifetimes = sorted({cell["LifetimeTicks"] for cell in model["Cells"]})
    sequence: list[int | None] = [None, None]
    for lifetime in lifetimes:
        sequence.extend([lifetime, lifetime])
    sequence.extend([None, None])
    frames = [_render_heatmap_image(model, input_hash, lifetime) for lifetime in sequence]
    durations = [450, 450] + [500, 500] * len(lifetimes) + [650, 900]
    output_path.parent.mkdir(parents=True, exist_ok=True)
    frames[0].save(
        output_path,
        format="GIF",
        save_all=True,
        append_images=frames[1:],
        duration=durations,
        loop=0,
        disposal=2,
        optimize=False,
    )


def render_artifacts(input_path: Path, output_directory: Path) -> dict[str, Path]:
    envelope, input_hash = load_envelope(input_path)
    model = build_render_model(envelope)
    outputs = {
        "heatmap": output_directory / "advantage-envelope-heatmap.png",
        "lifetime_gif": output_directory / "advantage-envelope-lifetime.gif",
        "manifest": output_directory / "advantage-envelope-render-manifest.json",
    }
    render_heatmap(model, input_hash, outputs["heatmap"])
    render_lifetime_gif(model, input_hash, outputs["lifetime_gif"])

    manifest = {
        "Renderer": "Data Layout Calibrator frozen advantage-envelope renderer",
        "RendererVersion": RENDERER_VERSION,
        "DecisionEngineVersion": model["DecisionEngineVersion"],
        "InputPath": input_path.as_posix(),
        "InputSha256": input_hash,
        "SchemaVersion": model["SchemaVersion"],
        "EnvelopeId": model["EnvelopeId"],
        "ScenarioId": model["ScenarioId"],
        "ContractVersion": model["ContractVersion"],
        "CandidateSetHash": model["CandidateSetHash"],
        "MeasurementSchemaHash": model["MeasurementSchemaHash"],
        "EnvironmentFingerprint": model["EnvironmentFingerprint"],
        "CalibrationSettingsHash": model["CalibrationSettingsHash"],
        "HoldoutSettingsHash": model["HoldoutSettingsHash"],
        "CalibrationSourceArtifactId": model["CalibrationSourceArtifactId"],
        "CalibrationSourceArtifactSha256": model[
            "CalibrationSourceArtifactSha256"
        ],
        "HoldoutSourceArtifactId": model["HoldoutSourceArtifactId"],
        "HoldoutSourceArtifactSha256": model["HoldoutSourceArtifactSha256"],
        "EvidenceScope": model["EvidenceScope"],
        "CalibrationUncertaintyMethod": model["CalibrationUncertaintyMethod"],
        "HoldoutUncertaintyMethod": model["HoldoutUncertaintyMethod"],
        "Policy": model["Policy"],
        "SelectionContract": (
            "Every selected CandidateId and status is copied from a frozen envelope cell; "
            "the renderer contains no cost ranking, confidence gate, Pareto, or selection algorithm."
        ),
        "Summary": model["Summary"],
        "Decisions": decision_snapshot(model),
        "Outputs": {
            "Heatmap": outputs["heatmap"].name,
            "LifetimeGif": outputs["lifetime_gif"].name,
        },
    }
    output_directory.mkdir(parents=True, exist_ok=True)
    outputs["manifest"].write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    return outputs


def _parse_args(arguments: Iterable[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Render a frozen schema-v1 advantage-envelope.json without reselecting."
    )
    parser.add_argument("input", type=Path, help="Path to advantage-envelope.json")
    parser.add_argument("output_directory", type=Path, help="Directory for PNG, GIF, and manifest")
    return parser.parse_args(arguments)


def main(arguments: Iterable[str] | None = None) -> int:
    options = _parse_args(arguments)
    outputs = render_artifacts(options.input, options.output_directory)
    for name, path in outputs.items():
        print(f"{name}: {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
