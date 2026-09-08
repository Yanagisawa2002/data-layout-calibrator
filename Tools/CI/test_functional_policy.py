"""Static safety-contract tests. Never execute a legacy measurement entrypoint."""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


class FunctionalPolicyTests(unittest.TestCase):
    def test_default_validation_dispatches_only_build_and_functional_tests(self):
        from validate_functional import validation_commands
        import sys
        commands = validation_commands(False)
        self.assertEqual(validation_commands(True), [["dotnet", "build", "Tools/FunctionalTests/FunctionalTests.csproj", "-c", "Release", "--nologo"]])
        allowed = {
            ("dotnet", "build", "Tools/FunctionalTests/FunctionalTests.csproj"),
            ("dotnet", "test", "Tools/FunctionalTests/FunctionalTests.csproj"),
        }
        for command in commands:
            if command[0] == "dotnet":
                self.assertIn(tuple(command[:3]), allowed)
            else:
                self.assertEqual(command[:4], [sys.executable, "-m", "unittest", "discover"])
                self.assertIn(command[command.index("-p") + 1], ("test_functional_policy.py", "test_measurement_contract.py"))
            self.assertFalse(any(arg.endswith(".ps1") or arg.startswith("-dla-") or arg == "--run" for arg in command))

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

    def test_ci_excludes_performance_paths(self):
        workflow = (ROOT / '.github/workflows/non-unity-ci.yml').read_text(encoding='utf-8')
        self.assertIn('python Tools/CI/validate_functional.py', workflow)
        self.assertNotIn('runTests', workflow)
        self.assertNotIn('Invoke-', workflow)
        self.assertNotIn('--run', workflow)
        self.assertIn('--filter FullyQualifiedName~DataLayoutScaffoldGeneratorTests', workflow)
        self.assertNotIn('Tools/ScientificTests/ScientificTests.csproj', workflow)


if __name__ == '__main__':
    unittest.main()
