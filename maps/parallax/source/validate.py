#!/usr/bin/env python3
"""Static guardrails against the compiled BSP. Does not certify live movement/balance."""
import itertools
import json
import math
from pathlib import Path
import struct
import zipfile

ROOT = Path(__file__).resolve().parent.parent

def dot(a, b):
    return sum(x*y for x, y in zip(a, b))

def sub(a, b):
    return tuple(x-y for x, y in zip(a, b))

def add(a, b):
    return tuple(x+y for x, y in zip(a, b))

def mul(a, s):
    return tuple(x*s for x in a)

def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])

class World:
    def __init__(self, bsp):
        assert bsp[:8] == b'IBSP.\x00\x00\x00', 'Expected IBSP 46'
        def lump(n):
            offset, length = struct.unpack_from('<ii', bsp, 8+n*8)
            return bsp[offset:offset+length]
        planes = [(x, z, -y, d/32) for x,y,z,d in struct.iter_unpack('<4f', lump(2))]
        sides = list(struct.iter_unpack('<ii', lump(9)))
        brushes = list(struct.iter_unpack('<iii', lump(8)))
        model = struct.unpack_from('<6f4i', lump(7))
        self.brushes = []
        self.vertices = []
        self.bounds = []
        for start, count, _ in brushes[model[8]:model[8]+model[9]]:
            ps = [planes[sides[i][0]] for i in range(start, start+count)]
            vs = []
            for a,b,c in itertools.combinations(ps, 3):
                det = dot(a[:3], cross(b[:3], c[:3]))
                if abs(det) < 1e-6:
                    continue
                v = mul(add(add(mul(cross(b[:3],c[:3]), a[3]),
                                mul(cross(c[:3],a[:3]), b[3])),
                            mul(cross(a[:3],b[:3]), c[3])), 1/det)
                if all(dot(p[:3],v) <= p[3]+.003 for p in ps):
                    vs.append(v)
            assert vs, 'Empty collision brush'
            self.brushes.append(ps)
            self.vertices.append(vs)
            self.bounds.append(tuple((min(v[i] for v in vs),max(v[i] for v in vs)) for i in range(3)))

    def trace(self, a, b, extent=(0,0,0)):
        """First solid hit fraction, clipping a segment against convex brush planes."""
        delta = sub(b,a)
        first = 1.0
        for ps, bounds in zip(self.brushes, self.bounds):
            if any(max(a[i],b[i])+extent[i] < bounds[i][0]-.001 or
                   min(a[i],b[i])-extent[i] > bounds[i][1]+.001 for i in range(3)):
                continue
            lo,hi = 0.,first
            for p in ps:
                dist = p[3]+sum(abs(p[i])*extent[i] for i in range(3))-dot(p[:3],a)
                denom = dot(p[:3],delta)
                if abs(denom)<1e-9:
                    if dist<-.001:
                        break
                elif denom>0:
                    hi=min(hi,dist/denom)
                else:
                    lo=max(lo,dist/denom)
                if lo>hi:
                    break
            else:
                if hi>=0 and lo<=first:
                    first=max(0,lo)
        return first

    def clear(self, foot, radius=.4, height=1.6):
        center=add(foot,(0,height/2,0))
        return self.trace(center,center,(radius,height/2,radius)) == 1


def validate():
    d=json.loads((ROOT/'parallax.json').read_text())
    with zipfile.ZipFile(ROOT/'parallax.pk3') as archive:
        bsp=archive.read('maps/parallax.bsp')
        assert all(f'textures/parallax/{name}.tga' in archive.namelist() for name in
                   ('floor','wall','trim','accent','upper','lower','boost','climb','spawn','pad'))
    w=World(bsp)
    assert d['import']['unitsPerUnit']==32 and d['scaleFactor']==4
    assert d['import']['keepSpawns'] is False and d['import']['keepClip'] is True
    assert d['import']['keepSky'] is False and not d.get('brushes')
    assert len(d['spawns'])==4 and len(d['jumpPads'])==2
    assert sorted(i['type'] for i in d['items'])==sorted(['AffinityWeapon']+['HealthMedium']*2+['UASmall']*4+['MissileSmall']*2)
    assert d['items'][0]['position']==[0,.6,0]
    # Compiled brush geometry itself must have a rotational counterpart.
    def signature(points):
        return tuple(sorted(set(tuple(round(v,2) for v in p) for p in points)))
    shapes={signature(v) for v in w.vertices}
    assert all(signature([(-x,y,-z) for x,y,z in vs]) in shapes for vs in w.vertices), 'Asymmetric geometry'
    for key in ('spawns','jumpPads','items'):
        for e in d[key]:
            x,y,z=e['position']
            matches=[o for o in d[key] if math.dist(o['position'],(-x,y,-z))<.001 and o.get('type')==e.get('type')]
            assert matches, f'Unpaired {key}: {e}'
            if key=='items':
                assert matches[0]['spawnInterval']==e['spawnInterval']
            if key=='jumpPads':
                tx,ty,tz=e['target']
                assert math.dist(matches[0]['target'],(-tx,ty,-tz))<.001
            assert w.clear(e['position'],.35,1.6 if key=='spawns' else .5), f'Embedded {key}: {e}'
            x,y,z=e['position']
            t=w.trace((x,y,z),(x,y-2,z))
            assert t<1, f'Unsupported {key}: {e}'
    for a,b in itertools.combinations(d['spawns'],2):
        for eye in (.6,1.4,2):
            assert w.trace(add(a['position'],(0,eye,0)),add(b['position'],(0,eye,0)))<1, 'Spawn-to-spawn LOS'
    # Sample the entire usable upper deck, rather than just its central camera.
    for x in range(-13,6):
        for z in range(15,22):
            a=(x,7.4,z)
            if not w.clear((x,6.1,z)) or w.trace((x,6.1,z),(x,5.9,z))==1:
                continue
            for spawn in d['spawns']:
                assert w.trace(a,add(spawn['position'],(0,1.4,0)))<1, f'Deck sees spawn from {a}'
    for spawn in d['spawns']:
        assert all(math.dist(spawn['position'],item['position'])>4 for item in d['items']), 'Spawn pickup too close'
    # Two clear, supported exits from each covered spawn, through the vestibule.
    for spawn in d['spawns']:
        x,_,z=spawn['position'];r=math.hypot(x,z);n=(x/r,0,z/r);t=(-n[2],0,n[0])
        for side in (-1,1):
            route=[add(mul(n,r),(0,.1,0)),add(mul(n,23),(0,.1,0)),add(add(mul(n,23),mul(t,side*8)),(0,.1,0))]
            for a,b in zip(route,route[1:]):
                assert w.trace(add(a,(0,.8,0)),add(b,(0,.8,0)),(.35,.8,.35))==1, 'Blocked spawn exit'
    for angle in range(360):
        a=math.radians(angle);p=(24*math.cos(a),.1,24*math.sin(a))
        assert w.clear(p,.35,1.6), f'Boost arc obstruction {angle}'
        assert w.trace(p,add(p,(0,-.2,0)))<1, f'Boost arc floor gap {angle}'
    # One intended 29.7-unit diagonal; its ends and both axial 50-unit views are blocked.
    assert w.trace((-10.5,1.4,-10.5),(10.5,1.4,10.5))==1, 'Duel lane blocked'
    assert w.trace((-12,1.4,-12),(12,1.4,12))<1, 'Duel end cover missing'
    for a,b in [((-25,1.4,0),(25,1.4,0)),((0,1.4,-25),(0,1.4,25))]:
        assert w.trace(a,b)<1, 'Unintended axial sightline'
    for pad in d['jumpPads']:
        delta=sub(pad['target'],pad['position']);h=math.hypot(delta[0],delta[2]);g=77/4096
        rise=max(delta[1],0)+max(2,h*.22);up=math.sqrt(2*g*rise)
        frames=(up+math.sqrt(2*g*max(rise-delta[1],.01)))/g
        points=[]
        for i in range(121):
            f=frames*i/120
            foot=add(pad['position'],(delta[0]*f/frames,up*f-g*f*f/2,delta[2]*f/frames))
            assert w.clear(foot,.45,1.7), f'Jump arc collision at {foot}'
            points.append(foot)
        assert w.trace(pad['target'],add(pad['target'],(0,-1,0)))<1, 'Pad landing unsupported'
    # Continuous paths on the actual BSP: ramps, exposed climb faces and boost arcs.
    for sign in (1,-1):
        for i in range(81):
            z=i/5; foot=(-11*sign,.17+z*6/16,z*sign)
            assert w.clear(foot,.28,1.6), f'Upper ramp obstruction at {foot}'
        for i in range(51):
            x=12+i/5;foot=(x*sign,-3.83+(x-12)*.4,7*sign)
            assert w.clear(foot,.25,1.6), f'Lower ramp obstruction at {foot}'
        for i in range(61):
            y=i/10;foot=(-7.5*sign,y+.05,15*sign)
            assert w.clear(foot,.35,1.6), f'Climb obstruction at {foot}'
    print(f'PASS: {len(w.brushes)} compiled brushes; rotational symmetry; exact item economy;')
    print('spawn/body/item clearance and support; no spawn-to-spawn or deck-to-spawn LOS;')
    print('two exits per spawn; continuous supported perimeter arc; bounded diagonal duel lane;')
    print('two swept jump arcs and supported landings; paired ramps and climb approach clearance.')
    print('Static BSP checks only. Live Hunter traversal, timing and competitive balance need playtesting.')

if __name__=='__main__':
    validate()
