"""Synthetic rejection fixtures for the archive validator; never observed evidence."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

MODULE = Path(__file__).resolve().parents[1] / "validate_counter_evidence.py"
SPEC = importlib.util.spec_from_file_location("counter_validation", MODULE)
VALIDATOR = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VALIDATOR)


class CounterArchiveTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        (self.root / "raw").mkdir()
        self.path = self.root / "cpu-counter-evidence.json"
        identity = {"RunId": "fixture", "ProcessEvidenceId": "fixture-process", "Cpu": "fixture-cpu", "BuildType": "Release",
                    "Binaries": [{"Path": "lib_burst_generated.dll", "Sha256": "A" * 64}]}
        unsupported = ("retired-instructions", "cache-references", "cache-misses", "branch-instructions", "branch-misses")
        self.document = {"Identity": identity, "Metrics": [{"MetricId": m, "Status": "Unavailable"} for m in unsupported],
                         "Rows": [], "Pairs": 2, "CollectedCaptures": 2, "UnavailableCaptures": 0, "FailedCaptures": 0,
                         "ActualCounterGate": "passed-process-cycles-only", "Summaries": [{"ParityPassed": True,
                         "ResidentAllocationBytes": 0, "IngressAllocationBytes": 0, "ExportAllocationBytes": 0,
                         "Overhead": {"Status": 1, "Repetitions": 2}}]}
        for pair in range(2):
            for position in range(2):
                on = (pair % 2 == 0) == (position == 1)
                context = {"RunId": "fixture", "ProcessEvidenceId": "fixture-process", "CandidateId": "a", "CandidateSchemaSha256": "B" * 64}
                capture = {"Context": context, "Status": 2 if on else 0, "Origin": 1 if on else 0,
                           "InterpretationLevel": 1 if on else 0, "RawCounters": []}
                if on:
                    payload = b"start=9007199254740993\nend=9007199254741043\ndelta=50\nrun=fixture\ncandidate=a\n"
                    name = f"pair-{pair}.txt"
                    (self.root / "raw" / name).write_bytes(payload)
                    capture["RawCounters"] = [{"CounterId": "windows-process-cpu-cycles", "Value": 50}]
                    capture["Artifacts"] = [{"ArtifactPath": "C:/original/raw/" + name,
                                             "ArtifactSha256": hashlib.sha256(payload).hexdigest().upper()}]
                self.document["Rows"].append({"ScenarioId": "fixture", "CandidateId": "a", "CandidateDefinitionSha256": "B" * 64,
                    "Pair": pair, "OrderPosition": position, "Enabled": on, "EndToEndNanoseconds": 100,
                    "StateHash": "same-output", "Capture": capture})

    def validate(self, collected=True):
        self.path.write_text(json.dumps(self.document), encoding="utf-8")
        return VALIDATOR.validate(self.path, collected)

    def test_valid_archive_preserves_integer_endpoints_above_double_precision(self):
        self.assertEqual(self.validate()["collected"], 2)

    def test_synthetic_provider_cannot_be_promoted(self):
        self.document["Rows"][1]["Capture"]["Origin"] = 2
        with self.assertRaisesRegex(ValueError, "not observed"):
            self.validate()

    def test_raw_corruption_rejected(self):
        (self.root / "raw" / "pair-0.txt").write_text("delta=999", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "hash mismatch"):
            self.validate()

    def test_missing_arm_rejected(self):
        self.document["Rows"].pop()
        with self.assertRaisesRegex(ValueError, "incomplete paired"):
            self.validate()

    def test_unknown_is_not_zero(self):
        self.document["Metrics"][0]["Value"] = 0
        with self.assertRaisesRegex(ValueError, "no numeric value"):
            self.validate()

    def test_context_mismatch_rejected(self):
        self.document["Rows"][1]["Capture"]["Context"]["ProcessEvidenceId"] = "wrong-process"
        with self.assertRaisesRegex(ValueError, "identity mismatch"):
            self.validate()

    def test_fallback_is_explicitly_unavailable_not_counter_success(self):
        for row in self.document["Rows"]:
            if row["Enabled"]:
                row["Capture"].update(Status=1, RawCounters=[], StatusCode="provider-not-configured", StatusReason="No provider")
        self.document.update(CollectedCaptures=0, UnavailableCaptures=2, ActualCounterGate="unavailable")
        with self.assertRaisesRegex(ValueError, "gate unmet"):
            self.validate()
        self.assertEqual(self.validate(False)["gate"], "unavailable")


if __name__ == "__main__":
    unittest.main()
