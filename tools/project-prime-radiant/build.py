#!/usr/bin/env python3
"""Explicit source-only fork build. Never downloads gamepacks or installs tools.
The output is a qualification candidate, not a certified editor distribution.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
LOCK = json.loads((HERE / 'upstream.lock.json').read_text())


def run(argv, cwd=None, capture=False):
    result = subprocess.run([str(x) for x in argv], cwd=cwd, check=True,
                            text=True, stdout=subprocess.PIPE if capture else None)
    return result.stdout.strip() if capture else None


def digest(path):
    result = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def validate_source(source):
    if run(['git', 'rev-parse', 'HEAD'], cwd=source, capture=True) != LOCK['commit']:
        raise ValueError('Source HEAD differs from the pinned reviewed build input.')
    expected = {patch: digest(HERE / patch) for patch in LOCK['patches']}
    state = json.loads((source / '.prime-source.json').read_text())
    if state.get('patches') != expected:
        raise ValueError('Patch manifest differs; use a fresh explicit checkout.')
    actual_diff = run(['git', 'diff', 'HEAD', '--no-ext-diff', '--binary'], cwd=source, capture=True)
    expected_diff = ''.join((HERE / patch).read_text() for patch in LOCK['patches']).strip()
    if actual_diff != expected_diff:
        raise ValueError('Source modifications differ from the recorded fork patches.')
    untracked = run(['git', 'ls-files', '--others', '--exclude-standard'], cwd=source, capture=True).splitlines()
    if any(Path(name).suffix.lower() in {'.c', '.cc', '.cpp', '.h', '.hpp'} or Path(name).name.lower().startswith('makefile') for name in untracked):
        raise ValueError('Unrecorded source files would change the build.')
    # Reverse checks prove every exact recorded patch is still applied.
    for patch in LOCK['patches']:
        run(['git', 'apply', '--reverse', '--check', HERE / patch], cwd=source)


def checkout(source):
    if source.exists():
        raise ValueError('Checkout destination must not exist; existing source is never reset.')
    source.parent.mkdir(parents=True, exist_ok=True)
    run(['git', 'clone', '--no-checkout', '--filter=blob:none', LOCK['repository'], source])
    run(['git', 'checkout', '--detach', LOCK['commit']], cwd=source)
    for patch in LOCK['patches']:
        run(['git', 'apply', '--check', HERE / patch], cwd=source)
        run(['git', 'apply', HERE / patch], cwd=source)
    (source / '.prime-source.json').write_text(json.dumps({
        'commit': LOCK['commit'],
        'patches': {p: digest(HERE / p) for p in LOCK['patches']}
    }, indent=2) + '\n')


def dependencies():
    receipt = {}
    for package in ['glib-2.0', 'libxml-2.0', 'libpng', 'Qt5Core', 'Qt5Widgets', 'Qt5Svg', 'assimp']:
        try: receipt[package] = run(['pkg-config', '--modversion', package], capture=True)
        except (OSError, subprocess.CalledProcessError): receipt[package] = None
    return receipt


def build(source, jobs, compiler_only):
    validate_source(source)
    options = ['BUILD=release', 'DOWNLOAD_GAMEPACKS=no', 'INSTALL_DLLS=no',
               'DEPENDENCIES_CHECK=off', 'RADIANT_ABOUTMSG=Project Prime Radiant source candidate']
    if os.name == 'nt' or os.environ.get('MSYSTEM'):
        options.append('MAKEFILE_CONF=msys2-Makefile.conf')
    if platform.system() == 'Darwin':
        # q3map2 is native; the upstream editor still requires its X11/Qt5 stack.
        prefix = Path('/opt/homebrew/opt/jpeg-turbo' if platform.machine() == 'arm64' else '/usr/local/opt/jpeg-turbo')
        if prefix.exists():
            options += ['CPPFLAGS_JPEG=-I' + str(prefix / 'include'), 'LIBS_JPEG=-L' + str(prefix / 'lib') + ' -ljpeg']
    run(['make', '-j' + str(jobs), 'binaries-q3map2' if compiler_only else 'all', *options], cwd=source)
    binaries = [p for p in (source / 'install').rglob('*') if p.is_file() and not p.is_symlink()]
    (source / 'prime-build-receipt.json').write_text(json.dumps({
        'schema': 1, 'commit': LOCK['commit'], 'patches': {p: digest(HERE / p) for p in LOCK['patches']},
        'platform': platform.platform(), 'compilerOnly': compiler_only,
        'dependencies': dependencies(), 'makeOptions': options,
        'binaries': {str(p.relative_to(source / 'install')): digest(p) for p in binaries},
        'guiAcceptance': 'pending', 'redistributionReview': 'pending'
    }, indent=2) + '\n')


def bundle(source, output):
    validate_source(source)
    if output.exists(): raise ValueError('Bundle destination must not exist.')
    receipt = json.loads((source / 'prime-build-receipt.json').read_text())
    install = source / 'install'
    current = {str(p.relative_to(install)) for p in install.rglob('*') if p.is_file() and not p.is_symlink()}
    if current != set(receipt['binaries']): raise ValueError('Build output file set changed.')
    for link in (p for p in install.rglob('*') if p.is_symlink()):
        try: link.resolve(strict=True).relative_to(install.resolve())
        except (ValueError, OSError): raise ValueError('Build output link escapes candidate: ' + str(link))
    if any(Path(name).suffix.lower() in {'.pk3', '.wad', '.bsp', '.ppmap', '.rom', '.nds'} for name in current):
        raise ValueError('Candidate must not contain game packages or cartridge data.')
    for name, sha in receipt['binaries'].items():
        p = install / name
        if not p.is_file() or digest(p) != sha: raise ValueError('Build output changed: ' + name)
    shutil.copytree(install, output, symlinks=True)
    notices = output / 'source-notices'
    notices.mkdir()
    for name in ['LICENSE', 'GPL', 'LGPL', 'CONTRIBUTORS', 'COMPILING']:
        shutil.copy2(source / name, notices / name)
    shutil.copy2(HERE / 'upstream.lock.json', notices)
    shutil.copy2(source / 'prime-build-receipt.json', notices)
    shutil.copytree(HERE / 'patches', notices / 'patches')
    # Include the exact upstream source plus patches/build recipe with candidate.
    with (notices / 'upstream-source.tar').open('wb') as target:
        subprocess.run(['git', 'archive', LOCK['commit']], cwd=source, stdout=target, check=True)
    recipe = notices / 'ProjectPrimeSource' / 'tools'
    shutil.copytree(HERE, recipe / 'project-prime-radiant', ignore=shutil.ignore_patterns('__pycache__'))
    shutil.copytree(ROOT / 'tools' / 'netradiant-gamepack', recipe / 'netradiant-gamepack')
    gamepack = ROOT / 'tools' / 'netradiant-gamepack'
    (output / 'games').mkdir(exist_ok=True)
    shutil.copy2(gamepack / 'projectprime.game', output / 'games')
    pack = output / 'gamepacks' / 'projectprime.game'
    (pack / 'baseq3').mkdir(parents=True)
    shutil.copy2(gamepack / 'entities.ent', pack / 'baseq3' / 'entities.ent')
    from real_tool_check import fixture
    fixture(pack)  # Original graybox plus generated checkerboard; no game assets.
    shutil.copy2(gamepack / 'README.md', pack / 'README.md')
    (output / 'QUALIFICATION-PENDING.txt').write_text(
        'Candidate only. GUI/platform, corresponding-source/dependency license review, signing, '
        'source roundtrip, real gameplay and release approval remain required.\n'
        'No cartridge assets, commercial gamepacks, or downloaded map data are included.\n')


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('command', choices=['checkout', 'build', 'bundle'])
    p.add_argument('--source', type=Path, required=True)
    p.add_argument('--output', type=Path)
    p.add_argument('--jobs', type=int, default=2)
    p.add_argument('--compiler-only', action='store_true')
    a = p.parse_args()
    if not 1 <= a.jobs <= 64: p.error('--jobs must be between 1 and 64')
    if a.command == 'checkout': checkout(a.source.resolve())
    elif a.command == 'build': build(a.source.resolve(), a.jobs, a.compiler_only)
    else:
        if a.output is None: p.error('bundle requires --output')
        bundle(a.source.resolve(), a.output.resolve())


if __name__ == '__main__':
    main()
