"""Dispatch-only parser/role checks; no process, socket, assets, or form acceptance."""
import contextlib
import importlib.util
import io
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location("run_form_hunter_tests", Path(__file__).with_name("run-net2.py"))
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
BASE = ["--build", "build", "--paths", "paths.txt", "--source", "test-source", "--out", "out", "--protocol", "57"]


class FormHunterDispatchTests(unittest.TestCase):
    def parse(self, *extra):
        with contextlib.redirect_stderr(io.StringIO()):
            return RUNNER.parse_args(BASE + list(extra))

    def rejects(self, *extra):
        with self.assertRaises(SystemExit) as caught:
            self.parse(*extra)
        self.assertEqual(caught.exception.code, 2)

    def test_omitted_option_preserves_default_namespace_manifest_and_roster(self):
        ordinary = self.parse("--scenario", "alt-morph", "--players", "3", "--observer-delay", "6")
        self.assertNotIn("form_hunter", vars(ordinary))
        self.assertEqual([RUNNER.client_role(ordinary, slot) for slot in range(3)],
                         [("Samus", False, True), ("Weavel", False, False), ("Trace", True, False)])
        explicit = self.parse("--scenario", "alt-morph", "--players", "3", "--observer-delay", "6", "--form-hunter", "Weavel")
        self.assertEqual(vars(ordinary), {key: value for key, value in vars(explicit).items() if key != "form_hunter"})
        self.assertEqual([RUNNER.client_role(explicit, slot) for slot in range(3)],
                         [RUNNER.client_role(ordinary, slot) for slot in range(3)])

    def test_each_supported_hunter_changes_only_target_role_at_each_requested_rtt(self):
        for hunter in RUNNER.HUNTERS[:-1]:
            for rtt in (45, 150, 250, 350, 750):
                with self.subTest(hunter=hunter, rtt=rtt):
                    control = self.parse("--scenario", "alt-morph", "--players", "3", "--observer-delay", "6", "--rtt-ms", str(rtt))
                    selected = self.parse("--scenario", "alt-morph", "--players", "3", "--observer-delay", "6", "--rtt-ms", str(rtt), "--form-hunter", hunter)
                    self.assertEqual(RUNNER.client_role(selected, 1), (hunter, False, False))
                    for slot in (0, 2):
                        self.assertEqual(RUNNER.client_role(selected, slot), RUNNER.client_role(control, slot))
                    self.assertEqual(RUNNER.fault_arguments(selected, 1), RUNNER.fault_arguments(control, 1))
                    self.assertEqual(RUNNER.authority_arguments(selected, Path("rotation.txt")),
                                     RUNNER.authority_arguments(control, Path("rotation.txt")))
                    self.assertEqual(vars(control), {key: value for key, value in vars(selected).items() if key != "form_hunter"})

    def test_target_selection_retains_rendered_and_eight_player_roles(self):
        for extra in (("--players", "3", "--rendered"), ("--players", "8")):
            control = self.parse("--scenario", "alt-morph", *extra)
            selected = self.parse("--scenario", "alt-morph", *extra, "--form-hunter", "Spire")
            for slot in range(control.players):
                expected = RUNNER.client_role(control, slot)
                self.assertEqual(RUNNER.client_role(selected, slot), ("Spire", *expected[1:]) if slot == 1 else expected)

    def test_option_requires_alt_morph_and_a_supported_explicit_name(self):
        for scenario in ("tour", "duel", "shockcoil", "bomb-samus", "bomb-kanden", "bomb-lockjaw"):
            self.rejects("--scenario", scenario, "--form-hunter", "Samus")
        self.rejects("--form-hunter", "Samus")
        for value in ("Trooper", "samus", "", "Unknown"):
            self.rejects("--scenario", "alt-morph", "--form-hunter", value)
        self.rejects("--scenario", "alt-morph", "--form-hunter")

    def test_existing_ai_scripted_mode_rejection_remains_in_force(self):
        self.rejects("--ai-net-client", "--scenario", "alt-morph", "--form-hunter", "Samus")

    def test_legacy_role_callers_without_new_attribute_keep_weavel_default(self):
        args = self.parse("--scenario", "alt-morph")
        self.assertFalse(hasattr(args, "form_hunter"))
        self.assertEqual(RUNNER.client_role(args, 1), ("Weavel", False, False))


if __name__ == "__main__":
    unittest.main()
