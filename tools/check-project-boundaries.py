#!/usr/bin/env python3
"""Check the physical project graph and keep retired server code out."""
from __future__ import annotations
import argparse
import importlib.util
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

PROJECTS = {
    'Game': set(),
    'Client': {'Game', 'Audio.Ncsf', 'Server.Shared', 'Shared.Replay'},
    'Server.Shared': {'Game'},
    'Server.Node': {'Server.Shared'},
    'Server.Worker': {'Game', 'Server.Shared', 'Shared.Replay'},
    'Tools': {'Game'},
    'Android': {'Game', 'Audio.Ncsf', 'Shared.Replay', 'Server.Shared'},
    'Audio.Ncsf': set(),
    'Shared.Replay': {'Game'},
    'Backend': {'Game'},
}
PLATFORM_NAMES = re.compile(
    r'\b(?:Avalonia|SoundFlow|NCSFCommon|ReFuel|Silk\.NET\.OpenAL)\b'
    r'|\bOpenTK\.(?:Graphics|Audio|Windowing)\b'
    r'|\bMphRead\.Mods\.(?:Render|Sound)\b'
)
PLATFORM_PACKAGES = re.compile(
    r'^(?:Avalonia(?:\.|$)|SoundFlow(?:\.|$)|NCSFCommon(?:\.|$)|ReFuel(?:\.|$)'
    r'|Silk\.NET\.OpenAL(?:\.|$)|OpenTK\.(?:Graphics|Audio|Windowing)(?:\.|$))'
)
SERVER_ALLOWED_PACKAGES = {'Microsoft.IdentityModel.JsonWebTokens'}
GAME_IO = re.compile(r'\bSystem\.Net\.Sockets\b|\b(?:NetTransport|UdpTransport|ServerProcessHost|ScenePresentation|PlayerPresentation)\b')
RETIRED_SERVER_SYMBOLS = re.compile(
    r'\b(?:MasterServer|MasterReporter|AuthoritativeServer|StandaloneAuthoritativeServer|'
    r'ServerTicketAuthority|ServerUpdate(?:Runtime|Install)?|ServerVote(?:Session|Options)?|'
    r'ServerVoting|AdminHttpServer)\b')
RETIRED_SERVER_PROJECT = Path('src/Server/Server.csproj')


def xml_files(project: Path) -> list[tuple[Path, ET.Element]]:
    """Read explicit local source-list imports as well as the project itself."""
    seen: set[Path] = set()
    result = []
    def read(path: Path) -> None:
        path = path.resolve()
        if path in seen:
            return
        seen.add(path)
        tree = ET.parse(path).getroot()
        result.append((path, tree))
        for item in tree.iter('Import'):
            name = item.get('Project', '')
            if name and '$(' not in name:
                read(path.parent / name.replace('\\', '/'))
    read(project)
    return result


def inspect(root: Path) -> list[str]:
    root = root.resolve()
    errors: list[str] = []
    spec = importlib.util.spec_from_file_location('multiplayer_guard', root / 'tools/check-multiplayer-only.py')
    if spec is None or spec.loader is None:
        raise ValueError('cannot load source tokenizer')
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    retired_project = (root / RETIRED_SERVER_PROJECT).resolve()
    for project in sorted(root.rglob('*.csproj')):
        if {'obj', 'bin'}.intersection(project.relative_to(root).parts):
            continue
        try:
            tree = ET.parse(project)
        except ET.ParseError:
            continue
        for item in tree.getroot().iter('ProjectReference'):
            target = (project.parent / item.attrib.get('Include', '').replace('\\', '/')).resolve()
            if target == retired_project:
                errors.append(f'{project.relative_to(root)}: reference to retired project: {RETIRED_SERVER_PROJECT.as_posix()}')
    for name, expected in PROJECTS.items():
        path = root / 'src' / name / f'{name}.csproj'
        if not path.is_file():
            errors.append(f'missing project: {path.relative_to(root)}')
            continue
        references: set[str] = set()
        packages: set[str] = set()
        for owner, tree in xml_files(path):
            for item in tree.iter():
                if item.tag in {'MphReadServer', 'MphReadAvalonia'} or (
                        item.tag == 'DefineConstants' and any(x in (item.text or '') for x in ['MPHREAD_SERVER', 'MPHREAD_AVALONIA'])):
                    errors.append(f'{owner.relative_to(root)}: retired build personality {item.tag}')
                if item.tag == 'ProjectReference':
                    target = (owner.parent / item.attrib['Include'].replace('\\', '/')).resolve()
                    if not target.is_file():
                        errors.append(f'{owner.relative_to(root)}: missing project reference {target}')
                    references.add(target.stem)
                if item.tag == 'PackageReference':
                    packages.add(item.attrib['Include'])
                if item.tag != 'Compile' or 'Include' not in item.attrib:
                    continue
                for include in item.attrib['Include'].split(';'):
                    normalized = include.replace('\\', '/')
                    target = (owner.parent / normalized).resolve()
                    cross_project = not target.is_relative_to(path.parent.resolve())
                    if cross_project and any(c in normalized for c in '*?$'):
                        errors.append(f'{owner.relative_to(root)}: shared sources must be explicit files: {include}')
                        continue
                    if cross_project and not target.is_file():
                        errors.append(f'{owner.relative_to(root)}: missing linked source: {include}')
                    if cross_project:
                        allowed = {'Client': {'Shared'},
                                   'Server.Worker': {'Shared'},
                                   'Tools': {'Shared', 'Audio.Ncsf'},
                                   'Android': {'Shared', 'Client'},
                                   'Backend': {'Shared'}}.get(name, set())
                        relative = target.relative_to(root / 'src') if target.is_relative_to(root / 'src') else None
                        if relative is None or relative.parts[0] not in allowed:
                            errors.append(f'{owner.relative_to(root)}: invalid {name} source link: {include}')
                        if name == 'Android' and (target.name in {'Program.cs', 'RenderWindow.cs', 'ModEntry.cs'}
                                or '/Desktop/' in str(target)):
                            errors.append(f'{owner.relative_to(root)}: desktop entry in Android: {include}')
        if references != expected:
            errors.append(f'{name}: project references {sorted(references)}; expected {sorted(expected)}')
        if name == 'Game' and packages != {'OpenTK.Mathematics'}:
            errors.append(f'Game: package budget exceeded: {sorted(packages)}')
        if name == 'Shared.Replay' and packages:
            errors.append(f'Shared.Replay: unexpected packages: {sorted(packages)}')
        if name == 'Server.Worker':
            forbidden = sorted(packages - SERVER_ALLOWED_PACKAGES)
            if forbidden:
                errors.append(f'{name}: unexpected platform packages: {forbidden}')
        if name == 'Backend':
            forbidden = sorted(package for package in packages if PLATFORM_PACKAGES.search(package))
            if forbidden:
                errors.append(f'{name}: unexpected platform packages: {forbidden}')
        if name in {'Game', 'Server.Worker', 'Shared.Replay', 'Backend'}:
            for source in sorted(path.parent.rglob('*.cs')):
                if {'obj', 'bin'}.intersection(source.relative_to(path.parent).parts):
                    continue
                code = module.mask_non_code(source.read_text(encoding='utf-8-sig'))
                patterns = [PLATFORM_NAMES] + ([GAME_IO] if name == 'Game' else [])
                for pattern in patterns:
                    match = pattern.search(code)
                    if match:
                        line = code.count('\n', 0, match.start()) + 1
                        errors.append(f'{source.relative_to(root)}:{line}: platform dependency {match.group()}')
    retired_server = root / RETIRED_SERVER_PROJECT.parent
    if retired_server.exists():
        errors.append('retired project remains: src/Server')
    for scan_root in (root / 'src', root / 'tests' / 'Tests', root / 'tools' / 'nettest'):
        if not scan_root.is_dir():
            continue
        for source in sorted(scan_root.rglob('*.cs')):
            if {'obj', 'bin'}.intersection(source.relative_to(root).parts):
                continue
            code = module.mask_non_code(source.read_text(encoding='utf-8-sig'))
            for match in RETIRED_SERVER_SYMBOLS.finditer(code):
                line = code.count('\n', 0, match.start()) + 1
                errors.append(f'{source.relative_to(root)}:{line}: retired server symbol {match.group()}')
    for retired in ['src/MphRead/MphRead.csproj', 'src/MphRead.Android/MphRead.Android.csproj', 'src/NcsfPlay/NcsfPlay.csproj']:
        if (root / retired).exists():
            errors.append(f'retired project remains: {retired}')
    return sorted(set(errors))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    try:
        errors = inspect(args.root.resolve())
    except (OSError, ValueError, ET.ParseError) as error:
        print(f'project boundaries: {error}', file=sys.stderr)
        return 2
    for error in errors:
        print(error)
    print(f'project boundaries: {len(errors)} violation(s)')
    return int(bool(errors))

if __name__ == '__main__':
    sys.exit(main())
