#!/usr/bin/env python3
"""Compile an original asset-free sealed graybox with a real local Q3Map2.
No GUI, physical-device, live gameplay, or release certification is implied.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import time


def box(lo, hi):
    x, y, z = lo; X, Y, Z = hi
    p = [(x,y,z),(X,y,z),(x,Y,z),(X,Y,z),(x,y,Z),(X,y,Z),(x,Y,Z),(X,Y,Z)]
    # Quake PlaneFromPoints uses cross(c-a,b-a), the reverse of the usual cross.
    faces = [(0,2,6),(1,5,7),(0,4,5),(2,3,7),(0,1,3),(4,6,7)]
    return '{\n' + '\n'.join(' '.join('( %d %d %d )' % p[i] for i in face)
        + ' prime/gray 0 0 0 1 1 0 0 0' for face in faces) + '\n}\n'


def fixture(root):
    maps = root / 'baseq3' / 'maps'; maps.mkdir(parents=True)
    textures = root / 'baseq3' / 'textures' / 'prime'; textures.mkdir(parents=True)
    # Original uncompressed 24-bit TGA checkerboard, no third-party media.
    header = struct.pack('<BBBHHBHHHHBB', 0,0,2,0,0,0,0,0,64,64,24,0x20)
    pixels = b''.join(bytes([96 if ((x//8+y//8)%2) else 144])*3 for y in range(64) for x in range(64))
    (textures / 'gray.tga').write_bytes(header + pixels)
    walls = [((-272,-272,-16),(272,272,0)),((-272,-272,256),(272,272,272)),
             ((-272,-272,0),(-256,272,256)),((256,-272,0),(272,272,256)),
             ((-256,-272,0),(256,-256,256)),((-256,256,0),(256,272,256))]
    source = maps / 'prime_acceptance.map'
    source.write_text('{\n"classname" "worldspawn"\n' + ''.join(box(*wall) for wall in walls) + '}\n'
        + '{\n"classname" "info_player_deathmatch"\n"origin" "0 0 32"\n"angle" "90"\n}\n'
        + '{\n"classname" "info_player_deathmatch"\n"origin" "64 64 32"\n"angle" "180"\n}\n')
    return source


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--q3map2',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True)
    a=p.parse_args()
    binary=a.q3map2
    if not binary.is_absolute() or binary.is_symlink() or not binary.is_file():
        p.error('--q3map2 must be an absolute regular local executable')
    output=a.output.resolve()
    if output.exists(): p.error('--output must not exist; fixtures are never overwritten')
    output.mkdir(parents=True)
    source=fixture(output)
    command=[str(binary),'-game','quake3','-fs_basepath',str(output),'-fs_game','baseq3','-meta',str(source)]
    started=time.monotonic()
    with (output/'q3map2.log').open('wb') as log:
        result=subprocess.run(command,stdout=log,stderr=subprocess.STDOUT,timeout=120)
    bsp=source.with_suffix('.bsp')
    valid=False; lumps=[]
    if bsp.is_file():
        data=bsp.read_bytes()
        if len(data)>=144 and data[:4]==b'IBSP' and struct.unpack_from('<I',data,4)[0]==46:
            lumps=[struct.unpack_from('<II',data,8+8*i) for i in range(17)]
            valid=all(offset+size<=len(data) for offset,size in lumps) and lumps[8][1]>0 and lumps[13][1]>0
    receipt={'schema':1,'command':command,'compilerSha256':sha(binary),'sourceSha256':sha(source),
        'elapsedSeconds':time.monotonic()-started,'exitCode':result.returncode,'validBsp46':valid,
        'bspSha256':sha(bsp) if bsp.exists() else None,'lumps':lumps,
        'scope':'real Q3Map2 compile; original sealed graybox',
        'gui':'pending','gameplay':'pending','platforms':['osx-arm64' if __import__('platform').machine()=='arm64' and __import__('platform').system()=='Darwin' else __import__('platform').platform()]}
    (output/'receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
    print(json.dumps(receipt,indent=2))
    if result.returncode!=0 or not valid: raise SystemExit('Real compiler acceptance failed; see q3map2.log')


if __name__=='__main__': main()
