#!/usr/bin/env python3
"""Validate immutable context-campaign evidence; this is never a boundedness gate."""
import argparse
from collections import Counter
import hashlib
import gzip
import json
from pathlib import Path
import re
import sys

from analyze_graphics_stacks import parse as parse_graphics
from analyze_malloc_history import FRAME, GROUP
from analyze_vmmap import parse as parse_vmmap

HASH = re.compile(r"^[0-9a-fA-F]{64}$")


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate JSON key: " + key)
        result[key] = value
    return result


def read_json(path):
    return json.loads(path.read_text(), object_pairs_hook=_unique_object,
                      parse_constant=lambda value: (_ for _ in ()).throw(ValueError("nonfinite JSON: " + value)))


def _integer(value):
    return isinstance(value, int) and not isinstance(value, bool)


def _rows(value):
    return value if isinstance(value, list) else []


def _errors(value, location="receipt"):
    """Explicit error receipts cannot be hidden by a top-level complete=true."""
    if isinstance(value, dict):
        for key, child in value.items():
            where = location + "." + key
            if key in ("error", "drainError") and child not in (None, "", False):
                yield where + " records an error"
            if key == "complete" and child is not True:
                yield where + " is not true"
            if key in ("faulted", "canceled") and _integer(child) and child > 0:
                yield where + " is nonzero"
            yield from _errors(child, where)
    elif isinstance(value, list):
        for index, child in enumerate(value):
            yield from _errors(child, f"{location}[{index}]")


def inspect_stacks(text):
    """Only call truncation where a recognized group is demonstrably incomplete.

    Unknown tool/OS formats are UNVERIFIED, never zero allocation bytes. There is
    no universal malloc_history footer, so a cut at a complete group boundary
    cannot be detected from stdout alone; tool exit and capture hashes matter.
    """
    if re.search(r"(?im)^\s*(?:\[output truncated\]|output truncated(?:\s|$)|capture truncated(?:\s|$))", text):
        return "INVALID", ["explicit output truncation marker"], None
    matches = list(GROUP.finditer(text))
    if not matches:
        return "UNVERIFIED", ["unsupported or empty outstanding-stack format"], None
    issues = []
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        lines = text[match.end():end].splitlines()
        frames = [line for line in lines if FRAME.match(line)]
        if not frames:
            issues.append(f"stack group {index} has no full frames")
        for line in lines:
            if re.match(r"^\d+\s+\S+\s+0x", line) and not FRAME.match(line):
                issues.append(f"stack group {index} has an incomplete frame")
            if re.match(r"^\d+ calls for", line) and not GROUP.match(line + "\n"):
                issues.append(f"stack group {index} has an incomplete group header")
    return ("INVALID" if issues else "VALID"), issues, parse_graphics(text.splitlines(True))


def _validate_campaign(manifest, root):
    """Return data-quality gates for a runner manifest and its artifact directory.

    VALID means the checked evidence is internally consistent, not that growth
    is fixed. Missing profiling/identity proof yields UNVERIFIED; contradicted,
    incomplete, altered, or failed evidence yields INVALID. No native processes
    are launched. Unsupported metrics remain null rather than measured zero.
    """
    root = Path(root)
    gates = {}
    native = []

    def report(name, status, issue=None):
        gate = gates.setdefault(name, {"name": name, "status": "VALID", "issues": []})
        rank = {"VALID": 0, "UNVERIFIED": 1, "INVALID": 2}
        if rank[status] > rank[gate["status"]]:
            gate["status"] = status
        if issue:
            gate["issues"].append(issue)

    def path_for(value):
        path = Path(value)
        return path if path.is_absolute() else root / path

    def artifact(path_value, expected_hash, gate, missing="INVALID"):
        if not isinstance(path_value, str) or not path_value:
            report(gate, missing, "artifact path unavailable")
            return None
        path = path_for(path_value)
        if not path.is_file():
            report(gate, missing, f"artifact unavailable: {path}")
            return None
        if not isinstance(expected_hash, str) or not HASH.fullmatch(expected_hash):
            report(gate, "UNVERIFIED", f"SHA-256 unavailable: {path}")
        elif sha256(path).lower() != expected_hash.lower():
            report(gate, "INVALID", f"SHA-256 changed: {path}")
        else:
            report(gate, "VALID")
        return path

    def receipt(path, gate):
        try:
            data = read_json(path)
            if not isinstance(data, dict):
                raise ValueError("receipt must be a JSON object")
            for error in _errors(data, str(path)):
                report("execution", "INVALID", error)
            return data
        except (OSError, ValueError) as error:
            report(gate, "INVALID", f"invalid JSON receipt {path}: {error}")
            return None

    if not isinstance(manifest, dict):
        return {"schema": 1, "status": "INVALID", "gates": [{"name": "manifest", "status": "INVALID", "issues": ["manifest must be an object"]}], "boundedness": "UNVERIFIED", "nativeAttribution": []}
    for name in ("execution", "binaryIdentity", "artifactIntegrity", "capturePhases", "nativeStacks", "contextLifecycle", "workload", "shaderSources", "provenance"):
        report(name, "VALID")
    for error in _errors({key: value for key, value in manifest.items() if key != "host"}, "manifest"):
        report("execution", "INVALID", error)
    if manifest.get("dryRun") is True:
        report("execution", "UNVERIFIED", "dry run did not execute a campaign")
    elif manifest.get("exitCode") != 0 or manifest.get("complete") is not True:
        report("execution", "INVALID", "campaign exit/complete receipt is missing or unsuccessful")
    if manifest.get("identityMatches") is False:
        report("binaryIdentity", "INVALID", "runner reports changed binaries")
    elif manifest.get("identityMatches") is not True:
        report("binaryIdentity", "UNVERIFIED", "runner final binary comparison unavailable")
    identity = manifest.get("identity") if isinstance(manifest.get("identity"), dict) else {}
    binary = manifest.get("binary")
    artifact(binary, identity.get("harnessSha256"), "binaryIdentity", "UNVERIFIED")
    client = str(path_for(binary).parent / "ProjectPrime.dll") if isinstance(binary, str) else None
    artifact(client, identity.get("clientSha256"), "binaryIdentity", "UNVERIFIED")
    if not isinstance(manifest.get("sourceSha"), str) or not re.fullmatch(r"[0-9a-fA-F]{40,64}", manifest["sourceSha"]) or manifest.get("sourceDirty") is not False:
        report("provenance", "UNVERIFIED", "clean source commit unavailable; development evidence is not release proof")
    fixture = manifest.get("fixtureIdentity")
    if not isinstance(fixture, dict):
        report("provenance", "UNVERIFIED", "immutable asset/input fixture identity unavailable")
    else:
        if manifest.get("fixtureIdentityMatches") is False or fixture.get("identityMatches") is False:
            report("provenance", "INVALID", "fixture identity changed")
        elif manifest.get("fixtureIdentityMatches", fixture.get("identityMatches")) is not True:
            report("provenance", "UNVERIFIED", "final fixture identity comparison unavailable")
        assets_path = root / "asset-manifest.json"
        if not assets_path.is_file():
            report("provenance", "UNVERIFIED", "asset-file identity manifest unavailable")
        else:
            assets = receipt(assets_path, "provenance")
            entries = assets.get("files") if assets else None
            if not isinstance(entries, list) or not entries:
                report("provenance", "INVALID", "asset-file identity entries missing")
            else:
                asset_keys = [entry.get("key") for entry in entries if isinstance(entry, dict)]
                if len(set(asset_keys)) != len(entries) or any(not isinstance(key, str) or not key for key in asset_keys):
                    report("provenance", "INVALID", "asset source-key collision or missing key")
                payload = json.dumps(entries, sort_keys=True, separators=(",", ":")).encode()
                digest = hashlib.sha256(payload).hexdigest()
                if digest != fixture.get("sha256") or digest != assets.get("sha256"):
                    report("provenance", "INVALID", "asset manifest identity differs from frozen fixture")
    host = manifest.get("host")
    if not isinstance(host, dict) or not host.get("os") or not host.get("architecture"):
        report("provenance", "UNVERIFIED", "host identity unavailable")
    elif any(_errors(host, "host")):
        report("provenance", "UNVERIFIED", "optional host identity query failed")
    if manifest.get("isolatedUserData") is not True:
        report("provenance", "UNVERIFIED", "isolated user-data receipt unavailable")

    command = _rows(manifest.get("command"))
    def option(name, default=None):
        if command.count(name) > 1:
            report("workload", "INVALID", "duplicate workload option: " + name)
        try:
            return command[command.index(name) + 1]
        except (ValueError, IndexError):
            return default
    try:
        cycles = [int(value) for value in option("-resource-context-cycles", "").split(",")]
        frames = int(option("-resourceframes", "120"))
        if not 1 <= len(cycles) <= 8 or any(not 1 <= count <= 200 for count in cycles):
            raise ValueError("cycle counts outside supported bounds")
    except (ValueError, AttributeError, TypeError) as error:
        report("workload", "INVALID", "requested workload unavailable: " + str(error))
        cycles = []
        frames = None
    driver_drain = "-resource-context-driver-drain" in command
    expected_count = manifest.get("expectedNativeCaptures", 0)
    environment = manifest.get("environment") if isinstance(manifest.get("environment"), dict) else {}
    requested_stacks = (_integer(expected_count) and expected_count > 0) or environment.get("MallocStackLogging") == "1"
    expected_phases = []
    for index in range(len(cycles)):
        prefix = f"context-{index:02d}"
        if manifest.get("schema", 1) >= 2:
            expected_phases.append(prefix + "-before-workload")
        expected_phases.extend([prefix + "-before-disposal", prefix + "-after-disposal"])
    if driver_drain:
        expected_phases.extend(["after-glfw-poll", "after-glfw-terminate"])
    expected_phases = expected_phases if requested_stacks else []
    declared = manifest.get("expectedNativePhases")
    if declared is not None and declared != expected_phases:
        report("capturePhases", "INVALID", "declared capture phases differ from requested workload")
    captures = _rows(manifest.get("captures"))
    actual_phases = [capture.get("phase") for capture in captures if isinstance(capture, dict)]
    if actual_phases != expected_phases:
        report("capturePhases", "INVALID", "missing, duplicate, unexpected, or out-of-order capture phases")
    if not requested_stacks:
        report("nativeStacks", "UNVERIFIED", "native stack capture was not requested")
    elif manifest.get("nativeCapturesComplete") is not True:
        report("capturePhases", "INVALID", "native capture completion receipt missing or false")
    for key in ("expectedNativeCaptures", "nativeCaptureCount"):
        if key in manifest and manifest[key] != len(expected_phases):
            report("capturePhases", "INVALID", key + " disagrees with expected phases")

    result_path = artifact("context.json", manifest.get("resultSha256"), "artifactIntegrity")
    log_path = artifact("context.log", manifest.get("logSha256"), "artifactIntegrity")
    if log_path and re.search(r"(?m)^(?:NATIVERENDER .*\bFAIL\b|Unhandled exception\.)", log_path.read_text(errors="replace")):
        report("execution", "INVALID", "process log contains a failure despite completion receipts")
    result = receipt(result_path, "contextLifecycle") if result_path else None
    pid = result.get("manifest", {}).get("processId") if result else None
    capture_paths = set()
    for capture in captures:
        if not isinstance(capture, dict):
            report("capturePhases", "INVALID", "capture record is not an object")
            continue
        if pid is not None and capture.get("pid") != pid:
            report("contextLifecycle", "INVALID", "capture PID differs from campaign process")
        steps = _rows(capture.get("steps"))
        tools = []
        for step in steps:
            if not isinstance(step, dict):
                report("nativeStacks", "INVALID", "capture step is not an object")
                continue
            cmd = _rows(step.get("command"))
            tool = Path(str(cmd[0])).name if cmd else ""
            tools.append(tool)
            if capture.get("pid") is not None and str(capture["pid"]) not in cmd:
                report("contextLifecycle", "INVALID", "capture tool command targets a different PID")
            if step.get("exitCode") != 0 or step.get("truncated") is True:
                report("nativeStacks", "INVALID", f"failed/truncated {tool} capture: {capture.get('phase')}")
            path = artifact(step.get("path"), step.get("sha256"), "artifactIntegrity")
            if not path:
                continue
            normalized_path = str(path.resolve())
            if normalized_path in capture_paths:
                report("artifactIntegrity", "INVALID", "capture file reused across phases/tools")
            capture_paths.add(normalized_path)
            if step.get("compression") == "gzip":
                try:
                    with gzip.open(path, "rb") as stream:
                        raw = stream.read()
                    if hashlib.sha256(raw).hexdigest() != step.get("uncompressedSha256"):
                        report("artifactIntegrity", "INVALID", "decompressed stdout hash differs from original capture")
                    text = raw.decode(errors="replace")
                except (OSError, EOFError) as error:
                    report("artifactIntegrity", "INVALID", "invalid compressed capture: " + str(error))
                    continue
            else:
                text = path.read_text(errors="replace")
            if tool == "malloc_history":
                if "-allBySize" not in cmd or "-fullStacks" not in cmd or any(flag in cmd for flag in ("-highWaterMark", "-allEvents")):
                    report("nativeStacks", "INVALID", "malloc capture is not current outstanding full stacks")
                status, issues, parsed = inspect_stacks(text)
                report("nativeStacks", status)
                for issue in issues:
                    report("nativeStacks", status, f"{capture.get('phase')}: {issue}")
                native.append({"phase": capture.get("phase"), "supported": parsed is not None,
                               "categories": parsed["categories"] if parsed else None})
            elif tool == "vmmap":
                try:
                    parsed = parse_vmmap(text)
                    if "-summary" not in cmd:
                        report("nativeStacks", "INVALID", "vmmap summary command unavailable")
                    if not parsed["supported"]:
                        report("nativeStacks", "UNVERIFIED", "unsupported VM-category format")
                except ValueError:
                    report("nativeStacks", "UNVERIFIED", "unsupported VM quantity format")
        if Counter(tools) != Counter(["vmmap", "malloc_history"]):
            report("nativeStacks", "INVALID", "each capture requires exactly one vmmap and malloc_history receipt")

    if result:
        if result.get("contextCycles") != cycles:
            report("workload", "INVALID", "completed context targets differ from runner request")
        if result.get("driverDrainRequested") is not driver_drain:
            report("contextLifecycle", "INVALID", "driver-drain receipt differs from runner request")
        records = _rows(result.get("records"))
        expected = []
        samples = result.get("postDisposalSamples")
        if not _integer(samples) or samples < 2:
            report("contextLifecycle", "INVALID", "invalid post-disposal sample count")
            samples = 0
        for index in range(len(cycles)):
            expected.extend((index, phase, None) for phase in ("before-context-creation", "context-created", "before-context-disposal", "after-context-disposal"))
            expected.extend((index, "post-context-disposal", sample) for sample in range(samples))
        if driver_drain:
            for phase in ("after-glfw-poll", "after-glfw-terminate"):
                expected.extend((len(cycles) - 1, phase, sample) for sample in range(samples))
        actual = [(row.get("context"), row.get("phase"), row.get("sample")) for row in records if isinstance(row, dict)]
        if actual != expected:
            report("contextLifecycle", "INVALID", "missing, duplicate, mixed-context, or out-of-order lifecycle samples")
        for row in records:
            if not isinstance(row, dict):
                continue
            if row.get("phase") == "context-created" and (row.get("exists") is not True or row.get("contextCurrent") is not True):
                report("contextLifecycle", "INVALID", "created context is not current/alive")
            if row.get("phase") == "after-context-disposal" and row.get("exists") is not False:
                report("contextLifecycle", "INVALID", "disposed native window remains alive")
        _check_identity(result.get("manifest"), identity, report, "context result")

    artifacts = _rows(manifest.get("resultArtifacts"))
    workload_artifacts = [item for item in artifacts if isinstance(item, dict) and item.get("kind") == "context-workload"]
    if workload_artifacts:
        if Counter(item.get("context") for item in workload_artifacts) != Counter(range(len(cycles))):
            report("workload", "INVALID", "missing, duplicate, or mixed-context workload receipts")
    else:
        report("artifactIntegrity", "UNVERIFIED", "per-context receipt hashes unavailable")
        workload_artifacts = [{"context": index, "path": f"context.json.contexts/context-{index:02d}-{count}cycles.json"} for index, count in enumerate(cycles)]
    reference_workload = None
    reference_software = None
    for item in workload_artifacts:
        path = artifact(item.get("path"), item.get("sha256"), "artifactIntegrity")
        data = receipt(path, "workload") if path else None
        if data is None:
            continue
        index = item.get("context")
        if not _integer(index) or not 0 <= index < len(cycles):
            continue
        count = cycles[index]
        expected_name = f"context-{index:02d}-{count}cycles.json"
        if path.name != expected_name:
            report("contextLifecycle", "INVALID", "workload receipt path/context index mismatch")
        _check_identity(data.get("manifest"), identity, report, f"context {index}")
        software = data.get("manifest") if isinstance(data.get("manifest"), dict) else {}
        software_signature = {key: software.get(key) for key in ("assemblyMvid", "harnessAssemblyMvid", "runtime", "openTkVersion", "os")}
        if any(value in (None, "") for value in software_signature.values()):
            report("provenance", "UNVERIFIED", f"context {index}: assembly/runtime/OS identity incomplete")
        if reference_software is not None and software_signature != reference_software:
            report("binaryIdentity", "INVALID", "assembly/runtime/OS identity differs between contexts")
        reference_software = software_signature
        if pid is not None and data.get("manifest", {}).get("processId") != pid:
            report("contextLifecycle", "INVALID", "per-context workload belongs to a different process")
        if data.get("contextCount") != 1 or data.get("cyclesRequested") != count or data.get("cyclesCompleted") != count:
            report("workload", "INVALID", f"context {index}: incomplete cycles or mixed contexts")
        arm = option("-resource-arm", "map-gl")
        if data.get("arm") != arm or data.get("simulationFramesPerArm") != (0 if arm == "studio" else frames):
            report("workload", "INVALID", f"context {index}: workload arm/frame mismatch")
        rows = _rows(data.get("studioReloads")) if arm == "studio" else _rows(data.get("records"))
        keys = [(row.get("cycle"), row.get("candidate")) for row in rows if isinstance(row, dict)] if arm != "studio" else [row.get("cycle") for row in rows if isinstance(row, dict)]
        wanted = [(cycle, candidate) for cycle in range(count) for candidate in (False, True)] if arm != "studio" else list(range(count))
        if keys != wanted:
            report("workload", "INVALID", f"context {index}: duplicate, missing, or out-of-order workload cycles")
        for row in rows:
            if isinstance(row, dict) and arm != "studio" and row.get("simulationFrame") != frames:
                report("workload", "INVALID", f"context {index}: simulation frame workload gap")
        workload = data.get("workload")
        if not isinstance(workload, dict):
            report("workload", "UNVERIFIED", "workload fixture metadata unavailable")
        else:
            if workload.get("simulationHz") != 60:
                report("workload", "INVALID", "simulation cadence differs from 60 Hz")
            signature = {key: workload.get(key) for key in ("fixedRng", "players", "bots", "shadows", "actualFramebuffer", "vendor", "renderer", "glVersion")}
            if reference_workload is not None and signature != reference_workload:
                report("workload", "INVALID", "workload fixture/graphics identity differs between contexts")
            reference_workload = signature
        _check_sources(data.get("shaderSourceLedger"), "-resource-shader-source-ledger" in command, report, index)

    gate_list = list(gates.values())
    status = "INVALID" if any(g["status"] == "INVALID" for g in gate_list) else "UNVERIFIED" if any(g["status"] == "UNVERIFIED" for g in gate_list) else "VALID"
    return {"schema": 1, "status": status, "gates": gate_list, "nativeAttribution": native,
            "boundedness": "UNVERIFIED", "limitations": "Data quality only. Stack origin is not retaining ownership; unsupported categories are null. Complete-group-boundary truncation cannot be inferred from stdout. Idle stability, deleted GL names, and RSS do not establish repeated-load boundedness."}


def validate_campaign(manifest, root):
    """Public fail-closed API; malformed receipts produce an INVALID JSON gate."""
    try:
        return _validate_campaign(manifest, root)
    except (OSError, ValueError, TypeError, AttributeError, KeyError) as error:
        return {"schema": 1, "status": "INVALID", "gates": [{"name": "manifest", "status": "INVALID", "issues": [str(error)]}], "boundedness": "UNVERIFIED", "nativeAttribution": []}


def _check_identity(receipt, identity, report, label):
    if not isinstance(receipt, dict):
        report("binaryIdentity", "UNVERIFIED", label + ": binary identity unavailable")
        return
    for key in ("clientSha256", "harnessSha256"):
        value, expected = receipt.get(key), identity.get(key)
        if not isinstance(value, str) or not isinstance(expected, str):
            report("binaryIdentity", "UNVERIFIED", label + ": " + key + " unavailable")
        elif value.lower() != expected.lower():
            report("binaryIdentity", "INVALID", label + ": " + key + " differs")


def _check_sources(ledger, requested, report, index):
    label = f"context {index}: "
    if not isinstance(ledger, dict):
        report("shaderSources", "INVALID" if requested else "UNVERIFIED", label + "shader source ledger unavailable")
        return
    if ledger.get("unexpectedStatusCallbacks", 0) != 0:
        report("shaderSources", "INVALID", label + "unexpected/duplicate status callbacks")
    for prefix, attempts in (("compile", "compileAttemptCallbacks"), ("link", "linkAttemptCallbacks")):
        status_key = prefix + "StatusCallbacks"
        if status_key not in ledger:
            continue  # Historical attempt-only schema makes no success assertion.
        count, successes, failures = ledger.get(status_key), ledger.get(prefix + "Successes"), ledger.get(prefix + "Failures")
        if not all(_integer(value) and value >= 0 for value in (count, successes, failures)) or count != successes + failures:
            report("shaderSources", "INVALID", label + prefix + " status counts are inconsistent")
        elif failures:
            report("shaderSources", "INVALID", label + prefix + " reports failed GL status")
        elif count != ledger.get(attempts):
            report("shaderSources", "UNVERIFIED", label + prefix + " status coverage is incomplete")
    sources = _rows(ledger.get("sources"))
    keys, declared_keys, hashes, callbacks = [], {}, set(), 0
    for source in sources:
        if not isinstance(source, dict):
            report("shaderSources", "INVALID", label + "invalid source receipt")
            continue
        stage, digest, count = source.get("stage"), source.get("sourceSha256"), source.get("callbacks")
        if not isinstance(stage, str) or not isinstance(digest, str) or not HASH.fullmatch(digest) or not _integer(count) or count < 1:
            report("shaderSources", "INVALID", label + "invalid stage/hash/callback receipt")
            continue
        key = (stage, digest.lower())
        keys.append(key)
        hashes.add(digest.lower())
        callbacks += count
        declared_key = source.get("sourceKey")
        if declared_key is not None:
            if str(declared_key) in declared_keys and declared_keys[str(declared_key)] != key:
                report("shaderSources", "INVALID", label + "source-key collision")
            declared_keys[str(declared_key)] = key
    if len(set(keys)) != len(keys):
        report("shaderSources", "INVALID", label + "duplicate source-stage keys")
    if ledger.get("sourceStageKeys") != len(keys) or ledger.get("uniqueSourceHashes") != len(hashes):
        report("shaderSources", "INVALID", label + "source-key totals disagree with entries")
    overflow = ledger.get("overflowCallbacks")
    if not _integer(overflow) or overflow < 0 or ledger.get("compileAttemptCallbacks") != callbacks + overflow:
        report("shaderSources", "INVALID", label + "compile callback coverage has gaps")
    elif overflow:
        report("shaderSources", "UNVERIFIED", label + "source ledger overflow prevents complete source attribution")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path, help="runner-manifest.json or campaign directory")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    path = args.manifest / "runner-manifest.json" if args.manifest.is_dir() else args.manifest
    try:
        result = validate_campaign(read_json(path), path.parent)
    except (OSError, ValueError, TypeError, AttributeError) as error:
        result = {"schema": 1, "status": "INVALID", "gates": [{"name": "manifest", "status": "INVALID", "issues": [str(error)]}], "boundedness": "UNVERIFIED", "nativeAttribution": []}
    output = json.dumps(result, indent=2, allow_nan=False) + "\n"
    if args.output:
        if args.output.exists():
            parser.error("output must be fresh to preserve evidence")
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(output)
    else:
        print(output, end="")
    return 1 if result["status"] == "INVALID" else 0


if __name__ == "__main__":
    sys.exit(main())
