#!/usr/bin/env python3
"""Prepare isolated input copies and commands. Does not build or open a window."""
import argparse
import hashlib
import json
import pathlib
import shlex
import shutil
import tempfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--assets', type=pathlib.Path, required=True)
parser.add_argument('--native', type=pathlib.Path, required=True)
parser.add_argument('--rmlui-source', type=pathlib.Path, required=True)
parser.add_argument('--opentk-dir', type=pathlib.Path)
parser.add_argument('--dotnet', default='/Users/jarrett/.dotnet/dotnet')
args = parser.parse_args()
repo = pathlib.Path(__file__).resolve().parents[2]
tool = repo / 'tools/rmlui-text-edit-check'
assets = args.assets.resolve(strict=True)
native = args.native.resolve(strict=True)
rmlui = args.rmlui_source.resolve(strict=True)
if not assets.is_dir() or not (assets / 'prime_home.rml').is_file():
    parser.error('--assets must contain the packaged RmlUi pages and fonts')
if not native.is_file() or not (rmlui / 'Include/RmlUi/Core.h').is_file():
    parser.error('pass an existing native library and the pinned RmlUi source')
root = pathlib.Path(tempfile.mkdtemp(prefix='prime-rmlui-text-edit-', dir='/private/tmp' if pathlib.Path('/private/tmp').is_dir() else '/tmp'))
shutil.copytree(assets, root / 'assets')
(root / 'runtime').mkdir()
shutil.copy2(native, root / 'runtime' / native.name)
manifest = []
def record(path, name):
    manifest.append({'path': name, 'bytes': path.stat().st_size,
                     'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
for path in sorted((root / 'assets').rglob('*')):
    if path.is_file(): record(path, str(path.relative_to(root)))
record(root / 'runtime' / native.name, 'runtime/' + native.name)
for path in sorted((repo / 'native/rmlui-poc').glob('projectprime_rmlui*')):
    if path.is_file(): record(path, str(path.relative_to(repo)))
for path in sorted((repo / 'src/MphRead/Mods/Launcher/RmlUi/Host').glob('*.cs')):
    record(path, str(path.relative_to(repo)))
for path in sorted(tool.rglob('*')):
    if path.is_file() and not {'bin', 'obj', '__pycache__'}.intersection(path.relative_to(tool).parts):
        record(path, str(path.relative_to(repo)))
(root / 'input-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
quote = shlex.quote
runtime = root / 'runtime' / native.name
commands = {
    'native_configure': ['cmake', '-S', str(tool / 'native'), '-B', str(root / 'native-check'),
                         '-DPRIME_RMLUI_NATIVE_LIBRARY=' + str(runtime), '-DRMLUI_SOURCE_DIR=' + str(rmlui)],
    'native_build': ['cmake', '--build', str(root / 'native-check'), '--parallel', '2'],
    'native_run': [str(root / 'native-check/PrimeTextEditCpuCheck'), str(root / 'assets')],
    'managed_cpu': [args.dotnet, 'run', '--project', str(tool / 'managed-cpu'), '-c', 'Release', '--', str(root / 'assets')],
}
if args.opentk_dir:
    opentk = args.opentk_dir.resolve(strict=True)
    commands['appkit_build_only_no_window'] = [args.dotnet, 'build', str(tool / 'appkit'), '-c', 'Release',
                                              '-p:OpenTkAssemblyDirectory=' + str(opentk)]
    if (opentk / 'libglfw.3.dylib').is_file():
        commands['appkit_copy_glfw_after_build'] = ['cp', str(opentk / 'libglfw.3.dylib'), str(tool / 'appkit/bin/Release/net10.0/libglfw.3.dylib')]
    commands['APPKIT_WINDOW_EXCLUSIVE_LEASE_REQUIRED'] = [args.dotnet, str(tool / 'appkit/bin/Release/net10.0/appkit.dll'), str(root / 'assets')]
(root / 'commands.json').write_text(json.dumps(commands, indent=2) + '\n')
print('Prepared only; no builds/windows: ' + str(root))
print('Set PRIME_RMLUI_TEXT_EDIT_NATIVE=' + quote(str(runtime)) + ' for managed commands.')
for name, command in commands.items():
    print(name + ': ' + shlex.join(command))
