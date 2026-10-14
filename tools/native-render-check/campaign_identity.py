"""Diagnostic fixture identity; never copy a player's settings or credentials."""
import hashlib
import json
from pathlib import Path
import platform
import shutil
import subprocess


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def asset_identity(paths_file):
    entries = []
    roots = {}
    for line in Path(paths_file).read_text().splitlines()[1:]:
        key, separator, value = line.partition('=')
        if not separator or not value.strip() or key == 'Export':
            continue
        root = Path(value.strip())
        if not root.is_absolute():
            root = Path(paths_file).parent / root
        root = root.resolve(strict=True)
        roots[key] = str(root)
        for path in sorted(root.rglob('*')):
            if path.is_file():
                # Relative keys distinguish identical filenames in different ROM versions.
                entries.append({'key': key + '/' + path.relative_to(root).as_posix(),
                                'bytes': path.stat().st_size, 'sha256': sha256(path)})
    if not entries:
        raise ValueError('paths.txt contains no extracted asset files')
    payload = json.dumps(entries, sort_keys=True, separators=(',', ':')).encode()
    return {'sha256': hashlib.sha256(payload).hexdigest(), 'fileCount': len(entries),
            'bytes': sum(e['bytes'] for e in entries), 'roots': roots, 'files': entries}


def isolate_user_data(template, output):
    source = Path(template).resolve() / 'paths.txt'
    identity = asset_identity(source)
    target = Path(output) / 'user-data'
    target.mkdir()
    lines = source.read_text().splitlines()
    normalized = [lines[0]]
    for line in lines[1:]:
        key, separator, value = line.partition('=')
        if separator:
            normalized.append(key + '=' + identity['roots'].get(key, ''))
    (target / 'paths.txt').write_text('\n'.join(normalized) + '\n')
    return target, identity


def host_identity(dotnet):
    def read(command):
        try:
            result = subprocess.run(command, capture_output=True, text=True, timeout=30)
            return {'command': command, 'exitCode': result.returncode,
                    'output': result.stdout.strip() if result.returncode == 0 else None,
                    'error': result.stderr.strip() or None}
        except (OSError, subprocess.TimeoutExpired) as error:
            return {'command': command, 'exitCode': None, 'output': None, 'error': str(error)}
    return {'os': platform.platform(), 'architecture': platform.machine(),
            'macOsBuild': read(['sw_vers']) if platform.system() == 'Darwin' else None,
            'dotnet': read([dotnet, '--info']),
            'gpuIdentitySource': 'Per-context GL vendor/renderer/version; driver build is macOS build.',
            'otherProjectPrimeProcesses': read(['pgrep', '-x', 'ProjectPrime']) if shutil.which('pgrep') else None}
