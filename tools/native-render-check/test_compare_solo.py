"""Content-free solo renderer comparison tests."""
import copy
import importlib.util
import json
from pathlib import Path
import unittest

# Directory name contains a hyphen, so load by filename rather than import.
spec = importlib.util.spec_from_file_location("solo_compare",
    Path(__file__).with_name("compare_solo.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def diagnostic(count, bots):
    return {
        "schema": 1, "room": "UNIT4_RM1", "mode": "Battle",
        "playerCount": count, "activeBots": bots, "nodeLayer": 34,
        "entityLayer": 1, "matchState": "InProgress",
        "camera": [0.0, 0.0, 0.0], "cameraNode": {
            "room": "UNIT4_RM1", "part": 0, "node": 1, "model": 0},
        "visiblePartCount": 2, "cullingFallbackAllParts": False,
        "roomOwnedOpaquePackets": 100, "opaquePackets": 100 + count,
        "lighting": {"light1Color": [1, 1, 1]},
        "depth": {"nearFraction": 0.7}
    }


def document(players, bots):
    return {"room": "UNIT4_RM1", "assemblyMvid": "same", "framebuffer": [1280, 720],
            "shadows": "Off", "cap": 60,
            "captures": [{"index": i, "simulationFrame": 240 + 300 * i,
                          "soloDiagnostic": diagnostic(players, bots)}
                         for i in range(4)]}


class Tests(unittest.TestCase):
    def test_identical_world(self):
        self.assertTrue(module.analyze(document(1, 0), document(2, 1))["passed"])

    def test_detect_missing_solo_room_culling(self):
        solo, bot = document(1, 0), document(2, 1)
        solo["captures"][0]["soloDiagnostic"]["cullingFallbackAllParts"] = True
        report = module.analyze(solo, bot)
        self.assertIn("ONE_SCENARIO_FALLS_BACK_TO_DRAW_ALL_ROOM_PARTS",
                      report["captures"][0]["diagnosticDifferences"])

    def test_detect_source_lighting_difference(self):
        solo, bot = document(1, 0), document(2, 1)
        solo["captures"][1]["soloDiagnostic"]["lighting"]["light1Color"] = [0, 0, 0]
        report = module.analyze(solo, bot)
        self.assertIn("LIGHT_SOURCE_VALUES_DIFFER",
                      report["captures"][1]["diagnosticDifferences"])

    def test_camera_mismatch_does_not_claim_depth_difference(self):
        solo, bot = document(1, 0), document(2, 1)
        bot["captures"][2]["soloDiagnostic"]["camera"] = [10, 0, 0]
        bot["captures"][2]["soloDiagnostic"]["depth"]["nearFraction"] = .02
        report = module.analyze(solo, bot)
        reasons = report["captures"][2]["diagnosticDifferences"]
        self.assertTrue(any("CAMERA_POSES_DIFFER" in x for x in reasons))
        self.assertNotIn("DEPTH_COVERAGE_DIFFERS_AT_MATCHED_CAMERA", reasons)

    def test_cross_version_refused(self):
        solo, bot = document(1, 0), document(2, 1)
        bot["assemblyMvid"] = "other"
        with self.assertRaisesRegex(ValueError, "different rooms or build identities"):
            module.analyze(solo, bot)

    def test_missing_capture_refused(self):
        solo, bot = document(1, 0), document(2, 1)
        bot["captures"].pop()
        with self.assertRaisesRegex(ValueError, "four matched"):
            module.analyze(solo, bot)


if __name__ == "__main__":
    unittest.main()
