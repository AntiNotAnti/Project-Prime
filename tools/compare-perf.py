#!/usr/bin/env python3
"""Compare CPU draw-work reports; these are not presentation-pacing measurements."""
import argparse
import json
import math
import pathlib
import re
import sys

IDENTITY = ("Room", "Players", "Width", "Height", "RenderScale", "PresentationHz")
CHECKS = (
    ("DrawAverageMs", 1.10, "lower"),
    ("DrawP95Ms", 1.12, "lower"),
    ("DrawP99Ms", 1.15, "lower"),
    ("DrawP999Ms", 1.20, "lower"),
    ("SimulationP99Ms", 1.15, "lower"),
    ("AllocatedBytesPerDraw", 1.25, "lower"),
    ("OnePercentLowFps", 0.85, "higher"),
    ("PointOnePercentLowFps", 0.80, "higher"),
)
GC_KEYS = ("Gen0Collections", "Gen1Collections", "Gen2Collections")
SHA256 = re.compile(r"[a-fA-F0-9]{64}\Z")


def load_object(path):
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError("duplicate JSON field: " + key)
            result[key] = value
        return result
    result = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=pairs)
    if not isinstance(result, dict):
        raise ValueError(str(path) + " must contain a JSON object")
    return result


def number(value, name, *, positive=False):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
        raise ValueError(name + " must be a finite number")
    if value < 0 or (positive and value == 0):
        raise ValueError(name + (" must be positive" if positive else " must be nonnegative"))
    return value


def validate_paired_report(report):
    if report.get("MeasurementKind") != "batched-draw-cpu":
        raise ValueError("paired A/B requires MeasurementKind=batched-draw-cpu")
    if not SHA256.fullmatch(str(report.get("ApplicationImageSha256", ""))):
        raise ValueError("paired A/B requires ApplicationImageSha256")
    if report.get("ApplicationImageKind") not in ("assembly", "process"):
        raise ValueError("paired A/B requires ApplicationImageKind")
    hardware = report.get("Hardware")
    if not isinstance(hardware, dict):
        raise ValueError("paired A/B requires Hardware metadata")
    for key in ("MachineSha256", "OperatingSystem", "ProcessArchitecture", "GlVendor", "GlRenderer", "GlVersion"):
        if not isinstance(hardware.get(key), str) or not hardware[key].strip():
            raise ValueError("paired A/B requires Hardware." + key)
    if not SHA256.fullmatch(hardware["MachineSha256"]):
        raise ValueError("Hardware.MachineSha256 must be a SHA-256 digest")
    number(hardware.get("LogicalProcessors"), "Hardware.LogicalProcessors", positive=True)
    workload = report.get("Workload")
    if not isinstance(workload, dict):
        raise ValueError("paired A/B requires Workload metadata")
    if workload.get("Schema") != 1 or workload.get("Route") != "map-audit-v1":
        raise ValueError("unsupported paired A/B workload contract")
    for key in ("SourceIdentity", "Settings", "GameRules"):
        if not isinstance(workload.get(key), dict) or not workload[key]:
            raise ValueError("paired A/B requires Workload." + key)
    source = workload["SourceIdentity"]
    if source.get("Scope") != "room-files-v1" or not isinstance(source.get("GameKey"), str) or not source["GameKey"]:
        raise ValueError("paired A/B requires a supported source identity")
    if not isinstance(source.get("Files"), dict) or not source["Files"]:
        raise ValueError("paired A/B requires room source file digests")
    for key, digest in source["Files"].items():
        if not isinstance(key, str) or not SHA256.fullmatch(str(digest)):
            raise ValueError("invalid room source digest")
    if not all(key in source["Files"] for key in ("Model", "Collision")):
        raise ValueError("room source identity requires model and collision digests")
    # These packs have dependencies outside the room-source scope. Refuse rather
    # than treating equal enabled settings as proof that their bytes are equal.
    for key in ("TextureReplacements", "CharacterModelReplacements"):
        if workload["Settings"].get(key) is not False:
            raise ValueError("paired A/B room-files scope requires " + key + "=false")
    if not isinstance(workload.get("Bots"), bool) or not isinstance(workload.get("MainHunter"), str) or not workload["MainHunter"]:
        raise ValueError("paired A/B requires bot and hunter workload metadata")
    number(report.get("Seconds"), "Seconds", positive=True)
    number(report.get("DrawSamples"), "DrawSamples", positive=True)
    for key in IDENTITY:
        if key == "Room":
            if not isinstance(report.get(key), str) or not report[key]:
                raise ValueError("Room must be nonempty")
        else:
            value = number(report.get(key), key, positive=True)
            if int(value) != value:
                raise ValueError(key + " must be an integer")
    if int(report["DrawSamples"]) != report["DrawSamples"]:
        raise ValueError("DrawSamples must be an integer")
    for key, _, _ in CHECKS:
        number(report.get(key), key, positive=key != "AllocatedBytesPerDraw")
    for key in GC_KEYS:
        value = number(report.get(key), key)
        if int(value) != value:
            raise ValueError(key + " must be an integer")
    if not (report["DrawP95Ms"] <= report["DrawP99Ms"] <= report["DrawP999Ms"]):
        raise ValueError("draw percentiles must be ordered")


def paired_checks(baseline, current, tolerance):
    validate_paired_report(baseline)
    validate_paired_report(current)
    identity = (*IDENTITY, "Seconds", "MeasurementKind", "ApplicationImageSha256", "ApplicationImageKind", "Hardware", "Workload")
    mismatches = [key for key in identity if baseline[key] != current[key]]
    if mismatches:
        raise ValueError("paired A/B identity differs: " + ", ".join(mismatches))
    if tolerance.get("Schema") != 1 or tolerance.get("MeasurementKind") != "batched-draw-cpu":
        raise ValueError("unsupported measured tolerance contract")
    # Tie the measured variance to these reports' exact machine/workload/image.
    for key in ("ApplicationImageSha256", "Hardware", "Workload"):
        if tolerance.get(key) != baseline[key]:
            raise ValueError("measured tolerance identity differs: " + key)
    expected_identity = {key: baseline[key] for key in (*IDENTITY, "Seconds")}
    if tolerance.get("ReportIdentity") != expected_identity:
        raise ValueError("measured tolerance ReportIdentity differs")
    if not isinstance(tolerance.get("Evidence"), str) or not tolerance["Evidence"].strip():
        raise ValueError("measured tolerance requires Evidence identifying baseline variance captures")
    limits = tolerance.get("MaxRegressionFraction")
    if not isinstance(limits, dict) or set(limits) != {key for key, _, _ in CHECKS}:
        raise ValueError("measured tolerance requires exactly the compared metric limits")
    result = []
    for key, _, direction in CHECKS:
        fraction = number(limits[key], "MaxRegressionFraction." + key)
        if fraction >= 1:
            raise ValueError("measured tolerance fractions must be less than 1")
        result.append((key, 1 + fraction if direction == "lower" else 1 - fraction, direction))
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", type=pathlib.Path)
    parser.add_argument("current", type=pathlib.Path)
    parser.add_argument("--no-fail", action="store_true", help="Report regressions without a nonzero regression exit")
    parser.add_argument("--paired-ab", action="store_true", help="Require same application image, hardware, sources and workload")
    parser.add_argument("--tolerances", type=pathlib.Path, help="Explicit measured same-machine variance limits for --paired-ab")
    args = parser.parse_args(argv)
    try:
        baseline = load_object(args.baseline)
        current = load_object(args.current)
        if args.paired_ab:
            if args.tolerances is None:
                raise ValueError("--paired-ab requires --tolerances with measured baseline variance")
            checks = paired_checks(baseline, current, load_object(args.tolerances))
        else:
            if args.tolerances is not None:
                raise ValueError("--tolerances requires --paired-ab")
            mismatch = [key for key in IDENTITY if baseline.get(key) != current.get(key)]
            if mismatch:
                raise ValueError("workload identity differs: " + ", ".join(mismatch))
            checks = CHECKS
        failed = []
        print(f"Performance comparison: {args.baseline.name} -> {args.current.name}")
        if args.paired_ab:
            print("Paired same-binary CPU draw-work comparison; no frame pacing or input-latency claim.")
        print(f"Workload: {current.get('Room')} | {current.get('Players')} players | "
              f"{current.get('Width')}x{current.get('Height')} | {current.get('PresentationHz')} Hz presentation")
        print()
        for key, threshold, direction in checks:
            before = float(baseline.get(key, 0) or 0)
            after = float(current.get(key, 0) or 0)
            change = (after / before) if before else (0.0 if after == 0 else float("inf"))
            if direction == "lower":
                bad = change > threshold if args.paired_ab else before > 0 and change > threshold
                limit = f"+{(threshold - 1) * 100:.1f}%"
            else:
                bad = before > 0 and change < threshold
                limit = f"-{(1 - threshold) * 100:.1f}%"
            delta = "n/a" if before == 0 else f"{(change - 1) * 100:+.1f}%"
            print(f"{key:24} {before:11.3f} -> {after:11.3f}  {delta:>9}  "
                  + ("REGRESSION" if bad else "ok"))
            if bad:
                failed.append((key, delta, limit))
        gc_before = sum(int(baseline.get(k, 0) or 0) for k in GC_KEYS)
        gc_after = sum(int(current.get(k, 0) or 0) for k in GC_KEYS)
        print(f"{'GC collections':24} {gc_before:11d} -> {gc_after:11d}")
        if failed:
            print("\nRegression thresholds exceeded:")
            for key, delta, limit in failed:
                print(f"  {key}: {delta} (limit {limit})")
        print("\n" + ("PASS" if not failed else "WARN"))
        return 1 if failed and not args.no_fail else 0
    except (OSError, ValueError, TypeError, OverflowError) as error:
        print("PERF COMPARE REFUSED: " + str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
