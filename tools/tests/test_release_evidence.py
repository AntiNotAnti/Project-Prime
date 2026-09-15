"""Tests for mechanically generated Project Prime release evidence."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "generate-release-evidence.py"
SPEC = importlib.util.spec_from_file_location("generate_release_evidence", SCRIPT)
RELEASE_EVIDENCE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(RELEASE_EVIDENCE)


class ReleaseEvidenceTests(unittest.TestCase):
    def test_source_identifiers_resolve_aliases_from_authoritative_constants(self):
        identifiers = RELEASE_EVIDENCE.source_identifiers(ROOT)

        self.assertEqual(25, identifiers["protocol"])
        self.assertEqual(4, identifiers["nodeControlProtocol"])
        self.assertEqual({"linear": 2, "indexed": 3}, identifiers["replayFormats"])

    def test_missing_artifacts_are_explicitly_not_run(self):
        with tempfile.TemporaryDirectory(prefix="prime-release-evidence-") as temporary:
            output = Path(temporary) / "release"

            status = RELEASE_EVIDENCE.main([
                "--root", str(ROOT),
                "--output-dir", str(output),
                "--trx", "lifecycle-fast=missing.trx",
                "--trx", "postgres=also-missing.trx",
            ])

            self.assertEqual(0, status)
            evidence = json.loads((output / "release-evidence.json").read_text())
            self.assertEqual("not-run", evidence["lifecycle"]["status"])
            self.assertEqual("not-run", evidence["postgres"]["status"])
            self.assertIn("lifecycle-fast", evidence["lifecycle"]["suites"])
            self.assertIn("postgres", evidence["postgres"]["suites"])

    def test_trx_totals_and_warning_status_are_derived(self):
        trx = """<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <ResultSummary outcome="Completed">
    <Counters total="95" executed="95" passed="95" failed="0" notExecuted="0" />
  </ResultSummary>
</TestRun>
"""
        with tempfile.TemporaryDirectory(prefix="prime-release-evidence-") as temporary:
            work = Path(temporary)
            trx_path = work / "lifecycle.trx"
            warning_path = work / "warnings.json"
            output = work / "release"
            trx_path.write_text(trx)
            warning_path.write_text(json.dumps({
                "status": "pass",
                "projects": [{"name": "Game", "currentCount": 41, "delta": 0}],
                "violations": [],
            }))

            status = RELEASE_EVIDENCE.main([
                "--root", str(ROOT),
                "--output-dir", str(output),
                "--gate", "builds=success",
                "--trx", f"lifecycle-fast={trx_path}",
                "--warning-report", str(warning_path),
            ])

            self.assertEqual(0, status)
            evidence = json.loads((output / "release-evidence.json").read_text())
            self.assertEqual("passed", evidence["builds"]["status"])
            self.assertEqual("passed", evidence["lifecycle"]["status"])
            self.assertEqual(95, evidence["lifecycle"]["totals"]["passed"])
            self.assertEqual("passed", evidence["warnings"]["status"])
            self.assertEqual(41, evidence["warnings"]["projects"]["Game"]["count"])
            self.assertIn("Gameplay protocol: `25`", (output / "release-evidence.md").read_text())

    def test_a_failed_suite_dominates_other_gate_inputs(self):
        self.assertEqual(
            "failed",
            RELEASE_EVIDENCE._combine_status(["passed", "not-run", "failed"]),
        )

    def test_workflows_preserve_results_and_generate_evidence_on_failure(self):
        build = (ROOT / ".github/workflows/build.yml").read_text(encoding="utf-8")
        network = (ROOT / ".github/workflows/network-tests.yml").read_text(encoding="utf-8")

        self.assertIn("python3 tools/generate-release-evidence.py", build)
        self.assertIn("python3 tools/generate-release-evidence.py", network)
        self.assertGreaterEqual(build.count("if: always()"), 4)
        self.assertGreaterEqual(network.count("if: always()"), 8)
        self.assertIn('logger "trx;LogFileName=lifecycle-fast-node.trx"', network)
        self.assertIn('logger "trx;LogFileName=postgres-backend.trx"', network)


if __name__ == "__main__":
    unittest.main()
