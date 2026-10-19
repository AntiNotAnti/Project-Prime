import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest

import fruity_comparator as native

spec = importlib.util.spec_from_file_location('run_net2_fruity_tests', Path(__file__).with_name('run-net2.py'))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class FruityComparatorTests(unittest.TestCase):
    def args(self):
        return SimpleNamespace(port=28193, players=3, seconds=25, rendered=False, record=False,
                               scenario='tour', fruity_hitrig=None)

    def test_native_environment_clears_all_game_policy_overrides(self):
        env = native.environment(Path('/private/fixture'), {'PATH': '/bin',
            'FRUITY_NCX_USER_DATA': '/old', 'PRIME_CONFIRMED_IMPACTS': '1',
            'MPHREAD_PHASE': 'one', 'PROJECT_PRIME_USER_DATA': '/normal', 'DYLD_LIBRARY_PATH': '/bad'})
        self.assertEqual(env, {'PATH': '/bin', 'FRUITY_NCX_USER_DATA': '/private/fixture', 'ALSOFT_DRIVERS': 'null'})

    def test_server_dispatch_keeps_upstream_combat_and_replay_defaults(self):
        argv = native.command(self.args(), Path('/bin/FruityPrime'), Path('/maps'), 'server', rotation=Path('/rotation'))
        for flag in ('-server', '-nomaster', '-rotation'): self.assertIn(flag, argv)
        for flag in ('-servershots', '-noclaims', '-nohitprediction', '-noserverreplays', '-nographics'):
            self.assertNotIn(flag, argv)
        self.assertEqual(argv[0], '/bin/FruityPrime')

    def test_native_clients_use_headless_and_existing_observer_mode(self):
        args = self.args()
        argv = native.command(args, Path('/binary'), Path('/maps'), 'peer-2', slot=2, faults=['-netlag', '100:20'])
        self.assertIn('-headless', argv)
        self.assertEqual(argv[argv.index('-hitrig') + 1], 'observe')
        self.assertNotIn('-spectate', argv)
        self.assertNotIn('-nographics', argv)
        self.assertIn('100:20', argv)
        args.rendered = True
        self.assertNotIn('-headless', native.command(args, Path('/binary'), Path('/maps'), 'peer-0'))

    def test_fixed_fixture_mode_is_only_an_existing_native_cli(self):
        args = self.args()
        args.fruity_hitrig = 'wells:powerbeam'
        argv = native.command(args, Path('/binary'), Path('/maps'), 'peer-0')
        self.assertEqual(argv[argv.index('-hitrig') + 1], 'wells:powerbeam')
        self.assertNotIn('-servershots', argv)

    def test_emulated_fixture_contract_and_roles_match_existing_native_route(self):
        maps = Path(__file__).parent / 'fixtures/ncx-emulated'
        for scenario, (room, rig) in native.NCX_FIXTURES.items():
            args = self.args()
            args.scenario, args.rooms, args.fixture_maps = scenario, room, maps
            contract = native.qualified_fixture(args)
            self.assertEqual(contract['room'], room)
            self.assertEqual(len(contract['definitions']), 3)
            self.assertEqual([runner.client_role(args, slot) for slot in range(3)],
                             [('Samus', False, True), ('Samus', False, True), ('Samus', True, False)])
            argv = native.command(args, Path('/binary'), maps, 'peer-1', slot=1)
            self.assertEqual(argv[argv.index('-hitrig') + 1], rig)
            observer = native.command(args, Path('/binary'), maps, 'peer-2', slot=2)
            self.assertEqual(observer[observer.index('-hitrig') + 1], 'observe')
            for path in maps.glob('*.json'):
                definition = json.loads(path.read_text())
                self.assertEqual(len(definition['brushes']), 6)
                self.assertTrue(all(b['solid'] and 'noBeams' not in b and 'visible' not in b for b in definition['brushes']))
            args.rooms = 'DIFFERENT ROOM'
            with self.assertRaises(ValueError): native.qualified_fixture(args)
        with tempfile.TemporaryDirectory() as directory:
            copied = Path(directory) / 'maps'
            native.shutil.copytree(maps, copied)
            (copied / 'static.json').write_text('{}')
            args.scenario, args.rooms, args.fixture_maps = 'ncx-static', 'NCX EMULATED STATIC', copied
            with self.assertRaises(ValueError): native.qualified_fixture(args)

    def test_text_reports_require_actual_join_snapshots_and_native_result(self):
        text = '[netcheck] peer-0 joined slot 0, loading ROOM (Battle)\n  packets: snapshots sent=0 received=187 late=0\n  RESULT: FAIL -- coverage\n'
        self.assertEqual(native.observe_client(text), {'joinedSlots': [0], 'snapshotsReceived': 187, 'nativeResult': 'FAIL'})
        self.assertEqual(native.observe_client('anything')['nativeResult'], 'UNAVAILABLE')

    def test_native_failures_are_preserved_even_with_successful_udp(self):
        records = [dict(name='server', expectedClients=2, exit=-15, timedOut=False)] + [
            dict(name='peer-' + str(i), exit=1, timedOut=False) for i in range(2)]
        texts = {r['name']: '[netcheck] peer joined slot 0, loading ROOM\npackets: snapshots sent=0 received=30\nRESULT: FAIL\n'
                 for r in records[1:]}
        result = native.summarize(records, texts, True)
        self.assertEqual(result['udpControlStatus'], 'PASS')
        self.assertEqual(result['nativeHarnessStatus'], 'FAIL')
        self.assertEqual(result['status'], 'FAIL')
        self.assertTrue(result['failures'])

    def test_no_native_control_becomes_drain_or_comparison_pass(self):
        records = [dict(name='server', expectedClients=2, exit=-15, timedOut=False)] + [
            dict(name='peer-' + str(i), exit=0, timedOut=False) for i in range(2)]
        texts = {r['name']: '[netcheck] peer joined slot 0, loading ROOM\npackets: snapshots sent=0 received=30\nRESULT: PASS\n'
                 for r in records[1:]}
        result = native.summarize(records, texts, True)
        self.assertEqual(result['udpControlStatus'], 'PASS')
        self.assertEqual(result['nativeHarnessStatus'], 'PASS')
        self.assertEqual(result['status'], 'INCOMPLETE')
        self.assertTrue(result['capabilities']['drain'].startswith('UNAVAILABLE'))
        self.assertEqual(result['capabilities']['comparison'], 'NOT_QUALIFIED')
        records.pop()
        self.assertEqual(native.summarize(records, texts, True)['udpControlStatus'], 'FAIL')

    def test_qualified_package_rejects_binary_adapter_or_runtime_changes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            build = root / 'FruityPrime.app'
            binary = build / 'Contents/MacOS/FruityPrime'
            binary.parent.mkdir(parents=True)
            binary.write_bytes(b'binary')
            patch, library = root / 'fruity-private-profile.patch', root / 'runtime.dylib'
            patch.write_bytes(b'profile adapter')
            library.write_bytes(b'runtime')
            manifest = dict(status='PASS', exit=0, sourceCommit='abc', adapterPatchSha256=native.digest(patch),
                            binarySha256=native.digest(binary),
                            loadedNonSystemLibraries=[dict(resolvedPath=str(library), sha256=native.digest(library))])
            path = root / 'manifest.json'
            path.write_text(json.dumps(manifest))
            source = 'abc+profile-adapter:' + native.digest(patch)
            self.assertEqual(native.qualify_package(build, path, source), manifest)
            for changed in (binary, patch, library):
                original = changed.read_bytes()
                changed.write_bytes(b'changed')
                with self.assertRaises(ValueError): native.qualify_package(build, path, source)
                changed.write_bytes(original)
            with self.assertRaises(ValueError): native.qualify_package(build, path, 'abc')

    def test_prime_default_and_visibility_dispatch_are_explicit(self):
        base = ['--build', '.', '--paths', 'paths.txt', '--source', 'fixture', '--out', '/private/fresh']
        args = runner.parse_args(base)
        self.assertEqual(args.engine, 'prime')
        self.assertFalse(args.shot_visibility)
        for flags in (['--shot-visibility'], ['--shot-visibility', '--rendered'], ['--shot-visibility', '--shot-timeline']):
            with self.assertRaises(SystemExit): runner.parse_args(base + flags)
        args = runner.parse_args(base + ['--shot-visibility', '--shot-timeline', '--rendered'])
        server = runner.child_environment(args, 'server', Path('/private/server'), {})
        peer = runner.child_environment(args, 'peer-0', Path('/private/peer-0'), {})
        self.assertNotIn('PRIME_NCX_VISIBILITY', server)
        self.assertEqual(peer['PRIME_NCX_VISIBILITY'], '/private/peer-0/ncx-visibility.json')
        self.assertEqual(peer['PRIME_NCX_TIMELINE'], '1')

    def test_powerbeam_roles_and_requested_rate_are_dispatch_only(self):
        base = ['--build', '.', '--paths', 'paths.txt', '--source', 'fixture', '--out', '/private/fresh',
                '--scenario', 'powerbeam', '--players', '3', '--rendered', '--shot-timeline']
        args = runner.parse_args(base)
        self.assertEqual([runner.client_role(args, i) for i in range(3)],
                         [('Samus', False, True), ('Kanden', False, False), ('Trace', True, False)])
        self.assertIsNone(args.presentation_hz)
        self.assertNotIn('PRIME_NCX_PRESENTATION_HZ', runner.child_environment(args, 'peer-0', Path('/private/peer-0'), {}))
        for rate in (30, 60, 120, 144, 240, 360, 500):
            args = runner.parse_args(base + ['--presentation-hz', str(rate)])
            self.assertEqual(runner.child_environment(args, 'peer-0', Path('/private/peer-0'), {})['PRIME_NCX_PRESENTATION_HZ'], str(rate))
            self.assertNotIn('PRIME_NCX_PRESENTATION_HZ', runner.child_environment(args, 'server', Path('/private/server'), {}))
        with self.assertRaises(SystemExit):
            runner.parse_args([v for v in base if v != '--rendered'] + ['--presentation-hz', '60'])
        with self.assertRaises(SystemExit):
            runner.parse_args([v for v in base if v != '--shot-timeline'] + ['--presentation-hz', '60'])

    def test_native_options_reject_unsupported_or_unqualified_controls(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            build = root / 'FruityPrime.app'
            build.mkdir()
            base = ['--build', str(build), '--paths', 'paths.txt', '--source', 'fixture', '--out', '/private/fresh',
                    '--engine', 'fruity-native', '--comparator-manifest', 'manifest.json', '--protocol', '34']
            self.assertEqual(runner.parse_args(base).engine, 'fruity-native')
            for flags in (['--drain-seconds', '5'], ['--shot-timeline'], ['--confirmed-impacts'],
                          ['--protocol', '59'], ['--shot-proof', 'enforce'], ['--rendered', '--presentation-hz', '60']):
                with self.assertRaises(SystemExit): runner.parse_args(base + flags)
            with self.assertRaises(SystemExit): runner.parse_args(base + ['--fruity-hitrig', 'wells:powerbeam'])


if __name__ == '__main__': unittest.main()
