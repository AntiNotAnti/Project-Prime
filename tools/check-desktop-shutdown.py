#!/usr/bin/env python3
"""Run on each desktop OS: python3 tools/check-desktop-shutdown.py <app command...>."""
import os
import argparse
from pathlib import Path
import subprocess
import sys
import tempfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--paths-file", type=Path, help="Enable loaded-match close checks using these extracted game paths")
parser.add_argument("command", nargs=argparse.REMAINDER)
args = parser.parse_args()
if not args.command:
    parser.error("an app command is required")

# Check actual process exit, not just the window disappearing or a success
# message printed before native teardown. Reuse state across fresh processes.
with tempfile.TemporaryDirectory(prefix="prime-shutdown-") as directory:
    env = dict(os.environ, PROJECT_PRIME_USER_DATA=directory)
    if args.paths_file:
        Path(directory, "paths.txt").write_bytes(args.paths_file.read_bytes())
    cases = [[], ["-audiocheck"]]
    if args.paths_file:
        cases.append(["-matchclosecheck"])
    for flags in cases:
        for attempt in range(2):
            command = args.command + ["-windowcheck", "-debuglog"] + flags
            result = subprocess.run(command, env=env, capture_output=True,
                                    text=True, timeout=120 if "-matchclosecheck" in flags else 30)
            print(result.stdout, end="")
            print(result.stderr, end="", file=sys.stderr)
            if result.returncode or "Launcher window check passed." not in result.stdout:
                raise SystemExit(result.returncode or 1)
            if "-audiocheck" in flags and "[windowcheck] audio device started" not in result.stdout:
                raise SystemExit("This build does not support the audio close check.")
            if "-matchclosecheck" in flags and "[windowcheck] match loaded" not in result.stdout:
                raise SystemExit("This build does not support the loaded-match close check.")
            print(f"PASS: {flags or ['launcher']}, launch {attempt + 1}, process exited")
