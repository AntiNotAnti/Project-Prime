#!/usr/bin/env python3
"""Sequential context controls with immutable binaries and optional live native captures."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

from analyze_resources import analyze


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def command(args):
    return [args.dotnet, str(args.binary.resolve()), "-room", "MP3 PROVING GROUND", "-players", "8",
            "-size", "1920x1080", "-hz", "-1", "-seconds", "8", "-shadows", "High",
            "-resourcestress", "-resource-arm", "map-gl", "-resourceframes", str(args.frames),
            "-resource-drain", "-resource-quiescent-samples", str(args.samples), "-resource-quiescent-ms", "1000",
            "-resource-context-cycles", ",".join(map(str, args.cycles)),
            "-resource-context-post-samples", str(args.samples), "-resource-context-post-ms", "1000",
            "-output", str(args.output.resolve() / "context.json")]


def capture(pid, stem, output):
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
        steps.append({"command": cmd, "exitCode": code, "wallSeconds": time.monotonic() - started,
                      "captureUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                      "path": str(path.resolve()), "sha256": sha256(path)})
    return {"phase": stem, "pid": pid, "trigger": "at least 3 seconds after phase marker while process alive", "steps": steps}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--binary", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--cycles", type=int, nargs="+", default=[120, 180])
    parser.add_argument("--frames", type=int, default=120)
    parser.add_argument("--samples", type=int, default=60)
    parser.add_argument("--timeout", type=float, default=3600)
    parser.add_argument("--capture-native-stacks", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    if not 1 <= len(args.cycles) <= 8 or any(c < 1 or c > 200 for c in args.cycles) or not 2 <= args.samples <= 120:
        parser.error("1..8 cycle counts of 1..200 and 2..120 samples required")
    if args.output.exists():
        parser.error("output directory must be fresh")
    args.output.mkdir(parents=True)
    cmd = command(args)
    client = args.binary.parent / "ProjectPrime.dll"
    identity = {"harnessSha256": sha256(args.binary), "clientSha256": sha256(client)}
    env = os.environ.copy()
    if args.capture_native_stacks:
        env["MallocStackLogging"] = "1"
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], text=True).strip())
    manifest = {"schema": 1, "sourceSha": source, "sourceDirty": dirty, "binary": str(args.binary.resolve()),
                "identity": identity, "command": cmd, "dryRun": args.dry_run,
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
    context = 0
    pid = None
    pending = {}
    observed = set()
    log = args.output / "context.log"
    with log.open("w") as dest, log.open("r") as reader:
        process = subprocess.Popen(cmd, stdout=dest, stderr=subprocess.STDOUT, env=env)
        try:
            while process.poll() is None:
                for line in reader.readlines():
                    match = re.search(r"NATIVERENDER pid=(\d+)", line)
                    if match:
                        pid = int(match[1])
                    match = re.search(r"cycle=(\d+)/(\d+)", line)
                    if match and match[1] == match[2]:
                        stem = f"context-{context:02d}-before-disposal"
                        if stem not in observed and stem not in pending:
                            pending[stem] = time.monotonic()
                    match = re.search(r"NATIVERENDER context=(\d+) disposed", line)
                    if match:
                        context = int(match[1])
                        pending[f"context-{context:02d}-after-disposal"] = time.monotonic()
                        context += 1
                if args.capture_native_stacks and pid:
                    for stem, marker in list(pending.items()):
                        if time.monotonic() - marker >= 3:
                            result = capture(pid, stem, args.output)
                            manifest["captures"].append(result)
                            observed.add(stem)
                            del pending[stem]
                            save()
                            print(stem + " capture complete", flush=True)
                if time.monotonic() - started > args.timeout:
                    manifest["error"] = "Campaign timeout; partial artifacts preserved"
                    process.kill()
                    break
                time.sleep(0.25)
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
            manifest["expectedNativeCaptures"] = len(args.cycles) * 2 if args.capture_native_stacks else 0
            manifest["nativeCaptureCount"] = len(manifest["captures"])
            save()
    for path in (args.output / "context.json.contexts").glob("*.json"):
        path.with_suffix(".analysis.json").write_text(json.dumps(analyze(json.loads(path.read_text())), indent=2) + "\n")
    return 0 if manifest.get("complete") and manifest["exitCode"] == 0 and manifest["identityMatches"] else 1


if __name__ == "__main__":
    sys.exit(main())
