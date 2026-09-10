"""CLI contract tests for the complete build orchestrator."""
from pathlib import Path
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "build-all.sh"


class BuildAllContractTests(unittest.TestCase):
    def test_help_is_safe_without_build_tooling(self):
        result = subprocess.run([str(SCRIPT), "--help"], capture_output=True, text=True, check=False)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("--skip-android", result.stdout)
        self.assertIn("PRIME_ANDROID_KEYSTORE_FILE", result.stdout)

    def test_invalid_version_fails_before_build(self):
        result = subprocess.run(
            [str(SCRIPT), "--version", "bad version", "--skip-android"],
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(2, result.returncode)
        self.assertIn("Version must", result.stderr)

    def test_existing_output_is_never_overwritten(self):
        with tempfile.TemporaryDirectory(prefix="prime-build-all-test-") as directory:
            result = subprocess.run(
                [str(SCRIPT), "--output", directory, "--skip-android"],
                capture_output=True,
                text=True,
                check=False,
            )
        self.assertEqual(1, result.returncode)
        self.assertIn("Refusing to overwrite", result.stderr)


if __name__ == "__main__":
    unittest.main()
