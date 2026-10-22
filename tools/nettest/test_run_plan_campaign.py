"""Wrapper recipes/guards only; no game, native item or fabricated campaign."""
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import Mock, patch

SPEC = importlib.util.spec_from_file_location('plan_campaign', Path(__file__).with_name('run_plan_campaign.py'))
MODULE = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(MODULE)


class PreflightTests(unittest.TestCase):
    def test_unclosed_campaign_blocks_dependent_auditor(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); source = root / 'source'
            (source / 'tools/nettest').mkdir(parents=True)
            for name in ('run-net2.py', 'net04_join_evidence.py'):
                (source / 'tools/nettest' / name).write_text('unit fixture only')
            build = root / 'build'; build.mkdir()
            sdk = root / 'sdk'; sdk.write_text('unit fixture only')
            paths = root / 'paths'; paths.write_text('unit fixture only')
            identity = {'commit': 'abc', 'tree': 'def', 'dirty': ''}
            hashes = {'ProjectPrime.dll': 'hash'}
            metadata = {'status': 'PASS', 'sourceBefore': identity, 'sourceAfter': identity,
                'physicalHashesBefore': hashes, 'physicalHashesAfter': hashes,
                'actualLoadedClient': {'sha256': 'hash', 'mvid': 'id', 'protocol': 61,
                    'sourceQualifiedPhysicalClient': True}, 'sourceConstants': {'protocol': 61}}
            proof = root / 'proof.json'; proof.write_text(json.dumps(metadata))
            out = root / 'out'
            def unclosed(*args):
                receipt = args[4]
                receipt['error'] = 'primary disk guard'
                receipt['runs'] = [{'guardFailure': 'primary disk guard', 'cleanupError': 'denied',
                    'ownedProcessGroupClosed': False}]
                campaign = out / 'campaign'; campaign.mkdir()
                (campaign / 'server.json').write_text('{}')
                return receipt['runs'][0]
            with patch.object(MODULE, 'source_identity', return_value=identity), \
                    patch.object(MODULE, 'frozen_hashes', return_value=hashes), \
                    patch.object(MODULE, 'source_protocol', return_value=61), \
                    patch.object(MODULE, 'sdk_identity', return_value={}), \
                    patch.object(MODULE, 'inventory', return_value={}), \
                    patch.object(MODULE.shutil, 'disk_usage', return_value=SimpleNamespace(free=MODULE.DISK_FLOOR)), \
                    patch.object(MODULE.socket, 'socket'), \
                    patch.object(MODULE, 'watched', side_effect=unclosed) as watched:
                result = MODULE.main(['--source-worktree', str(source), '--source', 'abc', '--build', str(build),
                    '--build-receipt', str(proof), '--qualification', str(proof), '--paths', str(paths),
                    '--dotnet', str(sdk), '--out', str(out), '--case', 'smoke2', '--execute'])
            self.assertEqual(result, 1)
            self.assertEqual(watched.call_count, 1)
            self.assertEqual(watched.call_args.args[6], 'campaign')
            receipt = json.loads((out / 'receipt.json').read_text())
            self.assertEqual(receipt['error'], 'primary disk guard')
            self.assertIn('dependent audit blocked', receipt['secondaryError'])
            self.assertEqual(receipt['runs'][0]['cleanupError'], 'denied')

    def test_cleanup_missing_group_never_signals(self):
        process = Mock(pid=12345)
        with patch.object(MODULE, 'members', return_value=[]), patch.object(MODULE.os, 'killpg', create=True) as kill:
            self.assertEqual(MODULE.stop_owned(process), [])
        kill.assert_not_called()

    @patch.object(MODULE.signal, "SIGKILL", 9, create=True)
    def test_cleanup_permission_race_rechecks_without_escalation(self):
        process = Mock(pid=12345); process.poll.return_value = 0
        for error in (PermissionError('denied'), ProcessLookupError('gone')):
            with patch.object(MODULE, 'members', side_effect=[['owned'], [], []]), \
                    patch.object(MODULE.os, 'killpg', create=True, side_effect=error) as kill:
                self.assertEqual(MODULE.stop_owned(process), [])
                kill.assert_called_once_with(12345, MODULE.signal.SIGTERM)

    @patch.object(MODULE.signal, "SIGKILL", 9, create=True)
    def test_cleanup_denied_live_group_preserves_guard_and_actual_members(self):
        process = Mock(pid=12345); process.poll.return_value = None
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); receipt = {}
            with patch.object(MODULE.subprocess, 'Popen', return_value=process), \
                    patch.object(MODULE, 'members', return_value=['12345 12345 owned']), \
                    patch.object(MODULE.shutil, 'disk_usage', return_value=SimpleNamespace(free=0)), \
                    patch.object(MODULE.os, 'killpg', create=True, side_effect=PermissionError('denied')) as kill:
                row = MODULE.watched(['unused'], root, {}, root / 'log', receipt, root / 'receipt.json', 'test', 10)
            kill.assert_called_once_with(12345, MODULE.signal.SIGTERM)
            self.assertIn('partial data preserved', row['guardFailure'])
            self.assertEqual(receipt['error'], row['guardFailure'])
            self.assertEqual(row['cleanupError'], 'denied')
            self.assertEqual(row['remainingOwnedGroupMembers'], ['12345 12345 owned'])
            self.assertFalse(row['ownedProcessGroupClosed'])
            self.assertIn('cleanupError', (root / 'receipt.json').read_text())

    def recipe(self, name):
        args = SimpleNamespace(case=name, port=None, source_worktree=Path('/qualified/source'),
            source='a' * 40, build=Path('/frozen/runtime'), paths=Path('/isolated/paths.txt'),
            dotnet=Path('/caller-provided/sdk'), protocol=61)
        return MODULE.command(args, Path('/new/campaign'))

    def test_six_bounded_distinct_recipes(self):
        ports = []
        for name, case in MODULE.CASES.items():
            command = self.recipe(name)
            self.assertEqual(command[command.index('--players') + 1], str(case.players))
            self.assertEqual(command[command.index('--seconds') + 1], str(case.seconds))
            ports.append(command[command.index('--port') + 1])
            self.assertEqual(command[command.index('--dotnet') + 1], str(Path('/caller-provided/sdk')))
            self.assertEqual(command[command.index('--protocol') + 1], '61')
            for flag, value in (('--ai-net-client', '2'), ('--ai-seed', '1729'), ('--drain-seconds', '35'),
                ('--timed-grant-proof', 'shadow'), ('--shot-proof', 'shadow'), ('--evidence-row-limit', '1048576'),
                ('--rooms', 'MP12 SIC TRANSIT'), ('--rtt-ms', str(case.rtt)), ('--jitter-ms', str(case.jitter)),
                ('--loss-percent', str(case.loss)), ('--reorder-percent', str(case.reorder)),
                ('--duplicate-percent', str(case.duplicate)), ('--profile', case.profile), ('--seed', '8128')):
                self.assertEqual(command[command.index(flag) + 1], value)
            for flag in ('--powerups', '--timed-replay-facts', '--record'):
                self.assertIn(flag, command)
            for flag in ('--soak', '--server-bot', '--scenario', '--pacing-lobby-client'):
                self.assertNotIn(flag, command)
        self.assertEqual(len(set(ports)), 6)

    def test_explicit_matrix_has_no_solo_or_simulated_stall(self):
        expected = {'smoke-lan2': (2, 0, 0, 0, 0, 0), 'smoke-moderate4': (4, 100, 20, 1, 3, 1),
                    'smoke-worst8': (8, 400, 80, 5, 5, 2)}
        for name, values in expected.items():
            case = MODULE.CASES[name]
            self.assertEqual((case.players, case.rtt, case.jitter, case.loss, case.reorder, case.duplicate), values)
        self.assertTrue(all(case.players in (2, 4, 8) for case in MODULE.CASES.values()))
        self.assertNotIn('--pump-stall', self.recipe('smoke-worst8'))

    def test_physical_explicit_sdk_hash_version_without_default(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'caller-sdk'; path.write_bytes(b'unit metadata file, not an executed SDK')
            with patch.object(MODULE.subprocess, 'check_output', return_value='10.0.100\n') as query:
                identity = MODULE.sdk_identity(path)
            self.assertEqual(identity['sha256'], MODULE.digest(path))
            self.assertEqual(identity['version'], '10.0.100')
            query.assert_called_once_with([str(path), '--version'], text=True, cwd=None, timeout=20)
            with patch.object(MODULE.subprocess, 'check_output', return_value='unknown'):
                with self.assertRaisesRegex(ValueError, 'valid version'):
                    MODULE.sdk_identity(path)
            link = Path(directory) / 'link-sdk'
            try:
                link.symlink_to(path)
            except OSError:
                return  # Windows host may not grant symlink creation.
            with self.assertRaisesRegex(ValueError, 'physical'):
                MODULE.sdk_identity(link)

    def test_protocol_read_from_source_single_valid_declaration(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); file = root / 'src/MphRead/Mods/Network/NetProtocol.cs'
            file.parent.mkdir(parents=True)
            file.write_text('public const int ProtocolVersion = 61;')
            self.assertEqual(MODULE.source_protocol(root), 61)
            for text in ('public const int ProtocolVersion = 256;', 'no declaration',
                         'public const int ProtocolVersion = 61; public const int ProtocolVersion = 62;'):
                file.write_text(text)
                with self.assertRaisesRegex(ValueError, 'one valid protocol'):
                    MODULE.source_protocol(root)

    def test_disk_guard_is_exact_and_preserves_failure(self):
        self.assertIsNone(MODULE.guard_failure(1, 2, MODULE.DISK_FLOOR))
        self.assertIn('partial data preserved', MODULE.guard_failure(1, 2, MODULE.DISK_FLOOR - 1))
        self.assertIn('wall-clock', MODULE.guard_failure(2, 2, MODULE.DISK_FLOOR + 1))

    def test_transport_pass_cannot_mask_nonpositive_join(self):
        self.assertEqual(MODULE.disposition({'status': 'PASS'}, {'status': 'INCOMPLETE'}), 'INCOMPLETE')
        self.assertEqual(MODULE.disposition({'status': 'PASS'}, {'status': 'FAIL'}), 'FAIL')
        self.assertEqual(MODULE.disposition({'status': 'FAIL'}, {'status': 'PASS'}), 'FAIL')
        self.assertEqual(MODULE.disposition({'status': 'PASS'}, {'status': 'PASS'}, guarded=True), 'FAIL')

    def test_inventory_reads_header_and_hashes_partial_without_decoding(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'actual-name.ppdemo.partial'
            path.write_bytes(b'PPDM\x06\x38' + b'uninterpreted compressed data')
            rows = MODULE.recordings(Path(directory))
            self.assertEqual(len(rows), 1)
            self.assertFalse(rows[0]['finalName'])
            self.assertEqual((rows[0]['format'], rows[0]['protocol']), (6, 56))
            self.assertEqual(rows[0]['sha256'], MODULE.digest(path))

    def test_both_scopes_need_actual_pass_for_bounded_join_label(self):
        self.assertEqual(MODULE.disposition({'status': 'PASS'}, {'status': 'PASS'}), 'PASS_BOUNDED_OBSERVED_JOINS')
        self.assertEqual(MODULE.disposition({}, {'status': 'PASS'}), 'INCOMPLETE')


if __name__ == '__main__':
    unittest.main()
