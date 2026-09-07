"""Fixture coverage for the bounded telemetry export and analysis CLI."""

import gzip
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import uuid


ROOT = Path(__file__).resolve().parents[2]
TOOLS_PROJECT = ROOT / "src" / "Tools" / "Tools.csproj"
DOTNET = os.environ.get("DOTNET") or shutil.which("dotnet")


def event(tick, kind, slot=0, life=1, x=0.0, y=0.0, z=0.0, *, team=0,
          hunter=0, weapon=255, value=0, subject=0, other_slot=255,
          enemy_distance=None, visible_enemies=None, other_hunter=255):
    return {
        "Tick": tick,
        "Kind": kind,
        "Slot": slot,
        "Life": life,
        "X": x,
        "Y": y,
        "Z": z,
        "Team": team,
        "Hunter": hunter,
        "Weapon": weapon,
        "Value": value,
        "Subject": subject,
        "OtherSlot": other_slot,
        "EnemyDistance": enemy_distance,
        "VisibleEnemies": visible_enemies,
        "OtherHunter": other_hunter,
    }


def fixture_events():
    events = [
        # Position samples form two route cells and provide the flag carry path.
        event(0, 0, x=0, z=0, hunter=0),
        event(60, 0, x=4, z=4, hunter=0),
        event(0, 0, slot=1, x=20, z=0, hunter=2),
        event(60, 0, slot=1, x=24, z=0, hunter=2),
        # Three independent spawn lives, with exact 3/5/10 second death windows.
        event(0, 1, x=0, z=0, hunter=0, enemy_distance=12.5, visible_enemies=1),
        event(600, 1, life=2, x=4, z=0, hunter=0, enemy_distance=20.0, visible_enemies=0),
        event(1200, 1, life=3, x=8, z=0, hunter=0, enemy_distance=28.0, visible_enemies=1),
        event(180, 4, x=0, z=0, hunter=0, weapon=1),
        event(900, 4, life=2, x=4, z=0, hunter=0, weapon=1),
        event(1800, 4, life=3, x=8, z=0, hunter=0, weapon=1),
        # Pickup -> kill and damage-share facts for Hunter 2.
        event(100, 5, slot=1, hunter=2, weapon=5, subject=44, value=1),
        event(160, 3, slot=1, hunter=2, weapon=1, other_slot=0, other_hunter=0),
        event(120, 2, slot=0, hunter=0, weapon=1, value=40, other_slot=1, other_hunter=2),
        event(121, 2, slot=0, hunter=0, weapon=3, value=60, other_slot=1, other_hunter=2),
        # Flag path, node contest interval, and overtime objective transitions.
        event(299, 0, slot=0, hunter=0, x=0, z=0),
        event(300, 5, slot=0, hunter=0, value=3, subject=77),
        event(300, 0, slot=0, hunter=0, x=0, z=0),
        event(360, 0, slot=0, hunter=0, x=3, z=4),
        event(420, 5, slot=0, hunter=0, value=6, subject=77),
        event(500, 5, value=8, subject=99, weapon=1),
        event(620, 5, value=8, subject=99, weapon=0),
        event(700, 5, value=11, weapon=1),
    ]
    return events


def telemetry_fixture(events=None):
    return {
        "Format": 1,
        "Id": str(uuid.UUID("11111111-2222-3333-4444-555555555555")),
        "Room": "TELEMETRY FIXTURE",
        "Mode": 0,
        "MatchId": 9,
        "StartTick": 0,
        "EndTick": 2400,
        "Completed": True,
        "DroppedEvents": 0,
        "Events": fixture_events() if events is None else events,
    }


@unittest.skipUnless(DOTNET, "dotnet SDK is required for telemetry CLI fixtures")
class TelemetryCliTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="prime-telemetry-cli-")
        self.root = Path(self.temporary.name)
        self.input = self.root / "fixture.telemetry.json.gz"
        self.write_fixture(telemetry_fixture())

    def tearDown(self):
        self.temporary.cleanup()

    def write_fixture(self, value, path=None):
        path = self.input if path is None else Path(path)
        with gzip.open(path, "wb") as output:
            output.write(json.dumps(value, separators=(",", ":")).encode("utf-8"))
        return path

    def run_cli(self, mode, input_path=None, prefix=None):
        input_path = self.input if input_path is None else Path(input_path)
        prefix = self.root / mode if prefix is None else Path(prefix)
        command = [str(DOTNET), "run", "--project", str(TOOLS_PROJECT), "-c", "Release", "--",
                   "telemetry", mode, str(input_path), str(prefix)]
        return subprocess.run(command, cwd=ROOT, capture_output=True, text=True,
                              timeout=120, check=False), prefix

    def test_all_cli_projections_have_exact_spawn_windows_damage_and_objectives(self):
        outputs = {}
        for mode in ("heatmap", "spawn-safety", "routes", "weapon-control"):
            result, prefix = self.run_cli(mode)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            outputs[mode] = json.loads(prefix.with_suffix(".json").read_text())
            self.assertTrue(prefix.with_suffix(".svg").read_text().startswith("<svg"))
            csv_lines = prefix.with_suffix(".csv").read_text().splitlines()
            self.assertEqual("tick,kind,slot,life,x,y,z,team,hunter,weapon,value,subject,other_slot", csv_lines[0])
            self.assertEqual(len(fixture_events()) + 1, len(csv_lines))
            self.assertIn(f"Prime Hunters {mode}", prefix.with_suffix(".svg").read_text())

        danger = outputs["spawn-safety"]["spawnDanger"]
        self.assertEqual(3, sum(point["count"] for point in danger))
        self.assertEqual(1, sum(point["deathsWithin3Seconds"] for point in danger))
        self.assertEqual(2, sum(point["deathsWithin5Seconds"] for point in danger))
        self.assertEqual(3, sum(point["deathsWithin10Seconds"] for point in danger))

        weapons = {entry["weapon"]: entry for entry in outputs["weapon-control"]["weapons"]}
        self.assertEqual(1, weapons[1]["Pickups"])
        self.assertEqual(1, weapons[1]["KillsAfterPickup"])
        self.assertAlmostEqual(1.0, weapons[1]["averagePickupToKillSeconds"])
        self.assertAlmostEqual(0.4, weapons[1]["damageShare"])
        self.assertAlmostEqual(0.6, weapons[3]["damageShare"])
        hunter = {entry["hunter"]: entry for entry in outputs["weapon-control"]["hunters"]}[2]
        self.assertEqual(100, hunter["Damage"])

        routes = outputs["routes"]
        self.assertAlmostEqual(5.0, routes["sampledFlagCarryDistance"])
        self.assertAlmostEqual(2.0, routes["nodeContestedSeconds"])
        transitions = {entry["kind"]: entry["count"] for entry in routes["objectiveTransitions"]}
        self.assertEqual(1, transitions["OvertimeStarted"])
        self.assertEqual(2, transitions["NodeContested"])

    def test_corrupt_malformed_and_out_of_range_journals_fail_closed(self):
        cases = []
        corrupt = self.root / "corrupt.gz"
        corrupt.write_bytes(b"not a gzip stream")
        cases.append(corrupt)

        malformed = self.root / "malformed.gz"
        self.write_fixture({**telemetry_fixture(), "Format": 99}, malformed)
        cases.append(malformed)

        invalid_event = self.root / "invalid-event.gz"
        bad_events = fixture_events()
        bad_events[0] = {**bad_events[0], "X": 100001}
        self.write_fixture(telemetry_fixture(bad_events), invalid_event)
        cases.append(invalid_event)

        for index, path in enumerate(cases):
            with self.subTest(path=path.name):
                result, prefix = self.run_cli("routes", path, self.root / f"invalid-{index}")
                self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertFalse(prefix.with_suffix(".json").exists())

    def test_decompression_bound_is_enforced_before_json_parsing(self):
        oversized = self.root / "oversized.gz"
        with gzip.open(oversized, "wb") as output:
            output.write(b"x" * (64 * 1024 * 1024 + 1))
        result, prefix = self.run_cli("routes", oversized, self.root / "oversized")
        self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("decoded byte limit", result.stderr)
        self.assertFalse(prefix.with_suffix(".json").exists())


if __name__ == "__main__":
    unittest.main()
