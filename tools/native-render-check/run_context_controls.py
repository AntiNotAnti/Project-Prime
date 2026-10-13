#!/usr/bin/env python3
"""Sequential context controls with immutable binaries and optional live native captures."""
import argparse
import hashlib
import gzip
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time

from analyze_resources import analyze
from campaign_identity import asset_identity, host_identity, isolate_user_data


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def command(args):
    cmd = [args.dotnet, str(args.binary.resolve()), "-room", "MP3 PROVING GROUND", "-players", "8",
            "-size", "1920x1080", "-hz", "-1", "-seconds", "8", "-shadows", "High",
            "-resourcestress", "-resource-arm", getattr(args, "arm", "map-gl"), "-resourceframes", str(args.frames),
            "-failure-stages", getattr(args, "failure_stages", "none"),
            "-resource-drain", "-resource-quiescent-samples", str(args.samples), "-resource-quiescent-ms", "1000",
            "-resource-context-cycles", ",".join(map(str, args.cycles)),
            "-resource-context-post-samples", str(args.samples), "-resource-context-post-ms", "1000",
            "-output", str(args.output.resolve() / "context.json")]
    if args.driver_drain:
        cmd.append("-resource-context-driver-drain")
    if args.capture_native_stacks:
        cmd += ["-resource-context-phase-acks", str(args.output.resolve() / "phase-acks")]
    if args.shader_source_ledger:
        cmd.append("-resource-shader-source-ledger")
    return cmd


def expected_phases(contexts, driver_drain):
    phases = [f"context-{index:02d}-{phase}" for index in range(contexts)
              for phase in ("before-workload", "before-disposal", "after-disposal")]
    return phases + (["after-glfw-poll", "after-glfw-terminate"] if driver_drain else [])


def capture(pid, stem, output, compress_stacks=False):
    steps = []
    for tool, cmd, timeout in [("vmmap", ["vmmap", "-summary", str(pid)], 20),
                               ("malloc-history", ["malloc_history", str(pid), "-allBySize", "-fullStacks"], 60)]:
        path = output / f"{stem}.{tool}.txt"
        started = time.monotonic()
        with path.open("w") as dest:
            try:
                result = subprocess.run(cmd, stdout=dest, stderr=subprocess.STDOUT, timeout=timeout)
                code = result.returncode
            except (subprocess.TimeoutExpired, OSError) as error:
                dest.write(str(error) + "\n")
                code = -1
        original_hash = sha256(path)
        compression = None
        if compress_stacks and tool == "malloc-history":
            packed = path.with_suffix(path.suffix + ".gz")
            with path.open("rb") as source, gzip.open(packed, "wb", compresslevel=1) as dest:
                shutil.copyfileobj(source, dest)
            path.unlink()
            path = packed
            compression = "gzip"
        steps.append({"command": cmd, "exitCode": code, "wallSeconds": time.monotonic() - started,
                      "captureUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                      "path": str(path.resolve()), "sha256": sha256(path),
                      "compression": compression, "uncompressedSha256": original_hash})
    return {"phase": stem, "pid": pid, "trigger": "at least 3 seconds after phase marker while process alive", "steps": steps}


def captures_complete(expected, captures):
    return len(captures) == expected and all(
        step["exitCode"] == 0 for record in captures for step in record["steps"])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--binary", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--cycles", type=int, nargs="+", default=[120, 180])
    parser.add_argument("--frames", type=int, default=120)
    parser.add_argument("--samples", type=int, default=60)
    parser.add_argument("--timeout", type=float, default=3600)
    parser.add_argument("--arm", choices=["map-gl", "studio"], default="map-gl")
    parser.add_argument("--failure-stages", default="none")
    parser.add_argument("--user-data-template", type=Path, help="Read only paths.txt; create isolated settings/save root")
    parser.add_argument("--max-rss-mib", type=int, default=4096)
    parser.add_argument("--min-free-disk-mib", type=int, default=512)
    parser.add_argument("--capture-native-stacks", action="store_true")
    parser.add_argument("--uncompressed-stacks", action="store_true", help="Keep large raw stack stdout without lossless gzip")
    parser.add_argument("--driver-drain", action="store_true", help="Final owner-thread GLFW poll/terminate phases; no recreation afterward")
    parser.add_argument("--shader-source-ledger", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    if not 1 <= len(args.cycles) <= 8 or any(c < 1 or c > 200 for c in args.cycles) or not 2 <= args.samples <= 120:
        parser.error("1..8 cycle counts of 1..200 and 2..120 samples required")
    if not 30 <= args.frames <= 600 or args.timeout <= 0 or args.max_rss_mib <= 0 or args.min_free_disk_mib < 0:
        parser.error("frames 30..600, positive timeout/RSS limit and nonnegative disk reserve required")
    if args.user_data_template is None and not args.dry_run:
        parser.error("--user-data-template is required for an isolated native campaign")
    if args.output.exists():
        parser.error("output directory must be fresh")
    args.output.mkdir(parents=True)
    cmd = command(args)
    client = args.binary.parent / "ProjectPrime.dll"
    identity = {"harnessSha256": sha256(args.binary), "clientSha256": sha256(client)}
    env = os.environ.copy()
    fixtures = None
    if args.user_data_template is not None:
        user_data, fixtures = isolate_user_data(args.user_data_template, args.output)
        env["PROJECT_PRIME_USER_DATA"] = str(user_data.resolve())
        (args.output / "asset-manifest.json").write_text(json.dumps(fixtures, indent=2) + "\n")
    if args.capture_native_stacks:
        env["MallocStackLogging"] = "1"
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], text=True).strip())
    manifest = {"schema": 2, "sourceSha": source, "sourceDirty": dirty, "binary": str(args.binary.resolve()),
                "identity": identity, "command": cmd, "dryRun": args.dry_run,
                "host": host_identity(args.dotnet) if not args.dry_run else None,
                "isolatedUserData": args.user_data_template is not None,
                "fixtureIdentity": None if fixtures is None else {k: v for k, v in fixtures.items() if k != "files"},
                "expectedNativePhases": expected_phases(len(args.cycles), args.driver_drain) if args.capture_native_stacks else [],
                "guardrails": {"maxRssMiB": args.max_rss_mib, "minFreeDiskMiB": args.min_free_disk_mib},
                "workload": {"kind": "fixed-input-resource-reload", "simulationHz": 60,
                             "rngSeeds": [12345, 98765], "mapLoadsPerCycle": 2 if args.arm == "map-gl" else 0,
                             "fixture": "ResourceStress.cs", "graphicsPreset": "Original", "failureStages": args.failure_stages},
                "environment": {k: v for k, v in env.items() if k.startswith("PROJECT_PRIME_GL_") or k in ["PROJECT_PRIME_USER_DATA", "MallocStackLogging", "MallocStackLoggingNoCompact", "DOTNET_GCHeapHardLimit", "DOTNET_gcServer"]},
                "profiling": "Optional stack logging perturbs allocator/memory/CPU. No FPS evidence; mmap groups are not resident bytes.",
                "captures": []}
    manifest_path = args.output / "runner-manifest.json"
    def save():
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    save()
    print(json.dumps(cmd), flush=True)
    if args.dry_run:
        return 0
    started = time.monotonic()
    pending = {}
    observed = set()
    log = args.output / "context.log"
    with log.open("w") as dest, log.open("r") as reader:
        process = subprocess.Popen(cmd, stdout=dest, stderr=subprocess.STDOUT, env=env)
        try:
            while process.poll() is None:
                for line in reader.readlines():
                    match = re.search(r"NATIVERENDER capture-phase=([\w-]+) pid=(\d+)", line)
                    if match:
                        stem, pid = match[1], int(match[2])
                        if stem in observed or stem in pending or stem not in manifest["expectedNativePhases"] or pid != process.pid:
                            raise ValueError("Duplicate, unexpected or mixed-process phase: " + stem)
                        pending[stem] = (time.monotonic(), pid)
                if args.capture_native_stacks:
                    for stem, (marker, pid) in list(pending.items()):
                        if time.monotonic() - marker >= 3:
                            result = capture(pid, stem, args.output, compress_stacks=not args.uncompressed_stacks)
                            manifest["captures"].append(result)
                            observed.add(stem)
                            del pending[stem]
                            save()
                            acknowledgements = args.output / "phase-acks"
                            (acknowledgements / (stem + ".ack")).write_text("capture complete\n")
                            print(stem + " capture complete", flush=True)
                if time.monotonic() - started > args.timeout:
                    manifest["error"] = "Campaign timeout; partial artifacts preserved"
                    process.kill()
                    break
                free = shutil.disk_usage(args.output).free / 1024**2
                rss = subprocess.run(["ps", "-o", "rss=", "-p", str(process.pid)], capture_output=True, text=True)
                rss_mib = int(rss.stdout.strip()) / 1024 if rss.returncode == 0 and rss.stdout.strip() else None
                if free < args.min_free_disk_mib or (rss_mib is not None and rss_mib > args.max_rss_mib):
                    manifest["error"] = f"Guardrail stop: freeDiskMiB={free}, rssMiB={rss_mib}; partial evidence preserved"
                    process.kill()
                    break
                time.sleep(0.25)
        except Exception as error:
            manifest["error"] = str(error)
        finally:
            if process.poll() is None:
                process.kill()
            manifest["exitCode"] = process.wait()
            manifest["wallSeconds"] = time.monotonic() - started
            manifest["identityMatches"] = identity == {"harnessSha256": sha256(args.binary), "clientSha256": sha256(client)}
            manifest["logSha256"] = sha256(log)
            result = args.output / "context.json"
            if result.is_file():
                manifest["resultSha256"] = sha256(result)
                manifest["complete"] = json.loads(result.read_text()).get("complete") is True
            manifest["expectedNativeCaptures"] = len(manifest["expectedNativePhases"])
            manifest["nativeCaptureCount"] = len(manifest["captures"])
            manifest["nativeCapturesComplete"] = captures_complete(manifest["expectedNativeCaptures"], manifest["captures"])
            manifest["resultArtifacts"] = [{"kind": "context-workload", "context": index, "path": str(path.resolve()), "sha256": sha256(path)}
                                           for index, path in enumerate(sorted((args.output / "context.json.contexts").glob("context-*.json")))
                                           if not path.name.endswith(".analysis.json")]
            if fixtures is not None:
                manifest["fixtureIdentityMatches"] = asset_identity(args.output / "user-data" / "paths.txt")["sha256"] == fixtures["sha256"]
            save()
    for path in (args.output / "context.json.contexts").glob("*.json"):
        path.with_suffix(".analysis.json").write_text(json.dumps(analyze(json.loads(path.read_text())), indent=2) + "\n")
    from validate_context_campaign import validate_campaign
    validation = validate_campaign(manifest, args.output)
    (args.output / "validation.json").write_text(json.dumps(validation, indent=2) + "\n")
    return 0 if manifest.get("complete") and manifest["exitCode"] == 0 and manifest["identityMatches"] and manifest["nativeCapturesComplete"] and validation["status"] != "INVALID" else 1


if __name__ == "__main__":
    sys.exit(main())
