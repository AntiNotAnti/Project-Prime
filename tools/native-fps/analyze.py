#!/usr/bin/env python3
"""Strict offline diagnostics from chronological application frame intervals."""
import argparse
import gzip
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
    schema = document.get("schema")
    boundaries = []
    timing_boundaries = []
    if schema in (2, 3) and "frames" in document:
        source = f"glframe-capture-schema{schema}"
        if schema == 3:
            if integer(document.get("droppedBoundaryRecords"), "droppedBoundaryRecords") != 0:
                raise ValueError("droppedBoundaryRecords must be zero; incomplete boundary evidence")
            boundaries = document.get("boundaries")
            if not isinstance(boundaries, list):
                raise ValueError("boundaries must be an array")
            last_boundary = None
            for boundary in boundaries:
                if not isinstance(boundary, dict) or not isinstance(boundary.get("Reason"), str) or not boundary["Reason"]:
                    raise ValueError("boundary reason required")
                stamp = integer(boundary.get("Timestamp"), "boundary timestamp")
                if last_boundary is not None and stamp < last_boundary:
                    raise ValueError("boundary timestamps must be chronological")
                last_boundary = stamp
                integer(boundary.get("Segment"), "boundary segment")
                integer(boundary.get("GenerationBefore"), "boundary generation before")
                integer(boundary.get("GenerationAfter"), "boundary generation after")
        integer(document.get("droppedRecords"), "droppedRecords")
        if document["droppedRecords"] != 0:
            raise ValueError("droppedRecords must be zero; incomplete raw capture")
        rows = document["frames"]
        if not isinstance(rows, list):
            raise ValueError("frames must be an array")
        intervals, caps = [], []
        frequency = document.get("timestampFrequency")
        if frequency is not None:
            integer(frequency, "timestampFrequency", 1)
        previous = previous_segment = None
        for i, row in enumerate(rows):
            if not isinstance(row, dict):
                raise ValueError(f"frames[{i}] must be an object")
            intervals.append(number(row.get("IntervalMs"), f"frames[{i}].IntervalMs", True))
            timestamp = integer(row.get("PresentTimestamp"), f"frames[{i}].PresentTimestamp")
            if previous is not None and timestamp <= previous:
                raise ValueError("frame timestamps must be strictly chronological")
            segment = integer(row.get("Segment"), f"frames[{i}].Segment")
            if schema == 3:
                start = integer(row.get("IntervalStartTimestamp"), "IntervalStartTimestamp")
                if start >= timestamp or frequency is None:
                    raise ValueError("schema 3 requires a positive explicit interval and timestamp frequency")
                expected = (float(timestamp) - float(start)) * 1000 / frequency
                if not math.isclose(intervals[-1], expected, rel_tol=1e-9, abs_tol=max(1e-6, 2000 / frequency)):
                    raise ValueError("frame interval disagrees with explicit start timestamp gap")
                if previous is not None and segment == previous_segment and start != previous:
                    raise ValueError("missing interval inside capture segment")
                before = integer(row.get("PresentationGenerationBefore"), "generation before")
                after = integer(row.get("PresentationGeneration"), "generation after")
                stalls = integer(row.get("StallsThisFrame"), "StallsThisFrame")
                drops = integer(row.get("DroppedStepsThisFrame"), "DroppedStepsThisFrame")
                reason = row.get("BoundaryReason")
                expected_reason = "timing-stall" if stalls else "catch-up-overflow" if drops else "presentation-reset" if after != before else "none"
                if after < before or reason != expected_reason or ((stalls or drops) and after == before):
                    raise ValueError("inconsistent timing boundary generation/reason")
                for scope in ("CallbackToPresentMs", "ProviderWaitMs"):
                    if number(row.get(scope), scope) < 0:
                        raise ValueError("negative frame scope")
                if reason != "none":
                    timing_boundaries.append({"frame": i, "reason": reason, "before": before, "after": after,
                                              "stalls": stalls, "droppedSteps": drops, "intervalMs": intervals[-1]})
            if previous is not None and frequency is not None and segment == previous_segment:
                # Match production ElapsedMs double conversion, including large clock values.
                expected_interval = (float(timestamp) - float(previous)) * 1000 / frequency
                if not math.isclose(intervals[-1], expected_interval, rel_tol=1e-9, abs_tol=max(1e-6, 2000 / frequency)):
                    raise ValueError("frame interval disagrees with chronological timestamp gap")
            previous = timestamp
            previous_segment = segment
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
        active_cap = caps[0] if caps else None
        active_vsync = rows[0].get("Vsync") if rows else None
        profiling = True
        reported_simulation_hz = None
        fixed_step_count = sum(integer(row["SimulationSteps"], "SimulationSteps") for row in rows) if rows and all("SimulationSteps" in row for row in rows) else None
    else:
        source = "legacy-native-harness"
        result = document.get("result", document)
        if not isinstance(result, dict):
            raise ValueError("result must be an object")
        if result.get("complete") is False or result.get("timingEvidenceComplete") is False:
            raise ValueError("native harness did not complete its timing evidence")
        if "droppedIntervalRecords" in result and integer(result["droppedIntervalRecords"], "droppedIntervalRecords") != 0:
            raise ValueError("droppedIntervalRecords must be zero; incomplete raw timing")
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
        active_cap = cap
        active_vsync = result.get("vsync", "UNVERIFIED")
        workload = result.get("observedWorkload") or {}
        profiling = False if workload.get("eligibleNormalTiming") is True else "UNVERIFIED"
        reported_simulation_hz = result.get("simulationHz")
        fixed_step_count = None
        frequency = None
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
        "intervalMeaning": ("Application Present returns" if source.startswith("glframe-capture-schema")
                            else "Harness frame callback returns, including post-Present bookkeeping")
                           + "; not physical scanout or GPU time",
        "loadedBinaryIdentity": identity,
        "fixedSimulationHz": 60, "reportedSimulationHz": reported_simulation_hz,
        "capturedSimulationHz": 1000 * fixed_step_count / total if fixed_step_count is not None else None,
        "activeCap": active_cap, "activeVsync": active_vsync, "profilingActive": profiling,
        "explicitGraphicsOverrides": identity.get("flags", "UNVERIFIED") if isinstance(identity, dict) else "UNVERIFIED",
        "timestampIntervalConsistency": "CHECKED: adjacent stored frames" if frequency is not None else "UNVERIFIED: no timestamp frequency",
        "samples": count, "chronologicalIntervalsMs": intervals,
        "lifecycleBoundaries": boundaries, "timingBoundaries": timing_boundaries,
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
        if args.output and (args.input.resolve() == args.output.resolve()
                            or (args.output.exists() and args.input.samefile(args.output))):
            raise ValueError("output must not overwrite the raw input receipt")
        opener = gzip.open if args.input.suffix == ".gz" else open
        with opener(args.input, "rt", encoding="utf-8-sig") as source:
            report = analyze(json.load(source))
        payload = json.dumps(report, indent=2, allow_nan=False) + "\n"
        if args.output:
            args.output.write_text(payload)
        else:
            sys.stdout.write(payload)
    except (ValueError, OSError, OverflowError, EOFError) as error:
        print(f"frame analysis failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
