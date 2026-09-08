#!/usr/bin/env python3
"""Author PARALLAX's original 64px geometric materials; requires Pillow.

The generated stone master is only resized/converted for the game pipeline.
All other materials are drawn from coordinates, not edits of generated art.
"""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
OUT = ROOT.parent / 'textures/parallax'


def material(name, base, edge, metal, light, motif):
    image = Image.new('RGB', (64, 64), base)
    draw = ImageDraw.Draw(image)
    # Broad chamfered slabs. Quiet interior preserves distant player silhouettes.
    outline = [(9, 1), (54, 1), (62, 9), (62, 54), (54, 62),
               (9, 62), (1, 54), (1, 9), (9, 1)]
    draw.line(outline, fill=edge, width=2)
    draw.line([(10, 4), (53, 4), (59, 10)], fill=metal, width=1)
    draw.line([(4, 10), (4, 53), (10, 59)], fill=metal, width=1)
    if motif == 'floor':
        for x, y in [(13, 13), (50, 50)]:
            draw.rectangle((x, y, x+1, y+1), fill=metal)
    elif motif == 'lower':
        draw.line([(16, 31), (23, 31), (23, 35), (40, 35), (40, 31), (47, 31)], fill=metal, width=2)
    elif motif == 'upper':
        for y in [25, 37]:
            draw.line([(19, y+5), (31, y-3), (43, y+5)], fill=metal, width=3)
    elif motif == 'boost':
        for x in [12, 49]:
            draw.line([(x, 0), (x, 63)], fill=metal, width=2)
        draw.line([(24, 41), (31, 31), (38, 41)], fill=light, width=2)
    elif motif == 'climb':
        for y in [16, 32, 48]:
            draw.line([(19, y+3), (31, y-5), (43, y+3)], fill=metal, width=2)
    elif motif == 'spawn':
        draw.line([(18, 41), (18, 25), (25, 18), (38, 18), (45, 25), (45, 41)], fill=metal, width=3)
        draw.rectangle((29, 29, 34, 34), fill=light)
    elif motif == 'pad':
        for r, color in [(23, metal), (16, light), (8, metal)]:
            draw.line([(31, 31-r), (31+r, 31), (31, 31+r), (31-r, 31), (31, 31-r)], fill=color, width=2)
    elif motif == 'accent':
        draw.line([(13, 0), (13, 17), (22, 17), (22, 25), (31, 25), (31, 38), (41, 38), (41, 47), (50, 47), (50, 63)], fill=metal, width=5)
        draw.line([(13, 0), (13, 17), (22, 17), (22, 25), (31, 25), (31, 38), (41, 38), (41, 47), (50, 47), (50, 63)], fill=light, width=2)
        draw.line([(50, 0), (50, 12), (41, 12), (41, 20)], fill=metal, width=2)
        draw.line([(13, 63), (13, 51), (22, 51), (22, 43)], fill=metal, width=2)
    image.save(OUT / f'{name}.tga', compression=None)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    # Colors remain distinct after the runtime's 5-bit/channel palette bake.
    material('floor', (111,108,94), (77,77,67), (139,134,113), None, 'floor')
    material('trim', (77,80,76), (60,63,59), (98,99,85), None, 'trim')
    material('lower', (78,103,111), (57,77,83), (110,139,145), None, 'lower')
    material('upper', (147,119,73), (106,87,55), (192,164,104), None, 'upper')
    material('boost', (75,109,106), (55,78,75), (97,139,131), (148,187,163), 'boost')
    material('climb', (170,160,129), (126,118,96), (208,191,144), None, 'climb')
    material('spawn', (102,120,96), (73,88,68), (146,163,117), (177,194,151), 'spawn')
    material('pad', (65,93,96), (47,66,69), (134,147,106), (116,214,197), 'pad')
    material('accent', (55,88,89), (41,62,62), (119,137,111), (121,209,191), 'accent')
    # Mechanical export only; retain the unmodified AI-generated original.
    with Image.open(ROOT / 'alimbic-stone.png') as master:
        master.convert('RGB').resize((64, 64), Image.Resampling.LANCZOS).save(OUT / 'wall.tga')
    print('Wrote ten 64x64 RGB materials.')


if __name__ == '__main__':
    main()
