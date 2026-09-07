"""Deterministic runner tests for mixed-combat soak failure evidence."""

import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import types
import unittest
import sys
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "run-mixed-combat-soak.py"
SPEC = importlib.util.spec_from_file_location("run_mixed_combat_soak", SCRIPT)
RUNNER = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(RUNNER)


class FakeProcess:
    instances = []
    client_exit_code = 0
    server_wait_timeout = False

    def __init__(self, command, stdout, **_):
        self.command = list(command)
        self.stdout = stdout
        self.wait_calls = []
        self.returncode = None
        if "--mixed-soak-server" in self.command:
            self.role = "server"
            stdout.write("[server] listening on UDP 40000\n")
            stdout.flush()
            report = Path(self.command[6])
            if self.client_exit_code == 0:
                report.write_text(json.dumps({
                    "passed": True,
                    "rootShots": [1, 2],
                    "cpuSeconds": 1.0,
                    "allocatedBytesPerTick": 2.0,
                }))
        elif "--mixed-soak-clients" in self.command:
            self.role = "clients"
            self.returncode = self.client_exit_code if self.client_exit_code else None
            report = Path(self.command[5])
            if self.client_exit_code == 0:
                report.write_text(json.dumps({"passed": True}))
        else:
            self.role = "proxy"
            stdout.write("[lag] : listening\n")
            stdout.flush()
        type(self).instances.append(self)

    def poll(self):
        return self.returncode

    def wait(self, timeout=None):
        self.wait_calls.append(timeout)
        if self.role == "server" and timeout == 25 and self.server_wait_timeout:
            raise subprocess.TimeoutExpired(self.command, timeout)
        if self.returncode is None:
            self.returncode = 0
        return self.returncode

    def terminate(self):
        self.returncode = 143

    def kill(self):
        self.returncode = -9


class MixedCombatRunnerTests(unittest.TestCase):
    def setUp(self):
        FakeProcess.instances = []
        FakeProcess.client_exit_code = 0
        FakeProcess.server_wait_timeout = False
        self.temporary = tempfile.TemporaryDirectory(prefix="prime-mixed-soak-runner-")
        self.root = Path(self.temporary.name)
        self.output = self.root / "output"
        self.output.mkdir()
        self.args = types.SimpleNamespace(
            output=self.output,
            dotnet="dotnet",
            data=self.root / "AMHE1",
            rtt=0,
            jitter=0,
            loss=0,
            seconds=10,
            seed=7,
        )

    def tearDown(self):
        self.temporary.cleanup()

    def run_fixture(self):
        with mock.patch.object(RUNNER, "port", return_value=40000), \
             mock.patch.object(RUNNER.subprocess, "Popen", FakeProcess):
            return RUNNER.run(self.args, "on", self.root / "nettest.dll")

    def test_success_keeps_reports_and_exit_codes(self):
        result = self.run_fixture()

        self.assertTrue(result["passed"])
        self.assertFalse(result["incomplete"])
        self.assertEqual({"server": 0, "clients": 0}, result["exitCodes"])
        self.assertEqual([], result.get("errors", []))
        self.assertIn(25, FakeProcess.instances[0].wait_calls)

    def test_early_client_failure_stops_server_without_remaining_soak_wait(self):
        FakeProcess.client_exit_code = 23

        result = self.run_fixture()

        self.assertFalse(result["passed"])
        self.assertTrue(result["incomplete"])
        self.assertEqual(23, result["exitCodes"]["clients"])
        self.assertEqual(143, result["exitCodes"]["server"])
        self.assertTrue(any("clients exited early with code 23" in error for error in result["errors"]))
        self.assertIn("server report missing", " ".join(result["errors"]))
        self.assertIn("clients report missing", " ".join(result["errors"]))
        server = next(process for process in FakeProcess.instances if process.role == "server")
        self.assertNotIn(25, server.wait_calls)
        self.assertTrue(Path(result["logs"]["server"]).exists())
        self.assertTrue(Path(result["logs"]["clients"]).exists())

    def test_main_continues_later_modes_and_writes_incomplete_comparison(self):
        output = self.root / "all-modes"
        failure = {"label": "on", "passed": False, "server": None, "clients": None}
        success = {
            "label": "off", "passed": True, "server": {
                "rootShots": [1], "cpuSeconds": 1.0, "allocatedBytesPerTick": 2.0,
            }, "clients": {"passed": True},
        }
        argv = ["run-mixed-combat-soak.py", "--nettest", str(self.root / "nettest.dll"),
                "--data", str(self.root / "AMHE1"), "--output", str(output),
                "--seconds", "10", "--modes", "on", "off"]
        with mock.patch.object(RUNNER, "run", side_effect=[failure, success]) as run, \
             mock.patch.object(sys, "argv", argv):
            self.assertEqual(1, RUNNER.main())

        self.assertEqual(2, run.call_count)
        summary = json.loads((output / "summary.json").read_text())
        self.assertEqual(["on", "off"], [entry["label"] for entry in summary["runs"]])
        self.assertTrue(summary["comparisons"][0]["incomplete"])

    def test_server_shutdown_timeout_is_reported_without_aborting_runner(self):
        FakeProcess.server_wait_timeout = True

        result = self.run_fixture()

        self.assertFalse(result["passed"])
        self.assertFalse(result["incomplete"])
        self.assertEqual(143, result["exitCodes"]["server"])
        self.assertTrue(any("server did not exit within 25s" in error for error in result["errors"]))


if __name__ == "__main__":
    unittest.main()
