#!/usr/bin/env python3
"""Capture the actual guarded Mac client; never builds or changes real user data."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile


SIZES = {
    '720p-small': (640, 360),
    '900p': (800, 450),
    '1080p': (960, 540),
    '1440p': (1280, 720),
    '4k-request': (1920, 1080),
    '16x10': (720, 450),
    'ultrawide': (1280, 540),
    '4x3': (640, 480),
}
CASES = [(f'home-{name}-density{density}', 'home', size, density)
    for name, size in SIZES.items() for density in (1, 2)]
CASES += [(f'{route}-{name}-density2', route, SIZES[name], 2)
    for route in ('news', 'settings') for name in ('720p-small', '1440p')]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def asset_digest(root):
    value = hashlib.sha256()
    for path in sorted(p for p in root.rglob('*') if p.is_file()):
        value.update(path.relative_to(root).as_posix().encode('utf-8'))
        value.update(b'\0')
        value.update(bytes.fromhex(digest(path)))
    return value.hexdigest()


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--native', required=True, type=Path)
parser.add_argument('--expected-sha256', required=True)
parser.add_argument('--paths', required=True, type=Path, help='Extraction paths.txt only; no account/prefs directory is copied.')
parser.add_argument('--out', required=True, type=Path)
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--case', choices=[item[0] for item in CASES], help='Capture one case; otherwise capture the full matrix serially.')
parser.add_argument('--list', action='store_true', help='Print matrix and verify the binary without opening any windows.')
args = parser.parse_args()
if sys.platform != 'darwin':
    parser.error('This candidate matrix records the local Mac Retina window convention; use a platform-specific matrix elsewhere.')
binary = args.native.resolve()
if not binary.is_file() or digest(binary) != args.expected_sha256:
    parser.error('The exact full native binary is missing or changed; refuse capture.')
for guard in ('Live account authentication is disabled in UI performance diagnostics.',
        'Live account requests are disabled in UI performance diagnostics.'):
    if guard.encode('utf-16le') not in binary.read_bytes():
        parser.error('The exact binary lacks diagnostic account guards; refuse capture.')
if args.paths.name != 'paths.txt' or not args.paths.is_file():
    parser.error('Supply an existing extraction paths.txt file.')
assets = binary.parent / 'rmlui'
if not (assets / 'prime_home.rml').is_file():
    parser.error('The full build has no matching native asset root.')
selected = [item for item in CASES if args.case is None or item[0] == args.case]
if args.list:
    print(json.dumps([{'case': key, 'route': route, 'requested_window': size,
        'forced_native_density': density} for key, route, size, density in selected], indent=2))
    raise SystemExit(0)
args.out.mkdir(parents=True, exist_ok=True)
initial_assets = asset_digest(assets)
for key, route, size, density in selected:
    if digest(binary) != args.expected_sha256 or asset_digest(assets) != initial_assets:
        raise RuntimeError('The binary/assets changed during capture; refuse mixed-checkpoint evidence.')
    output = args.out / key
    output.mkdir(parents=True, exist_ok=True)
    image = output / 'rmlui-home.png'
    evidence = output / 'rmlui-home.png.evidence.json'
    if image.exists() or evidence.exists():
        raise RuntimeError('Use a new output directory; existing captures are not silently replaced.')
    with tempfile.TemporaryDirectory(prefix='prime-rmlui-golden-data-') as scratch:
        fixture = Path(scratch)
        shutil.copy2(args.paths, fixture / 'paths.txt')
        env = dict(os.environ, PROJECT_PRIME_USER_DATA=str(fixture),
            PROJECT_PRIME_UI_PERF=str(output.resolve() / 'unused-performance-report.json'))
        # The performance switch is required for the existing central account,
        # Social startup and updater guards. Capture exits after 30 frames and
        # makes no timing acceptance claim.
        env.pop('PROJECT_PRIME_UI_PERF_EXIT', None)
        env.pop('PROJECT_PRIME_UI_PERF_SCREENSHOT', None)
        command = [args.dotnet, str(binary), '-launcher', '-ui=rmlui', '-renderer', 'metal', '-windowed',
            '-rmluipocshot', str(output.resolve()),
            '-rmluisize', f'{size[0]}x{size[1]}', '-rmluidensity', str(density)]
        if route != 'home': command += ['-rmluipage', route]
        with (output / 'capture.log').open('w') as log:
            result = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=90)
        if result.returncode != 0 or not image.is_file() or not evidence.is_file():
            for path in (fixture / 'logs').glob('ProjectPrime*'):
                if path.is_file(): shutil.copy2(path, output / path.name)
            raise RuntimeError(f'{key} failed actual presentation (exit{result.returncode}, image{image.exists()}, evidence{evidence.exists()}); inspect its local logs.')
        if any('session' in p.name.lower() or 'ticket' in p.name.lower() or p.name == 'ui-startup.json'
                for p in fixture.rglob('*') if p.is_file()):
            raise RuntimeError(f'{key} created account state or native-startup failure state; refuse acceptance.')
        png = image.read_bytes()
        if png[:8] != b'\x89PNG\r\n\x1a\n':
            raise RuntimeError('Actual image is not PNG.')
        width, height = struct.unpack('>II', png[16:24])
        actual = json.loads(evidence.read_text())
        if (actual.get('width'), actual.get('height')) != (width, height) or actual.get('actualBackend') != 'Metal':
            raise RuntimeError('PNG/backend evidence mismatch; refuse capture.')
        checkpoint = {'format': 1, 'case': key, 'route': route,
            'assembly_sha256': args.expected_sha256, 'asset_tree_sha256': initial_assets,
            'requested_window_width': size[0], 'requested_window_height': size[1],
            'expected_retina_framebuffer_width': size[0] * 2, 'expected_retina_framebuffer_height': size[1] * 2,
            'actual_framebuffer_width': width, 'actual_framebuffer_height': height,
            'requested_native_density': density, 'native_density_forced': True,
            'png_sha256': digest(image), 'actual_backend': actual['actualBackend'],
            'adapter': actual.get('adapter'), 'platform': actual.get('platform'),
            'requested_retina_size_observed': (width, height) == (size[0] * 2, size[1] * 2),
            'safe_fixture': 'Fresh empty user data containing extraction paths.txt only; central diagnostic account/Social/updater guards enabled.',
            'coverage': 'Actual final client composite; forced native density is not a physical monitor-density run. No performance, device-loss or legacy-golden acceptance inferred.',
            'visual_inspection': 'pending'}
        (output / 'checkpoint.json').write_text(json.dumps(checkpoint, indent=2) + '\n')
        print(f'{key}: requested window {size[0]}x{size[1]}, actual framebuffer {width}x{height}, forced native density {density}', flush=True)
