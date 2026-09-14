"""Source contract for the game-only Windows convenience launcher."""
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


class WindowsLauncherContractTests(unittest.TestCase):
    def test_launcher_keeps_game_paths_and_removes_retired_server_modes(self):
        script = (ROOT / "Start-ProjectPrime.ps1").read_text(encoding="utf-8")

        self.assertIn("[ValidateSet('menu', 'game')]", script)
        self.assertIn("src') 'Client", script)
        self.assertIn("Ensure-GamePaths", script)
        self.assertIn("'-launcher'", script)
        self.assertIn("'-noupdate'", script)
        for retired in (
            "ServerProject", "Start-Server", "Start-Directory", "-masterserver",
            "-server", "ProjectPrimeServer", "win-x64-server", "linux-x64-server",
            "ServerPort", "DirectoryPort", "HostPorts", "PublicAddress",
        ):
            self.assertNotIn(retired, script)

    def test_cmd_wrapper_still_delegates_to_powershell(self):
        wrapper = (ROOT / "Start-ProjectPrime.cmd").read_text(encoding="utf-8")
        self.assertIn("Start-ProjectPrime.ps1", wrapper)
        self.assertIn("%*", wrapper)

    def test_active_developer_guide_uses_the_node_worker_stack(self):
        guide = (ROOT / "CLAUDE.md").read_text(encoding="utf-8")

        self.assertIn("tools/package-server.sh --rid win-x64", guide)
        self.assertIn("ProjectPrimeServer.exe --contentRoot", guide)
        self.assertIn("Node owns Worker launch and lifecycle", guide)
        for retired in (
            "MphReadServer.exe -server",
            "MphRead -masterserver",
            'MphRead -server -data DIRECTORY',
        ):
            self.assertNotIn(retired, guide)


if __name__ == "__main__":
    unittest.main()
