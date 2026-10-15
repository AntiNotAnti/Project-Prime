#!/usr/bin/env python3
"""Verify extracted desktop package identities, including signed macOS sibling apps."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import struct

PAIR = '.project-prime-desktop.json'
MANIFEST = '.project-prime-files.json'


def binary_rids(path):
    """Read native apphost headers without executing cross-published binaries."""
    with path.open('rb') as stream:
        header = stream.read(64)
        if header[:2] == b'MZ' and len(header) == 64:
            stream.seek(struct.unpack_from('<I', header, 60)[0])
            pe = stream.read(6)
            return {'win-x64'} if pe == b'PE\0\0\x64\x86' else set()
        if header[:6] == b'\x7fELF\x02\x01' and len(header) >= 20:
            return {62: {'linux-x64'}, 183: {'linux-arm64'}}.get(struct.unpack_from('<H', header, 18)[0], set())
        cpu_rids = {0x1000007: 'osx-x64', 0x100000c: 'osx-arm64'}
        if header[:4] in (b'\xcf\xfa\xed\xfe', b'\xfe\xed\xfa\xcf'):
            cpu = struct.unpack_from('<I' if header[0] == 0xcf else '>I', header, 4)[0]
            return {cpu_rids[cpu]} if cpu in cpu_rids else set()
        if header[:4] in (b'\xca\xfe\xba\xbe', b'\xca\xfe\xba\xbf'):
            count = struct.unpack_from('>I', header, 4)[0]
            if count > 32:
                raise ValueError('invalid universal apphost header')
            stride = 32 if header[3] == 0xbf else 20
            stream.seek(8)
            entries = stream.read(count * stride)
            if len(entries) != count * stride:
                raise ValueError('truncated universal apphost header')
            return {cpu_rids[cpu] for offset in range(0, len(entries), stride)
                    if (cpu := struct.unpack_from('>I', entries, offset)[0]) in cpu_rids}
    return set()


def digest(path):
    result = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def verify(root, expected=None, rid=None):
    root = Path(root)
    if (root / PAIR).stat().st_size > 16384 or (root / MANIFEST).stat().st_size > 8 * 1024 * 1024:
        raise ValueError('paired release metadata exceeds its byte budget')
    pair = json.loads((root / PAIR).read_text(encoding='utf-8'))
    manifest = json.loads((root / MANIFEST).read_text(encoding='utf-8'))
    if pair.get('Version') != 2 or manifest.get('Version') != 1:
        raise ValueError('unsupported paired release schema')
    version = pair.get('GameVersion', '')
    if not isinstance(version, str) or len(version) > 128 or not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+', version) or version != pair.get('StudioVersion') or expected and version != expected:
        raise ValueError('Game and Studio release versions do not match')
    if type(pair.get('IpcVersion')) is not int or pair['IpcVersion'] <= 0:
        raise ValueError('invalid paired IPC version')
    files = manifest['Files']
    if not isinstance(files, list) or len(files) > 100000 or any(not isinstance(name, str) for name in files) or not isinstance(manifest.get('Hashes'), dict):
        raise ValueError('invalid release ownership')
    if len(files) != len(set(name.casefold() for name in files)):
        raise ValueError('ambiguous release ownership')
    for name in files:
        path = PurePosixPath(name)
        if path.is_absolute() or any(part in ('', '.', '..') or '\\' in part or ':' in part for part in name.split('/')):
            raise ValueError('unsafe release path')
        target = root
        for part in path.parts:
            target = target / part
            if target.is_symlink():
                raise ValueError('release cannot contain symbolic links')
        owned_hash = manifest['Hashes'].get(name)
        if not isinstance(owned_hash, str) or not re.fullmatch(r'[0-9a-fA-F]{64}', owned_hash) or not target.is_file() or digest(target) != owned_hash.lower():
            raise ValueError('release file digest mismatch: ' + name)
    if PAIR not in files:
        raise ValueError('paired metadata is not owned')
    layouts = [('ProjectPrime', 'ProjectPrimeStudio'), ('ProjectPrime.exe', 'ProjectPrimeStudio.exe'),
               ('Project Prime.app/Contents/MacOS/ProjectPrime', 'Project Prime Studio.app/Contents/MacOS/ProjectPrimeStudio')]
    layout = (pair.get('GameBinary'), pair.get('StudioBinary'))
    if layout not in layouts:
        raise ValueError('Game and Studio must be matching sibling binaries or sibling macOS apps')
    if rid is not None and layout != layouts[2 if rid.startswith('osx-') else 1 if rid.startswith('win-') else 0]:
        raise ValueError('paired binary layout does not match release target: ' + rid)
    for role, binary in (('Game', 'ProjectPrime'), ('Studio', 'ProjectPrimeStudio')):
        name = pair.get(role + 'Binary', '')
        binary_hash = pair.get(role + 'Sha256')
        if PurePosixPath(name).name not in (binary, binary + '.exe') or name not in files or not isinstance(binary_hash, str) or binary_hash.lower() != manifest['Hashes'][name].lower():
            raise ValueError('paired binary identity mismatch: ' + role)
        if rid is not None and rid not in binary_rids(root / name):
            raise ValueError('paired binary architecture mismatch: ' + role + '; expected ' + rid)
    mac = pair['GameBinary'].startswith('Project Prime.app/')
    if mac and not pair['StudioBinary'].startswith('Project Prime Studio.app/'):
        raise ValueError('macOS package must contain sibling Game and Studio apps')
    if not mac and ('/' in pair['GameBinary'] or '/' in pair['StudioBinary']):
        raise ValueError('desktop package must contain sibling Game and Studio binaries')
    return pair


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root')
    parser.add_argument('--version')
    parser.add_argument('--rid', choices=['win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64'])
    args = parser.parse_args()
    pair = verify(args.root, args.version, args.rid)
    print(f"Verified Game + Studio {pair['GameVersion']}; IPC {pair['IpcVersion']} and exact release hashes.")
