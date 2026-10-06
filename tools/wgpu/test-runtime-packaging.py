#!/usr/bin/env python3
"""Exercise actual package validator against stale/tampered runtime inputs."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile

parser = argparse.ArgumentParser()
parser.add_argument('production', type=Path)
parser.add_argument('fault', type=Path)
args = parser.parse_args()
validator = Path(__file__).parent/'verify-runtime.py'
production = args.production.read_bytes()
metadata = json.loads((args.production.parent/'PRIME-WGPU.json').read_text())
fault = args.fault.read_bytes()
passed = 0
with tempfile.TemporaryDirectory(prefix='prime-native-package-') as directory:
    target = Path(directory)/metadata['target']; target.mkdir()
    library = target/args.production.name
    manifest = target/'PRIME-WGPU.json'
    def check(name, data, meta, valid=False):
        global passed
        library.write_bytes(data)
        manifest.write_text(json.dumps(meta))
        result = subprocess.run([sys.executable,str(validator),str(library)],capture_output=True,text=True)
        if (result.returncode == 0) != valid:
            raise AssertionError(name + '\n' + result.stdout + result.stderr)
        passed += 1
    check('valid production', production, metadata, True)
    check('stale patch fingerprint', production, dict(metadata,patch_fingerprint='stale'))
    check('mismatched native hash', production, dict(metadata,sha256='0'*64))
    check('wrong source pin', production, dict(metadata,native_commit='wrong'))
    check('wrong target RID', production, dict(metadata,target='win-x64' if metadata['target']!='win-x64' else 'linux-x64'))
    check('test-only manifest flag', production, dict(metadata,fault_injection=True))
    check('test-only feature flag', production, dict(metadata,features=metadata['features']+',prime-fault-injection'))
    check('tampered actual binary', production+b'tampered', metadata)
    # Even falsifying both feature fields cannot ship the real injection DLL.
    check('hidden test-only export', fault, dict(metadata,sha256=hashlib.sha256(fault).hexdigest()))
    manifest.unlink()
    result = subprocess.run([sys.executable,str(validator),str(library)],capture_output=True)
    assert result.returncode != 0, 'missing manifest accepted'
    passed += 1
print(f'{passed} actual packaging validation cases passed.')
