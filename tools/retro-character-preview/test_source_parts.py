"""Synthetic appearance/part isolation; no Blender or commercial source files."""
import contextlib
import copy
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

import build_glb
import retarget_contract
import source_palette


APPEARANCES = ("darksamus-echoes", "darksamus-overcharged-echoes")


def fixture(character, part="body"):
    skeleton = {"Id": character, "Bones": [
        {"Id": 0, "Parent": 0xFFFFFFFF, "Position": [0, 0, 0]},
        {"Id": 1, "Parent": 0, "Position": [0, 1, 0]},
    ]}
    if part == "ball":
        skeleton["SourcePart"] = "ball"
    weights = {"format": 1, "vertexCount": 3,
               "groups": [[{"boneId": 1, "weight": 1}]], "vertexGroupIds": [0, 0, 0]}
    motions = {"Format": 1, "Clips": []}
    return skeleton, weights, motions


def write_fixture(root, character, part="body"):
    stem = build_glb.source_stem(character, part)
    skeleton, weights, motions = fixture(character, part)
    # Distinct geometry makes accidental body/ball file selection observable.
    height = 2 if part == "ball" else 1
    (root / (stem + ".obj")).write_text(
        f"v 0 0 0\nv 1 0 0\nv 0 {height} 0\nf 1 2 3\n", encoding="utf-8")
    for suffix, value in ((".skeleton.json", skeleton), (".weights.json", weights),
                          (".motions.json", motions)):
        (root / (stem + suffix)).write_text(json.dumps(value), encoding="utf-8")


class SourceParts(unittest.TestCase):
    def test_only_canonical_appearance_and_known_part_pairs_resolve(self):
        self.assertEqual(len(build_glb.CHARACTERS), 6)
        for character in build_glb.CHARACTERS:
            self.assertEqual(build_glb.source_stem(character), character)
            self.assertEqual(build_glb.source_stem(character, "body"), character)
        for character in APPEARANCES:
            self.assertEqual(build_glb.source_stem(character, "ball"), character + ".ball")
        for character, part in (("trooper-power", "ball"), (APPEARANCES[0], "unknown"),
                                (APPEARANCES[0], "../ball"), (APPEARANCES[0], "ball.obj"),
                                ("../darksamus-echoes", "body"),
                                ("darksamus-echoes.ball", "body")):
            with self.subTest(character=character, part=part), self.assertRaises(ValueError):
                build_glb.source_stem(character, part)

    def test_sidecars_require_canonical_parent_and_explicit_ball_tag(self):
        for character in APPEARANCES:
            body = fixture(character)
            self.assertEqual(build_glb.check_sidecars(character, *body, 3), (2, 1, 0))
            body[0]["SourcePart"] = "body"
            self.assertEqual(build_glb.check_sidecars(character, *body, 3, part="body"), (2, 1, 0))
            ball = fixture(character, "ball")
            self.assertEqual(build_glb.check_sidecars(character, *ball, 3, part="ball"), (2, 1, 0))
            for tag in (None, "body", "unknown"):
                wrong = copy.deepcopy(ball)
                if tag is None:
                    del wrong[0]["SourcePart"]
                else:
                    wrong[0]["SourcePart"] = tag
                with self.subTest(character=character, tag=tag), self.assertRaises(ValueError):
                    build_glb.check_sidecars(character, *wrong, 3, part="ball")
            with self.assertRaises(ValueError):
                build_glb.check_sidecars(character, *ball, 3)
            ball[0]["Id"] = next(x for x in APPEARANCES if x != character)
            with self.assertRaises(ValueError):
                build_glb.check_sidecars(character, *ball, 3, part="ball")

    def test_witnessed_echoes_root_sentinel_preserves_native_parent(self):
        for character in APPEARANCES:
            for part in ("body", "ball"):
                skeleton, weights, motions = fixture(character, part)
                skeleton.update(SourceGameId="G2ME01",
                                SourceCharacter="Dark_Samus_MorphBall" if part == "ball" else
                                "Dark_Samus" if character == APPEARANCES[0] else "Battle_3",
                                Refs={"Cinf": 0xC7DBC44A if part == "ball" else 0x4ABC8A50})
                skeleton["Bones"][0].update(Name="root", Parent=0x61)
                original = copy.deepcopy(skeleton)
                self.assertEqual(build_glb.check_sidecars(character, skeleton, weights, motions, 3, part=part),
                                 (2, 1, 0))
                plan = source_palette.build_plan(character,
                    [[0, 0, 0], [1, 0, 0], [0, 1, 0]], [], [[0, 1, 2]], [[-1, -1, -1]],
                    skeleton, weights, motions, part=part)
                self.assertEqual(skeleton, original)
                self.assertEqual(plan["sourceRig"]["Bones"][0]["Parent"], 0x61)
                changes = [lambda s: s.__setitem__("SourceGameId", "AMHE1"),
                           lambda s: s.pop("SourceGameId"),
                           lambda s: s.__setitem__("SourceCharacter", "unknown"),
                           lambda s: s["Refs"].__setitem__("Cinf", 0),
                           lambda s: s["Refs"].__setitem__("Cinf", 0x4ABC8A50 if part == "ball" else 0xC7DBC44A),
                           lambda s: s["Bones"][0].__setitem__("Parent", 0x62),
                           lambda s: s["Bones"][0].__setitem__("Name", "unknown"),
                           lambda s: s["Bones"][1].__setitem__("Parent", 0x61)]
                for change in changes:
                    wrong = copy.deepcopy(skeleton)
                    change(wrong)
                    with self.subTest(character=character, part=part, skeleton=wrong), self.assertRaises(ValueError):
                        build_glb.check_sidecars(character, wrong, weights, motions, 3, part=part)

    def test_loads_selected_part_and_never_substitutes_body(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for character in APPEARANCES:
                write_fixture(root, character)
                with self.assertRaisesRegex(ValueError, "lacks"):
                    build_glb.load_inputs(root, character, part="ball")
                write_fixture(root, character, "ball")
                body = build_glb.load_inputs(root, character)
                ball = build_glb.load_inputs(root, character, part="ball")
                self.assertEqual(body[0][2], [0, 1, 0])
                self.assertEqual(ball[0][2], [0, 2, 0])
                self.assertEqual(ball[4]["Id"], character)
                skeleton = root / (character + ".ball.skeleton.json")
                skeleton.write_text(json.dumps(body[4]), encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "source part"):
                    build_glb.load_inputs(root, character, part="ball")

    def test_palette_shapes_and_hashes_are_isolated_by_part(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for character in APPEARANCES:
                write_fixture(root, character)
                write_fixture(root, character, "ball")
                body = source_palette.write_private_plan(root, character, root / (character + ".body-plan.json"))
                explicit = source_palette.write_private_plan(
                    root, character, root / (character + ".explicit-body-plan.json"), part="body")
                self.assertEqual(body, explicit)
                self.assertEqual(body["kind"], "private-source-rig-palette-preflight")
                self.assertNotIn("sourcePart", body)
                ball = source_palette.write_private_plan(
                    root, character, root / (character + ".ball-plan.json"), part="ball")
                self.assertEqual(ball["kind"], "private-source-part-rig-palette-preflight")
                self.assertEqual(ball["sourcePart"], "ball")
                self.assertEqual(ball["character"], character)
                self.assertTrue(all(value is False for value in ball["acceptance"].values()))
                expected = {character + ".ball" + suffix for suffix in source_palette.SUFFIXES}
                self.assertEqual(set(ball["sourceSha256"]), expected)
                for name, digest in ball["sourceSha256"].items():
                    self.assertEqual(digest, hashlib.sha256((root / name).read_bytes()).hexdigest())
                self.assertTrue(set(body["sourceSha256"]).isdisjoint(ball["sourceSha256"]))
                (root / (character + ".obj")).write_text("changed body input\n", encoding="utf-8")
                repeat = source_palette.write_private_plan(
                    root, character, root / (character + ".ball-repeat.json"), part="ball")
                self.assertEqual(ball, repeat, "ball must neither read nor stamp body siblings")
                with mock.patch.object(source_palette, "_stamps", side_effect=[{"ball": "before"}, {"ball": "after"}]):
                    output = root / (character + ".changed-ball.json")
                    with self.assertRaisesRegex(ValueError, "changed"):
                        source_palette.write_private_plan(root, character, output, part="ball")
                    self.assertFalse(output.exists())

    def test_palette_rejects_cross_part_and_unknown_requests_before_output(self):
        inputs = ([[0, 0, 0], [1, 0, 0], [0, 1, 0]], [], [[0, 1, 2]], [[-1, -1, -1]])
        body = fixture(APPEARANCES[0])
        ball = fixture(APPEARANCES[0], "ball")
        for sidecars, part in ((body, "ball"), (ball, "body"), (ball, "unknown")):
            with self.assertRaises(ValueError):
                source_palette.build_plan(APPEARANCES[0], *inputs, *sidecars, part=part)
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with self.assertRaises(ValueError):
                source_palette.write_private_plan(root, "trooper-power", root / "refused.json", part="ball")
            self.assertFalse((root / "refused.json").exists())

    def test_cli_validation_palette_and_blender_forward_part(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for character in APPEARANCES:
                write_fixture(root, character, "ball")
                command = [sys.executable, str(Path(build_glb.__file__)), "--root", str(root),
                           "--character", character, "--part", "ball", "--validate"]
                result = subprocess.run(command, capture_output=True, text=True, timeout=10)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                output = root / (character + ".cli-ball-plan.json")
                result = subprocess.run(command + ["--palette-plan", str(output)],
                                        capture_output=True, text=True, timeout=10)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertEqual(json.loads(output.read_text())["sourcePart"], "ball")
                for animate in (False, True):
                    argv = ["build_glb.py", "--root", str(root), "--character", character, "--part", "ball"]
                    if animate:
                        argv += ["--animate-source"]
                    with mock.patch.object(sys, "argv", argv), mock.patch.object(build_glb, "build_blender") as blender, \
                            mock.patch("motion_bake.validate_motion_inventory", return_value=[{"Name": "synthetic"}]), \
                            contextlib.redirect_stdout(io.StringIO()):
                        build_glb.main()
                    suffix = ".source-motion.preview.glb" if animate else ".preview.glb"
                    self.assertEqual(blender.call_args.args[6], character)
                    self.assertEqual(blender.call_args.args[7], root / (character + ".ball" + suffix))
                    self.assertEqual(blender.call_args.kwargs["part"], "ball")
                    self.assertEqual(blender.call_args.kwargs["motion_clips"], [{"Name": "synthetic"}] if animate else None)

    def test_authoring_addition_does_not_expand_runtime_retarget_contract(self):
        overcharged = APPEARANCES[1]
        self.assertIn(overcharged, build_glb.CHARACTERS)
        self.assertNotIn(overcharged, retarget_contract.SOURCE_HUNTERS)
        self.assertNotIn(overcharged, retarget_contract.SOURCE_APPEARANCES)
        self.assertTrue(all(".ball" not in identifier for identifier in build_glb.CHARACTERS))


if __name__ == "__main__":
    unittest.main()
