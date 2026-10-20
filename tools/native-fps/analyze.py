#!/usr/bin/env python3
"""Strict offline diagnostics from chronological application frame intervals."""
import argparse
import json
import math
from pathlib import Path
import sys


def number(value, label, positive=False):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
        raise ValueError(f"{label}: expected finite number")
    if positive and value <= 0:
        raise ValueError(f"{label}: expected positive number")
    return value


def integer(value, label, minimum=0):
    number(value, label)
    if not isinstance(value, int) or value < minimum:
        raise ValueError(f"{label}: expected integer >= {minimum}")
    return value


def analyze(document):
    if not isinstance(document, dict):
        raise ValueError("root must be an object")
    groups = set()
    if document.get("schema") == 2 and "frames" in document:
        source = "glframe-capture-schema2"
        integer(document.get("droppedRecords"), "droppedRecords")
        if document["droppedRecords"] != 0:
            raise ValueError("droppedRecords must be zero; incomplete raw capture")
        rows = document["frames"]
        if not isinstance(rows, list):
            raise ValueError("frames must be an array")
        intervals, caps = [], []
        previous = None
        for i, row in enumerate(rows):
            if not isinstance(row, dict):
                raise ValueError(f"frames[{i}] must be an object")
            intervals.append(number(row.get("IntervalMs"), f"frames[{i}].IntervalMs", True))
            timestamp = integer(row.get("PresentTimestamp"), f"frames[{i}].PresentTimestamp")
            if previous is not None and timestamp <= previous:
                raise ValueError("frame timestamps must be strictly chronological")
            previous = timestamp
            segment = integer(row.get("Segment"), f"frames[{i}].Segment")
            cap = integer(row.get("Cap"), f"frames[{i}].Cap", -1)
            vsync = row.get("Vsync")
            if not isinstance(vsync, str) or not vsync:
                raise ValueError(f"frames[{i}].Vsync must be a nonempty string")
            groups.add((segment, cap, vsync))
            caps.append(cap)
        if len(groups) > 1:
            raise ValueError("mixed segments/caps/vsync; analyze separate raw captures")
        summary = document.get("summary")
        if not isinstance(summary, dict):
            raise ValueError("summary must contain samples")
        expected = summary.get("samples")
        identity = document.get("context")
    else:
        source = "legacy-native-harness"
        result = document.get("result", document)
        if not isinstance(result, dict):
            raise ValueError("result must be an object")
        intervals = result.get("frameIntervalsMs")
        if not isinstance(intervals, list):
            raise ValueError("result.frameIntervalsMs must be an array")
        intervals = [number(v, f"frameIntervalsMs[{i}]", True) for i, v in enumerate(intervals)]
        expected = result.get("samples")
        cap = integer(result.get("effectiveCap", result.get("cap", 0)), "cap", -1)
        caps = [cap] * len(intervals)
        identity = {"observedWorkload": result.get("observedWorkload"),
                    "assemblyMvid": result.get("assemblyMvid"),
                    "vendor": result.get("vendor"), "renderer": result.get("renderer")}
        if "droppedRecords" in result and integer(result["droppedRecords"], "droppedRecords") != 0:
            raise ValueError("droppedRecords must be zero")
    integer(expected, "samples")
    if expected != len(intervals):
        raise ValueError("samples count does not match raw intervals")
    if not intervals:
        raise ValueError("empty capture")
    ordered = sorted(intervals)
    count = len(intervals)
    def percentile(p):
        return ordered[math.ceil(count * p / 100) - 1]
    tail_count = math.ceil(count / 100)
    total = math.fsum(intervals)
    tail_total = math.fsum(ordered[-tail_count:])
    if not math.isfinite(total) or not math.isfinite(tail_total):
        raise ValueError("interval total is not finite")
    budgets = [(v, 1000 / c) for v, c in zip(intervals, caps) if c > 0]
    late15 = sum(v > 1.5 * budget for v, budget in budgets)
    late2 = sum(v > 2 * budget for v, budget in budgets)
    return {
        "schema": 1, "source": source, "qualification": "NOT_RUN",
        "qualificationReason": "Offline diagnostic only; external device evidence is required for qualification.",
        "baselineSourceStatus": "INCOMPLETE: raw capture alone does not establish a controlled baseline",
        "rolloutDecision": "NOT_RUN: no automatic A/B or rollout decision",
        "intervalMeaning": "Chronological application Present returns; not physical scanout or GPU time",
        "loadedBinaryIdentity": identity,
        "samples": count, "chronologicalIntervalsMs": intervals,
        "captureGroup": list(next(iter(groups))) if groups else None,
        "meanCompletedFrameFps": 1000 * count / total,
        "frameIntervalP50Ms": percentile(50), "frameIntervalP95Ms": percentile(95),
        "frameIntervalP99Ms": percentile(99),
        "frameIntervalP999Ms": percentile(99.9) if count >= 1000 else None,
        "onePercentLowEstimateFps": 1000 / percentile(99),
        "trueOnePercentLowFps": 1000 * tail_count / tail_total,
        "trueOnePercentLowMethod": "1000 / mean(slowest ceil(samples * 0.01) intervals in ms)",
        "tailSamples": tail_count, "numericBudgetSamples": len(budgets),
        "lateOver1Point5Budget": late15, "lateOver2Budget": late2,
        "lateOver1Point5Fraction": late15 / len(budgets) if budgets else None,
        "lateOver2Fraction": late2 / len(budgets) if budgets else None,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path)
    parser.add_argument("-output", "--output", "-o", type=Path)
    args = parser.parse_args()
    try:
        report = analyze(json.loads(args.input.read_text()))
        payload = json.dumps(report, indent=2, allow_nan=False) + "\n"
        if args.output:
            args.output.write_text(payload)
        else:
            sys.stdout.write(payload)
    except (ValueError, OSError, OverflowError) as error:
        print(f"frame analysis failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
