#!/usr/bin/env python3
"""Validate and optionally smoke a Linux server's actual release tarball."""
import argparse
import hashlib
import json
import pathlib
import struct
import shutil
import subprocess
import tarfile
import tempfile

MANIFEST = '.project-prime-files.json'


def validate(archive, architecture, smoke=False):
    with tempfile.TemporaryDirectory(prefix='prime-server-archive-') as temp:
        root = pathlib.Path(temp)
        with tarfile.open(archive, 'r:gz') as tar:
            names = set()
            for member in tar.getmembers():
                name = member.name.removeprefix('./')
                if name in ('', '.') and member.isdir():
                    continue
                parts = name.split('/')
                if (member.name.startswith('/') or any(p in ('', '.', '..') for p in parts)
                        or '\\' in name or ':' in name or name in names
                        or not (member.isfile() or member.isdir())):
                    raise ValueError(f'unsafe or duplicate archive entry: {member.name}')
                names.add(name)
            # Extract only the regular files/directories reviewed above; this also
            # supports the Python bundled with older developer macOS installs.
            for member in tar.getmembers():
                name = member.name.removeprefix('./')
                if name in ('', '.'):
                    continue
                destination = root / name
                if member.isdir():
                    destination.mkdir(parents=True, exist_ok=True)
                else:
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    with tar.extractfile(member) as source, destination.open('wb') as output:
                        shutil.copyfileobj(source, output)
                    destination.chmod(member.mode & 0o777)
        required = ('ProjectPrime', 'LICENSE', 'SERVER.md', 'SERVER.txt', MANIFEST)
        for name in required:
            if not (root / name).is_file():
                raise ValueError(f'missing required release file: {name}')
        binary = root / 'ProjectPrime'
        if binary.stat().st_mode & 0o111 != 0o111:
            raise ValueError('ProjectPrime is missing executable permissions')
        with binary.open('rb') as handle:
            header = handle.read(20)
        machine = {'x64': 62, 'arm64': 183}[architecture]
        if (len(header) < 20 or header[:6] != b'\x7fELF\x02\x01'
                or struct.unpack('<H', header[18:20])[0] != machine):
            raise ValueError(f'ProjectPrime is not a Linux {architecture} ELF executable')
        manifest = json.loads((root / MANIFEST).read_text())
        actual = {p.relative_to(root).as_posix() for p in root.rglob('*')
                  if p.is_file() and p.relative_to(root).as_posix() != MANIFEST}
        files = manifest.get('Files', [])
        if (manifest.get('Version') != 1 or len(files) != len(set(files))
                or set(files) != actual or set(manifest.get('Hashes', {})) != actual):
            raise ValueError('release manifest does not exactly cover archive files')
        for name in actual:
            if name.split('/')[0] in ('.project-prime-update.lock', '.project-prime-update-transaction'):
                raise ValueError(f'updater runtime state in release: {name}')
            digest_state = hashlib.sha256()
            with (root / name).open('rb') as handle:
                for chunk in iter(lambda: handle.read(1024 * 1024), b''):
                    digest_state.update(chunk)
            digest = digest_state.hexdigest()
            if digest != manifest['Hashes'][name]:
                raise ValueError(f'release checksum mismatch: {name}')
        if smoke:
            checker = pathlib.Path(__file__).with_name('check-dedicated-server.sh').resolve()
            # Running outside the checkout prevents accidentally finding developer files.
            subprocess.run(['bash', str(checker), str(root)], cwd=root, check=True)
        print(f'{archive}: valid Linux {architecture} server archive')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive')
    parser.add_argument('architecture', choices=('x64', 'arm64'))
    parser.add_argument('--smoke', action='store_true')
    args = parser.parse_args()
    try:
        validate(args.archive, args.architecture, args.smoke)
    except (ValueError, OSError, tarfile.TarError, subprocess.CalledProcessError) as error:
        parser.exit(1, f'FAIL: {error}\n')
