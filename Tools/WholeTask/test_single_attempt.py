"""Deterministic ledger and immutable-config checks, without measurements."""
import json
from pathlib import Path
import tempfile
import unittest
from single_attempt import configured_candidate, ledger
from workflow import sha


class CallerContracts(unittest.TestCase):
    def test_upfront_charge_cannot_disappear(self):
        result = ledger([100.] * 6, [90.] * 6, 200., True)
        self.assertEqual(result["observedUses"][-1]["automaticMs"], 740.)
        self.assertFalse(result["observedUses"][-1]["measuredCostRecovered"])
        self.assertEqual(result["modeledBreakEvenUses"], 20)

    def test_unconfirmed_or_same_aos_noise_never_claims_payback(self):
        for confirmed, same in [(False, False), (True, True)]:
            value = ledger([100.] * 6, [1.] * 6, 1., confirmed, same)
            self.assertIsNone(value["modeledBreakEvenUses"])
            self.assertFalse(value["observedUses"][-1]["measuredCostRecovered"])

    def test_bad_costs_rejected(self):
        for selected in ([], [float("nan")], [-1.]):
            with self.assertRaises(ValueError):
                ledger([100.], selected, 0., True)

    def test_frozen_config_rejects_tamper_and_wrong_source(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "decision.json"
            path.write_text(json.dumps(dict(decision=dict(SourceFingerprint="source", CandidateId="tuned-aos"))))
            digest = sha(path)
            self.assertEqual(configured_candidate(path, digest, "source", ["tuned-aos"]), "tuned-aos")
            with self.assertRaises(ValueError):
                configured_candidate(path, digest, "changed", ["tuned-aos"])
            path.write_text("{}")
            with self.assertRaises(ValueError):
                configured_candidate(path, digest, "source", ["tuned-aos"])
