#!/usr/bin/env python3
"""Bounded black-box smoke for the protected Linux single-file client."""

from __future__ import annotations

import argparse
import os
import re
import signal
import subprocess
import sys
from pathlib import Path


LAUNCHER_MARKER = re.compile(r"^[ \t]+Project Prime(?:[ \t]|\()", re.MULTILINE)


def run_smoke(package: Path, timeout_seconds: float) -> None:
    package = package.resolve()
    executable = package / "ProjectPrime"
    if not package.is_dir():
        raise ValueError(f"protected Linux package directory does not exist: {package}")
    if not executable.is_file() or not os.access(executable, os.X_OK):
        raise ValueError(f"protected Linux client is missing or not executable: {executable}")

    process = subprocess.Popen(
        [str(executable), "-launcher", "-text"],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        start_new_session=True,
        encoding="utf-8",
        errors="replace",
        cwd=package,
    )
    try:
        stdout, stderr = process.communicate("q\n", timeout=timeout_seconds)
    except subprocess.TimeoutExpired as error:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        stdout, stderr = process.communicate()
        if stdout:
            print(stdout, end="" if stdout.endswith("\n") else "\n")
        if stderr:
            print(stderr, file=sys.stderr, end="" if stderr.endswith("\n") else "\n")
        raise ValueError(f"protected Linux client did not exit within {timeout_seconds:g}s") from error

    combined = stdout + stderr
    if combined:
        print(combined, end="" if combined.endswith("\n") else "\n")
    if process.returncode != 0:
        raise ValueError(f"protected Linux client exited with status {process.returncode}")
    if not LAUNCHER_MARKER.search(stdout):
        raise ValueError("protected Linux client output omitted the Project Prime launcher marker")
    print("Protected Linux single-file launcher smoke passed.")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--package", required=True, type=Path,
                        help="Protected linux-x64 publish directory")
    parser.add_argument("--timeout", type=float, default=20,
                        help="Maximum launcher runtime in seconds (default: 20)")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.timeout <= 0 or args.timeout > 120:
        print("linux-client-smoke: --timeout must be greater than 0 and at most 120", file=sys.stderr)
        return 2
    try:
        run_smoke(args.package, args.timeout)
        return 0
    except (OSError, ValueError) as error:
        print(f"linux-client-smoke: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
