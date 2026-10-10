#!/usr/bin/env python3
"""Compare native solo-versus-one-bot Ice Hive samples, without presuming a good release.

This explains *what* diverges, not which build is correct. No network access,
game assets, or image libraries are needed for the JSON contract.
"""
import argparse
import json
import math
from pathlib import Path


def _capture(result, index):
    for frame in result.get("captures", []):
        if frame.get("index") == index:
            diagnostic = frame.get("soloDiagnostic")
            if not isinstance(diagnostic, dict) or diagnostic.get("schema") != 1:
                raise ValueError(f"frame {index}: missing -diagnose-solo evidence")
            return diagnostic
    raise ValueError(f"missing capture {index}")


def _near_camera(a, b, tolerance=0.10):
    lhs, rhs = a["camera"], b["camera"]
    return len(lhs) == len(rhs) == 3 and math.dist(lhs, rhs) <= tolerance


def analyze(solo, bot):
    if solo.get("room") != bot.get("room") or solo.get("assemblyMvid") != bot.get("assemblyMvid"):
        raise ValueError("cannot compare different rooms or build identities")
    if solo.get("framebuffer") != bot.get("framebuffer") or solo.get("shadows") != bot.get("shadows"):
        raise ValueError("cannot compare different framebuffer sizes or shadow settings")
    if solo.get("cap") != bot.get("cap"):
        raise ValueError("cannot compare different pacing caps")

    solo_caps = solo.get("captures", [])
    bot_caps = bot.get("captures", [])
    if len(solo_caps) != 4 or len(bot_caps) != 4:
        raise ValueError("native capture campaign requires four matched diagnostic frames")

    differences = []
    for index in range(4):
        a, b = _capture(solo, index), _capture(bot, index)
        if a["room"] != b["room"] or a["mode"] != b["mode"]:
            raise ValueError(f"capture {index}: different mode or room")
        if a["playerCount"] != 1 or b["playerCount"] != 2 or a["activeBots"] != 0 or b["activeBots"] != 1:
            raise ValueError(f"capture {index}: expected 1 human vs 1 human + 1 active bot")
        if solo_caps[index]["simulationFrame"] != bot_caps[index]["simulationFrame"]:
            raise ValueError(f"capture {index}: simulation frame mismatch")

        reasons = []
        same_camera = _near_camera(a, b)
        if (a["nodeLayer"], a["entityLayer"]) != (b["nodeLayer"], b["entityLayer"]):
            reasons.append("DIFFERENT_MAP_ENTITY_OR_NODE_LAYER")
        if a["matchState"] != b["matchState"]:
            reasons.append("MATCH_PHASE_DIFFERS")
        if not same_camera:
            reasons.append("CAMERA_POSES_DIFFER; image/color/depth parity inconclusive")
        if a["cameraNode"] != b["cameraNode"]:
            reasons.append("CAMERA_NODE_REF_DIFFERS")
        if a["cullingFallbackAllParts"] != b["cullingFallbackAllParts"]:
            reasons.append("ONE_SCENARIO_FALLS_BACK_TO_DRAW_ALL_ROOM_PARTS")
        if same_camera and a["visiblePartCount"] != b["visiblePartCount"]:
            reasons.append("PORTAL_VISIBLE_PART_COUNT_DIFFERS")
        if a["lighting"] != b["lighting"]:
            reasons.append("LIGHT_SOURCE_VALUES_DIFFER")
        if same_camera:
            # A second player naturally adds their mesh; do not compare total
            # opaque packet count or claim pixel identity from an added model.
            room_ratio = max(1, a["roomOwnedOpaquePackets"])
            if abs(a["roomOwnedOpaquePackets"] - b["roomOwnedOpaquePackets"]) > room_ratio * 0.01:
                reasons.append("ROOM_OPAQUE_PACKET_COUNT_DIFFERS")
            if abs(a["depth"]["nearFraction"] - b["depth"]["nearFraction"]) > 0.15:
                reasons.append("DEPTH_COVERAGE_DIFFERS_AT_MATCHED_CAMERA")
        differences.append({"index": index, "cameraComparable": same_camera,
                            "solo": a, "oneBot": b, "diagnosticDifferences": reasons})
    return {"schema": 1, "comparison": "solo-human-versus-human-plus-one-bot",
            "passed": all(not x["diagnosticDifferences"] for x in differences),
            "note": "Additional bot geometry is expected; all observations require image review.",
            "captures": differences}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("solo", type=Path)
    parser.add_argument("onebot", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        data = analyze(json.loads(args.solo.read_text()),
                       json.loads(args.onebot.read_text()))
    except (KeyError, TypeError, ValueError, OSError, json.JSONDecodeError) as exc:
        data = {"schema": 1, "passed": False, "error": str(exc)}
        status = 1
    else:
        # Finding a difference is the purpose of this diagnostic, not a CLI
        # failure; malformed/missing evidence must still fail closed.
        status = 0
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(data, indent=2) + "\n")
    print(args.output, "CAPTURE_DIFFERENCES" if not data["passed"] else "NO_DIAGNOSTIC_DIFFERENCES")
    return status


if __name__ == "__main__":
    raise SystemExit(main())
