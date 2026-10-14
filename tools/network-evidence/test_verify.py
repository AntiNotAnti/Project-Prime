"""Falsify immutable evidence checks without engine/native execution."""
import json
import copy
import shutil
import tempfile
import unittest
from pathlib import Path
from verify import verify, sha, projection
from chunks import write_chunks, read_chunks, encode, digest, MAX_CHUNK_BYTES, TEXT_CHARACTERS
from pack import pack
from compact_projection import (project_role, apply_declared_role_projection,
                                WINDOW_PATHS, TRANSPORT_PATHS)

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


class PackPreflightTests(unittest.TestCase):
    def test_running_campaign_cannot_be_published(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); source = root / 'source'; source.mkdir()
            (source / 'receipt.json').write_text(json.dumps({'status': 'RUNNING'}))
            destination = root / 'evidence'
            with self.assertRaisesRegex(ValueError, 'not finalized'):
                pack(root, source, destination)
            self.assertFalse(destination.exists())

    def test_missing_original_refused_before_any_derivative_written(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); source = root / 'source'; source.mkdir()
            (source / 'receipt.json').write_text(json.dumps({'status': 'INCOMPLETE',
                'sourceUnchanged': True, 'runs': [{'ownedProcessGroupClosed': True}]}))
            (source / 'campaign').mkdir()
            destination = root / 'evidence'
            with self.assertRaisesRegex(ValueError, 'missing declared original'):
                pack(root, source, destination)
            self.assertFalse(destination.exists())


class RoleProjectionTests(unittest.TestCase):
    def setUp(self):
        def metric(paths):
            value = {}
            for i, path in enumerate(paths):
                keys = path.split('.'); parent = value
                for key in keys[:-1]:
                    parent = parent.setdefault(key, {})
                parent[keys[-1]] = [1, 2, 3] if keys[-1] == 'QueueAgeHistogram' else i
            return value
        def shot(slot, life, sid):
            return {'Stage': 'accepted', 'Reason': None, 'Weapon': 1, 'Kind': 0,
                    'Key': dict(zip(['AuthorityEpoch', 'MatchId', 'ShooterSlot', 'Generation', 'LifeId', 'ShotId'],
                                    [1, 2, slot, 1, life, sid]))}
        self.source = {'Windows': [metric(WINDOW_PATHS), metric(WINDOW_PATHS)],
            'TransportWindows': [metric(TRANSPORT_PATHS)],
            'Shots': [shot(0, 1, 50), shot(0, 1, 51), shot(1, 28, 1646), shot(1, 28, 1676), shot(1, 28, 1677)],
            'Facts': [{'Type': 55}, {'Type': 70}]}
        self.keys = [(1, 2, 0, 1, 1, 50)]
        self.chronology = {'life': [1, 2, 1, 1, 28], 'firstShot': 1646, 'lastShot': 1676}
        self.projected = copy.deepcopy(self.source)
        self.spec = project_role(self.projected, self.keys, self.chronology)

    def test_all_positive_and_boundary_chronology_rows_kept_in_original_order(self):
        self.assertEqual(self.spec['fields'][2]['selectedOriginalIndices'], [0, 2, 3])
        self.assertEqual(apply_declared_role_projection(copy.deepcopy(self.source), self.spec), self.projected)
        self.assertEqual(len(self.projected['Windows']['rows']), 2)
        self.assertEqual(self.projected['TransportWindows']['rows'][0][10], [1, 2, 3])

    def test_forged_selected_row_index_refused(self):
        self.spec['fields'][2]['selectedOriginalIndices'] = [0, 3]
        with self.assertRaisesRegex(ValueError, 'exact original'):
            apply_declared_role_projection(copy.deepcopy(self.source), self.spec)

    def test_reordered_metric_columns_refused(self):
        self.spec['fields'][0]['columns'] = list(reversed(self.spec['fields'][0]['columns']))
        with self.assertRaisesRegex(ValueError, 'exact original'):
            apply_declared_role_projection(copy.deepcopy(self.source), self.spec)


if __name__ == '__main__':
    unittest.main()
