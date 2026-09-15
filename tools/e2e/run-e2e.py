#!/usr/bin/env python3
"""Run a guarded, free-running Project Prime two-client lifecycle.

This coordinator owns only the direct processes it starts. It never pauses a
client and never turns missing live prerequisites into a passing result.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import secrets
import shlex
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
MILESTONES = (
    "launcher-ready", "lobby-created", "lobby-joined", "both-ready",
    "match-loading", "match-start", "damage-confirmed", "death-confirmed",
    "respawn-confirmed", "match-end", "results", "rematch", "lobby-return",
)
ASSERTIONS = (
    "client-a-session-continuity", "client-b-session-continuity",
    "distinct-client-identities", "same-match", "map-and-mode", "hunter-selections",
    "both-connected", "worker-owns-match", "movement-reflected",
    "damage-authoritative", "death-authoritative", "respawn-authoritative",
    "results-agree", "rematch-new-phase", "return-lobby-preserves-session",
)


def strict_load(path: Path) -> object:
    def pairs(items: list[tuple[str, object]]) -> dict[str, object]:
        result: dict[str, object] = {}
        for key, value in items:
            if key in result:
                raise ValueError(f"duplicate JSON property: {key}")
            result[key] = value
        return result

    raw = path.read_bytes()
    if len(raw) == 0 or len(raw) > 2 * 1024 * 1024:
        raise ValueError(f"evidence file is empty or too large: {path}")
    def reject_constant(value: str) -> object:
        raise ValueError(f"non-finite JSON number: {value}")

    return json.loads(raw.decode("utf-8"), object_pairs_hook=pairs,
                      parse_constant=reject_constant)


def bounded_text(value: object, label: str, allow_empty: bool = False) -> None:
    if not isinstance(value, str) or len(value) > 256 \
            or (not allow_empty and not value) \
            or any(ord(char) < 0x20 or ord(char) == 0x7F for char in value):
        raise ValueError(f"{label} is not bounded text")


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def delete_run_tokens(paths: tuple[Path, Path]) -> None:
    """Remove only the two token files created by this coordinator run."""
    for path in paths:
        try:
            path.unlink()
        except FileNotFoundError:
            pass
        except OSError as error:
            print(f"could not remove run token {path}: {error}", file=sys.stderr)


def delete_private_runtime(path: Path) -> None:
    """Remove exactly this run's private runtime directory, never evidence."""
    if path.is_symlink():
        print(f"refusing to remove symbolic-link runtime directory: {path}", file=sys.stderr)
        return
    try:
        shutil.rmtree(path)
    except FileNotFoundError:
        pass
    except OSError as error:
        print(f"could not remove private runtime directory {path}: {error}", file=sys.stderr)


def write_sanitized_server_evidence(run_dir: Path,
                                    processes: list[OwnedProcess]) -> None:
    """Record process outcomes without exporting private stack state or secrets."""
    entries = []
    for owned in processes:
        entries.append({
            "name": owned.name,
            "exitCode": owned.process.poll(),
        })
    write_json(run_dir / "server-evidence.json", {
        "schemaVersion": 1,
        "processes": entries,
        "backendNodeLogs": "not-exported; private runtime state remains outside the evidence bundle",
        "workerEvidence": "unavailable; ManagedWorker discards authoritative Worker stdout and stderr",
    })


def initial_evidence(run_dir: Path, run_id: str) -> None:
    write_json(run_dir / "milestones.json", {
        "schemaVersion": 1,
        "runId": run_id,
        "milestones": {name: "not-run" for name in MILESTONES},
    })
    write_json(run_dir / "assertions.json", {
        "schemaVersion": 1,
        "runId": run_id,
        # Until the external driver proves the lifecycle with the shipping
        # owners, this bundle is explicitly not a live-evidence claim.
        "live": False,
        "status": "not-run",
        "assertions": [
            {"id": name, "status": "not-run", "detail": ""}
            for name in ASSERTIONS
        ],
    })
    write_json(run_dir / "network.json", {
        "schemaVersion": 1,
        "runId": run_id,
        "status": "not-run",
    })


def validate_evidence(run_dir: Path, run_id: str) -> None:
    assertions = strict_load(run_dir / "assertions.json")
    if not isinstance(assertions, dict) \
            or set(assertions) != {"schemaVersion", "runId", "live", "status", "assertions"} \
            or assertions["schemaVersion"] != 1 \
            or assertions["runId"] != run_id \
            or not isinstance(assertions["live"], bool) \
            or assertions["status"] not in {"passed", "failed", "not-run"}:
        raise ValueError("assertions schema or identity is invalid")
    entries = assertions["assertions"]
    if not isinstance(entries, list) or len(entries) != len(ASSERTIONS):
        raise ValueError("assertions must contain the fixed semantic assertion set")
    seen: set[str] = set()
    for entry in entries:
        if not isinstance(entry, dict) or set(entry) != {"id", "status", "detail"}:
            raise ValueError("assertion entry schema is invalid")
        bounded_text(entry["id"], "assertion id")
        bounded_text(entry["detail"], "assertion detail", allow_empty=True)
        if entry["id"] in seen or entry["status"] not in {"passed", "failed", "not-run"}:
            raise ValueError("assertion identity/status is invalid")
        seen.add(entry["id"])
    if seen != set(ASSERTIONS):
        raise ValueError("assertion IDs do not match the fixed lifecycle")

    milestones = strict_load(run_dir / "milestones.json")
    if not isinstance(milestones, dict) \
            or set(milestones) != {"schemaVersion", "runId", "milestones"} \
            or milestones["schemaVersion"] != 1 \
            or milestones["runId"] != run_id \
            or not isinstance(milestones["milestones"], dict) \
            or set(milestones["milestones"]) != set(MILESTONES):
        raise ValueError("milestones schema or identity is invalid")
    if any(value not in {"passed", "failed", "not-run"}
           for value in milestones["milestones"].values()):
        raise ValueError("milestone status is invalid")
    if assertions["status"] == "passed" and any(
            entry["status"] != "passed" for entry in entries):
        raise ValueError("passed evidence contains a non-passed assertion")
    if assertions["status"] == "passed" and any(
            value != "passed" for value in milestones["milestones"].values()):
        raise ValueError("passed evidence contains a non-passed milestone")
    if assertions["status"] == "passed" and assertions["live"] is not True:
        raise ValueError("passed evidence must explicitly identify a live run")


class OwnedProcess:
    def __init__(self, name: str, command: str, log_path: Path, env: dict[str, str]):
        self.name = name
        self.args = shlex.split(command)
        if not self.args:
            raise ValueError(f"{name} command is empty")
        self.log_file = log_path.open("wb")
        self.process = subprocess.Popen(
            self.args, cwd=ROOT, env=env,
            stdout=self.log_file, stderr=subprocess.STDOUT,
            start_new_session=False,
        )

    @property
    def pid(self) -> int:
        return self.process.pid

    def close_log(self) -> None:
        self.log_file.close()


def terminate_owned(processes: list[OwnedProcess]) -> None:
    for owned in processes:
        if owned.process.poll() is None:
            try:
                owned.process.send_signal(signal.SIGTERM)
            except ProcessLookupError:
                pass
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline and any(
            owned.process.poll() is None for owned in processes):
        time.sleep(.1)
    for owned in processes:
        if owned.process.poll() is None:
            try:
                owned.process.kill()
            except ProcessLookupError:
                pass
    for owned in processes:
        try:
            owned.process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            pass
        owned.close_log()


def main() -> int:
    def stop_signal(_signum: int, _frame: object) -> None:
        raise KeyboardInterrupt

    signal.signal(signal.SIGINT, stop_signal)
    signal.signal(signal.SIGTERM, stop_signal)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-id")
    parser.add_argument("--artifact-root", default=str(ROOT / "artifacts/e2e"))
    args = parser.parse_args()
    if os.environ.get("PRIME_E2E_ENABLE") != "1":
        print("E2E is disabled; set PRIME_E2E_ENABLE=1 for an explicit development run.", file=sys.stderr)
        return 2
    run_id = args.run_id or f"{time.strftime('%Y%m%dT%H%M%SZ', time.gmtime())}-{uuid.uuid4().hex[:12]}"
    if len(run_id) > 64 or not run_id or not all(
            char.isalnum() or char in "-_" for char in run_id):
        print("run ID is invalid", file=sys.stderr)
        return 2

    client_a = os.environ.get("PRIME_E2E_CLIENT_A_COMMAND", "")
    client_b = os.environ.get("PRIME_E2E_CLIENT_B_COMMAND", "")
    driver = os.environ.get("PRIME_E2E_DRIVER_COMMAND", "")
    if not client_a or not client_b or not driver:
        print("E2E requires PRIME_E2E_CLIENT_A_COMMAND, PRIME_E2E_CLIENT_B_COMMAND, and PRIME_E2E_DRIVER_COMMAND.", file=sys.stderr)
        return 2

    artifact_root = Path(args.artifact_root).expanduser().resolve(strict=False)
    if artifact_root.exists() and artifact_root.is_symlink():
        print(f"artifact root must not be a symbolic link: {artifact_root}", file=sys.stderr)
        return 1
    artifact_root.mkdir(parents=True, exist_ok=True)
    run_dir = artifact_root / run_id
    if run_dir.exists():
        print(f"run directory already exists; refusing stale evidence reuse: {run_dir}", file=sys.stderr)
        return 1
    (run_dir / "client-a/screenshots").mkdir(parents=True)
    (run_dir / "client-b/screenshots").mkdir(parents=True)
    run_dir.chmod(0o700)
    initial_evidence(run_dir, run_id)

    # These are placeholders until the private runtime directory exists; no
    # token is ever created under the public evidence bundle.
    token_a = run_dir / ".unused-client-a.token"
    token_b = run_dir / ".unused-client-b.token"
    token_paths = (token_a, token_b)
    private_runtime: Path | None = None
    try:
        private_runtime = Path(tempfile.mkdtemp(prefix="pe-"))
        private_runtime.chmod(0o700)
        token_a = private_runtime / "client-a.token"
        token_b = private_runtime / "client-b.token"
        token_paths = (token_a, token_b)
        for token in token_paths:
            token.write_text(secrets.token_urlsafe(32) + "\n", encoding="utf-8")
            token.chmod(0o600)
    except Exception:
        # A partial token setup must not leave the first secret behind if the
        # second file cannot be created.
        delete_run_tokens(token_paths)
        if private_runtime is not None:
            delete_private_runtime(private_runtime)
        raise
    private_state = private_runtime / "server-state"
    env = os.environ.copy()
    env.update({
        "PRIME_E2E_RUN_DIR": str(run_dir),
        "PRIME_E2E_CLIENT_A_TOKEN_FILE": str(token_a),
        "PRIME_E2E_CLIENT_B_TOKEN_FILE": str(token_b),
        "PRIME_E2E_SERVER_STATE_DIR": str(private_state),
    })

    if os.name == "nt":
        # Named pipes are names, not filesystem paths. Keep the value within
        # the bounded Windows endpoint grammar even for a long run ID.
        pipe_token = run_id[:32]
        endpoint_a = f"prime-e2e-{pipe_token}-a"
        endpoint_b = f"prime-e2e-{pipe_token}-b"
    else:
        # Keep socket endpoints in the private mode-700 runtime directory,
        # never in the evidence bundle. The protocol's portable sockaddr_un
        # bound is 104 UTF-8 bytes.
        endpoint_a = str(private_runtime / "a.sock")
        endpoint_b = str(private_runtime / "b.sock")
        if len(os.fsencode(endpoint_a)) > 104 or len(os.fsencode(endpoint_b)) > 104:
            raise ValueError("private E2E socket endpoint exceeds the 104-byte Unix bound")
    env.update({
        "PRIME_E2E_CONTROL_ENDPOINT_A": endpoint_a,
        "PRIME_E2E_CONTROL_ENDPOINT_B": endpoint_b,
    })

    server = os.environ.get("PRIME_E2E_SERVER_COMMAND")
    if not server:
        server = shlex.join([
            str(ROOT / "tools/start-dev.sh"), "--with-backend", "--state-dir",
            str(private_state),
        ])
    processes: list[OwnedProcess] = []
    driver_exit = 1
    evidence: object = {}
    process_failure: str | None = None
    try:
        # Supervisor output stays with the private runtime state. The public
        # bundle receives only the sanitized process summary written after
        # shutdown below; this prevents startup output from exposing generated
        # credentials or private-key paths.
        processes.append(OwnedProcess("server", server,
                                      private_runtime / "server.stdout.log", env))
        # Both clients are started before the driver; no client is paused or
        # run in lockstep with the other one.
        client_a_env = env.copy()
        client_a_env.update({
            "PRIME_E2E_CLIENT_SLOT": "a",
            "PRIME_E2E_CONTROL_ENDPOINT": endpoint_a,
            "PRIME_E2E_CONTROL_TOKEN_FILE": str(token_a),
        })
        client_b_env = env.copy()
        client_b_env.update({
            "PRIME_E2E_CLIENT_SLOT": "b",
            "PRIME_E2E_CONTROL_ENDPOINT": endpoint_b,
            "PRIME_E2E_CONTROL_TOKEN_FILE": str(token_b),
        })
        processes.append(OwnedProcess("client-a", client_a,
                                      run_dir / "client-a/client.log", client_a_env))
        processes.append(OwnedProcess("client-b", client_b,
                                      run_dir / "client-b/client.log", client_b_env))
        driver_process = OwnedProcess("driver", driver, run_dir / "driver.log", env)
        processes.append(driver_process)
        timeout_seconds = min(max(int(os.environ.get("PRIME_E2E_DRIVER_TIMEOUT_SECONDS", "1800")), 1), 3600)
        deadline = time.monotonic() + timeout_seconds
        while driver_process.process.poll() is None:
            for owned in processes[:3]:
                exit_code = owned.process.poll()
                if exit_code is not None:
                    process_failure = f"{owned.name} exited while driver was running ({exit_code})"
                    print(process_failure, file=sys.stderr)
                    driver_exit = 1
                    break
            if process_failure is not None:
                break
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                print(f"E2E driver timed out after {timeout_seconds}s", file=sys.stderr)
                driver_exit = 124
                process_failure = "driver-timeout"
                break
            try:
                driver_exit = driver_process.process.wait(timeout=min(0.1, remaining))
            except subprocess.TimeoutExpired:
                continue
        if process_failure is None:
            for owned in processes[:3]:
                exit_code = owned.process.poll()
                if exit_code is not None:
                    process_failure = f"{owned.name} exited before driver completion ({exit_code})"
                    print(process_failure, file=sys.stderr)
                    driver_exit = 1
                    break
        try:
            validate_evidence(run_dir, run_id)
            evidence = strict_load(run_dir / "assertions.json")
            status = evidence["status"] if isinstance(evidence, dict) else "failed"
            passed = process_failure is None and driver_exit == 0 and status == "passed"
        except (OSError, UnicodeError, ValueError, json.JSONDecodeError) as error:
            print(f"invalid E2E evidence: {error}", file=sys.stderr)
            passed = False
        write_json(run_dir / "summary.json", {
            "schemaVersion": 1,
            "runId": run_id,
            "live": bool(evidence.get("live")) if isinstance(evidence, dict) else False,
            "status": "passed" if passed else "failed",
            "driverExitCode": driver_exit,
            "serverPid": processes[0].pid,
            "clientAPid": processes[1].pid,
            "clientBPid": processes[2].pid,
            "processFailure": process_failure,
            "evidence": "semantic assertions plus supporting logs/screenshots",
        })
        return 0 if passed else 1
    except (OSError, ValueError) as error:
        print(f"E2E setup failed: {error}", file=sys.stderr)
        write_json(run_dir / "summary.json", {
            "schemaVersion": 1, "runId": run_id, "live": False, "status": "failed",
            "driverExitCode": driver_exit,
            "evidence": "not-run or incomplete; inspect logs",
        })
        return 1
    finally:
        terminate_owned(processes)
        try:
            write_sanitized_server_evidence(run_dir, processes)
        except OSError as error:
            print(f"could not write sanitized server evidence: {error}", file=sys.stderr)
        delete_run_tokens(token_paths)
        if private_runtime is not None:
            delete_private_runtime(private_runtime)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        raise SystemExit(130)
