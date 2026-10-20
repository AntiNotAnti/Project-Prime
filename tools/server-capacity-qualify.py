#!/usr/bin/env python3
"""Validate measured active Linux Project Prime authority capacity.

This is a source/host consistency and deadline gate, not an estimator of
production capacity, statistical confidence, or remote-client performance.
"""
import argparse
import json
from pathlib import Path


def load(path):
    with path.open(encoding="utf-8") as file:
        report = json.load(file)
    if not isinstance(report, dict) or not isinstance(report.get("arms"), list):
        raise ValueError(f"{path}: missing capacity arms")
    return report


def examine(arm, p99_ceiling, max_overruns):
    problems = []
    if arm.get("mode") != "active" or not arm.get("activeSimulationVerified"):
        problems.append("active simulation not verified")
    if arm.get("colocatedClientLoad") is not True:
        problems.append("client-load scope missing")
    if arm.get("clientsPerMatch", 0) < 2:
        problems.append("insufficient independent UDP peers")
    if not arm.get("serverBinarySha256") or not arm.get("clientBinarySha256"):
        problems.append("missing executable hashes")
    if not arm.get("processes") or len(arm["processes"]) != arm.get("instances"):
        problems.append("missing server process measurements")
    max_p99 = 0.0
    total_dropped = total_overruns = total_failures = 0
    for process in arm.get("processes", []):
        step = process.get("lastServerStep")
        if step is None or step.get("steps", 0) < 60:
            problems.append("no authoritative step evidence")
            continue
        p99 = step.get("p99Milliseconds")
        if p99 is None:
            problems.append("simulation p99 unavailable")
        else:
            max_p99 = max(max_p99, p99)
            if p99 > p99_ceiling:
                problems.append(f"p99 {p99} ms exceeds {p99_ceiling} ms")
        total_dropped += step.get("droppedTicks", 0)
        total_overruns += step.get("overruns", 0)
        total_failures += step.get("stepFailures", 0)
    if total_dropped:
        problems.append(f"{total_dropped} simulation deadlines dropped")
    if total_overruns > max_overruns:
        problems.append(f"{total_overruns} overruns above {max_overruns} budget")
    if total_failures:
        problems.append(f"{total_failures} simulation failures")
    if arm.get("aggregateCpuPercent", {}).get("mean") is None:
        problems.append("CPU evidence missing")
    if arm.get("aggregateRssMiB", {}).get("mean") is None:
        problems.append("RSS evidence missing")
    return dict(instances=arm.get("instances"), passed=not problems,
                problems=problems, maximumP99Milliseconds=max_p99,
                overruns=total_overruns, droppedTicks=total_dropped,
                failures=total_failures,
                aggregateCpuPercent=arm.get("aggregateCpuPercent", {}).get("mean"),
                aggregateRssMiB=arm.get("aggregateRssMiB", {}).get("mean"))


def qualify(before, after, p99_ceiling=16.67, max_overruns=0):
    if not after.get("arms"):
        raise ValueError("candidate has no capacity arms")
    if before is not None:
        if before.get("room") != after.get("room"):
            raise ValueError("room mismatch")
        if {arm.get("instances") for arm in before["arms"]} != {
                arm.get("instances") for arm in after["arms"]}:
            raise ValueError("instance-count mismatch")
    checks = []
    baseline = {arm["instances"]: arm for arm in before["arms"]} if before else {}
    for candidate in after["arms"]:
        n = candidate.get("instances")
        previous = baseline.get(n)
        if previous is not None:
            if (previous.get("seconds") != candidate.get("seconds")
                or previous.get("clientsPerMatch") != candidate.get("clientsPerMatch")
                or previous.get("mode") != candidate.get("mode")
                or previous.get("host") != candidate.get("host")):
                raise ValueError(f"{n} instances: unequal host/workload")
        result = examine(candidate, p99_ceiling, max_overruns)
        if previous is not None:
            old = examine(previous, p99_ceiling, max_overruns)
            result["baselinePassed"] = old["passed"]
            result["baselineProblems"] = old["problems"]
            for field in ("aggregateCpuPercent", "aggregateRssMiB", "maximumP99Milliseconds"):
                a, b = old[field], result[field]
                result[field + "Delta"] = None if a is None or b is None else b - a
        checks.append(result)
    return dict(schema=1, scope="co-located active UDP workload; not remote-host release qualification",
                before=before is not None, passGuardrails=all(c["passed"] for c in checks),
                comparisonSupportsImprovementClaim=False,
                reason="single matched runs do not establish a statistically significant improvement",
                arms=checks)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--max-p99-ms", type=float, default=16.67)
    parser.add_argument("--max-overruns", type=int, default=0)
    args = parser.parse_args()
    if args.max_p99_ms <= 0 or args.max_overruns < 0:
        parser.error("guardrails must be nonnegative, with positive p99")
    result = qualify(load(args.baseline) if args.baseline else None,
                     load(args.candidate), args.max_p99_ms, args.max_overruns)
    if args.output.exists():
        parser.error("output exists; preserve earlier run evidence")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passGuardrails"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
