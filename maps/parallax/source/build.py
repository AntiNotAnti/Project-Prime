#!/usr/bin/env python3
"""Compile the editable Q3 map and package its original assets (Python 3 only)."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--q3map2', default=os.environ.get('Q3MAP2', 'q3map2'))
    args = parser.parse_args()
    source = Path(__file__).resolve().parent
    output = source.parent / 'parallax.pk3'
    compiler = shutil.which(args.q3map2)
    if not compiler:
        parser.error('q3map2 not found; pass --q3map2 /path/to/q3map2')
    with tempfile.TemporaryDirectory(prefix='parallax-build-') as temporary:
        root = Path(temporary)
        game = root / 'baseq3'
        for folder in ('maps', 'scripts', 'textures'):
            shutil.copytree(source / folder, game / folder)
        subprocess.run([compiler, '-game', 'quake3', '-fs_basepath', str(root),
                        '-fs_game', 'baseq3', '-meta',
                        str(game / 'maps/parallax.map')], check=True)
        bsp = game / 'maps/parallax.bsp'
        if not bsp.is_file() or bsp.read_bytes()[:8] != b'IBSP.\x00\x00\x00':
            raise RuntimeError('Compiler did not produce an IBSP 46 level')
        if (game / 'maps/parallax.lin').exists():
            raise RuntimeError('World shell leaks; refusing to package')
        packed = root / 'parallax.pk3'
        with zipfile.ZipFile(packed, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
            files = [bsp] + sorted((game / 'scripts').rglob('*')) + sorted((game / 'textures').rglob('*'))
            for path in files:
                if path.is_file():
                    entry = zipfile.ZipInfo(path.relative_to(game).as_posix(), (2026, 1, 1, 0, 0, 0))
                    entry.compress_type = zipfile.ZIP_DEFLATED
                    entry.external_attr = 0o644 << 16
                    archive.writestr(entry, path.read_bytes())
        shutil.copyfile(packed, output)
    # The baked texture indices belong to this BSP, and must be regenerated.
    (source.parent / 'parallax.tex').unlink(missing_ok=True)
    print(f'Built {output} ({output.stat().st_size:,} bytes). Run FruityPrime -mapgen PARALLAX.')


if __name__ == '__main__':
    main()
