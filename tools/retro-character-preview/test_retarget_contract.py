"""PORT-22: asset-free explicit source/native retarget calibration contract."""
import copy
import importlib.util
from pathlib import Path
import sys
import unittest

path = Path(__file__).with_name("retarget_contract.py")
spec = importlib.util.spec_from_file_location("retarget_contract", path)
r = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = r
spec.loader.exec_module(r)

def fixture(char="darksamus-echoes"):
    hunter = r.SOURCE_HUNTERS[char]
    source = {"Id": char, "Bones": [
        {"Id":10, "Parent":4294967295, "Position":[0,0,0]},
        {"Id":11, "Parent":10, "Position":[1,0,0]},
        {"Id":12, "Parent":10, "Position":[0,1,0]},
        {"Id":13, "Parent":11, "Position":[2,0,0]},
    ]}
    names=["Pelvis", "Left", "Right"]
    native={"format":1, "hunter":hunter, "models":[{"part":"biped","lod":0,
        "nodes":[{"name":n,"bindWorldPosition":p} for n,p in zip(
            names, [[3,4,0],[3,6,0],[1,4,0]])],
        "matrixPalette":[{"slot":i,"nodeName":n} for i,n in enumerate(names)]}]}
    weights={"format":1,"vertexCount":3,"groups":[
        [{"boneId":10,"weight":.4},{"boneId":11,"weight":.6}],
        [{"boneId":11,"weight":.4},{"boneId":13,"weight":.6}],
        [{"boneId":12,"weight":1.0}]], "vertexGroupIds":[0,1,2]}
    mapping={"format":1,"sourceCharacter":char,"nativeHunter":hunter,
        "nativePart":"biped","sourceToNative":{
            "axis":[[0,-1,0],[1,0,0],[0,0,1]],
            "scale":2,"offset":[3,4,0]},
        "maxAnchorError":.001, "boneMap":{
            "10":"Pelvis","11":"Left","12":"Right","13":"Left"},
        "anchors":[{"sourceBoneId":i} for i in (10,11,12)]}
    return source,weights,native,mapping

class RetargetContractTests(unittest.TestCase):
    def test_supported_import_identities_and_weight_collapse(self):
        for char in r.SOURCE_HUNTERS:
            with self.subTest(char=char):
                result=r.validate_contract(*fixture(char))
                self.assertFalse(result["approvedForGameplay"])
                self.assertEqual(result["palette"],["Pelvis","Left","Right"])
                self.assertEqual(result["sourceGroupToNativeWeights"]["1"],
                                 [{"nativeNode":"Left","weight":1.0}])
                self.assertAlmostEqual(result["calibration"]["maximumError"],0)
                self.assertFalse(result["coordinateHandednessFlip"])

    def test_invalid_ids_palette_or_calibration_fail_closed(self):
        mutations=[
          lambda s,w,n,m:m.update(nativeHunter="Trace"),
          lambda s,w,n,m:m.update(nativePart="viewModel"),
          lambda s,w,n,m:m["boneMap"].pop("10"),
          lambda s,w,n,m:m["boneMap"].update({"99":"Left"}),
          lambda s,w,n,m:m["boneMap"].update({"13":"Missing"}),
          lambda s,w,n,m:m["sourceToNative"].update(scale=-2),
          lambda s,w,n,m:m["sourceToNative"].update(scale=float("nan")),
          lambda s,w,n,m:m["sourceToNative"].update(axis=[[1,0,0],[1,0,0],[0,0,1]]),
          lambda s,w,n,m:m["sourceToNative"].update(axis=[[1.0,0,0],[0,1,0],[0,0,1]]),
          lambda s,w,n,m:m.update(maxAnchorError=2),
          lambda s,w,n,m:m["sourceToNative"].update(offset=[3,5,0]),
          lambda s,w,n,m:m.update(anchors=m["anchors"][:2]),
          lambda s,w,n,m:m["anchors"].append({"sourceBoneId":10}),
          lambda s,w,n,m:n["models"][0]["nodes"][0].pop("bindWorldPosition"),
          lambda s,w,n,m:n.update(hunter="Trace"),
          lambda s,w,n,m:w["groups"][0][0].update(weight=-1),
          lambda s,w,n,m:w["groups"][0][0].update(weight=float("inf")),
          lambda s,w,n,m:w["groups"][0][0].update(weight=.1),
          lambda s,w,n,m:w["groups"][0][0].update(boneId=123),
          lambda s,w,n,m:w["vertexGroupIds"].append(40),
          lambda s,w,n,m:s["Bones"][0].update(Parent=11),
          lambda s,w,n,m:s["Bones"][1].update(Parent=777),
        ]
        for i, f in enumerate(mutations):
            values=copy.deepcopy(fixture())
            f(*values)
            with self.subTest(i=i),self.assertRaises(ValueError):
                r.validate_contract(*values)

    def test_noncollinear_anchor_requirement(self):
        s,w,n,m=fixture()
        s["Bones"][2]["Position"]=[2,0,0]
        n["models"][0]["nodes"][2]["bindWorldPosition"]=[3,8,0]
        with self.assertRaisesRegex(ValueError,"noncollinear"):
            r.validate_contract(s,w,n,m)

    def test_native_palette_overflow(self):
        s,w,n,m=fixture()
        for i in range(30):
            bone=100+i
            name=f"node_{bone}"
            s["Bones"].append({"Id":bone,"Parent":10,"Position":[0,0,float(i+1)]})
            n["models"][0]["nodes"].append({"name":name,"bindWorldPosition":[0,0,float(i+1)]})
            n["models"][0]["matrixPalette"].append({"slot":i+3,"nodeName":name})
            w["groups"].append([{"boneId":bone,"weight":1.0}])
            w["vertexGroupIds"].append(len(w["groups"])-1)
            w["vertexCount"]+=1
            m["boneMap"][str(bone)]=name
        with self.assertRaisesRegex(ValueError,"32-joint"):
            r.validate_contract(s,w,n,m)

    def test_reflection_requires_winding_repair(self):
        s,w,n,m=fixture()
        m["sourceToNative"]["axis"]=[[0,1,0],[1,0,0],[0,0,1]]
        n["models"][0]["nodes"][1]["bindWorldPosition"]=[3,6,0]
        n["models"][0]["nodes"][2]["bindWorldPosition"]=[5,4,0]
        self.assertTrue(r.validate_contract(s,w,n,m)["coordinateHandednessFlip"])

if __name__=="__main__":
    unittest.main()
