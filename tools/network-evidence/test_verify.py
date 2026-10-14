"""Falsify immutable evidence checks without engine/native execution."""
import json
import shutil
import tempfile
import unittest
from pathlib import Path
from verify import verify, sha, projection

SOURCE = Path(__file__).resolve().parents[2] / 'docs/evidence/network-campaigns-2026-10-14/smoke2-7588-attempt1'


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.evidence = Path(self.temp.name) / 'evidence'
        shutil.copytree(SOURCE, self.evidence)
        self.manifest = json.loads((self.evidence / 'manifest.json').read_text())

    def save(self):
        (self.evidence / 'manifest.json').write_text(json.dumps(self.manifest))

    def test_changed_bytes_are_refused(self):
        entry = self.manifest['entries'][0]
        path = self.evidence / entry['derivativePath']
        path.write_bytes(path.read_bytes() + b' ')
        with self.assertRaisesRegex(ValueError, 'derivative identity'):
            verify(self.evidence)

    def test_external_derivative_is_refused(self):
        self.manifest['entries'][0]['derivativePath'] = '../external.json'
        self.save()
        with self.assertRaisesRegex(ValueError, 'external derivative'):
            verify(self.evidence)

    def test_false_success_is_refused_even_with_new_digest(self):
        entry = self.manifest['entries'][0]
        path = self.evidence / entry['derivativePath']
        value = json.loads(path.read_text())
        value['status'] = 'PASS'
        raw = json.dumps(value).encode()
        path.write_bytes(raw)
        entry['derivativeSHA256'], entry['derivativeBytes'] = sha(raw), len(raw)
        entry['numericAndArrayStructureSHA256'] = sha(json.dumps(projection(value), separators=(',', ':')).encode())
        self.save()
        with self.assertRaisesRegex(ValueError, 'strict incomplete outcome'):
            verify(self.evidence)


if __name__ == '__main__':
    unittest.main()
