#!/usr/bin/env python3
"""Fail-closed physical OpenGL/GLES animation, map and netcode acceptance.

This tool validates *submitted* native-game-on-device evidence. It does not
control a physical device, synthesize traces or pronounce CI as hardware proof.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import statistics
import sys

FRAME = "[glframe-json] "
GPU = "[glgpu-json] "
REQUIRED_SCENES = {
    "samus", "kanden", "spire", "weavel", "noxus", "trace", "sylux",
    "gorea", "alimbic-turret", "quake-mover", "quake-fog",
    "quake-tcmod", "replay", "wall-occlusion"
}
CONTEXT = ("room", "width", "height", "cap", "renderer",
           "glVersion", "assemblyMvid", "shadows", "renderScale")
METRICS = ("frameIntervalP95Ms", "frameIntervalP99Ms", "renderCpuP99Ms",
           "onePercentLowEstimateFps", "simulationHz")

class EvidenceError(ValueError):
    pass

def _read_log(path: Path):
    content = path.read_text(encoding="utf-8")
    if "GL error" in content or "[glvisualcheck] FAIL" in content:
        raise EvidenceError(f"{path}: GL error or GPU visual failure")
    frame, gpu = [], []
    for line in content.splitlines():
        for prefix, target in ((FRAME, frame), (GPU, gpu)):
            at = line.find(prefix)
            if at < 0:
                continue
            item = json.loads(line[at + len(prefix):])
            if item.get("schema") != 1 or item.get("samples") != 240:
                raise EvidenceError("noncanonical 240-frame timing window")
            target.append(item)
            break
    if len(frame) < 4:
        raise EvidenceError("physical trace requires four 240-frame windows per arm")
    contexts = []
    for window in frame:
        c = window.get("context")
        if not isinstance(c, dict) or not all(c.get(k) not in ("", None) for k in CONTEXT):
            raise EvidenceError("context missing binary/map/GPU/cap provenance")
        contexts.append(tuple(c[k] for k in CONTEXT))
        for k in METRICS:
            v = window.get(k)
            if isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v):
                raise EvidenceError(f"invalid or missing {k}")
        if abs(window["simulationHz"] - 60.0) > 1.5:
            raise EvidenceError("canonical 60 Hz simulation drift")
        if window.get("droppedSimulationSteps") != 0 or window.get("stalls") != 0:
            raise EvidenceError("dropped fixed simulation step or frame stall")
    if len(set(contexts)) != 1:
        raise EvidenceError("capture window context changed")
    for item in gpu:
        for k in ("worldGpuP99Ms", "droppedQueries"):
            v = item.get(k)
            if isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v) or v < 0:
                raise EvidenceError(f"bad GPU measurement {k}")
        if item["droppedQueries"] != 0:
            raise EvidenceError("GPU timer query loss")
    return contexts[0], frame[1:], gpu[1:] if len(gpu) >= 4 else []

def _median(frames, name):
    return statistics.median(x[name] for x in frames)

def _evidence_file(root: Path, case: dict, key: str):
    entry = case.get(key)
    if not isinstance(entry, dict):
        raise EvidenceError("missing independent visual capture evidence")
    name, expected = entry.get("path"), entry.get("sha256")
    if not isinstance(name, str) or not isinstance(expected, str) or not re.fullmatch(r"[0-9a-f]{64}", expected):
        raise EvidenceError("missing screenshot path or SHA-256")
    path = (root / name).resolve()
    if not path.is_relative_to(root.resolve()) or not path.is_file():
        raise EvidenceError("missing screenshot or path escapes capture folder")
    blob = path.read_bytes()
    if not blob.startswith(bytes.fromhex("89504e470d0a1a0a")):
        raise EvidenceError("not a native PNG screenshot")
    if hashlib.sha256(blob).hexdigest() != expected:
        raise EvidenceError("screenshot hash mismatch")

def evaluate(manifest: dict, baseline: Path, enabled: Path, root: Path):
    if manifest.get("source") != "native-game-on-physical-device" or manifest.get("emulator") is not False:
        raise EvidenceError("synthetic/virtual device is not physical hardware acceptance")
    if not manifest.get("reviewed_by") or manifest.get("reviewed") is not True:
        raise EvidenceError("independent human visual review not attested")
    sha = manifest.get("build_sha")
    if not isinstance(sha, str) or not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise EvidenceError("missing immutable build SHA")
    binary = manifest.get("binary_sha256")
    if not isinstance(binary, str) or not re.fullmatch(r"[0-9a-f]{64}", binary):
        raise EvidenceError("missing actual tested binary digest")
    device = manifest.get("device")
    if not isinstance(device, dict) or not all(
        isinstance(device.get(k), str) and device[k] for k in
        ("platform", "gpu", "driver", "model", "refresh_hz")
    ):
        raise EvidenceError("missing real device, driver or display identity")
    if device["platform"] not in ("windows-opengl", "linux-opengl", "macos-opengl", "android-gles"):
        raise EvidenceError("unsupported GPU platform")
    cases = manifest.get("visual_cases")
    if not isinstance(cases, dict) or not REQUIRED_SCENES <= cases.keys():
        raise EvidenceError("missing required Hunter/boss/map/replay captures")
    for name in sorted(REQUIRED_SCENES):
        case = cases[name]
        if case.get("reviewed") is not True or case.get("passed") is not True:
            raise EvidenceError(f"{name}: native visual parity not signed off")
        _evidence_file(root, case, "baseline")
        _evidence_file(root, case, "enabled")

    b_context, b_frames, b_gpu = _read_log(baseline)
    e_context, e_frames, e_gpu = _read_log(enabled)
    if b_context != e_context or b_context[7] != e_context[7]:
        raise EvidenceError("not the same game build/map/GPU/quality between A and B")
    if str(b_context[6]) != manifest.get("assembly_mvid"):
        raise EvidenceError("timing trace MVID differs from attested binary")
    if b_frames[0].get("animationBlend") is not False or e_frames[0].get("animationBlend") is not True:
        raise EvidenceError("baseline/enabled animation switch not observed by runtime")
    p99_baseline = _median(b_frames, "frameIntervalP99Ms")
    cpu_baseline = _median(b_frames, "renderCpuP99Ms")
    low_baseline = _median(b_frames, "onePercentLowEstimateFps")
    p99_enabled = _median(e_frames, "frameIntervalP99Ms")
    cpu_enabled = _median(e_frames, "renderCpuP99Ms")
    low_enabled = _median(e_frames, "onePercentLowEstimateFps")
    gpu_ok = bool(b_gpu and e_gpu and _median(e_gpu, "worldGpuP99Ms")
                  <= _median(b_gpu, "worldGpuP99Ms") * 1.03)
    smooth = (p99_enabled <= p99_baseline * 1.05
              and cpu_enabled <= cpu_baseline * 1.05
              and low_enabled >= low_baseline * 0.98)
    return {
        "schema": 1, "hardware_verified": gpu_ok and smooth,
        "timing": {"baseline_p99_ms": p99_baseline, "enabled_p99_ms": p99_enabled,
                   "cpu_baseline_p99_ms": cpu_baseline, "cpu_enabled_p99_ms": cpu_enabled,
                   "low_baseline_fps": low_baseline, "low_enabled_fps": low_enabled,
                   "gpu_within_3_percent": gpu_ok},
        "result": ("PASSED" if gpu_ok and smooth else "NOT_VERIFIED"),
        "note": ("GPU timing absent or performance regressed" if not gpu_ok or not smooth
                 else "Submitted real-device evidence passed the declared thresholds")
    }

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--enabled", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
        result = evaluate(manifest, args.baseline, args.enabled, args.manifest.parent)
        code = 0 if result["hardware_verified"] else 1
    except (OSError, ValueError, TypeError, KeyError, json.JSONDecodeError) as exc:
        result = {"schema": 1, "hardware_verified": False, "result": "NOT_VERIFIED",
                  "reason": str(exc)}
        code = 1
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(result["result"], str(args.output))
    return code

if __name__ == "__main__":
    sys.exit(main())
