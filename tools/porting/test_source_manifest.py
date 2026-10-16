import json
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / "docs" / "porting" / "second-hunt-manifest.json"
DOC = ROOT / "docs" / "porting" / "second-hunt-content.md"

class PortManifestContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = json.loads(MANIFEST.read_text(encoding="utf-8"))

    def test_pinned_source_and_asset_policy(self):
        p = self.data["provenance"]
        self.assertEqual(p["repository"], "BountyHunterKanden/SecondHunt")
        self.assertRegex(p["commit"], r"^[0-9a-f]{40}$")
        self.assertRegex(p["targetBaseline"], r"^[0-9a-f]{40}$")
        self.assertFalse(self.data["defaultPolicy"]["enabled"])
        self.assertEqual(self.data["defaultPolicy"]["networkAssets"], "none")
        self.assertEqual(self.data["defaultPolicy"]["clientAssets"], "local-import-only")
        self.assertTrue(self.data["defaultPolicy"]["dedicatedServerMustHaveContent"])
        self.assertIn(p["commit"], DOC.read_text(encoding="utf-8"))

    def test_exact_ids_and_recipe_paths(self):
        chars = self.data["characters"]
        self.assertEqual({c["id"] for c in chars}, {"darksamus", "trooper"})
        self.assertEqual(len(chars), len({c["id"] for c in chars}))
        self.assertEqual({c["baseHunter"] for c in chars}, {"Samus", "Weavel"})
        self.assertEqual(len(self.data["arenas"]), 6)
        keys = [a["key"] for a in self.data["arenas"]]
        self.assertEqual(len(keys), len(set(keys)))
        self.assertEqual(keys, [
            "M01_SidehopperStation", "M02_Spires", "M03_CrossfireChaos",
            "M04_Pipeline", "M05_SpiderComplex", "M06_ShootingGallery"])
        for c in chars:
            self.assertEqual(len(c["cosmeticSkins"]), len(c["importRecipes"]))
            for recipe in c["importRecipes"]:
                self.assertRegex(recipe, r"^recipes/bodies/[A-Za-z0-9]+\.json$")
        for arena in self.data["arenas"]:
            self.assertEqual(arena["sourceGame"], "mp2")
            self.assertEqual(arena["recipe"], "recipes/arenas/" + arena["key"] + ".json")

    def test_receipt_remains_metadata_only(self):
        files = [p for p in MANIFEST.parent.rglob("*") if p.is_file()]
        self.assertTrue(files)
        self.assertTrue(all(p.suffix in (".md", ".json") for p in files),
                        "Never commit extracted proprietary source or generated assets.")
        self.assertEqual({g["id"] for g in self.data["games"]}, {"mp1", "mp2", "mp3"})

if __name__ == "__main__":
    unittest.main()
