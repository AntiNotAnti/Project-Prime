#!/usr/bin/env python3
"""Fail closed when packaging a stale, unpatched or test-only runtime."""
import hashlib
import json
from pathlib import Path
import sys
import runpy
library = Path(sys.argv[1])
metadata = json.loads((library.parent/'PRIME-WGPU.json').read_text())
assert metadata['bridge_abi'] == 1
expected = runpy.run_path(str(Path(__file__).parent/'prepare-native.py'))['patch_fingerprint']()
assert metadata['patch_fingerprint'] == expected, 'runtime was built from stale or different checked-in patches'
assert metadata['native_commit'] == '33133da4ec5a0174cb21539ef2d3346f75200411'
assert metadata['core_commit'] == '87576b72b37c6b78b41104eb25fc31893af94092'
assert not metadata['fault_injection'], 'refusing to ship fault-injection runtime'
assert 'prime-fault-injection' not in metadata['features'].split(','), 'refusing to ship fault-injection feature'
expected_target = {'arm64-v8a':'android-arm64', 'x86_64':'android-x64'}.get(library.parent.name, library.parent.name)
assert metadata['target'] == expected_target, 'runtime manifest target disagrees with package RID/ABI directory'
assert metadata['library'] == library.name
data = library.read_bytes()
assert metadata['sha256'] == hashlib.sha256(data).hexdigest(), 'runtime hash disagrees with build manifest'
assert b'primeWgpuInjectSurfaceOutcome\x00' not in data, 'test-only injection export detected in native binary'
for name in ('primeWgpuBridgeVersion', 'primeWgpuSurfaceConfigure', 'primeWgpuSurfaceAcquire',
             'primeWgpuSurfacePresent', 'primeWgpuSurfaceDiscard', 'primeWgpuQueueSubmit',
             'primeWgpuQueueGetTimestampPeriod'):
    assert name.encode() + b'\x00' in data, f'patched bridge export absent: {name}'
if expected_target.startswith('osx'):
    assert b'primeInstanceCreateSurfaceAppKit\x00' in data, 'MoltenVK AppKit surface bridge absent'
print(f'verified patched runtime: {library}')
