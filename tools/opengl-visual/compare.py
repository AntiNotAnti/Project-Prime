#!/usr/bin/env python3
"""Strict content-free OpenGL raw RGBA fixture comparator.

Input folders are produced with -glvisualcheck. Fails closed if a capture,
manifest, SHA256, dimension, or pixel value is inconsistent. A platform-specific
candidate/reference comparison must use the same driver and resolution.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

EXPECTED = ("opaque-wall.rgba", "opaque-wall-repeat.rgba",
            "translucent-window.rgba", "stencil-mask.rgba",
            "combined-opaque.rgba")


def load(folder: Path):
    manifest = json.loads((folder / "evidence.json").read_text())
    if manifest.get("schema") != 1 or manifest.get("width") != 64 or manifest.get("height") != 64:
        raise ValueError("unexpected GL fixture schema/dimensions")
    found = manifest.get("captures")
    if not isinstance(found, dict) or set(found) != set(EXPECTED):
        raise ValueError("required capture set differs")
    images = {}
    for filename in EXPECTED:
        pixels = (folder / filename).read_bytes()
        if len(pixels) != 64 * 64 * 4:
            raise ValueError(f"{filename} has wrong byte length")
        if hashlib.sha256(pixels).hexdigest().upper() != found[filename]:
            raise ValueError(f"{filename} SHA256 mismatch")
        images[filename] = pixels
    if images["opaque-wall.rgba"] != images["opaque-wall-repeat.rgba"]:
        raise ValueError("world frame changed during deterministic redraw")
    return manifest, images


def compare(reference: Path, candidate: Path):
    rmeta, ref = load(reference)
    cmeta, cand = load(candidate)
    if rmeta.get("driver") != cmeta.get("driver"):
        raise ValueError("driver mismatch; cross-driver captures are evidence, not bit-exact reference")
    report = {}
    for filename in EXPECTED:
        wrong = sum(a != b for a, b in zip(ref[filename], cand[filename]))
        report[filename] = {"differentBytes": wrong,
                            "totalBytes": len(ref[filename]),
                            "passed": wrong == 0}
    return {"passed": all(x["passed"] for x in report.values()),
            "driver": cmeta["driver"], "images": report}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        result = compare(args.reference, args.candidate)
    except (KeyError, ValueError, OSError, json.JSONDecodeError) as exc:
        result = {"passed": False, "error": str(exc)}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n")
    print(args.output, "PASS" if result["passed"] else "FAIL")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
