import argparse
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import run_context_controls as runner


class ContextRunnerTests(unittest.TestCase):
    def test_dry_run_preserves_invariant_workload_and_capture_policy(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            binary = root / "native-render-check.dll"
            binary.write_bytes(b"harness")
            (root / "ProjectPrime.dll").write_bytes(b"client")
            out = root / "new-campaign"
            argv = ["runner", "--binary", str(binary), "--output", str(out), "--cycles", "120", "180", "--capture-native-stacks", "--dry-run"]
            with patch("sys.argv", argv), patch.object(runner.subprocess, "check_output", side_effect=["source\n", ""]), patch.object(runner.subprocess, "Popen") as native:
                self.assertEqual(runner.main(), 0)
                native.assert_not_called()
            data = json.loads((out / "runner-manifest.json").read_text())
            cmd = data["command"]
            self.assertEqual(cmd[cmd.index("-resource-context-cycles") + 1], "120,180")
            self.assertEqual(cmd[cmd.index("-resource-arm") + 1], "map-gl")
            self.assertEqual(cmd[cmd.index("-resourceframes") + 1], "120")
            self.assertIn("-resource-drain", cmd)
            self.assertEqual(data["environment"]["MallocStackLogging"], "1")
            self.assertEqual(data["captures"], [])

    def test_failed_external_capture_preserves_commands_and_files(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            with patch.object(runner.subprocess, "run", side_effect=FileNotFoundError("not installed")):
                data = runner.capture(42, "context-00-after-disposal", root)
            self.assertEqual([s["exitCode"] for s in data["steps"]], [-1, -1])
            self.assertEqual(data["steps"][1]["command"], ["malloc_history", "42", "-allBySize", "-fullStacks"])
            for step in data["steps"]:
                self.assertIn("not installed", Path(step["path"]).read_text())
                self.assertEqual(step["sha256"], runner.sha256(Path(step["path"])))

    def test_driver_drain_capture_has_explicit_final_phases_and_acknowledgements(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            binary = root / "native-render-check.dll"
            binary.write_bytes(b"harness")
            (root / "ProjectPrime.dll").write_bytes(b"client")
            out = root / "new-campaign"
            argv = ["runner", "--binary", str(binary), "--output", str(out), "--cycles", "30", "--driver-drain", "--shader-source-ledger", "--capture-native-stacks", "--dry-run"]
            with patch("sys.argv", argv), patch.object(runner.subprocess, "check_output", side_effect=["source\n", ""]), patch.object(runner.subprocess, "Popen") as native:
                self.assertEqual(runner.main(), 0)
                native.assert_not_called()
            cmd = json.loads((out / "runner-manifest.json").read_text())["command"]
            self.assertIn("-resource-context-driver-drain", cmd)
            self.assertIn("-resource-shader-source-ledger", cmd)
            self.assertEqual(cmd[cmd.index("-resource-context-phase-acks") + 1], str(out.resolve() / "phase-acks"))

    def test_existing_output_is_rejected_before_native_start(self):
        with tempfile.TemporaryDirectory() as temp:
            with patch("sys.argv", ["runner", "--binary", "unused", "--output", temp]), patch.object(runner.subprocess, "Popen") as native:
                with self.assertRaises(SystemExit):
                    runner.main()
                native.assert_not_called()


if __name__ == "__main__":
    unittest.main()
