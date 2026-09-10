"""Authoritative report validation uses typed summaries, never legacy text heuristics."""
import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "compare-reports.py"
spec = importlib.util.spec_from_file_location("compare_reports", SCRIPT)
reports = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reports)

SAMPLE = """AUTHCHECK slot=0 frames=600 snapshots=300 localTravel=32.10 movingRemotes=1 lit=True result=PASS
PREDICTION samples=299 meanError=0.030 worstError=2.400 corrections=17 hard=8 historyMisses=22
REPLICATION world=True combatEvents=55 damageEvents=0
SPECTATOR requested=False observed=False rejoined=False
"""


class ReportTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="project-prime-reports-")
        self.addCleanup(self.temporary.cleanup)
        self.path = Path(self.temporary.name) / "client.log"
        self.path.write_text(SAMPLE)

    def test_corrections_history_misses_and_no_damage_are_valid(self):
        report = reports.read_report(self.path)
        self.assertEqual(report["PREDICTION"]["hard"], 8)
        self.assertEqual(report["PREDICTION"]["historyMisses"], 22)
        self.assertEqual(report["REPLICATION"]["damageEvents"], 0)

    def test_failed_missing_malformed_and_nonfinite_reports_are_rejected(self):
        invalid = [
            SAMPLE.replace("result=PASS", "result=FAIL"),
            SAMPLE.replace("meanError=0.030", "meanError=NaN"),
            SAMPLE.replace("worstError=2.400", "worstError=Infinity"),
            SAMPLE.replace("samples=299", "samples=-1"),
            SAMPLE.replace("world=True", "world=False"),
            SAMPLE.replace("requested=False", "requested=True"),
            SAMPLE.replace("lit=True", "lit=1"),
            SAMPLE.replace("frames=600", "frames=600 frames=700"),
            SAMPLE.replace("slot=0", "slot=8"),
            SAMPLE + "Unhandled exception. test crash\n",
            SAMPLE + SAMPLE,
            SAMPLE.split("SPECTATOR")[0],
            "legacy report only\n",
        ]
        for text in invalid:
            with self.subTest(text=text):
                self.path.write_text(text)
                with self.assertRaises(ValueError):
                    reports.read_report(self.path)

    def test_cli_accepts_different_peer_metrics_and_propagates_failure(self):
        peer = self.path.with_name("peer.log")
        peer.write_text(SAMPLE.replace("slot=0", "slot=1").replace("snapshots=300", "snapshots=290")
                       .replace("meanError=0.030", "meanError=0.900"))
        command = [sys.executable, str(SCRIPT), str(self.path), str(peer)]
        result = subprocess.run(command, capture_output=True, text=True, timeout=5, check=False)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("2/2 authoritative client reports passed", result.stdout)
        peer.write_text("missing report")
        result = subprocess.run(command, capture_output=True, text=True, timeout=5, check=False)
        self.assertEqual(result.returncode, 1)
        self.assertIn("missing sections", result.stdout)


if __name__ == "__main__":
    unittest.main()
