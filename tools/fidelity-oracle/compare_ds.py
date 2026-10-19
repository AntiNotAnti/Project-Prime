#!/usr/bin/env python3
"""Original-game trace comparison for the existing normalized fidelity oracle.

This consumes FidelityResult JSON; it does not simulate gameplay or record baselines.
Only derived numeric CSV may be supplied. Keep original captures outside the repo.
"""
from __future__ import annotations

import argparse
import csv
from decimal import Decimal, DecimalException, ROUND_HALF_UP, localcontext
import hashlib
import io
import json
from pathlib import Path
import re
import sys

UPSTREAM_REVISION = "c16a907b3658c2fd4079e394a73711596a85caee"
AMHE1_SHA1 = "a8679d4f8dcb445cbe946a424241ef44901b0caf"
LIMIT = 8 * 1024 * 1024
MAX_TICKS = 100000
CADENCE_SCENARIO = "weapon.powerbeam.cadence"
XYZ = tuple(f"position4096.{a}" for a in "xyz")
VXZ = ("velocity4096.x", "velocity4096.z")
# Required observables prevent a case from passing by comparing an unrelated field.
# A missing producer is a capture gap, not permission to synthesize its output.
CASES = {
    "movement.jump": ("movement.jump", ("position4096.y", "velocity4096.y"), "Current F2 Proving Ground setup differs from Second Hunt's landing-site route."),
    "movement.air-friction": ("movement.air-control", VXZ, "Dedicated release/coasting input route required."),
    "movement.walk-friction": ("movement.walk", VXZ, "Dedicated release/coasting input route required."),
    "camera.strafe-tilt": (None, ("tilt4096.strafe", "tilt4096.walk"), "F2 does not capture camera tilt."),
    "alt.samus.roll": ("alt.samus", XYZ + VXZ, "Dedicated flat-ground acceleration/coasting route required."),
    "alt.samus.boost": ("alt.samus", VXZ + ("boostDamage",), "DS boost route required."),
    "camera.follow": (None, XYZ + tuple(f"cameraPosition4096.{a}" for a in "xyz"), "F2 does not capture camera follow."),
    "movement.slope": ("movement.slope", XYZ + VXZ, "Match the DS incline and initial state; current F2 selects a different native surface."),
    "movement.collision-corner": ("movement.collision-corner", XYZ, "Match the DS corner and sustained-input route."),
    "weapon.cadence": (CADENCE_SCENARIO, ("weapon", "shotsFired"), "Mac F2 Power Beam producer validated at 60/120/144/240 Hz; matching original DS counter trace remains absent. Other weapons remain open."),
    "campaign.boss-transition": (None, ("bossState", "bossTransitionCount"), "No boss transition F2 producer."),
    "sound.cues": (None, ("soundCueId", "soundCueCount"), "No sound cue F2 producer; ordered within-tick audio still needs separate inspection."),
    "campaign.scripted-pickup": (None, ("pickupCount", "health", "ammoMissile", "ammoUa"), "No scripted pickup F2 producer."),
}
EVENT_COUNTS = {"shotsFired", "bossTransitionCount", "soundCueCount", "pickupCount"}


class TraceError(ValueError):
    pass


def require(ok, message):
    if not ok:
        raise TraceError(message)


def obj(value, keys, label):
    require(isinstance(value, dict) and set(value) == set(keys.split()), f"{label}: missing or unknown properties")
    return value


def integer(value, low, high, label):
    require(type(value) is int and low <= value <= high, f"{label}: integer outside declared range")
    return value


def label(value, name, limit=256):
    require(isinstance(value, str) and 0 < len(value) <= limit and value.strip() == value
            and not any(ord(c) < 32 for c in value), f"{name}: invalid text")
    return value


def digest(value, size, name):
    require(isinstance(value, str) and re.fullmatch(f"[0-9a-f]{{{size}}}", value) is not None,
            f"{name}: expected full lowercase hash")
    require(value != "0" * size, f"{name}: placeholder hash")
    return value


def no_duplicates(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "JSON: duplicate property")
        result[key] = value
    return result


def read_bytes(path):
    try:
        with Path(path).open("rb") as handle:
            data = handle.read(LIMIT + 1)
    except OSError:
        raise TraceError("Required local capture is unavailable; original accuracy remains NOT_VERIFIED.") from None
    require(0 < len(data) <= LIMIT, "Capture is empty or exceeds 8 MiB")
    return data


def read_json(data):
    def json_integer(value):
        require(len(value) <= 20, "JSON: integer exceeds normalized int64")
        return integer(int(value), -(2**63), 2**63 - 1, "JSON integer")

    try:
        return json.loads(data.decode("utf-8"), object_pairs_hook=no_duplicates,
                          parse_int=json_integer,
                          parse_constant=lambda _: (_ for _ in ()).throw(TraceError("JSON: non-finite value")))
    except TraceError:
        raise
    except (UnicodeError, ValueError, RecursionError):
        raise TraceError("Invalid UTF-8 JSON capture") from None


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def safe_canonical_text(value, name):
    # These are the literal-safe characters used by current FidelityResult contracts.
    # Do not guess how a future .NET JSON encoder canonicalizes other characters.
    require(isinstance(value, str) and re.fullmatch(r"[A-Za-z0-9 ._:/-]{1,160}", value) is not None,
            f"{name}: unsupported canonical text")
    return value


def validate_project(result):
    obj(result, "format scenario engineRevision canonicalHash checkpoints", "Project result")
    require(result["format"] == 1 and type(result["format"]) is int, "Unsupported FidelityResult format")
    scenario = obj(result["scenario"], "id version ticks seed content captureInterval tier description", "Project scenario")
    safe_canonical_text(scenario["id"], "scenario.id")
    safe_canonical_text(scenario["content"], "scenario.content")
    label(scenario["description"], "scenario.description", 4096)
    integer(scenario["version"], 1, 2**31 - 1, "scenario.version")
    ticks = integer(scenario["ticks"], 1, MAX_TICKS, "scenario.ticks")
    integer(scenario["seed"], 0, 2**32 - 1, "scenario.seed")
    interval = integer(scenario["captureInterval"], 1, ticks, "scenario.captureInterval")
    require(scenario["tier"] in {"F0", "F1", "F2", "F3", "F4", "F5"}, "Invalid Project evidence tier")
    label(result["engineRevision"], "engineRevision")
    digest(result["canonicalHash"], 64, "canonicalHash")
    points = result["checkpoints"]
    require(isinstance(points, list) and 0 < len(points) <= MAX_TICKS, "Invalid checkpoint count")
    by_tick = {}
    canonical = [scenario["id"], scenario["version"], ticks, scenario["seed"], interval,
                 scenario["content"], scenario["tier"]]
    previous = -1
    for point in points:
        obj(point, "tick values", "Project checkpoint")
        tick = integer(point["tick"], 0, ticks, "checkpoint.tick")
        require(tick > previous, "Project checkpoints must be unique and increasing")
        previous = tick
        values = point["values"]
        require(isinstance(values, dict) and 0 < len(values) <= 256, "Invalid checkpoint values")
        flat = [tick]
        for field in sorted(values):
            safe_canonical_text(field, "checkpoint field")
            integer(values[field], -(2**63), 2**63 - 1, "checkpoint value")
            flat.extend((field, values[field]))
        canonical.append(flat)
        by_tick[tick] = values
    encoded = json.dumps(canonical, separators=(",", ":"), ensure_ascii=True).encode("utf-8")
    require(sha256(encoded) == result["canonicalHash"], "Project canonical hash mismatch")
    if scenario["id"] == CADENCE_SCENARIO:
        validate_cadence_capture(scenario, by_tick)
    return by_tick


def validate_cadence_capture(scenario, points):
    """Validate diagnostic witnesses, never infer original-game acceptance."""
    require(scenario["version"] == 1 and scenario["ticks"] == 180 and scenario["seed"] == 123456
            and scenario["captureInterval"] == 1 and scenario["tier"] == "F2"
            and scenario["content"] == "extracted:AMHE1:MP3 PROVING GROUND:cadence-v1",
            "Cadence producer contract changed")
    require(set(points) == set(range(181)), "Cadence capture needs initial state and every Project frame")
    initial = points[0]
    require(all(initial.get(key) == value for key, value in {
        "capture.simulationHz": 60, "capture.sampleHz": 60, "capture.postStep": 0,
        "capture.balancedMode": 0, "capture.warmupTicks": 120, "capture.diagnosticOnly": 1,
        "capture.originalTraceVerified": 0, "actorSlot": 1, "hunter": 0, "weapon": 0}.items()),
        "Cadence initial-state/sample metadata missing or changed")
    require(all(key in initial for key in ["roomId", "health", "charge", "ammoUa", "ammoMissile", "rng1", "rng2"]
                + [f"{name}4096.{axis}" for name in ("position", "velocity", "facing") for axis in "xyz"]),
            "Cadence initial-state witnesses missing")
    previous = integer(initial.get("shotsFired.counter"), 0, 2**31 - 2, "Cadence initial counter")
    total, old_down, persistent_projectile = 0, False, False
    for tick in range(1, 181):
        point = points[tick]
        require(all(point.get(key) == value for key, value in {
            "capture.simulationHz": 60, "capture.sampleHz": 60, "capture.postStep": 1,
            "capture.balancedMode": 0, "capture.diagnosticOnly": 1,
            "capture.originalTraceVerified": 0, "weapon": 0}.items()),
            "Cadence per-frame sample metadata missing or changed")
        down = ((tick - 1) // 2) % 15 < 6
        require(point.get("input.shootDown") == int(down)
                and point.get("input.shootPressed") == int(down and not old_down)
                and point.get("input.shootReleased") == int(not down and old_down),
                "Cadence input witnesses differ from paired capture route")
        before = integer(point.get("shotsFired.counterBefore"), 0, 2**31 - 2, "Cadence before counter")
        after = integer(point.get("shotsFired.counter"), 0, 2**31 - 2, "Cadence after counter")
        delta = integer(point.get("shotsFired"), 0, 2**31 - 2, "Cadence frame delta")
        require(before == previous and after >= before and delta == after - before,
                "Cadence delta does not match contiguous canonical accepted-shot counter")
        population = integer(point.get("projectiles"), 0, 16, "Cadence projectile population")
        persistent_projectile |= delta == 0 and population > 0
        total += delta
        previous, old_down = after, down
    require(total > 0 and persistent_projectile,
            "Cadence capture must contain accepted shots and distinguish events from projectile population")


def validate_manifest(manifest, allow_synthetic):
    obj(manifest, "format case evidenceKind reference project alignment fields", "Manifest")
    require(type(manifest["format"]) is int and manifest["format"] == 1, "Unsupported comparison format")
    require(manifest["case"] in CASES, "Unknown CPP-07 comparison case")
    synthetic = manifest["evidenceKind"] == "synthetic-protocol-test"
    require(manifest["evidenceKind"] in {"original-game-trace", "synthetic-protocol-test"}, "Invalid evidenceKind")
    require(not synthetic or allow_synthetic, "Synthetic traces require --allow-synthetic and cannot verify original accuracy")
    ds = obj(manifest["reference"], "sha256 captureKind game romSha1 logicHz sampleHz samplePhase logicUpdateParity tool workflowRevision routeSha256 initialStateSha256 room setupSha256 inputsSha256", "Reference metadata")
    project = obj(manifest["project"], "sha256 sourceRevision sourceStateSha256 binarySha256 simulationHz balancedMode protocolVersion replayFormatVersion replayFingerprint collisionSha256 room setupSha256 inputsSha256", "Project metadata")
    for side, value in (("reference", ds), ("project", project)):
        for key in ("sha256", "setupSha256", "inputsSha256"):
            digest(value[key], 64, f"{side}.{key}")
        label(value["room"], f"{side}.room")
    require(ds["captureKind"] == "synthetic" if synthetic else ds["captureKind"] in {"emulator", "physical-device"}, "Capture kind disagrees with evidenceKind")
    require(ds["game"] == "AMHE1" and ds["romSha1"] == AMHE1_SHA1, "Only the source-audited AMHE1 original layout is supported")
    require(type(ds["logicHz"]) is int and ds["logicHz"] == 30 and type(ds["sampleHz"]) is int and ds["sampleHz"] == 60, "Reference must declare 30 Hz logic and 60 Hz post-display-frame samples")
    require(ds["samplePhase"] == "post-step", "Only post-step DS samples are comparable")
    integer(ds["logicUpdateParity"], 0, 1, "reference.logicUpdateParity")
    tool = obj(ds["tool"], "name version core", "Reference tool")
    for key in tool:
        label(tool[key], f"reference.tool.{key}")
    digest(ds["workflowRevision"], 40, "reference.workflowRevision")
    for key in ("routeSha256", "initialStateSha256"):
        digest(ds[key], 64, f"reference.{key}")
    for key, size in (("sourceRevision", 40), ("sourceStateSha256", 64), ("binarySha256", 64), ("replayFingerprint", 64), ("collisionSha256", 64)):
        digest(project[key], size, f"project.{key}")
    require(type(project["simulationHz"]) is int and project["simulationHz"] == 60, "Project simulation must be 60 Hz")
    require(project["balancedMode"] is False, "Vanilla comparison requires Balanced Mode explicitly false")
    integer(project["protocolVersion"], 1, 2**31 - 1, "project.protocolVersion")
    integer(project["replayFormatVersion"], 1, 2**31 - 1, "project.replayFormatVersion")
    for key in ("room", "setupSha256", "inputsSha256"):
        require(ds[key] == project[key], f"Reference/Project {key} mismatch; no automatic spatial or input alignment")
    alignment = obj(manifest["alignment"], "dsFrameColumn firstDsFrame firstProjectTick logicTicks", "Alignment")
    safe_canonical_text(alignment["dsFrameColumn"], "dsFrameColumn")
    integer(alignment["firstDsFrame"], 1, 2 * MAX_TICKS, "firstDsFrame")
    require(alignment["firstDsFrame"] % 2 == ds["logicUpdateParity"], "DS endpoint phase disagrees with declared logic update parity")
    integer(alignment["firstProjectTick"], 2, MAX_TICKS, "firstProjectTick")
    integer(alignment["logicTicks"], 2, MAX_TICKS, "logicTicks")
    fields = manifest["fields"]
    require(isinstance(fields, list) and 0 < len(fields) <= 256, "Invalid comparison fields")
    targets, columns = set(), set()
    for field in fields:
        obj(field, "dsColumn projectField encoding projectSampling absoluteTolerance rationale", "Field contract")
        column = safe_canonical_text(field["dsColumn"], "dsColumn")
        target = safe_canonical_text(field["projectField"], "projectField")
        require(column != alignment["dsFrameColumn"] and column not in columns and target not in targets, "Duplicate comparison field or frame column")
        columns.add(column)
        targets.add(target)
        require(field["encoding"] in {"fixed4096", "integer"}, "Unknown DS field encoding")
        require(("4096" in target) == (field["encoding"] == "fixed4096"), "Field encoding disagrees with normalized Project observable")
        require(field["projectSampling"] in {"endpoint", "sum-pair"}, "Unknown Project sampling")
        require(field["projectSampling"] != "sum-pair" or field["encoding"] == "integer", "sum-pair requires integer event counts")
        if target in EVENT_COUNTS:
            require(field["projectSampling"] == "sum-pair", "Event counters require per-60-Hz-frame deltas summed over both frames")
        integer(field["absoluteTolerance"], 0, 2**63 - 1, "absoluteTolerance")
        label(field["rationale"], "tolerance rationale", 1024)
        require(field["encoding"] == "fixed4096" or field["absoluteTolerance"] == 0, "Discrete integer observables require exact agreement")
    required = set(CASES[manifest["case"]][1])
    require(required <= targets, "Case is missing required observables: " + ", ".join(sorted(required - targets)))
    return synthetic


def read_ds(data, manifest):
    try:
        reader = csv.reader(io.StringIO(data.decode("utf-8"), newline=""), strict=True)
        header = next(reader)
        require(0 < len(header) <= 256 and len(set(header)) == len(header), "Missing or duplicate DS CSV header")
        needed = {manifest["alignment"]["dsFrameColumn"]} | {f["dsColumn"] for f in manifest["fields"]}
        require(needed <= set(header), "DS CSV is missing required columns")
        index = header.index(manifest["alignment"]["dsFrameColumn"])
        rows = {}
        previous = None
        for row in reader:
            require(len(row) == len(header), "DS CSV row has missing or extra cells")
            require(re.fullmatch(r"[0-9]+", row[index]) is not None, "Invalid DS frame number")
            frame = integer(int(row[index]), 1, 2 * MAX_TICKS, "DS frame")
            require(previous is None or frame == previous + 1, "DS CSV must retain every display frame in order; gaps, duplicates and prefiltered logic ticks are rejected")
            previous = frame
            rows[frame] = dict(zip(header, row))
            require(len(rows) <= 2 * MAX_TICKS, "Too many DS rows")
        require(bool(rows), "DS CSV has no data")
        return rows
    except (UnicodeError, csv.Error, StopIteration):
        raise TraceError("Invalid UTF-8 DS CSV capture") from None


def normalize_ds(text, encoding):
    require(len(text) <= 64 and text.strip() == text and bool(text), "Invalid numeric DS cell")
    require(re.fullmatch(r"[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?", text) is not None, "Invalid numeric DS cell")
    try:
        number = Decimal(text)
        require(number.is_finite(), "DS cell is non-finite")
        require(-(2**63) <= number <= 2**63 - 1, "DS cell overflows normalized int64")
        if encoding == "fixed4096":
            with localcontext() as context:
                context.prec = 100
                number = (number * 4096).to_integral_value(rounding=ROUND_HALF_UP)
        else:
            require(number == number.to_integral_value(), "Integer DS field is fractional")
        require(-(2**63) <= number <= 2**63 - 1, "DS cell overflows normalized int64")
        return int(number)
    except (DecimalException, OverflowError):
        raise TraceError("Invalid numeric DS cell") from None


def compare(manifest, ds_data, project_data, allow_synthetic=False):
    synthetic = validate_manifest(manifest, allow_synthetic)
    require(sha256(ds_data) == manifest["reference"]["sha256"], "DS CSV artifact hash mismatch")
    require(sha256(project_data) == manifest["project"]["sha256"], "Project JSON artifact hash mismatch")
    project = read_json(project_data)
    points = validate_project(project)
    require(project["scenario"]["tier"] == "F2", "DS comparison requires actual-engine F2 data; F0/F1 are protocol evidence only")
    require(project["engineRevision"] == manifest["project"]["sourceRevision"], "Project capture revision differs from manifest")
    rows = read_ds(ds_data, manifest)
    alignment = manifest["alignment"]
    count, first_ds, first_project = (alignment[k] for k in ("logicTicks", "firstDsFrame", "firstProjectTick"))
    last_ds, last_project = first_ds + 2 * (count - 1), first_project + 2 * (count - 1)
    require(last_ds <= 2 * MAX_TICKS and last_project <= project["scenario"]["ticks"], "Comparison window exceeds capture contract")
    fields, first_difference = [], None
    for field in manifest["fields"]:
        errors, divergent, first = [], 0, None
        target = field["projectField"]
        for i in range(count):
            ds_frame, project_tick = first_ds + 2 * i, first_project + 2 * i
            require(ds_frame in rows and ds_frame - 1 in rows, "DS window lacks a complete two-display-frame logic interval")
            require(project_tick in points and target in points[project_tick], "Project window lacks a required checkpoint or observable")
            expected = normalize_ds(rows[ds_frame][field["dsColumn"]], field["encoding"])
            actual = points[project_tick][target]
            if field["projectSampling"] == "sum-pair":
                require(project_tick - 1 in points and target in points[project_tick - 1], "Event comparison requires both 60 Hz frame deltas; endpoint-only captures are insufficient")
                actual += points[project_tick - 1][target]
                require(expected >= 0 and points[project_tick][target] >= 0 and points[project_tick - 1][target] >= 0, "Event deltas cannot be negative")
            error = abs(actual - expected)
            errors.append(error)
            if error > field["absoluteTolerance"]:
                divergent += 1
                difference = {"logicTick": i + 1, "dsFrame": ds_frame, "projectTick": project_tick,
                              "field": target, "expected": expected, "actual": actual,
                              "absoluteError": error, "absoluteTolerance": field["absoluteTolerance"]}
                first = first or difference
                if first_difference is None or (difference["logicTick"], target) < (first_difference["logicTick"], first_difference["field"]):
                    first_difference = difference
        fields.append({"field": target, "encoding": field["encoding"], "projectSampling": field["projectSampling"],
                       "absoluteTolerance": field["absoluteTolerance"], "rationale": field["rationale"],
                       "samples": count, "maxAbsoluteError": max(errors),
                       "meanAbsoluteError": str(Decimal(sum(errors)) / count),
                       "divergentSamples": divergent, "firstDifference": first})
    matched = first_difference is None
    status = ("SYNTHETIC_PROTOCOL_MATCH" if matched else "SYNTHETIC_PROTOCOL_DIVERGED") if synthetic else ("ORIGINAL_TRACE_MATCH" if matched else "ORIGINAL_TRACE_DIVERGED")
    return {"format": 1, "case": manifest["case"], "status": status,
            "originalAccuracy": "NOT_VERIFIED" if synthetic else "COMPARED_DECLARED_CAPTURE",
            "releaseAcceptance": "NOT_VERIFIED",
            "acceptanceScope": "Declared numeric fields and mapped window only; capture provenance requires review.",
            "evidenceKind": manifest["evidenceKind"], "captureKind": manifest["reference"]["captureKind"],
            "comparisonWritesRuntimeState": False, "projectScenario": project["scenario"]["id"],
            "projectCanonicalHash": project["canonicalHash"],
            "manifestSha256": sha256(json.dumps(manifest, sort_keys=True, separators=(",", ":")).encode()),
            "referenceSha256": manifest["reference"]["sha256"], "projectSha256": manifest["project"]["sha256"],
            "referenceIdentity": {k: manifest["reference"][k] for k in ("captureKind", "game", "romSha1", "tool", "workflowRevision", "routeSha256", "initialStateSha256", "logicUpdateParity")},
            "setupIdentity": {k: manifest["project"][k] for k in ("room", "setupSha256", "inputsSha256")},
            "projectIdentity": {k: manifest["project"][k] for k in ("sourceRevision", "sourceStateSha256", "binarySha256", "protocolVersion", "replayFormatVersion", "replayFingerprint", "collisionSha256", "balancedMode")},
            "alignment": {"logicHz": 30, "projectSimulationHz": 60, "logicTicks": count,
                          "firstDsFrame": first_ds, "lastDsFrame": last_ds,
                          "firstProjectTick": first_project, "lastProjectTick": last_project,
                          "automaticAlignment": False},
            "firstDifference": first_difference, "fields": fields}


def coverage():
    return {"format": 1, "originalAccuracy": "NOT_VERIFIED", "committedOriginalTraces": 0,
            "upstreamRevision": UPSTREAM_REVISION,
            "cases": [{"case": key, "status": "NOT_VERIFIED", "existingProjectScenario": value[0],
                       "requiredObservables": value[1], "captureGap": value[2],
                       "requiredEvidence": "Local DS trace and matching actual-engine capture with reviewed setup and tolerance manifest."}
                      for key, value in CASES.items()]}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("status", help="List source-audited capture gaps; does not claim DS acceptance")
    command = commands.add_parser("compare", help="Compare local derived DS CSV to existing FidelityResult JSON")
    command.add_argument("manifest")
    command.add_argument("--ds-csv", required=True)
    command.add_argument("--project-json", required=True)
    command.add_argument("--allow-synthetic", action="store_true", help="Protocol tests only; original accuracy remains NOT_VERIFIED")
    args = parser.parse_args(argv)
    try:
        if args.command == "status":
            report, code = coverage(), 0
        else:
            manifest = read_json(read_bytes(args.manifest))
            report = compare(manifest, read_bytes(args.ds_csv), read_bytes(args.project_json), args.allow_synthetic)
            code = 1 if report["firstDifference"] else 0
    except (TraceError, TypeError, KeyError) as error:
        report, code = {"format": 1, "status": "NOT_COMPARABLE", "originalAccuracy": "NOT_VERIFIED", "reason": str(error)}, 2
    print(json.dumps(report, indent=2, sort_keys=True))
    return code


if __name__ == "__main__":
    sys.exit(main())
