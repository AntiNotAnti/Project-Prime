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

    def validate_deploy_env(self, contents: str) -> subprocess.CompletedProcess[str]:
        env_dir = Path(self.temporary.name) / "state"
        env_dir.mkdir(exist_ok=True)
        (env_dir / "dev.env").write_text(contents, encoding="utf-8")
        source = DEPLOY.read_text(encoding="utf-8")
        block = source.split("# BEGIN_DEPLOY_ENV_VALIDATION\n", 1)[1].split(
            "# END_DEPLOY_ENV_VALIDATION", 1
        )[0]
        program = (
            "import pathlib,shlex,sys\n"
            "state=pathlib.Path(sys.argv[1]); public_host='rebooty.xyz'; "
            "public_control='wss://rebooty.xyz:8443/v1/control'\n" + block
        )
        return subprocess.run(
            [sys.executable, "-c", program, str(env_dir)],
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

    def test_deploy_env_requires_one_nonempty_backend_connection_without_disclosure(self):
        public = (
            "PRIME_NODE_PUBLIC_HOST=rebooty.xyz\n"
            "PRIME_NODE_PUBLIC_CONTROL_URI=wss://rebooty.xyz:8443/v1/control\n"
        )
        secret = "Password=do-not-print-this"
        cases = (
            public,
            public + "ConnectionStrings__Backend=\n",
            public + "ConnectionStrings__Backend='   '\n",
            public + f"ConnectionStrings__Backend='{secret}'\nConnectionStrings__Backend='second'\n",
        )
        for contents in cases:
            with self.subTest(contents=contents):
                result = self.validate_deploy_env(contents)
                self.assertNotEqual(0, result.returncode)
                self.assertNotIn(secret, result.stdout + result.stderr)

        valid = self.validate_deploy_env(public + f"ConnectionStrings__Backend='{secret}'\n")
        self.assertEqual(0, valid.returncode, valid.stderr)
        self.assertNotIn(secret, valid.stdout + valid.stderr)

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
        database_check = script.index("Checking candidate Backend database compatibility")
        self.assertGreater(database_check, verify)
        self.assertLess(database_check, script.index('ACTIVATION_SENT=1'))
        self.assertIn('ProjectPrime.Backend" --check-database', script)
        self.assertIn("candidate Backend database check failed", script)

    def test_prior_backend_health_contract_accepts_only_ready_or_verified_legacy(self):
        source = DEPLOY.read_text(encoding="utf-8")
        block = source.split("# BEGIN_PRIOR_HEALTH_CONTRACT\n", 1)[1].split(
            "# END_PRIOR_HEALTH_CONTRACT", 1
        )[0]

        def probe(ready, legacy):
            fake_curl = (
                'curl() { case "$*" in *health/ready*) printf "%s" "$READY_STATUS" ;; '
                '*) printf "%s" "$LEGACY_STATUS" ;; esac; }\n'
            )
            environment = os.environ.copy()
            environment.update({"READY_STATUS": ready, "LEGACY_STATUS": legacy})
            return subprocess.run(
                ["bash", "-c", fake_curl + block + '\nprintf "%s" "$prior_health"\n'],
                env=environment, capture_output=True, text=True, check=False,
            )

        self.assertEqual("ready", probe("200", "000").stdout)
        self.assertEqual("legacy", probe("404", "200").stdout)
        for ready, legacy in (("503", "200"), ("000", "200"), ("404", "503")):
            with self.subTest(ready=ready, legacy=legacy):
                self.assertNotEqual(0, probe(ready, legacy).returncode)

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
        self.assertIn("http://127.0.0.1:18085/health/ready", script)
        self.assertIn("Backend readiness health refused deployment", script)
        self.assertIn("prior_health=legacy", script)
        self.assertIn("prior_backend_health='http://127.0.0.1:18085/v1/nodes?", script)

    def test_stack_unit_owns_launcher_and_only_state_is_writable(self):
        unit = UNIT.read_text(encoding="utf-8")
        for marker in ("start-stack-dev.sh --with-backend", "KillMode=mixed", "TimeoutStopSec=75",
                       "UMask=0077", "Restart=on-failure", "ReadWritePaths=__ROOT__/state",
                       "ReadWritePaths=__ROOT__/AMHE1",
                       "PRIME_SERVER_PACKAGE=__ROOT__/current", "PRIME_DEV_ENV_FILE=__ROOT__/state/dev.env",
                       "PRIME_REQUIRE_BACKEND_DATABASE=1",
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
