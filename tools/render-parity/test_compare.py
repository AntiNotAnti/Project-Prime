"""Content-free regression checks: python -m unittest discover -s tools/render-parity."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from PIL import Image


class CompareTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.ref = self.root / 'ref'
        self.candidate = self.root / 'candidate'
        self.ref.mkdir()
        self.candidate.mkdir()
        Image.new('RGB', (2, 2), 'black').save(self.ref / 'case.png')
        Image.new('RGB', (2, 2), 'black').save(self.candidate / 'case.png')

    def run_compare(self, *args):
        result = subprocess.run([sys.executable, str(Path(__file__).with_name('compare.py')),
            str(self.ref), str(self.candidate), '--output', str(self.root / 'result.json'), *args],
            capture_output=True, text=True)
        report = self.root / 'result.json'
        return result.returncode, json.loads(report.read_text()) if report.exists() else None

    def test_identical(self):
        code, result = self.run_compare()
        self.assertEqual(code, 0)
        self.assertEqual(result['images'][0]['rmse'], 0)
        self.assertTrue(Path(result['images'][0]['differenceImage']).is_file())

    def test_single_pixel_metrics_and_case_threshold(self):
        image = Image.new('RGB', (2, 2), 'black')
        image.putpixel((0, 0), (255, 255, 255))
        image.save(self.candidate / 'case.png')
        thresholds = self.root / 'thresholds.json'
        thresholds.write_text(json.dumps({'case.png': {'rmse': 0.49}}))
        code, result = self.run_compare('--mean-limit', '1', '--structural-limit', '1',
            '--changed-limit', '1', '--thresholds', str(thresholds))
        self.assertEqual(code, 1)
        self.assertEqual(result['images'][0]['rmse'], 0.5)
        self.assertEqual(result['images'][0]['maximum'], 1)

    def test_extra_capture_fails(self):
        Image.new('RGB', (2, 2)).save(self.candidate / 'extra.png')
        self.assertEqual(self.run_compare()[0], 1)

    def test_missing_capture_fails(self):
        (self.candidate / 'case.png').unlink()
        self.assertEqual(self.run_compare()[0], 1)

    def test_bad_image_fails_structurally(self):
        (self.candidate / 'case.png').write_text('bad PNG')
        code, result = self.run_compare()
        self.assertEqual(code, 1)
        self.assertIn('error', result['images'][0])

    def test_invalid_limit_fails(self):
        self.assertEqual(self.run_compare('--mean-limit', 'nan')[0], 2)

    def test_size_mismatch_fails(self):
        Image.new('RGB', (3, 3)).save(self.candidate / 'case.png')
        self.assertEqual(self.run_compare()[0], 1)

    def test_empty_capture_set_fails(self):
        (self.ref / 'case.png').unlink()
        (self.candidate / 'case.png').unlink()
        self.assertEqual(self.run_compare()[0], 1)


if __name__ == '__main__':
    unittest.main()
