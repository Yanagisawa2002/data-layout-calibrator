"""Synthetic parser/rejection fixtures only; never benchmark evidence."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

MODULE = Path(__file__).resolve().parents[1] / 'summarize_envelope.py'
spec = importlib.util.spec_from_file_location('summary', MODULE)
summary = importlib.util.module_from_spec(spec)
spec.loader.exec_module(summary)


class AuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.addCleanup(self.temp.cleanup)
        self.write('grid-declaration.json', dict(WorkerCounts=[1, 8], ElementCounts=[4096, 65536],
            ColdAccessEveryTicks=[1, 8], LifetimeTicks=[1, 16, 256], IndependentProcesses=5,
            Candidates=[], UnmeasuredAxes='synthetic-test-fixture, no actual measurements'))
        self.write('build-identity.json', dict(scope='synthetic-test-fixture'))

    def write(self, name, value):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value), encoding='utf-8')
        return path

    def receipt(self, process=1):
        value = dict(DeclarationSha256=summary.sha(self.root/'grid-declaration.json'),
            BuildIdentity=summary.sha(self.root/'build-identity.json'), ScriptingBackend='IL2CPP',
            DevelopmentBuild=False, BurstEnabled=True, ProcessIndex=process, ProcessId=123,
            StartUtc=f'synthetic-time-{process}', CandidateSetSha256='A'*64, CompletedCells=0, Failure='interrupted',
            Processor='synthetic-test-fixture', OperatingSystem='synthetic', UnityVersion='synthetic', BurstIsaProbeMask=3)
        return value

    def test_no_evidence_is_incomplete_never_zero_cost_success(self):
        report = summary.audit(self.root)
        self.assertEqual(report['status'], 'incomplete')
        self.assertEqual(report['measuredCells'], 0)
        self.assertIsNone(report['credibleCoveragePercent'])
        self.assertEqual(len(report['missingGates']), 5)

    def test_changed_build_or_nonformal_backend_is_rejected(self):
        for field, value in [('BuildIdentity', 'B'*64), ('ScriptingBackend', 'Mono'),
                             ('DevelopmentBuild', True), ('BurstEnabled', False)]:
            receipt = self.receipt()
            receipt[field] = value
            self.write('run-01/receipt.json', receipt)
            with self.assertRaisesRegex(ValueError, 'identity/backend'):
                summary.audit(self.root)

    def test_duplicate_process_artifacts_cannot_count_as_independent(self):
        self.write('run-01/receipt.json', self.receipt())
        duplicate = self.receipt(2)
        duplicate['StartUtc'] = 'synthetic-time-1'
        self.write('run-02/receipt.json', duplicate)
        with self.assertRaisesRegex(ValueError, 'Repeated process'):
            summary.audit(self.root)

    def test_different_device_cannot_be_pooled(self):
        self.write('run-01/receipt.json', self.receipt())
        changed = self.receipt(2)
        changed['Processor'] = 'different synthetic CPU'
        self.write('run-02/receipt.json', changed)
        with self.assertRaisesRegex(ValueError, 'identity differs'):
            summary.audit(self.root)

    def test_changed_candidate_pool_rejected(self):
        self.write('run-01/receipt.json', self.receipt())
        changed = self.receipt(2)
        changed['CandidateSetSha256'] = 'B'*64
        self.write('run-02/receipt.json', changed)
        with self.assertRaisesRegex(ValueError, 'Candidate sets differ'):
            summary.audit(self.root)

    def test_locked_final_cannot_reference_tampered_raw_file(self):
        self.write('run-01/receipt.json', self.receipt())
        self.write('run-01/p1-c00-frozen.json', dict(HoldoutWasRead=False))
        self.write('run-01/p1-c00-settings.json', dict(CalibrationSeed=1, HoldoutSeed=2))
        raw = self.write('run-01/p1-c00-calibration.json', dict(timing='synthetic'))
        self.write('run-01/p1-c00-envelope.json', dict(FinalDecisionLocked=True, HoldoutCanRerank=False,
            CandidateSetHash='A'*64, EnvelopeId='p1-c00', CalibrationSourceArtifactId='p1-c00-calibration',
            CalibrationSourceArtifactSha256='B'*64))
        self.assertNotEqual(summary.sha(raw), 'B'*64)
        with self.assertRaisesRegex(ValueError, 'raw source hash mismatch'):
            summary.audit(self.root)


if __name__ == '__main__':
    unittest.main()
