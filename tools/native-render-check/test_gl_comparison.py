"""Corruption/identity/contamination controls for the portable GL receipt verifier."""
import copy
import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('gl_comparison', Path(__file__).with_name('gl_comparison.py'))
gl = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gl)


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name) / 'bundle'
        source = Path(__file__).resolve().parent / 'fixtures/gl-evidence'
        shutil.copytree(source, self.root)
        self.bundle = json.loads((self.root / 'manifest.json').read_text())

    def tearDown(self):
        self.temp.cleanup()

    def rehash(self, record, kind):
        digest = gl.sha256(self.root / record[kind])
        record[kind + 'Sha256'] = digest
        for entry in self.bundle['files']:
            if entry['file'] == record[kind]:
                entry['sha256'] = digest
                if 'derivativeBytes' in entry:
                    from sanitize_gl_evidence import projection
                    raw = (self.root / record[kind]).read_bytes()
                    entry['derivativeBytes'] = len(raw)
                    entry['numericProjection'] = projection(raw, Path(record[kind]).suffix)

    def mutate_result(self, edit):
        record = next(row for row in self.bundle['records'] if row['category'] == 'unprofiled' and row.get('status') != 'EXCLUDED')
        data = json.loads((self.root / record['result']).read_text())
        edit(data)
        gl.write(self.root / record['result'], data)
        self.rehash(record, 'result')

    def test_historical_negative_recomputes_from_raw_samples(self):
        report = gl.validate(self.bundle, self.root)
        self.assertEqual(report['acceptedSamplesPerArm'], {'0': 5, '1': 5})
        self.assertEqual(report['performanceDisposition'], 'REJECTED')
        self.assertAlmostEqual(report['candidatePercentChange']['frameIntervalP99Ms'], 11.24982122060354)

    def test_changed_log_fails_digest(self):
        path = self.root / self.bundle['records'][1]['log']
        path.write_text(path.read_text() + 'changed\n')
        with self.assertRaisesRegex(ValueError, 'changed derivative|changed evidence'):
            gl.validate(self.bundle, self.root)

    def test_declared_file_hash_does_not_replace_loaded_mvid(self):
        self.mutate_result(lambda data: data.update(assemblyMvid='wrong-loaded-assembly'))
        with self.assertRaisesRegex(ValueError, 'MVID'):
            gl.validate(self.bundle, self.root)

    def test_profiled_performance_fails_even_with_new_hashes(self):
        record = self.bundle['records'][1]
        path = self.root / record['log']
        path.write_text(path.read_text() + '[glpasses-json] {}\n')
        self.rehash(record, 'log')
        with self.assertRaisesRegex(ValueError, 'contaminates'):
            gl.validate(self.bundle, self.root)

    def test_exclusions_require_reason_and_do_not_hide_tail_samples(self):
        self.bundle['records'][0]['reason'] = ''
        with self.assertRaisesRegex(ValueError, 'reason'):
            gl.validate(self.bundle, self.root)

    def test_retained_owned_gl_name_fails(self):
        self.mutate_result(lambda data: data['cleanup']['detailedRetirement'].update(buffersRecognizedAfter=1))
        with self.assertRaisesRegex(ValueError, 'retirement'):
            gl.validate(self.bundle, self.root)

    def test_wrong_fixture_fails(self):
        self.mutate_result(lambda data: data.update(renderSize=[1280, 720]))
        with self.assertRaisesRegex(ValueError, 'fixture differs'):
            gl.validate(self.bundle, self.root)

    def test_duplicate_run_fails(self):
        self.bundle['records'].append(copy.deepcopy(self.bundle['records'][1]))
        with self.assertRaisesRegex(ValueError, 'duplicate'):
            gl.validate(self.bundle, self.root)

    def test_insufficient_accepted_samples_fails(self):
        self.bundle['records'][1].update(status='EXCLUDED', reason='preserved environmental interference')
        with self.assertRaisesRegex(ValueError, 'five accepted'):
            gl.validate(self.bundle, self.root)

    def test_symlinked_harness_is_rejected_before_resolution(self):
        binary = self.root / 'native-render-check.dll'
        try:
            binary.symlink_to(self.root / self.bundle['records'][1]['result'])
        except OSError as error:
            if getattr(error, 'winerror', None) == 1314:
                self.skipTest('Windows symbolic-link privilege unavailable')
            raise
        with self.assertRaisesRegex(ValueError, 'physical'):
            gl.physical_binary(binary)

    def test_schedule_retains_five_arms_and_reverse_capture_control(self):
        order = [row[1] for row in gl.schedule('unprofiled')]
        self.assertEqual(order.count(0), 5)
        self.assertEqual(order.count(1), 5)
        self.assertEqual([row[1] for row in gl.schedule('parity')], [0, 1, 1, 0, 0, 1])


if __name__ == '__main__':
    unittest.main()
