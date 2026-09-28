"""Build original, transparent armor markings; no extracted game art is stored.
Coordinates are authored on a 128-unit UV sheet. Run from any directory.
"""
from pathlib import Path
import struct, zlib, binascii
ROOT = Path(__file__).resolve().parents[2] / 'src/MphRead/Assets/Cosmetics/Decals'
COLORS = {'Samus': (93,218,238), 'Kanden': (185,235,100), 'Trace': (244,109,87),
          'Sylux': (91,195,255), 'Noxus': (177,155,255), 'Spire': (255,187,82), 'Weavel': (123,227,190)}
# Deliberately different insignia, not random/noise textures.
CRESTS = [ [(48,58),(64,42),(80,58),(64,86),(48,58)],
           [(46,48),(64,62),(82,48),(74,80),(54,80),(46,48)],
           [(44,82),(64,42),(84,82),(64,70),(44,82)],
           [(48,44),(80,44),(64,62),(80,84),(48,84),(64,62),(48,44)],
           [(64,42),(84,64),(64,86),(44,64),(64,42)],
           [(46,80),(46,54),(64,42),(82,54),(82,80),(46,80)],
           [(44,48),(84,48),(72,64),(84,80),(44,80),(56,64),(44,48)] ]
def chunk(tag, data):
    return struct.pack('>I', len(data))+tag+data+struct.pack('>I', binascii.crc32(tag+data)&0xffffffff)
def save(path, pixels):
    path.parent.mkdir(parents=True, exist_ok=True)
    raw=b''.join(b'\0'+bytes(pixels[y*128*4:(y+1)*128*4]) for y in range(128))
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',128,128,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(raw,9))+chunk(b'IEND',b''))
for h,(hunter,color) in enumerate(COLORS.items()):
    for skin in ('Obsidian','Alimbic'):
        pixels=bytearray(128*128*4)
        def line(points, rgba, width=1):
            for (x0,y0),(x1,y1) in zip(points,points[1:]):
                steps=max(abs(x1-x0),abs(y1-y0),1)
                for t in range(steps+1):
                    x=round(x0+(x1-x0)*t/steps); y=round(y0+(y1-y0)*t/steps)
                    for dy in range(-(width//2),width-width//2):
                        for dx in range(-(width//2),width-width//2):
                            if 0<=x+dx<128 and 0<=y+dy<128:
                                i=((y+dy)*128+x+dx)*4; pixels[i:i+4]=bytes(rgba)
        if skin=='Obsidian':
            # Broken technical panel seams and a double racing stripe.
            for x in (12,112):
                line([(x,0),(x,28),(x+4,32),(x+4,96),(x,100),(x,127)], (185,205,218,160))
            line([(24,0),(24,36),(32,44),(32,100),(24,108),(24,127)], (*color,210),2)
            line([(29,0),(29,31),(37,39),(37,96)], (*color,110))
            line(CRESTS[h], (*color,210),2)
            for y in (18,110): line([(48,y),(76,y)],(206,222,230,150))
        else:
            # Angular inlaid circuit channels and a framed hunter crest.
            for y in (12,108):
                line([(0,y),(36,y),(44,y+8),(84,y+8),(92,y),(127,y)], (231,188,104,205),2)
            line([(8,32),(28,32),(36,40),(36,88),(28,96),(8,96)],(88,241,210,200),2)
            line([(120,32),(100,32),(92,40),(92,88),(100,96),(120,96)],(88,241,210,200),2)
            line(CRESTS[h],(110,255,225,225),2)
            line([(58,64),(64,58),(70,64),(64,70),(58,64)],(248,220,157,210))
        save(ROOT/hunter/(skin+'.png'), pixels)
print('Wrote 14 original decal sheets to',ROOT)
