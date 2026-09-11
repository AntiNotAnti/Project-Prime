"""Contract tests for desktop SDL GPU publish validation."""
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools/check-renderer-package.py"
SPEC = importlib.util.spec_from_file_location("check_renderer_package", SCRIPT)
CHECKER = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = CHECKER
SPEC.loader.exec_module(CHECKER)


class RendererPackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="prime-renderer-package-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def make_package(self, rid="linux-x64"):
        executable, sdl_library, shader_format = CHECKER.RUNTIME_FILES[rid]
        (self.root / executable).write_bytes(b"app")
        (self.root / sdl_library).write_bytes(b"sdl")
        shader_root = self.root / "Rendering/Shaders/Generated"
        shader_root.mkdir(parents=True)
        for manifest_name in CHECKER.MANIFESTS:
            stem = "scene_triangle" if manifest_name == "manifest.json" else manifest_name.removesuffix("_manifest.json")
            artifact_name = f"{stem}.vert.{shader_format}"
            contents = f"{rid}:{stem}".encode()
            (shader_root / artifact_name).write_bytes(contents)
            manifest = {
                "artifacts": {
                    f"vertex_{shader_format}": {
                        "format": shader_format.upper(),
                        "path": artifact_name,
                        "sha256": hashlib.sha256(contents).hexdigest(),
                    }
                }
            }
            (shader_root / manifest_name).write_text(json.dumps(manifest), encoding="utf-8")

    def test_complete_platform_package_passes(self):
        self.make_package("linux-x64")

        self.assertEqual([], CHECKER.inspect_package(self.root, "linux-x64"))

    def test_editor_package_can_declare_its_own_executable(self):
        self.make_package("linux-x64")
        (self.root / "ProjectPrime").rename(self.root / "ProjectPrime.Editor")

        self.assertEqual(
            [],
            CHECKER.inspect_package(
                self.root, "linux-x64", "ProjectPrime.Editor"
            ),
        )

    def test_missing_sdl_and_stale_shader_are_reported(self):
        self.make_package("win-arm64")
        (self.root / "SDL3.dll").unlink()
        shader = next((self.root / "Rendering/Shaders/Generated").glob("*.dxil"))
        shader.write_bytes(b"changed")

        errors = CHECKER.inspect_package(self.root, "win-arm64")

        self.assertIn("missing SDL3.dll", errors)
        self.assertIn(f"stale shader artifact {shader.name}", errors)

    def test_legacy_renderer_assembly_is_rejected(self):
        self.make_package("linux-x64")
        (self.root / "OpenTK.Graphics.dll").write_bytes(b"legacy")

        errors = CHECKER.inspect_package(self.root, "linux-x64")

        self.assertIn(
            "legacy renderer assembly remains: OpenTK.Graphics.dll", errors
        )

    def test_client_pins_stable_native_sdl_without_ppy_snapshot_assets(self):
        project = ET.parse(ROOT / "src/Client/Client.csproj").getroot()
        packages = {
            item.attrib["Include"]: item
            for item in project.iter("PackageReference")
        }

        binding = packages["ppy.SDL3-CS"]
        self.assertEqual("2026.722.0", binding.attrib["Version"])
        self.assertEqual("native", binding.attrib["ExcludeAssets"])
        for package in ("SDL3-CS.Windows", "SDL3-CS.Linux", "SDL3-CS.MacOS"):
            self.assertEqual("3.4.16", packages[package].attrib["Version"])

    def test_desktop_client_has_no_legacy_opentk_renderer_packages(self):
        project = ET.parse(ROOT / "src/Client/Client.csproj").getroot()
        packages = {
            item.attrib["Include"]
            for item in project.iter("PackageReference")
        }

        self.assertNotIn("OpenTK", packages)
        self.assertNotIn("OpenTK.Graphics", packages)
        self.assertNotIn("OpenTK.Windowing.Desktop", packages)
        self.assertIn("OpenTK.Mathematics", packages)


if __name__ == "__main__":
    unittest.main()
