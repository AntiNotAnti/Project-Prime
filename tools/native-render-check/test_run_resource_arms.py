import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import run_resource_arms


class ResourceRunnerTests(unittest.TestCase):
    def setUp(self):
        platform_patch=patch.object(run_resource_arms.platform,"platform",return_value="test platform")
        platform_patch.start()
        self.addCleanup(platform_patch.stop)

    def setup_paths(self, directory):
        root = Path(directory)
        binary = root / "native-render-check.dll"
        binary.write_bytes(b"harness")
        binary.with_name("ProjectPrime.dll").write_bytes(b"client")
        return binary, root / "campaign"

    def check_output(self, command, **kwargs):
        if command[-1] == "--info":
            return "test SDK/runtime info"
        return "" if command[-1] == "--porcelain" else "test-source-sha"

    def test_dryrun_same_fixture_sequential_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            binary, output = self.setup_paths(directory)
            argv = ["runner", "--binary", str(binary), "--output", str(output), "--dry-run"]
            with patch.object(sys, "argv", argv), patch.object(subprocess, "check_output", self.check_output), patch.object(subprocess, "run") as run:
                self.assertEqual(run_resource_arms.main(), 0)
                run.assert_not_called()
            manifest = json.loads((output / "manifest.json").read_text())
            self.assertEqual(manifest["runOrder"], ["combined", "map-gl", "studio", "audio"])
            for record in manifest["runs"]:
                command = record["command"]
                self.assertEqual(command[command.index("-resourcecycles") + 1], "60")
                self.assertEqual(command[command.index("-resourceframes") + 1], "120")
                self.assertEqual(command[command.index("-resource-arm") + 1], record["arm"])

    def test_timeout_preserves_failed_manifest_and_stops_campaign(self):
        with tempfile.TemporaryDirectory() as directory:
            binary, output = self.setup_paths(directory)
            argv = ["runner", "--binary", str(binary), "--output", str(output)]
            with patch.object(sys, "argv", argv), patch.object(subprocess, "check_output", self.check_output), patch.object(subprocess, "run", side_effect=subprocess.TimeoutExpired("test", 1)) as run:
                self.assertEqual(run_resource_arms.main(), 1)
                self.assertEqual(run.call_count, 1)
            manifest = json.loads((output / "manifest.json").read_text())
            self.assertEqual(manifest["runs"][0]["exitCode"], -1)
            self.assertIn("timeout", manifest["runs"][0]["error"])
            self.assertTrue((output / "00-combined.log").exists())

    def test_fresh_directory_required(self):
        with tempfile.TemporaryDirectory() as directory:
            binary, output = self.setup_paths(directory)
            output.mkdir()
            (output / "old.json").write_text("preserve")
            argv = ["runner", "--binary", str(binary), "--output", str(output), "--dry-run"]
            with patch.object(sys, "argv", argv), patch.object(subprocess, "check_output", self.check_output):
                with self.assertRaises(SystemExit):
                    run_resource_arms.main()
            self.assertEqual((output / "old.json").read_text(), "preserve")


if __name__ == "__main__":
    unittest.main()
