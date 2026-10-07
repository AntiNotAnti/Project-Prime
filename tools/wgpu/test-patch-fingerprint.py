#!/usr/bin/env python3
"""The same UTF-8 patch checkout has the same identity on every desktop OS."""
import importlib.util
from pathlib import Path
import tempfile

spec = importlib.util.spec_from_file_location('prepare_native', Path(__file__).with_name('prepare-native.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
inputs = ['prime-appkit-surface.rs', 'prime-surface-outcomes.rs', 'prepare-native.py',
          'prime-dx12-wsi-policy.rs', 'prime-submit-observation.rs', 'patches/one.patch']
with tempfile.TemporaryDirectory(prefix='prime-native-fingerprint-') as directory:
    module.ROOT = Path(directory)
    for name in inputs:
        path = module.ROOT/'tools/wgpu'/name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(('header ' + name + '\ncafé\n').encode('utf-8'))
    expected = module.patch_fingerprint()
    for name in inputs:
        path = module.ROOT/'tools/wgpu'/name
        path.write_bytes(path.read_bytes().replace(b'\n', b'\r\n'))
    assert module.patch_fingerprint() == expected, 'CRLF changed patch identity'
    for name in inputs:
        path = module.ROOT/'tools/wgpu'/name
        path.write_bytes(path.read_bytes().replace(b'\r\n', b'\r'))
    assert module.patch_fingerprint() == expected, 'CR changed patch identity'
    path = module.ROOT/'tools/wgpu/prime-appkit-surface.rs'
    path.write_bytes(path.read_bytes() + b'changed native patch\n')
    assert module.patch_fingerprint() != expected, 'Real patch changes must invalidate identity'
print('PASS: native patch identity preserves checkout line endings and detects content changes')
