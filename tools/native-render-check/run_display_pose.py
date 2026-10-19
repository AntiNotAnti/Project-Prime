#!/usr/bin/env python3
"""Run serial, immutable native display-pose arms with asset-only user data."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import time

from campaign_identity import asset_identity, isolate_user_data, sha256
from analyze_display_pose import HUNTERS, RATES, analyze


def write(path, value):
    with path.open("x") as stream:
        json.dump(value, stream, indent=2)
        stream.write("\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--binary", type=Path, required=True)
    parser.add_argument("--user-data-template", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--hunters", nargs="+", choices=HUNTERS, default=HUNTERS)
    parser.add_argument("--rates", nargs="+", type=int, choices=RATES, default=RATES)
    parser.add_argument("--timeout", type=int, default=180)
    args = parser.parse_args()
    binary = args.binary.resolve(strict=True)
    output = args.output.resolve()
    output.mkdir()  # Refuse to overwrite any earlier attempt.
    runtime = [{"path": str(p.relative_to(binary.parent)), "sha256": sha256(p), "bytes": p.stat().st_size}
               for p in sorted(binary.parent.rglob("*")) if p.is_file()]
    assets = asset_identity(args.user_data_template / "paths.txt")
    # The required Samus 60/144 pilot comes first when present in the request.
    cells = [(h, r) for h in args.hunters for r in args.rates]
    pilot = [cell for cell in [("Samus", 60), ("Samus", 144)] if cell in cells]
    cells = pilot + [cell for cell in cells if cell not in pilot]
    write(output / "plan.json", {"schema": 1, "binary": str(binary), "runtime": runtime,
          "assets": assets, "cells": cells, "scenarioId": "native-pose-charge-retreat-v2",
          "scope": "native correctness/readbacks only; no timing, scanout or OS-focus acceptance"})
    reports, receipts = [], []
    failed = False
    for ordinal, (hunter, rate) in enumerate(cells, 1):
        cell = output / f"{ordinal:02d}-{hunter.lower()}-{rate}"
        cell.mkdir()
        user_data, identity = isolate_user_data(args.user_data_template, cell)
        if identity["sha256"] != assets["sha256"]:
            raise RuntimeError("Assets changed before a native arm")
        if any(sha256(binary.parent / name) != next(row["sha256"] for row in runtime if row["path"] == name)
               for name in [binary.name, "ProjectPrime.dll"]):
            raise RuntimeError("Frozen native harness/client changed")
        work = cell / "work"
        work.mkdir()
        command = [str(args.dotnet.resolve()), str(binary), "-posecheck", "-pose-pixels",
                   "-pose-scenario", "native-pose-charge-retreat-v2", "-room", "MP3 PROVING GROUND",
                   "-players", "2", "-hunter", hunter, "-seconds", "24", "-hz", str(rate),
                   "-size", "1280x720", "-shadows", "Off", "-shots", str(cell / "shots"),
                   "-output", str(cell / "result.json")]
        environment = dict(os.environ, PROJECT_PRIME_USER_DATA=str(user_data))
        started = time.monotonic()
        timed_out = False
        with (cell / "console.log").open("x") as log:
            process = subprocess.Popen(command, cwd=work, env=environment, stdout=log, stderr=subprocess.STDOUT)
            try:
                code = process.wait(timeout=args.timeout)
            except subprocess.TimeoutExpired:
                process.kill()
                code = process.wait()
                timed_out = True
        report = json.loads((cell / "result.json").read_text()) if (cell / "result.json").is_file() else {}
        reports.append(report)
        analysis = analyze([report], rates=(rate,), hunters=(hunter,), require_pixel_controls=True,
                           scenario_id="native-pose-charge-retreat-v2", require_particle_sampling=True)
        write(cell / "analysis.json", analysis)
        passed = code == 0 and not timed_out and not analysis["errors"]
        previous = next((row for row in reports[:-1] if row.get("hunter") == hunter), None)
        if passed and previous is not None:
            pair = analyze([previous, report], rates=(previous["requestedOutputScheduleHz"], rate),
                           hunters=(hunter,), require_pixel_controls=True,
                           scenario_id="native-pose-charge-retreat-v2", require_particle_sampling=True)
            write(cell / "cross-rate-analysis.json", pair)
            passed = not pair["errors"]
        receipt = {"hunter": hunter, "rate": rate, "command": command, "exitCode": code,
                   "timedOut": timed_out, "wallSeconds": time.monotonic() - started,
                   "status": "PASS" if passed else "FAIL", "resultSha256": sha256(cell / "result.json")
                       if (cell / "result.json").is_file() else None}
        receipts.append(receipt)
        write(cell / "receipt.json", receipt)
        print(f"{ordinal}/{len(cells)} {hunter}/{rate}: {receipt['status']}", flush=True)
        if not passed:
            failed = True
            break
    unchanged = all((binary.parent / row["path"]).is_file() and sha256(binary.parent / row["path"]) == row["sha256"]
                    for row in runtime)
    assets_unchanged = asset_identity(args.user_data_template / "paths.txt")["sha256"] == assets["sha256"]
    analysis = analyze(reports, rates=tuple(args.rates), hunters=tuple(args.hunters), require_pixel_controls=True,
                       scenario_id="native-pose-charge-retreat-v2", require_particle_sampling=True)
    write(output / "analysis.json", analysis)
    write(output / "summary.json", {"status": "PASS" if not failed and unchanged and assets_unchanged and not analysis["errors"] else "FAIL_OR_INCOMPLETE",
          "runtimeUnchanged": unchanged, "assetsUnchanged": assets_unchanged,
          "passed": sum(row["status"] == "PASS" for row in receipts), "requested": len(cells),
          "notRun": len(cells) - len(receipts), "cells": receipts})
    return 0 if not failed and unchanged and assets_unchanged and not analysis["errors"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
