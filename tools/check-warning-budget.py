#!/usr/bin/env python3
"""Collect and compare the Project Prime production warning budget.

The warning budget is deliberately a report/check layer rather than a
repository-wide warnings-as-errors policy.  ``collect`` builds each named
production project and records its own compiler/analyzer diagnostics.  The
project path in an MSBuild diagnostic is used to avoid counting the same
project-reference warning in every consumer build.  ``check`` rejects a new
warning or an increase in an existing warning code, while leaving intentional
``NoWarn``/format suppressions untouched.

Examples::

    python3 tools/check-warning-budget.py collect \
        --report artifacts/warnings/current.json --no-restore
    python3 tools/check-warning-budget.py check \
        --baseline tools/warning-budget-baseline.json \
        --current artifacts/warnings/current.json \
        --output artifacts/warnings/comparison.json
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
from typing import Any, Iterable


SCHEMA_VERSION = 1

# Keep this list explicit.  The budget is for production assemblies named by
# the stabilization plan, not every fixture/tool/test project in the tree.
PRODUCTION_PROJECTS: dict[str, str] = {
    "Game": "src/Game/Game.csproj",
    "Client.Core": "src/Client.Core/Client.Core.csproj",
    "Client.Presentation": "src/Client.Presentation/Client.Presentation.csproj",
    "Client": "src/Client/Client.csproj",
    "Renderer": "src/Renderer/Renderer.csproj",
    "Server.Shared": "src/Server.Shared/Server.Shared.csproj",
    "Server.Node": "src/Server.Node/Server.Node.csproj",
    "Server.Worker": "src/Server.Worker/Server.Worker.csproj",
    "MapPlatform": "src/MapPlatform/MapPlatform.csproj",
    "Backend": "src/Backend/Backend.csproj",
}

# MSBuild's normal diagnostic shape is:
#   path(line,column): warning CAxxxx: message [project.csproj]
# The location can also be ``CSC``/``MSBxxxx`` without a source position.
WARNING_RE = re.compile(
    r"^(?P<location>.+?)\s*:\s+warning\s+"
    r"(?P<code>[A-Za-z]+\d+)\s*:\s+(?P<message>.*?)"
    r"(?:\s+\[(?P<project>[^\]]+)\])?$"
)
LOCATION_RE = re.compile(
    r"^(?P<path>.*?)(?:\((?P<line>\d+)(?:,(?P<column>\d+))?\))?$"
)


def _utc_now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def _relative(root: Path, path: Path) -> str:
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def _resolve(root: Path, value: str) -> Path:
    candidate = Path(value)
    return candidate.resolve() if candidate.is_absolute() else (root / candidate).resolve()


def _git(root: Path, *args: str) -> str | None:
    try:
        result = subprocess.run(
            ["git", *args], cwd=root, capture_output=True, text=True, check=False,
            timeout=5,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None
    return result.stdout.strip() if result.returncode == 0 else None


def _environment(root: Path, dotnet_version: str | None, configuration: str) -> dict[str, Any]:
    return {
        "configuration": configuration,
        "os": platform.system(),
        "osVersion": platform.version(),
        "architecture": platform.machine(),
        "python": platform.python_version(),
        "dotnet": dotnet_version,
        "processorCount": os.cpu_count() or 1,
        "ci": os.environ.get("CI", "false").lower() == "true",
        "gitCommit": _git(root, "rev-parse", "HEAD"),
        "gitDirty": bool(_git(root, "status", "--porcelain")),
    }


def _parse_location(value: str) -> tuple[str, int | None, int | None]:
    match = LOCATION_RE.match(value.strip())
    if match is None:
        return value.strip(), None, None
    return (
        match.group("path").strip(),
        int(match.group("line")) if match.group("line") else None,
        int(match.group("column")) if match.group("column") else None,
    )


def parse_warnings(text: str, root: Path, target_project: Path) -> list[dict[str, Any]]:
    """Parse and de-duplicate diagnostics owned by ``target_project``."""
    target_project = target_project.resolve()
    parsed: dict[tuple[Any, ...], dict[str, Any]] = {}
    for raw_line in text.splitlines():
        match = WARNING_RE.match(raw_line.strip())
        if match is None:
            continue
        project_text = match.group("project")
        if project_text and _resolve(root, project_text) != target_project:
            # This warning belongs to a referenced project.  That project has
            # its own budget entry and must not be charged to every consumer.
            continue
        source, line, column = _parse_location(match.group("location"))
        source_path = ""
        if source and source not in {"CSC", "MSB", "MSBuild"}:
            source_path = _relative(root, _resolve(root, source))
        code = match.group("code").upper()
        message = " ".join(match.group("message").split())
        key = (code, source_path, line, column, message)
        parsed[key] = {
            "code": code,
            "path": source_path,
            "line": line,
            "column": column,
            "message": message,
        }
    return sorted(parsed.values(), key=lambda item: (
        item["code"], item["path"], item["line"] or 0, item["column"] or 0, item["message"],
    ))


def _warning_summary(warnings: Iterable[dict[str, Any]]) -> dict[str, dict[str, Any]]:
    grouped: dict[str, list[dict[str, Any]]] = {}
    for warning in warnings:
        grouped.setdefault(warning["code"], []).append({
            key: warning[key] for key in ("path", "line", "column", "message")
        })
    return {
        code: {"count": len(items), "locations": items}
        for code, items in sorted(grouped.items())
    }


def _project_specs(values: list[str] | None) -> list[tuple[str, str]]:
    if not values:
        return list(PRODUCTION_PROJECTS.items())
    specs: list[tuple[str, str]] = []
    for value in values:
        key, separator, path = value.partition("=")
        if not separator or not key or not path:
            raise ValueError(f"--project must be NAME=PATH, got {value!r}")
        specs.append((key, path))
    return specs


def collect(args: argparse.Namespace) -> int:
    root = args.root.resolve()
    dotnet = args.dotnet or shutil.which("dotnet")
    if not dotnet:
        print("warning budget: dotnet was not found", file=sys.stderr)
        return 2
    try:
        version_result = subprocess.run(
            [dotnet, "--version"], cwd=root, capture_output=True, text=True,
            check=False, timeout=15,
        )
        dotnet_version = version_result.stdout.strip() if version_result.returncode == 0 else None
    except (OSError, subprocess.TimeoutExpired):
        dotnet_version = None

    projects: dict[str, dict[str, Any]] = {}
    failed = False
    for name, project_value in _project_specs(args.project):
        project = _resolve(root, project_value)
        # Incremental builds may legitimately print no analyzer diagnostics
        # after the compiler has skipped the project.  Force a fresh compile
        # so the report is an observation of the current source, not the
        # state of a local obj/ cache.
        command = [dotnet, "build", str(project), "-c", args.configuration,
                   "--no-incremental", "-v:minimal"]
        if args.no_restore:
            command.append("--no-restore")
        try:
            result = subprocess.run(
                command, cwd=root, capture_output=True, text=True, check=False,
                timeout=args.timeout,
            )
            output = result.stdout + result.stderr
            exit_code = result.returncode
        except (OSError, subprocess.TimeoutExpired) as error:
            output = str(error)
            exit_code = 2
        warnings = parse_warnings(output, root, project)
        failed |= exit_code != 0
        projects[name] = {
            "path": _relative(root, project),
            "buildExitCode": exit_code,
            "warningCount": len(warnings),
            "warnings": _warning_summary(warnings),
        }
        status = "PASS" if exit_code == 0 else "FAIL"
        print(f"{status} {name}: {len(warnings)} warning(s)")

    report = {
        "schemaVersion": SCHEMA_VERSION,
        "kind": "ProjectPrime.WarningBudgetReport",
        "generatedUtc": _utc_now(),
        "environment": _environment(root, dotnet_version, args.configuration),
        "projects": projects,
    }
    output = args.report.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"Wrote warning report: {output}")
    return 2 if failed else 0


def _read_json(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError(f"cannot read {path}: {error}") from error
    if not isinstance(value, dict):
        raise ValueError(f"{path} must contain a JSON object")
    return value


def _counts(entry: dict[str, Any]) -> dict[str, int]:
    warnings = entry.get("warnings", {})
    if not isinstance(warnings, dict):
        raise ValueError("project warnings must be an object")
    result: dict[str, int] = {}
    for code, value in warnings.items():
        if isinstance(value, dict):
            count = value.get("count")
        else:
            count = value
        if not isinstance(count, int) or count < 0:
            raise ValueError(f"warning count for {code} must be a non-negative integer")
        result[str(code).upper()] = count
    declared = entry.get("warningCount")
    if declared is not None and (not isinstance(declared, int) or declared < 0):
        raise ValueError("warningCount must be a non-negative integer")
    if declared is not None and declared != sum(result.values()):
        raise ValueError("warningCount does not match warning-code counts")
    return result


def compare(baseline: dict[str, Any], current: dict[str, Any]) -> dict[str, Any]:
    if baseline.get("schemaVersion") != SCHEMA_VERSION or current.get("schemaVersion") != SCHEMA_VERSION:
        raise ValueError("warning reports must use schemaVersion 1")
    baseline_projects = baseline.get("projects")
    current_projects = current.get("projects")
    if not isinstance(baseline_projects, dict) or not isinstance(current_projects, dict):
        raise ValueError("warning reports must contain a projects object")

    rows: list[dict[str, Any]] = []
    violations: list[str] = []
    for name, expected in sorted(baseline_projects.items()):
        if name not in current_projects:
            violations.append(f"{name}: missing from current report")
            continue
        actual = current_projects[name]
        if not isinstance(expected, dict) or not isinstance(actual, dict):
            raise ValueError(f"project entry {name} must be an object")
        if expected.get("path") and actual.get("path") != expected.get("path"):
            violations.append(
                f"{name}: project path changed from {expected.get('path')} to {actual.get('path')}"
            )
        build_exit = actual.get("buildExitCode", 0)
        if build_exit != 0:
            violations.append(f"{name}: build failed with exit code {build_exit}")
        before, after = _counts(expected), _counts(actual)
        increases = {
            code: after.get(code, 0) - before.get(code, 0)
            for code in sorted(set(before) | set(after))
            if after.get(code, 0) > before.get(code, 0)
        }
        if increases:
            details = ", ".join(f"{code} +{delta}" for code, delta in increases.items())
            violations.append(f"{name}: warning budget increased ({details})")
        rows.append({
            "name": name,
            "baselineCount": sum(before.values()),
            "currentCount": sum(after.values()),
            "delta": sum(after.values()) - sum(before.values()),
            "increases": increases,
            "buildExitCode": build_exit,
        })
    for name in sorted(set(current_projects) - set(baseline_projects)):
        violations.append(f"{name}: current project is not in the warning baseline")

    return {
        "schemaVersion": SCHEMA_VERSION,
        "kind": "ProjectPrime.WarningBudgetComparison",
        "generatedUtc": _utc_now(),
        "policy": "new warning codes and warning-count increases are rejected",
        "status": "fail" if violations else "pass",
        "projects": rows,
        "violations": violations,
    }


def check(args: argparse.Namespace) -> int:
    try:
        result = compare(_read_json(args.baseline), _read_json(args.current))
    except ValueError as error:
        print(f"warning budget: {error}", file=sys.stderr)
        return 2
    if args.output:
        output = args.output.resolve()
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    for row in result["projects"]:
        print(f"{row['name']}: {row['currentCount']} warning(s) ({row['delta']:+d})")
    for violation in result["violations"]:
        print(f"FAIL {violation}")
    if not result["violations"]:
        print("warning budget: PASS")
    return 1 if result["violations"] else 0


def parser() -> argparse.ArgumentParser:
    root = Path(__file__).resolve().parents[1]
    command = argparse.ArgumentParser(description=__doc__)
    subparsers = command.add_subparsers(dest="command", required=True)

    collect_parser = subparsers.add_parser("collect", help="build projects and write a warning report")
    collect_parser.add_argument("--root", type=Path, default=root)
    collect_parser.add_argument("--dotnet", default=None)
    collect_parser.add_argument("--configuration", default="Release")
    collect_parser.add_argument("--no-restore", action="store_true")
    collect_parser.add_argument("--timeout", type=int, default=600)
    collect_parser.add_argument("--project", action="append", help="NAME=PATH (may be repeated)")
    collect_parser.add_argument("--report", type=Path, required=True)
    collect_parser.set_defaults(handler=collect)

    check_parser = subparsers.add_parser("check", help="compare a report to a committed baseline")
    check_parser.add_argument("--baseline", type=Path, required=True)
    check_parser.add_argument("--current", type=Path, required=True)
    check_parser.add_argument("--output", type=Path)
    check_parser.set_defaults(handler=check)
    return command


def main(argv: list[str] | None = None) -> int:
    args = parser().parse_args(argv)
    try:
        return args.handler(args)
    except (OSError, ValueError) as error:
        print(f"warning budget: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
