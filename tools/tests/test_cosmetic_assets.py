"""Cosmetic source assets remain deterministic and package through the shared client."""
from hashlib import sha256
import json
from pathlib import Path
import struct
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]
ASSET_DIR = ROOT / "src/Client.Presentation/Assets/Cosmetics"
ATLAS = ASSET_DIR / "project-prime-vfx-atlas.png"
MANIFEST = ASSET_DIR / "project-prime-vfx-atlas.json"


class CosmeticAssetTests(unittest.TestCase):
    def test_atlas_matches_immutable_manifest(self):
        metadata = json.loads(MANIFEST.read_text(encoding="utf-8"))
        image = ATLAS.read_bytes()
        self.assertEqual("89504e470d0a1a0a", image[:8].hex())
        width, height = struct.unpack(">II", image[16:24])
        self.assertEqual((metadata["width"], metadata["height"]), (width, height))
        self.assertEqual(metadata["sha256"], sha256(image).hexdigest())
        self.assertLessEqual(len(image), 2 * 1024 * 1024)

    def test_sprite_regions_are_unique_and_bounded(self):
        metadata = json.loads(MANIFEST.read_text(encoding="utf-8"))
        keys = set()
        for sprite in metadata["sprites"]:
            self.assertNotIn(sprite["key"], keys)
            keys.add(sprite["key"])
            self.assertGreaterEqual(sprite["x"], 0)
            self.assertGreaterEqual(sprite["y"], 0)
            self.assertGreater(sprite["width"], 2 * metadata["sampling"]["insetPixels"])
            self.assertGreater(sprite["height"], 2 * metadata["sampling"]["insetPixels"])
            self.assertLessEqual(sprite["x"] + sprite["width"], metadata["width"])
            self.assertLessEqual(sprite["y"] + sprite["height"], metadata["height"])
        self.assertEqual(20, len(keys))

    def test_shared_presentation_packages_assets_for_desktop_and_android(self):
        presentation = ET.parse(
            ROOT / "src/Client.Presentation/Client.Presentation.csproj").getroot()
        resources = {item.attrib.get("Include") for item in presentation.iter("AvaloniaResource")}
        self.assertIn("Assets/Cosmetics/**", resources)

        android = ET.parse(ROOT / "src/Android/Android.csproj").getroot()
        references = {item.attrib.get("Include") for item in android.iter("ProjectReference")}
        self.assertIn("../Client.Presentation/Client.Presentation.csproj", references)

    def test_original_project_prime_asset_is_explicitly_allowlisted(self):
        allowlist = (ROOT / "tools/asset-guard-allow.txt").read_text(encoding="utf-8")
        self.assertIn(
            "src/Client.Presentation/Assets/Cosmetics/project-prime-vfx-atlas.png",
            allowlist.splitlines(),
        )


if __name__ == "__main__":
    unittest.main()
