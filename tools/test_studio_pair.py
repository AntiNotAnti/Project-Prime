import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import struct
import tempfile
import unittest
from unittest import mock


def module(name):
    spec = importlib.util.spec_from_file_location(name.replace('-', '_'), Path(__file__).with_name(name + '.py'))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


pair = module('studio-pair')
check = module('paired-install-check')


class PairedInstallTests(unittest.TestCase):
    def manifest(self, root):
        subprocess.run([sys.executable, str(Path(__file__).with_name('write-release-manifest.py')), str(root)], check=True, capture_output=True)

    def test_platform_layouts_have_exact_owned_binary_identities(self):
        for game, studio in [('ProjectPrime.exe', 'ProjectPrimeStudio.exe'), ('ProjectPrime', 'ProjectPrimeStudio'), ('Project Prime.app/Contents/MacOS/ProjectPrime', 'Project Prime Studio.app/Contents/MacOS/ProjectPrimeStudio')]:
            with self.subTest(game=game), tempfile.TemporaryDirectory() as scratch:
                root = Path(scratch)
                for name in (game, studio):
                    (root / name).parent.mkdir(parents=True, exist_ok=True)
                    (root / name).write_bytes(name.encode())
                pair.metadata(root, '1.2.3')
                self.manifest(root)
                self.assertEqual('1.2.3', check.verify(root, '1.2.3')['StudioVersion'])
                (root / studio).write_bytes(b'tampered Studio')
                with self.assertRaisesRegex(ValueError, 'digest mismatch'):
                    check.verify(root)

    def test_merge_conflict_is_rejected_before_copy(self):
        with tempfile.TemporaryDirectory() as scratch:
            game, studio = Path(scratch) / 'game', Path(scratch) / 'studio'
            game.mkdir(); studio.mkdir()
            (game / 'ProjectPrime').write_bytes(b'game')
            (studio / 'ProjectPrimeStudio').write_bytes(b'studio')
            (game / 'shared.dll').write_bytes(b'old engine')
            (studio / 'shared.dll').write_bytes(b'new engine')
            with self.assertRaisesRegex(ValueError, 'shared publish file differs'):
                pair.merge(game, studio, '1.2.3')
            self.assertFalse((game / 'ProjectPrimeStudio').exists())
            self.assertFalse((game / pair.METADATA).exists())

    def test_pair_cannot_lie_about_version_or_binary_hash(self):
        with tempfile.TemporaryDirectory() as scratch:
            root = Path(scratch)
            (root / 'ProjectPrime').write_bytes(b'game')
            (root / 'ProjectPrimeStudio').write_bytes(b'studio')
            data = pair.metadata(root, '1.2.3')
            data['StudioVersion'] = '1.2.4'
            (root / pair.METADATA).write_text(json.dumps(data))
            self.manifest(root)
            with self.assertRaisesRegex(ValueError, 'versions'):
                check.verify(root)
            data['StudioVersion'] = '1.2.3'; data['StudioSha256'] = '0' * 64
            (root / pair.METADATA).write_text(json.dumps(data))
            self.manifest(root)
            with self.assertRaisesRegex(ValueError, 'binary identity'):
                check.verify(root)

    def test_runtime_lease_cannot_ship(self):
        with tempfile.TemporaryDirectory() as scratch:
            root = Path(scratch)
            (root / '.project-prime-in-use.lock').touch()
            with self.assertRaises(subprocess.CalledProcessError):
                self.manifest(root)

    def test_native_apphost_architecture_must_match_release_target(self):
        elf = lambda machine: b'\x7fELF\x02\x01' + bytes(12) + struct.pack('<H', machine) + bytes(44)
        pe = b'MZ' + bytes(58) + struct.pack('<I', 64) + b'PE\0\0\x64\x86'
        macho = lambda cpu: b'\xcf\xfa\xed\xfe' + struct.pack('<I', cpu) + bytes(24)
        for rid, binary, suffix in [('linux-x64', elf(62), ''), ('linux-arm64', elf(183), ''),
                                    ('win-x64', pe, '.exe'), ('osx-x64', macho(0x1000007), ''),
                                    ('osx-arm64', macho(0x100000c), '')]:
            with self.subTest(rid=rid), tempfile.TemporaryDirectory() as scratch:
                root = Path(scratch)
                game = 'Project Prime.app/Contents/MacOS/ProjectPrime' if rid.startswith('osx-') else 'ProjectPrime' + suffix
                studio = 'Project Prime Studio.app/Contents/MacOS/ProjectPrimeStudio' if rid.startswith('osx-') else 'ProjectPrimeStudio' + suffix
                (root / game).parent.mkdir(parents=True, exist_ok=True)
                (root / studio).parent.mkdir(parents=True, exist_ok=True)
                (root / game).write_bytes(binary)
                (root / studio).write_bytes(binary)
                pair.metadata(root, '1.2.3'); self.manifest(root)
                check.verify(root, '1.2.3', rid)
                (root / studio).write_bytes(b'wrong binary')
                pair.metadata(root, '1.2.3'); self.manifest(root)
                with self.assertRaisesRegex(ValueError, 'architecture mismatch: Studio'):
                    check.verify(root, '1.2.3', rid)

    def test_mixed_platform_binaries_cannot_form_a_pair(self):
        with tempfile.TemporaryDirectory() as scratch:
            root = Path(scratch)
            (root / 'ProjectPrime').write_bytes(b'game')
            (root / 'ProjectPrimeStudio.exe').write_bytes(b'studio')
            pair.metadata(root, '1.2.3'); self.manifest(root)
            with self.assertRaisesRegex(ValueError, 'matching sibling'):
                check.verify(root)

    def test_universal_macos_apphost_has_both_supported_slices(self):
        with tempfile.TemporaryDirectory() as scratch:
            binary = Path(scratch) / 'universal'
            binary.write_bytes(b'\xca\xfe\xba\xbe' + struct.pack('>I', 2)
                + struct.pack('>IIIII', 0x1000007, 0, 0, 0, 0)
                + struct.pack('>IIIII', 0x100000c, 0, 0, 0, 0))
            self.assertEqual({'osx-x64', 'osx-arm64'}, check.binary_rids(binary))
            binary.write_bytes(b'\xca\xfe\xba\xbe' + struct.pack('>I', 2))
            with self.assertRaisesRegex(ValueError, 'truncated'):
                check.binary_rids(binary)

    def test_merge_directory_obstruction_is_rejected_before_any_copy(self):
        with tempfile.TemporaryDirectory() as scratch:
            game, studio = Path(scratch) / 'game', Path(scratch) / 'studio'
            game.mkdir(); studio.mkdir()
            (game / 'ProjectPrime').write_bytes(b'game')
            (game / 'z-native').write_bytes(b'file')
            (studio / 'ProjectPrimeStudio').write_bytes(b'studio')
            (studio / 'z-native').mkdir()
            (studio / 'z-native/lib.so').write_bytes(b'native')
            with self.assertRaisesRegex(ValueError, 'directory is obstructed'):
                pair.merge(game, studio, '1.2.3')
            self.assertFalse((game / 'ProjectPrimeStudio').exists())

    def test_executed_version_smoke_rejects_wrong_app_or_protocol(self):
        version = '1.2.3'
        game = {'gameVersion': version, 'studioIpcVersion': pair.protocol_version()}
        studio = dict(game, studioVersion=version + '+source-revision')
        def responses(data):
            return [subprocess.CompletedProcess([], 0, json.dumps(game), ''),
                    subprocess.CompletedProcess([], 0, json.dumps(data), '')]
        with mock.patch.object(pair.subprocess, 'run', side_effect=responses(studio)):
            pair.smoke('game', 'studio', version)
        for wrong in (dict(studio, studioVersion='9.9.9'), dict(studio, gameVersion='9.9.9'),
                      dict(studio, studioIpcVersion=999), game):
            with self.subTest(wrong=wrong), mock.patch.object(pair.subprocess, 'run', side_effect=responses(wrong)):
                with self.assertRaisesRegex(ValueError, 'version mismatch'):
                    pair.smoke('game', 'studio', version)


if __name__ == '__main__':
    unittest.main()
