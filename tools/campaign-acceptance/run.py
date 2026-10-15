#!/usr/bin/env python3
"""CAMP-07 acceptance receipts. Runs each content-free contract in a fresh process.

This is NOT a playable campaign emulator. Do not equate its PASS with native
GL/audio/controller/ROM acceptance. Asset-dependent ownership is opt-in only.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
NETTEST = ROOT / "tools" / "nettest" / "nettest.csproj"
CASES = (
    "scene-lifetime",
    "post-match-save",
    "campaign-save-management",
    "campaign-navigation",
    "campaign-ship-flow",
    "campaign-movies",
    "campaign-endings",
    "campaign-presentation",
    "campaign-audio",
    "campaign-journey",
    "campaign-dialog-input",
    "campaign-delano7",
    "foreground-binding",
)
GUARDRAILS = ("pickup-protocol", "compact-intents", "pickup-replay")


def describe_process(args: list[str]) -> str:
    try:
        result = subprocess.run(args, cwd=ROOT, capture_output=True, text=True,
                                timeout=10, check=False)
        return result.stdout.strip() if result.returncode == 0 else "UNAVAILABLE"
    except (OSError, subprocess.TimeoutExpired):
        return "UNAVAILABLE"


def main() -> int:
    parser = argparse.ArgumentParser(description="CAMP-07 content-free release receipts")
    parser.add_argument("--output", required=True, type=Path,
                        help="New evidence directory; an existing receipt is never overwritten")
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--expected-commit",
                        help="Require the exact checked-out PR head/release commit")
    parser.add_argument("--include-guardrails", action="store_true",
                        help="Also test multiplayer protocol / replay contracts")
    parser.add_argument("--assets-dir", type=Path,
                        help="Optional local extracted AMHE1 filesystem. Never upload this.")
    parser.add_argument("--replay", type=Path,
                        help="Optional local replay; only used with --assets-dir")
    parser.add_argument("--asset-loads", type=int, default=10)
    parser.add_argument("--timeout", type=int, default=180,
                        help="Maximum seconds per independent test command")
    args = parser.parse_args()
    if args.replay is not None and args.assets_dir is None:
        parser.error("--replay requires --assets-dir")
    if not 1 <= args.asset_loads <= 500:
        parser.error("--asset-loads must be in 1..500")
    if args.timeout < 10:
        parser.error("--timeout must be at least 10 seconds")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    receipt = output / "campaign-acceptance.json"
    if receipt.exists():
        parser.error("Receipt already exists; preserve old evidence and choose a fresh output")

    dotnet_version = describe_process(["dotnet", "--version"])
    git_sha = describe_process(["git", "rev-parse", "HEAD"])
    if args.expected_commit and git_sha.lower() != args.expected_commit.lower():
        parser.error("Checkout is not the expected commit; do not record stale acceptance evidence")
    assembly = ROOT / "tools" / "nettest" / "bin" / args.configuration / "net10.0" / "nettest.dll"
    asm_hash = hashlib.sha256(assembly.read_bytes()).hexdigest() if assembly.is_file() else "NOT_FOUND"
    records: list[dict[str, object]] = []

    def execute(name: str, command: list[str], *, runtime: bool = False) -> bool:
        start = time.monotonic()
        log_file = output / (name.replace("/", "-") + ".log")
        result = "FAIL"
        code: int | None = None
        summary = ""
        try:
            completed = subprocess.run(command, cwd=ROOT, stdout=subprocess.PIPE,
                                       stderr=subprocess.STDOUT, text=True,
                                       errors="replace", timeout=args.timeout,
                                       check=False)
            code = completed.returncode
            summary = completed.stdout
            if code == 0:
                result = "PASS"
        except subprocess.TimeoutExpired as exc:
            result = "TIMEOUT"
            message = exc.stdout or b""
            if isinstance(message, bytes):
                message = message.decode("utf-8", errors="replace")
            summary = message + "\nCAMP-07 TIMEOUT: " + name
        except OSError as exc:
            summary = type(exc).__name__ + ": " + str(exc)
        elapsed = round(time.monotonic() - start, 3)
        log_file.write_text(summary, encoding="utf-8", errors="replace")
        records.append({
            "case": name, "status": result, "exitCode": code, "seconds": elapsed,
            "log": log_file.name, "logSha256": hashlib.sha256(
                log_file.read_bytes()).hexdigest(),
            "scope": "asset-backed headless world lifecycle" if runtime
                     else "asset-free process-isolated contract",
        })
        print(f"{result:7s} {name:29s} {elapsed:7.2f}s", flush=True)
        if result != "PASS":
            print("  see " + str(log_file), flush=True)
        return result == "PASS"

    try:
        for case in (*CASES, *(GUARDRAILS if args.include_guardrails else ())):
            execute(case, ["dotnet", "run", "--project", str(NETTEST),
                           "-c", args.configuration, "--no-build", "--",
                           "--" + case])
        if args.assets_dir is not None:
            if not args.assets_dir.is_dir() or (
                args.replay is not None and not args.replay.is_file()
            ):
                records.append({"case": "asset-lifecycle", "status": "FAIL",
                                "reason": "local asset/replay path missing"})
            else:
                native_receipt = output / "campaign-native-scene.json"
                execute("campaign-native-scene", [
                    "dotnet", "run", "--project", str(NETTEST),
                    "-c", args.configuration, "--no-build", "--",
                    "--campaign-native-scene", str(args.assets_dir.resolve()),
                    str(native_receipt)
                ], runtime=True)
                asset_receipt = output / "asset-lifecycle.json"
                execute("asset-lifecycle", [
                    "dotnet", "run", "--project", str(NETTEST),
                    "-c", args.configuration, "--no-build", "--",
                    "--asset-lifecycle", str(args.assets_dir.resolve()),
                    str(args.asset_loads),
                    str(args.replay.resolve()) if args.replay else "-",
                    str(asset_receipt)
                ], runtime=True)
    finally:
        passed = sum(item["status"] == "PASS" for item in records)
        overall = "PASS" if passed == len(records) and len(records) > 0 else "FAIL"
        content = {
            "schema": 1,
            "status": overall,
            "createdUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
            "scope": "campaign contract acceptance, not physical gameplay completion",
            "gitSha": git_sha,
            "dotnetVersion": dotnet_version,
            "os": platform.platform(),
            "architecture": platform.machine(),
            "runtime": sys.version.split()[0],
            "nettestSha256": asm_hash,
            "assetBacked": ("REQUESTED" if args.assets_dir else "NOT_RUN"),
            "nativeDeviceAudioGraphics": "NOT_RUN",
            "actualCampaignCombatScanningMoviesCredits": "NOT_RUN",
            "nativeAdventureSceneSmoke": "REQUESTED" if args.assets_dir else "NOT_RUN",
            "multiplayerLiveProtocolInteroperability": "NOT_RUN",
            "replayHistoricalMediaPlayback": "NOT_RUN",
            "passed": passed,
            "total": len(records),
            "cases": records,
        }
        # Create-only receipt avoids confusing stale evidence with new results.
        with receipt.open("x", encoding="utf-8") as handle:
            json.dump(content, handle, indent=2)
            handle.write("\n")
        print(f"CAMP-07 {overall}: {passed}/{len(records)} isolated contracts"
              f"; evidence {receipt}", flush=True)
    return 0 if overall == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
