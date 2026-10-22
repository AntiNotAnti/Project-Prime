import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('consumer', Path(__file__).with_name('run.py'))
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
CLIENT, ENTRY, FIXTURE = 'a' * 64, 'b' * 64, 'c' * 64
MVID, FMVID = '11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222'


def raw(frames=72001, bombs=0, result='Success', active='False', authority='False'):
    return (f'IDENTITY ClientPath=/thin/ProjectPrime.dll SHA256={CLIENT} MVID={MVID}\n'
            f'IDENTITY EntrySHA256={ENTRY} MVID={FMVID}\n'
            'SESSION_BEFORE active=False authority=False\n'
            f'SCENE_FINAL frames={frames} sceneEvents=8 certs=0 scopes=0 current={frames-1} result={result} error= warning= pendingBombs={bombs} activeNetSession={active} authority={authority}\n')


class ConsumerContractTests(unittest.TestCase):
    def test_long_original_scene_eof_can_have_no_archival_grant_facts(self):
        actual = m.parse_scene(raw(), CLIENT, MVID, ENTRY, 120000)
        self.assertEqual(actual['frames'], 72001)
        self.assertEqual(actual['certificateFactsObserved'], 0)
        self.assertIn('Natural scene.Step false', actual['eof'])

    def test_success_at_fixed_hard_cap_is_not_eof(self):
        with self.assertRaisesRegex(ValueError, 'not EOF'):
            m.parse_scene(raw(frames=1000000), CLIENT, MVID, ENTRY, 999999)

    def test_configured_bound_is_enforced(self):
        with self.assertRaises(ValueError):
            m.parse_scene(raw(frames=72001), CLIENT, MVID, ENTRY, 72000)

    def test_bare_decoder_final_is_not_scene_eof(self):
        text = raw().replace('SCENE_FINAL', 'FINAL')
        with self.assertRaisesRegex(ValueError, 'Missing'):
            m.parse_scene(text, CLIENT, MVID, ENTRY, 120000)

    def test_no_positive_claim_for_bomb_backlog_or_live_authority(self):
        for text in (raw(bombs=2048), raw(active='True'), raw(authority='True'), raw(result='Truncated')):
            with self.subTest(text=text), self.assertRaises(ValueError):
                m.parse_scene(text, CLIENT, MVID, ENTRY, 120000)

    def test_step_exception_after_success_marker_still_fails(self):
        with self.assertRaisesRegex(ValueError, 'exception'):
            m.parse_scene(raw() + 'SCENE_EXCEPTION frames=7\n', CLIENT, MVID, ENTRY, 120000)

    def test_missing_duplicate_identity_is_setup_failure(self):
        for text in (raw().replace(CLIENT, FIXTURE), raw() + raw().splitlines()[0] + '\n'):
            with self.assertRaises(m.SourceSetupError):
                m.parse_scene(text, CLIENT, MVID, ENTRY, 120000)

    def test_missing_duplicate_final_and_zero_steps_fail(self):
        for text in (raw().split('SCENE_FINAL')[0], raw()+raw().splitlines()[-1]+'\n', raw(frames=0)):
            with self.assertRaises(ValueError):
                m.parse_scene(text, CLIENT, MVID, ENTRY, 120000)

    def test_qualified_fixture_is_not_inferred_from_engine_hash(self):
        physical = {'ProjectPrime.dll': CLIENT, 'nettest.dll': FIXTURE}
        src = {'commit': 'd'*40, 'tree': 'e'*40, 'dirty': ''}
        q = dict(status='PASS_AVAILABLE_SOURCE_NATIVE_QUALIFICATION', sourceBefore=src, sourceAfter=src,
                 allOwnedGroupsClosed=True, physicalHashesAfter=physical, nettestMvid=FMVID,
                 actualLoadedClient=dict(sha256=CLIENT, mvid=MVID, sourceQualifiedPhysicalClient=True),
                 actualLoadedNettest=dict(sha256=FIXTURE, mvid=FMVID, sourceQualifiedPhysicalFixture=True))
        self.assertEqual(m.validate_qualification(q, src['commit'], physical), src)
        for mutate in ('fixtureSha', 'fixtureMvid', 'dirty', 'closure'):
            import copy
            bad = copy.deepcopy(q)
            if mutate == 'fixtureSha': bad['actualLoadedNettest']['sha256'] = ENTRY
            if mutate == 'fixtureMvid': bad['nettestMvid'] = MVID
            if mutate == 'dirty': bad['sourceAfter']['dirty'] = ' M file'
            if mutate == 'closure': bad['allOwnedGroupsClosed'] = False
            with self.subTest(mutate=mutate), self.assertRaises(ValueError):
                m.validate_qualification(bad, src['commit'], physical)

    def test_paths_sanitized_only_and_export_is_owned(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            p = root/'paths.txt'
            p.write_text(f'0.35.1.0\nAMHE1={root}\nExport=/old/export\n')
            text, assets = m.paths_profile(p, root/'new-profile')
            self.assertIn(f'Export={root}/new-profile/exports', text)
            self.assertEqual(assets['AMHE1'], str(root))
            p.write_text(f'0.35.1.0\nAMHE1={root}\nPassword=secret\n')
            with self.assertRaises(ValueError): m.paths_profile(p, root/'new-profile')

    def test_current_and_legacy_headers_are_observed_not_decoded(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp)/'record.ppdemo'
            for magic, version in ((b'PPDM', 4), (b'PPDM', 6), (b'FPDM', 1)):
                p.write_bytes(magic+bytes([version, 56]))
                self.assertEqual(m.replay_header(p)['format'], version)
            p.write_bytes(b'PPDM'+bytes([7, 56]))
            with self.assertRaises(ValueError): m.replay_header(p)

    def test_stream_hash_detects_changed_tail(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp)/'large'
            p.write_bytes(b'a'*(1024*1024+1))
            before = m.sha(p)
            with p.open('r+b') as f:
                f.seek(-1, 2); f.write(b'b')
            self.assertNotEqual(before, m.sha(p))


if __name__ == '__main__': unittest.main()
