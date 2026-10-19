"""PORT-25 asset-free source-character acceptance-matrix regression fixtures."""
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest

FOLDER=Path(__file__).parent
sys.path.insert(0,str(FOLDER))
from test_local_visual_pack import synthetic_glb
from test_native_glb import fixture
import local_visual_pack

SPEC=importlib.util.spec_from_file_location("acceptance_matrix",FOLDER/"acceptance_matrix.py")
accept=importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(accept)


def stage(root, ident="darksamus-echoes"):
    native,mapping=fixture(root,ident)
    n=root/(ident+".native.json")
    m=root/(ident+".mapping.json")
    glb=root/(ident+".glb")
    n.write_text(json.dumps(native),encoding="utf-8")
    m.write_text(json.dumps(mapping),encoding="utf-8")
    synthetic_glb(glb)
    target=root/(ident+"-test-pack")
    local_visual_pack.prepare_test_pack(root,ident,n,m,glb,target)
    return target


class AcceptanceMatrixTests(unittest.TestCase):
    def test_all_five_visual_stages_stay_nonplayable(self):
        for char in accept.SOURCE_HUNTERS:
            with self.subTest(char=char),tempfile.TemporaryDirectory() as scratch:
                root=Path(scratch)
                path=stage(root,char)
                report=accept.evaluate([path])
                self.assertEqual(report["assetPreflightPassed"],1)
                self.assertEqual(report["runtimeBipedCandidatesPassed"],0)
                self.assertEqual(report["results"][0]["fallbackHunter"],
                                 accept.SOURCE_HUNTERS[char])
                self.assertEqual(report["results"][0]["bindingKind"],"private-catalog-appearance")
                self.assertEqual((report["results"][0]["characterId"],report["results"][0]["skinSource"]),
                                 accept.SOURCE_APPEARANCES[char])
                self.assertFalse(report["fullPortReady"])
                self.assertFalse(report["onlineMultiplayerEnabled"])
                self.assertGreaterEqual(len(report["manualGatesRequired"]),8)

    def test_refuses_tampered_staging_or_false_claims(self):
        with tempfile.TemporaryDirectory() as scratch:
            root=Path(scratch)
            path=stage(root)
            entry=path/"validation-receipt.json"
            original=entry.read_text(encoding="utf-8")
            bad=json.loads(original)
            bad["approvedForGameplay"]=True
            entry.write_text(json.dumps(bad),encoding="utf-8")
            self.assertEqual(accept.evaluate([path])["assetPreflightPassed"],0)
            entry.write_text(original,encoding="utf-8")
            glb=path/"samus"/"biped.glb"
            with glb.open("ab") as handle:
                handle.write(b"tamper")
            self.assertEqual(accept.evaluate([path])["assetPreflightPassed"],0)

    def test_runtime_validator_fail_closed(self):
        if os.name=="nt":
            self.skipTest("synthetic executable fixture is POSIX-only")
        with tempfile.TemporaryDirectory() as scratch:
            root=Path(scratch)
            path=stage(root)
            good=root/"ok-validator"
            good.write_text("#!/usr/bin/env python3\nimport sys\nsys.exit(0 if '-charactermodelvalidate' in sys.argv else 5)\n",
                            encoding="utf-8")
            good.chmod(0o755)
            passed=accept.evaluate([path],good)
            self.assertEqual(passed["assetPreflightPassed"],1)
            self.assertEqual(passed["runtimeBipedCandidatesPassed"],1)
            self.assertEqual(passed["results"][0]["runtimeValidation"],"pass")
            self.assertFalse(passed["fullPortReady"])
            bad=root/"fail-validator"
            bad.write_text("#!/usr/bin/env python3\nimport sys\nsys.exit(12)\n",encoding="utf-8")
            bad.chmod(0o755)
            failed=accept.evaluate([path],bad)
            self.assertEqual(failed["runtimeBipedCandidatesPassed"],0)
            self.assertEqual(failed["results"][0]["runtimeExitCode"],12)

    def test_strict_matrix_requires_all_five_private_variants(self):
        with tempfile.TemporaryDirectory() as scratch:
            root=Path(scratch)
            partial=stage(root)
            partial_report=accept.evaluate([partial],require_all=True)
            self.assertTrue(partial_report["strictCompleteSetRequested"])
            self.assertFalse(partial_report["allFiveVariantsProvidedAndValid"])
            self.assertEqual(len(partial_report["missingVariants"]),4)
            # The partial-set fixture above has already staged Dark Samus.
            # Reuse that exact, verified pack; staging it twice is correctly
            # forbidden by the private output-path safety contract.
            candidates=[partial]
            for ident in accept.SOURCE_HUNTERS:
                if ident != "darksamus-echoes":
                    candidates.append(stage(root,ident))
            complete=accept.evaluate(candidates,require_all=True)
            self.assertTrue(complete["allFiveVariantsProvidedAndValid"])
            self.assertEqual(complete["missingVariants"],[])
            self.assertFalse(complete["fullPortReady"])
            self.assertFalse(complete["onlineMultiplayerEnabled"])
            candidates[0].joinpath("validation-receipt.json").write_text("{}",encoding="utf-8")
            tampered=accept.evaluate(candidates,require_all=True)
            self.assertFalse(tampered["allFiveVariantsProvidedAndValid"])
            self.assertEqual(len(tampered["missingVariants"]),1)

    def test_rejects_duplicate_source_variant(self):
        with tempfile.TemporaryDirectory() as scratch:
            root=Path(scratch)
            path=stage(root)
            report=accept.evaluate([path,path])
            self.assertEqual(report["assetPreflightPassed"],1)
            self.assertEqual(report["inputPacks"],2)
            self.assertIn("Duplicate source identity",report["results"][1]["error"])

    def test_identity_and_parent_and_provenance_must_match_together(self):
        mutations=(
            ("characterId","trooper"),("skinSource","MP3DarkSamus"),
            ("skinSource","MP1PowerTrooper"),("hunter","Weavel"),
            ("provenance","../validation-receipt.json"),
            ("provenanceSha256","0"*64),("mobileModel","samus/biped.glb"),
        )
        with tempfile.TemporaryDirectory() as scratch:
            path=stage(Path(scratch))
            manifest_path=path/"characters.json"
            original=json.loads(manifest_path.read_text(encoding="utf-8"))
            for key,value in mutations:
                with self.subTest(field=key,value=value):
                    manifest=json.loads(json.dumps(original))
                    manifest["models"][0][key]=value
                    manifest_path.write_text(json.dumps(manifest),encoding="utf-8")
                    self.assertEqual(accept.evaluate([path])["assetPreflightPassed"],0)
            for key in ("characterId","skinSource","provenance","provenanceSha256"):
                with self.subTest(missing=key):
                    manifest=json.loads(json.dumps(original))
                    del manifest["models"][0][key]
                    manifest_path.write_text(json.dumps(manifest),encoding="utf-8")
                    self.assertEqual(accept.evaluate([path])["assetPreflightPassed"],0)

    def test_rehashed_receipt_cannot_change_identity_or_claim_acceptance(self):
        with tempfile.TemporaryDirectory() as scratch:
            path=stage(Path(scratch))
            receipt_path=path/"validation-receipt.json"
            manifest_path=path/"characters.json"
            receipt=json.loads(receipt_path.read_text(encoding="utf-8"))
            manifest=json.loads(manifest_path.read_text(encoding="utf-8"))
            mutations=[("characterId","trooper"),("skinSource","MP1IceTrooper"),
                       ("nativeHunter","Weavel"),("sourceCharacter","trooper-power"),
                       ("presentationPart","viewModel"),
                       ("visualOnly",1),("format",True),("sourceSkeletonSha256","0"*64)]
            mutations.extend((key,True) for key in (
                "approvedForGameplay","multiplayerEnabled","playerSelectionEnabled",
                "firstPersonVerified","alternateFormVerified","sourceMotionRetargetVerified",
                "devicePerformanceVerified"))
            for key,value in mutations:
                with self.subTest(field=key):
                    changed=dict(receipt)
                    changed[key]=value
                    receipt_path.write_text(json.dumps(changed),encoding="utf-8")
                    manifest["models"][0]["provenanceSha256"]=local_visual_pack.sha256_file(receipt_path)
                    manifest_path.write_text(json.dumps(manifest),encoding="utf-8")
                    self.assertEqual(accept.evaluate([path])["assetPreflightPassed"],0)

    def test_old_hunter_only_pack_keeps_legacy_binding(self):
        with tempfile.TemporaryDirectory() as scratch:
            path=stage(Path(scratch))
            manifest_path=path/"characters.json"
            manifest=json.loads(manifest_path.read_text(encoding="utf-8"))
            for key in ("characterId","skinSource","provenance","provenanceSha256"):
                del manifest["models"][0][key]
            manifest_path.write_text(json.dumps(manifest),encoding="utf-8")
            report=accept.evaluate([path])
            self.assertEqual(report["assetPreflightPassed"],1)
            self.assertEqual(report["results"][0]["bindingKind"],"legacy-native-hunter")
            self.assertIsNone(report["results"][0]["characterId"])
            self.assertFalse(report["fullPortReady"])

    def test_distinct_trooper_looks_share_parent_without_aliasing_identity(self):
        with tempfile.TemporaryDirectory() as scratch:
            root=Path(scratch)
            paths=[stage(root,ident) for ident in ("trooper-power","trooper-ice")]
            rows=accept.evaluate(paths)["results"]
            self.assertEqual([row["assetPreflight"] for row in rows],["pass","pass"])
            self.assertEqual([row["fallbackHunter"] for row in rows],["Weavel","Weavel"])
            self.assertEqual([row["skinSource"] for row in rows],["MP1PowerTrooper","MP1IceTrooper"])

    def test_duplicate_json_contract_properties_rejected(self):
        with tempfile.TemporaryDirectory() as scratch:
            path=stage(Path(scratch))
            manifest_path=path/"characters.json"
            manifest_path.write_text('{"format":1,"format":1}',encoding="utf-8")
            self.assertEqual(accept.evaluate([path])["assetPreflightPassed"],0)


if __name__=="__main__":
    unittest.main()
