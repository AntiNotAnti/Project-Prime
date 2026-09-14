#!/usr/bin/env python3
"""Write the portable server example with RID-specific packaged apphost names."""
from __future__ import annotations

import argparse
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
TEMPLATE = ROOT / "tools" / "server.example.json"
PORTABLE_WORKER = "worker/ProjectPrime.Server.Worker"
WINDOWS_WORKER = PORTABLE_WORKER + ".exe"
SUPPORTED_RIDS = {"linux-x64", "linux-arm64", "win-x64", "osx-arm64"}


def packaged_config(rid: str) -> dict:
    if rid not in SUPPORTED_RIDS:
        raise ValueError(f"unsupported server RID: {rid}")
    config = json.loads(TEMPLATE.read_text(encoding="utf-8"))
    processes = config["Node"]["Workers"]["Processes"]
    if len(processes) != 1 or processes[0].get("FileName") != PORTABLE_WORKER:
        raise ValueError("portable server example has an unexpected Worker launch contract")
    if rid == "win-x64":
        processes[0]["FileName"] = WINDOWS_WORKER
    return config


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True, choices=sorted(SUPPORTED_RIDS))
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    args.output.write_text(
        json.dumps(packaged_config(args.rid), indent=2) + "\n", encoding="utf-8"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
