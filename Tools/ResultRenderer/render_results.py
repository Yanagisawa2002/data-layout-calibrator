#!/usr/bin/env python3
"""Render immutable Data Layout Calibrator suite results.

The renderer deliberately has no selection algorithm. Candidate measurements control
cell labels and visual scales; every baseline, best, and selected marker is copied
verbatim from FinalDecision in the input suite.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import math
from pathlib import Path
from typing import Any, Iterable

from PIL import Image, ImageDraw, ImageFont


RENDERER_VERSION = "1.1.0"

INK = (31, 42, 55)
MUTED = (91, 105, 121)
QUIET = (222, 228, 234)
PANEL = (248, 250, 252)
WHITE = (255, 255, 255)
BLUE_DARK = (20, 73, 112)
BLUE = (41, 112, 160)
BLUE_LIGHT = (226, 239, 248)
GOLD = (180, 126, 27)
GOLD_LIGHT = (250, 241, 217)
INVALID = (239, 241, 244)

STATUS_NAMES = {
    0: "INVALID",
    1: "INCONCLUSIVE · AOS RETAINED",
    2: "OPTIMIZED",
    3: "STATISTICAL TIE · AOS RETAINED",
    4: "REGRESSION · AOS RETAINED",
}

EVIDENCE_SCOPE_NAMES = {
    0: "single Player",
    1: "multiple processes / one device",
    2: "multiple devices",
}

ESTIMATOR_NAMES = {
    1: "legacy independent percent",
    2: "paired-block log-ratio",
    3: "process-hierarchical log-ratio",
}


class RenderContractError(ValueError):
    """Raised when a fixed result cannot be rendered without inventing semantics."""


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
    raise RenderContractError("No supported TrueType font was found for static rendering.")


def _required(mapping: dict[str, Any], key: str, context: str) -> Any:
    if key not in mapping:
        raise RenderContractError(f"{context} is missing '{key}'.")
    return mapping[key]


def _candidate_id(candidate: dict[str, Any], context: str) -> str:
    value = _required(candidate, "CandidateId", context)
    return _canonical_identifier(value, f"{context}.CandidateId")


def _canonical_identifier(value: Any, context: str) -> str:
    if not isinstance(value, str) or not value or value != value.strip():
        raise RenderContractError(
            f"{context} must be a non-empty string without surrounding whitespace."
        )
    return value


def load_suite(path: Path) -> tuple[dict[str, Any], str]:
    payload = path.read_bytes()
    try:
        suite = json.loads(payload.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exception:
        raise RenderContractError(f"'{path}' is not a valid UTF-8 JSON suite.") from exception
    if not isinstance(suite, dict):
        raise RenderContractError("Suite root must be a JSON object.")
    return suite, hashlib.sha256(payload).hexdigest().upper()


def build_render_model(suite: dict[str, Any]) -> dict[str, Any]:
    """Validate display inputs and copy immutable decisions into a render-only model.

    This function never ranks candidate measurements. In particular, SelectedId,
    BaselineId, and BestId are read only from FinalDecision.
    """

    schema = _required(suite, "SchemaVersion", "suite")
    if schema not in (2, 3):
        raise RenderContractError(
            f"Unsupported suite schema {schema!r}; expected schema 2 or 3."
        )

    raw_scenarios = _required(suite, "Scenarios", "suite")
    if not isinstance(raw_scenarios, list) or not raw_scenarios:
        raise RenderContractError("Suite must contain at least one scenario.")

    scenarios: list[dict[str, Any]] = []
    for scenario_index, raw in enumerate(raw_scenarios):
        context = f"suite.Scenarios[{scenario_index}]"
        descriptor = _required(raw, "Scenario", context)
        scenario_id = _required(descriptor, "ScenarioId", f"{context}.Scenario")
        if schema >= 3:
            scenario_id = _canonical_identifier(
                scenario_id, f"{context}.Scenario.ScenarioId"
            )
        display_name = _required(descriptor, "DisplayName", f"{context}.Scenario")
        scenario_contract_version = int(descriptor.get("ContractVersion", 0))
        if schema >= 3 and scenario_contract_version <= 0:
            raise RenderContractError(
                f"{context}.Scenario requires a positive ContractVersion."
            )
        decision = _required(raw, "FinalDecision", context)
        results = _required(raw, "CalibrationResults", context)
        if not isinstance(results, list) or not results:
            raise RenderContractError(f"{context}.CalibrationResults must not be empty.")

        cells: list[dict[str, Any]] = []
        candidate_ids: set[str] = set()
        factor_coordinates: set[tuple[str, int]] = set()
        for result_index, result in enumerate(results):
            result_context = f"{context}.CalibrationResults[{result_index}]"
            candidate = _required(result, "Candidate", result_context)
            if schema >= 3:
                if str(_required(result, "ScenarioId", result_context)) != str(scenario_id):
                    raise RenderContractError(
                        f"{result_context} ScenarioId disagrees with its enclosing scenario."
                    )
                if int(_required(result, "ScenarioContractVersion", result_context)) != scenario_contract_version:
                    raise RenderContractError(
                        f"{result_context} ContractVersion disagrees with its enclosing scenario."
                    )
            identifier = _candidate_id(candidate, f"{result_context}.Candidate")
            if identifier in candidate_ids:
                raise RenderContractError(
                    f"{context} contains duplicate candidate id '{identifier}'."
                )
            candidate_ids.add(identifier)

            latency = _required(result, "AmortizedLatency", result_context)
            p95_ms = _required(latency, "P95Milliseconds", f"{result_context}.AmortizedLatency")
            is_valid = bool(result.get("Completed")) and bool(result.get("ParityPassed"))
            if is_valid and (not isinstance(p95_ms, (int, float)) or p95_ms <= 0):
                raise RenderContractError(f"{result_context} has an invalid P95 measurement.")

            layout_id = _canonical_identifier(
                _required(candidate, "LayoutId", result_context),
                f"{result_context}.Candidate.LayoutId",
            )
            layout_policy = candidate.get("Layout") or {}
            kernel_policy = candidate.get("Kernel") or {}
            batch_policy = candidate.get("Batch") or {}
            execution_policy = candidate.get("Execution") or {}
            explicit_factors = schema >= 3 and all(
                isinstance(policy, dict) and policy.get("PolicyId")
                for policy in (
                    layout_policy,
                    kernel_policy,
                    batch_policy,
                    execution_policy,
                )
            )
            if schema >= 3 and not explicit_factors:
                raise RenderContractError(
                    f"{result_context}.Candidate is missing explicit schema-3 factor policies."
                )
            layout_factor = str(layout_policy.get("PolicyId") or layout_id)
            kernel_factor = str(kernel_policy.get("PolicyId") or "LegacyUnspecified")
            batch_factor = str(batch_policy.get("PolicyId") or "LegacyBatch")
            execution_factor = str(
                execution_policy.get("PolicyId") or "FrameFaithful"
            )
            logical_batch = int(
                _required(candidate, "LogicalBatchSize", result_context)
            )
            if schema >= 3:
                layout_factor = _canonical_identifier(
                    layout_factor, f"{result_context}.Candidate.Layout.PolicyId"
                )
                kernel_factor = _canonical_identifier(
                    kernel_factor, f"{result_context}.Candidate.Kernel.PolicyId"
                )
                batch_factor = _canonical_identifier(
                    batch_factor, f"{result_context}.Candidate.Batch.PolicyId"
                )
                execution_factor = _canonical_identifier(
                    execution_factor,
                    f"{result_context}.Candidate.Execution.PolicyId",
                )
                if int(candidate.get("PolicySchemaVersion", 0)) != 1:
                    raise RenderContractError(
                        f"{result_context}.Candidate has an unsupported policy schema."
                    )
                if layout_factor != layout_id:
                    raise RenderContractError(
                        f"{result_context}.Candidate LayoutId disagrees with Layout.PolicyId."
                    )
                if int(batch_policy.get("LogicalBatchSize", 0)) != logical_batch:
                    raise RenderContractError(
                        f"{result_context}.Candidate logical batch fields disagree."
                    )
            row_id = "\x1f".join(
                (layout_factor, kernel_factor, batch_factor, execution_factor)
            )
            factor_coordinate = (row_id, logical_batch)
            if factor_coordinate in factor_coordinates:
                raise RenderContractError(
                    f"{context} contains a duplicate factor/batch coordinate."
                )
            factor_coordinates.add(factor_coordinate)

            cells.append(
                {
                    "CandidateId": identifier,
                    "LayoutId": layout_id,
                    "LayoutPolicyId": layout_factor,
                    "KernelPolicyId": kernel_factor,
                    "BatchPolicyId": batch_factor,
                    "ExecutionPolicyId": execution_factor,
                    "RowId": row_id,
                    "ExplicitFactors": explicit_factors,
                    "Batch": logical_batch,
                    "SortOrder": int(candidate.get("SortOrder", 0)),
                    "P95Microseconds": float(p95_ms) * 1000.0 if is_valid else None,
                    "Valid": is_valid,
                }
            )

        baseline_id = _candidate_id(
            _required(decision, "BaselineCandidate", f"{context}.FinalDecision"),
            f"{context}.FinalDecision.BaselineCandidate",
        )
        selected_id = _candidate_id(
            _required(decision, "SelectedCandidate", f"{context}.FinalDecision"),
            f"{context}.FinalDecision.SelectedCandidate",
        )
        best_id = _candidate_id(
            _required(decision, "BestMeasuredCandidate", f"{context}.FinalDecision"),
            f"{context}.FinalDecision.BestMeasuredCandidate",
        )
        for role, identifier in (
            ("baseline", baseline_id),
            ("selected", selected_id),
            ("best measured", best_id),
        ):
            if identifier not in candidate_ids:
                raise RenderContractError(
                    f"{context} {role} candidate '{identifier}' is absent from CalibrationResults."
                )

        status = int(_required(decision, "Status", f"{context}.FinalDecision"))
        if status not in STATUS_NAMES:
            raise RenderContractError(f"{context} has unknown decision status {status}.")
        if status != 2 and selected_id != baseline_id:
            raise RenderContractError(
                f"{context} is a fallback decision but does not select its frozen AoS baseline."
            )
        if status == 2 and selected_id != best_id:
            raise RenderContractError(
                f"{context} is optimized but SelectedCandidate and BestMeasuredCandidate differ."
            )

        row_order: dict[str, int] = {}
        rows_by_id: dict[str, dict[str, str | bool]] = {}
        for cell in cells:
            row_id = cell["RowId"]
            row_order[row_id] = min(
                row_order.get(row_id, cell["SortOrder"]), cell["SortOrder"]
            )
            rows_by_id[row_id] = {
                "Id": row_id,
                "Layout": cell["LayoutPolicyId"],
                "Kernel": cell["KernelPolicyId"],
                "BatchPolicy": cell["BatchPolicyId"],
                "Execution": cell["ExecutionPolicyId"],
                "ExplicitFactors": cell["ExplicitFactors"],
            }
        factor_rows = [
            rows_by_id[row_id]
            for row_id in sorted(row_order, key=lambda name: (row_order[name], name))
        ]
        batches = sorted({cell["Batch"] for cell in cells})

        confidence = _required(
            decision,
            "ImprovementConfidenceInterval",
            f"{context}.FinalDecision",
        )
        if schema >= 3:
            sampling_design = _required(raw, "SamplingDesign", context)
            if int(sampling_design.get("SchemaVersion", 0)) != 1:
                raise RenderContractError(
                    f"{context}.SamplingDesign has an unsupported schema."
                )
            evidence_scope = int(
                _required(sampling_design, "EvidenceScope", f"{context}.SamplingDesign")
            )
            if evidence_scope not in EVIDENCE_SCOPE_NAMES:
                raise RenderContractError(
                    f"{context}.SamplingDesign has unknown evidence scope {evidence_scope}."
                )
            confidence_iterations = int(confidence.get("Iterations", 0))
            if confidence_iterations < 0:
                raise RenderContractError(
                    f"{context}.FinalDecision confidence interval has a negative iteration count."
                )
            if confidence_iterations > 0 and int(confidence.get("SchemaVersion", 0)) != 1:
                raise RenderContractError(
                    f"{context}.FinalDecision confidence interval has an unsupported schema."
                )
            evidence_scope_name = EVIDENCE_SCOPE_NAMES[evidence_scope]
            confidence_resampling_unit = str(confidence.get("ResamplingUnit") or "")
            if confidence_iterations > 0 and not confidence_resampling_unit:
                raise RenderContractError(
                    f"{context}.FinalDecision confidence interval is missing its resampling unit."
                )
            estimator_kind = int(confidence.get("EstimatorKind", 0))
            has_log_ratio_estimate = bool(confidence.get("HasLogRatioEstimate", False))
            if confidence_iterations > 0:
                confidence_level = float(confidence.get("ConfidenceLevel", 0.0))
                point_percent = float(confidence.get("PointEstimatePercent", 0.0))
                lower_percent = float(confidence.get("LowerBoundPercent", 0.0))
                upper_percent = float(confidence.get("UpperBoundPercent", 0.0))
                if (
                    not 0.0 < confidence_level < 1.0
                    or not all(
                        math.isfinite(value)
                        for value in (point_percent, lower_percent, upper_percent)
                    )
                    or lower_percent > upper_percent
                    or not str(confidence.get("Estimand") or "")
                ):
                    raise RenderContractError(
                        f"{context}.FinalDecision confidence interval has invalid metadata or percent bounds."
                    )
                if estimator_kind not in ESTIMATOR_NAMES:
                    raise RenderContractError(
                        f"{context}.FinalDecision confidence interval has an unsupported estimator."
                    )
                if ((estimator_kind == 1 and has_log_ratio_estimate) or
                        (estimator_kind in (2, 3) and not has_log_ratio_estimate)):
                    raise RenderContractError(
                        f"{context}.FinalDecision confidence interval has inconsistent log-ratio provenance."
                    )
                point_log_ratio = float(confidence.get("PointEstimateLogRatio", 0.0))
                lower_log_ratio = float(confidence.get("LowerBoundLogRatio", 0.0))
                upper_log_ratio = float(confidence.get("UpperBoundLogRatio", 0.0))
                if estimator_kind == 1 and any(
                    value != 0.0
                    for value in (point_log_ratio, lower_log_ratio, upper_log_ratio)
                ):
                    raise RenderContractError(
                        f"{context}.FinalDecision legacy percent interval must not expose realized log-ratio values."
                    )
                if estimator_kind == 1 and int(confidence.get("RandomSeed", 0)) != 0:
                    raise RenderContractError(
                        f"{context}.FinalDecision migrated legacy interval must not invent a bootstrap seed."
                    )
                if estimator_kind in (2, 3) and (
                    int(confidence.get("RandomSeed", 0)) == 0
                    or
                    not all(
                        math.isfinite(value)
                        for value in (point_log_ratio, lower_log_ratio, upper_log_ratio)
                    )
                    or lower_log_ratio > upper_log_ratio
                ):
                    raise RenderContractError(
                        f"{context}.FinalDecision confidence interval has invalid log-ratio bounds."
                    )
                if estimator_kind in (2, 3) and not all(
                    math.isclose(actual, expected, rel_tol=1e-9, abs_tol=1e-9)
                    for actual, expected in (
                        (point_percent, (1.0 - math.exp(point_log_ratio)) * 100.0),
                        (lower_percent, (1.0 - math.exp(upper_log_ratio)) * 100.0),
                        (upper_percent, (1.0 - math.exp(lower_log_ratio)) * 100.0),
                    )
                ):
                    raise RenderContractError(
                        f"{context}.FinalDecision confidence interval has inconsistent percentage transforms."
                    )
            elif (
                int(confidence.get("SchemaVersion", 0)) != 0
                or estimator_kind != 0
                or has_log_ratio_estimate
                or float(confidence.get("ConfidenceLevel", 0.0)) != 0.0
                or int(confidence.get("RandomSeed", 0)) != 0
                or bool(confidence.get("Estimand"))
                or bool(confidence.get("ResamplingUnit"))
                or any(
                    float(confidence.get(field, 0.0)) != 0.0
                    for field in (
                        "PointEstimateLogRatio",
                        "LowerBoundLogRatio",
                        "UpperBoundLogRatio",
                        "PointEstimatePercent",
                        "LowerBoundPercent",
                        "UpperBoundPercent",
                    )
                )
            ):
                raise RenderContractError(
                    f"{context}.FinalDecision absent confidence interval declares realized estimator metadata."
                )
        else:
            confidence_iterations = int(confidence.get("Iterations", 0))
            evidence_scope_name = "scope unspecified (schema 2)"
            confidence_resampling_unit = "schema-2 bootstrap"
            estimator_kind = 0
            has_log_ratio_estimate = False
        uncertainty_presentation = (
            f"{confidence_resampling_unit} CI · {evidence_scope_name}"
            if confidence_iterations > 0
            else f"descriptive only (no inferential CI) · {evidence_scope_name}"
        )
        scenarios.append(
            {
                "ScenarioId": str(scenario_id),
                "ScenarioContractVersion": scenario_contract_version,
                "DisplayName": str(display_name),
                "ElementCount": int(_required(raw, "ElementCount", context)),
                "LifetimeTicks": int(_required(raw, "LifetimeTicks", context)),
                "Cells": cells,
                "FactorRows": factor_rows,
                "Batches": batches,
                # The fields below are copied verbatim from FinalDecision. Do not derive
                # any of them from Cells.
                "Status": status,
                "StatusName": STATUS_NAMES[status],
                "BaselineId": baseline_id,
                "SelectedId": selected_id,
                "BestId": best_id,
                "BaselineP95Microseconds": float(
                    _required(decision, "BaselineP95Milliseconds", context)
                )
                * 1000.0,
                "BestP95Microseconds": float(
                    _required(decision, "BestMeasuredP95Milliseconds", context)
                )
                * 1000.0,
                "ImprovementPercent": float(
                    _required(decision, "ImprovementPercent", context)
                ),
                "ConfidenceIterations": confidence_iterations,
                "ConfidenceLevel": float(confidence.get("ConfidenceLevel", 0.0)),
                "ConfidenceLowerPercent": float(confidence.get("LowerBoundPercent", 0.0)),
                "ConfidenceUpperPercent": float(confidence.get("UpperBoundPercent", 0.0)),
                "ConfidenceResamplingUnit": confidence_resampling_unit,
                "ConfidenceEstimatorKind": estimator_kind,
                "ConfidenceEstimatorName": ESTIMATOR_NAMES.get(
                    estimator_kind,
                    "no realized estimator"
                    if schema >= 3
                    else "schema-2 estimator unspecified",
                ),
                "HasLogRatioEstimate": has_log_ratio_estimate,
                "EvidenceScopeName": evidence_scope_name,
                "UncertaintyPresentation": uncertainty_presentation,
                "Reason": str(_required(decision, "Reason", context)),
            }
        )

    environment = _required(suite, "Environment", "suite")
    return {
        "SchemaVersion": schema,
        "RunId": str(_required(suite, "RunId", "suite")),
        "CreatedUtcIso8601": str(_required(suite, "CreatedUtcIso8601", "suite")),
        "Backend": str(_required(environment, "ScriptingBackend", "suite.Environment")),
        "BuildType": str(_required(environment, "BuildType", "suite.Environment")),
        "UnityVersion": str(_required(environment, "UnityVersion", "suite.Environment")),
        "Scenarios": scenarios,
    }


def decision_snapshot(model: dict[str, Any]) -> list[dict[str, Any]]:
    """Return provenance fields for the manifest without inspecting measurements."""

    return [
        {
            "ScenarioId": scenario["ScenarioId"],
            "ScenarioContractVersion": scenario["ScenarioContractVersion"],
            "Status": scenario["Status"],
            "StatusName": scenario["StatusName"],
            "BaselineCandidateId": scenario["BaselineId"],
            "SelectedCandidateId": scenario["SelectedId"],
            "BestMeasuredCandidateId": scenario["BestId"],
            "BaselineP95Microseconds": scenario["BaselineP95Microseconds"],
            "BestMeasuredP95Microseconds": scenario["BestP95Microseconds"],
            "ImprovementPercent": scenario["ImprovementPercent"],
            "ConfidenceIterations": scenario["ConfidenceIterations"],
            "ConfidenceLevel": scenario["ConfidenceLevel"],
            "ConfidenceLowerPercent": scenario["ConfidenceLowerPercent"],
            "ConfidenceUpperPercent": scenario["ConfidenceUpperPercent"],
            "ConfidenceResamplingUnit": scenario["ConfidenceResamplingUnit"],
            "ConfidenceEstimatorKind": scenario["ConfidenceEstimatorKind"],
            "ConfidenceEstimatorName": scenario["ConfidenceEstimatorName"],
            "HasLogRatioEstimate": scenario["HasLogRatioEstimate"],
            "EvidenceScope": scenario["EvidenceScopeName"],
            "UncertaintyPresentation": scenario["UncertaintyPresentation"],
        }
        for scenario in model["Scenarios"]
    ]


def _mix(start: tuple[int, int, int], end: tuple[int, int, int], amount: float) -> tuple[int, int, int]:
    amount = max(0.0, min(1.0, amount))
    return tuple(round(left + (right - left) * amount) for left, right in zip(start, end))


def _cell_color(value: float | None, low: float, high: float) -> tuple[int, int, int]:
    if value is None:
        return INVALID
    if math.isclose(low, high):
        strength = 0.65
    else:
        # min/max set only the visual intensity. They never select or label a winner.
        strength = 1.0 - ((value - low) / (high - low))
    return _mix(BLUE_LIGHT, BLUE, 0.25 + 0.75 * strength)


def _text_width(draw: ImageDraw.ImageDraw, text: str, font: ImageFont.FreeTypeFont) -> int:
    box = draw.textbbox((0, 0), text, font=font)
    return box[2] - box[0]


def _ellipsize(
    draw: ImageDraw.ImageDraw,
    text: str,
    font: ImageFont.FreeTypeFont,
    maximum_width: int,
) -> str:
    if _text_width(draw, text, font) <= maximum_width:
        return text
    suffix = "…"
    low = 0
    high = len(text)
    while low < high:
        middle = (low + high + 1) // 2
        candidate = text[:middle] + suffix
        if _text_width(draw, candidate, font) <= maximum_width:
            low = middle
        else:
            high = middle - 1
    return text[:low] + suffix


def _draw_centered(
    draw: ImageDraw.ImageDraw,
    bounds: tuple[int, int, int, int],
    text: str,
    font: ImageFont.FreeTypeFont,
    fill: tuple[int, int, int],
) -> None:
    box = draw.textbbox((0, 0), text, font=font)
    width = box[2] - box[0]
    height = box[3] - box[1]
    x = bounds[0] + (bounds[2] - bounds[0] - width) / 2
    y = bounds[1] + (bounds[3] - bounds[1] - height) / 2 - box[1]
    draw.text((x, y), text, font=font, fill=fill)


def _draw_pill(
    draw: ImageDraw.ImageDraw,
    x: int,
    y: int,
    text: str,
    font: ImageFont.FreeTypeFont,
    fill: tuple[int, int, int],
    ink: tuple[int, int, int],
    outline: tuple[int, int, int] | None = None,
) -> tuple[int, int, int, int]:
    width = _text_width(draw, text, font) + 24
    height = 30
    bounds = (x, y, x + width, y + height)
    draw.rounded_rectangle(bounds, radius=15, fill=fill, outline=outline, width=2 if outline else 1)
    _draw_centered(draw, bounds, text, font, ink)
    return bounds


def _draw_dashed_rectangle(
    draw: ImageDraw.ImageDraw,
    bounds: tuple[int, int, int, int],
    color: tuple[int, int, int],
    width: int = 3,
    dash: int = 10,
) -> None:
    left, top, right, bottom = bounds
    for start in range(left, right, dash * 2):
        draw.line((start, top, min(start + dash, right), top), fill=color, width=width)
        draw.line((start, bottom, min(start + dash, right), bottom), fill=color, width=width)
    for start in range(top, bottom, dash * 2):
        draw.line((left, start, left, min(start + dash, bottom)), fill=color, width=width)
        draw.line((right, start, right, min(start + dash, bottom)), fill=color, width=width)


def render_heatmap(model: dict[str, Any], input_hash: str, output_path: Path) -> None:
    width = 1400
    grid_left = 380
    grid_right = width - 70
    cell_height = 96
    panel_heights = [
        130 + len(scenario["FactorRows"]) * cell_height
        for scenario in model["Scenarios"]
    ]
    height = 160 + sum(panel_heights) + 86

    image = Image.new("RGB", (width, height), WHITE)
    draw = ImageDraw.Draw(image)
    title_font = _font(38, bold=True)
    subtitle_font = _font(19)
    panel_title_font = _font(25, bold=True)
    axis_font = _font(16, bold=True)
    value_font = _font(23, bold=True, mono=True)
    id_font = _font(14, mono=True)
    small_font = _font(13, bold=True)
    footer_font = _font(14, mono=True)

    draw.text((60, 42), "Calibration candidates and frozen decisions", font=title_font, fill=INK)
    first = model["Scenarios"][0]
    subtitle = (
        f"Calibration-phase amortized P95 · µs/tick · {first['ElementCount']:,} records · "
        f"{model['Backend']} {model['BuildType']} · lower is faster · per-workload color scale"
    )
    draw.text((60, 96), subtitle, font=subtitle_font, fill=MUTED)

    y = 145
    for scenario, panel_height in zip(model["Scenarios"], panel_heights):
        panel_bounds = (40, y, width - 40, y + panel_height - 14)
        draw.rounded_rectangle(panel_bounds, radius=18, fill=PANEL, outline=QUIET, width=2)
        draw.text((64, y + 22), scenario["DisplayName"], font=panel_title_font, fill=INK)

        if scenario["Status"] == 2:
            badge = (
                f"OPTIMIZED · {scenario['SelectedId']} · "
                f"{scenario['ImprovementPercent']:.2f}% lower P95"
            )
            badge_fill, badge_ink, badge_outline = BLUE, WHITE, None
        else:
            badge = f"{scenario['StatusName']} · {scenario['SelectedId']}"
            badge_fill, badge_ink, badge_outline = GOLD_LIGHT, INK, GOLD
        badge = _ellipsize(draw, badge, small_font, 620)
        badge_width = _text_width(draw, badge, small_font) + 24
        _draw_pill(
            draw,
            width - 65 - badge_width,
            y + 20,
            badge,
            small_font,
            badge_fill,
            badge_ink,
            badge_outline,
        )

        batches = scenario["Batches"]
        cell_width = (grid_right - grid_left) // max(1, len(batches))
        header_y = y + 76
        draw.text((64, header_y + 10), "FACTORS / BATCH", font=axis_font, fill=MUTED)
        for column, batch in enumerate(batches):
            bounds = (
                grid_left + column * cell_width,
                header_y,
                grid_left + (column + 1) * cell_width - 8,
                header_y + 42,
            )
            _draw_centered(draw, bounds, f"batch {batch}", axis_font, MUTED)

        valid_values = [
            cell["P95Microseconds"]
            for cell in scenario["Cells"]
            if cell["P95Microseconds"] is not None
        ]
        low = min(valid_values)
        high = max(valid_values)
        by_coordinate = {
            (cell["RowId"], cell["Batch"]): cell for cell in scenario["Cells"]
        }

        for row, factor_row in enumerate(scenario["FactorRows"]):
            row_y = header_y + 47 + row * cell_height
            draw.text((64, row_y + 20), factor_row["Layout"], font=panel_title_font, fill=INK)
            if factor_row["ExplicitFactors"]:
                draw.text(
                    (64, row_y + 50),
                    factor_row["Kernel"],
                    font=id_font,
                    fill=MUTED,
                )
                draw.text(
                    (64, row_y + 68),
                    f"{factor_row['BatchPolicy']} · {factor_row['Execution']}",
                    font=id_font,
                    fill=MUTED,
                )
            for column, batch in enumerate(batches):
                cell = by_coordinate.get((factor_row["Id"], batch))
                left = grid_left + column * cell_width
                bounds = (left, row_y, left + cell_width - 8, row_y + cell_height - 8)
                if cell is None:
                    draw.rounded_rectangle(bounds, radius=10, fill=INVALID, outline=QUIET, width=1)
                    _draw_centered(draw, bounds, "N/A", id_font, MUTED)
                    continue

                fill = _cell_color(cell["P95Microseconds"], low, high)
                draw.rounded_rectangle(bounds, radius=10, fill=fill, outline=WHITE, width=2)
                text_color = WHITE if sum(fill) < 420 else INK
                if cell["P95Microseconds"] is None:
                    value_text = "REJECTED"
                else:
                    value_text = f"{cell['P95Microseconds']:.3f} µs"
                _draw_centered(
                    draw,
                    (bounds[0], bounds[1] + 31, bounds[2], bounds[3] - 20),
                    value_text,
                    value_font,
                    text_color,
                )
                _draw_centered(
                    draw,
                    (bounds[0], bounds[3] - 25, bounds[2], bounds[3] - 3),
                    _ellipsize(
                        draw,
                        cell["CandidateId"],
                        id_font,
                        (bounds[2] - bounds[0]) - 16,
                    ),
                    id_font,
                    text_color,
                )

                if cell["CandidateId"] == scenario["BaselineId"]:
                    _draw_pill(
                        draw,
                        bounds[0] + 8,
                        bounds[1] + 7,
                        "BASELINE",
                        small_font,
                        WHITE,
                        INK,
                    )
                if cell["CandidateId"] == scenario["SelectedId"]:
                    draw.rounded_rectangle(bounds, radius=10, outline=BLUE_DARK, width=6)
                    selected_width = _text_width(draw, "SELECTED", small_font) + 24
                    _draw_pill(
                        draw,
                        bounds[2] - selected_width - 7,
                        bounds[1] + 7,
                        "SELECTED",
                        small_font,
                        BLUE_DARK,
                        WHITE,
                    )
                if (
                    cell["CandidateId"] == scenario["BestId"]
                    and scenario["BestId"] != scenario["SelectedId"]
                ):
                    _draw_dashed_rectangle(draw, bounds, GOLD)

        y += panel_height

    footer_y = height - 68
    draw.line((60, footer_y - 12, width - 60, footer_y - 12), fill=QUIET, width=2)
    footer = (
        f"Run {model['RunId']} · input SHA256 {input_hash[:16]}… · "
        "markers copied from FinalDecision; renderer never ranks or selects"
    )
    draw.text((60, footer_y), footer, font=footer_font, fill=MUTED)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(output_path, format="PNG", optimize=True)


def _wrap(draw: ImageDraw.ImageDraw, text: str, font: ImageFont.FreeTypeFont, width: int) -> list[str]:
    words = text.split()
    lines: list[str] = []
    current = ""
    for word in words:
        candidate = word if not current else f"{current} {word}"
        if current and _text_width(draw, candidate, font) > width:
            lines.append(current)
            current = word
        else:
            current = candidate
    if current:
        lines.append(current)
    return lines


def _gif_frame(model: dict[str, Any], input_hash: str, progress: float) -> Image.Image:
    width = 1280
    card_height = 235
    height = 176 + card_height * len(model["Scenarios"]) + 76
    image = Image.new("RGB", (width, height), WHITE)
    draw = ImageDraw.Draw(image)
    title_font = _font(36, bold=True)
    subtitle_font = _font(18)
    card_title_font = _font(24, bold=True)
    label_font = _font(16, bold=True)
    value_font = _font(19, bold=True, mono=True)
    small_font = _font(14)
    footer_font = _font(13, mono=True)

    draw.text((56, 36), "AoS baseline → frozen layout decision", font=title_font, fill=INK)
    phase = "AoS baseline" if progress < 0.18 else "Frozen FinalDecision"
    phase_color = MUTED if progress < 0.18 else BLUE_DARK
    draw.text((58, 91), phase, font=subtitle_font, fill=phase_color)
    draw.text(
        (width - 460, 91),
        f"{model['Backend']} {model['BuildType']} · amortized P95 µs/tick",
        font=subtitle_font,
        fill=MUTED,
    )

    for index, scenario in enumerate(model["Scenarios"]):
        top = 138 + index * card_height
        bounds = (40, top, width - 40, top + card_height - 18)
        draw.rounded_rectangle(bounds, radius=18, fill=PANEL, outline=QUIET, width=2)
        draw.text((64, top + 20), scenario["DisplayName"], font=card_title_font, fill=INK)

        baseline = scenario["BaselineP95Microseconds"]
        target = scenario["BestP95Microseconds"] if scenario["Status"] == 2 else baseline
        current = baseline + (target - baseline) * progress
        scale_max = max(baseline, target, scenario["BestP95Microseconds"], 0.001) * 1.16
        bar_left = 280
        bar_right = width - 90
        bar_top = top + 91
        bar_height = 44
        usable = bar_right - bar_left
        baseline_right = bar_left + round(usable * baseline / scale_max)
        current_right = bar_left + round(usable * current / scale_max)

        draw.text((64, bar_top + 10), "latency", font=label_font, fill=MUTED)
        draw.rounded_rectangle(
            (bar_left, bar_top, baseline_right, bar_top + bar_height),
            radius=10,
            fill=WHITE,
            outline=MUTED,
            width=3,
        )
        draw.rounded_rectangle(
            (bar_left, bar_top, max(bar_left + 18, current_right), bar_top + bar_height),
            radius=10,
            fill=BLUE if scenario["Status"] == 2 else GOLD_LIGHT,
            outline=BLUE_DARK if scenario["Status"] == 2 else GOLD,
            width=2,
        )
        draw.text(
            (bar_left + 13, bar_top + 9),
            f"{current:.3f} µs",
            font=value_font,
            fill=WHITE if scenario["Status"] == 2 else INK,
        )
        draw.text(
            (baseline_right + 10, bar_top + 10),
            f"AoS {baseline:.3f}",
            font=value_font,
            fill=MUTED,
        )

        if progress < 0.18:
            decision_text = scenario["BaselineId"]
            badge_text = "BASELINE"
            badge_fill, badge_ink, badge_outline = WHITE, INK, MUTED
        elif scenario["Status"] == 2:
            decision_text = scenario["SelectedId"]
            badge_text = f"{scenario['ImprovementPercent']:.2f}% LOWER P95"
            badge_fill, badge_ink, badge_outline = BLUE_DARK, WHITE, None
        else:
            decision_text = scenario["SelectedId"]
            badge_text = scenario["StatusName"]
            badge_fill, badge_ink, badge_outline = GOLD_LIGHT, INK, GOLD

        draw.text(
            (64, top + 157),
            _ellipsize(draw, decision_text, value_font, 195),
            font=value_font,
            fill=INK,
        )
        _draw_pill(
            draw,
            280,
            top + 153,
            badge_text,
            label_font,
            badge_fill,
            badge_ink,
            badge_outline,
        )
        if progress >= 0.18 and scenario["ConfidenceIterations"] > 0:
            resampling = {
                "paired measurement block": "paired-block",
                "Player process, then paired measurement block": "process→block",
                "schema-2 bootstrap": "schema-2",
            }.get(scenario["ConfidenceResamplingUnit"], "declared-unit")
            ci = (
                f"{scenario['ConfidenceLevel']:.0%} {resampling} CI · "
                f"{scenario['EvidenceScopeName']} "
                f"[{scenario['ConfidenceLowerPercent']:.2f}%, "
                f"{scenario['ConfidenceUpperPercent']:.2f}%]"
            )
            draw.text(
                (610, top + 159),
                _ellipsize(draw, ci, small_font, width - 650),
                font=small_font,
                fill=MUTED,
            )
        elif progress >= 0.18:
            reason_lines = _wrap(
                draw,
                f"Descriptive only (no inferential CI) · {scenario['Reason']}",
                small_font,
                560,
            )
            draw.text((610, top + 155), reason_lines[0], font=small_font, fill=MUTED)

        if scenario["BestId"] != scenario["SelectedId"]:
            best_x = bar_left + round(usable * scenario["BestP95Microseconds"] / scale_max)
            for dash_y in range(bar_top - 5, bar_top + bar_height + 6, 9):
                draw.line((best_x, dash_y, best_x, min(dash_y + 5, bar_top + bar_height + 5)), fill=GOLD, width=3)

    footer_y = height - 53
    draw.text(
        (56, footer_y),
        f"SHA256 {input_hash[:16]}… · all candidate IDs and percentages copied from FinalDecision",
        font=footer_font,
        fill=MUTED,
    )
    return image


def render_comparison_gif(model: dict[str, Any], input_hash: str, output_path: Path) -> None:
    progress_values = [0.0, 0.0, 0.0, 0.10, 0.22, 0.38, 0.56, 0.72, 0.86, 1.0, 1.0, 1.0, 1.0, 1.0]
    frames = [_gif_frame(model, input_hash, progress) for progress in progress_values]
    durations = [260, 260, 560, 150, 150, 150, 150, 150, 150, 260, 260, 260, 260, 900]
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
    suite, input_hash = load_suite(input_path)
    model = build_render_model(suite)
    outputs = {
        "heatmap": output_directory / "data-layout-calibrator-heatmap.png",
        "comparison_gif": output_directory / "data-layout-calibrator-comparison.gif",
        "manifest": output_directory / "data-layout-calibrator-render-manifest.json",
    }
    render_heatmap(model, input_hash, outputs["heatmap"])
    render_comparison_gif(model, input_hash, outputs["comparison_gif"])

    manifest = {
        "Renderer": "Data Layout Calibrator fixed-result renderer",
        "RendererVersion": RENDERER_VERSION,
        "InputPath": input_path.as_posix(),
        "InputSha256": input_hash,
        "SchemaVersion": model["SchemaVersion"],
        "RunId": model["RunId"],
        "SelectionContract": (
            "All baseline, selected, and best markers are copied from FinalDecision; "
            "the renderer contains no candidate selection algorithm."
        ),
        "Decisions": decision_snapshot(model),
        "Outputs": {
            "Heatmap": outputs["heatmap"].name,
            "ComparisonGif": outputs["comparison_gif"].name,
        },
    }
    output_directory.mkdir(parents=True, exist_ok=True)
    outputs["manifest"].write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    return outputs


def _parse_args(arguments: Iterable[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Render a schema-2 or schema-3 fixed calibration suite without reselecting candidates."
    )
    parser.add_argument("input", type=Path, help="Path to calibration-suite.json")
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
