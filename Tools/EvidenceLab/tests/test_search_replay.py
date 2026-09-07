import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import search_replay as replay
from process_hierarchy import read, sha

ROOT = Path(__file__).resolve().parents[3]
SOURCE = ROOT / "Docs/evidence/vnext-formal-il2cpp-2026-09-02/run-01-calibration-suite.json"


class SearchReplayTests(unittest.TestCase):
    def fixture(self, directory):
        # Synthetic comparison bookkeeping around retained raw data. These durations
        # are deliberately test constants, never exported as real search evidence.
        final = read(SOURCE)["Scenarios"][0]
        calibration = copy.deepcopy(final)
        calibration["HoldoutBaselineResult"] = None
        calibration["HoldoutSelectedResult"] = None
        calibration["HoldoutDatasetHash"] = ""
        calibration["FinalDecision"] = calibration["CalibrationDecision"]
        settings = {k: final[k] for k in ("SamplesPerCandidate", "BoundarySamplesPerCandidate", "BootstrapIterations",
            "BootstrapConfidenceLevel", "MinimumImprovementPercent", "LifetimeTicks", "CalibrationSeed", "HoldoutSeed")}
        result = {"SchemaVersion": 1, "BothSelectionsFrozenBeforeHoldout": True, "FinalEvidenceRequirementsUnchanged": True,
            "Quick": calibration, "Adaptive": final, "Exhaustive": final,
            "ExecutedFinalistIds": [r["Candidate"]["CandidateId"] for r in final["CalibrationResults"]],
            "AdaptiveComponentEvaluationCount": 2 * 32 * 80, "ExhaustiveComponentEvaluationCount": 32 * 80,
            "SharedPreflightMilliseconds": 1, "QuickMilliseconds": 2, "PlanningMilliseconds": 3,
            "AdaptiveFullMilliseconds": 4, "ExhaustiveFullMilliseconds": 5,
            "AdaptiveCalibrationMilliseconds": 10, "ExhaustiveCalibrationMilliseconds": 6,
            "ShortlistOracleRegretPercent": 0, "ActualAdaptiveSelectionOracleRegretPercent": 0,
            "MaximumAllowedRegretPercent": 1, "RegretGatePassed": True}
        def write(name, value):
            path = directory / (name + ".json")
            path.write_text(json.dumps(value), encoding="utf-8")
            return sha(path)
        env = directory.parent / "environment.json"
        env.write_text('{"scope":"synthetic test only"}')
        result["EnvironmentFingerprint"] = sha(env)
        for name, field, value in (
            ("settings", "SettingsSha256", settings), ("quick", "QuickSha256", calibration),
            ("adaptive-calibration", "AdaptiveCalibrationSha256", calibration),
            ("exhaustive-calibration", "ExhaustiveCalibrationSha256", calibration),
            ("frozen-selections", "FrozenSelectionsSha256", {"Adaptive": calibration, "Exhaustive": calibration}),
            ("adaptive-final", "AdaptiveFinalSha256", final), ("exhaustive-final", "ExhaustiveFinalSha256", final)):
            result[field] = write(name, value)
        write("comparison", result)
        return directory / "comparison.json", result

    def test_accepts_consistent_raw_evidence_and_rejects_false_saved_costs(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp) / "cell"; directory.mkdir()
            path, result = self.fixture(directory)
            self.assertEqual("passed", replay.validate_comparison(path)["integrityReplay"])
            for field, value in (("AdaptiveCalibrationMilliseconds", 4),
                                 ("AdaptiveComponentEvaluationCount", 1),
                                 ("ActualAdaptiveSelectionOracleRegretPercent", 3),
                                 ("RegretGatePassed", False)):
                changed = copy.deepcopy(result); changed[field] = value
                path.write_text(json.dumps(changed))
                with self.assertRaises(ValueError): replay.validate_comparison(path)

    def test_rejects_hash_tamper_and_false_freeze(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp) / "cell"; directory.mkdir()
            path, result = self.fixture(directory)
            result["QuickSha256"] = "0" * 64
            path.write_text(json.dumps(result))
            with self.assertRaises(ValueError): replay.validate_comparison(path)
            result["BothSelectionsFrozenBeforeHoldout"] = False
            path.write_text(json.dumps(result))
            with self.assertRaises(ValueError): replay.validate_comparison(path)

    def test_rejects_lowered_holdout_gate_and_candidate_substitution(self):
        original = read(SOURCE)["Scenarios"][0]
        for mutate in (
            lambda p: p.__setitem__("SamplesPerCandidate", 3),
            lambda p: p["HoldoutSelectedResult"]["Candidate"].__setitem__("CandidateId", "replacement"),
            lambda p: p.__setitem__("HoldoutSeed", p["CalibrationSeed"]),
            lambda p: p["FinalDecision"].__setitem__("Status", 1)):
            changed = copy.deepcopy(original); mutate(changed)
            with self.assertRaises(ValueError): replay.verify_holdout(changed, original)

    def test_rejects_duplicate_or_unpaired_measurement_positions(self):
        p = read(SOURCE)["Scenarios"][0]
        p["CalibrationResults"][0]["ResidentOrderPositions"] = p["CalibrationResults"][1]["ResidentOrderPositions"]
        with self.assertRaises(ValueError): replay.profile_costs(p)


if __name__ == "__main__": unittest.main()
