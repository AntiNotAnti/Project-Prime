"""Regression checks for portable, installable release ownership manifests."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

WRITER = Path(__file__).with_name("write-release-manifest.py")


def write(root):
    return subprocess.run(
        [sys.executable, str(WRITER), str(root)],
        text=True, capture_output=True
    )


class ManifestPublishingTests(unittest.TestCase):
    def test_manifest_contains_hidden_desktop_metadata(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "ProjectPrime").write_text("binary")
            (root / ".project-prime-desktop.json").write_text(
                '{"Version":1,"GameVersion":"0.1.53","StudioVersion":"0.1.53","IpcVersion":1}'
            )
            self.assertEqual(0, write(root).returncode)
            manifest = json.loads((root / ".project-prime-files.json").read_text())
            self.assertIn(".project-prime-desktop.json", manifest["Files"])
            self.assertIn("ProjectPrime", manifest["Files"])
            self.assertEqual(len(manifest["Hashes"]), len(manifest["Files"]))

    def test_runtime_lock_is_never_published(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "ProjectPrime").write_text("binary")
            (root / ".project-prime-update.lock").write_text("")
            result = write(root)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("unsafe file", result.stderr + result.stdout)

    def test_symlink_cannot_escape_manifest_root(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            target = root / "target"
            target.write_text("binary")
            try:
                (root / "linked").symlink_to(target)
            except OSError:
                self.skipTest("symlinks unavailable on this runner")
            result = write(root)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("symlink", result.stderr + result.stdout)

    @unittest.skipIf(sys.platform.startswith("win"), "Windows cannot create colon names")
    def test_nonportable_filename_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "bad:name").write_text("binary")
            result = write(root)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("unsafe file", result.stderr + result.stdout)


if __name__ == "__main__":
    unittest.main()
