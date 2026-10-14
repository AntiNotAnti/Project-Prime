"""Falsify immutable evidence checks without engine/native execution."""
import json
import shutil
import tempfile
import unittest
from pathlib import Path
from verify import verify, sha, projection
from chunks import write_chunks, read_chunks, encode, digest, MAX_CHUNK_BYTES, TEXT_CHARACTERS

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


class ChunkTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        self.original = {'rows': [{'ordinal': i, 'value': i / 3} for i in range(11)],
                         'unicode': '\u03c0' * TEXT_CHARACTERS, 'text': '\\"' * TEXT_CHARACTERS}
        self.index = write_chunks(self.original, self.folder)

    def test_exact_deterministic_recomposition_and_bound(self):
        self.assertEqual(read_chunks(self.index, self.folder), self.original)
        self.assertTrue(all(r['bytes'] <= MAX_CHUNK_BYTES for r in self.index['chunks']))
        self.assertEqual(self.index, write_chunks(self.original, self.folder))

    def test_missing_chunk_refused(self):
        (self.folder / self.index['chunks'][0]['path']).unlink()
        with self.assertRaises(FileNotFoundError):
            read_chunks(self.index, self.folder)

    def test_reordered_chunks_refused(self):
        self.index['chunks'][0], self.index['chunks'][1] = self.index['chunks'][1], self.index['chunks'][0]
        with self.assertRaisesRegex(ValueError, 'chunk order/range'):
            read_chunks(self.index, self.folder)

    def test_changed_chunk_refused_even_with_updated_chunk_digest(self):
        record = self.index['chunks'][0]
        path = self.folder / record['path']
        item = json.loads(path.read_text())
        item['text'] = item['text'].replace('ordinal', 'Ordinal', 1)
        raw = encode(item)
        path.write_bytes(raw)
        record['sha256'], record['bytes'] = digest(raw), len(raw)
        with self.assertRaisesRegex(ValueError, 'recomposed projection identity'):
            read_chunks(self.index, self.folder)


if __name__ == '__main__':
    unittest.main()
