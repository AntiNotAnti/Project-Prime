"""Contract tests for the campaign-runtime source guard."""
import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools/check-multiplayer-only.py"
SPEC = importlib.util.spec_from_file_location("check_multiplayer_only", SCRIPT)
GUARD = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = GUARD
SPEC.loader.exec_module(GUARD)


class MultiplayerGuardTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="project-prime-multiplayer-guard-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def write_source(self, relative, source):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(source)
        return path

    def run_guard(self, root=None):
        return subprocess.run(
            [sys.executable, str(SCRIPT), "--root", str(root or self.root)],
            cwd=ROOT,
            text=True,
            capture_output=True,
            check=False,
            timeout=10,
        )

    def test_banned_runtime_symbols_are_reported_but_schema_names_are_safe(self):
        self.write_source(
            "src/Game/Runtime.cs",
            """class Runtime {
    StorySave save;
    ModeStateAdventure adventure;
    LaunchKind.Adventure;
    LaunchKind.Offline;
    GameState.SinglePlayer;
    GameState.Multiplayer;
    GameState.Mode;
    GameMode.SinglePlayer;
    EnemyInstanceEntity enemy;
    EnemySpawnEntity spawn;
    ArtifactEntity artifact;
    StorySaveData rawLayout;
    PlayerScan scan;
    PlayerDialog dialog;
    DialogType dialogType;
    ResetCombatVisor resetCombat;
    ResetScanVisor resetScan;
    ProcessScan processScan;
    UpdateScan updateScan;
    ScanVisor visor;
    Keybind Scan;
    EnemySpawnEntityData schemaSpawn;
    ArtifactEntityEditor schemaArtifact;
}
""",
        )

        result = self.run_guard()

        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        for rule in {
            "campaign-save",
            "adventure-mode-flow",
            "campaign-launch",
            "global-game-mode",
            "campaign-mode-selector",
            "raw-story-layout",
            "deleted-entity-runtime",
            "player-scan-dialog-runtime",
            "player-scan-visor",
            "player-scan-input",
        }:
            self.assertIn(f"src/Game/Runtime.cs:", result.stdout)
            self.assertIn(f": {rule}:", result.stdout)
        self.assertNotIn("EnemySpawnEntityData", result.stdout)
        self.assertNotIn("ArtifactEntityEditor", result.stdout)
        self.assertIn("checked 1 C# files;", result.stdout)

    def test_raw_exceptions_are_exact_files_and_scoped_to_one_rule(self):
        exceptions = {
            "campaign-mode-selector": [
                ("src/Shared/ContentPreparation/RepackModelPacking.cs", "GameMode.SinglePlayer"),
                ("tests/Tests/Match/MatchDomainTests.cs", "GameMode.SinglePlayer"),
                ("tests/Tests/Match/RotationRulesTests.cs", "GameMode.SinglePlayer"),
            ],
            "raw-enemy-identity": [
                ("src/Game/Content/Formats/Enums.cs", "EnemyInstance"),
            ],
            "raw-movie-codec": [
                ("src/Tools/Conversion/Movie.cs", "VxDecoder"),
                ("src/Tools/Program.cs", "VxDecoder"),
            ],
            "player-scan-visor": [
                ("src/Client/HUD/HudInfo.cs", "ScanVisor"),
                ("src/Client/Rendering/Renderer.cs", "ScanVisor"),
                ("src/Client/Rendering/Entities/ObjectEntityPresentation.cs", "ScanVisor"),
            ],
        }
        for entries in exceptions.values():
            for relative, token in entries:
                self.write_source(relative, f"class Fixture {{ object value = {token}; }}\n")

        clean = self.run_guard()
        self.assertEqual(clean.returncode, 0, clean.stdout + clean.stderr)
        self.assertIn("0 violation(s)", clean.stdout)

        siblings = []
        for rule, entries in exceptions.items():
            for relative, token in entries:
                original = Path(relative)
                sibling = original.with_name(f"{original.stem}Sibling.cs")
                self.write_source(sibling, f"class Fixture {{ object value = {token}; }}\n")
                siblings.append((sibling.as_posix(), rule, token))
        # An exact exception for GameMode.SinglePlayer must not exempt another rule.
        self.write_source("src/Shared/ContentPreparation/RepackModelPacking.cs", "class Fixture { StorySave save; }\n")

        rejected = self.run_guard()
        self.assertEqual(rejected.returncode, 1, rejected.stdout + rejected.stderr)
        for relative, rule, token in siblings:
            self.assertIn(f"{relative}:1: {rule}: {token}", rejected.stdout)
        self.assertIn(
            "src/Shared/ContentPreparation/RepackModelPacking.cs:1: campaign-save: StorySave",
            rejected.stdout,
        )

    def test_comments_and_literals_are_masked_but_interpolated_code_is_checked(self):
        self.write_source(
            "src/Game/Literals.cs",
            (
                "// StorySave ModeStateAdventure GameState.SinglePlayer\n"
                "/* LaunchKind.Offline EnemyInstanceEntity ScanVisor */\n"
                'const string plain = "StorySave GameMode.SinglePlayer";\n'
                'const string verbatim = @"PlayerDialog ArtifactEntity";\n'
                'const string raw = """PlayerScan Keybind Scan""";\n'
                "const char value = 'S';\n"
                'var interpolated = $"{Foo("a", GameState.Mode)}";\n'
            ),
        )

        result = self.run_guard()

        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("src/Game/Literals.cs:7: global-game-mode: GameState.Mode", result.stdout)
        self.assertNotIn("campaign-save", result.stdout)
        self.assertNotIn("deleted-entity-runtime", result.stdout)
        self.assertNotIn("player-scan-dialog-runtime", result.stdout)
        self.assertNotIn("player-scan-input", result.stdout)
        self.assertNotIn("player-scan-visor", result.stdout)
        self.assertIn("1 violation(s)", result.stdout)

    def test_future_src_game_is_scanned_and_generated_directories_are_ignored(self):
        self.write_source("src/Game/Src.cs", "class Future { StorySave save; }\n")
        self.write_source("src/Game/bin/Ignored.cs", "class Generated { StorySave save; }\n")
        self.write_source("src/Game/obj/Ignored.cs", "class Generated { StorySave save; }\n")

        result = self.run_guard()

        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("src/Game/Src.cs:1: campaign-save: StorySave", result.stdout)
        self.assertNotIn("Ignored.cs", result.stdout)
        self.assertIn("checked 1 C# files; 1 violation(s)", result.stdout)

    def test_reintroduced_deleted_player_files_are_rejected_even_when_empty(self):
        self.write_source("src/Game/PlayerScan.cs", "")
        self.write_source("src/Game/PlayerDialog.cs", "")

        result = self.run_guard()

        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("src/Game/PlayerDialog.cs:1: retired-player-file: PlayerDialog.cs", result.stdout)
        self.assertIn("src/Game/PlayerScan.cs:1: retired-player-file: PlayerScan.cs", result.stdout)
        self.assertIn("checked 2 C# files; 2 violation(s)", result.stdout)

    def test_missing_or_empty_source_root_fails(self):
        missing = self.run_guard(self.root / "missing")
        self.assertEqual(missing.returncode, 2)
        self.assertEqual(missing.stdout, "")
        self.assertIn("missing source directory", missing.stderr)

        (self.root / "src").mkdir()
        empty = self.run_guard()
        self.assertEqual(empty.returncode, 2)
        self.assertEqual(empty.stdout, "")
        self.assertIn("no C# sources found", empty.stderr)

    def test_reports_are_deterministic_and_sorted_by_relative_path(self):
        self.write_source("src/Zed.cs", "class Zed { GameState.Mode mode; }\n")
        self.write_source("src/Nested/Middle.cs", "class Middle { LaunchKind.Offline kind; }\n")
        self.write_source("src/Aaa.cs", "class Aaa { StorySave save; }\n")

        first = self.run_guard()
        second = self.run_guard()

        self.assertEqual(first.returncode, 1, first.stdout + first.stderr)
        self.assertEqual(first.stdout, second.stdout)
        self.assertEqual(first.stderr, second.stderr)
        report_lines = [
            line
            for line in first.stdout.splitlines()
            if ": " in line and not line.startswith("multiplayer-only source guard:")
        ]
        self.assertEqual(report_lines, sorted(report_lines))
        self.assertEqual(report_lines[0].split(":", 1)[0], "src/Aaa.cs")
        self.assertEqual(report_lines[-1].split(":", 1)[0], "src/Zed.cs")


if __name__ == "__main__":
    unittest.main()
