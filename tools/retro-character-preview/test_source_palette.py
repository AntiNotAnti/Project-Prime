"""Asset-free own-rig palette equivalence; no Blender, GL or source assets."""
import copy
import hashlib
import json
import math
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

import source_palette as palette

CHARACTER = "darksamus-echoes"


def fixture():
    bones = [{"Id": 10+i, "Parent": 0xFFFFFFFF if i == 0 else 9+i,
              "Name": f"synthetic_{i}", "Position": [i*.1, i*.2, i*.3]} for i in range(39)]
    vertices = [[i*.13, (i % 3)*.4, (i % 7)*.1] for i in range(39)]
    uvs = [[i/40, (i % 3)/3] for i in range(40)]
    faces = [[i, i+1, i+2] for i in range(0, 39, 3)]
    face_uvs = copy.deepcopy(faces)
    # Same position with a different UV must be duplicated, including across batches.
    faces.append([0, 37, 38]); face_uvs.append([39, 37, 38])
    weights = {"format": 1, "vertexCount": 39,
        "groups": [[{"boneId": 10+i, "weight": .2},
                    {"boneId": 10+(i+1) % 39, "weight": .6}] for i in range(39)],
        "vertexGroupIds": list(range(39))}
    skeleton = {"Id": CHARACTER, "Bones": bones}
    motions = {"Format": 1, "Clips": [{"Name": "synthetic-sparse", "Status": "sparse-source-pose-preview", "Samples": []},
        {"Name": "unresolved-blend", "Status": "composite-needs-blend-graph", "Samples": []}]}
    return vertices, uvs, faces, face_uvs, skeleton, weights, motions


def multiply(a, b):
    return [[sum(a[row][k]*b[k][col] for k in range(4)) for col in range(4)] for row in range(4)]


def transform(matrix, point):
    return [sum(matrix[row][col]*(point+[1])[col] for col in range(4)) for row in range(3)]


def hierarchical_pose(skeleton, phase):
    """Independent CPU oracle: evaluate all parents before gather, even if unused by a draw."""
    result, rests = {}, {b["Id"]: b["Position"] for b in skeleton["Bones"]}
    by_id = {b["Id"]: b for b in skeleton["Bones"]}
    def evaluate(ident):
        if ident in result: return result[ident]
        bone = by_id[ident]
        parent = bone["Parent"]
        angle = math.sin(bone["Id"]*.3+phase)*.3
        c, s = math.cos(angle), math.sin(angle)
        offset = [x-y for x,y in zip(bone["Position"], rests.get(parent, [0,0,0]))]
        local = [[c,-s,0,offset[0]], [s,c,0,offset[1]], [0,0,1,offset[2]+phase*.01], [0,0,0,1]]
        result[ident] = multiply(evaluate(parent), local) if parent in by_id else local
        return result[ident]
    for ident in by_id: evaluate(ident)
    # Known synthetic translation-only inverse bind. Gather happens after the
    # complete animated parent hierarchy, never by rebuilding a truncated rig.
    return {bone["Id"]: multiply(result[bone["Id"]],
        [[1,0,0,-bone["Position"][0]], [0,1,0,-bone["Position"][1]],
         [0,0,1,-bone["Position"][2]], [0,0,0,1]]) for bone in skeleton["Bones"]}


class SourcePalette(unittest.TestCase):
    def test_39_bones_partition_without_pruning_hierarchy_or_motion(self):
        inputs = fixture(); original = copy.deepcopy(inputs)
        plan = palette.build_plan(CHARACTER, *inputs)
        self.assertGreater(len(plan["batches"]), 1)
        self.assertEqual(len(plan["sourceRig"]["Bones"]), 39)
        self.assertEqual(plan["sourceRig"], inputs[4])
        self.assertEqual(plan["sourceMotionInventory"], inputs[6])
        self.assertEqual(inputs, original, "planner must not mutate source rig, weights or clips")
        self.assertEqual([face for b in plan["batches"] for face in b["sourceFaces"]], list(range(len(inputs[2]))))
        self.assertTrue(all(1 <= len(b["palette"]) <= 32 for b in plan["batches"]))
        self.assertEqual(set(bone for b in plan["batches"] for bone in b["palette"]), set(range(10,49)))
        self.assertTrue(all(value is False for value in plan["acceptance"].values()))

    def test_all_supported_private_variants_without_catalog_activation(self):
        for character in sorted(palette.CHARACTERS):
            inputs = list(fixture()); inputs[4]["Id"] = character
            plan = palette.build_plan(character, *inputs)
            self.assertEqual(plan["character"], character)
            self.assertTrue(all(value is False for value in plan["acceptance"].values()))
        with self.assertRaises(ValueError): palette.build_plan("darksamus-corruption", *fixture())

    def test_local_indices_and_gathered_matrices_match_global_cpu_across_poses(self):
        inputs = fixture(); vertices, uvs, faces, face_uvs, skeleton, weights, _ = inputs
        skeleton["Bones"].reverse()  # Hierarchy evaluation cannot assume source file order.
        plan = palette.build_plan(CHARACTER, *inputs)
        for phase in (0, .125, .5, 1, 2, 5):
            global_matrices = hierarchical_pose(skeleton, phase)
            for batch in plan["batches"]:
                local_matrices = [global_matrices[bone] for bone in batch["palette"]]
                for at, face in enumerate(batch["sourceFaces"]):
                    for corner in range(3):
                        output = batch["vertices"][batch["indices"][at*3+corner]]
                        source = faces[face][corner]; uv = face_uvs[face][corner]
                        self.assertEqual((output["sourceVertex"], output["sourceUv"]), (source, uv))
                        self.assertEqual(output["position"], vertices[source]); self.assertEqual(output["uv"], uvs[uv])
                        influences = weights["groups"][weights["vertexGroupIds"][source]]
                        self.assertEqual([batch["palette"][j] for j in output["joints"]], [i["boneId"] for i in influences])
                        self.assertEqual(output["weights"], [i["weight"] for i in influences])
                        expected = [sum(i["weight"]*transform(global_matrices[i["boneId"]], vertices[source])[axis] for i in influences) for axis in range(3)]
                        actual = [sum(w*transform(local_matrices[j], output["position"])[axis] for j,w in zip(output["joints"],output["weights"])) for axis in range(3)]
                        for a,b in zip(actual, expected): self.assertAlmostEqual(a,b,places=12)

    def test_uv_seams_and_winding_survive_within_same_batch(self):
        inputs = list(fixture()); inputs[2] = [[0,1,2],[0,2,1]]; inputs[3] = [[0,1,2],[39,2,1]]
        batch = palette.build_plan(CHARACTER, *inputs)["batches"][0]
        self.assertEqual(len(batch["vertices"]), 4)
        self.assertNotEqual(batch["indices"][0], batch["indices"][3])
        self.assertEqual([batch["vertices"][i]["sourceVertex"] for i in batch["indices"]], [0,1,2,0,2,1])

    def test_determinism_and_full_hierarchy_keeps_unweighted_helper_parent(self):
        inputs = list(fixture())
        inputs[5]["groups"] = [[influence for influence in group if influence["boneId"] != 10] for group in inputs[5]["groups"]]
        first = palette.build_plan(CHARACTER, *inputs); second = palette.build_plan(CHARACTER, *inputs)
        self.assertEqual(first, second)
        self.assertEqual(first["sourceRig"]["Bones"][1]["Parent"], 10)
        self.assertNotIn(10, {bone for batch in first["batches"] for bone in batch["palette"]})
        self.assertEqual(len(first["sourceRig"]["Bones"]), 39, "unweighted root remains required for hierarchy evaluation")

    def test_invalid_indices_weights_and_palette_fail_closed(self):
        edits = [lambda x: x[2][0].__setitem__(0, -1), lambda x: x[3][0].__setitem__(0, 100),
            lambda x: x[2].__setitem__(0, [0,0,1]), lambda x: x[4]["Bones"][0].__setitem__("Id", True),
            lambda x: x[5]["groups"][0].append({"boneId":10,"weight":.1}),
            lambda x: x[5]["groups"][0][0].__setitem__("weight",float("nan")),
            lambda x: x[5]["groups"][0][0].__setitem__("weight",True),
            lambda x: x[5]["vertexGroupIds"].__setitem__(0,True), lambda x: x[4]["Bones"][0].__setitem__("Parent",11)]
        for edit in edits:
            inputs = list(fixture());edit(inputs)
            with self.assertRaises(ValueError): palette.build_plan(CHARACTER,*inputs)
        with self.assertRaisesRegex(ValueError,"cannot be pruned"): palette.build_plan(CHARACTER,*fixture(),palette_limit=1)
        for limit in (0,33,True):
            with self.assertRaises(ValueError): palette.build_plan(CHARACTER,*fixture(),palette_limit=limit)

    def test_partition_and_duplicate_vertex_budgets(self):
        inputs = list(fixture())
        with mock.patch.object(palette,"MAX_DRAW_VERTICES",1):
            with self.assertRaisesRegex(ValueError,"UV inventory"): palette.build_plan(CHARACTER,*inputs)
        with mock.patch.object(palette,"MAX_BATCHES",1):
            with self.assertRaisesRegex(ValueError,"partition count"): palette.build_plan(CHARACTER,*fixture())
        inputs = list(fixture()); inputs[1] = []; inputs[3] = [[-1,-1,-1] for _ in inputs[2]]
        with mock.patch.object(palette,"MAX_DRAW_VERTICES",3):
            with self.assertRaisesRegex(ValueError,"draw vertex count"): palette.build_plan(CHARACTER,*inputs)

    def test_private_cli_stamps_no_overwrite_escape_or_fake_runtime_receipt(self):
        inputs = fixture()
        with tempfile.TemporaryDirectory(prefix="prime-synthetic-source-palette-") as temporary:
            root = Path(temporary); vertices,uvs,faces,face_uvs,skeleton,weights,motions=inputs
            lines=["v "+" ".join(map(str,v)) for v in vertices]+["vt "+" ".join(map(str,v)) for v in uvs]
            lines += ["f "+" ".join(f"{v+1}/{uv+1}" for v,uv in zip(face,tex)) for face,tex in zip(faces,face_uvs)]
            (root/(CHARACTER+".obj")).write_text("\n".join(lines)+"\n")
            for suffix,value in ((".skeleton.json",skeleton),(".weights.json",weights),(".motions.json",motions)):
                (root/(CHARACTER+suffix)).write_text(json.dumps(value))
            output=root/"private-plan.json"
            result=subprocess.run([sys.executable,str(Path(__file__).with_name("build_glb.py")),"--validate","--root",str(root),"--character",CHARACTER,"--palette-plan",str(output)],capture_output=True,text=True,timeout=10)
            self.assertEqual(result.returncode,0,result.stdout+result.stderr)
            plan=json.loads(output.read_text());self.assertEqual(len(plan["batches"]),2)
            for name,digest in plan["sourceSha256"].items():self.assertEqual(digest,hashlib.sha256((root/name).read_bytes()).hexdigest())
            with self.assertRaises(ValueError):palette.write_private_plan(root,CHARACTER,output)
            with self.assertRaises(ValueError):palette.write_private_plan(root,CHARACTER,root.parent/"palette-escape-test.json")
            with mock.patch.object(palette,"_stamps",side_effect=[{"source":"before"},{"source":"after"}]):
                with self.assertRaisesRegex(ValueError,"changed"):palette.write_private_plan(root,CHARACTER,root/"changed.json")
            self.assertFalse((root/"changed.json").exists())
            with mock.patch.object(palette,"MAX_PLAN_BYTES",4):
                with self.assertRaisesRegex(ValueError,"output budget"): palette.write_private_plan(root,CHARACTER,root/"oversized.json")
            self.assertFalse((root/"oversized.json").exists())


if __name__ == "__main__":unittest.main()
