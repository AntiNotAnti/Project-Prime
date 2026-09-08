#!/usr/bin/env python3
"""Bake original textures and package the existing BSP without a game installation.

Uses the repository's texture baker and the documented MapBundle ZIP layout.
The full BSP is retained; the game's cooker optionally trims unused lumps.
Requires Pillow. This does not generate room binaries or run a game smoke test.
"""
import json
from pathlib import Path
import subprocess
import sys
import zipfile


def main():
    source = Path(__file__).resolve().parents[1]
    level = source.parent
    repo = source.parents[2]
    subprocess.run([sys.executable, str(source / 'build.py'), '--materials-only'], check=True)
    pack = level / 'parallax.pk3'
    textures = level / 'parallax.tex'
    subprocess.run([sys.executable, str(repo / 'tools/bake-textures.py'),
                    str(pack), 'parallax', str(pack), str(textures)], check=True)
    definition = json.loads((level / 'parallax.json').read_text())
    definition['import']['source'] = 'maps/parallax.bsp'
    definition['import']['textures'] = 'parallax.tex'
    with zipfile.ZipFile(pack) as archive:
        bsp = archive.read('maps/parallax.bsp')
    output = level.parent / 'PARALLAX.fpmap'
    temporary = output.with_suffix('.fpmap.tmp')
    entries = {'parallax.json': (json.dumps(definition, indent=2)+'\n').encode(),
               'maps/parallax.bsp': bsp, 'parallax.tex': textures.read_bytes()}
    try:
        with zipfile.ZipFile(temporary, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
            for name, data in entries.items():
                info = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                info.external_attr = 0o644 << 16
                archive.writestr(info, data)
        temporary.replace(output)
    finally:
        temporary.unlink(missing_ok=True)
    print(f'Packaged {output} ({output.stat().st_size:,} bytes). Runtime smoke test still required.')


if __name__ == '__main__':
    main()
