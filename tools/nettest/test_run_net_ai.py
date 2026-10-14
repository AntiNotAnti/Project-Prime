"""Parser-only G1 checks; these tests never launch game processes."""
import contextlib
import importlib.util
import io
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('run_net_ai_tests', Path(__file__).with_name('run-net2.py'))
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
BASE = ['--build', '/tmp/build', '--paths', '/tmp/paths.txt', '--source', 'a'*40,
        '--out', '/tmp/ai-test', '--protocol', '51']


class AiParserTests(unittest.TestCase):
    def parse(self, *args):
        with contextlib.redirect_stderr(io.StringIO()):
            return RUNNER.parse_args(BASE + list(args))

    def rejects(self, *args):
        with self.assertRaises(SystemExit) as caught:
            self.parse(*args)
        self.assertEqual(caught.exception.code, 2)

    def test_default_and_explicit_levels(self):
        self.assertIsNone(self.parse().ai_net_client)
        args = self.parse('--ai-net-client')
        self.assertEqual((args.ai_net_client, args.ai_seed), (2, 1729))
        for level in range(4):
            args = self.parse('--ai-net-client', str(level), '--ai-seed', '44')
            self.assertEqual((args.ai_net_client, args.ai_seed), (level, 44))

    def test_server_powerups_default_and_opt_in(self):
        ordinary = self.parse()
        self.assertFalse(ordinary.powerups)
        self.assertEqual(RUNNER.server_flags(ordinary), [])
        enabled = self.parse("--powerups")
        self.assertTrue(enabled.powerups)
        self.assertEqual(RUNNER.server_flags(enabled), ["-powerups"])
        ai = self.parse("--ai-net-client", "--powerups")
        self.assertEqual(RUNNER.server_flags(ai), ["-powerups"])

    def test_ai_options_require_ai_mode(self):
        self.rejects('--server-bot')
        self.rejects('--ai-seed', '44')

    def test_native_ai_disallows_scripted_scenarios(self):
        for scenario in ('duel', 'missile', 'shockcoil', 'alt-morph'):
            self.rejects('--ai-net-client', '--scenario', scenario)
        for options in (('--soak',), ('--observer-delay', '2'), ('--bomb-triangle-fixture',)):
            self.rejects('--ai-net-client', *options)

    def test_level_and_unsigned_seed_boundaries(self):
        for level in ('-1', '4'):
            self.rejects('--ai-net-client', level)
        for seed in ('0', '-1', '4294967295', 'nan'):
            self.rejects('--ai-net-client', '--ai-seed', seed)
        self.assertEqual(self.parse('--ai-net-client', '--ai-seed', '4294967294').ai_seed, 4294967294)

    def test_bot_capacity_and_network_seed_independence(self):
        self.rejects('--ai-net-client', '--players', '8', '--server-bot')
        args = self.parse('--ai-net-client', '--players', '4', '--server-bot', '--seed', '-100', '--ai-seed', '50')
        self.assertEqual((args.seed, args.ai_seed, args.server_bot), (-100, 50, True))


class FrozenBuildStagingTests(unittest.TestCase):
    def test_staging_keeps_entry_physical_and_dependency_link_shared(self):
        with tempfile.TemporaryDirectory() as root:
            root = Path(root)
            build = root / "build"
            build.mkdir()
            for name in ("ProjectPrime.dll", "ProjectPrime.deps.json", "ProjectPrime.runtimeconfig.json"):
                (build / name).write_bytes(name.encode())
            dependency = root / "shared.dll"
            dependency.write_bytes(b"unchanged package dependency")
            (build / dependency.name).symlink_to(dependency)
            stage = root / "stage"
            receipt = RUNNER.stage_build(build, stage)
            self.assertTrue((stage / dependency.name).is_symlink())
            self.assertEqual((stage / dependency.name).read_bytes(), dependency.read_bytes())
            for name, evidence in receipt.items():
                self.assertFalse((stage / name).is_symlink())
                self.assertEqual(evidence["sha256"], RUNNER.digest(build / name))

    def test_staging_refuses_a_redirected_entry_point(self):
        with tempfile.TemporaryDirectory() as root:
            root = Path(root)
            build = root / "build"
            build.mkdir()
            client = root / "other-client.dll"
            client.write_bytes(b"wrong source")
            (build / "ProjectPrime.dll").symlink_to(client)
            with self.assertRaisesRegex(RuntimeError, "physical file"):
                RUNNER.stage_build(build, root / "stage")
            self.assertFalse((root / "stage").exists())


class TimedEvidenceRunnerTests(unittest.TestCase):
    parse = AiParserTests.parse
    rejects = AiParserTests.rejects

    def environment(self, args, name="server"):
        return RUNNER.child_environment(args, name, Path("/tmp/isolated-profile"), {
            "PATH": "/runtime", "PRIME_TIMED_GRANT_PROOF": "enforce",
            "PRIME_TIMED_REPLAY_FACTS": "1", "PRIME_SHOT_PROOF": "enforce",
            "MPHREAD_UNDECLARED": "1", "PROJECT_PRIME_UNDECLARED": "1"})

    def test_default_clears_inherited_timed_flags_and_keeps_legacy_policy(self):
        args = self.parse()
        self.assertEqual((args.timed_grant_proof, args.timed_replay_facts), ("off", False))
        env = self.environment(args)
        self.assertEqual(env["PATH"], "/runtime")
        self.assertEqual(env["PRIME_SHOT_PROOF"], "shadow")
        for flag in ("PRIME_TIMED_GRANT_PROOF", "PRIME_TIMED_REPLAY_FACTS",
                     "MPHREAD_UNDECLARED", "PROJECT_PRIME_UNDECLARED"):
            self.assertNotIn(flag, env)
        command = RUNNER.authority_arguments(args, Path("/tmp/rotation.txt"))
        self.assertIn("-noserverreplays", command)
        self.assertNotIn("-recorddemo", command)

    def test_shadow_observation_is_independent_of_legacy_mode_and_record(self):
        args = self.parse("--timed-grant-proof", "shadow", "--shot-proof", "off", "--record")
        for role in ("server", "peer1"):
            env = self.environment(args, role)
            self.assertEqual(env["PRIME_TIMED_GRANT_PROOF"], "shadow")
            self.assertEqual(env["PRIME_SHOT_PROOF"], "off")
            self.assertNotIn("PRIME_TIMED_REPLAY_FACTS", env)
        self.assertIn("-noserverreplays", RUNNER.authority_arguments(args, Path("/tmp/rotation.txt")))

    def test_archival_opt_in_records_authority_and_sets_only_its_replay_flag(self):
        args = self.parse("--timed-grant-proof", "shadow", "--timed-replay-facts", "--powerups")
        env = self.environment(args)
        self.assertEqual(env["PRIME_TIMED_REPLAY_FACTS"], "1")
        self.assertNotIn("PRIME_TIMED_REPLAY_FACTS", self.environment(args, "peer1"))
        command = RUNNER.authority_arguments(args, Path("/tmp/rotation.txt"))
        self.assertNotIn("-noserverreplays", command)
        self.assertIn("-powerups", command)
        self.assertEqual(command[command.index("-rotation") + 1], "/tmp/rotation.txt")
        self.assertFalse(args.record)  # Archival opt-in does not request client files.

    def test_archival_off_and_production_enforce_are_rejected_before_launch(self):
        self.rejects("--timed-replay-facts")
        self.rejects("--timed-grant-proof", "off", "--timed-replay-facts")
        self.rejects("--timed-grant-proof", "enforce")
        # The pre-existing legacy mode remains a separate permitted contract.
        self.assertEqual(self.parse("--shot-proof", "enforce").shot_proof, "enforce")

class PacingLobbyParserTests(unittest.TestCase):
    parse = AiParserTests.parse
    rejects = AiParserTests.rejects

    def test_lobby_route_uses_common_authentication_not_a1_server_switch(self):
        with tempfile.TemporaryDirectory() as root:
            entry = Path(root) / "nettest.dll"
            entry.write_bytes(b"isolated entry")
            args = self.parse('--pacing-lobby-client', str(entry), '--players', '8',
                              '--ai-net-client', '2', '--powerups')
            args.pacing_owner_token = 'explicit-test-token'
            self.assertEqual(RUNNER.server_flags(args), ['-lobby'])

    def test_lobby_route_refuses_incompatible_workloads_before_staging(self):
        with tempfile.TemporaryDirectory() as root:
            entry = Path(root) / "nettest.dll"
            entry.write_bytes(b"isolated entry")
            valid = ['--pacing-lobby-client', str(entry), '--players', '8', '--ai-net-client', '--powerups']
            for extra in (('--players', '4'), ('--rendered',), ('--server-bot',), ('--rooms', 'A,B')):
                self.rejects(*valid, *extra)
            self.rejects('--pacing-lobby-client', str(entry), '--players', '8', '--ai-net-client')
            self.rejects('--pacing-lobby-client', str(entry) + '.missing', '--players', '8', '--ai-net-client', '--powerups')


if __name__ == '__main__':
    unittest.main()
