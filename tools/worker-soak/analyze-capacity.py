#!/usr/bin/env python3
"""Analyze one reproducible Worker capacity matrix.

The matrix runner deliberately records raw, cumulative Worker diagnostics.  This
tool only summarizes those observations; it does not turn them into a universal
capacity limit.  A malformed or incomplete row is an error because silently
dropping a row would make the resulting comparison misleading.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import sys
import uuid
from collections import Counter, OrderedDict
from pathlib import Path
from typing import Any, Iterable


EXPECTED_SHAPES: tuple[tuple[int, int, int, str], ...] = (
    (2, 1, 1, "matrix"),
    (4, 1, 1, "matrix"),
    (4, 1, 2, "matrix"),
    (8, 1, 2, "matrix"),
    (8, 1, 4, "matrix"),
    (16, 1, 4, "matrix"),
    (4, 2, 2, "two-worker-scaling"),
)
EXPECTED_ROSTERS: tuple[tuple[int, int, int], ...] = (
    (1, 2, 1),
    (2, 1, 2),
    (4, 0, 0),
)
EXPECTED_ROWS: tuple[tuple[int, int, int, str, int, int, int], ...] = tuple(
    (matches, workers, lanes, family, players, bots, observers)
    for matches, workers, lanes, family in EXPECTED_SHAPES
    for players, bots, observers in EXPECTED_ROSTERS
)

HASH_RE = re.compile(r"^[0-9a-fA-F]{64}$")
COUNTER_FIELDS = (
    "packetsReceived",
    "packetsSent",
    "bytesReceived",
    "bytesSent",
    "queueDrops",
    "packetsRejected",
)
GC_FIELDS = ("gen0Collections", "gen1Collections", "gen2Collections")
LANE_COUNTER_FIELDS = ("ticks", "catchUpTicks", "droppedTicks")
_MISSING = object()


class AnalysisError(ValueError):
    """Raised when a matrix cannot provide complete, trustworthy evidence."""


def _key(value: str) -> str:
    return re.sub(r"[^a-z0-9]", "", value.lower())


def _get(value: Any, *names: str, default: Any = _MISSING) -> Any:
    if not isinstance(value, dict):
        if default is _MISSING:
            raise AnalysisError("expected an object")
        return default
    normalized = {_key(str(name)): item for name, item in value.items()}
    for name in names:
        match = normalized.get(_key(name), _MISSING)
        if match is not _MISSING:
            return match
    if default is _MISSING:
        raise AnalysisError(f"missing field: {names[0]}")
    return default


def _object(value: Any, label: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise AnalysisError(f"{label} must be an object")
    return value


def _list(value: Any, label: str) -> list[Any]:
    if not isinstance(value, list):
        raise AnalysisError(f"{label} must be an array")
    return value


def _text(value: Any, label: str, *, allow_empty: bool = False) -> str:
    if not isinstance(value, str) or (not allow_empty and not value.strip()):
        raise AnalysisError(f"{label} must be nonempty text")
    if any(ord(char) < 32 for char in value):
        raise AnalysisError(f"{label} contains control text")
    return value


def _int(value: Any, label: str, *, minimum: int | None = None) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        raise AnalysisError(f"{label} must be an integer")
    if minimum is not None and value < minimum:
        raise AnalysisError(f"{label} must be >= {minimum}")
    return value


def _number(value: Any, label: str, *, minimum: float | None = None) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise AnalysisError(f"{label} must be numeric")
    result = float(value)
    if not math.isfinite(result):
        raise AnalysisError(f"{label} must be finite")
    if minimum is not None and result < minimum:
        raise AnalysisError(f"{label} must be >= {minimum}")
    return result


def _bool(value: Any, label: str) -> bool:
    if not isinstance(value, bool):
        raise AnalysisError(f"{label} must be boolean")
    return value


def _guid_text(value: Any, label: str) -> str:
    if isinstance(value, dict):
        value = _get(value, "value", "Value", default=None)
    text = _text(value, label)
    if "/" in text or "\\" in text or text in {".", ".."}:
        raise AnalysisError(f"{label} cannot contain a path component")
    # System.Text.Json emits Guid values with hyphens, while the critical
    # event log deliberately uses ToString("N").  Canonicalize both forms so
    # reconciliation compares the identity rather than its presentation.
    try:
        return uuid.UUID(text).hex
    except ValueError:
        return text


def _hash(value: Any, label: str) -> str:
    text = _text(value, label)
    if not HASH_RE.fullmatch(text):
        raise AnalysisError(f"{label} must be a SHA-256 hex digest")
    return text.lower()


def _json(path: Path, label: str) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise AnalysisError(f"{label}: invalid JSON: {error}") from error
    return _object(value, label)


def _jsonl(path: Path, label: str) -> list[dict[str, Any]]:
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeError) as error:
        raise AnalysisError(f"{label}: cannot read: {error}") from error
    if not lines:
        raise AnalysisError(f"{label}: empty JSONL file")
    result: list[dict[str, Any]] = []
    for number, line in enumerate(lines, 1):
        if not line.strip():
            raise AnalysisError(f"{label}:{number}: blank JSONL record")
        try:
            value = json.loads(line)
        except json.JSONDecodeError as error:
            raise AnalysisError(f"{label}:{number}: invalid JSON: {error}") from error
        result.append(_object(value, f"{label}:{number}"))
    return result


def _required_file(path: Path, label: str) -> Path:
    if not path.is_file():
        raise AnalysisError(f"missing {label}: {path}")
    return path


def _canonical(value: Any) -> Any:
    """Copy a JSON value with stable key ordering for equality checks."""
    if isinstance(value, dict):
        return {key: _canonical(value[key]) for key in sorted(value)}
    if isinstance(value, list):
        return [_canonical(item) for item in value]
    return value


def _round_number(value: float) -> int | float:
    if value == 0:
        return 0
    if value.is_integer():
        return int(value)
    return value


def _delta(end: dict[str, int], start: dict[str, int], fields: Iterable[str], label: str) -> dict[str, int]:
    result: dict[str, int] = {}
    for field in fields:
        amount = end[field] - start[field]
        if amount < 0:
            raise AnalysisError(f"{label}: cumulative counter regressed: {field}")
        result[field] = amount
    return result


def _rate(delta: int, seconds: float) -> float | None:
    if seconds <= 0:
        return None
    return _round_number(delta / seconds)


def _normalise_counter_values(value: dict[str, Any], fields: Iterable[str], label: str) -> dict[str, int]:
    result: dict[str, int] = {}
    for field in fields:
        raw_name = {
            "packetsReceived": ("PacketsReceived", "packetsReceived"),
            "packetsSent": ("PacketsSent", "packetsSent"),
            "bytesReceived": ("BytesReceived", "bytesReceived"),
            "bytesSent": ("BytesSent", "bytesSent"),
            "queueDrops": ("QueueDrops", "queueDrops"),
            "packetsRejected": ("PacketsRejected", "packetsRejected"),
            "gen0Collections": ("Gen0Collections", "gen0Collections"),
            "gen1Collections": ("Gen1Collections", "gen1Collections"),
            "gen2Collections": ("Gen2Collections", "gen2Collections"),
            "ticks": ("Ticks", "ticks"),
            "catchUpTicks": ("CatchUpTicks", "catchUpTicks"),
            "droppedTicks": ("DroppedTicks", "droppedTicks"),
        }[field]
        result[field] = _int(_get(value, *raw_name), f"{label}.{field}", minimum=0)
    return result


def _normalise_roster(value: Any, label: str) -> tuple[int, int, int]:
    roster = _object(value, label)
    return (
        _int(_get(roster, "players", "Players"), f"{label}.players", minimum=0),
        _int(_get(roster, "bots", "Bots"), f"{label}.bots", minimum=0),
        _int(_get(roster, "observers", "Observers"), f"{label}.observers", minimum=0),
    )


def _parse_provenance(value: dict[str, Any]) -> tuple[dict[str, Any], int]:
    if _get(value, "kind") != "provenance":
        raise AnalysisError("matrix.jsonl: second record must be kind=provenance")
    seconds = _int(_get(value, "seconds"), "provenance.seconds", minimum=1)
    result: dict[str, Any] = {}
    for name in (
        "kind", "host", "dotnet", "os", "osBuild", "architecture", "cpuModel",
        "logicalCpuCount", "memoryBytes", "workerAssembly", "workerSha256",
        "soakAssembly", "soakSha256", "workerRuntimeManifest", "soakRuntimeManifest",
        "dataDirectory", "dataDirectoryManifest", "seconds",
    ):
        raw = _get(value, name, default=None)
        if raw is not None:
            result[name] = raw
    for name in ("host", "dotnet", "os", "osBuild", "architecture", "cpuModel",
                 "workerAssembly", "soakAssembly", "dataDirectory"):
        if name in result:
            _text(result[name], f"provenance.{name}", allow_empty=name == "cpuModel")
    if "logicalCpuCount" in result:
        _int(result["logicalCpuCount"], "provenance.logicalCpuCount", minimum=1)
    if "memoryBytes" in result and result["memoryBytes"] is not None:
        _int(result["memoryBytes"], "provenance.memoryBytes", minimum=1)
    for name in ("workerSha256", "soakSha256"):
        if name not in result:
            raise AnalysisError(f"provenance.{name} is required")
        result[name] = _hash(result[name], f"provenance.{name}")
    for name in ("workerRuntimeManifest", "soakRuntimeManifest", "dataDirectoryManifest"):
        if name not in result:
            continue
        manifest = _object(result[name], f"provenance.{name}")
        if "sha256" in {_key(str(k)) for k in manifest}:
            digest = _get(manifest, "sha256")
            # Keep the source's key spelling in the emitted provenance but validate the digest.
            _hash(digest, f"provenance.{name}.sha256")
    result["seconds"] = seconds
    return result, seconds


def _parse_run_record(value: dict[str, Any], index: int) -> dict[str, Any]:
    label = f"manifest run {index}"
    if _get(value, "kind") != "run":
        raise AnalysisError(f"{label}: expected kind=run")
    run_id = _guid_text(_get(value, "id"), f"{label}.id")
    family = _text(_get(value, "family"), f"{label}.family")
    matches = _int(_get(value, "matchesPerWorker"), f"{label}.matchesPerWorker", minimum=1)
    workers = _int(_get(value, "workers"), f"{label}.workers", minimum=1)
    lanes = _int(_get(value, "lanes"), f"{label}.lanes", minimum=1)
    total = _int(_get(value, "totalMatches"), f"{label}.totalMatches", minimum=1)
    roster = _normalise_roster(_get(value, "roster"), f"{label}.roster")
    exit_status = _int(_get(value, "exitStatus"), f"{label}.exitStatus")
    args = _get(value, "args", default=[])
    _list(args, f"{label}.args")
    return {
        "id": run_id,
        "family": family,
        "matchesPerWorker": matches,
        "workers": workers,
        "lanes": lanes,
        "totalMatches": total,
        "roster": roster,
        "exitStatus": exit_status,
        "args": args,
    }


def _parse_manifest(root: Path) -> tuple[dict[str, Any], list[dict[str, Any]], int]:
    path = _required_file(root / "matrix.jsonl", "matrix manifest")
    records = _jsonl(path, "matrix.jsonl")
    if len(records) != 2 + len(EXPECTED_ROWS):
        raise AnalysisError(
            f"matrix.jsonl: expected {len(EXPECTED_ROWS)} run records, found {max(0, len(records) - 2)}"
        )
    header = records[0]
    if _get(header, "kind") != "matrix" or _int(_get(header, "format"), "matrix.format") != 1:
        raise AnalysisError("matrix.jsonl: expected kind=matrix, format=1 header")
    provenance, seconds = _parse_provenance(records[1])
    runs: list[dict[str, Any]] = []
    seen_ids: set[str] = set()
    seen_shapes: set[tuple[int, int, int, str, int, int, int]] = set()
    for index, record in enumerate(records[2:]):
        run = _parse_run_record(record, index)
        expected = EXPECTED_ROWS[index]
        actual = (
            run["matchesPerWorker"], run["workers"], run["lanes"], run["family"],
            *run["roster"],
        )
        if actual != expected:
            raise AnalysisError(
                f"matrix.jsonl: row {index} is stale or out of order; expected {expected}, found {actual}"
            )
        if run["totalMatches"] != run["matchesPerWorker"] * run["workers"]:
            raise AnalysisError(f"matrix.jsonl: {run['id']} has inconsistent totalMatches")
        if run["id"] in seen_ids:
            raise AnalysisError(f"matrix.jsonl: duplicate run id {run['id']}")
        shape_key = (*actual,)
        if shape_key in seen_shapes:
            raise AnalysisError(f"matrix.jsonl: duplicate matrix shape/roster for {run['id']}")
        seen_ids.add(run["id"])
        seen_shapes.add(shape_key)
        if run["exitStatus"] != 0:
            raise AnalysisError(f"{run['id']}: child exitStatus={run['exitStatus']}")
        runs.append(run)
    if len(seen_shapes) != len(EXPECTED_ROWS):
        raise AnalysisError("matrix.jsonl: fixed matrix is incomplete")
    directories = {item.name for item in root.iterdir() if item.is_dir()}
    if directories != seen_ids:
        stale = sorted(directories - seen_ids)
        missing = sorted(seen_ids - directories)
        details = []
        if stale:
            details.append("stale directories=" + ",".join(stale))
        if missing:
            details.append("missing directories=" + ",".join(missing))
        raise AnalysisError("matrix output row directories do not reconcile: " + "; ".join(details))
    return provenance, runs, seconds


def _parse_metrics(run_dir: Path) -> tuple[list[dict[str, Any]], list[dict[str, Any]], list[dict[str, Any]]]:
    files = sorted(run_dir.glob("metrics-*.jsonl"), key=lambda path: path.name)
    if not files:
        raise AnalysisError(f"{run_dir.name}: no metrics segments")
    events: list[dict[str, Any]] = []
    previous_elapsed: float | None = None
    for path in files:
        segment = _jsonl(path, f"{run_dir.name}/{path.name}")
        for event in segment:
            kind = _text(_get(event, "kind"), f"{path.name}.kind")
            if kind == "sample":
                elapsed = _number(_get(event, "elapsed"), f"{path.name}.elapsed", minimum=0)
                if previous_elapsed is not None and elapsed < previous_elapsed:
                    raise AnalysisError(f"{run_dir.name}: sample elapsed order regressed")
                previous_elapsed = elapsed
            events.append(event)
    trend_path = _required_file(run_dir / "trend.jsonl", "trend metrics")
    trend = _jsonl(trend_path, f"{run_dir.name}/trend.jsonl")
    previous_trend: float | None = None
    for event in trend:
        elapsed = _number(_get(event, "elapsed"), f"{run_dir.name}/trend.jsonl.elapsed", minimum=0)
        if previous_trend is not None and elapsed < previous_trend:
            raise AnalysisError(f"{run_dir.name}: trend elapsed order regressed")
        previous_trend = elapsed
    critical_path = _required_file(run_dir / "critical-events.jsonl", "critical event log")
    critical = _jsonl(critical_path, f"{run_dir.name}/critical-events.jsonl")
    return events, trend, critical


def _parse_lane_record(lane: dict[str, Any], label: str) -> dict[str, Any]:
    lane_id = _int(_get(lane, "laneId", "LaneId"), f"{label}.laneId", minimum=0)
    counters = _normalise_counter_values(lane, LANE_COUNTER_FIELDS, label)
    latency = {
        "p50Milliseconds": _number(_get(lane, "p50Milliseconds", "P50Milliseconds"), f"{label}.p50", minimum=0),
        "p95Milliseconds": _number(_get(lane, "p95Milliseconds", "P95Milliseconds"), f"{label}.p95", minimum=0),
        "p99Milliseconds": _number(_get(lane, "p99Milliseconds", "P99Milliseconds"), f"{label}.p99", minimum=0),
        "maxMilliseconds": _number(_get(lane, "maxMilliseconds", "MaxMilliseconds"), f"{label}.max", minimum=0),
    }
    if latency["p95Milliseconds"] < latency["p50Milliseconds"]:
        raise AnalysisError(f"{label}: p95 is below p50")
    if latency["p99Milliseconds"] < latency["p95Milliseconds"]:
        raise AnalysisError(f"{label}: p99 is below p95")
    if latency["maxMilliseconds"] < latency["p99Milliseconds"]:
        raise AnalysisError(f"{label}: max is below p99")
    return {"laneId": lane_id, **counters, **latency}


def _parse_worker_sample(worker: dict[str, Any], elapsed: float, label: str) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    worker_id = _guid_text(_get(worker, "workerId", "WorkerId"), f"{label}.workerId")
    incarnation = _guid_text(_get(worker, "incarnation", "Incarnation"), f"{label}.incarnation")
    health = _get(worker, "health", "Health", default=None)
    if health is None:
        return ({"workerId": worker_id, "incarnation": incarnation, "elapsed": elapsed, "health": None}, [])
    health = _object(health, f"{label}.health")
    working_set = _int(_get(health, "workingSetBytes", "WorkingSetBytes"), f"{label}.workingSetBytes", minimum=0)
    tick_p99 = _number(_get(health, "tickP99Milliseconds", "TickP99Milliseconds"), f"{label}.tickP99Milliseconds", minimum=0)
    diagnostics = _get(health, "diagnostics", "Diagnostics", default=None)
    base = {
        "workerId": worker_id,
        "incarnation": incarnation,
        "elapsed": elapsed,
        "workingSetBytes": working_set,
        "tickP99Milliseconds": tick_p99,
        "status": _get(health, "status", "Status", default=None),
        "health": health,
    }
    if diagnostics is None:
        return ({**base, "diagnostics": None}, [])
    diagnostics = _object(diagnostics, f"{label}.diagnostics")
    cpu = _number(_get(diagnostics, "cpuPercent", "CpuPercent"), f"{label}.cpuPercent", minimum=0)
    if cpu > 100:
        raise AnalysisError(f"{label}.cpuPercent must be <= 100")
    gc = _normalise_counter_values(diagnostics, GC_FIELDS, f"{label}.gc")
    network = _normalise_counter_values(diagnostics, COUNTER_FIELDS, f"{label}.network")
    heap = _int(_get(diagnostics, "managedHeapBytes", "ManagedHeapBytes"), f"{label}.managedHeapBytes", minimum=0)
    lanes = _list(_get(diagnostics, "lanes", "Lanes"), f"{label}.lanes")
    parsed_lanes: list[dict[str, Any]] = []
    lane_ids: set[int] = set()
    for index, lane in enumerate(lanes):
        parsed = _parse_lane_record(_object(lane, f"{label}.lanes[{index}]"), f"{label}.lanes[{index}]")
        if parsed["laneId"] in lane_ids:
            raise AnalysisError(f"{label}: duplicate lane id {parsed['laneId']}")
        lane_ids.add(parsed["laneId"])
        parsed_lanes.append(parsed)
    return ({**base, "cpuPercent": cpu, "managedHeapBytes": heap, "gc": gc, "network": network,
             "lanes": parsed_lanes, "diagnostics": diagnostics}, parsed_lanes)


def _timeline_summary(records: list[dict[str, Any]], active_records: list[dict[str, Any]], fields: tuple[str, ...], label: str) -> dict[str, Any]:
    if not records or not active_records:
        raise AnalysisError(f"{label}: no active diagnostics")
    first = records[0]
    start = active_records[0]
    end = active_records[-1]
    active_seconds = max(0.0, end["elapsed"] - start["elapsed"])
    startup = {field: first[field] for field in fields}
    start_values = {field: start[field] for field in fields}
    end_values = {field: end[field] for field in fields}
    return {
        "startup": startup,
        "activeWindow": {
            "start": start_values,
            "end": end_values,
            "delta": _delta(end_values, start_values, fields, label),
            "durationSeconds": _round_number(active_seconds),
        },
    }


def _timeline_worker(records: list[dict[str, Any]], active_records: list[dict[str, Any]], workload_seconds: int,
                    lane_groups: list[dict[str, Any]]) -> dict[str, Any]:
    first = records[0]
    active = active_records
    if not active:
        raise AnalysisError(f"worker {first['workerId']}/{first['incarnation']}: no active diagnostics")
    active_seconds = max(0.0, active[-1]["elapsed"] - active[0]["elapsed"])
    network_fields = COUNTER_FIELDS
    gc_fields = GC_FIELDS
    start_network = active[0]["network"]
    end_network = active[-1]["network"]
    start_gc = active[0]["gc"]
    end_gc = active[-1]["gc"]
    network_delta = _delta(end_network, start_network, network_fields,
                           f"worker {first['workerId']}/{first['incarnation']} network")
    gc_delta = _delta(end_gc, start_gc, gc_fields,
                      f"worker {first['workerId']}/{first['incarnation']} GC")
    scheduler_delta = {field: sum(group["scheduler"]["activeWindow"]["delta"][field] for group in lane_groups)
                       for field in LANE_COUNTER_FIELDS}
    scheduler_start = {field: sum(group["scheduler"]["activeWindow"]["start"][field] for group in lane_groups)
                       for field in LANE_COUNTER_FIELDS}
    scheduler_end = {field: sum(group["scheduler"]["activeWindow"]["end"][field] for group in lane_groups)
                     for field in LANE_COUNTER_FIELDS}
    cpu_values = [record["cpuPercent"] for record in active]
    tick_values = [record["tickP99Milliseconds"] for record in active]
    return {
        "workerId": first["workerId"],
        "incarnation": first["incarnation"],
        "sampleCount": len(active),
        "elapsed": {"start": active[0]["elapsed"], "end": active[-1]["elapsed"]},
        "cpu": {
            "normalized": True,
            "unit": "percent",
            "denominator": "logicalCpuCount",
            "peakPercent": _round_number(max(cpu_values)),
            "firstPercent": _round_number(cpu_values[0]),
            "lastPercent": _round_number(cpu_values[-1]),
            "sampleCount": len(cpu_values),
        },
        "managedHeapBytes": {"peak": max(record["managedHeapBytes"] for record in active),
                              "first": active[0]["managedHeapBytes"], "last": active[-1]["managedHeapBytes"]},
        "workingSetBytes": {"peak": max(record["workingSetBytes"] for record in active),
                             "first": active[0]["workingSetBytes"], "last": active[-1]["workingSetBytes"]},
        "workerTickP99Milliseconds": {"worst": _round_number(max(tick_values))},
        "gc": _timeline_summary([{"elapsed": r["elapsed"], **r["gc"]} for r in records],
                                 [{"elapsed": r["elapsed"], **r["gc"]} for r in active], gc_fields,
                                 f"worker {first['workerId']}/{first['incarnation']} GC"),
        "network": {
            **_timeline_summary([{"elapsed": r["elapsed"], **r["network"]} for r in records],
                                [{"elapsed": r["elapsed"], **r["network"]} for r in active], network_fields,
                                f"worker {first['workerId']}/{first['incarnation']} network"),
            "activeWindow": {
                "start": {field: start_network[field] for field in network_fields},
                "end": {field: end_network[field] for field in network_fields},
                "delta": network_delta,
                "perSecond": {field: _rate(network_delta[field], active_seconds) for field in network_fields},
                "durationSeconds": _round_number(active_seconds),
            },
        },
        "scheduler": {
            "activeWindow": {"start": scheduler_start, "end": scheduler_end, "delta": scheduler_delta,
                              "durationSeconds": _round_number(active_seconds)},
        },
        "laneCount": len(lane_groups),
    }


def _timeline_lane(records: list[dict[str, Any]], active_records: list[dict[str, Any]], label: str) -> dict[str, Any]:
    first = records[0]
    active = active_records
    scheduler = _timeline_summary(records, active, LANE_COUNTER_FIELDS, label)
    return {
        "workerId": first["workerId"],
        "incarnation": first["incarnation"],
        "laneId": first["laneId"],
        "sampleCount": len(active),
        "elapsed": {"start": active[0]["elapsed"], "end": active[-1]["elapsed"]},
        "scheduler": scheduler,
        "latencyMilliseconds": {
            "p50Worst": _round_number(max(record["p50Milliseconds"] for record in active)),
            "p95Worst": _round_number(max(record["p95Milliseconds"] for record in active)),
            "p99Worst": _round_number(max(record["p99Milliseconds"] for record in active)),
            "maxWorst": _round_number(max(record["maxMilliseconds"] for record in active)),
        },
    }


def _artifact_observation(artifacts: list[dict[str, Any]], workload_seconds: int) -> dict[str, Any]:
    grouped: dict[str, list[dict[str, Any]]] = {"replay": [], "telemetry": []}
    for event in artifacts:
        name = _text(_get(event, "name", "Name"), "artifact.name")
        length = _int(_get(event, "length", "Length"), "artifact.length", minimum=1)
        digest = _hash(_get(event, "hash", "Hash"), "artifact.hash")
        match_id = _guid_text(_get(event, "matchId", "MatchId", "active.MatchId"), "artifact.matchId")
        validation = _object(_get(event, "validation", "Validation"), "artifact.validation")
        if name.lower().endswith(".fpdemo"):
            kind = "replay"
            records = _int(_get(validation, "recordCount", "RecordCount"), "artifact.validation.recordCount", minimum=1)
            checkpoints = _int(_get(validation, "checkpointCount", "CheckpointCount"), "artifact.validation.checkpointCount", minimum=1)
            events_value = {"records": records, "checkpoints": checkpoints,
                            "firstFrame": _int(_get(validation, "firstFrame", "FirstFrame"), "artifact.validation.firstFrame", minimum=0),
                            "lastFrame": _int(_get(validation, "lastFrame", "LastFrame"), "artifact.validation.lastFrame", minimum=0)}
        elif name.lower().endswith(".telemetry.json"):
            kind = "telemetry"
            event_count = _int(_get(validation, "events", "Events"), "artifact.validation.events", minimum=0)
            dropped = _int(_get(validation, "droppedEvents", "DroppedEvents"), "artifact.validation.droppedEvents", minimum=0)
            if dropped != 0:
                raise AnalysisError(f"artifact {match_id}: telemetry dropped events")
            events_value = {"events": event_count, "droppedEvents": dropped}
        else:
            raise AnalysisError(f"artifact {match_id}: unknown artifact name {name}")
        grouped[kind].append({"matchId": match_id, "name": name, "bytes": length,
                              "sha256": digest, "events": events_value})
    result: dict[str, Any] = {}
    for kind in ("replay", "telemetry"):
        entries = sorted(grouped[kind], key=lambda item: (item["matchId"], item["name"], item["sha256"]))
        total_bytes = sum(item["bytes"] for item in entries)
        event_totals: dict[str, int] = {}
        for item in entries:
            for name, value in item["events"].items():
                if name == "droppedEvents":
                    event_totals[name] = event_totals.get(name, 0) + value
                elif name in {"records", "checkpoints", "events"}:
                    event_totals[name] = event_totals.get(name, 0) + value
        result[kind] = {
            "count": len(entries),
            "bytes": total_bytes,
            "bytesPerWorkloadSecond": _round_number(total_bytes / workload_seconds),
            "events": event_totals,
            "raw": entries,
        }
    result["total"] = {
        "count": result["replay"]["count"] + result["telemetry"]["count"],
        "bytes": result["replay"]["bytes"] + result["telemetry"]["bytes"],
        "bytesPerWorkloadSecond": _round_number((result["replay"]["bytes"] + result["telemetry"]["bytes"]) / workload_seconds),
    }
    return result


def _reconcile_files(run_dir: Path, summary: dict[str, Any], backend_reports: list[dict[str, Any]], report_ids: set[str]) -> dict[str, Any]:
    ingested = _int(_get(summary, "ingested"), f"{run_dir.name}.summary.ingested", minimum=0)
    receipts_dir = run_dir / "receipts"
    if not receipts_dir.is_dir():
        raise AnalysisError(f"{run_dir.name}: missing receipts directory")
    receipts = sorted(receipts_dir.glob("*.json"), key=lambda path: path.name)
    if len(receipts) != ingested:
        raise AnalysisError(f"{run_dir.name}: receipt count {len(receipts)} != ingested {ingested}")
    report_hashes = {item["matchId"]: item["payloadHash"] for item in backend_reports}
    receipt_ids: set[str] = set()
    for path in receipts:
        binding = _json(path, f"{run_dir.name}/{path.relative_to(run_dir)}")
        match_id = _guid_text(_get(binding, "matchId", "MatchId"), "receipt.matchId")
        digest = _hash(_get(binding, "hash", "Hash"), "receipt.hash")
        if match_id in receipt_ids or match_id not in report_hashes or report_hashes[match_id] != digest:
            raise AnalysisError(f"{run_dir.name}: receipt/report reconciliation failed for {match_id}")
        receipt_ids.add(match_id)
        _guid_text(_get(binding, "worker", "Worker", "workerId", "WorkerId"), "receipt.worker")
        _guid_text(_get(binding, "incarnation", "Incarnation"), "receipt.incarnation")
    if receipt_ids != report_ids:
        raise AnalysisError(f"{run_dir.name}: receipt match IDs do not match backend reports")
    outbox_dir = run_dir / "outbox"
    if not outbox_dir.is_dir():
        raise AnalysisError(f"{run_dir.name}: missing outbox directory")
    outbox_files = sorted(item for item in outbox_dir.iterdir() if item.name != ".owner")
    if outbox_files:
        raise AnalysisError(f"{run_dir.name}: outbox retains {len(outbox_files)} non-owner files")
    worker_dirs = sorted(
        (item for item in run_dir.iterdir() if item.is_dir() and item.name.startswith("worker-")),
        key=lambda path: path.name,
    )
    leftover: list[str] = []
    for worker_dir in worker_dirs:
        for path in worker_dir.rglob("*"):
            if not path.is_file():
                continue
            if path.name.endswith(".fpdemo") or path.name.endswith(".telemetry.json") or "/reports/" in path.as_posix():
                leftover.append(str(path.relative_to(run_dir)))
    if leftover:
        raise AnalysisError(f"{run_dir.name}: acknowledged artifact files remain: {', '.join(sorted(leftover))}")
    return {"reports": len(backend_reports), "receipts": len(receipts), "outboxFiles": len(outbox_files),
            "leftoverArtifacts": 0}


def _analyze_run(root: Path, run: dict[str, Any], workload_seconds: int, logical_cpu_count: int) -> dict[str, Any]:
    run_dir = root / run["id"]
    if not run_dir.is_dir():
        raise AnalysisError(f"{run['id']}: run directory is missing")
    summary = _json(_required_file(run_dir / "summary.json", "summary.json"), f"{run['id']}/summary.json")
    if _get(summary, "kind") != "complete":
        raise AnalysisError(f"{run['id']}: summary is not complete")
    actual_scenario = _object(_get(summary, "scenario"), f"{run['id']}.summary.scenario")
    scenario_shape = (
        _int(_get(actual_scenario, "matchesPerWorker", "MatchesPerWorker"), "summary.scenario.matchesPerWorker", minimum=1),
        _int(_get(actual_scenario, "workers", "Workers"), "summary.scenario.workers", minimum=1),
        _int(_get(actual_scenario, "lanes", "Lanes"), "summary.scenario.lanes", minimum=1),
        _normalise_roster(_get(actual_scenario, "roster", "Roster"), "summary.scenario.roster"),
    )
    expected_shape = (run["matchesPerWorker"], run["workers"], run["lanes"], run["roster"])
    if scenario_shape != expected_shape:
        raise AnalysisError(f"{run['id']}: stale summary scenario")
    for name in ("requestedSeconds", "workloadSeconds"):
        if _int(_get(summary, name), f"{run['id']}.summary.{name}", minimum=1) != workload_seconds:
            raise AnalysisError(f"{run['id']}: summary {name} does not match manifest workload")
    if not _bool(_get(summary, "passed"), f"{run['id']}.summary.passed"):
        raise AnalysisError(f"{run['id']}: summary passed=false")
    events, trend, critical = _parse_metrics(run_dir)
    summary_events = [event for event in events if _get(event, "kind") == "complete"]
    if len(summary_events) != 1 or _canonical(summary_events[0]) != _canonical(summary):
        raise AnalysisError(f"{run['id']}: metrics summary does not match summary.json")
    samples = [event for event in events if _get(event, "kind") == "sample"]
    active_samples = [event for event in samples if _number(_get(event, "elapsed"), "sample.elapsed", minimum=0) <= workload_seconds]
    if not active_samples:
        raise AnalysisError(f"{run['id']}: no active sample at or before workloadSeconds")
    starts = [event for event in events if _get(event, "kind") == "lobby_started"]
    terminals = [event for event in critical if _get(event, "kind") == "terminal"]
    if not terminals:
        raise AnalysisError(f"{run['id']}: no terminal events")
    start_ids = {_guid_text(_get(event, "MatchId", "matchId", "active.MatchId"), "lobby_started.MatchId") for event in starts}
    terminal_ids: set[str] = set()
    terminal_counts: Counter[str] = Counter()
    for event in terminals:
        match_id = _guid_text(_get(event, "matchId", "MatchId"), "terminal.matchId")
        if match_id in terminal_ids:
            raise AnalysisError(f"{run['id']}: duplicate terminal {match_id}")
        terminal_ids.add(match_id)
        status = _text(_get(event, "status"), "terminal.status")
        terminal_counts[status] += 1
        _guid_text(_get(event, "workerId", "WorkerId"), "terminal.workerId")
        _guid_text(_get(event, "incarnation", "Incarnation"), "terminal.incarnation")
    created = _int(_get(summary, "created"), f"{run['id']}.summary.created", minimum=1)
    if created != len(starts) or created != len(terminals) or start_ids != terminal_ids:
        raise AnalysisError(f"{run['id']}: created/terminal reconciliation failed")
    summary_terminals = _object(_get(summary, "terminals"), f"{run['id']}.summary.terminals")
    normalized_summary_terminals = {str(key): _int(value, f"{run['id']}.summary.terminals.{key}", minimum=0)
                                    for key, value in summary_terminals.items()}
    if dict(terminal_counts) != normalized_summary_terminals:
        raise AnalysisError(f"{run['id']}: summary terminal counts do not match critical events")
    for field, status in (("completed", "completed"), ("interrupted", "interrupted"), ("failures", "failed")):
        if _int(_get(summary, field), f"{run['id']}.summary.{field}", minimum=0) != terminal_counts.get(status, 0):
            raise AnalysisError(f"{run['id']}: summary {field} does not match terminals")
    backend = _object(_get(summary, "backend"), f"{run['id']}.summary.backend")
    backend_reports_raw = _list(_get(backend, "reports", "Reports"), f"{run['id']}.summary.backend.reports")
    backend_reports: list[dict[str, Any]] = []
    report_ids: set[str] = set()
    completed_ids = {event["matchId"] for event in (
        {"matchId": _guid_text(_get(item, "matchId", "MatchId"), "terminal.matchId"),
         "status": _get(item, "status")} for item in terminals
    ) if event["status"] == "completed"}
    for report in backend_reports_raw:
        report = _object(report, f"{run['id']}.backend.report")
        match_id = _guid_text(_get(report, "matchId", "MatchId"), "backend.report.matchId")
        if match_id in report_ids or match_id not in completed_ids:
            raise AnalysisError(f"{run['id']}: backend report match ID is not a unique completed terminal")
        digest = _hash(_get(report, "payloadHash", "PayloadHash"), "backend.report.payloadHash")
        bytes_value = _int(_get(report, "bytes", "Bytes"), "backend.report.bytes", minimum=1)
        report_ids.add(match_id)
        backend_reports.append({"matchId": match_id, "payloadHash": digest, "bytes": bytes_value})
    persisted = _int(_get(backend, "persistedReports", "PersistedReports"), f"{run['id']}.backend.persistedReports", minimum=0)
    ingested = _int(_get(summary, "ingested"), f"{run['id']}.summary.ingested", minimum=0)
    mismatches = _int(_get(backend, "payloadHashMismatches", "PayloadHashMismatches"), f"{run['id']}.backend.payloadHashMismatches", minimum=0)
    if persisted != ingested or persisted != len(backend_reports) or ingested != len(completed_ids) or mismatches != 0:
        raise AnalysisError(f"{run['id']}: report/backend reconciliation failed")
    outbox = _object(_get(summary, "outbox"), f"{run['id']}.summary.outbox")
    for name in ("durablePending", "queuedPending", "quarantined"):
        if _int(_get(outbox, name, name[0].upper() + name[1:]), f"{run['id']}.outbox.{name}", minimum=0) != 0:
            raise AnalysisError(f"{run['id']}: outbox has pending or quarantined reports")
    artifacts_raw = [event for event in events if _get(event, "kind") == "artifact"]
    artifacts = _artifact_observation(artifacts_raw, workload_seconds)
    for kind in ("replay", "telemetry"):
        if artifacts[kind]["count"] != len(completed_ids):
            raise AnalysisError(f"{run['id']}: {kind} artifact count does not match completed reports")
        if {item["matchId"] for item in artifacts[kind]["raw"]} != completed_ids:
            raise AnalysisError(f"{run['id']}: {kind} artifact match IDs do not reconcile")
    file_reconciliation = _reconcile_files(run_dir, summary, backend_reports, report_ids)

    worker_records: dict[tuple[str, str], list[dict[str, Any]]] = OrderedDict()
    lane_records: dict[tuple[str, str, int], list[dict[str, Any]]] = OrderedDict()
    for sample_index, sample in enumerate(samples):
        elapsed = _number(_get(sample, "elapsed"), f"{run['id']}.sample.elapsed", minimum=0)
        workers = _list(_get(sample, "workers"), f"{run['id']}.sample.workers")
        seen_workers: set[tuple[str, str]] = set()
        for worker_index, worker in enumerate(workers):
            parsed, lanes = _parse_worker_sample(_object(worker, f"{run['id']}.sample.workers[{worker_index}]"), elapsed,
                                                  f"{run['id']}.sample.workers[{worker_index}]")
            worker_key = (parsed["workerId"], parsed["incarnation"])
            if worker_key in seen_workers:
                raise AnalysisError(f"{run['id']}: duplicate Worker identity in one sample")
            seen_workers.add(worker_key)
            if parsed["health"] is None or parsed.get("diagnostics") is None:
                continue
            worker_records.setdefault(worker_key, []).append(parsed)
            for lane in lanes:
                lane_key = (*worker_key, lane["laneId"])
                lane_records.setdefault(lane_key, []).append({"elapsed": elapsed, **lane,
                                                               "workerId": parsed["workerId"], "incarnation": parsed["incarnation"]})
    active_worker_groups: list[dict[str, Any]] = []
    for key in sorted(lane_records):
        records = lane_records[key]
        active = [record for record in records if record["elapsed"] <= workload_seconds]
        active_worker_groups.append(_timeline_lane(records, active, f"{key[0]}/{key[1]}/lane-{key[2]}"))
    if not active_worker_groups:
        raise AnalysisError(f"{run['id']}: no active Worker lane diagnostics")
    workers_output: list[dict[str, Any]] = []
    for key in sorted(worker_records):
        records = worker_records[key]
        active = [record for record in records if record["elapsed"] <= workload_seconds]
        lane_groups = [group for group in active_worker_groups if (group["workerId"], group["incarnation"]) == key]
        workers_output.append(_timeline_worker(records, active, workload_seconds, lane_groups))
    trend_worker_process: list[dict[str, Any]] = []
    process_records: dict[tuple[str, str], list[dict[str, Any]]] = OrderedDict()
    for point in trend:
        elapsed = _number(_get(point, "elapsed"), f"{run['id']}.trend.elapsed", minimum=0)
        if elapsed > workload_seconds:
            continue
        for process in _list(_get(point, "processes"), f"{run['id']}.trend.processes"):
            process = _object(process, f"{run['id']}.trend.process")
            if _get(process, "role") != "Worker":
                continue
            worker_id = _guid_text(_get(process, "workerId", "WorkerId"), "trend.workerId")
            incarnation = _guid_text(_get(process, "incarnation", "Incarnation"), "trend.incarnation")
            process_value = _object(_get(process, "process"), "trend.process.value")
            cpu = _number(_get(process_value, "cpuSeconds", "CpuSeconds"), "trend.process.CpuSeconds", minimum=0)
            working = _int(_get(process_value, "workingSet64", "WorkingSet64"), "trend.process.WorkingSet64", minimum=0)
            process_records.setdefault((worker_id, incarnation), []).append({"elapsed": elapsed, "cpuSeconds": cpu, "workingSetBytes": working})
    for key in sorted(process_records):
        records = process_records[key]
        if len(records) < 1:
            continue
        delta_cpu = records[-1]["cpuSeconds"] - records[0]["cpuSeconds"]
        if delta_cpu < 0:
            raise AnalysisError(f"{run['id']}: trend process CPU counter regressed")
        duration = max(0.0, records[-1]["elapsed"] - records[0]["elapsed"])
        trend_worker_process.append({"workerId": key[0], "incarnation": key[1], "sampleCount": len(records),
                                     "peakWorkingSetBytes": max(record["workingSetBytes"] for record in records),
                                     "cpuSecondsDelta": _round_number(delta_cpu),
                                     "cpuPercentOfLogicalCpu": None if duration <= 0 else _round_number(delta_cpu / duration / logical_cpu_count * 100)})
    # The Worker diagnostics are the authoritative normalized CPU values.  The
    # process trend is retained as a second, independently observable series.
    for item in trend_worker_process:
        duration = None
        records = process_records[(item["workerId"], item["incarnation"])]
        if len(records) > 1:
            duration = max(0.0, records[-1]["elapsed"] - records[0]["elapsed"])
        item["cpuPercentOfLogicalCpu"] = None if not duration else _round_number(item["cpuSecondsDelta"] / duration / logical_cpu_count * 100)

    observed = {
        "activeSampleCount": len(active_samples),
        "activeElapsedSeconds": _round_number(max(_number(_get(item, "elapsed"), "sample.elapsed", minimum=0) for item in active_samples)
                                               - min(_number(_get(item, "elapsed"), "sample.elapsed", minimum=0) for item in active_samples)),
        "workers": workers_output,
        "workerGroups": active_worker_groups,
        "counterDeltasByWorkerIncarnation": [
            {"workerId": item["workerId"], "incarnation": item["incarnation"],
             "gc": item["gc"]["activeWindow"]["delta"],
             "network": item["network"]["activeWindow"]["delta"],
             "scheduler": item["scheduler"]["activeWindow"]["delta"]}
            for item in workers_output
        ],
        "processTrend": trend_worker_process,
        "artifacts": artifacts,
        "reconciliation": {"created": created, "completed": len(completed_ids),
                            "terminals": len(terminals), "reports": file_reconciliation["reports"],
                            **file_reconciliation},
    }
    return {"id": run["id"], "shape": {"matchesPerWorker": run["matchesPerWorker"], "workers": run["workers"],
                                         "lanes": run["lanes"], "family": run["family"]},
            "roster": {"players": run["roster"][0], "bots": run["roster"][1], "observers": run["roster"][2]},
            "valid": True, "observed": observed,
            "reconciliation": {"summary": True, "terminals": True, "reports": True, "outbox": True,
                                "artifacts": True, "metricsSegments": True}}


def _shape_observed(rows: list[dict[str, Any]], workload_seconds: int) -> dict[str, Any]:
    workers = [worker for row in rows for worker in row["observed"]["workers"]]
    lanes = [lane for row in rows for lane in row["observed"]["workerGroups"]]
    artifacts = [row["observed"]["artifacts"] for row in rows]
    return {
        "rosterCount": len(rows),
        "completedMatches": sum(row["observed"]["reconciliation"]["completed"] for row in rows),
        "activeSamples": sum(row["observed"]["activeSampleCount"] for row in rows),
        "peakCpuPercent": max(worker["cpu"]["peakPercent"] for worker in workers),
        "peakManagedHeapBytes": max(worker["managedHeapBytes"]["peak"] for worker in workers),
        "peakWorkingSetBytes": max(worker["workingSetBytes"]["peak"] for worker in workers),
        "worstP95Milliseconds": max(lane["latencyMilliseconds"]["p95Worst"] for lane in lanes),
        "worstP99Milliseconds": max(lane["latencyMilliseconds"]["p99Worst"] for lane in lanes),
        "worstMaxMilliseconds": max(lane["latencyMilliseconds"]["maxWorst"] for lane in lanes),
        "replayBytes": sum(item["replay"]["bytes"] for item in artifacts),
        "telemetryBytes": sum(item["telemetry"]["bytes"] for item in artifacts),
        "replayBytesPerWorkloadSecond": _round_number(sum(item["replay"]["bytes"] for item in artifacts) /
                                                       max(1, workload_seconds)),
        "telemetryBytesPerWorkloadSecond": _round_number(sum(item["telemetry"]["bytes"] for item in artifacts) /
                                                          max(1, workload_seconds)),
    }


def analyze_capacity(root: str | Path) -> dict[str, Any]:
    """Read and analyze a matrix root, returning deterministic JSON data."""
    root_path = Path(root).resolve()
    if not root_path.is_dir():
        raise AnalysisError(f"matrix output root is not a directory: {root_path}")
    provenance, runs, workload_seconds = _parse_manifest(root_path)
    logical_cpu_count = _int(provenance.get("logicalCpuCount", 1), "provenance.logicalCpuCount", minimum=1)
    analyzed = [_analyze_run(root_path, run, workload_seconds, logical_cpu_count) for run in runs]
    shapes: list[dict[str, Any]] = []
    offset = 0
    for matches, workers, lanes, family in EXPECTED_SHAPES:
        group = analyzed[offset:offset + len(EXPECTED_ROSTERS)]
        offset += len(EXPECTED_ROSTERS)
        if len(group) != len(EXPECTED_ROSTERS):
            raise AnalysisError("internal fixed matrix grouping error")
        shapes.append({"shape": {"matchesPerWorker": matches, "workers": workers, "lanes": lanes, "family": family},
                       "valid": all(row["valid"] for row in group), "observed": _shape_observed(group, workload_seconds),
                       "rosters": group})
    return {
        "analyzer": "A23",
        "format": 1,
        "valid": True,
        "matrix": {"expectedRows": len(EXPECTED_ROWS), "rows": len(analyzed), "workloadSeconds": workload_seconds},
        "provenance": provenance,
        "shapes": shapes,
    }


def write_compact_json(value: dict[str, Any], path: str | Path) -> None:
    destination = Path(path)
    payload = json.dumps(value, ensure_ascii=True, allow_nan=False, sort_keys=True, separators=(",", ":")) + "\n"
    try:
        destination.write_text(payload, encoding="utf-8")
    except OSError as error:
        raise AnalysisError(f"cannot write {destination}: {error}") from error


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", nargs="?", type=Path, help="capacity matrix output root")
    parser.add_argument("--input", dest="input_root", type=Path, help="capacity matrix output root")
    parser.add_argument("--output", required=True, type=Path, help="compact JSON report path")
    args = parser.parse_args(argv)
    root = args.input_root or args.root
    if root is None:
        parser.error("a matrix output root is required")
    try:
        report = analyze_capacity(root)
        write_compact_json(report, args.output)
    except (AnalysisError, OSError) as error:
        print(f"analyze-capacity: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
