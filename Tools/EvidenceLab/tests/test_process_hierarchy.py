import copy
import importlib.util
import json
import math
from pathlib import Path
import tempfile
import unittest

MODULE = Path(__file__).resolve().parents[1] / "process_hierarchy.py"
spec = importlib.util.spec_from_file_location("process_hierarchy", MODULE)
h = importlib.util.module_from_spec(spec)
spec.loader.exec_module(h)
ROOT = MODULE.parents[2]
MANIFEST = ROOT / "Docs/evidence/vnext-formal-il2cpp-2026-09-02/formal-run-manifest.json"


class ProcessHierarchyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.loaded = h.load_retained(MANIFEST, "particle-integrate-v2")
        suite = h.read(MANIFEST.parent / h.read(MANIFEST)["runs"][0]["file"])
        cls.scenario = suite["Scenarios"][0]

    def test_all_five_real_hashes_and_primary_are_retained(self):
        manifest, settings, env, processes, selections, provenance = self.loaded
        self.assertEqual(5, len(processes))
        self.assertEqual("run-01", manifest["primaryRun"])
        self.assertEqual(5, len(set(p["sha256"] for p in provenance)))
        self.assertGreater(len(set(s["selectedId"] for s in selections)), 1)
        self.assertAlmostEqual(83.56516115321342, selections[0]["rawImprovementPercent"])

    def test_resampling_is_deterministic(self):
        processes = self.loaded[3]
        self.assertEqual(h.bootstrap(processes, 600, 100), h.bootstrap(processes, 600, 100))

    def test_equal_process_weight_not_frame_pooling(self):
        def fixed(ratio, count):
            return [[(10, 10 * ratio)] * count, [(0, 0)] * count, [(0, 0)] * count]
        interval = h.bootstrap([fixed(.5, 3), fixed(2, 300)], 1, 100)
        self.assertAlmostEqual(0, interval["pointImprovementPercent"])
        self.assertLess(interval["lowerImprovementPercent"], 0)
        self.assertGreater(interval["upperImprovementPercent"], 0)

    def test_paired_block_draws_preserve_proportional_cost(self):
        pairs = [[(x, x * .25) for x in (1, 2, 9, 35, 4)]] * 3
        interval = h.bootstrap([pairs, pairs], 20, 100)
        self.assertAlmostEqual(75, interval["lowerImprovementPercent"])
        self.assertAlmostEqual(75, interval["upperImprovementPercent"])

    def test_reject_mismatched_duplicate_and_invalid_blocks(self):
        for mutation in (lambda r: r["ResidentBlockIds"].__setitem__(0, 99),
                         lambda r: r["ResidentBlockIds"].__setitem__(0, 1),
                         lambda r: r["IngressSamplesMilliseconds"].__setitem__(0, math.nan),
                         lambda r: r.__setitem__("ParityPassed", False),
                         lambda r: r.__setitem__("BoundaryManagedAllocationBytes", 1),
                         lambda r: r.__setitem__("Phase", 0)):
            b, c = copy.deepcopy(self.scenario["HoldoutBaselineResult"]), copy.deepcopy(self.scenario["HoldoutSelectedResult"])
            mutation(c)
            with self.assertRaises(ValueError): h.prepare_pair(b, c, 600)

    def test_raw_array_reordering_joins_by_block_id(self):
        b, c = copy.deepcopy(self.scenario["HoldoutBaselineResult"]), copy.deepcopy(self.scenario["HoldoutSelectedResult"])
        expected = h.prepare_pair(b, c, 600)
        for prefix, field in h.COMPONENTS:
            for key in (field, prefix + "BlockIds", prefix + "OrderPositions"): c[key].reverse()
        self.assertEqual(expected, h.prepare_pair(b, c, 600))

    def test_reject_edited_manifest_hash_or_duplicate_launch(self):
        for duplicate in (False, True):
            with tempfile.TemporaryDirectory() as temp:
                path = Path(temp) / "manifest.json"
                manifest = h.read(MANIFEST)
                # Absolute paths point to immutable source files; test edits only scratch manifest.
                for r in manifest["runs"]:
                    r["file"] = str(MANIFEST.parent / r["file"])
                    r["preflightFile"] = str(MANIFEST.parent / r["preflightFile"])
                if duplicate: manifest["runs"][1] = copy.deepcopy(manifest["runs"][0])
                else: manifest["runs"][0]["sha256"] = "0" * 64
                path.write_text(json.dumps(manifest))
                with self.assertRaises(ValueError): h.load_retained(path, "particle-integrate-v2")

    def test_no_synthetic_transform_holdout(self):
        with self.assertRaises(ValueError): h.load_retained(MANIFEST, "transform-export-v1")


if __name__ == "__main__": unittest.main()
