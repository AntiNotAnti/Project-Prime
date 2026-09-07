"""Focused tests for the repository's asset and custom-map shell guards."""
import shutil
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile


ROOT = Path(__file__).resolve().parents[2]
ASSET_GUARD = ROOT / "tools/check-no-game-assets.sh"
MAP_GUARD = ROOT / "tools/check-maps-shipped.sh"


@unittest.skipUnless(shutil.which("unzip"), "check-maps-shipped.sh requires unzip")
class MapGuardTests(unittest.TestCase):
    def run_guard(self, script, root):
        return subprocess.run(
            ["bash", str(script), str(root)],
            cwd=ROOT,
            text=True,
            capture_output=True,
            check=False,
        )

    def write_bundle(self, root, *, include_bsp=True, include_texture=True, padding=8192):
        maps = root / "maps"
        maps.mkdir()
        bundle = maps / "large-listing.fpmap"
        with zipfile.ZipFile(bundle, "w", compression=zipfile.ZIP_STORED) as archive:
            if include_bsp:
                # Put the matching entry first so grep -q would terminate while
                # unzip still has a large listing to write.
                archive.writestr("room.bsp", b"custom level")
            archive.writestr("room.json", b'{"textures":"room.tex"}')
            if include_texture:
                archive.writestr("room.tex", b"custom texture")
            for index in range(padding):
                archive.writestr("padding/file%05d.dat" % index, b"")
        return bundle

    def test_large_bundle_listing_with_level_and_texture_passes(self):
        with tempfile.TemporaryDirectory(prefix="fruity-map-guard-") as temporary:
            self.write_bundle(Path(temporary))
            result = self.run_guard(MAP_GUARD, Path(temporary))
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("carries its level", result.stdout)
            self.assertIn("carries its textures", result.stdout)

    def test_bundle_without_level_remains_rejected(self):
        with tempfile.TemporaryDirectory(prefix="fruity-map-guard-") as temporary:
            self.write_bundle(Path(temporary), include_bsp=False)
            result = self.run_guard(MAP_GUARD, Path(temporary))
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("no level in it", result.stdout)

    def test_bundle_without_named_texture_remains_rejected(self):
        with tempfile.TemporaryDirectory(prefix="fruity-map-guard-") as temporary:
            self.write_bundle(Path(temporary), include_texture=False)
            result = self.run_guard(MAP_GUARD, Path(temporary))
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("does not carry it", result.stdout)

    def test_asset_guard_accepts_safe_custom_level_with_spaces(self):
        with tempfile.TemporaryDirectory(prefix="fruity-asset-guard-") as temporary:
            root = Path(temporary) / "maps with spaces"
            root.mkdir()
            (root / "custom.bsp").write_bytes(b"level")
            (root / "README.txt").write_text("source")
            result = self.run_guard(ASSET_GUARD, root)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_asset_guard_rejects_unlisted_cartridge_like_image(self):
        with tempfile.TemporaryDirectory(prefix="fruity-asset-guard-") as temporary:
            root = Path(temporary) / "maps with spaces"
            root.mkdir()
            (root / "unlisted.png").write_bytes(b"not an allowlisted project asset")
            result = self.run_guard(ASSET_GUARD, root)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("a game asset, or a picture of one", result.stdout)


if __name__ == "__main__":
    unittest.main()
