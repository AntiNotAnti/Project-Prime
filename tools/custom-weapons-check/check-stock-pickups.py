#!/usr/bin/env python3
"""Run already-built stock M7 pickup acceptance in 27 isolated fresh processes."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import subprocess
import time

ROOMS = (
    "AD1 TRANSFER LOCK BT", "AD1 TRANSFER LOCK DM", "AD2 ALINOS PERCH", "AD2 MAGMA VENTS",
    "CTF1 FAULT LINE - EXPANDED", "CTF1_FAULT LINE", "E3 FIRST HUNT", "Gorea Prison",
    "MP1 SANCTORUS", "MP10 OVERLOAD", "MP11 BREAKTHROUGH", "MP12 SIC TRANSIT",
    "MP13 ACCELERATOR", "MP14 OUTER REACH", "MP2 HARVESTER", "MP3 PROVING GROUND",
    "MP4 HIGHGROUND", "MP4 HIGHGROUND - EXPANDED", "MP5 FUEL SLUICE", "MP6 HEADSHOT",
    "MP7 PROCESSOR CORE", "MP8 FIRE CONTROL", "MP9 CRYOCHASM", "UNIT 3 VESPER STARPORT",
    "UNIT 4 ARCTERRA BASE", "UNIT1 ALINOS LANDFALL", "UNIT2 LANDING BAY",
)
ROOT = Path(__file__).resolve().parents[2]
BINARY = ROOT / "tools/custom-weapons-check/bin/Release/net10.0/custom-weapons-check.dll"
HASH_FILES = {
    "main_game": ROOT / "src/MphRead/bin/Release/net10.0/ProjectPrime.dll",
    "tool_game_copy": BINARY.with_name("ProjectPrime.dll"),
    "tool": BINARY,
}


def hashes():
    result = {}
    for name, path in HASH_FILES.items():
        try:
            result[name] = hashlib.sha256(path.read_bytes()).hexdigest()
        except OSError:
            result[name] = None
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", required=True, type=Path, help="Private game-data directory containing paths.txt")
    parser.add_argument("--output", required=True, type=Path, help="New, nonexistent evidence directory")
    parser.add_argument("--dotnet", help="dotnet executable; otherwise resolve dotnet on PATH")
    args = parser.parse_args()
    data, output = args.data.resolve(), args.output.resolve()
    dotnet = shutil.which(args.dotnet or "dotnet")
    if dotnet is None:
        parser.error("dotnet executable unavailable; pass --dotnet explicitly")
    dotnet = str(Path(dotnet).resolve())
    if not (data / "paths.txt").is_file():
        parser.error("--data must contain paths.txt; no assets or configuration will be generated")
    if output.exists():
        parser.error("--output already exists; use a new directory to preserve earlier attempts")
    for path in HASH_FILES.values():
        if not path.is_file():
            parser.error(f"Required prebuilt binary missing: {path}; this runner never builds")
    initial = hashes()
    output.mkdir(parents=True, exist_ok=False)
    results, failure = [], None
    started_all = time.monotonic()
    if any(value is None for value in initial.values()):
        failure = "Required prebuilt binary could not be read; no children were run"
    elif initial["main_game"] != initial["tool_game_copy"]:
        failure = "Main and tool-copy game DLLs differ; rebuild/copy the tool explicitly before running"
    for index, room in enumerate(ROOMS):
        if failure:
            break
        name = f"{index:02d}-" + re.sub(r"[^a-zA-Z0-9]+", "-", room).strip("-")
        user_data = output / "user-data" / name
        user_data.mkdir(parents=True)
        shutil.copyfile(data / "paths.txt", user_data / "paths.txt")
        env = dict(os.environ, PROJECT_PRIME_USER_DATA=str(user_data), DOTNET_PROCESSOR_COUNT="2")
        command = [dotnet, str(BINARY), "--stock-pickup-scene", str(data), room]
        before, code, reason = hashes(), None, None
        started = time.monotonic()
        with (output / f"{name}.log").open("w") as log:
            log.write(f"cwd: {ROOT}\ncommand: {shlex.join(command)}\n")
            log.write(f"PROJECT_PRIME_USER_DATA={user_data}\nDOTNET_PROCESSOR_COUNT=2\n")
            log.flush()
            if before != initial:
                reason = "Binary changed before launch; child was not run"
            else:
                try:
                    code = subprocess.run(command, cwd=ROOT, env=env, stdout=log,
                                          stderr=subprocess.STDOUT, timeout=120).returncode
                except (subprocess.TimeoutExpired, OSError) as error:
                    reason = str(error)
                    log.write(f"\nRUNNER FAILURE: {reason}\n")
        after = hashes()
        text = (output / f"{name}.log").read_text(errors="replace")
        marker = re.search(r"PASS \d+ stock pickup scene checks for " + re.escape(room) + r";", text)
        passed = code == 0 and before == initial == after and marker is not None and reason is None
        if not passed:
            reason = reason or ("Binary hashes changed" if before != initial or after != initial
                                else "Child failed or expected room-specific acceptance marker is absent")
            failure = f"{room}: {reason}"
        results.append(dict(room=room, command=command, cwd=str(ROOT),
                            environment={key: env[key] for key in ("PROJECT_PRIME_USER_DATA", "DOTNET_PROCESSOR_COUNT")},
                            exit_code=code, seconds=round(time.monotonic() - started, 6),
                            result="PASS" if passed else "FAIL", reason=reason,
                            hashes_before=before, hashes_after=after, log=f"{name}.log"))
        (output / "results.json").write_text(json.dumps(results, indent=2) + "\n")
        print(f"{'PASS' if passed else 'FAIL'} {room} ({results[-1]['seconds']:.3f}s)", flush=True)
    final = hashes()
    if final != initial:
        failure = failure or "Binary hashes changed before cohort completion"
    passed_count = sum(row["result"] == "PASS" for row in results)
    success = passed_count == len(ROOMS) and failure is None
    summary = dict(result="PASS" if success else "FAIL", planned=len(ROOMS), completed=len(results),
                   passed=passed_count, not_run=len(ROOMS) - len(results), reason=failure,
                   seconds=round(time.monotonic() - started_all, 6), hashes_before=initial,
                   hashes_after=final, hash_files={key: str(path) for key, path in HASH_FILES.items()},
                   data=str(data), results="results.json")
    (output / "results.json").write_text(json.dumps(results, indent=2) + "\n")
    (output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    print(f"{summary['result']} {passed_count}/{len(ROOMS)} stock pickup rooms; {summary['not_run']} not run; evidence: {output}")
    if failure:
        print(failure)
    return 0 if success else 1


if __name__ == "__main__":
    raise SystemExit(main())
