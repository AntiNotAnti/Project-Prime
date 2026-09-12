import json
import plistlib
import subprocess
import shutil
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
TOOL = ROOT / "tools" / "package-macos-app.py"
UPDATE_TOOL = ROOT / "tools" / "update-release.py"


class MacOsAppPackageTests(unittest.TestCase):
    def test_package_has_project_prime_identity_icon_and_version(self):
        with tempfile.TemporaryDirectory(prefix="project-prime-macos-app-") as temporary:
            root = Path(temporary)
            source = root / "published"
            source.mkdir()
            (source / "ProjectPrime").write_bytes(b"local client")
            app = source / "Project Prime.app"

            subprocess.run(
                ["python3", str(TOOL), "package", str(source), str(app),
                 "--version", "v2.4.6"], check=True,
            )

            with (app / "Contents" / "Info.plist").open("rb") as handle:
                info = plistlib.load(handle)
            self.assertEqual("Project Prime", info["CFBundleName"])
            self.assertEqual("Project Prime", info["CFBundleDisplayName"])
            self.assertEqual("com.antinotanti.projectprime", info["CFBundleIdentifier"])
            self.assertEqual("2.4.6", info["CFBundleShortVersionString"])
            self.assertEqual("2.4.6", info["CFBundleVersion"])
            self.assertEqual("project-prime.icns", info["CFBundleIconFile"])
            icon = app / "Contents" / "Resources" / "project-prime.icns"
            self.assertTrue(icon.is_file())
            self.assertTrue(icon.read_bytes().startswith(b"icns"))
            if shutil.which("iconutil"):
                iconset = root / "project-prime.iconset"
                result = subprocess.run(
                    ["iconutil", "--convert", "iconset", "--output", str(iconset), str(icon)],
                    check=False, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                )
                self.assertEqual(0, result.returncode, result.stderr)
            launcher = app / "Contents" / "MacOS" / "ProjectPrime"
            self.assertTrue(launcher.stat().st_mode & 0o111)
            self.assertIn(b'cd -- "$ROOT"\n', launcher.read_bytes())

            subprocess.run(
                ["python3", str(TOOL), "check", str(app), "--version", "2.4.6"], check=True,
            )

            # The bundle is an optional launch surface inside the existing
            # extracted package, so the signed update contract must manage its
            # metadata and wrapper along with the root executable.
            (source / "editor").mkdir()
            (source / "editor" / "ProjectPrime.Editor").write_bytes(b"editor")
            contract = root / "contract"
            subprocess.run(
                ["python3", str(UPDATE_TOOL), "--version", "2.4.6",
                 "--output", str(contract), "--desktop-dir", str(source)], check=True,
            )
            manifest = json.loads((source / "release-files.json").read_text())
            managed = {entry["path"] for entry in manifest["files"]}
            self.assertIn("Project Prime.app/Contents/Info.plist", managed)
            self.assertIn("Project Prime.app/Contents/MacOS/ProjectPrime", managed)

    def test_check_rejects_avalanche_default_identity(self):
        with tempfile.TemporaryDirectory(prefix="project-prime-macos-app-") as temporary:
            root = Path(temporary)
            source = root / "published"
            source.mkdir()
            (source / "ProjectPrime").write_bytes(b"local client")
            app = source / "Project Prime.app"
            subprocess.run(
                ["python3", str(TOOL), "package", str(source), str(app),
                 "--version", "1.0.0"], check=True, stdout=subprocess.DEVNULL,
            )
            plist = app / "Contents" / "Info.plist"
            with plist.open("rb") as handle:
                info = plistlib.load(handle)
            info["CFBundleName"] = "Avalonia Application"
            with plist.open("wb") as handle:
                plistlib.dump(info, handle)
            result = subprocess.run(
                ["python3", str(TOOL), "check", str(app)],
                check=False, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
            )
            self.assertNotEqual(0, result.returncode)
            self.assertIn("CFBundleName", result.stderr)


if __name__ == "__main__":
    unittest.main()
