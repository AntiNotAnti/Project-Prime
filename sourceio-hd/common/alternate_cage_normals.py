"""Restore raw Source normals after unchanged strict Weighted4 export.

Blender can alter normals around coincident zero-area faces and their neighbors.
Retain every face and copy generated positions, UVs, skin and winding exactly.
"""
import json,struct,math
from pathlib import Path
from collections import defaultdict
import numpy as np
from glb import load,read
from alternate_components import component_keys

def restore(root,cfg,source):
    from alternate_cage_geometry import fitted_triangles
    root=Path(root);path=root/'kit/starter'/cfg['hunter'].lower()/'altform_weighted4.glb';doc,binary=load(path);blob=bytearray(binary);atlas=json.loads((root/'atlas-layout.json').read_text())['materials'];contract,frames,triangles,proof=fitted_triangles(cfg,source,root/'kit',atlas);names=[doc['nodes'][i]['name'] for i in doc['skins'][0]['joints']];expected=defaultdict(list);source_count=len(triangles);degenerate_source_count=0
    for t in triangles:
        expected[t['material']].append(dict(t,weights=np.array([[w.get(n,0) for n in names] for w in t['weights']])))
        p=t['positions'];degenerate_source_count+=np.linalg.norm(np.cross(p[1]-p[0],p[2]-p[0]))<1e-12
    def add(values,kind,ctype):
        values=np.array(values);width={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[kind];code={5121:'B',5123:'H',5125:'I',5126:'f'}[ctype]
        while len(blob)%4:blob.append(0)
        payload=struct.pack('<'+code*width*len(values),*values.reshape(-1).tolist());view=len(doc['bufferViews']);doc['bufferViews'].append({'buffer':0,'byteOffset':len(blob),'byteLength':len(payload)});blob.extend(payload);ix=len(doc['accessors']);access={'bufferView':view,'componentType':ctype,'count':len(values),'type':kind}
        if kind=='VEC3':access.update(min=values.min(0).tolist(),max=values.max(0).tolist())
        doc['accessors'].append(access);return ix
    def key(p,u):return tuple(sorted(tuple(round(float(v),5) for v in (*a,*b)) for a,b in zip(p,u)))
    prepared={}
    for mat,ts in expected.items():
        buckets=defaultdict(list);cells=defaultdict(list)
        for ti,t in enumerate(ts):buckets[key(t['positions'],t['uvs'])].append(ti);cells[tuple(math.floor(float(v)/3e-5) for v in t['positions'].mean(0))].append(ti)
        prepared[mat]=(ts,buckets,cells,set())
    maximum_pos=maximum_uv=maximum_weight=0.;restored=0;indexed_records=0
    for mesh in doc['meshes']:
        for pr in mesh['primitives']:
            mat=doc['materials'][pr['material']]['name']
            if mat not in expected:continue
            ts,buckets,cells,consumed=prepared[mat];attrs={n:np.array(read(doc,binary,i)) for n,i in pr['attributes'].items()};new={n:[] for n in attrs};indices=np.array(read(doc,binary,pr['indices'])).reshape(-1,3)
            for face in indices:
                positions=attrs['POSITION'][face];uvs=attrs['TEXCOORD_0'][face];weight=np.zeros((3,len(names)))
                for k,vi in enumerate(face):
                    for j,w in zip(attrs['JOINTS_0'][vi],attrs['WEIGHTS_0'][vi]):weight[k,j]+=w
                near=[];cell=tuple(math.floor(float(v)/3e-5) for v in positions.mean(0))
                for x in [-1,0,1]:
                    for y in [-1,0,1]:
                        for z in [-1,0,1]:near.extend(i for i in cells.get((cell[0]+x,cell[1]+y,cell[2]+z),[]) if i not in consumed)
                scores=[]
                for ti in near:
                    t=ts[ti]
                    for shift in range(3):
                        order=np.roll(np.arange(3),shift);pe=float(np.linalg.norm(positions-t['positions'][order],axis=1).max());ue=float(np.max(abs(uvs-t['uvs'][order])));we=float(np.max(abs(weight-t['weights'][order])));scores.append((pe+ue+we,ti,order,pe,ue,we))
                assert scores,('No raw Source triangle for exported surface',mat,positions.tolist(),uvs.tolist())
                score,ti,order,pe,ue,we=min(scores,key=lambda s:s[0]);assert pe<1e-5 and ue<2e-6 and we<2e-6,('Generated Source geometry/UV/weights/winding changed',mat,pe,ue,we);consumed.add(ti);t=ts[ti];maximum_pos=max(maximum_pos,pe);maximum_uv=max(maximum_uv,ue);maximum_weight=max(maximum_weight,we)
                for k,vi in enumerate(face):
                    for name,values in attrs.items():new[name].append(t['normals'][order[k]] if name=='NORMAL' else values[vi])
                restored+=3
            # Weld only byte-identical complete attribute records, including the
            # restored normal. This retains independently authored seam normals
            # while avoiding unnecessary mobile vertex residency.
            metadata={name:dict(doc['accessors'][pr['attributes'][name]]) for name in new};lookup={};welded={name:[] for name in new};out_indices=[]
            for i in range(len(new['POSITION'])):
                chunks=[]
                for name,values in new.items():
                    access=metadata[name];code={5121:'B',5123:'H',5125:'I',5126:'f'}[access['componentType']];chunks.append(struct.pack('<'+code*len(values[i]),*values[i]))
                record=b''.join(chunks)
                if record not in lookup:
                    lookup[record]=len(welded['POSITION'])
                    for name,values in new.items():welded[name].append(values[i])
                out_indices.append(lookup[record])
            for name,values in welded.items():access=metadata[name];pr['attributes'][name]=add(values,access['type'],access['componentType'])
            pr['indices']=add(np.array(out_indices).reshape(-1,1),'SCALAR',5125);indexed_records+=len(welded['POSITION'])
    assert all(len(consumed)==len(ts) for ts,b,c,consumed in prepared.values()),'Generated exporter omitted Source triangles';assert restored==source_count*3
    while len(blob)%4:blob.append(0)
    doc['buffers'][0]['byteLength']=len(blob);encoded=json.dumps(doc,separators=(',',':')).encode();encoded+=b' '*(-len(encoded)%4);path.write_bytes(struct.pack('<4sII',b'glTF',2,28+len(encoded)+len(blob))+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(blob),0x004e4942)+blob)
    receipt={'pass':True,'method':'Original GLB authored normals transformed by one uniform conversion fit; continuous barycentric native skin independently reconstructed; generated position/UV/skin/winding exact copies; only byte-identical complete records welded','sourceTriangles':source_count,'sourceDegenerateRestTrianglesRetained':int(degenerate_source_count),'authoredSourceNormalCornersRestored':restored,'finalIndexedSourceRecords':indexed_records,'maximumRawSourceGeneratedPositionError':maximum_pos,'maximumRawSourceGeneratedUvError':maximum_uv,'maximumRawSourceGeneratedWeightError':maximum_weight,'nativeEffectPayloadUnchanged':True,'generatedExporterUnchanged':True};(root/'raw-source-normal-restoration.json').write_text(json.dumps(receipt,indent=2)+'\n');return receipt
