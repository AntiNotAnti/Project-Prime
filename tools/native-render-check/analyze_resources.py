#!/usr/bin/env python3
"""Describe growth and post-drain trends; never infer a native leak from RSS."""
import argparse
import json
import math
from pathlib import Path

METRICS = ("workingSetBytes", "privateBytes", "virtualBytes", "managedLiveBytes",
           "gcCommittedBytes", "gcHeapSizeBytes", "gcFragmentedBytes", "lohSizeBytes",
           "lohFragmentedBytes", "processThreadCount", "processHandleCount",
           "threadPoolThreads", "threadPoolPendingWorkItems")


def numeric(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def trend(rows, x_key, metric):
    points = [(row[x_key], row.get("memory", {}).get(metric)) for row in rows]
    points = [(x, y) for x, y in points if numeric(x) and numeric(y)]
    if not points:
        return {"samples": 0, "available": False, "slope": None}
    xs, ys = zip(*points)
    mx, my = sum(xs) / len(xs), sum(ys) / len(ys)
    denom = sum((x - mx) ** 2 for x in xs)
    slope = sum((x - mx) * (y - my) for x, y in points) / denom if denom else None
    residual = sum((y - my - (slope or 0) * (x - mx)) ** 2 for x, y in points)
    total = sum((y - my) ** 2 for y in ys)
    return {"samples": len(points), "available": True, "first": ys[0], "last": ys[-1],
            "delta": ys[-1] - ys[0], "mean": my, "min": min(ys), "max": max(ys),
            "slope": slope, "rSquared": 1 - residual / total if slope is not None and total else None}


def series(rows, warmup_cycles, block_cycles):
    rows = sorted((r for r in rows if numeric(r.get("cycle"))), key=lambda r: r["cycle"])
    eligible = [r for r in rows if r["cycle"] >= warmup_cycles]
    midpoint = eligible[len(eligible) // 2]["cycle"] if eligible else None
    tail = [r for r in eligible if r["cycle"] >= midpoint] if eligible else []
    blocks = []
    for start in range(warmup_cycles, int(rows[-1]["cycle"]) + 1 if rows else warmup_cycles, block_cycles):
        block = [r for r in eligible if start <= r["cycle"] < start + block_cycles]
        if block:
            blocks.append({"firstCycle": start, "lastCycle": start + block_cycles - 1,
                           "metrics": {m: trend(block, "cycle", m) for m in METRICS}})
    return {"sampleCount": len(rows), "postWarmupSamples": len(eligible),
            "slopeUnit": "metric units per full cycle (zero-based cycle index)",
            "postWarmup": {m: trend(eligible, "cycle", m) for m in METRICS},
            "latterHalf": {m: trend(tail, "cycle", m) for m in METRICS}, "blocks": blocks}


def analyze(data, warmup_cycles=5, block_cycles=10):
    if warmup_cycles < 0 or block_cycles < 1:
        raise ValueError("warmup must be nonnegative and block size positive")
    records = data.get("records", [])
    # Separate observations so differing cleanup points are never pooled into
    # a claimed subsystem slope. Both map loads comprise one full cycle.
    groups = {"postReferenceUnload": [r for r in records if r.get("candidate") is False],
              "postCandidateUnload": [r for r in records if r.get("candidate") is True],
              "postStudioUnload": data.get("studioReloads", [])}
    quiescent = [r for r in data.get("drainSamples", []) if r.get("phase") == "quiescent"]
    manifest = data.get("manifest", {})
    return {"schema": 1, "arm": data.get("arm", "combined"), "complete": data.get("complete") is True,
            "error": data.get("error") or data.get("drainError"), "warmupCyclesExcluded": warmup_cycles,
            "sourceIdentity": {k: manifest.get(k) for k in ("clientSha256", "harnessSha256", "assemblyMvid")},
            "series": {name: series(rows, warmup_cycles, block_cycles) for name, rows in groups.items() if rows},
            "quiescent": {"sampleCount": len(quiescent), "slopeUnit": "metric units per elapsed second",
                          "metrics": {m: trend(quiescent, "elapsedSeconds", m) for m in METRICS}},
            "boundedness": "UNVERIFIED: slopes are descriptive evidence. RSS includes runtime, driver caches and allocator retention; plateau/leak requires repeated matched runs and native allocation/VM-category evidence.",
            "limitations": "OLS/R-squared are descriptive, not independent-sample confidence intervals. LOH/GC metrics describe the last collection. Null platform counters remain unavailable. No FPS or native subsystem byte attribution."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("result", type=Path)
    parser.add_argument("--warmup-cycles", type=int, default=5)
    parser.add_argument("--block-cycles", type=int, default=10)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = analyze(json.loads(args.result.read_text()), args.warmup_cycles, args.block_cycles)
    text = json.dumps(result, indent=2, allow_nan=False) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text)
    else:
        print(text, end="")


if __name__ == "__main__":
    main()
