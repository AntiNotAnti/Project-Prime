"""Functional safety tests for the development-stack lifecycle controller."""
from __future__ import annotations

import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest


ROOT = Path(__file__).resolve().parents[2]
CONTROL = ROOT / "tools/dev-stack-control.py"


class DevStackControlTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="prime-stack-control-")
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name)
        self.fake_root = self.base / "repo"
        (self.fake_root / "tools").mkdir(parents=True)
        self.state = self.base / "state"
        self.processes: list[subprocess.Popen[str]] = []

    def tearDown(self):
        for child in self.processes:
            if child.poll() is None:
                child.terminate()
                try:
                    child.wait(timeout=2)
                except subprocess.TimeoutExpired:
                    child.kill()
                    child.wait(timeout=2)

    def run_control(self, action: str, state: Path | None = None):
        return subprocess.run(
            [
                sys.executable,
                str(CONTROL),
                action,
                "--root",
                str(self.fake_root),
                "--state-dir",
                str(state or self.state),
                "--grace-seconds",
                "1",
            ],
            capture_output=True,
            text=True,
            check=False,
        )

    def wait_for(self, path: Path) -> None:
        deadline = time.monotonic() + 5
        while not path.exists() and time.monotonic() < deadline:
            time.sleep(0.05)
        self.assertTrue(path.exists(), f"timed out waiting for {path}")

    def wait_for_command(self, child: subprocess.Popen[str], token: Path) -> None:
        deadline = time.monotonic() + 5
        command = ""
        while time.monotonic() < deadline:
            command = subprocess.run(
                ["ps", "-ww", "-p", str(child.pid), "-o", "command="],
                capture_output=True,
                text=True,
                check=False,
            ).stdout
            if str(token) in command:
                return
            time.sleep(0.05)
        self.fail(f"timed out waiting for fixture command: {command!r}")

    def test_stopped_status_is_read_only(self):
        missing_state = self.base / "never-created"
        result = self.run_control("status", missing_state)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("is stopped", result.stdout)
        self.assertFalse(missing_state.exists())

    def test_stale_scoped_process_is_stopped_but_unrelated_process_survives(self):
        fixture = self.fake_root / "ProjectPrimeServer"
        fixture.write_text(
            "#!/usr/bin/env python3\nimport time\nwhile True: time.sleep(1)\n",
            encoding="utf-8",
        )
        fixture.chmod(0o755)
        stale = subprocess.Popen([str(fixture)], text=True)
        unrelated = subprocess.Popen(["sleep", "60"], text=True)
        self.processes.extend((stale, unrelated))
        self.wait_for_command(stale, fixture)

        result = self.run_control("stop")

        self.assertEqual(0, result.returncode, result.stderr)
        try:
            stale.wait(timeout=3)
        except subprocess.TimeoutExpired:
            command = subprocess.run(
                ["ps", "-ww", "-p", str(stale.pid), "-o", "command="],
                capture_output=True,
                text=True,
                check=False,
            ).stdout
            self.fail(
                "scoped fixture survived controller\n"
                f"command: {command!r}\nstdout: {result.stdout}\nstderr: {result.stderr}"
            )
        self.assertIsNone(unrelated.poll(), "unrelated sleep process was killed")
        self.assertIn("stale Project Prime", result.stdout)

    def test_command_that_only_mentions_server_paths_is_not_stopped(self):
        mentioned = subprocess.Popen(
            [
                sys.executable,
                "-c",
                "import time; time.sleep(60)",
                str(self.fake_root / "ProjectPrimeServer"),
                str(self.state),
            ],
            text=True,
        )
        self.processes.append(mentioned)
        time.sleep(0.15)

        result = self.run_control("stop")

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIsNone(mentioned.poll(), "a command that only mentioned server paths was killed")

    def test_valid_locked_supervisor_reports_running_and_stops_cleanly(self):
        fixture = self.fake_root / "tools/start-dev.sh"
        fixture.write_text(
            """#!/usr/bin/env python3
import fcntl, json, os, pathlib, signal, subprocess, sys, time
state = pathlib.Path(sys.argv[1]).resolve()
state.mkdir(parents=True)
lock = (state / 'supervisor.lock').open('a+')
fcntl.flock(lock.fileno(), fcntl.LOCK_EX)
birth = subprocess.run(['ps', '-p', str(os.getpid()), '-o', 'lstart='], check=True,
                       capture_output=True, text=True).stdout.strip()
metadata = {'pid': os.getpid(), 'birth_token': birth,
            'root': str(pathlib.Path(__file__).resolve().parents[1]),
            'state_dir': str(state), 'script': str(pathlib.Path(__file__).resolve()),
            'package_dir': str((state / 'package').resolve())}
(state / 'supervisor.json').write_text(json.dumps(metadata))
(state / 'ready').write_text('ready')
signal.signal(signal.SIGTERM, lambda *_: sys.exit(0))
while True: time.sleep(1)
""",
            encoding="utf-8",
        )
        fixture.chmod(0o755)
        supervisor = subprocess.Popen([str(fixture), str(self.state)], text=True)
        self.processes.append(supervisor)
        self.wait_for(self.state / "ready")

        status = self.run_control("status")
        self.assertEqual(0, status.returncode, status.stderr)
        self.assertIn("is running", status.stdout)

        stopped = self.run_control("stop")
        self.assertEqual(0, stopped.returncode, stopped.stderr)
        supervisor.wait(timeout=3)
        self.assertFalse((self.state / "supervisor.json").exists())

    def test_held_lock_without_valid_metadata_fails_closed(self):
        self.state.mkdir()
        holder_code = """
import fcntl, pathlib, sys, time
handle = pathlib.Path(sys.argv[1]).open('a+')
fcntl.flock(handle.fileno(), fcntl.LOCK_EX)
pathlib.Path(sys.argv[2]).write_text('ready')
time.sleep(60)
"""
        holder = subprocess.Popen(
            [sys.executable, "-c", holder_code, str(self.state / "supervisor.lock"), str(self.state / "ready")],
            text=True,
        )
        self.processes.append(holder)
        self.wait_for(self.state / "ready")

        result = self.run_control("stop")

        self.assertEqual(2, result.returncode)
        self.assertIn("Refusing to stop", result.stderr)
        self.assertIsNone(holder.poll())


if __name__ == "__main__":
    unittest.main()
