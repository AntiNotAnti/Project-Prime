#!/usr/bin/env python3
"""Run requested arms sequentially with one immutable binary/settings manifest."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import time

from analyze_resources import analyze


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--binary", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--arms", nargs="+", choices=["combined", "map-gl", "studio", "audio"], default=["combined", "map-gl", "studio", "audio"])
    parser.add_argument("--cycles", type=int, default=60)
    parser.add_argument("--frames", type=int, default=120)
    parser.add_argument("--quiescent-samples", type=int, default=30)
    parser.add_argument("--quiescent-ms", type=int, default=1000)
    parser.add_argument("--warmup-cycles", type=int, default=5)
    parser.add_argument("--timeout", type=int, default=3600)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("extra", nargs=argparse.REMAINDER, help="shared harness flags after --")
    args = parser.parse_args()
    if not 1 <= args.cycles <= 200 or not 30 <= args.frames <= 600:
        parser.error("cycles 1..200 and frames 30..600 required")
    if not 2 <= args.quiescent_samples <= 120 or not 10 <= args.quiescent_ms <= 5000:
        parser.error("quiescent samples 2..120 and ms 10..5000 required")
    extra = args.extra[1:] if args.extra[:1] == ["--"] else args.extra
    forbidden = {"-resource-arm", "-resourcecycles", "-resourceframes", "-output", "-resource-evidence", "-resourcestress", "-resource-drain", "-resource-quiescent-samples", "-resource-quiescent-ms"}
    if forbidden.intersection(extra):
        parser.error("shared extras cannot override runner-owned resource/output flags")
    binary, output = args.binary.resolve(), args.output.resolve()
    client = binary.with_name("ProjectPrime.dll")
    identity = {"harnessSha256": sha256(binary), "clientSha256": sha256(client)}
    repository = Path(__file__).resolve().parents[2]
    def git(*command):
        return subprocess.check_output(["git", "-C", str(repository), *command], text=True).strip()
    manifest = {"schema": 1, "sourceSha": git("rev-parse", "HEAD"), "sourceDirty": bool(git("status", "--porcelain")),
                "binary": str(binary), "identity": identity, "platform": platform.platform(),
                "runtimeInfo": subprocess.check_output([args.dotnet, "--info"], text=True),
                "environment": {k: v for k, v in os.environ.items() if k.startswith("PROJECT_PRIME_GL_") or k == "PROJECT_PRIME_USER_DATA"},
                "runOrder": args.arms, "dryRun": args.dry_run, "runs": [],
                "purpose": "Subsystem lifecycle attribution. Same-build arms; no FPS or native leak claim. Repeat with reverse order in a fresh output directory."}
    if output.exists() and any(output.iterdir()):
        parser.error("output must be a fresh/empty directory to preserve prior evidence")
    output.mkdir(parents=True, exist_ok=True)
    shared = ["-room", "MP3 PROVING GROUND", "-players", "8", "-size", "1920x1080", "-hz", "-1", "-seconds", "8", "-shadows", "High"] + extra
    failed = False
    for index, arm in enumerate(args.arms):
        result = output / f"{index:02d}-{arm}.json"
        command = [args.dotnet, str(binary), *shared, "-resourcestress", "-resource-arm", arm,
                   "-resourcecycles", str(args.cycles), "-resourceframes", str(args.frames), "-resource-drain",
                   "-resource-quiescent-samples", str(args.quiescent_samples), "-resource-quiescent-ms", str(args.quiescent_ms), "-output", str(result)]
        record = {"arm": arm, "command": command, "result": str(result)}
        manifest["runs"].append(record)
        (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
        if args.dry_run:
            print(json.dumps(command))
            continue
        if sha256(binary) != identity["harnessSha256"] or sha256(client) != identity["clientSha256"]:
            raise RuntimeError("binary changed during campaign")
        started = time.monotonic()
        with result.with_suffix(".log").open("w") as log:
            try:
                completed = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=args.timeout)
                record["exitCode"] = completed.returncode
            except subprocess.TimeoutExpired:
                record["exitCode"], record["error"] = -1, "timeout; process terminated"
        record["wallSeconds"] = time.monotonic() - started
        record["logSha256"] = sha256(result.with_suffix(".log"))
        if result.exists():
            record["resultSha256"] = sha256(result)
            data = json.loads(result.read_text())
            analysis = analyze(data, args.warmup_cycles)
            result.with_suffix(".analysis.json").write_text(json.dumps(analysis, indent=2) + "\n")
            actual = data.get("manifest", {})
            record["identityMatches"] = all(actual.get(k) == v.upper() for k, v in identity.items())
            failed |= not analysis["complete"] or not record["identityMatches"]
        else:
            record["error"] = record.get("error", "missing result")
            failed = True
        failed |= record["exitCode"] != 0
        (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
        print(f"{arm}: exit={record['exitCode']} elapsed={record['wallSeconds']:.2f}s", flush=True)
        if failed:
            break
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
