"""Focused contracts for safe whole-stack VPS deployment."""
from __future__ import annotations

import os
from pathlib import Path
import stat
import struct
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
DEPLOY = ROOT / "deploy-server.sh"
VALIDATOR = ROOT / "tools/validate-deploy-inputs.py"
UNIT = ROOT / "tools/systemd/projectprime-stack.service"


class DeployServerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="project-prime-deploy-")
        self.addCleanup(self.temporary.cleanup)
        self.bundle = Path(self.temporary.name) / "bundle"

    @staticmethod
    def elf(machine: int) -> bytes:
        data = bytearray(64)
        data[:6] = b"\x7fELF\x02\x01"
        struct.pack_into("<H", data, 18, machine)
        return bytes(data)

    def make_bundle(self, machine: int = 62) -> None:
        for relative in ("ProjectPrimeServer", "worker/ProjectPrime.Server.Worker", "backend/ProjectPrime.Backend"):
            path = self.bundle / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(self.elf(machine))
            path.chmod(path.stat().st_mode | stat.S_IXUSR)
        launcher = self.bundle / "start-stack-dev.sh"
        launcher.write_text("#!/usr/bin/env bash\nexit 0\n", encoding="utf-8")
        launcher.chmod(0o755)
        (self.bundle / "maps").mkdir()
        with zipfile.ZipFile(self.bundle / "maps/test.fpmap", "w") as archive:
            archive.writestr("test.json", "{}")

    def validate(self, rid: str = "linux-x64") -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(VALIDATOR), "--bundle", str(self.bundle), "--rid", rid],
            capture_output=True, text=True, check=False,
        )

    def test_complete_x64_stack_bundle_passes(self):
        self.make_bundle()
        result = self.validate()
        self.assertEqual(0, result.returncode, result.stderr)

    def test_bundle_rid_mismatch_and_symlink_fail(self):
        self.make_bundle(machine=183)
        self.assertIn("bundle/RID mismatch", self.validate().stderr)
        real = Path(self.temporary.name) / "real"
        self.bundle.replace(real)
        self.bundle.symlink_to(real, target_is_directory=True)
        self.assertIn("symlink", self.validate("linux-arm64").stderr)

    def test_defaults_and_protected_root_are_exact(self):
        script = DEPLOY.read_text(encoding="utf-8")
        for marker in ("51.161.113.128", "MPH_SERVER_USER:-ubuntu", "MPH_SERVER_DIR:-/srv/project-prime",
                       "MPH_SERVER_DATA:-/srv/project-prime/AMHE1", "MPH_SERVER_RID:-linux-x64",
                       "PRIME_NODE_PUBLIC_HOST:-rebooty.xyz", "wss://$PUBLIC_HOST:8443/v1/control",
                       "state/dev.env public rebooty endpoint does not match deployment settings"):
            self.assertIn(marker, script)
        self.assertNotIn("chown -R", script)
        self.assertNotIn('chmod -R 700 "$DEPLOY_DIR"', script)

    def test_legacy_stack_selection_is_exact_and_fail_closed(self):
        script = DEPLOY.read_text(encoding="utf-8")
        for marker in ("expected exactly one verified legacy stack owner",
                       "parallel legacy stack launcher is outside the verified supervisor tree",
                       "legacy supervisor state/env mismatch",
                       "legacy supervisor command/cwd changed", "manual and systemd stack instances overlap",
                       "'Fruity'+'PrimeServer'", "'Prime'+'Hunters.Backend'",
                       "'Fruity'+'Prime.Server.Worker'", "expected exactly one complete process layout",
                       'kill -TERM "$legacy_pid"'):
            self.assertIn(marker, script)

    def test_upload_precedes_whole_stack_stop(self):
        script = DEPLOY.read_text(encoding="utf-8")
        verify = script.index("Verifying upload before whole-stack downtime")
        self.assertIn('rsync -a -- "$BUNDLE/" "$WORK/stage/"', script)
        self.assertIn('rsync_upload "$WORK/stage/" "$REMOTE_STAGE/"', script)
        self.assertNotIn('tar -C "$WORK/stage"', script)
        self.assertLess(verify, script.index('kill -TERM "$legacy_pid"'))
        self.assertLess(verify, script.index("systemctl stop projectprime-stack"))

    def test_activation_rollback_reboot_and_retention_contracts(self):
        script = DEPLOY.read_text(encoding="utf-8")
        for marker in ("systemctl enable projectprime-stack", "Backend or Node health check failed",
                       "complete prior stack restored", "stack activation and rollback failed",
                       "MainPID", "journal.txt", "'app','state','AMHE1'", ">/dev/null 2>&1",
                       "MPH_SERVER_HEALTH_TIMEOUT:-180", "SECONDS + health_timeout",
                       "Waiting up to ${health_timeout}s for Backend and Node health",
                       "expected_argv=['bash',str(current/'start-stack-dev.sh')",
                       "current.resolve()!=release", "argv!=expected_argv",
                       "Stopping stale Project Prime processes", "signal.SIGTERM", "signal.SIGKILL",
                       "unrelated Project Prime installations are deliberately outside this set"):
            self.assertIn(marker, script)

    def test_stack_unit_owns_launcher_and_only_state_is_writable(self):
        unit = UNIT.read_text(encoding="utf-8")
        for marker in ("start-stack-dev.sh --with-backend", "KillMode=mixed", "TimeoutStopSec=75",
                       "UMask=0077", "Restart=on-failure", "ReadWritePaths=__ROOT__/state",
                       "ReadWritePaths=__ROOT__/AMHE1",
                       "PRIME_SERVER_PACKAGE=__ROOT__/current", "PRIME_DEV_ENV_FILE=__ROOT__/state/dev.env",
                       "current/ProjectPrimeServer", "current/FruityPrimeServer"):
            self.assertIn(marker, unit)

    def test_help_needs_no_config_or_network(self):
        environment = os.environ.copy()
        environment["PRIME_DEPLOY_ENV_FILE"] = str(Path(self.temporary.name) / "absent")
        result = subprocess.run([str(DEPLOY), "--help"], env=environment, capture_output=True, text=True, check=False)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("state-generated", result.stdout)


if __name__ == "__main__":
    unittest.main()
