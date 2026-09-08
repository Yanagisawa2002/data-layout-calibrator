"""Functional fixture/model tests only: no measurements, image/GIF rendering or Player."""
import copy
import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from render_results import allocation_observation_status, build_render_model, timing_semantics, RenderContractError
from test_renderer import fixed_suite


class MeasurementContractTests(unittest.TestCase):
    def test_legacy_numbers_and_decision_are_preserved(self):
        suite = fixed_suite()
        before = copy.deepcopy(suite)
        model = build_render_model(suite)
        self.assertEqual(suite, before)
        scenario = model['Scenarios'][0]
        self.assertEqual(scenario['BaselineP95Microseconds'], 20)
        self.assertEqual(scenario['BestP95Microseconds'], 18)
        self.assertEqual(scenario['SelectedId'], 'AoS-b64')
        self.assertIn('not tick/lifecycle P95', scenario['TimingSemantics']['DisplayLabel'])
        self.assertIn('Unknown', scenario['AllocationEvidenceStatus'][0])

    def test_missing_counter_zero_is_unknown(self):
        self.assertIn('Unknown', allocation_observation_status({'HotPathManagedAllocationBytes': 0}))

    def test_scope_and_windows_are_required(self):
        result = dict(AllocationCapability=dict(SchemaVersion=1, Availability=1, Scope=1,
            PositiveControlPassed=True, EmptyControlPassed=True, Provider='fixture', Unit='bytes', PositiveControlMinimumBytes=1048576, ObservedPositiveBytes=1048576),
            RequiredAllocationScope=3, AllocationWindowsComplete=True,
            HotPathManagedAllocationBytes=0, BoundaryManagedAllocationBytes=0)
        self.assertIn('Unknown', allocation_observation_status(result))
        result['RequiredAllocationScope'] = 1
        self.assertIn('Validated zero', allocation_observation_status(result))
        result['AllocationWindowsComplete'] = False
        self.assertIn('Unknown', allocation_observation_status(result))

    def test_unknown_or_misnamed_metric_is_rejected(self):
        for contract in ({'SchemaVersion': 99}, {'SchemaVersion': 1, 'SelectionMetric': 'true-tick-p95'}):
            with self.assertRaises(RenderContractError):
                timing_semantics({'TimingContract': contract})

    def test_additive_contract_is_accepted_without_reestimating(self):
        suite = fixed_suite()
        contract = timing_semantics({})
        contract.pop('DisplayLabel')
        contract.update(SchemaVersion=1, HistoricalSemantics=False)
        suite['Scenarios'][0]['TimingContract'] = contract
        model = build_render_model(suite)
        self.assertEqual(model['Scenarios'][0]['ImprovementPercent'], 10)
        self.assertFalse(model['Scenarios'][0]['TimingSemantics']['HistoricalSemantics'])

    def test_real_history_is_read_only_and_not_recategorized_as_current_evidence(self):
        # Source-backed historical input; only DTO rendering/semantics, no numerical inference.
        path = Path(__file__).resolve().parents[3] / 'Docs/evidence/il2cpp-release-calibration-suite.json'
        contents = path.read_bytes()
        model = build_render_model(json.loads(contents))
        self.assertTrue(all(s['TimingSemantics']['HistoricalSemantics'] for s in model['Scenarios']))
        self.assertEqual(path.read_bytes(), contents)


if __name__ == '__main__':
    unittest.main()
