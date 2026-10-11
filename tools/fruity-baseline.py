#!/usr/bin/env python3
"""Plan or run reproducible PR00 native profiles without building binaries."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import subprocess
import sys
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]
AB = {
    "legacy-list": ("PROJECT_PRIME_GL_VBO", "0", "-gllegacylist"),
    "legacy-bindings": ("PROJECT_PRIME_GL_BIND_CACHE", "0", "-gllegacybindings"),
    "batch-off": ("PROJECT_PRIME_GL_BATCH", "0", None),
    "legacy-depth": ("PROJECT_PRIME_GL_FORCE_DEPTH_REPLAY", "1", "-gllegacydepth"),
    "legacy-state": ("PROJECT_PRIME_GL_FORCE_STATE_REQUESTS", "1", "-gllegacystate"),
    "legacy-room-vis": ("PROJECT_PRIME_GL_LEGACY_ROOM_VIS", "1", "-gllegacyroomvis"),
    "legacy-item-vis": ("PROJECT_PRIME_GL_LEGACY_ITEM_VIS", "1", "-gllegacyitemvis"),
}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else "NOT RUN: file missing"


def output(command):
    try:
        return subprocess.check_output(command, cwd=ROOT, stderr=subprocess.STDOUT).decode().strip()
    except (OSError, subprocess.CalledProcessError) as error:
        return "NOT RUN: " + str(error)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--plan-only", action="store_true")
    mode.add_argument("--run", action="store_true")
    parser.add_argument("--tool", type=Path, required=True, help="Already-built native-render-check.dll")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--assets-data-dir", type=Path, required=True, help="PROJECT_PRIME_USER_DATA directory containing paths.txt")
    parser.add_argument("--game-data-version", required=True, help="Operator supplied extracted-game revision/hash identity")
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--room", default="MP1 SANCTORUS")
    parser.add_argument("--size", default="1920x1080")
    parser.add_argument("--hunter", default="Samus")
    parser.add_argument("--hz", type=int, action="append", help="Repeatable cap; defaults to 60/120/144/240")
    parser.add_argument("--seconds", type=int, default=8)
    parser.add_argument("--camera", required=True, help="Fixed free-camera x,y,z")
    parser.add_argument("--facing", required=True, help="Fixed forward x,y,z")
    parser.add_argument("--ab", action="append", choices=sorted(AB), default=[], help="Add an independent arm differing from default in this switch only; repeatable")
    args = parser.parse_args()
    if not 8 <= args.seconds <= 120 or any(hz != -1 and not 30 <= hz <= 500 for hz in (args.hz or [])):
        parser.error("native harness requires seconds 8..120 and hz -1 or 30..500")
    for value in (args.camera, args.facing):
        try:
            if len(value.split(",")) != 3:
                raise ValueError()
            vector = [float(part) for part in value.split(",")]
            if not all(math.isfinite(part) for part in vector):
                raise ValueError()
        except ValueError:
            parser.error("camera and facing must have three finite comma-separated numbers")
    if not any(float(part) for part in args.facing.split(",")):
        parser.error("facing must be nonzero")
    tool = args.tool.resolve()
    engine = (tool.parent / "ProjectPrime.dll").resolve()
    data = args.assets_data_dir.resolve()
    destination = args.output_dir.resolve()
    if args.run and (not tool.is_file() or not engine.is_file() or not (data / "paths.txt").is_file()):
        parser.error("--run requires the built tool, engine DLL, and assets-data-dir/paths.txt")
    destination.mkdir(parents=True, exist_ok=True)
    # Include staged, unstaged, and untracked content identities; ignored build
    # outputs are covered separately by DLL hashes.
    untracked = output(["git", "ls-files", "--others", "--exclude-standard"])
    source = {
        "commit": output(["git", "rev-parse", "HEAD"]),
        "status": output(["git", "status", "--porcelain=v1"]),
        "dirtyDiffSha256": hashlib.sha256(subprocess.check_output(
            ["git", "diff", "--binary", "HEAD"], cwd=ROOT)).hexdigest(),
        "untrackedSha256": {name: digest(ROOT / name) for name in untracked.splitlines() if name},
    }
    manifest = {
        "schema": 1, "createdUtc": datetime.now(timezone.utc).isoformat(),
        "mode": "run" if args.run else "plan-only", "source": source,
        "platform": platform.platform(), "machine": platform.machine(),
        "sdkVersion": output([args.dotnet, "--version"]),
        "tool": {"path": str(tool), "sha256": digest(tool)},
        "engine": {"path": str(engine), "sha256": digest(engine)},
        "gameData": {"userDataDir": str(data), "version": args.game_data_version,
                     "pathsFileSha256": digest(data / "paths.txt"),
                     "pathsFile": (data / "paths.txt").read_text() if (data / "paths.txt").is_file() else "NOT RUN: paths.txt missing"},
        "preset": "Original (native harness fixed preset)",
        "measurement": f"{args.seconds} seconds requested, first 240 simulation steps excluded by native harness",
        "runs": [],
    }
    # Pin known A/B settings so inherited shell switches cannot contaminate arms.
    baseline_env = {key: "0" if "FORCE_" in key or "LEGACY_" in key else "1" for key, _, _ in AB.values()}
    baseline_env.update(PROJECT_PRIME_GL_FORCE_LISTS="0", PROJECT_PRIME_GL_FORCE_BINDINGS="0")
    baseline_env["PROJECT_PRIME_USER_DATA"] = str(data)
    for profile, players in (("solo", 1), ("one-bot", 2)):
        for hz in dict.fromkeys(args.hz or (60, 120, 144, 240)):
            for arm in ("default", *dict.fromkeys(args.ab)):
                name = f"{profile}-{hz}-{arm}"
                result_path = destination / (name + ".json")
                env = dict(baseline_env)
                flags = []
                if arm != "default":
                    key, value, flag = AB[arm]
                    env[key] = value
                    if flag:
                        flags.append(flag)
                command = [args.dotnet, str(tool), "-room", args.room, "-players", str(players),
                           "-idle", "-seconds", str(args.seconds), "-hz", str(hz), "-size", args.size,
                           "-hunter", args.hunter, "-shadows", "Off", "-outlines", "Off",
                           "-camera", args.camera, "-facing", args.facing, "-glprofile",
                           *flags, "-output", str(result_path)]
                run = {"name": name, "command": command, "environmentOverrides": env,
                       "status": "NOT RUN", "nativeResult": None, "actualGpu": "NOT RUN",
                       "assemblyMvid": "NOT RUN", "strictFpsAcceptance": "NOT RUN: no acceptance assertion"}
                manifest["runs"].append(run)
                # Captures force one simulation step per picture and extra
                # readbacks. Keep their evidence out of timed profile results.
                image_result = destination / (name + "-images.json")
                image_command = command[:-2] + ["-shots", str(destination / (name + "-images")),
                                               "-output", str(image_result)]
                run["imageEvidence"] = {"status": "NOT RUN", "command": image_command,
                                        "resultPath": str(image_result), "benchmark": False}
                if args.run:
                    # Never adopt stale results from an earlier campaign.
                    if result_path.exists():
                        result_path.unlink()
                    with (destination / (name + ".log")).open("w") as log:
                        try:
                            process = subprocess.run(command, cwd=ROOT, env={**os.environ, **env},
                                                     stdout=log, stderr=subprocess.STDOUT, timeout=150)
                            run["exitCode"] = process.returncode
                            run["status"] = "EXECUTED" if process.returncode == 0 else "FAILED"
                        except (OSError, subprocess.TimeoutExpired) as error:
                            run["status"] = "FAILED"
                            run["error"] = str(error)
                    if result_path.is_file():
                        try:
                            native = json.loads(result_path.read_text())
                            run["nativeResult"] = native
                            run["assemblyMvid"] = native.get("assemblyMvid", "NOT RUN: absent from native result")
                            run["actualGpu"] = {key: native[key] for key in
                                                ("gpu", "vendor", "renderer", "glVersion") if key in native} or "NOT RUN: absent from native JSON; inspect context log"
                        except (OSError, ValueError) as error:
                            run["status"] = "FAILED"
                            run["error"] = str(error)
                    else:
                        run["status"] = "FAILED"
                        run["error"] = "Native result missing"
                    image = run["imageEvidence"]
                    if image_result.exists():
                        image_result.unlink()
                    with (destination / (name + "-images.log")).open("w") as log:
                        try:
                            process = subprocess.run(image_command, cwd=ROOT, env={**os.environ, **env},
                                                     stdout=log, stderr=subprocess.STDOUT, timeout=650)
                            image["exitCode"] = process.returncode
                            image["status"] = "EXECUTED" if process.returncode == 0 else "FAILED"
                            if image_result.is_file():
                                image["nativeResult"] = json.loads(image_result.read_text())
                            else:
                                image["status"] = "FAILED"
                                image["error"] = "Native image result missing"
                        except (OSError, ValueError, subprocess.TimeoutExpired) as error:
                            image["status"] = "FAILED"
                            image["error"] = str(error)
                (destination / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(destination / "manifest.json")
    return int(any(run["status"] == "FAILED" or run["imageEvidence"]["status"] == "FAILED"
                   for run in manifest["runs"]))


if __name__ == "__main__":
    sys.exit(main())
