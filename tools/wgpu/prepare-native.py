#!/usr/bin/env python3
"""Prepare pinned sources and patches without editing Cargo's shared checkouts."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tarfile
import io

NATIVE = '33133da4ec5a0174cb21539ef2d3346f75200411'
CORE = '87576b72b37c6b78b41104eb25fc31893af94092'
ROOT = Path(__file__).resolve().parents[2]


def run(*args, **kw):
    return subprocess.check_output(args, **kw)


def ensure_source(path, url, commit, offline):
    if path.exists():
        try:
            run('git', '-C', str(path), 'cat-file', '-e', commit + '^{commit}')
            return path
        except subprocess.CalledProcessError:
            pass
    if offline:
        raise RuntimeError(f'pinned source unavailable offline: {url}@{commit}')
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists():
        subprocess.check_call(['git', 'clone', '--filter=blob:none', '--no-checkout', url, str(path)])
    subprocess.check_call(['git', '-C', str(path), 'fetch', '--depth', '1', 'origin', commit])
    return path


def archive(source, commit, target):
    data = run('git', '-C', str(source), 'archive', commit)
    target.mkdir(parents=True, exist_ok=True)
    with tarfile.open(fileobj=io.BytesIO(data)) as files:
        for member in files.getmembers():
            destination = (target/member.name).resolve()
            if target.resolve() not in destination.parents and destination != target.resolve():
                raise RuntimeError(f'unsafe pinned archive member: {member.name}')
        files.extractall(target)


def patch_fingerprint():
    inputs = [ROOT/'tools/wgpu/prime-appkit-surface.rs', ROOT/'tools/wgpu/prime-surface-outcomes.rs',
              ROOT/'tools/wgpu/prepare-native.py', ROOT/'tools/wgpu/prime-dx12-wsi-policy.rs', *sorted((ROOT/'tools/wgpu/patches').glob('*.patch'))]
    digest = hashlib.sha256((NATIVE + CORE).encode())
    for item in inputs:
        digest.update(item.name.encode()); digest.update(item.read_bytes())
    return digest.hexdigest()


def prepare(offline):
    fingerprint = patch_fingerprint()
    inputs = [ROOT/'tools/wgpu/prime-appkit-surface.rs', ROOT/'tools/wgpu/prime-surface-outcomes.rs']
    target = ROOT/'artifacts/wgpu-native-build-patched'/fingerprint
    manifest = target/'PRIME-PATCHES.json'
    if manifest.exists():
        return target
    native = ensure_source(ROOT/'artifacts/wgpu-native-src'/NATIVE,
                           'https://github.com/gfx-rs/wgpu-native.git', NATIVE, offline)
    # Read the pinned object from an existing Cargo checkout, never its dirty files.
    cargo = Path(os.environ.get('CARGO_HOME', str(Path.home()/'.cargo')))
    core = next((p for p in cargo.glob('git/checkouts/wgpu-*/*')
                 if (p/'wgpu-core').is_dir() and run('git','-C',str(p),'rev-parse','HEAD',text=True).strip()==CORE), None)
    if core is None:
        core = ensure_source(ROOT/'artifacts/wgpu-core-src'/CORE,
                             'https://github.com/gfx-rs/wgpu.git', CORE, offline)
    headers = native/'ffi/webgpu-headers'
    header_commit = run('git','-C',str(native),'ls-tree',NATIVE,'ffi/webgpu-headers',text=True).split()[2]
    ensure_source(headers, 'https://github.com/webgpu-native/webgpu-headers.git', header_commit, offline)
    archive(native, NATIVE, target)
    archive(headers, header_commit, target/'ffi/webgpu-headers')
    core_target = target.parent/(fingerprint + '-core')
    archive(core, CORE, core_target)
    (core_target/'wgpu-hal/src/dx12/prime_wsi.rs').write_bytes((ROOT/'tools/wgpu/prime-dx12-wsi-policy.rs').read_bytes())
    for patch in sorted((ROOT/'tools/wgpu/patches').glob('*.patch')):
        where = core_target if patch.name.startswith('dx12-') else target
        subprocess.check_call(['git','apply','--check',str(patch)], cwd=where)
        subprocess.check_call(['git','apply',str(patch)], cwd=where)
    for item in inputs[:2]:
        (target/item.name).write_bytes(item.read_bytes())
    lib = target/'src/lib.rs'
    with lib.open('a') as stream:
        for item in inputs[:2]:
            stream.write(f'\ninclude!(concat!(env!("CARGO_MANIFEST_DIR"), "/{item.name}"));\n')
    toml = target/'Cargo.toml'
    text = toml.read_text().replace('[features]\n','[features]\nprime-fault-injection = []\n',1).replace('members = ["."]', 'members = ["."]\nexclude = ["_prime_wgpu"]', 1)
    text += '\n[patch."https://github.com/gfx-rs/wgpu"]\n'
    for crate in ('naga','wgpu-core','wgpu-hal','wgpu-types'):
        text += f'{crate} = {{ path = "../{fingerprint}-core/{crate}" }}\n'
    toml.write_text(text)
    # Only change source identity of the four pinned packages. All registry
    # versions/checksums remain exactly those in the upstream lockfile.
    lock = target/'Cargo.lock'
    text = lock.read_text()
    text = '\n'.join(line for line in text.split('\n')
                     if not line.startswith('source = "git+https://github.com/gfx-rs/wgpu?'))
    lock.write_text(text)
    manifest.write_text(json.dumps({'bridge_abi':1,'native_commit':NATIVE,'core_commit':CORE,
                                    'patch_fingerprint':fingerprint}, indent=2)+'\n')
    return target


if __name__ == '__main__':
    args = argparse.ArgumentParser()
    args.add_argument('--offline', action='store_true')
    options = args.parse_args()
    print(prepare(options.offline))
