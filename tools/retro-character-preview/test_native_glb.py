"""PORT-23: native-space GLB candidate preprocessing, no Blender or game media."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("build_native_glb.py")
sys.path.insert(0, str(SCRIPT.parent))
spec = importlib.util.spec_from_file_location("build_native_glb", SCRIPT)
candidate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(candidate)


def native_matrix(x, y, z):
    return [[1,0,0,x],[0,1,0,y],[0,0,1,z],[0,0,0,1]]


def fixture(root, ident="darksamus-echoes"):
    path = Path(root)
    (path / (ident + ".obj")).write_text(
        "o RetroLocalCharacter\n"
        "v 0 0 0\nv 1 0 0\nv 0 1 0\n"
        "vt 0 0\nvt 1 0\nvt 0 1\n"
        "g material_0\n"
        "f 1/1 2/2 3/3\n", encoding="utf-8")
    skeleton={"Id":ident,"Bones":[
        {"Id":10,"Parent":4294967295,"Position":[0,0,0]},
        {"Id":11,"Parent":10,"Position":[1,0,0]},
        {"Id":12,"Parent":10,"Position":[0,1,0]},
    ]}
    weights={"format":1,"vertexCount":3,
        "groups":[[{"boneId":10,"weight":1.0}],
                  [{"boneId":11,"weight":.5},{"boneId":12,"weight":.5}]],
        "vertexGroupIds":[0,1,1]}
    native={"format":1, "hunter":candidate.SOURCE_HUNTERS[ident], "models":[{
        "part":"biped", "lod":0,
        "nodes":[{"name":n, "bindWorldPosition":p,
                  "bindWorldMatrix":native_matrix(*p), "parentName":("Pelvis" if n!="Pelvis" else None)}
                 for n,p in [("Pelvis",[3,4,0]),("Left",[3,6,0]),("Right",[1,4,0])]],
        "matrixPalette":[{"slot":i,"nodeName":n} for i,n in enumerate(("Pelvis","Left","Right"))],
        "materials":[{"name":"body"}]}]}
    mapping={"format":1,"sourceCharacter":ident,"nativeHunter":candidate.SOURCE_HUNTERS[ident],
        "nativePart":"biped", "sourceToNative":{"axis":[[0,-1,0],[1,0,0],[0,0,1]],
                                              "scale":2,"offset":[3,4,0]},
        "boneMap":{"10":"Pelvis","11":"Left","12":"Right"},
        "anchors":[{"sourceBoneId":i} for i in (10,11,12)],
        "maxAnchorError":.001,"materialMap":{"material_0":"body"}}
    motions={"Format":1,"Clips":[]}
    for suffix, data in ((".skeleton.json",skeleton),(".weights.json",weights),
                          (".motions.json",motions)):
        (path / (ident + suffix)).write_text(json.dumps(data), encoding="utf-8")
    return native,mapping


class NativeGlbContract(unittest.TestCase):
    def test_all_source_identities_and_material_mappings(self):
        for ident in candidate.SOURCE_HUNTERS:
            with self.subTest(character=ident), tempfile.TemporaryDirectory() as root:
                native,mapping=fixture(root,ident)
                p=candidate.validate_native_candidate(Path(root), ident, native, mapping)
                self.assertEqual(len(p["vertices"]),3)
                self.assertEqual(len(p["faces"]),1)
                self.assertEqual(p["faces"][0],(0,1,2))
                self.assertEqual(p["vertices"][1],(3,6,0))
                self.assertEqual(p["source_groups"],["material_0"])
                self.assertEqual(p["materials"]["material_0"],"body")
                self.assertEqual(sorted(p["world_matrices"]),["Left","Pelvis","Right"])

    def test_axis_reflection_reverses_triangle_winding_and_uvs(self):
        with tempfile.TemporaryDirectory() as root:
            native,mapping=fixture(root)
            mapping["sourceToNative"]["axis"]=[[0,1,0],[1,0,0],[0,0,1]]
            native["models"][0]["nodes"][2]["bindWorldPosition"]=[5,4,0]
            native["models"][0]["nodes"][2]["bindWorldMatrix"]=native_matrix(5,4,0)
            p=candidate.validate_native_candidate(Path(root),"darksamus-echoes",native,mapping)
            self.assertEqual(p["faces"][0],(0,2,1))
            self.assertEqual(p["face_uvs"][0],(0,2,1))
            self.assertTrue(p["skin"]["coordinateHandednessFlip"])

    def test_strict_material_and_matrix_contracts(self):
        with tempfile.TemporaryDirectory() as root:
            original_native,original_map=fixture(root)
            mutations=[
                lambda n,m:m.pop("materialMap"),
                lambda n,m:m["materialMap"].update({"material_1":"body"}),
                lambda n,m:m["materialMap"].update({"material_0":"not_native"}),
                lambda n,m:n["models"][0]["materials"].clear(),
                lambda n,m:n["models"][0]["nodes"][0].pop("bindWorldMatrix"),
                lambda n,m:n["models"][0]["nodes"][0].update(bindWorldMatrix=native_matrix(100,4,0)),
                lambda n,m:n["models"][0]["nodes"][0]["bindWorldMatrix"][0].__setitem__(0,2),
                lambda n,m:n["models"][0]["nodes"][0]["bindWorldMatrix"][1].__setitem__(1,0),
                lambda n,m:n["models"][0]["nodes"][0]["bindWorldMatrix"][0].__setitem__(0,float("nan")),
                lambda n,m:n["models"][0]["nodes"][0]["bindWorldMatrix"][3].__setitem__(2,1),
                lambda n,m:n["models"][0]["nodes"][0]["bindWorldMatrix"][0].__setitem__(0,-1),
            ]
            for i, mutate in enumerate(mutations):
                n,m=copy.deepcopy((original_native,original_map))
                mutate(n,m)
                with self.subTest(i=i),self.assertRaises(ValueError):
                    candidate.validate_native_candidate(Path(root),"darksamus-echoes",n,m)

    def test_cli_validation_does_not_import_blender(self):
        with tempfile.TemporaryDirectory() as root:
            native,mapping=fixture(root)
            native_path=Path(root)/"native-reference.json"
            map_path=Path(root)/"mapping.json"
            native_path.write_text(json.dumps(native),encoding="utf-8")
            map_path.write_text(json.dumps(mapping),encoding="utf-8")
            process=subprocess.run([sys.executable,str(SCRIPT),
                "--source-root",root,"--character","darksamus-echoes",
                "--native-reference",str(native_path),
                "--mapping",str(map_path),"--validate"],
                text=True,capture_output=True,check=False,timeout=20)
            self.assertEqual(process.returncode,0,process.stdout+process.stderr)
            self.assertIn("Validated native-space candidate",process.stdout)

    def test_group_corruption_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            native,mapping=fixture(root)
            obj=Path(root)/"darksamus-echoes.obj"
            source=obj.read_text(encoding="utf-8")
            obj.write_text(source.replace("g material_0","g extra_mesh"),encoding="utf-8")
            with self.assertRaisesRegex(ValueError,"source.*material|OBJ material"):
                candidate.validate_native_candidate(Path(root),"darksamus-echoes",native,mapping)


if __name__=="__main__":
    unittest.main()
