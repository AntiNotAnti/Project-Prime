#!/usr/bin/env python3
"""Safely inspect or stop one Project Prime development stack."""
from __future__ import annotations

import argparse
import fcntl
import json
import os
from pathlib import Path
import signal
import shlex
import subprocess
import sys
import time
from dataclasses import dataclass


@dataclass(frozen=True)
class Process:
    pid: int
    ppid: int
    birth_token: str
    command: str


def canonical(path: str | Path) -> Path:
    return Path(path).expanduser().resolve(strict=False)


def ps_value(pid: int, field: str) -> str | None:
    result = subprocess.run(
        ["ps", "-ww", "-p", str(pid), "-o", f"{field}="],
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode != 0:
        return None
    value = result.stdout.strip()
    return value or None


def process(pid: int, ppid: int | None = None) -> Process | None:
    if pid <= 1:
        return None
    birth = ps_value(pid, "lstart")
    command = ps_value(pid, "command")
    if birth is None or command is None:
        return None
    if ppid is None:
        raw_ppid = ps_value(pid, "ppid")
        if raw_ppid is None:
            return None
        try:
            ppid = int(raw_ppid)
        except ValueError:
            return None
    return Process(pid, ppid, birth, command)


def process_table() -> dict[int, Process]:
    result = subprocess.run(
        ["ps", "-ww", "-axo", "pid=,ppid=,lstart=,command="],
        capture_output=True,
        text=True,
        check=True,
    )
    table: dict[int, Process] = {}
    for line in result.stdout.splitlines():
        fields = line.strip().split(None, 7)
        if len(fields) != 8:
            continue
        try:
            pid, ppid = int(fields[0]), int(fields[1])
        except ValueError:
            continue
        table[pid] = Process(pid, ppid, " ".join(fields[2:7]), fields[7])
    return table


def ancestors(table: dict[int, Process], pid: int) -> set[int]:
    found: set[int] = set()
    while pid in table and table[pid].ppid not in found:
        pid = table[pid].ppid
        found.add(pid)
    return found


def descendants(table: dict[int, Process], parent: int) -> list[Process]:
    selected: set[int] = {parent}
    changed = True
    while changed:
        changed = False
        for item in table.values():
            if item.ppid in selected and item.pid not in selected:
                selected.add(item.pid)
                changed = True
    return [table[pid] for pid in selected if pid != parent and pid in table]


def load_metadata(path: Path) -> tuple[dict[str, object] | None, str | None]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return None, "supervisor.json is absent"
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        return None, f"supervisor.json is unreadable: {error}"
    if not isinstance(value, dict):
        return None, "supervisor.json is not an object"
    return value, None


def validate_metadata(
    metadata: dict[str, object] | None, root: Path, state: Path
) -> tuple[Process | None, list[str]]:
    errors: list[str] = []
    if metadata is None:
        return None, errors
    expected_script = canonical(root / "tools/start-dev.sh")
    expected_paths = {"root": root, "state_dir": state, "script": expected_script}
    for field, expected in expected_paths.items():
        value = metadata.get(field)
        if not isinstance(value, str) or canonical(value) != expected:
            errors.append(f"metadata {field} does not match {expected}")
    package_value = metadata.get("package_dir")
    if not isinstance(package_value, str) or not Path(package_value).is_absolute():
        errors.append("metadata package_dir is not an absolute path")
    elif canonical(package_value) != Path(package_value):
        errors.append("metadata package_dir is not canonical")
    try:
        pid = int(metadata.get("pid", 0))
    except (TypeError, ValueError):
        pid = 0
    item = process(pid)
    if item is None:
        errors.append(f"metadata process {pid} is not running")
        return None, errors
    if metadata.get("birth_token") != item.birth_token:
        errors.append(f"metadata process {pid} has a different birth token")
    script_matches = False
    try:
        for token in shlex.split(item.command):
            command_path = canonical(token) if token.startswith("/") else canonical(root / token)
            if command_path == expected_script:
                script_matches = True
                break
    except ValueError:
        pass
    if not script_matches:
        errors.append(f"metadata process {pid} command is not {expected_script}")
    return (item if not errors else None), errors


def lock_is_held(lock_path: Path) -> tuple[bool, object | None]:
    try:
        handle = lock_path.open("r+")
    except FileNotFoundError:
        return False, None
    try:
        fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        return True, handle
    return False, handle


def scoped_orphans(root: Path, state: Path, metadata: dict[str, object] | None) -> list[Process]:
    table = process_table()
    excluded = ancestors(table, os.getpid()) | {os.getpid()}
    scope_roots = {root, state}
    if metadata is not None and isinstance(metadata.get("package_dir"), str):
        scope_roots.add(canonical(str(metadata["package_dir"])))
    expected_script = canonical(root / "tools/start-dev.sh")
    executable_names = {
        "ProjectPrime.Backend",
        "ProjectPrime.Backend.exe",
        "ProjectPrimeServer",
        "ProjectPrimeServer.exe",
        "ProjectPrime.Server.Node",
        "ProjectPrime.Server.Node.exe",
        "ProjectPrime.Server.Worker",
        "ProjectPrime.Server.Worker.exe",
    }
    backend_project = canonical(root / "src/Backend/Backend.csproj")
    selected_pids: set[int] = set()
    for item in table.values():
        if item.pid in excluded:
            continue
        try:
            command_tokens = shlex.split(item.command)
        except ValueError:
            continue
        if "--state-dir" in command_tokens:
            state_index = command_tokens.index("--state-dir")
            if state_index + 1 >= len(command_tokens) or canonical(command_tokens[state_index + 1]) != state:
                continue
        matched = False
        launch_tokens = command_tokens[:1]
        launcher_name = Path(command_tokens[0]).name.lower() if command_tokens else ""
        if launcher_name == "env":
            launch_tokens = command_tokens[:3]
        elif launcher_name in {"bash", "sh", "zsh", "dotnet"} or launcher_name.startswith("python"):
            launch_tokens = command_tokens[:2]
        if "--project" in command_tokens:
            project_index = command_tokens.index("--project")
            if project_index + 1 < len(command_tokens):
                launch_tokens.append(command_tokens[project_index + 1])
        for token in launch_tokens:
            if not token.startswith("/"):
                continue
            command_path = canonical(token)
            if command_path == expected_script or command_path == backend_project:
                matched = True
                break
            if command_path.name in executable_names and any(
                command_path == scope or scope in command_path.parents for scope in scope_roots
            ):
                matched = True
                break
        if matched:
            selected_pids.add(item.pid)

    # An orphaned supervisor can still own children whose argv contains only
    # runtime pipe/token arguments. Once the parent is exactly scoped, its
    # descendants are part of the same stack and may be stopped with it.
    for pid in tuple(selected_pids):
        selected_pids.update(child.pid for child in descendants(table, pid))
    return [table[pid] for pid in selected_pids if pid in table and pid not in excluded]


def still_same(item: Process) -> bool:
    current = process(item.pid)
    if current is None or current.birth_token != item.birth_token:
        return False
    state = ps_value(item.pid, "stat")
    return state is not None and not state.startswith("Z")


def signal_verified(items: list[Process], sig: signal.Signals) -> None:
    for item in sorted(items, key=lambda value: value.pid, reverse=True):
        if still_same(item):
            try:
                os.kill(item.pid, sig)
            except ProcessLookupError:
                pass


def wait_gone(items: list[Process], seconds: int) -> list[Process]:
    deadline = time.monotonic() + seconds
    remaining = [item for item in items if still_same(item)]
    while remaining and time.monotonic() < deadline:
        time.sleep(0.1)
        remaining = [item for item in remaining if still_same(item)]
    return remaining


def clean_stale_files(state: Path) -> None:
    for name in ("supervisor.json", "supervisor.pid", "node.pid", "backend.pid"):
        try:
            (state / name).unlink()
        except FileNotFoundError:
            pass


def resolve_state(root: Path, explicit: str | None) -> Path:
    if explicit:
        return canonical(explicit)
    configured = os.environ.get("PRIME_DEV_STATE_DIR")
    if configured:
        return canonical(configured)
    temporary = os.environ.get("TMPDIR") or "/tmp"
    return canonical(Path(temporary) / "project-prime-dev")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("status", "stop"))
    parser.add_argument("--root", type=canonical, default=canonical(Path(__file__).parents[1]))
    parser.add_argument("--state-dir")
    parser.add_argument("--grace-seconds", type=int, default=60)
    args = parser.parse_args()
    if args.grace_seconds < 0:
        parser.error("--grace-seconds must be non-negative")
    root = canonical(args.root)
    state = resolve_state(root, args.state_dir)
    metadata, metadata_error = load_metadata(state / "supervisor.json")
    supervisor, validation_errors = validate_metadata(metadata, root, state)
    held, lock_handle = lock_is_held(state / "supervisor.lock")

    if args.action == "status":
        if held and supervisor is not None:
            print(f"Project Prime development stack is running (supervisor {supervisor.pid}).")
            print(f"State: {state}")
            return 0
        if held:
            details = [metadata_error] if metadata_error else validation_errors
            print("Development stack lock is held, but its owner cannot be verified.", file=sys.stderr)
            for detail in details:
                print(f"  {detail}", file=sys.stderr)
            return 2
        orphans = scoped_orphans(root, state, metadata)
        if orphans:
            print(f"Project Prime stack is stale ({len(orphans)} scoped process(es) remain).")
            print(f"State: {state}")
            return 1
        print("Project Prime development stack is stopped.")
        print(f"State: {state}")
        return 0

    if held and supervisor is None:
        details = [metadata_error] if metadata_error else validation_errors
        print("Refusing to stop: the state lock is held but its owner cannot be verified.", file=sys.stderr)
        for detail in details:
            print(f"  {detail}", file=sys.stderr)
        return 2

    targets: list[Process]
    if held and supervisor is not None:
        table = process_table()
        targets = [supervisor] + descendants(table, supervisor.pid)
        print(f"Stopping Project Prime development supervisor {supervisor.pid}...")
        signal_verified([supervisor], signal.SIGTERM)
    else:
        targets = scoped_orphans(root, state, metadata)
        if targets:
            print(f"Recovering {len(targets)} stale Project Prime process(es)...")
            signal_verified(targets, signal.SIGTERM)

    remaining = wait_gone(targets, args.grace_seconds)
    if remaining:
        print(
            f"Warning: {len(remaining)} verified Project Prime process(es) did not stop "
            f"within {args.grace_seconds}s; forcing exit.",
            file=sys.stderr,
        )
        signal_verified(remaining, signal.SIGKILL)
        remaining = wait_gone(remaining, 5)
    if remaining:
        print("Unable to stop all verified Project Prime processes.", file=sys.stderr)
        return 1

    # Re-check the lock. A verified supervisor can take a moment to run cleanup.
    if held:
        if lock_handle is not None:
            lock_handle.close()
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            held, lock_handle = lock_is_held(state / "supervisor.lock")
            if not held:
                break
            if lock_handle is not None:
                lock_handle.close()
            time.sleep(0.1)
    if held:
        if lock_handle is not None:
            lock_handle.close()
        print("The development stack lock remains held after processes exited.", file=sys.stderr)
        return 1
    if scoped_orphans(root, state, metadata):
        if lock_handle is not None:
            lock_handle.close()
        print("Scoped Project Prime processes appeared during shutdown; state was not cleaned.", file=sys.stderr)
        return 1
    clean_stale_files(state)
    if lock_handle is not None:
        lock_handle.close()
    print("Project Prime development stack is stopped.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
