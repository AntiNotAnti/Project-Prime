"""Parser-only G1 checks; these tests never launch game processes."""
import contextlib
import importlib.util
import io
from pathlib import Path
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


if __name__ == '__main__':
    unittest.main()
