"""PORT-21 source-only animation validation and retarget math, no assets/Blender."""
import copy
import importlib.util
import math
from pathlib import Path
import sys
import unittest

PATH = Path(__file__).with_name("motion_bake.py")
spec = importlib.util.spec_from_file_location("motion_bake", PATH)
motion_bake = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = motion_bake
spec.loader.exec_module(motion_bake)


def fixture(character="darksamus-echoes"):
    skeleton = {"Id": character, "Bones": [
        {"Id": 10, "Parent": 4294967295, "Name": "Root", "Position": [0, 0, 0]},
        {"Id": 11, "Parent": 10, "Name": "Arm", "Position": [0, 1, 0]},
    ]}
    s = []
    for i in range(5):
        angle = math.pi * i / 8
        s.append({"TimeSeconds": i / 4, "Bones": [
            {"Bone": 10, "Rotation": [0, 0, 0, 1], "Translation": [10 + i, 0, 0]},
            {"Bone": 11, "Rotation": [0, 0, math.sin(angle), math.cos(angle)], "Translation": None},
        ]})
    motions = {"Format": 1, "Origin": "local-owned-retro-source", "Clips": [
        {"Name": "idle", "ResourceIds": [7], "Status": "sparse-source-pose-preview",
         "DurationSeconds": 1, "Samples": s},
        {"Name": "blend", "ResourceIds": [8, 9], "Status": "composite-needs-blend-graph",
         "DurationSeconds": None, "Samples": []},
    ]}
    return skeleton, motions


class MotionContract(unittest.TestCase):
    def test_darksamus_and_all_trooper_variants(self):
        for character in ("darksamus-echoes", "trooper-power", "trooper-ice",
                          "trooper-wave", "trooper-plasma"):
            skel, motion = fixture(character)
            clips = motion_bake.validate_motion_inventory(skel, motion)
            self.assertEqual(len(clips), 1)
            self.assertEqual((clips[0].name, clips[0].resource_id), ("idle", 7))
            self.assertEqual(len(clips[0].poses), 5)

    def test_bind_pose_zero_and_source_relative_translation(self):
        clips = motion_bake.validate_motion_inventory(*fixture())
        deltas = motion_bake.pose_deltas(clips[0])
        self.assertEqual(deltas[0][1][10], ((0, 0, 0, 1), (0, 0, 0)))
        self.assertEqual(deltas[-1][1][10][1], (4, 0, 0))
        self.assertAlmostEqual(abs(deltas[-1][1][11][0][2]), 1)
        self.assertAlmostEqual(abs(deltas[-1][1][11][0][3]), 0)

    def test_antipodal_quaternion_continuity(self):
        skeleton, inventory = fixture()
        inventory["Clips"][0]["Samples"][2]["Bones"][1]["Rotation"] = [
            -x for x in inventory["Clips"][0]["Samples"][2]["Bones"][1]["Rotation"]
        ]
        deltas = motion_bake.pose_deltas(motion_bake.validate_motion_inventory(skeleton, inventory)[0])
        self.assertGreater(sum(a*b for a,b in zip(deltas[1][1][11][0], deltas[2][1][11][0])), 0)
        self.assertGreater(sum(a*b for a,b in zip(deltas[2][1][11][0], deltas[3][1][11][0])), 0)

    def test_invalid_motion_cannot_pass(self):
        skeleton, valid = fixture()
        cases = []
        def edited(change):
            cloned = copy.deepcopy(valid)
            change(cloned["Clips"][0], cloned)
            cases.append(cloned)
        edited(lambda clip, inv: clip.update(DurationSeconds=0))
        edited(lambda clip, inv: clip.update(DurationSeconds=121))
        edited(lambda clip, inv: clip.update(ResourceIds=[5, 6]))
        edited(lambda clip, inv: clip.update(Samples=clip["Samples"][:-1]))
        edited(lambda clip, inv: clip["Samples"][1].update(TimeSeconds=0))
        edited(lambda clip, inv: clip["Samples"][-1].update(TimeSeconds=1.2))
        edited(lambda clip, inv: clip["Samples"][2]["Bones"][0].update(Bone=100))
        edited(lambda clip, inv: clip["Samples"][2]["Bones"].append(copy.deepcopy(clip["Samples"][2]["Bones"][0])))
        edited(lambda clip, inv: clip["Samples"][2]["Bones"][0].update(Rotation=[0, 0, 0, 0]))
        edited(lambda clip, inv: clip["Samples"][2]["Bones"][0].update(Rotation=[float("nan"), 0, 0, 1]))
        edited(lambda clip, inv: clip["Samples"][2]["Bones"][0].update(Translation=[float("inf"), 0, 0]))
        edited(lambda clip, inv: clip["Samples"][2]["Bones"][0].pop("Translation"))
        edited(lambda clip, inv: clip["Samples"][2]["Bones"][1].update(Translation=[0, 0, 1]))
        edited(lambda clip, inv: inv.update(Clips=inv["Clips"] * 2100))
        edited(lambda clip, inv: clip.update(Name="bad\u0000name"))
        for i, invalid in enumerate(cases):
            with self.subTest(i=i), self.assertRaises(ValueError):
                motion_bake.validate_motion_inventory(skeleton, invalid)

    def test_unsupported_and_ambiguous_clips(self):
        skeleton, inventory = fixture()
        with self.assertRaisesRegex(ValueError, "not a sampled primitive"):
            motion_bake.validate_motion_inventory(skeleton, inventory, "blend")
        with self.assertRaisesRegex(ValueError, "absent"):
            motion_bake.validate_motion_inventory(skeleton, inventory, "missing")
        inventory["Clips"].append(copy.deepcopy(inventory["Clips"][0]))
        with self.assertRaisesRegex(ValueError, "ambiguous"):
            motion_bake.validate_motion_inventory(skeleton, inventory, "idle")
        self.assertEqual(len(motion_bake.validate_motion_inventory(skeleton, inventory)), 2)

    def test_sampled_clip_budget(self):
        skeleton, inventory = fixture()
        inventory["Clips"] = [copy.deepcopy(inventory["Clips"][0]) for _ in range(13)]
        with self.assertRaisesRegex(ValueError, "twelve"):
            motion_bake.validate_motion_inventory(skeleton, inventory)


    def test_cli_validation_never_requires_blender_or_game_files(self):
        import json
        import subprocess
        import tempfile
        skeleton, inventory = fixture()
        with tempfile.TemporaryDirectory(prefix="prime-asset-free-motion-") as root:
            root = Path(root)
            ident = "darksamus-echoes"
            (root / (ident + ".obj")).write_text(
                "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n", encoding="utf-8")
            (root / (ident + ".skeleton.json")).write_text(json.dumps(skeleton), encoding="utf-8")
            (root / (ident + ".motions.json")).write_text(json.dumps(inventory), encoding="utf-8")
            weights = {"format": 1, "vertexCount": 3,
                       "groups": [[{"boneId": 10, "weight": 1.0}]],
                       "vertexGroupIds": [0, 0, 0]}
            (root / (ident + ".weights.json")).write_text(json.dumps(weights), encoding="utf-8")

            def check(extra, successful):
                result = subprocess.run([
                    sys.executable, str(PATH.with_name("build_glb.py")),
                    "--root", str(root), "--character", ident, "--validate", *extra
                ], capture_output=True, text=True, check=False, timeout=15)
                self.assertEqual(result.returncode == 0, successful, result.stdout + result.stderr)

            check([], True)  # Original static preview contract remains untouched.
            check(["--animate-source"], True)
            check(["--animate-source", "--clip", "idle"], True)
            check(["--animate-source", "--clip", "blend"], False)
            check(["--clip", "idle"], False)

    def test_quaternion_delta_uses_initial_orientation(self):
        skeleton, inventory = fixture()
        for i, sample in enumerate(inventory["Clips"][0]["Samples"]):
            angle = math.pi / 4 + math.pi * i / 8
            sample["Bones"][1]["Rotation"] = [0, 0, math.sin(angle), math.cos(angle)]
        clip = motion_bake.validate_motion_inventory(skeleton, inventory)[0]
        poses = motion_bake.pose_deltas(clip)
        self.assertAlmostEqual(poses[0][1][11][0][3], 1)
        self.assertAlmostEqual(abs(poses[-1][1][11][0][2]), 1)


if __name__ == "__main__":
    unittest.main()
