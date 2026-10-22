#!/usr/bin/env python3
"""Fail-closed identities for existing native/perf/replay fixtures; never runs a game."""
import argparse
import importlib.util
import json
import math
from pathlib import Path
import re
import subprocess

# Reuse the established large-file hashing implementation; no second capture harness.
_spec = importlib.util.spec_from_file_location(
    "campaign_identity", Path(__file__).resolve().parents[1] / "native-render-check/campaign_identity.py")
_identity = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_identity)
sha256 = _identity.sha256

HUNTERS = ("Samus", "Kanden", "Trace", "Sylux", "Noxus", "Spire", "Weavel")
COMPARABLE = (
    "device.machineId", "device.kind", "device.os", "device.cpu", "device.gpu", "device.driver",
    "build.configuration", "build.flags",
    "workload.fixtureVersion", "workload.mode", "workload.room", "workload.mapSha256",
    "workload.replaySha256", "workload.simulationSha256", "workload.simulationHz",
    "workload.players", "workload.hunter", "workload.detail", "workload.width", "workload.height",
    "workload.renderScale", "workload.presentationHz", "workload.runtimeSettings", "workload.seed",
    "workload.warmupSimulationSteps", "workload.measurementSimulationSteps",
    "workload.harness", "workload.timingScope", "workload.profiling", "workload.readback",
    "workload.phase", "workload.roster", "workload.rngSeeds", "workload.measurementStartRng", "workload.measurementEndRng", "workload.inputExecution", "workload.replayExecuted",
)
METRICS = ("frameP50Ms", "frameP95Ms", "frameP99Ms", "allocatedBytes", "gcCollections",
           "residentBytes", "cpuMs", "gpuMs", "gcPauseMs", "directNativeBytes", "inputLatencyMs")


def field(data, dotted):
    for key in dotted.split("."):
        data = data.get(key) if isinstance(data, dict) else None
    return data


def known(value):
    return isinstance(value, str) and value.strip().lower() not in (
        "", "unknown", "unverified", "not_verified", "n/a", "none", "null")


def number(value, minimum=0):
    try:
        return type(value) in (int, float) and math.isfinite(value) and value >= minimum
    except OverflowError:
        return False


def digest(value, length=64):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{%d}" % length, value) is not None


def settings_known(value):
    if isinstance(value, dict):
        return bool(value) and all(known(k) and settings_known(v) for k, v in value.items())
    if isinstance(value, list):
        return bool(value) and all(settings_known(v) for v in value)
    return type(value) is bool or number(value, -float("inf")) or known(value)


def source_lock(repo):
    """Read current source only; this does not assert that a binary was built from it."""
    def git(*args):
        return subprocess.run(["git", "-C", str(repo), *args], check=True,
                              capture_output=True).stdout
    status = git("status", "--porcelain=v1", "--untracked-files=all")
    return {"schemaVersion": 1, "sourceSha": git("rev-parse", "HEAD").decode().strip(),
            "dirty": bool(status), "status": status.decode(),
            "meaning": "Source observation only; not a build receipt or performance/device pass."}


def valid_rng(value):
    return isinstance(value, dict) and set(value) == {"rng1", "rng2"} and all(
        type(n) is int and 0 <= n <= 0xffffffff for n in value.values())


def valid_mvid(value):
    import uuid
    try:
        return isinstance(value, str) and str(uuid.UUID(value)) == value.lower() and uuid.UUID(value).int != 0
    except (ValueError, TypeError, AttributeError):
        return False


def audit_native_result(raw, manifest):
    """Cross-check the existing native fixture's observed fields, rather than trusting copied metrics."""
    errors = []
    receipt = raw.get("observedWorkload")
    if not isinstance(receipt, dict):
        errors.append("observed workload receipt required; older raw reports cannot establish workload execution")
        receipt = {}
    for key, expected in (("schemaVersion", 1), ("eligibleNormalTiming", True), ("failure", None),
                          ("mode", "offline"), ("phase", "entry"),
                          ("inputExecution", "frozen-device-snapshots-with-bot-ai"), ("replayExecuted", False)):
        value = receipt.get(key)
        if key not in receipt or type(value) is not type(expected) or value != expected:
            errors.append("observed workload missing/invalid: " + key)
    for key in ("mode", "phase", "inputExecution", "replayExecuted"):
        if receipt.get(key) != field(manifest, "workload." + key):
            errors.append("observed workload disagrees: " + key)
    roster = receipt.get("startRoster")
    if not isinstance(roster, list) or len(roster) != field(manifest, "workload.players") or not roster:
        errors.append("observed player roster missing/count mismatch")
    else:
        for index, player in enumerate(roster):
            if not isinstance(player, dict) or type(player.get("slot")) is not int or player["slot"] != index or player.get("hunter") not in HUNTERS or type(player.get("bot")) is not bool:
                errors.append("observed player roster invalid")
        if isinstance(roster[0], dict) and roster[0].get("hunter") != field(manifest, "workload.hunter"):
            errors.append("observed slot-zero hunter disagrees")
    if roster != receipt.get("endRoster") or roster != field(manifest, "workload.roster"):
        errors.append("observed roster changed or differs from declared exact roster")
    seeds = receipt.get("seedAssignmentReadback")
    if seeds != field(manifest, "workload.rngSeeds") or not valid_rng(seeds) or seeds.get("rng1") != field(manifest, "workload.seed"):
        errors.append("assigned RNG seed readback missing/disagrees")
    for key in ("measurementStartRng", "measurementEndRng"):
        if not valid_rng(receipt.get(key)) or receipt.get(key) != field(manifest, "workload." + key):
            errors.append("observed RNG state missing/invalid/disagrees: " + key)
    start, end = receipt.get("measurementStartTick"), receipt.get("measurementEndTick")
    if type(start) is not int or type(end) is not int or start < 0 or end <= start or start != field(manifest, "workload.warmupSimulationSteps") or end - start != field(manifest, "workload.measurementSimulationSteps") or end != raw.get("simulationFrames"):
        errors.append("observed measurement tick interval disagrees")
    artifacts = {a.get("role"): a for a in manifest.get("artifacts", []) if isinstance(a, dict)}
    for key, role in (("clientBinary", "client-binary"), ("harnessBinary", "harness-binary")):
        binary = receipt.get(key)
        if not isinstance(binary, dict) or not known(binary.get("name")) or not digest(binary.get("sha256")) or binary.get("sha256") != artifacts.get(role, {}).get("sha256") or not valid_mvid(binary.get("mvid")):
            errors.append("observed binary binding missing/disagrees: " + role)
    if field(receipt, "clientBinary.mvid") != raw.get("assemblyMvid"):
        errors.append("observed client MVID differs from raw loaded assembly")
    if "transitionGc" in raw or raw.get("captures") or raw.get("captureKillFeedLifetimeStabilized") is True:
        errors.append("diagnostic capture/lifecycle run cannot establish normal timing")
    if raw.get("complete") is not True:
        errors.append("raw native result is incomplete")
    expected = {
        "room": field(manifest, "workload.room"),
        "cap": field(manifest, "workload.presentationHz"),
        "framebuffer": [field(manifest, "workload.width"), field(manifest, "workload.height")],
        "renderer": field(manifest, "device.gpu"), "droppedSimulationSteps": 0, "stalls": 0,
    }
    for key, value in expected.items():
        if raw.get(key) != value:
            errors.append("raw native result disagrees: " + key)
    for key in ("shadows", "textureQuality", "lighting", "resolutionScale", "vsync", "effectiveCap"):
        value = field(manifest, "workload.runtimeSettings." + key)
        if value is None or raw.get(key) != value:
            errors.append("raw native settings disagree/missing: " + key)
    if raw.get("resolutionScale") != field(manifest, "workload.renderScale"):
        errors.append("raw native render scale disagrees")
    metrics = {"frameP50Ms": raw.get("frameIntervalP50Ms"),
               "frameP95Ms": raw.get("frameIntervalP95Ms"),
               "frameP99Ms": raw.get("frameIntervalP99Ms"),
               "allocatedBytes": raw.get("allocatedBytes"), "residentBytes": raw.get("workingSetBytes")}
    collections = raw.get("gcCollections")
    metrics["gcCollections"] = sum(collections) if isinstance(collections, list) and len(collections) == 3 and all(
        type(n) is int and n >= 0 for n in collections) else None
    for key, value in metrics.items():
        if value is None or field(manifest, "measurements." + key + ".value") != value:
            errors.append("raw native measurement disagrees/missing: " + key)
    if not type(raw.get("samples")) is int or raw["samples"] <= 0:
        errors.append("raw native result has no timing samples")
    return errors


def audit(data, root):
    """Return every missing/mismatched condition without converting missing values to zero."""
    errors = []
    def require(condition, message):
        if not condition:
            errors.append(message)
    require(type(data.get("schemaVersion")) is int and data["schemaVersion"] == 1, "schemaVersion must be 1")
    for name in ("source.repository", "build.toolchain", "build.command") + tuple(
            n for n in COMPARABLE if n.startswith("device.")):
        require(known(field(data, name)), name + " is missing/unverified")
    require(digest(field(data, "source.sha"), 40), "source.sha must be an exact Git SHA")
    require(field(data, "source.dirty") is False, "source is dirty or cleanliness is unverified")
    require(field(data, "build.sourceSha") == field(data, "source.sha"), "build/source SHA mismatch")
    require(field(data, "build.sourceDirty") is False, "build source cleanliness is unverified")
    require(field(data, "device.kind") == "physical", "physical device evidence required")
    require(field(data, "workload.fixtureVersion") == "cpp00-v2", "unknown canonical fixture version")
    require(field(data, "workload.room") == "MP3 PROVING GROUND", "canonical room required")
    for key in ("mapSha256", "replaySha256", "simulationSha256"):
        require(digest(field(data, "workload." + key)), "workload." + key + " missing/invalid")
    require(field(data, "workload.simulationHz") == 60, "simulation must remain 60 Hz")
    require(field(data, "workload.players") in (2, 4, 8), "canonical player count required")
    require(field(data, "workload.hunter") in HUNTERS, "canonical hunter required")
    require(field(data, "workload.presentationHz") in (60, 120, 144, 240), "canonical output rate required")
    require(field(data, "workload.mode") in ("offline", "dedicated-online"), "canonical mode required")
    require(field(data, "workload.mode") == "offline",
            "native-render-check is an offline fixture; dedicated-online adapter/evidence remains required")
    require(field(data, "workload.detail") in ("low", "high"), "canonical detail required")
    require(field(data, "workload.phase") in ("entry", "reentry"), "entry/reentry phase required")
    for name in ("width", "height", "warmupSimulationSteps", "measurementSimulationSteps"):
        value = field(data, "workload." + name)
        require(type(value) is int and value > 0, "workload." + name + " must be a positive integer")
    require(type(field(data, "workload.seed")) is int, "workload.seed missing/invalid")
    require(valid_rng(field(data, "workload.rngSeeds")), "workload.rngSeeds missing/invalid")
    require(isinstance(field(data, "workload.roster"), list), "workload.roster missing/invalid")
    require(field(data, "workload.inputExecution") == "frozen-device-snapshots-with-bot-ai", "unsupported input execution; replay-driven adapter remains unimplemented")
    require(field(data, "workload.replayExecuted") is False, "this native timing runner does not execute an input replay")
    require(field(data, "workload.phase") == "entry", "this native timing runner does not measure room reentry")
    require(number(field(data, "workload.renderScale"), 0.01), "workload.renderScale missing/invalid")
    for name in ("build.flags", "workload.runtimeSettings"):
        require(isinstance(field(data, name), dict) and settings_known(field(data, name)), name + " must explicitly record known settings")
    for name in ("build.configuration", "workload.harness", "workload.timingScope"):
        require(known(field(data, name)), name + " missing/unverified")
    require(field(data, "workload.harness") == "native-render-check-v2", "unsupported raw evidence adapter")
    require(field(data, "workload.timingScope") == "completed-frame-includes-swap", "native timing scope mismatch")
    require(field(data, "workload.profiling") is False, "profiled runs cannot substitute for unprofiled timing")
    require(field(data, "workload.readback") is False, "screenshot/readback runs cannot substitute for timing")
    artifacts = data.get("artifacts")
    require(isinstance(artifacts, list) and bool(artifacts), "artifacts missing")
    roles = set()
    for artifact in artifacts if isinstance(artifacts, list) else []:
        if not isinstance(artifact, dict):
            errors.append("invalid artifact record")
            continue
        role, path, expected = (artifact.get(k) for k in ("role", "path", "sha256"))
        require(known(role) and role not in roles, "artifact role missing/duplicate")
        if isinstance(role, str):
            roles.add(role)
        require(digest(expected), "invalid artifact digest: " + str(role))
        if not known(path):
            errors.append("missing artifact path: " + str(role))
            continue
        target = Path(root) / path
        try:
            require(target.is_file() and sha256(target) == expected, "artifact missing/changed: " + str(role))
            if role == "source-lock" and target.is_file():
                lock = json.loads(target.read_text())
                require(isinstance(lock, dict) and lock.get("sourceSha") == field(data, "source.sha") and lock.get("dirty") is False,
                        "source-lock does not attest a clean matching source SHA")
            if role == "raw-result" and target.is_file():
                raw = json.loads(target.read_text())
                require(isinstance(raw, dict), "raw-result must be an object")
                if isinstance(raw, dict):
                    errors.extend(audit_native_result(raw, data))
            if role in ("asset-manifest", "simulation-report") and target.is_file():
                identity = json.loads(target.read_text())
                key = "sha256" if role == "asset-manifest" else "canonicalHash"
                expected_identity = field(data, "workload." + ("mapSha256" if role == "asset-manifest" else "simulationSha256"))
                require(isinstance(identity, dict) and identity.get(key) == expected_identity,
                        role + " identity does not match workload")
            if role == "replay-fixture":
                require(expected == field(data, "workload.replaySha256"), "replay fixture identity mismatch")
        except (OSError, ValueError, TypeError) as error:
            errors.append("unreadable artifact " + str(role) + ": " + str(error))
    require({"client-binary", "harness-binary", "raw-result", "raw-log", "source-lock", "build-log",
             "asset-manifest", "replay-fixture", "simulation-report", "simulation-verify-log"} <= roles,
            "required binary/raw/source/build artifact roles missing")
    measurements = data.get("measurements", {})
    for name in METRICS:
        item = measurements.get(name) if isinstance(measurements, dict) else None
        if not isinstance(item, dict):
            errors.append("measurement missing: " + name)
            continue
        value = item.get("value")
        if name in ("frameP50Ms", "frameP95Ms", "frameP99Ms", "allocatedBytes", "gcCollections", "residentBytes"):
            require(item.get("status") == "measured" and number(value), "required measured value missing: " + name)
        elif item.get("status") == "unavailable":
            require(value is None and known(item.get("reason")), "unavailable value must be null with reason: " + name)
        else:
            require(item.get("status") == "measured" and number(value), "invalid measured value: " + name)
        if name.startswith("frame"):
            require(number(value, 0.000001), "frame timings must be positive: " + name)
    percentiles = [field(data, "measurements." + n + ".value") for n in METRICS[:3]]
    if all(number(n) for n in percentiles):
        require(percentiles == sorted(percentiles), "frame percentiles out of order")
    return errors


def compare(left, right, left_root, right_root):
    errors = ["baseline: " + e for e in audit(left, left_root)]
    errors += ["candidate: " + e for e in audit(right, right_root)]
    errors += ["identity differs: " + key for key in COMPARABLE if field(left, key) != field(right, key)]
    # The comparison never assigns a performance winner or a release/device PASS.
    return {"status": "NOT_COMPARABLE" if errors else "COMPARABLE", "reasons": errors,
            "scope": "Identity/integrity gate only; independently inspect raw measurements and build provenance."}


def read(path):
    data = json.loads(path.read_text())
    if not isinstance(data, dict):
        raise ValueError("manifest must be an object")
    return data


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    lock = commands.add_parser("lock")
    lock.add_argument("--repo", type=Path, required=True)
    lock.add_argument("--output", type=Path, required=True)
    check = commands.add_parser("check")
    check.add_argument("manifest", type=Path)
    pair = commands.add_parser("compare")
    pair.add_argument("baseline", type=Path)
    pair.add_argument("candidate", type=Path)
    args = parser.parse_args()
    try:
        if args.command == "lock":
            result = source_lock(args.repo)
            # Immutable by default: preserve failed/dirty observations too.
            with args.output.open("x") as stream:
                json.dump(result, stream, indent=2)
                stream.write("\n")
            print(json.dumps({"status": "DIRTY_SOURCE" if result["dirty"] else "SOURCE_LOCKED",
                              "sourceSha": result["sourceSha"], "output": str(args.output)}))
            return 2 if result["dirty"] else 0
        if args.command == "check":
            errors = audit(read(args.manifest), args.manifest.parent)
            result = {"status": "NOT_COMPARABLE" if errors else "IDENTITIES_COMPLETE", "reasons": errors}
        else:
            result = compare(read(args.baseline), read(args.candidate), args.baseline.parent, args.candidate.parent)
        print(json.dumps(result, indent=2))
        return 2 if result["reasons"] else 0
    except (OSError, ValueError, TypeError, subprocess.SubprocessError) as error:
        print(json.dumps({"status": "NOT_COMPARABLE", "reasons": [str(error)]}))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
