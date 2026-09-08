"""Static safety-contract tests. Never execute a legacy measurement entrypoint."""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


class FunctionalPolicyTests(unittest.TestCase):
    def test_legacy_scripts_refuse_before_any_work(self):
        for name in (
            'Integration/Invoke-UnityValidation.ps1', 'Integration/Invoke-PlayerValidation.ps1',
            'CpuCounters/Invoke-CounterEvidence.ps1', 'Envelope/Invoke-EnvelopeGrid.ps1',
            'EvidenceLab/Invoke-SearchComparison.ps1', 'Validate-ParticleMatrix.ps1',
            'Validation/Invoke-GeneratedWorkloadValidation.ps1',
        ):
            source = (ROOT / 'Tools' / name).read_text(encoding='utf-8')
            self.assertRegex(source, r"\n\)\nthrow 'Legacy performance/Player orchestration is disabled")

    def test_functional_project_is_an_explicit_safe_test_allowlist(self):
        project_path = ROOT / 'Tools/FunctionalTests/FunctionalTests.csproj'
        project = ET.parse(project_path)
        forbidden = re.compile(r'Stopwatch\.|GC\.GetAllocatedBytesForCurrentThread\(|'
            r'new ThreadManagedAllocationCounter\(\s*\)|DateTime\.(?:Utc)?Now|'
            r'ScenarioCalibrationEngine\.(?:Run|RunSearchComparison|RunEnvelopeCell)\(')
        for node in project.findall('.//Compile'):
            source = node.attrib['Include']
            if '/Tests/' not in source:
                continue
            self.assertNotIn('*', source)
            text = (project_path.parent / source).read_text(encoding='utf-8')
            self.assertIsNone(forbidden.search(text), source)

    def test_ci_enforces_functional_environment(self):
        workflow = (ROOT / '.github/workflows/non-unity-ci.yml').read_text(encoding='utf-8')
        self.assertIn("DLC_FUNCTIONAL_ONLY: '1'", workflow)
        self.assertIn('python Tools/CI/validate_functional.py', workflow)
        self.assertNotIn('runTests', workflow)
        self.assertNotIn('Tools/ScientificTests/ScientificTests.csproj', workflow)


if __name__ == '__main__':
    unittest.main()
