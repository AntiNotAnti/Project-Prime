import importlib.util
from pathlib import Path
from types import SimpleNamespace
import unittest


spec = importlib.util.spec_from_file_location('run_net2_timeline', Path(__file__).with_name('run-net2.py'))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class ShotTimelineDispatchTests(unittest.TestCase):
    def args(self, enabled=False):
        return SimpleNamespace(out=Path('/private/fixture'), shot_proof='shadow',
                               timed_grant_proof='off', evidence_row_limit=None,
                               timed_replay_facts=False, shot_timeline=enabled)

    def test_default_does_not_inherit_or_enable_timeline(self):
        env = runner.child_environment(self.args(), 'server', Path('/private/profile'),
                                       {'PRIME_NCX_TIMELINE': '1', 'PATH': '/bin'})
        self.assertNotIn('PRIME_NCX_TIMELINE', env)
        self.assertEqual(env['PATH'], '/bin')
        self.assertEqual(env['PRIME_SHOT_PROOF'], 'shadow')

    def test_explicit_opt_in_is_the_only_timeline_dispatch(self):
        env = runner.child_environment(self.args(True), 'peer-0', Path('/private/profile'), {})
        self.assertEqual(env['PRIME_NCX_TIMELINE'], '1')
        self.assertEqual(env['PRIME_NET2_EVIDENCE'], str(Path('/private/fixture/peer-0.json')))
        self.assertNotIn('PRIME_TIMED_GRANT_PROOF', env)

    def test_command_line_defaults_off(self):
        base = ['--build', '.', '--paths', 'paths.txt', '--source', 'fixture', '--out', '/private/fresh']
        self.assertFalse(runner.parse_args(base).shot_timeline)
        self.assertTrue(runner.parse_args(base + ['--shot-timeline']).shot_timeline)

    def test_fast_shadow_is_declared_and_authority_only(self):
        args = self.args()
        inherited = {'PRIME_COMBAT_FASTPATH': 'shadow', 'PATH': '/bin'}
        self.assertNotIn('PRIME_COMBAT_FASTPATH', runner.child_environment(args, 'server', Path('/private/profile'), inherited))
        args.combat_fastpath = 'shadow'
        server = runner.child_environment(args, 'server', Path('/private/profile'), inherited)
        self.assertEqual(server['PRIME_COMBAT_FASTPATH'], 'shadow')
        self.assertNotIn('PRIME_COMBAT_FASTPATH', runner.child_environment(args, 'peer-0', Path('/private/profile'), inherited))
        self.assertEqual(server['PATH'], '/bin')

    def test_fast_shadow_cli_defaults_off_and_cannot_enable_settlement(self):
        base = ['--build', '.', '--paths', 'paths.txt', '--source', 'fixture', '--out', '/private/fresh']
        self.assertEqual(runner.parse_args(base).combat_fastpath, 'off')
        self.assertEqual(runner.parse_args(base + ['--combat-fastpath', 'shadow']).combat_fastpath, 'shadow')
        with self.assertRaises(SystemExit):
            runner.parse_args(base + ['--combat-fastpath', 'eligible'])

    def test_paid_mode_is_authority_only_and_requires_declared_evidence(self):
        args = self.args(True); args.combat_fastpath = 'paid'
        inherited = {'PRIME_COMBAT_FASTPATH': 'paid'}
        self.assertEqual(runner.child_environment(args, 'server', Path('/private/profile'), inherited)['PRIME_COMBAT_FASTPATH'], 'paid')
        self.assertNotIn('PRIME_COMBAT_FASTPATH', runner.child_environment(args, 'peer-0', Path('/private/profile'), inherited))
        base = ['--build', '.', '--paths', 'paths.txt', '--source', 'fixture', '--out', '/private/fresh']
        with self.assertRaises(SystemExit): runner.parse_args(base + ['--combat-fastpath', 'paid'])
        parsed = runner.parse_args(base + ['--combat-fastpath', 'paid', '--shot-timeline', '--claim-receipts'])
        self.assertTrue(parsed.paid_receipt_latency)
        parsed = runner.parse_args(base + ['--combat-fastpath', 'shadow', '--shot-timeline', '--claim-receipts', '--paid-receipt-latency'])
        self.assertTrue(parsed.paid_receipt_latency)


if __name__ == '__main__':
    unittest.main()
