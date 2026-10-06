"""Restore authored corner normals/colors after the unchanged rigid validator.

Generated positions, UVs and winding are copied exactly. Splitting records per
triangle corner preserves Source seam normals without changing any surface.
"""
import json,struct,math
from pathlib import Path
from collections import defaultdict
import numpy as np
from glb import load,read

def write(path,doc,blob):
    while len(blob)%4:blob.append(0)
    doc['buffers'][0]['byteLength']=len(blob);encoded=json.dumps(doc,separators=(',',':')).encode();encoded+=b' '*(-len(encoded)%4)
    Path(path).write_bytes(struct.pack('<4sII',b'glTF',2,28+len(encoded)+len(blob))+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(blob),0x004e4942)+blob)

def restore(path,triangles):
    doc,binary=load(path);blob=bytearray(binary);groups=defaultdict(list)
    for t in triangles:groups[t['node'],t['material']].append(t)
    def add(values,kind,fmt):
        while len(blob)%4:blob.append(0)
        width={'VEC3':3,'VEC2':2,'SCALAR':1}[kind];values=np.asarray(values);payload=struct.pack('<'+fmt*width*len(values),*values.reshape(-1).tolist());view=len(doc['bufferViews']);doc['bufferViews'].append({'buffer':0,'byteOffset':len(blob),'byteLength':len(payload)});blob.extend(payload);ix=len(doc['accessors']);access={'bufferView':view,'componentType':5126 if fmt=='f' else 5125,'type':kind,'count':len(values)}
        if kind=='VEC3':access.update(min=values.min(0).tolist(),max=values.max(0).tolist())
        doc['accessors'].append(access);return ix
    def key(points,uvs):return tuple(sorted(tuple(round(float(v),5) for v in (*p,*u)) for p,u in zip(points,uvs)))
    maximum=0.;source_corners=effect_corners=0;seen=set()
    for node in doc['nodes']:
        if 'mesh' not in node:continue
        assert node.get('matrix') is None and node.get('translation',[0,0,0])==[0,0,0] and node.get('scale',[1,1,1])==[1,1,1],'Rigid output mesh node transform must be identity'
        for pr in doc['meshes'][node['mesh']]['primitives']:
            material=doc['materials'][pr['material']]['name'];pair=node['name'],material;ts=groups[pair];seen.add(pair);buckets=defaultdict(list);cells=defaultdict(list)
            for ti,t in enumerate(ts):buckets[key(t['positions'],t['uvs'])].append(ti);cells[tuple(math.floor(float(v)/3e-5) for v in t['positions'].mean(0))].append(ti)
            consumed=set();pos=np.array(read(doc,binary,pr['attributes']['POSITION']));uv=np.array(read(doc,binary,pr['attributes']['TEXCOORD_0']));norm=np.array(read(doc,binary,pr['attributes']['NORMAL']));indices=np.array(read(doc,binary,pr['indices'])).reshape(-1);outp=[];outu=[];outn=[];outc=[]
            for face in indices.reshape(-1,3):
                kp=key(pos[face],uv[face]);near=[]
                # Rounded exact keys may contain an opposite-side coating but
                # omit the correct orientation just across a rounding boundary.
                center=pos[face].mean(0);cell=tuple(math.floor(float(v)/3e-5) for v in center)
                for x in [-1,0,1]:
                    for y in [-1,0,1]:
                        for z in [-1,0,1]:near.extend(i for i in cells.get((cell[0]+x,cell[1]+y,cell[2]+z),[]) if i not in consumed)
                scored=[]
                for ti in near:
                    t=ts[ti]
                    # Preserve oriented triangle correspondence. Opposite-side
                    # glow layers can share every position and UV while keeping
                    # opposite normals; unordered matching swaps those layers.
                    for shift in range(3):
                        order=np.roll(np.arange(3),shift);pe=float(np.max(np.linalg.norm(pos[face]-t['positions'][order],axis=1)));ue=float(np.max(np.abs(uv[face]-t['uvs'][order])));ne=float(np.max(np.linalg.norm(norm[face]-t.get('normals',norm[face])[order],axis=1)));scored.append((pe+ue,ne,ti,order,pe,ue))
                assert scored,('No original rigid triangle',pair,kp)
                valid=[s for s in scored if s[4]<1e-5 and s[5]<2e-6];assert valid,('Generated Source/effect oriented triangle changed',pair,min(s[0] for s in scored));score,ne,ti,order,pe,ue=min(valid,key=lambda s:(s[0],s[1],s[2]));consumed.add(ti);t=ts[ti]
                for i,corner in zip(face,order):
                    maximum=max(maximum,float(np.linalg.norm(pos[i]-t['positions'][corner])));outp.append(pos[i]);outu.append(uv[i]);outn.append(t.get('normals',norm[face])[corner]);outc.append(t.get('colors',np.ones((3,3)))[corner])
                if t['nativeEffect']:effect_corners+=3
                else:source_corners+=3
            assert len(consumed)==len(ts),('Generated helper dropped rigid triangles',pair,len(consumed),len(ts));pr['attributes']={'POSITION':add(outp,'VEC3','f'),'TEXCOORD_0':add(outu,'VEC2','f'),'NORMAL':add(outn,'VEC3','f')};pr['indices']=add(np.arange(len(outp)).reshape(-1,1),'SCALAR','I')
            if any(t['nativeEffect'] for t in ts):pr['attributes']['COLOR_0']=add(outc,'VEC3','f')
    assert seen==set(groups) and maximum<1e-5
    write(path,doc,blob)
    return {'pass':True,'maximumGeneratedPositionError':maximum,'sourceNormalCornersRestored':source_corners,'nativeEffectColorCornersRestored':effect_corners,'generatedPositionsUvsWindingPreserved':True,'generatedExporterUnchanged':True}
