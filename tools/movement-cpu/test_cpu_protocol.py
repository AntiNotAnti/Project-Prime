import json
from pathlib import Path
import tempfile
import unittest
import run_cpu_protocol as runner


class ProtocolTests(unittest.TestCase):
    def test_fixed_balanced_schedule(self):
        arms = runner.schedule()
        self.assertEqual(len(arms), 32)
        for room in runner.ROOMS:
            selected = [a for a in arms if a['room'] == room]
            for treatment in ('trace', 'control'):
                self.assertEqual([a['model'] for a in selected if a['treatment'] == treatment], list(runner.MODELS) * 2)
            for a, b in zip(selected[::2], selected[1::2]):
                self.assertEqual(a['model'], b['model'])
                self.assertNotEqual(a['treatment'], b['treatment'])

    def test_existing_recording_rejected(self):
        with self.assertRaises(RuntimeError):
            runner.require_idle_wpr('WPR recording is in progress')
        with self.assertRaises(RuntimeError):
            runner.require_idle_wpr('unexpected localized or unknown output')
        runner.require_idle_wpr('Microsoft WPR\nWPR is not recording\n')

    def summary(self, perturb=False, cpu_fail=False, workload_fail=False):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            records = []
            for index, arm in enumerate(runner.schedule()):
                directory = root / str(index)
                directory.mkdir()
                model_factor = 1.02 if arm['model'] == 'Momentum' else 1
                observer_factor = 1.2 if perturb and arm['treatment'] == 'trace' else 1
                row = dict(timing=dict(p95=model_factor * observer_factor, processCpuMilliseconds=100),
                           workload=dict(value=arm['model'], bad=index if workload_fail else 0))
                (directory / 'benchmark.json').write_text(json.dumps(row))
                factor = 1.1 if cpu_fail and arm['model'] == 'Momentum' else model_factor
                sample = dict(cpuMs=factor, cpuLowerMs=factor-.0001, cpuUpperMs=factor+.0001)
                (directory / 'cpu.json').write_text(json.dumps(dict(rows=[sample], p99CpuMs=factor)))
                records.append(dict(arm, directory=str(index)))
            runner.summarize(root, records)
            return json.loads((root / 'summary.json').read_text())

    def test_cpu_within_threshold_is_not_feature_acceptance(self):
        result = self.summary()
        self.assertFalse(result['acceptanceClaim'])
        self.assertTrue(all(m['status'] == 'within5PercentCPU-provisional' for m in result['maps']))

    def test_observer_control_failure_is_inconclusive(self):
        self.assertTrue(all(m['status'] == 'measurement-invalid/inconclusive' for m in self.summary(perturb=True)['maps']))

    def test_cpu_failure_stays_failure(self):
        self.assertTrue(all(m['status'] == 'FAIL-stop-and-investigate' for m in self.summary(cpu_fail=True)['maps']))

    def test_workload_mismatch_invalidates_measurement(self):
        self.assertTrue(all(m['status'] == 'measurement-invalid/inconclusive' for m in self.summary(workload_fail=True)['maps']))


if __name__ == '__main__':
    unittest.main()

class DeferredAnalysisTests(unittest.TestCase):
    def test_missing_all_audits_cannot_claim_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runner.summarize(root, [])
            result = json.loads((root / 'summary.json').read_text())
            self.assertFalse(result['acceptanceClaim'])
            self.assertEqual(result['status'], 'measurement-incomplete/inconclusive')

    def test_one_missing_audit_invalidates_whole_batch(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            records = []
            for index, arm in enumerate(runner.schedule()):
                target = root / str(index)
                target.mkdir()
                (target / 'benchmark.json').write_text('{}')
                if arm['treatment'] == 'trace' and index != 1:
                    (target / 'cpu.json').write_text('{}')
                records.append(dict(arm, directory=str(index)))
            runner.summarize(root, records)
            self.assertEqual(json.loads((root / 'summary.json').read_text())['status'], 'measurement-incomplete/inconclusive')

    def test_lossless_archive_receipt_and_crc(self):
        import analyze_cpu_protocol as analysis
        import gzip
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'raw.xml'
            data = b'example numeric fixture' * 500
            path.write_bytes(data)
            receipt = analysis.archive_export(path)
            self.assertFalse(path.exists())
            self.assertEqual(gzip.decompress(path.with_suffix('.xml.gz').read_bytes()), data)
            self.assertEqual(receipt['originalSha256'], receipt['verifiedSha256'])
            self.assertTrue(receipt['crcVerified'])

    def test_existing_archive_does_not_remove_original(self):
        import analyze_cpu_protocol as analysis
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'raw.xml'
            path.write_bytes(b'original')
            path.with_suffix('.xml.gz').write_bytes(b'existing')
            with self.assertRaises(RuntimeError):
                analysis.archive_export(path)
            self.assertTrue(path.exists())
