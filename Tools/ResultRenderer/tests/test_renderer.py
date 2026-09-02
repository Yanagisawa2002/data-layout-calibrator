from __future__ import annotations

import copy
import hashlib
import json
import math
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from render_results import (
    MEASUREMENT_SCHEMA_SHA256,
    RenderContractError,
    _candidate_definition_bytes,
    _candidate_set_sha256,
    build_render_model,
    render_artifacts,
)


def fixed_suite() -> dict:
    def candidate(identifier: str, layout: str, batch: int, p95_ms: float) -> dict:
        return {
            "Candidate": {
                "CandidateId": identifier,
                "LayoutId": layout,
                "LogicalBatchSize": batch,
                "SortOrder": 0 if layout == "AoS" else 1,
            },
            "AmortizedLatency": {"P95Milliseconds": p95_ms},
            "Completed": True,
            "ParityPassed": True,
        }

    return {
        "SchemaVersion": 2,
        "RunId": "fixed-test-run",
        "CreatedUtcIso8601": "2026-09-02T00:00:00Z",
        "Environment": {
            "ScriptingBackend": "IL2CPP",
            "BuildType": "Release",
            "UnityVersion": "6000.5.3f1",
        },
        "Scenarios": [
            {
                "Scenario": {
                    "ScenarioId": "negative-control",
                    "DisplayName": "Negative Control",
                    "ContractVersion": 1,
                },
                "ElementCount": 65536,
                "LifetimeTicks": 600,
                "CalibrationResults": [
                    candidate("AoS-b64", "AoS", 64, 0.020),
                    candidate("SoA-b64", "SoA", 64, 0.018),
                ],
                "FinalDecision": {
                    "Status": 1,
                    "BaselineCandidate": {"CandidateId": "AoS-b64"},
                    "SelectedCandidate": {"CandidateId": "AoS-b64"},
                    "BestMeasuredCandidate": {"CandidateId": "SoA-b64"},
                    "BaselineP95Milliseconds": 0.020,
                    "BestMeasuredP95Milliseconds": 0.018,
                    "ImprovementPercent": 10.0,
                    "ImprovementConfidenceInterval": {
                        "Iterations": 500,
                        "ConfidenceLevel": 0.95,
                        "LowerBoundPercent": -1.0,
                        "UpperBoundPercent": 17.0,
                    },
                    "Reason": "The measured difference did not clear the frozen gate.",
                },
            }
        ],
    }


def make_schema3(suite: dict) -> dict:
    suite["SchemaVersion"] = 3
    for scenario in suite["Scenarios"]:
        scenario["SamplingDesign"] = {
            "SchemaVersion": 1,
            "EvidenceScope": 0,
        }
        interval = scenario["FinalDecision"]["ImprovementConfidenceInterval"]
        interval["SchemaVersion"] = 1
        interval["EstimatorKind"] = 2
        interval["HasLogRatioEstimate"] = True
        interval["RandomSeed"] = 1
        interval["Estimand"] = "log(candidate_amortized_p95 / baseline_amortized_p95)"
        interval["ResamplingUnit"] = "paired measurement block"
        interval["PointEstimateLogRatio"] = math.log(0.9)
        interval["LowerBoundLogRatio"] = math.log(0.83)
        interval["UpperBoundLogRatio"] = math.log(1.01)
        interval["PointEstimatePercent"] = 10.0
        for result in scenario["CalibrationResults"]:
            result["ScenarioId"] = scenario["Scenario"]["ScenarioId"]
            result["ScenarioContractVersion"] = scenario["Scenario"]["ContractVersion"]
            candidate = result["Candidate"]
            layout = candidate["LayoutId"]
            kernel = (
                "ScalarBranchless"
                if "branchless" in candidate["CandidateId"].lower()
                else "ScalarBranched"
            )
            candidate["PolicySchemaVersion"] = 1
            candidate["Layout"] = {"PolicyId": layout, "BlockWidth": 1}
            candidate["Kernel"] = {
                "PolicyId": kernel,
                "ControlFlow": 2 if kernel == "ScalarBranchless" else 1,
                "VectorWidth": 1,
            }
            candidate["Batch"] = {
                "PolicyId": "JobBatch",
                "LogicalBatchSize": candidate["LogicalBatchSize"],
            }
            candidate["Execution"] = {
                "PolicyId": "FrameFaithful",
                "Topology": 0,
                "TemporalBlockTicks": 1,
            }
    return suite


def attach_valid_envelope_reference(suite: dict) -> dict:
    make_schema3(suite)
    scenario = suite["Scenarios"][0]
    candidates = [result["Candidate"] for result in scenario["CalibrationResults"]]
    for candidate in candidates:
        candidate["IsBaseline"] = candidate["LayoutId"] == "AoS"
        candidate["Layout"]["AlignmentBytes"] = 0
        candidate["Layout"]["PaddingBytes"] = 0
        candidate["Execution"]["SemanticsPermitReordering"] = False
    scenario["AdvantageEnvelope"] = {
        "SchemaVersion": 1,
        "ArtifactId": "synthetic-advantage-envelope",
        "ArtifactSha256": "A" * 64,
        "ArtifactSchemaVersion": 1,
        "DecisionEngineVersion": "1.0.0",
        "ScenarioId": scenario["Scenario"]["ScenarioId"],
        "ContractVersion": scenario["Scenario"]["ContractVersion"],
        "CandidateSetSha256": _candidate_set_sha256(
            candidates, "synthetic candidates"
        ),
        "MeasurementSchemaSha256": MEASUREMENT_SCHEMA_SHA256,
    }
    return suite


class FixedDecisionRendererTests(unittest.TestCase):
    def test_candidate_definition_golden_vector_matches_runtime_protocol(self) -> None:
        candidate = {
            "PolicySchemaVersion": 1,
            "CandidateId": "aos",
            "LayoutId": "AoS",
            "LogicalBatchSize": 64,
            "IsBaseline": True,
            "SortOrder": 0,
            "Layout": {
                "PolicyId": "AoS",
                "BlockWidth": 1,
                "AlignmentBytes": 0,
                "PaddingBytes": 0,
            },
            "Kernel": {
                "PolicyId": "LegacyUnspecified",
                "ControlFlow": 0,
                "VectorWidth": 1,
            },
            "Batch": {"PolicyId": "JobBatch", "LogicalBatchSize": 64},
            "Execution": {
                "PolicyId": "FrameFaithful",
                "Topology": 0,
                "TemporalBlockTicks": 1,
                "SemanticsPermitReordering": False,
            },
        }
        digest = hashlib.sha256(
            _candidate_definition_bytes(candidate, "candidate")
        ).hexdigest().upper()
        self.assertEqual(
            "9484C1C638CF82EB5D499BB5DDBEF86C2F7610B202C6BA323597D0CC3E69470F",
            digest,
        )

    def test_schema3_envelope_reference_is_validated_and_preserved(self) -> None:
        suite = attach_valid_envelope_reference(fixed_suite())

        model = build_render_model(suite)

        reference = model["Scenarios"][0]["AdvantageEnvelopeReference"]
        self.assertEqual("synthetic-advantage-envelope", reference["ArtifactId"])
        self.assertEqual(MEASUREMENT_SCHEMA_SHA256, reference["MeasurementSchemaSha256"])

        suite["Scenarios"][0]["CalibrationResults"][1]["Candidate"]["Layout"][
            "AlignmentBytes"
        ] = 64
        with self.assertRaisesRegex(RenderContractError, "not bound"):
            build_render_model(suite)

    def test_schema3_envelope_reference_rejects_noncanonical_artifact_hash(self) -> None:
        suite = attach_valid_envelope_reference(fixed_suite())
        suite["Scenarios"][0]["AdvantageEnvelope"]["ArtifactSha256"] = "a" * 64

        with self.assertRaisesRegex(RenderContractError, "64 uppercase hexadecimal"):
            build_render_model(suite)

    def test_schema3_missing_factor_policies_is_rejected(self) -> None:
        suite = fixed_suite()
        make_schema3(suite)
        del suite["Scenarios"][0]["CalibrationResults"][0]["Candidate"]["Kernel"]

        with self.assertRaisesRegex(RenderContractError, "missing explicit schema-3"):
            build_render_model(suite)

    def test_schema3_factor_rows_do_not_overwrite_same_layout_and_batch(self) -> None:
        suite = fixed_suite()
        results = suite["Scenarios"][0]["CalibrationResults"]
        branchless = copy.deepcopy(results[0])
        branchless["Candidate"]["CandidateId"] = "AoS-branchless-b64"
        results.append(branchless)
        make_schema3(suite)

        model = build_render_model(suite)

        rows = model["Scenarios"][0]["FactorRows"]
        self.assertEqual(3, len(rows))
        self.assertEqual(
            {"AoS-b64", "AoS-branchless-b64", "SoA-b64"},
            {cell["CandidateId"] for cell in model["Scenarios"][0]["Cells"]},
        )

    def test_schema3_duplicate_factor_coordinate_is_rejected(self) -> None:
        suite = fixed_suite()
        duplicate = copy.deepcopy(suite["Scenarios"][0]["CalibrationResults"][0])
        duplicate["Candidate"]["CandidateId"] = "same-factors-different-id"
        suite["Scenarios"][0]["CalibrationResults"].append(duplicate)
        make_schema3(suite)

        with self.assertRaisesRegex(RenderContractError, "duplicate factor/batch"):
            build_render_model(suite)

    def test_regression_status_keeps_frozen_aos_selection(self) -> None:
        suite = fixed_suite()
        make_schema3(suite)
        suite["Scenarios"][0]["FinalDecision"]["Status"] = 4

        model = build_render_model(suite)

        self.assertEqual("REGRESSION · AOS RETAINED", model["Scenarios"][0]["StatusName"])
        self.assertEqual("AoS-b64", model["Scenarios"][0]["SelectedId"])

    def test_schema3_manifest_model_preserves_uncertainty_scope(self) -> None:
        suite = fixed_suite()
        make_schema3(suite)

        model = build_render_model(suite)

        scenario = model["Scenarios"][0]
        self.assertEqual("single Player", scenario["EvidenceScopeName"])
        self.assertEqual(
            "paired measurement block", scenario["ConfidenceResamplingUnit"]
        )
        self.assertEqual("paired-block log-ratio", scenario["ConfidenceEstimatorName"])
        self.assertTrue(scenario["HasLogRatioEstimate"])

    def test_schema3_legacy_percent_interval_cannot_claim_log_ratio_values(self) -> None:
        suite = fixed_suite()
        make_schema3(suite)
        interval = suite["Scenarios"][0]["FinalDecision"][
            "ImprovementConfidenceInterval"
        ]
        interval["EstimatorKind"] = 1
        interval["HasLogRatioEstimate"] = False
        interval["RandomSeed"] = 0
        interval["Estimand"] = "schema2 independent composite-P95 improvement percent"
        interval["PointEstimateLogRatio"] = 0.0
        interval["LowerBoundLogRatio"] = 0.0
        interval["UpperBoundLogRatio"] = 0.0

        model = build_render_model(suite)
        self.assertEqual(
            "legacy independent percent",
            model["Scenarios"][0]["ConfidenceEstimatorName"],
        )
        self.assertFalse(model["Scenarios"][0]["HasLogRatioEstimate"])

        interval["PointEstimateLogRatio"] = -0.1
        with self.assertRaisesRegex(RenderContractError, "must not expose"):
            build_render_model(suite)

    def test_schema3_paired_interval_requires_log_ratio_provenance(self) -> None:
        suite = fixed_suite()
        make_schema3(suite)
        suite["Scenarios"][0]["FinalDecision"]["ImprovementConfidenceInterval"][
            "HasLogRatioEstimate"
        ] = False

        with self.assertRaisesRegex(RenderContractError, "inconsistent log-ratio"):
            build_render_model(suite)

    def test_schema3_without_interval_is_labeled_descriptive_only(self) -> None:
        suite = fixed_suite()
        make_schema3(suite)
        interval = suite["Scenarios"][0]["FinalDecision"][
            "ImprovementConfidenceInterval"
        ]
        interval["Iterations"] = 0
        interval["SchemaVersion"] = 0
        interval["EstimatorKind"] = 0
        interval["HasLogRatioEstimate"] = False
        interval["ConfidenceLevel"] = 0.0
        interval["RandomSeed"] = 0
        interval["Estimand"] = ""
        interval["ResamplingUnit"] = ""
        interval["PointEstimateLogRatio"] = 0.0
        interval["LowerBoundLogRatio"] = 0.0
        interval["UpperBoundLogRatio"] = 0.0
        interval["PointEstimatePercent"] = 0.0
        interval["LowerBoundPercent"] = 0.0
        interval["UpperBoundPercent"] = 0.0

        model = build_render_model(suite)

        self.assertEqual(
            "descriptive only (no inferential CI) · single Player",
            model["Scenarios"][0]["UncertaintyPresentation"],
        )
        self.assertEqual(
            "no realized estimator",
            model["Scenarios"][0]["ConfidenceEstimatorName"],
        )

    def test_schema3_whitespace_candidate_identity_is_rejected(self) -> None:
        suite = fixed_suite()
        make_schema3(suite)
        suite["Scenarios"][0]["CalibrationResults"][0]["Candidate"][
            "CandidateId"
        ] = " AoS-b64"

        with self.assertRaisesRegex(RenderContractError, "surrounding whitespace"):
            build_render_model(suite)

    def test_schema3_mismatched_scenario_contract_is_rejected(self) -> None:
        suite = fixed_suite()
        make_schema3(suite)
        suite["Scenarios"][0]["CalibrationResults"][0][
            "ScenarioContractVersion"
        ] = 2

        with self.assertRaisesRegex(RenderContractError, "ContractVersion disagrees"):
            build_render_model(suite)

    def test_faster_measurement_cannot_change_frozen_selection(self) -> None:
        suite = fixed_suite()
        original = build_render_model(suite)
        mutated = copy.deepcopy(suite)
        mutated["Scenarios"][0]["CalibrationResults"][1]["AmortizedLatency"][
            "P95Milliseconds"
        ] = 0.000001
        changed = build_render_model(mutated)

        self.assertEqual("AoS-b64", original["Scenarios"][0]["SelectedId"])
        self.assertEqual(
            "INCONCLUSIVE · AOS RETAINED",
            original["Scenarios"][0]["StatusName"],
        )
        self.assertEqual(
            "schema-2 bootstrap CI · scope unspecified (schema 2)",
            original["Scenarios"][0]["UncertaintyPresentation"],
        )
        self.assertEqual("AoS-b64", changed["Scenarios"][0]["SelectedId"])
        self.assertEqual("SoA-b64", changed["Scenarios"][0]["BestId"])

    def test_unknown_frozen_candidate_is_rejected(self) -> None:
        suite = fixed_suite()
        suite["Scenarios"][0]["FinalDecision"]["SelectedCandidate"][
            "CandidateId"
        ] = "invented-by-renderer"
        with self.assertRaisesRegex(RenderContractError, "absent from CalibrationResults"):
            build_render_model(suite)

    def test_end_to_end_outputs_preserve_manifest_decision(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "suite.json"
            source.write_text(json.dumps(fixed_suite()), encoding="utf-8")
            outputs = render_artifacts(source, root / "rendered")

            self.assertTrue(outputs["heatmap"].is_file())
            self.assertTrue(outputs["comparison_gif"].is_file())
            with Image.open(outputs["comparison_gif"]) as animation:
                self.assertGreater(animation.n_frames, 1)
            manifest = json.loads(outputs["manifest"].read_text(encoding="utf-8"))
            decision = manifest["Decisions"][0]
            self.assertEqual("AoS-b64", decision["SelectedCandidateId"])
            self.assertEqual("SoA-b64", decision["BestMeasuredCandidateId"])


if __name__ == "__main__":
    unittest.main()
