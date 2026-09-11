"""Contract tests for the protected Linux client launch smoke."""

from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SMOKE = ROOT / "tools/protection/linux-client-smoke.py"


class LinuxClientProtectionSmokeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="prime-linux-smoke-test-")
        self.addCleanup(self.temporary.cleanup)
        self.package = Path(self.temporary.name)

    def write_client(self, body):
        client = self.package / "ProjectPrime"
        client.write_text("#!/bin/sh\n" + body, encoding="utf-8")
        client.chmod(0o755)

    def run_smoke(self, timeout="2"):
        return subprocess.run([
            sys.executable, str(SMOKE), "--package", str(self.package),
            "--timeout", timeout,
        ], capture_output=True, text=True, check=False)

    def test_launches_exact_text_launcher_and_feeds_quit(self):
        self.write_client(
            'test "$1" = "-launcher" || exit 21\n'
            'test "$2" = "-text" || exit 22\n'
            'read -r answer\n'
            'test "$answer" = "q" || exit 23\n'
            "printf '  Project Prime v1.0.0\\n'\n"
        )

        result = self.run_smoke()

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("single-file launcher smoke passed", result.stdout)

    def test_rejects_successful_process_without_launcher_marker(self):
        self.write_client("read -r answer\nprintf 'another application\\n'\n")

        result = self.run_smoke()

        self.assertNotEqual(0, result.returncode)
        self.assertIn("omitted the Project Prime launcher marker", result.stderr)

    def test_kills_launcher_that_does_not_exit_within_bound(self):
        self.write_client("sleep 5\n")

        result = self.run_smoke("0.1")

        self.assertNotEqual(0, result.returncode)
        self.assertIn("did not exit within", result.stderr)


if __name__ == "__main__":
    unittest.main()
