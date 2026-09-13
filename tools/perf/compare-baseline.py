#!/usr/bin/env python3
"""Package and compare existing Project Prime performance observations.

This tool deliberately does not generate a synthetic benchmark.  It packages
JSON emitted by the existing ``nettest --performance-baseline`` command,
``worker-soak/analyze-capacity.py``, or client/render baseline telemetry and
compares only the numeric observations present in the supplied baseline.

The default comparison is report-only.  A metric more than 10 percent worse
is ``advisory`` and one more than 20 percent worse is a
``failure-candidate``.  Operators may opt into a non-zero exit code for the
latter with ``--fail-on-candidate`` after a baseline has been stabilized on a
comparable environment.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
import math
import os
from pathlib import Path
import platform
import re
import subprocess
import sys
from typing import Any


SCHEMA_VERSION = 1
ADVISORY_PERCENT = 10.0
CANDIDATE_PERCENT = 20.0
SCENARIO_FIELDS = (
    "NanosecondsPerOperation",
    "AllocatedBytesPerOperation",
    "P95Nanoseconds",
    "P99Nanoseconds",
    "P999Nanoseconds",
)
SAFE_NAME = re.compile(r"[^A-Za-z0-9_.-]+")


def _utc_now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def _read_json(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError(f"cannot read {path}: {error}") from error
    if not isinstance(value, dict):
        raise ValueError(f"{path} must contain a JSON object")
    return value


def _git(root: Path, *args: str) -> str | None:
    try:
        result = subprocess.run(
            ["git", *args], cwd=root, capture_output=True, text=True,
            check=False, timeout=5,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None
    return result.stdout.strip() if result.returncode == 0 else None


def environment(root: Path) -> dict[str, Any]:
    dotnet = None
    try:
        result = subprocess.run(["dotnet", "--version"], cwd=root,
                                capture_output=True, text=True, check=False, timeout=15)
        if result.returncode == 0:
            dotnet = result.stdout.strip()
    except (OSError, subprocess.TimeoutExpired):
        pass
    return {
        "os": platform.system(),
        "osVersion": platform.version(),
        "architecture": platform.machine(),
        "python": platform.python_version(),
        "dotnet": dotnet,
        "processorCount": os.cpu_count() or 1,
        "ci": os.environ.get("CI", "false").lower() == "true",
        "gitCommit": _git(root, "rev-parse", "HEAD"),
        "gitDirty": bool(_git(root, "status", "--porcelain")),
    }


def _number(value: Any) -> float | int | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    if isinstance(value, float) and not math.isfinite(value):
        return None
    return value


def _name(value: Any, fallback: str) -> str:
    return SAFE_NAME.sub("_", str(value or fallback)).strip("._") or fallback


def _flatten(value: Any, prefix: str, output: dict[str, float | int]) -> None:
    number = _number(value)
    if number is not None:
        output[prefix] = number
        return
    if isinstance(value, dict):
        for key in sorted(value):
            _flatten(value[key], f"{prefix}.{key}" if prefix else str(key), output)
    elif isinstance(value, list):
        for index, item in enumerate(value):
            _flatten(item, f"{prefix}[{index}]", output)


def extract_metrics(source_name: str, report: dict[str, Any]) -> dict[str, float | int]:
    """Extract metrics from known existing report shapes only."""
    source = _name(source_name, "source")
    output: dict[str, float | int] = {}
    scenarios = report.get("Scenarios", report.get("scenarios"))
    if isinstance(scenarios, list):
        for index, scenario in enumerate(scenarios):
            if not isinstance(scenario, dict):
                continue
            status = scenario.get("Status", scenario.get("status"))
            if status is not None and str(status).lower() not in {"ok", "pass", "passed"}:
                # The content-free nettest report intentionally emits
                # content-backed scenarios as ``gap`` entries with numeric
                # zero defaults. They are not measured observations.
                continue
            scenario_name = _name(scenario.get("Name", scenario.get("name")), f"scenario_{index}")
            for field in SCENARIO_FIELDS:
                value = scenario.get(field, scenario.get(field[0].lower() + field[1:]))
                number = _number(value)
                if number is not None:
                    output[f"{source}.{scenario_name}.{field}"] = number

    shapes = report.get("shapes")
    if isinstance(shapes, list):
        for index, shape in enumerate(shapes):
            if not isinstance(shape, dict) or not isinstance(shape.get("observed"), dict):
                continue
            _flatten(shape["observed"], f"{source}.shape[{index}].observed", output)

    # Client/render captures use stable sections rather than a common command
    # envelope.  Only these measured sections are admitted; metadata such as
    # frame IDs and resolutions is not treated as a performance metric.
    for section in ("CpuFrameTime", "cpuFrameTime", "Device", "device",
                    "Geometry", "geometry", "Telemetry", "telemetry"):
        if section in report and isinstance(report[section], dict):
            _flatten(report[section], f"{source}.{section}", output)

    # Future producers may expose a deliberate metrics object.  This fallback
    # keeps the tool useful for those reports without flattening arbitrary
    # identities/counters from unrelated JSON documents.
    if isinstance(report.get("metrics"), dict):
        _flatten(report["metrics"], source, output)
    if not output:
        raise ValueError(
            f"source {source_name!r} contains no supported metrics; pass an existing "
            "nettest, worker-soak, client baseline, or explicit metrics report"
        )
    return output


def collect(args: argparse.Namespace) -> int:
    root = args.root.resolve()
    if not args.source:
        print("perf baseline: at least one --source NAME=JSON is required", file=sys.stderr)
        return 2
    sources: dict[str, dict[str, Any]] = {}
    metrics: dict[str, dict[str, Any]] = {}
    for value in args.source:
        name, separator, file_value = value.partition("=")
        if not separator or not name or not file_value:
            print(f"perf baseline: --source must be NAME=JSON, got {value!r}", file=sys.stderr)
            return 2
        path = Path(file_value).resolve()
        try:
            report = _read_json(path)
            extracted = extract_metrics(name, report)
        except ValueError as error:
            print(f"perf baseline: {error}", file=sys.stderr)
            return 2
        sources[name] = {
            "path": str(path.relative_to(root)) if path.is_relative_to(root) else str(path),
            "kind": report.get("kind", report.get("Kind", "unknown")),
            "schemaVersion": report.get("schemaVersion", report.get("SchemaVersion")),
            "contentMode": report.get("contentMode", report.get("ContentMode")),
            "generatedUtc": report.get("generatedUtc", report.get("GeneratedUtc")),
            "runtime": report.get("runtime", report.get("Runtime")),
            "metricCount": len(extracted),
        }
        for key, metric in extracted.items():
            if key in metrics:
                print(f"perf baseline: duplicate metric {key}", file=sys.stderr)
                return 2
            metrics[key] = {"value": metric, "direction": args.direction}
        print(f"PASS {name}: {len(extracted)} measured metric(s)")

    payload = {
        "schemaVersion": SCHEMA_VERSION,
        "kind": "ProjectPrime.PerformanceBaseline",
        "generatedUtc": _utc_now(),
        "environment": environment(root),
        "policy": {
            "advisoryPercent": ADVISORY_PERCENT,
            "failureCandidatePercent": CANDIDATE_PERCENT,
            "defaultDirection": args.direction,
            "reportOnly": True,
        },
        "sources": sources,
        "metrics": metrics,
    }
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"Wrote performance baseline: {output}")
    return 0


def _metric_map(report: dict[str, Any]) -> dict[str, dict[str, Any]]:
    if report.get("schemaVersion") != SCHEMA_VERSION:
        raise ValueError("performance reports must use schemaVersion 1")
    raw = report.get("metrics")
    if not isinstance(raw, dict):
        raise ValueError("performance report must contain a metrics object")
    result: dict[str, dict[str, Any]] = {}
    for name, value in raw.items():
        if isinstance(value, dict):
            metric = dict(value)
            numeric = metric.get("value")
            direction = metric.get("direction", "lower-is-better")
        else:
            numeric, direction = value, "lower-is-better"
            metric = {"value": numeric, "direction": direction}
        number = _number(numeric)
        if number is None or direction not in {"lower-is-better", "higher-is-better", "neutral"}:
            raise ValueError(f"invalid metric {name!r}")
        metric["value"] = number
        metric["direction"] = direction
        result[str(name)] = metric
    return result


def _regression_percent(before: float | int, after: float | int, direction: str) -> float:
    if direction == "neutral":
        return 0.0
    if before == 0:
        if after == before:
            return 0.0
        return math.inf if (
            direction == "lower-is-better" and after > before
        ) or (
            direction == "higher-is-better" and after < before
        ) else -math.inf
    improvement_sign = 1 if direction == "lower-is-better" else -1
    return improvement_sign * ((after - before) / abs(before) * 100.0)


def compare(baseline: dict[str, Any], current: dict[str, Any]) -> dict[str, Any]:
    before = _metric_map(baseline)
    after = _metric_map(current)
    rows: list[dict[str, Any]] = []
    missing: list[str] = []
    for name in sorted(before):
        if name not in after:
            missing.append(name)
            continue
        baseline_metric, current_metric = before[name], after[name]
        direction = current_metric.get("direction", baseline_metric.get("direction", "lower-is-better"))
        percent = _regression_percent(baseline_metric["value"], current_metric["value"], direction)
        if direction == "neutral" or percent <= ADVISORY_PERCENT:
            status = "pass"
        elif percent <= CANDIDATE_PERCENT:
            status = "advisory"
        else:
            status = "failure-candidate"
        rows.append({
            "name": name,
            "baseline": baseline_metric["value"],
            "current": current_metric["value"],
            "direction": direction,
            "regressionPercent": None if not math.isfinite(percent) else round(percent, 4),
            "status": status,
        })
    extra = sorted(set(after) - set(before))
    candidates = [row["name"] for row in rows if row["status"] == "failure-candidate"]
    advisories = [row["name"] for row in rows if row["status"] == "advisory"]
    baseline_environment = baseline.get("environment", {})
    current_environment = current.get("environment", {})
    comparable_fields = ("os", "architecture", "dotnet")
    environment_values = [field for field in comparable_fields
                          if baseline_environment.get(field) is not None
                          and current_environment.get(field) is not None]
    environment_match = bool(environment_values) and all(
        baseline_environment.get(field) == current_environment.get(field)
        for field in environment_values
    )
    return {
        "schemaVersion": SCHEMA_VERSION,
        "kind": "ProjectPrime.PerformanceComparison",
        "generatedUtc": _utc_now(),
        "policy": {
            "advisoryPercent": ADVISORY_PERCENT,
            "failureCandidatePercent": CANDIDATE_PERCENT,
            "reportOnly": True,
        },
        "environmentMatch": environment_match,
        "environmentNote": "Environment metadata differs; interpret thresholds as advisory." if not environment_match else "Comparable OS/architecture/runtime metadata.",
        "metrics": rows,
        "missingMetrics": missing,
        "newMetrics": extra,
        "advisories": advisories,
        "failureCandidates": candidates,
        "status": "incomplete" if missing else ("failure-candidate" if candidates else "advisory" if advisories else "pass"),
    }


def check(args: argparse.Namespace) -> int:
    try:
        result = compare(_read_json(args.baseline), _read_json(args.current))
    except ValueError as error:
        print(f"perf baseline: {error}", file=sys.stderr)
        return 2
    if args.output:
        output = args.output.resolve()
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(
        f"performance comparison: {result['status']} "
        f"({len(result['advisories'])} advisory, {len(result['failureCandidates'])} failure-candidate)"
    )
    for row in result["metrics"]:
        print(f"{row['status'].upper()} {row['name']}: "
              f"{row['baseline']} -> {row['current']} ({row['regressionPercent']}%)")
    for name in result["missingMetrics"]:
        print(f"MISSING {name}")
    if args.fail_on_candidate and (result["failureCandidates"] or result["missingMetrics"]):
        return 1
    return 0


def parser() -> argparse.ArgumentParser:
    root = Path(__file__).resolve().parents[2]
    command = argparse.ArgumentParser(description=__doc__)
    subparsers = command.add_subparsers(dest="command", required=True)

    collect_parser = subparsers.add_parser("collect", help="package existing JSON observations")
    collect_parser.add_argument("--root", type=Path, default=root)
    collect_parser.add_argument("--source", action="append", help="NAME=JSON (may be repeated)")
    collect_parser.add_argument("--direction", choices=("lower-is-better", "higher-is-better", "neutral"),
                                default="lower-is-better")
    collect_parser.add_argument("--output", type=Path, required=True)
    collect_parser.set_defaults(handler=collect)

    check_parser = subparsers.add_parser("compare", help="compare two packaged observations")
    check_parser.add_argument("--baseline", type=Path, required=True)
    check_parser.add_argument("--current", type=Path, required=True)
    check_parser.add_argument("--output", type=Path)
    check_parser.add_argument("--fail-on-candidate", action="store_true")
    check_parser.set_defaults(handler=check)
    return command


def main(argv: list[str] | None = None) -> int:
    args = parser().parse_args(argv)
    try:
        return args.handler(args)
    except (OSError, ValueError) as error:
        print(f"perf baseline: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
