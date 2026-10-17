"""PORT-24 content-free local candidate GLB/manifest/receipt gates."""
import copy
import importlib.util
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

FOLDER = Path(__file__).parent
sys.path.insert(0, str(FOLDER))
from test_native_glb import fixture

SPEC = importlib.util.spec_from_file_location("local_visual_pack", FOLDER/"local_visual_pack.py")
pack = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(pack)


def synthetic_glb(path, names=("Pelvis","Left","Right"), material="body"):
    nodes=[{"name":n} for n in names]
    nodes.append({"name":"Mesh","mesh":0,"skin":0})
    data={"asset":{"version":"2.0"},
          "buffers":[{"byteLength":4}],
          "nodes":nodes,
          "skins":[{"joints":list(range(len(names))),"inverseBindMatrices":0}],
          "meshes":[{"primitives":[{"attributes":{
              "POSITION":1,"JOINTS_0":2,"WEIGHTS_0":3},"indices":4,"material":0,"mode":4}]}],
          "accessors":[{"componentType":5126,"count":len(names),"type":"MAT4"}],
          "materials":[{"name":material}]}
    blob=json.dumps(data,separators=(",",":")).encode("utf-8")
    blob+=b" " *((-len(blob)) % 4)
    binary=b"\0"*4
    path.write_bytes(struct.pack("<4sII",b"glTF",2,28+len(blob)+len(binary))+
        struct.pack("<I4s",len(blob),b"JSON")+blob+
        struct.pack("<I4s",len(binary),b"BIN\0")+binary)
    return data


class LocalPreviewPack(unittest.TestCase):
    def test_stages_opt_in_biped_only_for_dark_samus_and_troopers(self):
        for ident in pack.SOURCE_HUNTERS:
            with self.subTest(source=ident),tempfile.TemporaryDirectory() as temp:
                root=Path(temp)
                native,mapping=fixture(root,ident)
                native_file=root/"native-reference.json"
                mapping_file=root/"mapping.json"
                glb=root/"candidate.glb"
                native_file.write_text(json.dumps(native),encoding="utf-8")
                mapping_file.write_text(json.dumps(mapping),encoding="utf-8")
                synthetic_glb(glb)
                dest=root/"staged-preview"
                receipt=pack.prepare_test_pack(root,ident,native_file,mapping_file,glb,dest)
                self.assertTrue((dest/"characters.json").is_file())
                manifest=json.loads((dest/"characters.json").read_text(encoding="utf-8"))
                self.assertEqual(len(manifest["models"]),1)
                self.assertEqual(manifest["models"][0]["part"],"biped")
                self.assertEqual(manifest["models"][0]["hunter"],pack.SOURCE_HUNTERS[ident])
                self.assertEqual(manifest["models"][0]["skinning"],"weighted4")
                self.assertEqual(set(manifest["models"][0]["boneMap"]),
                    {"Pelvis","Left","Right"})
                self.assertTrue(receipt["visualOnly"])
                self.assertFalse(receipt["approvedForGameplay"])
                self.assertFalse(receipt["multiplayerEnabled"])
                self.assertFalse(receipt["firstPersonVerified"])
                self.assertEqual((dest/pack.SOURCE_HUNTERS[ident].lower()/"biped.glb").read_bytes(),glb.read_bytes())
                with self.assertRaises(ValueError):
                    pack.prepare_test_pack(root,ident,native_file,mapping_file,glb,dest)

    def test_rejects_invalid_model_and_transform_and_material(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)
            glb=root/"test.glb"
            good=synthetic_glb(glb)
            pack.inspect_candidate_glb(glb,["Pelvis","Left","Right"],["body"])
            with self.assertRaisesRegex(ValueError,"palette"):
                pack.inspect_candidate_glb(glb,["Pelvis","Left"],["body"])
            with self.assertRaisesRegex(ValueError,"native"):
                pack.inspect_candidate_glb(glb,["Pelvis","Left","Right"],["bad"])
            bad=copy.deepcopy(good)
            bad["nodes"][3]["translation"]=[1,0,0]
            self.rewrite(glb,bad)
            with self.assertRaisesRegex(ValueError,"nonidentity"):
                pack.inspect_candidate_glb(glb,["Pelvis","Left","Right"],["body"])
            bad=copy.deepcopy(good)
            bad["skins"][0]["inverseBindMatrices"]=300
            self.rewrite(glb,bad)
            with self.assertRaisesRegex(ValueError,"inverse-bind"):
                pack.inspect_candidate_glb(glb,["Pelvis","Left","Right"],["body"])
            bad=copy.deepcopy(good)
            bad["meshes"][0]["primitives"][0]["attributes"].pop("WEIGHTS_0")
            self.rewrite(glb,bad)
            with self.assertRaisesRegex(ValueError,"vertex attributes"):
                pack.inspect_candidate_glb(glb,["Pelvis","Left","Right"],["body"])
            glb.write_bytes(b"not a glb")
            with self.assertRaises(ValueError):
                pack.inspect_candidate_glb(glb,["Pelvis","Left","Right"],["body"])

    @staticmethod
    def rewrite(path, json_data):
        text=json.dumps(json_data,separators=(",",":")).encode("utf-8")
        text+=b" "*((-len(text))%4)
        path.write_bytes(struct.pack("<4sII",b"glTF",2,28+len(text)+4)+
            struct.pack("<I4s",len(text),b"JSON")+text+
            struct.pack("<I4s",4,b"BIN\0")+b"\0"*4)

    def test_refuses_live_default_directory(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)
            native,mapping=fixture(root)
            n=root/"native.json"
            m=root/"mapping.json"
            g=root/"preview.glb"
            n.write_text(json.dumps(native),encoding="utf-8")
            m.write_text(json.dumps(mapping),encoding="utf-8")
            synthetic_glb(g)
            live=root/"character-models"/"default"
            live.parent.mkdir()
            with self.assertRaisesRegex(ValueError,"live default"):
                pack.prepare_test_pack(root,"darksamus-echoes",n,m,g,live)


if __name__=="__main__":
    unittest.main()
