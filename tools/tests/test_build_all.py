"""CLI contract tests for the complete build orchestrator."""
from pathlib import Path
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "build-all.sh"
IMPLEMENTATION = ROOT / "tools" / "build-all.sh"


class BuildAllContractTests(unittest.TestCase):
    def test_help_is_safe_without_build_tooling(self):
        result = subprocess.run([str(SCRIPT), "--help"], capture_output=True, text=True, check=False)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("--skip-android", result.stdout)
        self.assertIn("--no-client-protection", result.stdout)
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

    def test_every_desktop_client_bundles_the_matching_editor(self):
        script = IMPLEMENTATION.read_text(encoding="utf-8")
        self.assertIn("dotnet publish src/Editor/Editor.csproj", script)
        self.assertIn('-o "$destination/editor"', script)
        self.assertIn('--executable "$editor_executable" "$destination/editor"', script)
        self.assertIn('"desktop_editor": {', script)

    def test_bundled_editor_publish_excludes_standalone_symbols(self):
        project = ET.parse(ROOT / "src/Editor/Editor.csproj").getroot()
        properties = {
            child.tag: child.text
            for group in project.findall("PropertyGroup")
            for child in group
        }
        self.assertEqual("embedded", properties.get("DebugType"))
        self.assertEqual(
            "false", properties.get("CopyOutputSymbolsToPublishDirectory"))
        for dependency in ("MapPlatform", "Renderer", "Imaging"):
            dependency_project = ET.parse(
                ROOT / f"src/{dependency}/{dependency}.csproj").getroot()
            debug_types = dependency_project.findall(".//DebugType")
            self.assertTrue(
                any(debug_type.text == "embedded" for debug_type in debug_types),
                dependency,
            )


if __name__ == "__main__":
    unittest.main()
