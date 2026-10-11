"""Content-free regressions for validation of the delivered server tarball."""
import io
import importlib.util
import pathlib
import struct
import subprocess
import sys
import tarfile
import tempfile
import unittest

TOOLS = pathlib.Path(__file__).parent
spec = importlib.util.spec_from_file_location('archive_check', TOOLS / 'check-server-archive.py')
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)


class ServerArchiveTests(unittest.TestCase):
    def fixture(self, root, machine=62, mode=0o755):
        binary = root / 'ProjectPrime'
        binary.write_bytes(b'\x7fELF\x02\x01' + bytes(12) + struct.pack('<H', machine))
        binary.chmod(mode)
        for name in ('LICENSE', 'SERVER.md', 'SERVER.txt'):
            (root / name).write_text('fixture')
        (root / 'licenses').mkdir()
        (root / 'licenses' / 'NOTICE.txt').write_text('third-party notice fixture')
        subprocess.run([sys.executable, str(TOOLS / 'write-release-manifest.py'), str(root)],
                       check=True, capture_output=True)

    def check(self, mutate=None, machine=62, mode=0o755):
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp) / 'package'
            root.mkdir()
            self.fixture(root, machine, mode)
            if mutate:
                mutate(root)
            archive = pathlib.Path(temp) / 'release.tar.gz'
            with tarfile.open(archive, 'w:gz') as tar:
                tar.add(root, arcname='.')
            checker.validate(archive, 'x64')

    def test_valid_archive(self):
        self.check()

    def test_wrong_architecture(self):
        with self.assertRaisesRegex(ValueError, 'ELF'):
            self.check(machine=183)

    def test_lost_executable_permissions(self):
        with self.assertRaisesRegex(ValueError, 'permissions'):
            self.check(mode=0o644)

    def test_missing_license(self):
        with self.assertRaisesRegex(ValueError, 'LICENSE'):
            self.check(lambda root: (root / 'LICENSE').unlink())

    def test_tampered_file(self):
        with self.assertRaisesRegex(ValueError, 'checksum'):
            self.check(lambda root: (root / 'SERVER.md').write_text('tampered'))

    def test_unowned_file(self):
        with self.assertRaisesRegex(ValueError, 'exactly cover'):
            self.check(lambda root: (root / 'unexpected').write_text('extra'))

    def test_path_traversal(self):
        with tempfile.TemporaryDirectory() as temp:
            archive = pathlib.Path(temp) / 'release.tar.gz'
            with tarfile.open(archive, 'w:gz') as tar:
                member = tarfile.TarInfo('../escape')
                member.size = 1
                tar.addfile(member, io.BytesIO(b'x'))
            with self.assertRaisesRegex(ValueError, 'unsafe'):
                checker.validate(archive, 'x64')
            self.assertFalse((pathlib.Path(temp) / 'escape').exists())

    def test_duplicate_entry(self):
        with tempfile.TemporaryDirectory() as temp:
            archive = pathlib.Path(temp) / 'release.tar.gz'
            with tarfile.open(archive, 'w:gz') as tar:
                for name in ('ProjectPrime', './ProjectPrime'):
                    member = tarfile.TarInfo(name)
                    member.size = 1
                    tar.addfile(member, io.BytesIO(b'x'))
            with self.assertRaisesRegex(ValueError, 'duplicate'):
                checker.validate(archive, 'x64')

    def test_symlink(self):
        with self.assertRaisesRegex(ValueError, 'unsafe'):
            self.check(lambda root: (root / 'link').symlink_to('LICENSE'))


if __name__ == '__main__':
    unittest.main()
