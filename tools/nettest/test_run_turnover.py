"""Dispatch/validation only; no process, gameplay, rotation or cohort acceptance."""
import contextlib
import importlib.util
import io
from itertools import combinations
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location("run_turnover_tests", Path(__file__).with_name("run-net2.py"))
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
BASE = ["--build", "build", "--paths", "paths.txt", "--source", "test-source", "--out", "out", "--protocol", "57"]
FIELDS = ("turnover_client_slot", "spectate_at", "rejoin_at")
OPTIONS = ("--turnover-client-slot", "--spectate-at", "--rejoin-at")


class TurnoverDispatchTests(unittest.TestCase):
    def parse(self, *extra):
        with contextlib.redirect_stderr(io.StringIO()):
            return RUNNER.parse_args(BASE + list(extra))

    def rejects(self, *extra):
        with self.assertRaises(SystemExit) as caught:
            self.parse(*extra)
        self.assertEqual(caught.exception.code, 2)

    def test_omitted_controls_preserve_namespace_manifest_and_arguments(self):
        control = self.parse("--players", "3", "--seconds", "60", "--drain-seconds", "20")
        selected = self.parse("--players", "3", "--seconds", "60", "--drain-seconds", "20",
            "--turnover-client-slot", "1", "--spectate-at", "10", "--rejoin-at", "20")
        self.assertTrue(all(field not in vars(control) for field in FIELDS))
        self.assertEqual(vars(control), {key: value for key, value in vars(selected).items() if key not in FIELDS})
        self.assertEqual(RUNNER.authority_arguments(control, Path("rotation.txt")),
                         RUNNER.authority_arguments(selected, Path("rotation.txt")))
        for slot in range(control.players):
            self.assertEqual(RUNNER.client_role(control, slot), RUNNER.client_role(selected, slot))
            self.assertEqual(RUNNER.fault_arguments(control, slot), RUNNER.fault_arguments(selected, slot))
            self.assertEqual(RUNNER.turnover_arguments(control, slot), [])

    def test_only_each_explicit_client_receives_native_timer_arguments(self):
        for players in (2, 3, 4, 8):
            for chosen in range(players):
                with self.subTest(players=players, chosen=chosen):
                    args = self.parse("--players", str(players), "--seconds", "120",
                        "--turnover-client-slot", str(chosen), "--spectate-at", "0", "--rejoin-at", "20.5")
                    for slot in range(players):
                        self.assertEqual(RUNNER.turnover_arguments(args, slot),
                            ["-spectate", "0.0", "-rejoin", "20.5"] if slot == chosen else [])
                        self.assertFalse(RUNNER.client_role(args, slot)[1])

    def test_all_three_options_are_required_as_a_tuple(self):
        values = ("0", "10", "20")
        for count in (1, 2):
            for indices in combinations(range(3), count):
                extra = [value for index in indices for value in (OPTIONS[index], values[index])]
                self.rejects(*extra)

    def test_slot_must_be_a_real_launched_client(self):
        for slot in ("-1", "2", "8", "1.5", "nan"):
            self.rejects("--players", "2", "--turnover-client-slot", slot, "--spectate-at", "10", "--rejoin-at", "20")

    def test_times_must_be_finite_ordered_and_within_activity_duration(self):
        for spectate, rejoin in (("-1", "20"), ("10", "10"), ("20", "10"), ("0", "0"),
                                 ("10", "60"), ("10", "61"), ("nan", "20"), ("10", "nan"),
                                 ("inf", "20"), ("10", "inf"), ("-inf", "20"), ("10", "-inf")):
            with self.subTest(spectate=spectate, rejoin=rejoin):
                self.rejects("--seconds", "60", "--turnover-client-slot", "0",
                    "--spectate-at", spectate, "--rejoin-at", rejoin)

    def test_incompatible_existing_controllers_are_rejected(self):
        selected = ["--turnover-client-slot", "0", "--spectate-at", "10", "--rejoin-at", "20"]
        for extra in (("--ai-net-client",), ("--soak", "--players", "8", "--seconds", "1200"),
                      ("--rendered",), ("--observer-delay", "1")):
            self.rejects(*selected, *extra)
        for scenario in ("duel", "shockcoil", "shockcoil-all", "shockcoil-mixed", "missile",
                         "self-moving", "self-wall", "alt-morph", *RUNNER.BOMB_HUNTERS):
            self.rejects(*selected, "--scenario", scenario)


if __name__ == "__main__":
    unittest.main()
