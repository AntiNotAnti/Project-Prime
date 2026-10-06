"""Remove explicitly named tiny unrelated Source props from atlas input only."""
import json,struct,copy
from pathlib import Path
from collections import Counter
import numpy as np
from glb import load,read
def curate(cfg,source,output):
    doc,blob=load(source);chosen=copy.deepcopy(doc);removed={};retained=Counter()
    for m in chosen['meshes']:
        if m['name'] not in cfg['sourceMeshes']:continue
        keep=[]
        for pr in m['primitives']:
            mat=doc['materials'][pr['material']]['name'];ii=np.array(read(doc,blob,pr['indices'])).reshape(-1)
            if mat in cfg.get('excludedSourceMaterials',[]):
                ps=np.array(read(doc,blob,pr['attributes']['POSITION']))[ii];removed[mat]={'triangles':len(ii)//3,'boundsGame':[ps.min(0).tolist(),ps.max(0).tolist()]}
            else:keep.append(pr);retained[mat]+=len(ii)//3
        m['primitives']=keep
    assert {m:v['triangles'] for m,v in removed.items()}==cfg['excludedSourceMaterialTriangles'];assert sum(retained.values())==cfg['expectedTriangles'];encoded=json.dumps(chosen,separators=(',',':')).encode();encoded+=b' '*(-len(encoded)%4);output=Path(output);output.write_bytes(struct.pack('<4sII',b'glTF',2,28+len(encoded)+len(blob))+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(blob),0x004e4942)+blob)
    receipt={'scope':'Atlas input only; original GLB remains authoring/audit authority. Exact named unrelated tiny props excluded from shipping by matching config.','excluded':removed,'retainedMaterialTriangles':dict(retained),'originalBinaryPayloadUnchanged':True};(output.parent/'source-selection.json').write_text(json.dumps(receipt,indent=2)+'\n');return output
