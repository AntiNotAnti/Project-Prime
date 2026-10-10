#!/usr/bin/env python3
"""Fail-closed evaluation of same-binary OpenGL E/F A/B measurements.

Input game logs must contain [glframe-json] and [glprofile-json] lines from
at least three warm-up-excluded, 240-frame profiling windows per arm. GPU timer
data is required for automatic eligibility, as is independent *native-game*
visual acceptance evidence. This never changes production defaults.
"""
import argparse
import json
import math
from pathlib import Path
import statistics
import sys

ARMS = {
    "baseline": (False, False),
    "vbo": (True, False),
    "binding": (False, True),
    "combined": (True, True),
}
CATEGORIES = {
    "[glframe-json] ": "frame",
    "[glgpu-json] ": "gpu",
    "[glprofile-json] ": "submission",
}
REQUIRED_VISUAL = {"ice-hive-walls", "ice-hive-shadows",
                   "glass-and-portals", "studio-and-hud", "android-gles"}


def median(data, key):
    return statistics.median(sample[key] for sample in data)


def read_samples(path: Path, wanted: tuple[bool, bool]):
    samples = {"frame": [], "gpu": [], "submission": []}
    contents = path.read_text(encoding="utf-8")
    if "GL error" in contents or "[glvisualcheck] FAIL" in contents:
        raise ValueError(f"{path}: GL error / visual failure in run")
    for line in contents.splitlines():
        for prefix, category in CATEGORIES.items():
            offset = line.find(prefix)
            if offset < 0:
                continue
            item = json.loads(line[offset + len(prefix):])
            if item.get("schema") != 1 or item.get("samples") != 240:
                raise ValueError(f"{path}: incompatible profile schema/window")
            samples[category].append(item)
            break
    if len(samples["frame"]) < 3 or len(samples["submission"]) < 3:
        raise ValueError(f"{path}: insufficient warmed 240-frame windows")
    for sample in samples["frame"]:
        if (sample.get("vbo"), sample.get("bindingCache")) != wanted:
            raise ValueError(f"{path}: claimed optimization flags do not match actual runtime")
        for key in ("renderCpuP95Ms", "renderCpuP99Ms", "presentWallP99Ms",
                    "frameIntervalP95Ms", "frameIntervalP99Ms",
                    "simulationHz", "onePercentLowEstimateFps"):
            number = sample[key]
            if isinstance(number, bool) or not isinstance(number, (float, int)) or not math.isfinite(number):
                raise ValueError(f"{path}: nonfinite {key}")
        if abs(sample["simulationHz"] - 60.0) > 1.5:
            raise ValueError(f"{path}: simulation rate deviated from 60 Hz")
        if sample["droppedSimulationSteps"] or sample["stalls"]:
            raise ValueError(f"{path}: dropped simulation steps or stalls during capture")
    for category in ("gpu", "submission"):
        for sample in samples[category]:
            for key, number in sample.items():
                if key in ("schema", "samples"):
                    continue
                if isinstance(number, bool) or not isinstance(number, (int, float)) or not math.isfinite(number) or number < 0:
                    raise ValueError(f"{path}: invalid {category}.{key}")
    # Ignore the first completed window after level/prewarm to reduce
    # shader/upload startup contamination. Still demand >=3 measurement windows.
    return {k: (v[1:] if len(v) > 3 else v) for k, v in samples.items()}


def evaluate(logs, visual):
    # Self-reported evidence has to name actual native captures. Synthetic
    # pixel fixtures alone cannot opt production hardware into an optimization.
    screenshots = set(visual.get("cases", []))
    visual_pass = (visual.get("source") == "native-game-on-device"
                   and visual.get("passed") is True
                   and REQUIRED_VISUAL <= screenshots)
    inputs = {name: read_samples(path, ARMS[name]) for name, path in logs.items()}
    origin = inputs["baseline"]
    base_frame = origin["frame"]
    base_gpu = origin["gpu"]
    baseline = {
        "frameP99Ms": median(base_frame, "frameIntervalP99Ms"),
        "cpuP99Ms": median(base_frame, "renderCpuP99Ms"),
        "gpuP99Ms": median(base_gpu, "worldGpuP99Ms") if base_gpu else None,
        "onePercentLowFps": median(base_frame, "onePercentLowEstimateFps")
    }
    results = {}
    for name in ("vbo", "binding", "combined"):
        arm = inputs[name]
        frames = arm["frame"]
        improvement = ((baseline["frameP99Ms"] - median(frames, "frameIntervalP99Ms"))
                       / baseline["frameP99Ms"]) if baseline["frameP99Ms"] > 0 else 0
        gpu_present = bool(base_gpu and arm["gpu"])
        gpu_ok = (gpu_present
                  and median(arm["gpu"], "worldGpuP99Ms") <= baseline["gpuP99Ms"] * 1.03
                  and all(w["droppedQueries"] == 0 for w in arm["gpu"]))
        cpu_ok = median(frames, "renderCpuP99Ms") <= baseline["cpuP99Ms"] * 1.05
        p95_ok = median(frames, "frameIntervalP95Ms") <= median(base_frame, "frameIntervalP95Ms") * 1.05
        low_ok = median(frames, "onePercentLowEstimateFps") >= baseline["onePercentLowFps"] * 0.98
        # Never auto-enable without real visual acceptance and GPU timing,
        # even if a headless suite or facade counters look favorable.
        recommended = visual_pass and gpu_ok and cpu_ok and p95_ok and low_ok and improvement >= 0.03
        results[name] = {
            "eligible": recommended,
            "p99FrameImprovementFraction": round(improvement, 5),
            "cpuP99WithinFivePercent": cpu_ok,
            "frameP95WithinFivePercent": p95_ok,
            "onePercentLowWithinTwoPercent": low_ok,
            "gpuWithinThreePercent": gpu_ok,
            "nativeVisualAccepted": visual_pass
        }
    return {"schema": 1, "baseline": baseline, "arms": results,
            "automaticActivation": False,
            "reason": "Eligibility is a recommendation only; code defaults remain off."}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ARMS:
        parser.add_argument(f"--{name}", type=Path, required=True)
    parser.add_argument("--visual", type=Path, required=True,
                        help="independent native GPU acceptance manifest")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        logs = {n: getattr(args, n) for n in ARMS}
        report = evaluate(logs, json.loads(args.visual.read_text()))
        status = 0
    except (ValueError, KeyError, OSError, TypeError, json.JSONDecodeError) as exc:
        report = {"schema": 1, "automaticActivation": False,
                  "error": str(exc), "arms": {}}
        status = 1
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n")
    print(args.output, "READY_FOR_REVIEW" if status == 0 else "INVALID_EVIDENCE")
    return status


if __name__ == "__main__":
    sys.exit(main())
