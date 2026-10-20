#!/usr/bin/env python3
"""Compare Project Prime Android performance logs from matching real-device runs.

Each androidperf line is a 5-second *window summary*, not a raw frame trace.
Aggregated report percentiles are explicitly labeled as window medians.
"""
import argparse
import json
import re
import statistics
import sys
from pathlib import Path

NUMBER = r"([0-9]+(?:\\.[0-9]+)?)"
FRAMES = re.compile(r"frame ms p50/p90/p95/p99\\s+" + "/".join([NUMBER] * 4))
STAGES = re.compile(r"stage p95 sim/render/ui/swap\\s+" + "/".join([NUMBER] * 4))
COUNTS = re.compile(r">20/33/50\\s+(\\d+)/(\\d+)/(\\d+) of (\\d+)")
SHAPE = re.compile(r"(\\d+)x(\\d+) world (\\d+)% cap (.+?) display active/max " + NUMBER + "/" + NUMBER + r" Hz")
THERMAL = re.compile(r"thermal\\s+(\\d+)")


def parse_lines(lines):
    windows = []
    for line in lines:
        if "frame ms p50/p90/p95/p99" not in line:
            continue
        frame, stages, counts, shape = [pattern.search(line) for pattern in
                                         (FRAMES, STAGES, COUNTS, SHAPE)]
        if any(match is None for match in (frame, stages, counts, shape)):
            raise ValueError("Malformed Android frame report: " + line.strip()[:250])
        width, height, scale, cap, active, maximum = shape.groups()
        over20, over33, over50, count = map(int, counts.groups())
        if count <= 0 or over20 > count or over33 > over20 or over50 > over33:
            raise ValueError("Android frame report contains inconsistent counts")
        thermal = THERMAL.search(line)
        windows.append(dict(
            p50=float(frame[1]), p90=float(frame[2]),
            p95=float(frame[3]), p99=float(frame[4]),
            sim95=float(stages[1]), render95=float(stages[2]),
            ui95=float(stages[3]), swap95=float(stages[4]),
            over20=over20, over33=over33, over50=over50, frames=count,
            shape=(int(width), int(height), int(scale), cap.strip()),
            refresh=(float(active), float(maximum)),
            thermal=int(thermal[1]) if thermal else None))
    return windows


def summarize(windows, minimum):
    if len(windows) < minimum:
        raise ValueError(f"Expected >= {minimum} reporting windows, found {len(windows)}")
    shapes = {tuple(row["shape"]) for row in windows}
    if len(shapes) != 1:
        raise ValueError("Settings/resolution varied within the captured run")
    frames = sum(row["frames"] for row in windows)
    data = {
        "windows": len(windows),
        "frames": frames,
        "shape": list(next(iter(shapes))),
        "median_window_p95_ms": statistics.median(row["p95"] for row in windows),
        "median_window_p99_ms": statistics.median(row["p99"] for row in windows),
        "worst_window_p99_ms": max(row["p99"] for row in windows),
        "median_window_render_p95_ms": statistics.median(row["render95"] for row in windows),
        "median_window_ui_p95_ms": statistics.median(row["ui95"] for row in windows),
        "over20_rate": sum(row["over20"] for row in windows) / frames,
        "over33_rate": sum(row["over33"] for row in windows) / frames,
        "over50_rate": sum(row["over50"] for row in windows) / frames,
        "median_active_refresh_hz": statistics.median(row["refresh"][0] for row in windows),
        "max_thermal_status": max((row["thermal"] or 0) for row in windows),
    }
    return data


def compare(baseline, candidate, tolerance_pct):
    if baseline["shape"] != candidate["shape"]:
        raise ValueError("Baseline and candidate used different resolution, scale or FPS cap")
    if abs(baseline["median_active_refresh_hz"] - candidate["median_active_refresh_hz"]) > .5:
        raise ValueError("Baseline and candidate used different display refresh rates")
    field = "median_window_p99_ms"
    change_pct = (candidate[field] / max(.000001, baseline[field]) - 1) * 100
    threshold = tolerance_pct
    return {"p99_window_median_change_pct": round(change_pct, 3),
            "regression": change_pct > threshold,
            "tolerance_pct": threshold}


def line(p95, p99, over33=0):
    return (f"frame ms p50/p90/p95/p99 16/16.5/{p95}/{p99}; "
            f"stage p95 sim/render/ui/swap 2/7/2/5; limiter p95 0.2; "
            f">20/33/50 {over33}/{over33}/0 of 300; alloc 10 KiB; "
            "1280x720 world 90% cap Display display active/max 120/120 Hz "
            "thermal 0 app-governor off")


def self_test():
    base = summarize(parse_lines([line(17, 22), line(18, 24)]), 2)
    better = summarize(parse_lines([line(16, 19), line(16, 20)]), 2)
    assert base["frames"] == 600 and base["median_window_p99_ms"] == 23
    assert not compare(base, better, 5)["regression"]
    assert compare(better, base, 5)["regression"]
    assert base["over33_rate"] == 0
    try:
        summarize(parse_lines([line(16, 20)]), 2)
    except ValueError:
        pass
    else:
        raise AssertionError("minimum windows should be enforced")
    print("ANDROID PERF COMPARE: self-test passed")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--candidate", type=Path)
    parser.add_argument("--minimum-windows", type=int, default=12)
    parser.add_argument("--max-p99-regression-pct", type=float, default=5.)
    parser.add_argument("--json-out", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0
    if args.baseline is None or args.candidate is None:
        parser.error("provide --baseline and --candidate")
    if args.minimum_windows < 1 or args.max_p99_regression_pct < 0:
        parser.error("minimum windows must be positive and tolerance nonnegative")
    try:
        baseline = summarize(parse_lines(args.baseline.read_text(
            encoding="utf-8", errors="replace").splitlines()), args.minimum_windows)
        candidate = summarize(parse_lines(args.candidate.read_text(
            encoding="utf-8", errors="replace").splitlines()), args.minimum_windows)
        verdict = compare(baseline, candidate, args.max_p99_regression_pct)
    except (ValueError, OSError) as exc:
        print(f"ANDROID PERF COMPARE: invalid capture: {exc}", file=sys.stderr)
        return 2
    report = {"baseline": baseline, "candidate": candidate, "comparison": verdict}
    output = json.dumps(report, indent=2)
    print(output)
    if args.json_out:
        args.json_out.write_text(output + "\n", encoding="utf-8")
    return 1 if verdict["regression"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
