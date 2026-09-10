"""Focused local contracts for transactional production deployment."""
from __future__ import annotations

import json
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
VALIDATOR = ROOT / "tools/validate-deploy-inputs.py"


class DeployServerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="prime-deploy-test-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.bundle = self.root / "bundle"
        self.config = self.root / "appsettings.json"

    @staticmethod
    def elf(machine: int) -> bytes:
        data = bytearray(64)
        data[:6] = b"\x7fELF\x02\x01"
        struct.pack_into("<H", data, 18, machine)
        return bytes(data)

    def make_bundle(self, machine: int = 183) -> None:
        for relative in ("FruityPrimeServer", "worker/FruityPrime.Server.Worker", "backend/PrimeHunters.Backend"):
            path = self.bundle / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(self.elf(machine))
            path.chmod(path.stat().st_mode | stat.S_IXUSR)
        (self.bundle / "maps").mkdir()
        (self.bundle / "maps/test.fpmap").write_bytes(b"map")

    def make_config(self) -> None:
        self.config.write_text(json.dumps({"Node": {
            "Authentication": {"NodeId": "00000000-0000-0000-0000-000000000001", "Issuer": "https://node.example", "Keys": [
                {"KeyId": "key", "PublicKeyPemPath": "/srv/project-prime/keys/tickets.pem"}
            ]},
            "Workers": {"Processes": [{"FileName": "worker/FruityPrime.Server.Worker",
                "WorkingDirectory": "/home/game/project-prime/current",
                "ArtifactDirectory": "/home/game/project-prime/artifacts",
                "Arguments": ["--content-dir", "/srv/project-prime/content/AMHE1",
                              "--map-dir", "/home/game/project-prime/current/maps"]}]}
        }}), encoding="utf-8")

    def make_fake_remote_tools(self, deploy: Path) -> dict[str, str]:
        fake_bin = self.root / "fake-remote-bin"
        fake_bin.mkdir()
        state = self.root / "fake-systemd-state"
        enabled = self.root / "fake-systemd-enabled"
        starts = self.root / "fake-systemd-starts"
        unit = self.root / "fake-fruityprime-node.service"
        state.write_text("inactive\n", encoding="utf-8")
        enabled.write_text("disabled\n", encoding="utf-8")
        starts.write_text("0\n", encoding="utf-8")
        scripts = {
            "ssh": "#!/usr/bin/env bash\nshift\nexec bash -c \"$1\"\n",
            "sudo": r'''#!/usr/bin/env bash
[[ ${1:-} != -n ]] || shift
case ${1:-} in
  test)
    shift
    [[ ${2:-} != /etc/systemd/system/fruityprime-node.service ]] || set -- "$1" "$FAKE_UNIT"
    exec test "$@"
    ;;
  cp)
    shift
    [[ ${1:-} != /etc/systemd/system/fruityprime-node.service ]] || set -- "$FAKE_UNIT" "$2"
    exec cp "$@"
    ;;
  install)
    shift
    args=("$@"); last=$((${#args[@]} - 1))
    [[ ${args[$last]} != /etc/systemd/system/fruityprime-node.service ]] || args[$last]=$FAKE_UNIT
    exec install "${args[@]}"
    ;;
  rm)
    shift
    args=("$@"); last=$((${#args[@]} - 1))
    [[ ${args[$last]} != /etc/systemd/system/fruityprime-node.service ]] || args[$last]=$FAKE_UNIT
    exec rm "${args[@]}"
    ;;
  chown) exit 0 ;;
esac
exec "$@"
''',
            "systemctl": r'''#!/usr/bin/env bash
command=${1:-}; shift || true
case "$command" in
  --version) printf 'systemd test\n' ;;
  is-active)
    value=$(cat "$FAKE_SYSTEMD_STATE")
    [[ ${1:-} == --quiet ]] || printf '%s\n' "$value"
    [[ $value == active ]]
    ;;
  is-enabled)
    value=$(cat "$FAKE_SYSTEMD_ENABLED")
    printf '%s\n' "$value"
    [[ $value == enabled ]]
    ;;
  start)
    count=$(cat "$FAKE_SYSTEMD_STARTS"); count=$((count + 1)); printf '%s\n' "$count" > "$FAKE_SYSTEMD_STARTS"
    if [[ ${FAKE_ROLLBACK_START_FAIL:-0} == 1 && $count -ge 3 ]]; then
      printf 'inactive\n' > "$FAKE_SYSTEMD_STATE"; exit 1
    fi
    printf 'active\n' > "$FAKE_SYSTEMD_STATE"
    ;;
  stop)
    printf 'inactive\n' > "$FAKE_SYSTEMD_STATE"
    [[ ${FAKE_STOP_FAIL:-0} != 1 ]]
    ;;
  enable) printf 'enabled\n' > "$FAKE_SYSTEMD_ENABLED" ;;
  disable) printf 'disabled\n' > "$FAKE_SYSTEMD_ENABLED" ;;
  daemon-reload) : ;;
  show)
    if [[ " $* " == *" LoadState "* ]]; then printf 'not-found\n'; else printf '4242\n'; fi
    ;;
  *) exit 1 ;;
esac
''',
            "uname": "#!/usr/bin/env bash\nprintf 'aarch64\\n'\n",
            "curl": "#!/usr/bin/env bash\n[[ ${FAKE_CURL_FAIL:-0} != 1 ]]\n",
            "sleep": "#!/usr/bin/env bash\nexit 0\n",
            "journalctl": "#!/usr/bin/env bash\nprintf 'isolated deployment failure\\n'\n",
            "readlink": r'''#!/usr/bin/env bash
if [[ ${1:-} == -f && ${2:-} == /proc/*/exe ]]; then
  target=$(/usr/bin/readlink "$FAKE_DEPLOY/current")
  printf '%s/%s/FruityPrimeServer\n' "$FAKE_DEPLOY" "$target"
else
  exec /usr/bin/readlink "$@"
fi
''',
            "mv": r'''#!/usr/bin/env bash
if [[ ${1:-} == -Tf ]]; then
  /bin/rm -f "$3"
  exec /bin/mv "$2" "$3"
fi
exec /bin/mv "$@"
''',
        }
        for name, script in scripts.items():
            path = fake_bin / name
            path.write_text(script, encoding="utf-8")
            path.chmod(0o755)
        environment = os.environ.copy()
        environment.update({
            "PATH": str(fake_bin) + os.pathsep + environment["PATH"],
            "FAKE_DEPLOY": str(deploy),
            "FAKE_SYSTEMD_STATE": str(state),
            "FAKE_SYSTEMD_ENABLED": str(enabled),
            "FAKE_SYSTEMD_STARTS": str(starts),
            "FAKE_UNIT": str(unit),
        })
        return environment

    def prepare_remote_case(self) -> tuple[Path, Path]:
        self.make_bundle(); self.make_config()
        (self.bundle / "maps/test.fpmap").unlink()
        with zipfile.ZipFile(self.bundle / "maps/test.fpmap", "w") as archive:
            archive.writestr("test.json", "{}")
        remote_root = self.root / "remote"
        deploy = remote_root / "project-prime"
        content = remote_root / "content" / "AMHE1"
        key = remote_root / "keys" / "tickets.pem"
        (content / "_bin").mkdir(parents=True)
        (content / "models").mkdir()
        (content / "levels").mkdir()
        (content / "_bin/arm9.bin").write_bytes(b"arm9")
        key.parent.mkdir(parents=True)
        key.write_text("public key", encoding="utf-8")
        config = json.loads(self.config.read_text(encoding="utf-8"))
        worker = config["Node"]["Workers"]["Processes"][0]
        config["Node"]["Authentication"]["Keys"][0]["PublicKeyPemPath"] = str(key)
        worker["WorkingDirectory"] = str(deploy / "current")
        worker["ArtifactDirectory"] = str(deploy / "artifacts")
        worker["Arguments"] = ["--content-dir", str(content), "--map-dir", str(deploy / "current/maps")]
        self.config.write_text(json.dumps(config), encoding="utf-8")
        return deploy, content

    def deploy_command(self, deploy: Path, content: Path, *extra: str) -> list[str]:
        return [str(ROOT / "deploy-server.sh"), "--host", "fake", "--user", "gameuser",
            "--deploy-dir", str(deploy), "--config", str(self.config), "--data", str(content),
            "--bundle", str(self.bundle), "--rid", "linux-arm64", *extra]

    def validate(self, rid: str = "linux-arm64"):
        return subprocess.run([sys.executable, str(VALIDATOR), "--bundle", str(self.bundle),
            "--rid", rid, "--config", str(self.config), "--deploy-dir", "/home/game/project-prime",
            "--data-dir", "/srv/project-prime/content/AMHE1"], capture_output=True, text=True, check=False)

    def test_valid_bundle_and_operator_paths_pass(self):
        self.make_bundle(); self.make_config()
        result = self.validate()
        self.assertEqual(0, result.returncode, result.stderr)

    def test_bundle_rid_mismatch_is_rejected(self):
        self.make_bundle(machine=62); self.make_config()
        result = self.validate("linux-arm64")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("bundle/RID mismatch", result.stderr)

    def test_symlink_and_malicious_remote_path_are_rejected(self):
        self.make_bundle(); self.make_config()
        alias = self.bundle / "alias"
        alias.symlink_to(self.bundle / "FruityPrimeServer")
        result = self.validate()
        self.assertIn("symlink", result.stderr)
        alias.unlink()
        result = subprocess.run([sys.executable, str(VALIDATOR), "--bundle", str(self.bundle),
            "--rid", "linux-arm64", "--config", str(self.config), "--deploy-dir", "/srv/../etc",
            "--data-dir", "/srv/project-prime/content/AMHE1"], capture_output=True, text=True, check=False)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("normalized remote path", result.stderr)

    def test_operator_config_cannot_launch_a_different_worker(self):
        self.make_bundle(); self.make_config()
        config = json.loads(self.config.read_text(encoding="utf-8"))
        config["Node"]["Workers"]["Processes"][0]["FileName"] = "/tmp/not-project-prime"
        self.config.write_text(json.dumps(config), encoding="utf-8")
        result = self.validate()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("packaged Project Prime Worker", result.stderr)

    def test_operator_config_must_use_the_atomic_current_map_path(self):
        self.make_bundle(); self.make_config()
        config = json.loads(self.config.read_text(encoding="utf-8"))
        config["Node"]["Workers"]["Processes"][0]["Arguments"][-1] = "/home/game/project-prime/maps"
        self.config.write_text(json.dumps(config), encoding="utf-8")
        result = self.validate()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("--map-dir must be /home/game/project-prime/current/maps", result.stderr)

    def test_mutable_operator_paths_cannot_overlap_retained_releases(self):
        self.make_bundle(); self.make_config()
        config = json.loads(self.config.read_text(encoding="utf-8"))
        config["Node"]["Workers"]["Processes"][0]["ArtifactDirectory"] = \
            "/home/game/project-prime/releases/operator-artifacts"
        self.config.write_text(json.dumps(config), encoding="utf-8")
        result = self.validate()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("outside the current/release tree", result.stderr)

    def test_preflight_only_runs_remote_checks_without_upload_or_stop(self):
        deploy, content = self.prepare_remote_case()
        environment = self.make_fake_remote_tools(deploy)
        result = subprocess.run(self.deploy_command(deploy, content, "--preflight-only"),
            cwd=ROOT, env=environment, capture_output=True, text=True, check=False)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("no upload occurred", result.stdout)
        self.assertFalse(deploy.exists())

    def test_isolated_activation_and_failed_update_restore_previous_release(self):
        deploy, content = self.prepare_remote_case()
        environment = self.make_fake_remote_tools(deploy)
        first = subprocess.run(self.deploy_command(deploy, content), cwd=ROOT, env=environment,
            capture_output=True, text=True, check=False)
        self.assertEqual(0, first.returncode, first.stderr)
        first_target = os.readlink(deploy / "current")
        first_config = (deploy / "config/appsettings.json").read_bytes()
        self.assertFalse((deploy / ".deploy.lock").exists())

        config = json.loads(self.config.read_text(encoding="utf-8"))
        config["Node"]["MaximumSessions"] = 999
        self.config.write_text(json.dumps(config), encoding="utf-8")
        failed_environment = environment.copy()
        failed_environment["FAKE_CURL_FAIL"] = "1"
        second = subprocess.run(self.deploy_command(deploy, content), cwd=ROOT,
            env=failed_environment, capture_output=True, text=True, check=False)
        self.assertNotEqual(0, second.returncode)
        self.assertIn("previous unit/current/config state was restored", second.stderr)
        self.assertEqual(first_target, os.readlink(deploy / "current"))
        self.assertEqual(first_config, (deploy / "config/appsettings.json").read_bytes())
        self.assertFalse((deploy / ".deploy.lock").exists())
        self.assertFalse(list(deploy.glob(".appsettings.*.staging")))
        self.assertFalse(list(deploy.glob(".fruityprime-node.*.service")))
        journals = list((deploy / "releases").glob("*/deploy-failure-journal.txt"))
        self.assertEqual(1, len(journals))

        stop_failure_environment = environment.copy()
        stop_failure_environment["FAKE_STOP_FAIL"] = "1"
        stop_failure = subprocess.run(self.deploy_command(deploy, content), cwd=ROOT,
            env=stop_failure_environment, capture_output=True, text=True, check=False)
        self.assertNotEqual(0, stop_failure.returncode)
        self.assertIn("previous unit/current/config state was restored", stop_failure.stderr)
        self.assertEqual("active", Path(environment["FAKE_SYSTEMD_STATE"]).read_text(encoding="utf-8").strip())
        self.assertEqual(first_target, os.readlink(deploy / "current"))
        self.assertFalse((deploy / ".deploy.lock").exists())

        (deploy / "current").unlink()
        (deploy / "current").symlink_to("releases/../keys")
        unsafe = subprocess.run(self.deploy_command(deploy, content), cwd=ROOT, env=environment,
            capture_output=True, text=True, check=False)
        self.assertNotEqual(0, unsafe.returncode)
        self.assertIn("Unsafe current release link", unsafe.stderr)
        self.assertEqual("active", Path(environment["FAKE_SYSTEMD_STATE"]).read_text(encoding="utf-8").strip())
        (deploy / "current").unlink()
        (deploy / "current").symlink_to(first_target)

        (deploy / ".deploy.lock").mkdir()
        locked = subprocess.run(self.deploy_command(deploy, content), cwd=ROOT, env=environment,
            capture_output=True, text=True, check=False)
        self.assertEqual(75, locked.returncode)
        self.assertIn("Deployment lock already exists", locked.stderr)
        self.assertTrue((deploy / ".deploy.lock").is_dir())
        self.assertEqual(first_target, os.readlink(deploy / "current"))

    def test_health_url_cannot_escape_loopback_with_userinfo(self):
        deploy, content = self.prepare_remote_case()
        environment = self.make_fake_remote_tools(deploy)
        result = subprocess.run(self.deploy_command(deploy, content, "--preflight-only",
            "--health-url", "https://127.0.0.1:password@example.com/health"),
            cwd=ROOT, env=environment, capture_output=True, text=True, check=False)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("loopback URL", result.stderr)
        self.assertFalse(deploy.exists())

    def test_failed_automatic_rollback_retains_lock_and_recovery_snapshot(self):
        deploy, content = self.prepare_remote_case()
        environment = self.make_fake_remote_tools(deploy)
        first = subprocess.run(self.deploy_command(deploy, content), cwd=ROOT, env=environment,
            capture_output=True, text=True, check=False)
        self.assertEqual(0, first.returncode, first.stderr)
        first_target = os.readlink(deploy / "current")

        failed_environment = environment.copy()
        failed_environment.update({"FAKE_CURL_FAIL": "1", "FAKE_ROLLBACK_START_FAIL": "1"})
        failed = subprocess.run(self.deploy_command(deploy, content), cwd=ROOT,
            env=failed_environment, capture_output=True, text=True, check=False)
        self.assertNotEqual(0, failed.returncode)
        self.assertIn("automatic rollback both failed", failed.stderr)
        self.assertTrue((deploy / ".deploy.lock").is_dir())
        self.assertEqual(first_target, os.readlink(deploy / "current"))
        self.assertEqual(1, len(list(deploy.glob(".rollback-*"))))
        self.assertEqual(1, len(list(deploy.glob(".appsettings.*.staging"))))
        self.assertEqual(1, len(list(deploy.glob(".fruityprime-node.*.service"))))

    def test_script_orders_upload_verification_before_stop_and_preserves_rollback_markers(self):
        script = (ROOT / "deploy-server.sh").read_text(encoding="utf-8")
        self.assertLess(script.index("Verifying uploaded release"), script.index("systemctl stop fruityprime-node"))
        for marker in ("Deployment lock already exists", "prior_target", "legacy_migrated",
                       "deploy-failure-journal.txt", "MainPID", "MPH_SERVER_KEEP_RELEASES"):
            self.assertIn(marker, script)
        self.assertNotIn("systemctl start fruityprime-backend", script)


if __name__ == "__main__":
    unittest.main()
