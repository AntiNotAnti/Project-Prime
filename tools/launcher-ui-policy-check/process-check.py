#!/usr/bin/env python3
"""Reject invalid or unavailable UI choices in actual client processes before a window is created."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--native", required=True, type=Path, help="Actual Avalonia-free client DLL")
    parser.add_argument("--transitional", required=True, type=Path, help="Actual client DLL with both presentation implementations")
    parser.add_argument("--default-client", required=True, type=Path, help="Ordinary native client DLL built without presentation properties")
    parser.add_argument("--output", type=Path, help="New empty fixture directory outside user state")
    arguments = parser.parse_args()
    output = arguments.output.resolve() if arguments.output else Path(tempfile.mkdtemp(prefix="prime-ui-process-"))
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        raise ValueError("The process fixture directory must be empty.")
    cases = [
        ("native-invalid", arguments.native, ["-ui=bogus"], "Unknown client UI"),
        ("native-missing", arguments.native, ["-ui"], "Supply auto, rmlui or legacy"),
        ("native-conflict", arguments.native, ["-ui=legacy", "-ui=rmlui"], "Conflicting client UI options"),
        ("native-legacy-unavailable", arguments.native, ["-ui=legacy"], "does not include the legacy presentation"),
        ("native-legacy-alias-precedence", arguments.native, ["-ui=legacy", "-rmlui"], "does not include the legacy presentation"),
        ("transitional-invalid", arguments.transitional, ["-ui=bogus"], "Unknown client UI"),
        ("transitional-conflict", arguments.transitional, ["-ui=rmlui", "-ui=legacy"], "Conflicting client UI options"),
        ("default-legacy-unavailable", arguments.default_client, ["-ui=legacy"], "does not include the legacy presentation"),
    ]
    report = []
    for name, binary, flags, expected in cases:
        fixture = output / name
        fixture.mkdir()
        environment = os.environ.copy()
        environment["PROJECT_PRIME_USER_DATA"] = str(fixture)
        environment["PROJECT_PRIME_UI_PERF"] = str(fixture / "unused-performance.json")
        process = subprocess.run([arguments.dotnet, str(binary.resolve()), *flags], cwd=fixture,
                                 env=environment, capture_output=True, text=True, timeout=30)
        logs = "\n".join(path.read_text(errors="replace") for path in (fixture / "logs").glob("*") if path.is_file())
        text = process.stdout + process.stderr + logs
        (fixture / "process.log").write_text(text)
        if process.returncode == 0 or expected not in text:
            raise RuntimeError(f"{name}: expected nonzero rejection; diagnostics: {fixture / 'process.log'}")
        if "creating the game window" in text.lower():
            raise RuntimeError(f"{name}: rejection happened after window creation")
        if list(fixture.glob("ui-startup-*.json")):
            raise RuntimeError(f"{name}: a diagnostic run wrote a startup recovery record")
        report.append(dict(case=name, exitCode=process.returncode, expectedError=expected,
                           windowCreated=False, startupRecordCreated=False))
    (output / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    print(f"ACTUAL UI PROCESS PASS {len(report)} startup rejection cases; evidence: {output}")


if __name__ == "__main__":
    main()
