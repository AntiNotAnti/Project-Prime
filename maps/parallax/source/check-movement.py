#!/usr/bin/env python3
"""Run focused CPU movement checks using the checkout's existing headless scene API."""
import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile
from xml.sax.saxutils import escape


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-dir', required=True, type=Path)
    parser.add_argument('--data-dir', required=True, type=Path,
                        help='Extracted AMHE1 directory with PARALLAX already generated')
    parser.add_argument('--map-dir', required=True, type=Path)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    game, data, maps = (p.resolve() for p in (args.game_dir, args.data_dir, args.map_dir))
    for name in ('FruityPrime.dll', 'FruityPrime.runtimeconfig.json', 'FruityPrime.deps.json'):
        if not (game / name).is_file():
            parser.error(f'Missing {game / name}')
    with tempfile.TemporaryDirectory(prefix='parallax-movement-') as temporary:
        root = Path(temporary)
        shutil.copyfile(Path(__file__).with_name('movement-check.cs'), root / 'Program.cs')
        references = ''.join(
            f'<Reference Include="{name}"><HintPath>{escape(str(game / (name + ".dll")))}</HintPath></Reference>'
            for name in ('FruityPrime', 'OpenTK.Mathematics'))
        (root / 'Check.csproj').write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
            '<TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>'
            '</PropertyGroup><ItemGroup>' + references + '</ItemGroup></Project>')
        output = root / 'run'
        subprocess.run([args.dotnet, 'build', str(root / 'Check.csproj'), '-c', 'Release',
                        '-o', str(output)], check=True)
        # Match the selected game build's dependency closure without modifying it.
        for assembly in game.glob('*.dll'):
            shutil.copyfile(assembly, output / assembly.name)
        subprocess.run([args.dotnet, 'exec', '--runtimeconfig', str(game / 'FruityPrime.runtimeconfig.json'),
                        '--depsfile', str(game / 'FruityPrime.deps.json'), str(output / 'Check.dll'),
                        str(game), str(data), str(maps)], check=True)


if __name__ == '__main__':
    main()
