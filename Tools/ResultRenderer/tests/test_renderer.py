from __future__ import annotations

import copy
import json
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from render_results import RenderContractError, build_render_model, render_artifacts


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


class FixedDecisionRendererTests(unittest.TestCase):
    def test_faster_measurement_cannot_change_frozen_selection(self) -> None:
        suite = fixed_suite()
        original = build_render_model(suite)
        mutated = copy.deepcopy(suite)
        mutated["Scenarios"][0]["CalibrationResults"][1]["AmortizedLatency"][
            "P95Milliseconds"
        ] = 0.000001
        changed = build_render_model(mutated)

        self.assertEqual("AoS-b64", original["Scenarios"][0]["SelectedId"])
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
