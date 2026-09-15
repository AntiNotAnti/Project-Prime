#!/usr/bin/env python3
"""Generate Project Prime release evidence from source constants and CI artifacts.

Every gate is explicit: missing inputs are recorded as ``not-run``. Test totals
come from TRX counters, warning state comes from the warning-budget comparison,
and protocol/replay identifiers come directly from their defining C# constants.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import subprocess
import sys
from typing import Any
import xml.etree.ElementTree as ET


SCHEMA_VERSION = 1
STATUSES = {"passed", "failed", "not-run"}
STATUS_ALIASES = {
    "success": "passed",
    "pass": "passed",
    "passed": "passed",
    "failure": "failed",
    "fail": "failed",
    "failed": "failed",
    "cancelled": "failed",
    "canceled": "failed",
    "skipped": "not-run",
    "not-run": "not-run",
    "not_run": "not-run",
    "": "not-run",
}
GATES = (
    "builds",
    "tests",
    "warnings",
    "projectBoundaries",
    "multiplayerGuard",
    "protocolGuard",
    "postgres",
    "lifecycle",
    "fidelity",
    "e2e",
    "platformAcceptance",
)
CONST_RE = re.compile(
    r"\b(?:public|internal|private)\s+const\s+(?:byte|ushort|int|uint)\s+"
    r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?P<value>[A-Za-z_][A-Za-z0-9_]*|\d+)\s*;"
)


def _utc_now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def _normalize_status(value: str) -> str:
    normalized = STATUS_ALIASES.get(value.strip().lower())
    if normalized is None:
        raise ValueError(f"unsupported status {value!r}")
    return normalized


def _combine_status(values: list[str]) -> str:
    if not values:
        return "not-run"
    if "failed" in values:
        return "failed"
    if "not-run" in values:
        return "not-run"
    return "passed"


def _pairs(values: list[str] | None, option: str) -> list[tuple[str, str]]:
    result: list[tuple[str, str]] = []
    for value in values or []:
        name, separator, item = value.partition("=")
        if not separator or not name or not item:
            raise ValueError(f"{option} must be NAME=VALUE, got {value!r}")
        result.append((name, item))
    return result


def _read_constants(path: Path) -> dict[str, int | str]:
    values: dict[str, int | str] = {}
    for match in CONST_RE.finditer(path.read_text(encoding="utf-8")):
        raw = match.group("value")
        values[match.group("name")] = int(raw) if raw.isdigit() else raw
    return values


def _resolve_constant(path: Path, name: str) -> int:
    constants = _read_constants(path)
    current: int | str | None = constants.get(name)
    visited: set[str] = set()
    while isinstance(current, str):
        if current in visited:
            raise ValueError(f"constant cycle while resolving {name} in {path}")
        visited.add(current)
        current = constants.get(current)
    if not isinstance(current, int):
        raise ValueError(f"cannot resolve integer constant {name} in {path}")
    return current


def source_identifiers(root: Path) -> dict[str, Any]:
    net_header = root / "src/Game/Protocol/NetHeader.cs"
    node_control = root / "src/Server.Shared/NodeControlCodec.cs"
    replay = root / "src/Shared.Replay/ReplayFile.cs"
    return {
        "protocol": _resolve_constant(net_header, "Version"),
        "nodeControlProtocol": _resolve_constant(node_control, "Version"),
        "replayFormats": {
            "linear": _resolve_constant(replay, "FormatVersion"),
            "indexed": _resolve_constant(replay, "IndexedFormatVersion"),
        },
    }


def _relative(root: Path, path: Path) -> str:
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return path.name


def _trx_counters(path: Path, root: Path) -> dict[str, Any]:
    if not path.is_file():
        return {"status": "not-run", "artifact": _relative(root, path)}
    try:
        document = ET.parse(path)
    except (ET.ParseError, OSError) as error:
        return {
            "status": "failed",
            "artifact": _relative(root, path),
            "error": f"invalid TRX: {error}",
        }
    counters = next(
        (element for element in document.iter() if element.tag.rsplit("}", 1)[-1] == "Counters"),
        None,
    )
    if counters is None:
        return {
            "status": "failed",
            "artifact": _relative(root, path),
            "error": "TRX has no Counters element",
        }
    totals: dict[str, int] = {}
    for name in ("total", "executed", "passed", "failed", "error", "timeout", "aborted", "inconclusive", "notExecuted"):
        raw = counters.attrib.get(name)
        if raw is not None:
            try:
                totals[name] = int(raw)
            except ValueError as error:
                raise ValueError(f"TRX counter {name} in {path} is not an integer") from error
    failed = sum(totals.get(name, 0) for name in ("failed", "error", "timeout", "aborted"))
    executed = totals.get("executed", totals.get("total", 0))
    status = "failed" if failed or executed == 0 else "passed"
    result: dict[str, Any] = {
        "status": status, "artifact": _relative(root, path), "totals": totals,
    }
    if executed == 0:
        result["error"] = "TRX contains no executed tests"
    return result


def _warning_evidence(path: Path | None, root: Path) -> dict[str, Any] | None:
    if path is None:
        return None
    if not path.is_file():
        return {"status": "not-run", "artifact": _relative(root, path)}
    try:
        report = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        return {"status": "failed", "artifact": _relative(root, path), "error": str(error)}
    status = report.get("status")
    if status not in {"pass", "fail"}:
        return {
            "status": "failed",
            "artifact": _relative(root, path),
            "error": "warning comparison has no pass/fail status",
        }
    result: dict[str, Any] = {
        "status": "passed" if status == "pass" else "failed",
        "artifact": _relative(root, path),
    }
    projects = report.get("projects")
    if isinstance(projects, list):
        result["projects"] = {
            str(row.get("name")): {
                "count": row.get("currentCount"),
                "delta": row.get("delta"),
            }
            for row in projects if isinstance(row, dict) and row.get("name")
        }
    violations = report.get("violations")
    if isinstance(violations, list):
        result["violations"] = [str(value) for value in violations]
    return result


def _git_commit(root: Path) -> str | None:
    result = subprocess.run(
        ["git", "rev-parse", "HEAD"], cwd=root, capture_output=True, text=True,
        check=False, timeout=5,
    )
    return result.stdout.strip() if result.returncode == 0 else None


def generate(args: argparse.Namespace) -> dict[str, Any]:
    root = args.root.resolve()
    identifiers = source_identifiers(root)
    evidence: dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "kind": "ProjectPrime.ReleaseEvidence",
        "generatedUtc": _utc_now(),
        "commit": args.commit or _git_commit(root),
        **identifiers,
    }
    gate_statuses: dict[str, list[str]] = {name: [] for name in GATES}
    for name, raw_status in _pairs(args.gate, "--gate"):
        if name not in gate_statuses:
            raise ValueError(f"unknown gate {name!r}")
        gate_statuses[name].append(_normalize_status(raw_status))
    for name in GATES:
        evidence[name] = {"status": _combine_status(gate_statuses[name])}

    suites_by_gate: dict[str, dict[str, Any]] = {
        "tests": {}, "lifecycle": {}, "postgres": {}, "fidelity": {}, "e2e": {},
    }
    for name, path_text in _pairs(args.trx, "--trx"):
        lowered = name.lower()
        target = (
            "lifecycle" if lowered.startswith("lifecycle")
            else "postgres" if lowered.startswith("postgres")
            else "fidelity" if lowered.startswith("fidelity")
            else "e2e" if lowered.startswith("e2e")
            else "tests"
        )
        path = Path(path_text)
        if not path.is_absolute():
            path = root / path
        suites_by_gate[target][name] = _trx_counters(path, root)
    for gate, suites in suites_by_gate.items():
        if not suites:
            continue
        evidence[gate]["suites"] = suites
        statuses = gate_statuses[gate] + [suite["status"] for suite in suites.values()]
        evidence[gate]["status"] = _combine_status(statuses)
        totals: dict[str, int] = {}
        for suite in suites.values():
            for key, value in suite.get("totals", {}).items():
                totals[key] = totals.get(key, 0) + value
        if totals:
            evidence[gate]["totals"] = totals

    warning = _warning_evidence(args.warning_report, root)
    if warning is not None:
        statuses = gate_statuses["warnings"] + [warning["status"]]
        evidence["warnings"] = {**warning, "status": _combine_status(statuses)}
    return evidence


def _markdown(evidence: dict[str, Any]) -> str:
    replay = evidence["replayFormats"]
    lines = [
        "# Project Prime release evidence",
        "",
        f"Generated: `{evidence['generatedUtc']}`  ",
        f"Commit: `{evidence.get('commit') or 'unavailable'}`  ",
        f"Gameplay protocol: `{evidence['protocol']}`  ",
        f"Node control protocol: `{evidence['nodeControlProtocol']}`  ",
        f"Replay formats: linear `{replay['linear']}`, indexed `{replay['indexed']}`",
        "",
        "| Gate | Status | Automated totals |",
        "| --- | --- | --- |",
    ]
    for name in GATES:
        gate = evidence[name]
        totals = gate.get("totals", {})
        summary = ", ".join(f"{key}={value}" for key, value in totals.items()) or "—"
        lines.append(f"| `{name}` | **{gate['status']}** | {summary} |")
    lines.extend((
        "",
        "`not-run` is intentional evidence: the required environment or artifact was not supplied.",
        "Only `passed` gates from the qualifying environment may support a release decision.",
        "",
    ))
    return "\n".join(lines)


def parser() -> argparse.ArgumentParser:
    root = Path(__file__).resolve().parents[1]
    command = argparse.ArgumentParser(description=__doc__)
    command.add_argument("--root", type=Path, default=root)
    command.add_argument("--output-dir", type=Path, default=Path("artifacts/release"))
    command.add_argument("--commit")
    command.add_argument("--gate", action="append", help="GATE=STATUS; may be repeated")
    command.add_argument("--trx", action="append", help="SUITE=PATH; may be repeated")
    command.add_argument("--warning-report", type=Path)
    return command


def main(argv: list[str] | None = None) -> int:
    args = parser().parse_args(argv)
    try:
        evidence = generate(args)
        output = args.output_dir
        if not output.is_absolute():
            output = args.root.resolve() / output
        output.mkdir(parents=True, exist_ok=True)
        json_path = output / "release-evidence.json"
        markdown_path = output / "release-evidence.md"
        json_path.write_text(json.dumps(evidence, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        markdown_path.write_text(_markdown(evidence), encoding="utf-8")
        print(f"Wrote release evidence: {json_path}")
        print(f"Wrote release evidence: {markdown_path}")
        return 0
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(f"release evidence: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
