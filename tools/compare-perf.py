#!/usr/bin/env python3
import json
import math
import pathlib
import sys

IDENTITY = ("Room", "Players", "Width", "Height", "RenderScale", "PresentationHz")
CHECKS = (
    ("DrawAverageMs", 1.10, "lower"),
    ("DrawP95Ms", 1.12, "lower"),
    ("DrawP99Ms", 1.15, "lower"),
    ("DrawP999Ms", 1.20, "lower"),
    ("SimulationP99Ms", 1.15, "lower"),
    ("AllocatedBytesPerDraw", 1.25, "lower"),
    ("DrawP99EquivalentThroughputFps", 0.85, "higher"),
    ("DrawP999EquivalentThroughputFps", 0.80, "higher"),
)
GC_COUNTERS = ("Gen0Collections", "Gen1Collections", "Gen2Collections")


def finite_number(report, key, minimum=0, positive=False):
    value = report.get(key)
    if type(value) not in (int, float):
        raise ValueError(f"{key} must be a JSON number")
    try:
        value = float(value)
    except OverflowError:
        raise ValueError(f"{key} must be finite") from None
    if not math.isfinite(value) or value < minimum or (positive and value == 0):
        qualifier = "positive" if positive else "nonnegative"
        raise ValueError(f"{key} must be finite and {qualifier}")
    return value


def load_report(path):
    def reject_constant(value):
        raise ValueError(f"nonfinite JSON constant {value}")

    report = json.loads(path.read_text(encoding="utf-8"), parse_constant=reject_constant)
    if not isinstance(report, dict):
        raise ValueError("report must be a JSON object")
    # Version1 mislabeled draw throughput as presented FPS. Refuse old or
    # mixed schemas before comparing the complete schema2 metric contract.
    if type(report.get("Version")) is not int or report["Version"] != 2:
        raise ValueError("regenerate both throughput reports with schema2")
    if not isinstance(report.get("Room"), str) or not report["Room"].strip():
        raise ValueError("Room must be a nonempty string")
    for key in ("Players", "Width", "Height", "PresentationHz"):
        if type(report.get(key)) is not int or report[key] <= 0:
            raise ValueError(f"{key} must be a positive integer")
    finite_number(report, "RenderScale", positive=True)
    for key, _, _ in CHECKS:
        finite_number(report, key)
    # GC counts are informational and remain optional for older reports.
    # A present value must still be a valid count, rather than coerced to zero.
    for key in GC_COUNTERS:
        if key in report and (type(report[key]) is not int or report[key] < 0):
            raise ValueError(f"{key} must be a nonnegative integer")
    return report


def main(args=None):
    args = sys.argv[1:] if args is None else args
    if len(args) < 2:
        raise SystemExit("usage: compare-perf.py BASELINE.json CURRENT.json [--no-fail]")
    baseline_path, current_path = map(pathlib.Path, args[:2])
    no_fail = "--no-fail" in args[2:]
    reports = []
    for path in (baseline_path, current_path):
        try:
            reports.append(load_report(path))
        except (OSError, UnicodeError, ValueError) as error:
            print(f"PERF COMPARE REFUSED: {path.name}: {error}")
            return 2
    baseline, current = reports
    mismatch = [key for key in IDENTITY if baseline[key] != current[key]]
    if mismatch:
        print("PERF COMPARE REFUSED: workload identity differs: " + ", ".join(mismatch))
        for key in mismatch:
            print(f"  {key}: baseline={baseline[key]!r}, current={current[key]!r}")
        return 2

    failed = []
    print(f"Performance comparison: {baseline_path.name} -> {current_path.name}")
    print(f"Workload: {current['Room']} | {current['Players']} players | "
          f"{current['Width']}x{current['Height']} | "
          f"{current['PresentationHz']} Hz presentation")
    print()
    for key, threshold, direction in CHECKS:
        before, after = float(baseline[key]), float(current[key])
        change = (after / before) if before else (1.0 if after == 0 else float("inf"))
        if direction == "lower":
            bad = change > threshold
            limit = f"+{(threshold - 1) * 100:.0f}%"
        else:
            bad = change < threshold
            limit = f"-{(1 - threshold) * 100:.0f}%"
        delta = "n/a" if before == 0 else f"{(change - 1) * 100:+.1f}%"
        print(f"{key:24} {before:11.3f} -> {after:11.3f}  {delta:>9}  "
              + ("REGRESSION" if bad else "ok"))
        if bad:
            failed.append((key, delta, limit))
    gc_before = sum(baseline.get(key, 0) for key in GC_COUNTERS)
    gc_after = sum(current.get(key, 0) for key in GC_COUNTERS)
    print(f"{'GC collections':24} {gc_before:11d} -> {gc_after:11d}")

    if failed:
        print()
        print("Regression thresholds exceeded:")
        for key, delta, limit in failed:
            print(f"  {key}: {delta} (limit {limit})")
        if not no_fail:
            return 1
    print()
    print("PASS" if not failed else "WARN")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
