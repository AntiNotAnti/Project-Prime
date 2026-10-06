"""Select original GLB whole components before Blender seam/weld changes."""
from collections import defaultdict
from itertools import product
import math
import numpy as np
from glb import load,read
from alternate_components import component_keys

def original_components(cfg,source):
    doc,blob=load(source);objects={doc['meshes'][n['mesh']]['name']:n['name'] for n in doc['nodes'] if 'mesh'in n};result={}
    for mesh in doc['meshes']:
        if mesh['name'] not in cfg['sourceMeshes']:continue
        obj=objects[mesh['name']];points=[];faces=[]
        for pr in mesh['primitives']:
            start=len(points);points+=read(doc,blob,pr['attributes']['POSITION']);faces.extend(tuple(start+int(v) for v in f) for f in np.array(read(doc,blob,pr['indices'])).reshape(-1,3))
        selections={}
        for oi,override in enumerate(cfg.get('geometryOverrides',[])):
            if override.get('object',obj)==obj and 'component'in override:
                spec=dict(override['component']);spec['center']=spec.pop('centerGame');selections[oi]=component_keys(points,faces,spec)
        result[obj]={'points':np.array(points),'selections':selections}
    return result

def imported_vertex_keys(points_blender,original):
    # GLB Y-up to Blender Z-up is baked by the importer. Normal/UV splits can
    # alter indexed welding around quantization boundaries, so match the raw
    # authored coordinate instead of re-counting imported connected pieces.
    cell=1e-5;grid=defaultdict(list);source=original['points']
    def key(p):return tuple(math.floor(float(v)/cell) for v in p)
    for i,p in enumerate(source):grid[key(p)].append(i)
    result=[];maximum=0.
    for value in points_blender:
        p=np.array([value[0],value[2],-value[1]],float);k=key(p);near=[]
        for delta in product([-1,0,1],repeat=3):near.extend(grid.get(tuple(x+y for x,y in zip(k,delta)),[]))
        assert near,'Imported Source vertex has no raw GLB coordinate';best=min(near,key=lambda i:float(np.linalg.norm(source[i]-p)));error=float(np.linalg.norm(source[best]-p));assert error<1e-6,('Imported Source position changed',error,p.tolist());maximum=max(maximum,error);result.append(tuple(round(float(v),6) for v in source[best]))
    return result,maximum
