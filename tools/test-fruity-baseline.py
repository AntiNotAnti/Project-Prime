"""Plan-only contract check; never launches native rendering or a build."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


class BaselinePlanCheck(unittest.TestCase):
    def test_independent_arms_and_separate_images(self):
        with tempfile.TemporaryDirectory() as directory:
            script = Path(__file__).with_name("fruity-baseline.py")
            subprocess.run([sys.executable, str(script), "--plan-only", "--tool", directory + "/missing.dll",
                            "--dotnet", directory + "/missing-dotnet", "--assets-data-dir", directory,
                            "--game-data-version", "test-unavailable", "--output-dir", directory + "/output",
                            "--camera", "0,0,0", "--facing", "0,0,-1", "--hz", "60",
                            "--ab", "legacy-list", "--ab", "legacy-room-vis", "--ab", "legacy-item-vis"],
                           check=True, capture_output=True)
            manifest = json.loads((Path(directory) / "output/manifest.json").read_text())
            self.assertEqual(len(manifest["runs"]), 8)
            for run in manifest["runs"]:
                self.assertEqual(run["status"], "NOT RUN")
                self.assertNotIn("-shots", run["command"])
                self.assertIn("-shots", run["imageEvidence"]["command"])
                legacy_flags = [flag for flag in run["command"] if flag.startswith("-gllegacy")]
                self.assertEqual(len(legacy_flags), 0 if run["name"].endswith("default") else 1)
                env = run["environmentOverrides"]
                self.assertEqual(env["PROJECT_PRIME_GL_LEGACY_ROOM_VIS"],
                                 "1" if run["name"].endswith("legacy-room-vis") else "0")
                self.assertEqual(env["PROJECT_PRIME_GL_LEGACY_ITEM_VIS"],
                                 "1" if run["name"].endswith("legacy-item-vis") else "0")


if __name__ == "__main__":
    unittest.main()
