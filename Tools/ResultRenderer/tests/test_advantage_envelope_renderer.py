from __future__ import annotations

import copy
import json
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from render_advantage_envelope import (
    EnvelopeRenderContractError,
    build_render_model,
    render_artifacts,
)


def synthetic_sha(character: str) -> str:
    return character * 64


def fixed_synthetic_envelope() -> dict:
    def descriptor(identifier: str, baseline: bool) -> dict:
        return {
            "CandidateId": identifier,
            "CandidateDefinitionSha256": synthetic_sha("A" if baseline else "B"),
            "DisplayName": identifier,
            "LayoutPolicyId": "aos-layout-v1" if baseline else "soa-layout-v1",
            "KernelPolicyId": "synthetic-kernel-v1",
            "BatchPolicyId": "batch-64-v1",
            "ExecutionPolicyId": "frame-faithful-v1",
            "LogicalBatchSize": 64,
            "IsTunedAoSBaseline": baseline,
            "SortOrder": 0 if baseline else 1,
        }

    def outcomes(candidate_cost: float) -> list[dict]:
        return [
            {
                "Candidate": descriptor("aos-tuned", True),
                "SourceEvidenceHash": synthetic_sha("4"),
                "AmortizedP95MillisecondsPerTick": 10.0,
            },
            {
                "Candidate": descriptor("soa-candidate", False),
                "SourceEvidenceHash": synthetic_sha("5"),
                "AmortizedP95MillisecondsPerTick": candidate_cost,
            },
        ]

    def interval(point: float, lower: float, upper: float) -> dict:
        return {
            "ReplicateCount": 100,
            "ConfidenceLevel": 0.95,
            "PointEstimatePercent": point,
            "LowerBoundPercent": lower,
            "UpperBoundPercent": upper,
        }

    def cell(
        lifetime: int,
        status: int,
        selected: str,
        frozen: str,
        calibration_improvement: float,
        holdout_improvement: float,
        holdout_confirmed: bool,
        candidate_cost: float,
        lower: float,
    ) -> dict:
        return {
            "Axis": {
                "ElementCount": 65536,
                "LifetimeTicks": lifetime,
                "HotToColdRatio": 3.0,
                "WorkerCount": 4,
                "ExecutionPolicyId": "frame-faithful-v1",
            },
            "Status": status,
            "CalibrationPartitionId": "synthetic-calibration",
            "HoldoutPartitionId": "synthetic-holdout" if holdout_confirmed else "",
            "HoldoutBaselineEvidenceHash": (
                synthetic_sha("6") if holdout_confirmed else ""
            ),
            "HoldoutCandidateEvidenceHash": (
                synthetic_sha("7") if holdout_confirmed else ""
            ),
            "BaselineCandidateId": "aos-tuned",
            "BestMeasuredCandidateId": "soa-candidate",
            "FrozenCalibrationWinnerCandidateId": frozen,
            "SelectedCandidateId": selected,
            "HoldoutConfirmed": holdout_confirmed,
            "MinimumRequiredImprovementPercent": 10.0,
            "CalibrationImprovementPercent": calibration_improvement,
            "CalibrationConfidenceInterval": interval(
                calibration_improvement,
                calibration_improvement,
                calibration_improvement,
            ),
            "HoldoutImprovementPercent": holdout_improvement,
            "HoldoutConfidenceInterval": interval(
                holdout_improvement,
                lower,
                holdout_improvement + 2.0,
            ),
            "CandidateOutcomes": outcomes(candidate_cost),
            "Reason": "Synthetic fixed decision for renderer contract testing only.",
        }

    cells = [
        cell(5, 1, "aos-tuned", "aos-tuned", 2.0, 0.0, False, 9.8, 0.0),
        cell(20, 3, "soa-candidate", "soa-candidate", 10.0, 10.0, True, 9.0, 8.0),
        cell(40, 3, "soa-candidate", "soa-candidate", 15.0, 15.0, True, 8.5, 12.0),
    ]
    return {
        "SchemaVersion": 1,
        "ArtifactType": "advantage-envelope",
        "DecisionEngineVersion": "1.0.0",
        "EnvelopeId": "synthetic-envelope",
        "CreatedUtcIso8601": "2026-09-02T00:00:00Z",
        "ScenarioId": "synthetic-scenario",
        "ContractVersion": 7,
        "CandidateSetHash": synthetic_sha("A"),
        "MeasurementSchemaHash": synthetic_sha("B"),
        "EnvironmentFingerprint": synthetic_sha("C"),
        "CalibrationSettingsHash": synthetic_sha("D"),
        "HoldoutSettingsHash": synthetic_sha("E"),
        "CalibrationSourceArtifactId": "synthetic-calibration-artifact",
        "CalibrationSourceArtifactSha256": synthetic_sha("F"),
        "HoldoutSourceArtifactId": "synthetic-holdout-artifact",
        "HoldoutSourceArtifactSha256": synthetic_sha("0"),
        "EvidenceScope": "synthetic-test-fixture",
        "CalibrationUncertaintyMethod": "synthetic-aligned-bootstrap-replicates",
        "HoldoutUncertaintyMethod": "synthetic-aligned-bootstrap-replicates",
        "Policy": {
            "MinimumImprovementPercent": 10.0,
            "ConfidenceLevel": 0.95,
            "MinimumBootstrapReplicates": 100,
            "MinimumCalibrationResidentSamples": 3,
            "MinimumCalibrationBoundarySamples": 3,
            "MinimumHoldoutResidentSamples": 3,
            "MinimumHoldoutBoundarySamples": 3,
        },
        "FinalDecisionLocked": True,
        "HoldoutCanRerank": False,
        "Cells": cells,
        "WinnerRegions": [
            {
                "ElementCount": 65536,
                "HotToColdRatio": 3.0,
                "WorkerCount": 4,
                "ExecutionPolicyId": "frame-faithful-v1",
                "MinimumSampledLifetimeTicks": 5,
                "MaximumSampledLifetimeTicks": 5,
                "SampledLifetimeTicks": [5],
                "Status": 1,
                "SelectedCandidateId": "aos-tuned",
            },
            {
                "ElementCount": 65536,
                "HotToColdRatio": 3.0,
                "WorkerCount": 4,
                "ExecutionPolicyId": "frame-faithful-v1",
                "MinimumSampledLifetimeTicks": 20,
                "MaximumSampledLifetimeTicks": 40,
                "SampledLifetimeTicks": [20, 40],
                "Status": 3,
                "SelectedCandidateId": "soa-candidate",
            },
        ],
        "Summary": {
            "TotalCellCount": 3,
            "ValidCellCount": 3,
            "CredibleAdvantageCellCount": 2,
            "StatisticalGreyCellCount": 0,
            "AoSFallbackCellCount": 1,
            "HoldoutRejectedCellCount": 0,
            "CredibleCoveragePercent": 200.0 / 3.0,
            "PeakConfirmedImprovementPercent": 15.0,
            "MedianConfirmedImprovementPercent": 12.5,
            "FloorConfirmedImprovementPercent": 10.0,
            "WorstConfirmedConfidenceLowerBoundPercent": 8.0,
        },
    }


class FrozenAdvantageEnvelopeRendererTests(unittest.TestCase):
    def test_candidate_definition_hash_is_required(self) -> None:
        envelope = fixed_synthetic_envelope()
        del envelope["Cells"][0]["CandidateOutcomes"][0]["Candidate"][
            "CandidateDefinitionSha256"
        ]

        with self.assertRaisesRegex(
            EnvelopeRenderContractError, "CandidateDefinitionSha256"
        ):
            build_render_model(envelope)

    def test_all_hash_fields_require_canonical_uppercase_sha256(self) -> None:
        cases = [
            (("CandidateSetHash",), "ABC"),
            (("MeasurementSchemaHash",), synthetic_sha("a")),
            (("EnvironmentFingerprint",), synthetic_sha("G")),
            (("CalibrationSettingsHash",), ""),
            (("HoldoutSettingsHash",), synthetic_sha("-")),
            (("CalibrationSourceArtifactSha256",), synthetic_sha("z")),
            (("HoldoutSourceArtifactSha256",), synthetic_sha(" ")),
            (("Cells", 0, "CandidateOutcomes", 0, "SourceEvidenceHash"), "BAD"),
            (
                (
                    "Cells",
                    0,
                    "CandidateOutcomes",
                    0,
                    "Candidate",
                    "CandidateDefinitionSha256",
                ),
                synthetic_sha("g"),
            ),
            (("Cells", 1, "HoldoutBaselineEvidenceHash"), synthetic_sha("b")),
            (("Cells", 1, "HoldoutCandidateEvidenceHash"), synthetic_sha("X")),
        ]
        for path, malformed in cases:
            with self.subTest(path=path):
                envelope = fixed_synthetic_envelope()
                target = envelope
                for component in path[:-1]:
                    target = target[component]
                target[path[-1]] = malformed

                with self.assertRaisesRegex(
                    EnvelopeRenderContractError,
                    "64 uppercase hexadecimal|non-empty string",
                ):
                    build_render_model(envelope)

    def test_unsupported_decision_engine_version_is_rejected(self) -> None:
        envelope = fixed_synthetic_envelope()
        envelope["DecisionEngineVersion"] = "2.0.0"

        with self.assertRaisesRegex(
            EnvelopeRenderContractError, "Unsupported DecisionEngineVersion"
        ):
            build_render_model(envelope)

    def test_changed_candidate_cost_cannot_change_frozen_selection(self) -> None:
        envelope = fixed_synthetic_envelope()
        original = build_render_model(envelope)
        mutated = copy.deepcopy(envelope)
        for cell in mutated["Cells"]:
            cell["CandidateOutcomes"][1]["AmortizedP95MillisecondsPerTick"] = 0.000001
        changed = build_render_model(mutated)

        self.assertEqual(
            [cell["SelectedCandidateId"] for cell in original["Cells"]],
            [cell["SelectedCandidateId"] for cell in changed["Cells"]],
        )
        self.assertEqual("aos-tuned", changed["Cells"][0]["SelectedCandidateId"])

    def test_region_that_disagrees_with_frozen_cell_is_rejected(self) -> None:
        envelope = fixed_synthetic_envelope()
        envelope["WinnerRegions"][1]["SelectedCandidateId"] = "aos-tuned"

        with self.assertRaisesRegex(
            EnvelopeRenderContractError, "disagrees with the frozen cell decision"
        ):
            build_render_model(envelope)

    def test_fallback_cell_cannot_display_nonbaseline_candidate(self) -> None:
        envelope = fixed_synthetic_envelope()
        envelope["Cells"][0]["SelectedCandidateId"] = "soa-candidate"

        with self.assertRaisesRegex(EnvelopeRenderContractError, "fallback state"):
            build_render_model(envelope)

    def test_display_name_cannot_replace_missing_canonical_candidate_id(self) -> None:
        envelope = fixed_synthetic_envelope()
        del envelope["Cells"][0]["CandidateOutcomes"][1]["Candidate"]["CandidateId"]

        with self.assertRaisesRegex(EnvelopeRenderContractError, "CandidateId"):
            build_render_model(envelope)

    def test_summary_that_disagrees_with_cells_is_rejected(self) -> None:
        envelope = fixed_synthetic_envelope()
        envelope["Summary"]["CredibleAdvantageCellCount"] = 3

        with self.assertRaisesRegex(
            EnvelopeRenderContractError, "CredibleAdvantageCellCount"
        ):
            build_render_model(envelope)

    def test_end_to_end_outputs_preserve_fixed_decisions_and_scope(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "advantage-envelope.json"
            source.write_text(
                json.dumps(fixed_synthetic_envelope()), encoding="utf-8"
            )

            outputs = render_artifacts(source, root / "rendered")

            self.assertTrue(outputs["heatmap"].is_file())
            self.assertTrue(outputs["lifetime_gif"].is_file())
            with Image.open(outputs["lifetime_gif"]) as animation:
                self.assertGreater(animation.n_frames, 1)
            manifest = json.loads(outputs["manifest"].read_text(encoding="utf-8"))
            self.assertEqual("synthetic-test-fixture", manifest["EvidenceScope"])
            self.assertEqual("aos-tuned", manifest["Decisions"][0]["SelectedCandidateId"])
            self.assertEqual(
                "soa-candidate", manifest["Decisions"][1]["SelectedCandidateId"]
            )
            self.assertIn("contains no cost ranking", manifest["SelectionContract"])


if __name__ == "__main__":
    unittest.main()
