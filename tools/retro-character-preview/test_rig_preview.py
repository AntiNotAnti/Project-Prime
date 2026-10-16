"""Asset-free source preview validation, no Blender runtime or Nintendo data."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("build_glb.py")


class RetroRigPreview(unittest.TestCase):
    def test_fixture_and_rejections(self):
        with tempfile.TemporaryDirectory(prefix="prime-local-rig-") as root:
            folder = Path(root)
            ident = "darksamus-echoes"
            (folder / (ident + ".obj")).write_text(
                "o RetroLocalCharacter\n"
                "v 0 0 0\nv 1 0 0\nv 0 1 0\n"
                "vt 0 0\nvt 1 0\nvt 0 1\n"
                "vn 0 0 1\n"
                "f 1/1/1 2/2/1 3/3/1\n", encoding="utf-8")
            skeleton = {
                "Id": ident, "Bones": [
                    {"Id": 10, "Parent": 4294967295, "Name": "Root", "Position": [0, 0, 0]},
                    {"Id": 11, "Parent": 10, "Name": "Arm", "Position": [0, 1, 0]},
                ],
            }
            weights = {
                "format": 1, "vertexCount": 3,
                "groups": [[{"boneId": 10, "weight": 1.0}],
                           [{"boneId": 10, "weight": 0.4}, {"boneId": 11, "weight": 0.6}]],
                "vertexGroupIds": [0, 0, 1],
            }
            motions = {"Format": 1, "Clips": [{"Name": "idle", "Samples": []}]}
            path = folder / (ident + ".skeleton.json")
            path.write_text(json.dumps(skeleton), encoding="utf-8")
            wpath = folder / (ident + ".weights.json")
            wpath.write_text(json.dumps(weights), encoding="utf-8")
            (folder / (ident + ".motions.json")).write_text(json.dumps(motions), encoding="utf-8")

            def check(expected):
                done = subprocess.run(
                    [sys.executable, str(SCRIPT), "--validate", "--root", str(folder),
                     "--character", ident],
                    text=True, capture_output=True, check=False, timeout=15
                )
                self.assertEqual(done.returncode == 0, expected, done.stdout + done.stderr)

            check(True)
            weights["vertexCount"] = 4
            wpath.write_text(json.dumps(weights), encoding="utf-8")
            check(False)
            weights["vertexCount"] = 3
            weights["groups"][1][1]["boneId"] = 300
            wpath.write_text(json.dumps(weights), encoding="utf-8")
            check(False)
            weights["groups"][1][1]["boneId"] = 11
            wpath.write_text(json.dumps(weights), encoding="utf-8")
            check(True)
            skeleton["Bones"][0]["Parent"] = 11
            path.write_text(json.dumps(skeleton), encoding="utf-8")
            check(False)
            skeleton["Bones"][0]["Parent"] = 4294967295
            path.write_text(json.dumps(skeleton), encoding="utf-8")
            check(True)
            weights["groups"][1] = [
                {"boneId": 10, "weight": 0.2},
                {"boneId": 11, "weight": 0.2},
                {"boneId": 10, "weight": 0.2},
                {"boneId": 11, "weight": 0.2},
                {"boneId": 10, "weight": 0.2},
            ]
            wpath.write_text(json.dumps(weights), encoding="utf-8")
            check(False)


if __name__ == "__main__":
    unittest.main()
