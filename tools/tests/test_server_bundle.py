"""Structural checks for the public Backend + Node + Worker server bundle contract."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]


class ServerBundleContractTests(unittest.TestCase):
    @staticmethod
    def _config_generator(script_name):
        script = (ROOT / "tools" / script_name).read_text(encoding="utf-8")
        marker = "if ! identity=$(python3 - <<'PY'\n"
        start = script.index(marker) + len(marker)
        end = script.index("\nPY\n", start)
        return script[start:end]

    def _generate_config(self, script_name, descriptor, map_key=""):
        with tempfile.TemporaryDirectory(prefix="prime-server-bundle-test-") as directory:
            state = Path(directory)
            descriptor_path = state / "content-description.json"
            config_path = state / "appsettings.json"
            descriptor_path.write_text(json.dumps(descriptor), encoding="utf-8")
            environment = os.environ.copy()
            environment.update({
                "CONFIG_PATH": str(config_path),
                "DESCRIPTOR_PATH": str(descriptor_path),
                "MAP_DIR": "/srv/project-prime/maps",
                "MAP_KEY": map_key,
                "CONTENT_VERSION": "AMHE1",
                "NODE_ID": "00000000-0000-0000-0000-000000000001",
                "TICKET_ISSUER": "https://node.example",
                "KEY_ID": "dev-current",
                "PUBLIC_KEY": "/srv/project-prime/node-public.pem",
                "CONTENT_DIR": "/srv/project-prime/AMHE1",
                "ARTIFACT_DIR": str(state / "artifacts"),
                "REPLAY_DIR": str(state / "replays"),
                "WORKER_HOST": "127.0.0.1",
                "WORKER_BIND": "127.0.0.1",
                "LANES": "2",
                "MAX_MATCHES": "4",
                "MAX_MATCHES_PER_LANE": "2",
                "PLAYER_LIMIT": "32",
                "MAX_LOBBIES": "64",
                "MAX_SESSIONS": "256",
                "NODE_NAME": "Project Prime Dev",
                "NODE_REGION": "dev",
                "NODE_CONTROL_URI": "wss://node.example:8443/v1/control",
                "BACKEND_URL": "http://127.0.0.1:18085",
                "PACKAGE_DIR": "/srv/project-prime/package",
                "BUNDLE_DIR": "/srv/project-prime/package",
            })
            result = subprocess.run(
                [sys.executable, "-c", self._config_generator(script_name)],
                env=environment,
                capture_output=True,
                text=True,
                check=False,
            )
            return result, json.loads(config_path.read_text(encoding="utf-8")) if config_path.exists() else None

    def test_example_has_no_credentials_or_content_and_uses_the_a25_profile(self):
        config = json.loads((ROOT / "tools/server.example.json").read_text(encoding="utf-8"))
        node = config["Node"]
        self.assertEqual([], node["Maps"])
        self.assertNotIn("Authentication", node)
        worker = node["Workers"]["Processes"]
        self.assertEqual(1, len(worker))
        launch = worker[0]
        self.assertEqual("worker/FruityPrime.Server.Worker", launch["FileName"])
        self.assertEqual(
            ["--lanes", "2", "--max-matches", "4", "--max-matches-per-lane", "2"],
            launch["Arguments"],
        )
        self.assertEqual(4, launch["Capacity"]["MatchLimit"])
        self.assertEqual(32, launch["Capacity"]["PlayerLimit"])
        self.assertNotIn("--content-dir", launch["Arguments"])
        self.assertNotIn("--content-hash", launch["Arguments"])

    def test_packager_keeps_worker_below_worker_directory_and_renames_only_node_apphost(self):
        script = (ROOT / "tools/package-server.sh").read_text(encoding="utf-8")
        self.assertIn("src/Backend/Backend.csproj", script)
        self.assertIn('"$STAGE/backend"', script)
        self.assertIn('BACKEND_APPHOST="$STAGE/backend/PrimeHunters.Backend.exe"', script)
        self.assertIn('BACKEND_APPHOST="$STAGE/backend/PrimeHunters.Backend"', script)
        self.assertIn("src/Server.Node/Server.Node.csproj", script)
        self.assertIn("src/Server.Worker/Server.Worker.csproj", script)
        self.assertIn('"$STAGE/worker"', script)
        self.assertIn("FruityPrimeServer.exe", script)
        self.assertIn("FruityPrimeServer", script)
        self.assertIn("FruityPrime.Server.Node", script)
        self.assertIn("grep -F 'FruityPrime.Server.Node'", script)
        self.assertIn("start-bundle-dev.sh", script)
        self.assertIn("start-stack-dev.sh", script)
        self.assertNotIn("| rg ", script)
        self.assertIn("osx-arm64", script)

    def test_local_launcher_discovers_backend_from_combined_package(self):
        script = (ROOT / "tools/start-dev.sh").read_text(encoding="utf-8")
        self.assertIn('PACKAGE_DIR/backend/PrimeHunters.Backend', script)
        self.assertIn('PACKAGE_DIR/backend/PrimeHunters.Backend.exe', script)
        self.assertIn('SCRIPT_DIR/FruityPrimeServer', script)

    def test_local_backend_readiness_rechecks_its_child_after_health_success(self):
        script = (ROOT / "tools/start-dev.sh").read_text(encoding="utf-8")
        readiness = script.split('echo "Waiting for Backend:', 1)[1].split('NODE_LOG=', 1)[0]
        self.assertGreaterEqual(readiness.count('kill -0 "$BACKEND_PID" 2>/dev/null'), 2)
        self.assertIn(
            'if curl --fail --silent --show-error --max-time 3 "$HEALTH"',
            readiness,
        )
        self.assertIn(
            "sleep 1\n            if ! kill -0 \"$BACKEND_PID\"",
            readiness,
        )

    def test_development_launchers_default_to_cloudflare_proxyable_node_port(self):
        for name in ("start-dev.sh", "start-bundle-dev.sh"):
            script = (ROOT / "tools" / name).read_text(encoding="utf-8")
            self.assertIn("wss://$NODE_PUBLIC_HOST:8443/v1/control", script)
            self.assertIn("https://0.0.0.0:8443", script)

    def test_launchers_use_the_same_explicit_map_directory_for_discovery_and_runtime(self):
        expected = {
            "start-dev.sh": "$PACKAGE_DIR/maps",
            "start-bundle-dev.sh": "$BUNDLE_DIR/maps",
        }
        for name, map_dir in expected.items():
            script = (ROOT / "tools" / name).read_text(encoding="utf-8")
            self.assertIn('--prepare-content true --content-dir "$CONTENT_DIR"', script)
            self.assertIn('--describe-content true --content-dir "$CONTENT_DIR"', script)
            self.assertIn('--content-version "$CONTENT_VERSION" --map-dir "$MAP_DIR"', script)
            self.assertIn(f"MAP_DIR={map_dir}", script)
            self.assertIn('"--map-dir",os.environ["MAP_DIR"]', script)
            self.assertIn('chmod 600 "$DESCRIPTOR_TMP"', script)
            self.assertIn('mv -f "$DESCRIPTOR_TMP" "$DESCRIPTOR_PATH"', script)
            self.assertLess(script.index("--prepare-content true"), script.index("--describe-content true"))
            self.assertGreaterEqual(script.count("--map-dir"), 3)

    def test_launchers_skip_preparation_when_content_has_a_baked_manifest(self):
        for name in ("start-dev.sh", "start-bundle-dev.sh"):
            script = (ROOT / "tools" / name).read_text(encoding="utf-8")
            manifest_check = 'if [[ -f "$CONTENT_DIR/server-content.json" ]]; then'
            manifest_index = script.index(manifest_check)
            self.assertIn("CONTENT_IS_BAKED=1", script[manifest_index:])
            descriptor_index = script.index("DESCRIPTOR_PATH=", manifest_index)
            preparation_guard = script.index(
                'if [[ "$CONTENT_IS_BAKED" == 0 ]]; then', descriptor_index
            )
            preparation_index = script.index("--prepare-content true", preparation_guard)
            describe_index = script.index("--describe-content true", preparation_index)
            guard_end = script.rfind("\nfi", preparation_guard, describe_index)
            self.assertLess(manifest_index, preparation_guard)
            self.assertLess(preparation_index, guard_end)
            self.assertLess(guard_end, describe_index)

    def test_launchers_acquire_the_content_lock_before_preparation(self):
        for name in ("start-dev.sh", "start-bundle-dev.sh"):
            script = (ROOT / "tools" / name).read_text(encoding="utf-8")
            open_index = script.index('exec 9>"$CONTENT_LOCK_FILE"')
            flock_index = script.index("fcntl.flock(9, fcntl.LOCK_EX | fcntl.LOCK_NB)")
            preparation_index = script.index("--prepare-content true")
            self.assertLess(open_index, flock_index)
            self.assertLess(flock_index, preparation_index)
            self.assertIn("CONTENT_LOCK_ROOT=${TMPDIR:-/tmp}/project-prime-content-locks-$UID", script)
            self.assertIn('python3 - "$CONTENT_DIR"', script)
            self.assertIn("hashlib.sha256", script)

    def test_launchers_release_the_content_lock_after_node_lifetime(self):
        for name in ("start-dev.sh", "start-bundle-dev.sh"):
            script = (ROOT / "tools" / name).read_text(encoding="utf-8")
            cleanup_start = script.index("cleanup() {")
            cleanup_end = script.index("\n}", cleanup_start)
            cleanup = script[cleanup_start:cleanup_end]
            self.assertIn("trap release_content_lock EXIT", script)
            self.assertIn("release_content_lock", cleanup)
            self.assertLess(cleanup.index('wait "$NODE_PID"'), cleanup.index("release_content_lock"))

    def test_launchers_report_content_lock_contention_before_preparation(self):
        for name in ("start-dev.sh", "start-bundle-dev.sh"):
            script = (ROOT / "tools" / name).read_text(encoding="utf-8")
            lock_start = script.index("if python3 - <<'PY'")
            lock_end = script.index("\nDESCRIPTOR_PATH=", lock_start)
            lock_block = script[lock_start:lock_end]
            self.assertIn("fcntl.LOCK_NB", lock_block)
            self.assertIn("import sys", lock_block)
            self.assertIn("another Project Prime launcher", lock_block)
            self.assertIn("another launcher is using it", lock_block)
            self.assertIn("Worker content preparation was not started", lock_block)
            self.assertNotIn("--prepare-content true", lock_block)

    def test_empty_map_key_generates_sorted_descriptor_maps_with_identity_and_modes(self):
        descriptor = {
            "ContentVersion": "AMHE1",
            "ContentHash": "hash-123",
            "BuildVersion": "build-123",
            "ProtocolVersion": 1,
            "Maps": [
                {"MapKey": "ZETA", "Modes": [11, 0]},
                {"MapKey": "ALPHA", "Modes": [2, 0]},
            ],
        }
        for name in ("start-dev.sh", "start-bundle-dev.sh"):
            result, config = self._generate_config(name, descriptor)
            self.assertEqual(0, result.returncode, result.stderr)
            maps = config["Node"]["Maps"]
            self.assertEqual(["ALPHA", "ZETA"], [entry["MapKey"] for entry in maps])
            self.assertEqual([0, 2], maps[0]["Modes"])
            self.assertEqual([0, 11], maps[1]["Modes"])
            for entry in maps:
                self.assertEqual("AMHE1", entry["ContentVersion"])
                self.assertEqual("hash-123", entry["ContentHash"])
                self.assertEqual("build-123", entry["BuildVersion"])
                self.assertEqual(1, entry["ProtocolVersion"])
            arguments = config["Node"]["Workers"]["Processes"][0]["Arguments"]
            self.assertEqual("/srv/project-prime/maps", arguments[arguments.index("--map-dir") + 1])

    def test_nonempty_map_key_selects_one_exact_descriptor_map(self):
        descriptor = {
            "ContentVersion": "AMHE1", "ContentHash": "hash", "BuildVersion": "build",
            "ProtocolVersion": 1,
            "Maps": [
                {"MapKey": "ZETA", "Modes": [0]},
                {"MapKey": "ALPHA", "Modes": [1]},
            ],
        }
        result, config = self._generate_config("start-dev.sh", descriptor, map_key="ZETA")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(["ZETA"], [entry["MapKey"] for entry in config["Node"]["Maps"]])

        result, config = self._generate_config("start-bundle-dev.sh", descriptor, map_key="missing")
        self.assertNotEqual(0, result.returncode)
        self.assertIsNone(config)
        self.assertIn("not present in the Worker descriptor", result.stderr)

    def test_malformed_or_out_of_bounds_discovery_fails_clearly(self):
        malformed = {
            "ContentVersion": "AMHE1", "ContentHash": "hash", "BuildVersion": "build",
            "ProtocolVersion": 1, "Maps": [{"MapKey": "ALPHA", "Modes": [12]}],
        }
        result, config = self._generate_config("start-dev.sh", malformed)
        self.assertNotEqual(0, result.returncode)
        self.assertIsNone(config)
        self.assertIn("expected 0..11", result.stderr)

        too_many_maps = {
            "ContentVersion": "AMHE1", "ContentHash": "hash", "BuildVersion": "build",
            "ProtocolVersion": 1,
            "Maps": [{"MapKey": f"MAP-{index:03d}", "Modes": [0]} for index in range(257)],
        }
        result, config = self._generate_config("start-bundle-dev.sh", too_many_maps)
        self.assertNotEqual(0, result.returncode)
        self.assertIsNone(config)
        self.assertIn("more than 256 maps", result.stderr)

        duplicate_modes = {
            "ContentVersion": "AMHE1", "ContentHash": "hash", "BuildVersion": "build",
            "ProtocolVersion": 1,
            "Maps": [{"MapKey": "ALPHA", "Modes": [0, 0]}],
        }
        result, config = self._generate_config("start-dev.sh", duplicate_modes)
        self.assertNotEqual(0, result.returncode)
        self.assertIsNone(config)
        self.assertIn("duplicate MatchMode", result.stderr)

        too_many_pairs = {
            "ContentVersion": "AMHE1", "ContentHash": "hash", "BuildVersion": "build",
            "ProtocolVersion": 1,
            "Maps": [
                {"MapKey": f"MAP-{index:03d}", "Modes": list(range(12))}
                for index in range(65)
            ],
        }
        result, config = self._generate_config("start-bundle-dev.sh", too_many_pairs)
        self.assertNotEqual(0, result.returncode)
        self.assertIsNone(config)
        self.assertIn("more than 768 map/mode pairs", result.stderr)

    def test_custom_map_bundles_are_published_to_client_and_server(self):
        client = (ROOT / "src/Client/Client.csproj").read_text(encoding="utf-8")
        server = (ROOT / "tools/package-server.sh").read_text(encoding="utf-8")
        self.assertIn("%(RecursiveDir)%(Filename)%(Extension)", client)
        self.assertIn("CopyToPublishDirectory>PreserveNewest", client)
        self.assertIn("CookCustomMapBundles", client)
        self.assertIn("DestinationFiles=\"@(_CustomMapBundle->", client)
        self.assertIn('find "$ROOT/maps" -type f -name \'*.fpmap\'', server)
        self.assertIn('-mapdir "$ROOT/maps" -mapbundle all', server)
        self.assertIn('bash "$ROOT/tools/check-maps-shipped.sh" "$OUTPUT"', server)

    def test_workflows_use_combined_bundle_and_leave_updater_migration_to_operators(self):
        workflows = "\n".join(
            (ROOT / ".github/workflows" / name).read_text(encoding="utf-8")
            for name in ("build.yml", "release.yml", "network-tests.yml")
        )
        self.assertIn("tools/package-server.sh", workflows)
        self.assertNotIn("generate-authoritative-server-update-assets", workflows)
        self.assertNotIn("server-update-package.py", workflows)

    def test_windows_binary_names_are_packaging_names(self):
        script = (ROOT / "tools/package-server.sh").read_text(encoding="utf-8")
        self.assertIn('NODE_APPHOST="$STAGE/node/FruityPrime.Server.Node.exe"', script)
        self.assertIn('PACKAGE_NODE="$STAGE/node/FruityPrimeServer.exe"', script)
        self.assertIn('WORKER_APPHOST="$STAGE/worker/FruityPrime.Server.Worker.exe"', script)

    def test_package_smoke_is_fresh_extracted_and_exercises_both_planes(self):
        wrapper = (ROOT / "tools/package-smoke.sh").read_text(encoding="utf-8")
        smoke = (ROOT / "tools/package-smoke/Program.cs").read_text(encoding="utf-8")
        self.assertIn("tar -xzf", wrapper)
        for marker in (
            "ClientWebSocket", "lobby.create", "lobby.configure", "lobby.start",
            "match.handoff", "NetMessageType.Join", "match.ended",
            "smoke-replay", "smoke-artifacts", "RequestGracefulStop",
            "WaitForNoNewWorkersAsync",
        ):
            self.assertIn(marker, smoke)

    def test_deployment_requires_explicit_operator_config_and_keeps_legacy_migration_manual(self):
        script = (ROOT / "deploy-server.sh").read_text(encoding="utf-8")
        self.assertIn("MPH_SERVER_CONFIG", script)
        self.assertIn("tools/package-server.sh", script)
        self.assertIn("fruityprime-node.service", script)
        self.assertIn("REMOTE_BACKUP", script)
        self.assertNotIn("src/Server/Server.csproj", script)


if __name__ == "__main__":
    unittest.main()
