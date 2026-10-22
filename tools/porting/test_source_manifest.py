import json
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / "tools" / "porting" / "fixtures" / "second-hunt" / "second-hunt-manifest.json"
DOC = ROOT / "tools" / "porting" / "fixtures" / "second-hunt" / "second-hunt-content.md"

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


    def test_audit_preserves_distinct_historical_provenance(self):
        p, audit = self.data["provenance"], self.data["integrationAudit"]
        self.assertEqual(p["commit"], "c16a907b3658c2fd4079e394a73711596a85caee")
        self.assertEqual(p["targetBaseline"], "ebbf3c51fa6f205ac3b8c952f4f3ab67d0e7cc80")
        self.assertEqual(audit["previousSourceCommit"], p["commit"])
        self.assertEqual(audit["sourceCommit"], "41452f2d053fcfa679e33c00a73d120a53f59e61")
        self.assertEqual(audit["targetBaseline"], "5bc952147ce45f87d777ff0de4b2927f0c27ef9e")
        self.assertEqual(audit["status"], "source-audit-only")
        self.assertNotIn("MP2DarkSamusFinal", self.data["characters"][0]["cosmeticSkins"])

    def test_delta_has_complete_frozen_commit_and_path_inventory(self):
        audit = self.data["integrationAudit"]
        delta = json.loads((MANIFEST.parent / audit["sourceDelta"]).read_text(encoding="utf-8"))
        self.assertEqual(delta["fromExclusive"], audit["previousSourceCommit"])
        self.assertEqual(delta["toInclusive"], audit["sourceCommit"])
        self.assertEqual(delta["commitCount"], len(delta["commits"]))
        self.assertEqual(delta["commitCount"], 11)
        paths = delta["changedPaths"]
        self.assertEqual(delta["changedPathCount"], len(paths))
        self.assertEqual(len(paths), 641)
        self.assertEqual(paths, sorted(set(paths)))
        self.assertEqual(delta["commits"][-1]["commit"], audit["sourceCommit"])
        commits = set()
        historical_paths = set()
        for commit in delta["commits"]:
            self.assertRegex(commit["commit"], r"^[0-9a-f]{40}$")
            self.assertNotIn(commit["commit"], commits)
            commits.add(commit["commit"])
            self.assertTrue(commit["subject"])
            self.assertTrue(commit["changedPaths"])
            historical_paths.update(commit["changedPaths"])
            self.assertTrue(all(not Path(path).is_absolute() and ".." not in Path(path).parts
                                for path in commit["changedPaths"]))
        # Intermediate commits can touch paths reverted before the final reference.
        self.assertTrue(set(paths) <= historical_paths)

    def test_current_contract_witnesses_do_not_invent_new_defaults(self):
        audit = self.data["integrationAudit"]
        wire, replay = audit["wire"], audit["replay"]
        protocol = (ROOT / "src/MphRead/Mods/Network/NetProtocol.cs").read_text()
        # The manifest is a frozen integration audit, not the live wire-version
        # owner. Later unrelated protocol bumps must not rewrite its provenance.
        self.assertEqual(wire["protocol"], 61)
        live_version = re.search(r"\bProtocolVersion\s*=\s*(\d+)\s*;", protocol)
        self.assertIsNotNone(live_version, "live protocol must remain explicitly declared")
        self.assertGreaterEqual(int(live_version.group(1)), wire["protocol"])
        self.assertLessEqual(int(live_version.group(1)), 255, "demo protocol header is one byte")
        compatibility = (ROOT / "src/MphRead/Mods/Network/ReplayIdentityCompatibility.cs").read_text()
        self.assertRegex(compatibility, r"OldestReplayProtocol\s*=\s*4\s*;")
        self.assertIn("protocol >= OldestReplayProtocol && protocol <= NetConfig.ProtocolVersion", compatibility)
        modifiers = (ROOT / "src/MphRead/Mods/Network/MatchDefinition.cs").read_text()
        self.assertIn("enum MatchModifierFlags : " + wire["modifierStorage"], modifiers)
        self.assertEqual(wire["modifierBytes"], 4)
        for name, bit in [("MomentumMovement", wire["momentumBit"]), ("Quake3Movement", wire["quake3Bit"])]:
            self.assertRegex(modifiers, name + r"\s*=\s*" + str(1 << bit))
        for name, value in wire["capabilityPacketIds"].items():
            self.assertRegex(protocol, name + r"\s*=\s*" + str(value) + r"\b")
        self.assertFalse(wire["capabilitiesEnableGameplay"])
        reader = (ROOT / "src/MphRead/Mods/Network/DemoFile.cs").read_text()
        live_declaration = re.search(
            r"\bif\s*\(\s*header\[4\]\s+is not\s+\(([^)]*)\)\s*\)", reader)
        self.assertIsNotNone(live_declaration, "live replay format declaration must remain explicit")
        declared = live_declaration.group(1)
        self.assertRegex(declared, r"^\s*\d+(?:\s+or\s+\d+)*\s*$")
        live_formats = [int(value) for value in re.findall(r"\d+", declared)]
        self.assertEqual(len(live_formats), len(set(live_formats)), "live replay formats must be unique")
        self.assertTrue(set(replay["readableFormats"]).issubset(live_formats))
        self.assertEqual(replay["defaultMetadataFormat"], 3)
        self.assertEqual(replay["timedShotCertificateFormat"], 6)
        self.assertTrue(replay["timedShotsAreOptIn"])

    def test_witnesses_and_all_phase_gates_are_inspectable(self):
        audit = self.data["integrationAudit"]
        paths = set()
        for witness in audit["frozenSourceFiles"]:
            self.assertNotIn(witness["path"], paths)
            paths.add(witness["path"])
            self.assertTrue((ROOT / witness["path"]).is_file())
            self.assertRegex(witness["sha256"], r"^[0-9a-f]{64}$")
            self.assertIn(witness["role"], ("contract", "fixture"))
        ledger = (MANIFEST.parent / audit["ledger"]).read_text(encoding="utf-8")
        for phase in range(11):
            self.assertIn(f"SH-{phase:02}", ledger)
        research_memo_path = MANIFEST.parent / audit["researchMemo"]
        research_memo_path.read_text(encoding="utf-8")
        self.assertTrue(research_memo_path.is_file())
        self.assertFalse(audit["measurements"]["releaseQualified"])
        self.assertEqual(audit["measurements"]["physicalAndroidTouchBaseline"], "not-captured")
        self.assertEqual(audit["measurements"]["sameHardwareFrameTimeBaseline"], "not-captured")

if __name__ == "__main__":
    unittest.main()
