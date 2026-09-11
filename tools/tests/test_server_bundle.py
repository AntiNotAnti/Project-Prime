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
        self.assertEqual("worker/ProjectPrime.Server.Worker", launch["FileName"])
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
        self.assertIn('BACKEND_APPHOST="$STAGE/backend/ProjectPrime.Backend.exe"', script)
        self.assertIn('BACKEND_APPHOST="$STAGE/backend/ProjectPrime.Backend"', script)
        self.assertIn("src/Server.Node/Server.Node.csproj", script)
        self.assertIn("src/Server.Worker/Server.Worker.csproj", script)
        self.assertIn('"$STAGE/worker"', script)
        self.assertIn("ProjectPrimeServer.exe", script)
        self.assertIn("ProjectPrimeServer", script)
        self.assertIn("ProjectPrime.Server.Node", script)
        self.assertIn("grep -F 'ProjectPrime.Server.Node'", script)
        self.assertIn("start-bundle-dev.sh", script)
        self.assertIn("start-stack-dev.sh", script)
        self.assertIn("--skip-map-cook", script)
        self.assertIn('if [[ "$SKIP_MAP_COOK" -eq 0 ]]', script)
        self.assertNotIn("| rg ", script)
        self.assertIn("osx-arm64", script)

    def test_local_launcher_discovers_backend_from_combined_package(self):
        script = (ROOT / "tools/start-dev.sh").read_text(encoding="utf-8")
        self.assertIn('PACKAGE_DIR/backend/ProjectPrime.Backend', script)
        self.assertIn('PACKAGE_DIR/backend/ProjectPrime.Backend.exe', script)
        self.assertIn('SCRIPT_DIR/ProjectPrimeServer', script)

    def test_local_backend_readiness_rechecks_its_child_after_health_success(self):
        script = (ROOT / "tools/start-dev.sh").read_text(encoding="utf-8")
        readiness = script.split('echo "Waiting for Backend:', 1)[1].split('NODE_LOG=', 1)[0]
        self.assertGreaterEqual(readiness.count('kill -0 "$BACKEND_PID" 2>/dev/null'), 2)
        self.assertIn(
            'if curl --fail --silent --show-error --max-time 3 "$HEALTH"',
            readiness,
        )
        self.assertIn('HEALTH=$HEALTH"health/live"', script)
        self.assertIn('HEALTH=$HEALTH"health/ready"', script)
        self.assertIn(
            "sleep 1\n            if ! kill -0 \"$BACKEND_PID\"",
            readiness,
        )

    def test_launcher_selects_health_by_backend_database_contract(self):
        script = (ROOT / "tools/start-dev.sh").read_text(encoding="utf-8")
        block = script.split("# BEGIN_BACKEND_HEALTH_SELECTION\n", 1)[1].split(
            "# END_BACKEND_HEALTH_SELECTION", 1
        )[0]

        def select(start_backend, supplied, explicit=""):
            environment = os.environ.copy()
            environment.update({
                "START_BACKEND": str(start_backend),
                "BACKEND_CONNECTION_SUPPLIED": str(supplied),
                "PRIME_BACKEND_HEALTH_URL": explicit,
                "BACKEND_BIND": "http://0.0.0.0:18085",
                "BACKEND_URL": "https://backend.example.test",
            })
            result = subprocess.run(
                ["bash", "-c", block + '\nprintf "%s" "$HEALTH"\n'],
                env=environment, capture_output=True, text=True, check=False,
            )
            self.assertEqual(0, result.returncode, result.stderr)
            return result.stdout

        self.assertEqual("http://127.0.0.1:18085/health/live", select(1, 0))
        self.assertEqual("http://127.0.0.1:18085/health/ready", select(1, 1))
        self.assertEqual("https://backend.example.test/health/ready", select(0, 0))
        self.assertEqual("https://explicit.example/health/ready", select(0, 1, "https://explicit.example"))

    def test_required_database_gate_fails_without_printing_connection(self):
        launcher = ROOT / "tools/start-dev.sh"
        with tempfile.TemporaryDirectory(prefix="prime-required-database-") as directory:
            env_file = Path(directory) / "dev.env"
            env_file.write_text("PRIME_REQUIRE_BACKEND_DATABASE=0\n", encoding="utf-8")
            environment = os.environ.copy()
            environment.update({
                "PRIME_REQUIRE_BACKEND_DATABASE": "1",
                "ConnectionStrings__Backend": "   ",
                "PRIME_DEV_ENV_FILE": str(env_file),
            })
            result = subprocess.run(
                [str(launcher)], env=environment, capture_output=True, text=True, check=False,
            )
            self.assertNotEqual(0, result.returncode)
            self.assertIn("non-empty Backend database connection is required", result.stderr)
            self.assertNotIn("ConnectionStrings__Backend", result.stdout + result.stderr)

            environment["PRIME_REQUIRE_BACKEND_DATABASE"] = "2"
            result = subprocess.run(
                [str(launcher)], env=environment, capture_output=True, text=True, check=False,
            )
            self.assertNotEqual(0, result.returncode)
            self.assertIn("must be 0 or 1", result.stderr)

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
            self.assertIn('--describe-content true --content-dir "$CONTENT_DIR"', script)
            self.assertIn('--content-version "$CONTENT_VERSION" --map-dir "$MAP_DIR"', script)
            self.assertIn(f"MAP_DIR={map_dir}", script)
            self.assertIn('"--map-dir",os.environ["MAP_DIR"]', script)
            self.assertIn('chmod 600 "$DESCRIPTOR_TMP"', script)
            self.assertIn('mv -f "$DESCRIPTOR_TMP" "$DESCRIPTOR_PATH"', script)
            self.assertNotIn("--prepare-content true", script)
            self.assertEqual(2, script.count("--map-dir"))

    def test_launchers_bind_default_map_cache_to_private_state_directory(self):
        for name in ("start-dev.sh", "start-bundle-dev.sh"):
            script = (ROOT / "tools" / name).read_text(encoding="utf-8")
            block = script.split("# BEGIN_STATE_DATA_DIRECTORY\n", 1)[1].split(
                "# END_STATE_DATA_DIRECTORY", 1)[0]
            with self.subTest(name=name), tempfile.TemporaryDirectory(
                prefix="prime-state-map-data-") as directory:
                state = Path(directory).resolve()
                environment = os.environ.copy()
                environment.update({"STATE_DIR": str(state), "PRIME_DATA_DIRECTORY": ""})
                result = subprocess.run(
                    ["bash", "-c", block + '\nprintf "%s" "$PRIME_DATA_DIRECTORY"\n'],
                    env=environment, capture_output=True, text=True, check=False,
                )
                expected = state / "map-data"
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual(str(expected), result.stdout)
                self.assertTrue(expected.is_dir())
                self.assertEqual(0o700, expected.stat().st_mode & 0o777)

                override = state / "operator-cache"
                environment["PRIME_DATA_DIRECTORY"] = "operator-cache"
                result = subprocess.run(
                    ["bash", "-c", block + '\nprintf "%s" "$PRIME_DATA_DIRECTORY"\n'],
                    cwd=state, env=environment, capture_output=True, text=True, check=False,
                )
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual(str(override), result.stdout)
                self.assertTrue(override.is_dir())
                self.assertEqual(0o700, override.stat().st_mode & 0o777)

    def test_launchers_never_prepare_content_during_discovery(self):
        for name in ("start-dev.sh", "start-bundle-dev.sh"):
            script = (ROOT / "tools" / name).read_text(encoding="utf-8")
            self.assertNotIn("--prepare-content true", script)
            self.assertIn("--describe-content true", script)

    def test_launchers_acquire_the_content_lock_before_discovery(self):
        for name in ("start-dev.sh", "start-bundle-dev.sh"):
            script = (ROOT / "tools" / name).read_text(encoding="utf-8")
            open_index = script.index('exec 9>"$CONTENT_LOCK_FILE"')
            flock_index = script.index("fcntl.flock(9, fcntl.LOCK_EX | fcntl.LOCK_NB)")
            discovery_index = script.index("--describe-content true")
            self.assertLess(open_index, flock_index)
            self.assertLess(flock_index, discovery_index)
            self.assertIn("CONTENT_LOCK_ROOT=${TMPDIR:-/tmp}/project-prime-content-locks-$UID", script)
            self.assertIn('python3 - "$CONTENT_DIR"', script)
            self.assertIn("hashlib.sha256", script)

    def test_launchers_release_the_content_lock_after_node_lifetime(self):
        for name in ("start-dev.sh", "start-bundle-dev.sh"):
            script = (ROOT / "tools" / name).read_text(encoding="utf-8")
            cleanup_start = script.index("cleanup() {")
            cleanup_end = script.index("\n}", cleanup_start)
            cleanup = script[cleanup_start:cleanup_end]
            if name == "start-dev.sh":
                self.assertIn("trap release_supervisor_lock EXIT", script)
            else:
                self.assertIn("trap release_content_lock EXIT", script)
            self.assertIn("release_content_lock", cleanup)
            if name == "start-dev.sh":
                self.assertLess(cleanup.index('stop_child "$NODE_PID"'), cleanup.index("release_content_lock"))
            else:
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
        self.assertIn('NODE_APPHOST="$STAGE/node/ProjectPrime.Server.Node.exe"', script)
        self.assertIn('PACKAGE_NODE="$STAGE/node/ProjectPrimeServer.exe"', script)
        self.assertIn('WORKER_APPHOST="$STAGE/worker/ProjectPrime.Server.Worker.exe"', script)

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

    def test_deployment_uses_combined_release_bundle_and_starts_complete_stack(self):
        script = (ROOT / "deploy-server.sh").read_text(encoding="utf-8")
        self.assertIn("MPH_SERVER_CONFIG", script)
        self.assertIn("tools/package-server.sh", script)
        self.assertIn("projectprime-stack.service", script)
        self.assertIn("MPH_SERVER_BUNDLE", script)
        self.assertIn("MPH_SERVER_RID", script)
        self.assertIn("--preflight-only", script)
        self.assertIn(".deploy-lock", script)
        self.assertIn(".deploy-manifest.sha256", script)
        self.assertIn("mv -Tf", script)
        self.assertIn("journal.txt", script)
        self.assertIn("start-stack-dev.sh", script)
        self.assertNotIn("systemctl start ProjectPrime.Backend", script)
        self.assertNotIn("systemctl start projectprime-backend", script)
        self.assertNotIn("src/Server/Server.csproj", script)


if __name__ == "__main__":
    unittest.main()
