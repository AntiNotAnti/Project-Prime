"""Structural checks for the public Node + Worker server bundle contract."""
import json
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


class ServerBundleContractTests(unittest.TestCase):
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
        self.assertIn("src/Server.Node/Server.Node.csproj", script)
        self.assertIn("src/Server.Worker/Server.Worker.csproj", script)
        self.assertIn('"$STAGE/worker"', script)
        self.assertIn("FruityPrimeServer.exe", script)
        self.assertIn("FruityPrimeServer", script)
        self.assertIn("FruityPrime.Server.Node", script)
        self.assertIn("osx-arm64", script)

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
