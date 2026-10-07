#!/usr/bin/env python3
"""Exercise actual build-script option forwarding without building native code."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

script = Path(__file__).with_name('build-native.sh')
shells = list(dict.fromkeys(path for path in ('/bin/bash', shutil.which('bash')) if path and Path(path).is_file()))
assert shells, 'bash is required to check native build argument forwarding'
with tempfile.TemporaryDirectory(prefix='prime-native-shell-') as temporary:
    root = Path(temporary)
    cargo = root / 'cargo'
    cargo.write_text('#!/bin/sh\nprintf "%s\\n" "$@" > "$PRIME_SHELL_FIXTURE/cargo-args"\nexit 77\n', encoding='utf-8')
    rustup = root / 'rustup'
    rustup.write_text('#!/bin/sh\nprintf "aarch64-apple-darwin\\n"\n', encoding='utf-8')
    uname = root / 'uname'
    uname.write_text('#!/bin/sh\nprintf "Darwin\\n"\n', encoding='utf-8')
    prepare = root / 'prepare'
    prepare.write_text('#!/bin/sh\nprintf "%s\\n" "$@" > "$PRIME_SHELL_FIXTURE/prepare-args"\nprintf "%s/source\\n" "$PRIME_SHELL_FIXTURE"\n', encoding='utf-8')
    for executable in (cargo, rustup, uname, prepare):
        executable.chmod(0o755)
    environment = dict(os.environ, PATH=str(root) + os.pathsep + os.environ['PATH'],
                       PRIME_PYTHON=str(prepare), PRIME_SHELL_FIXTURE=str(root))
    for shell in shells:
        for optional in ([], ['--offline']):
            result = subprocess.run([shell, str(script), 'osx-arm64', *optional], env=environment, text=True, capture_output=True)
            assert result.returncode == 77, (shell, optional, result.stdout, result.stderr)
            prepared = (root / 'prepare-args').read_text(encoding='utf-8').splitlines()
            built = (root / 'cargo-args').read_text(encoding='utf-8').splitlines()
            assert prepared[1:] == optional, prepared
            assert built.count('--offline') == len(optional), built
            assert '--release' in built and built[0] == 'build', built
print(f'PASS: actual native build option forwarding in {len(shells)} Bash executables, online and offline.')
