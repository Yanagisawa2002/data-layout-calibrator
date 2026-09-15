"""Offline data/protocol tests; never launch native workloads or inspect live clocks."""
import json
from pathlib import Path
import unittest

from campaign import compare, cohort, require_qualified_performance
from analyze_campaign import costs, interval
from telemetry import LinuxCpuTelemetry
from prepare_sources import HERE, ROOT, digest, replace_once


class ProtocolContracts(unittest.TestCase):
    def test_during_task_interference_cannot_complete_performance_discovery(self):
        record=dict(mode="performance",native=dict(diagnostic=False),performanceEnvironmentEligible=False)
        with self.assertRaises(RuntimeError): require_qualified_performance(record)
        record["mode"]="correctness"
        require_qualified_performance(record)
        record["mode"]="performance"; record["native"]["diagnostic"]=True
        require_qualified_performance(record)
    def test_entire_cohort_rejects_a_slow_or_ineligible_process(self):
        records=[dict(processIdentity=f"p-{pair}-{role}",role=role,pair=pair,position=(arm-pair)%2,
                      performanceEnvironmentEligible=True,completeTaskMs=100.)
                 for pair in range(6) for arm,role in enumerate(("tuned-aos","selected"))]
        self.assertTrue(cohort(records,["tuned-aos","selected"],6,5)["passed"])
        records[-2]["completeTaskMs"]=150.
        bad=cohort(records,["tuned-aos","selected"],6,5)
        self.assertFalse(bad["passed"])
        self.assertEqual(bad["discardedProcesses"],0)
        records[-2]["completeTaskMs"]=100.
        records[-1]["performanceEnvironmentEligible"]=False
        self.assertFalse(cohort(records,["tuned-aos","selected"],6,5)["passed"])

    def test_selected_cost_charges_calibration_and_real_uses(self):
        result=costs([100.]*14,[90.]*14,200.)
        self.assertEqual(result["measured"][0]["selectedTotalMs"],290.)
        self.assertFalse(result["measured"][-1]["costRecovered"])
        self.assertEqual(result["modeledBreakEvenCalls"],20)
        bounds=interval([100.]*6,[90.]*6,1000)
        self.assertAlmostEqual(bounds["lower95"],10)

    def test_cgroup_quota_is_not_host_core_count(self):
        monitor=LinuxCpuTelemetry.__new__(LinuxCpuTelemetry)
        monitor.affinity=[2]; monitor.siblings=[]; monitor.clock_ticks=100
        a=dict(time=0.,ownedSeconds=0.,quotaCores=25.,cpu={"cpu2":dict(total=100,idle=100)},
               cgroupCpu=dict(usage_usec=0,nr_throttled=0,throttled_usec=0))
        b=dict(time=1.,ownedSeconds=.9,quotaCores=25.,cpu={"cpu2":dict(total=200,idle=105)},
               cgroupCpu=dict(usage_usec=2000000,nr_throttled=1,throttled_usec=100))
        result=monitor.interval(a,b,True)
        self.assertAlmostEqual(result["cgroupUsagePercentOfQuota"],8.)
        self.assertAlmostEqual(result["affinityBackgroundCpuPercent"],5.)
        self.assertEqual(result["throttledPeriods"],1)

    def test_upstream_bytes_are_complete_and_locked(self):
        lock = json.loads((HERE / "upstream-lock.json").read_text())
        self.assertGreater(len(lock["files"]), 200)
        for item in lock["files"]:
            self.assertEqual(digest(ROOT/item["path"]), item["sha256"], item["path"])

    def test_adaptation_rejects_source_drift(self):
        with self.assertRaises(ValueError):
            replace_once("different content", "expected input", "replacement")
        with self.assertRaises(ValueError):
            replace_once("twice twice", "twice", "replacement")

    def test_full_output_and_input_both_required(self):
        valid = dict(fullOutputHash="full-output-a", datasetHash="input-a")
        compare([valid, dict(valid)])
        broken = dict(valid, fullOutputHash="truncated-vtu")
        with self.assertRaises(ValueError):
            compare([valid, broken])
        with self.assertRaises(ValueError):
            compare([valid, dict(valid, datasetHash="different-input")])

if __name__ == "__main__":
    unittest.main()
