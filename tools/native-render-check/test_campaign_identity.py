import json
from pathlib import Path
import tempfile
import unittest
from campaign_identity import asset_identity, isolate_user_data


class FixtureIdentityTests(unittest.TestCase):
    def test_relative_assets_are_normalized_without_player_settings(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            template, output = root / 'original', root / 'campaign'
            (template / 'files' / 'data').mkdir(parents=True)
            output.mkdir()
            asset = template / 'files' / 'data' / 'room.bin'
            asset.write_bytes(b'fixture')
            (template / 'paths.txt').write_text('version\nAMHE1=files\nExport=private-export\n')
            (template / 'launcher.txt').write_text('operator preferences')
            data, identity = isolate_user_data(template, output)
            self.assertEqual(list(data.iterdir()), [data / 'paths.txt'])
            self.assertIn(str((template / 'files').resolve()), (data / 'paths.txt').read_text())
            self.assertNotIn('private-export', (data / 'paths.txt').read_text())
            self.assertEqual(identity['fileCount'], 1)
            self.assertEqual(asset_identity(data / 'paths.txt')['sha256'], identity['sha256'])
            asset.write_bytes(b'changed')
            self.assertNotEqual(asset_identity(data / 'paths.txt')['sha256'], identity['sha256'])

    def test_no_extracted_assets_cannot_supply_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'paths.txt'
            path.write_text('version\nAMHE1=\n')
            with self.assertRaises(ValueError):
                asset_identity(path)

    def test_full_relative_path_is_part_of_asset_key(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for key in ['a', 'b']:
                (root / key).mkdir()
                (root / key / 'same.bin').write_bytes(b'same')
            path = root / 'paths.txt'
            path.write_text('version\nA=a\nB=b\n')
            identity = asset_identity(path)
            self.assertEqual([e['key'] for e in identity['files']], ['A/same.bin', 'B/same.bin'])
