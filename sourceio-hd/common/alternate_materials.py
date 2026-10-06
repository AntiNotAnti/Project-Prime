"""Alternate pack material companions and Source transparency, after export."""
import copy,json,struct,hashlib,shutil
from pathlib import Path
from glb import load,read
from material_maps import add_material_maps
from recolors import add_recolors

def write_glb(path,doc,blob):
    while len(blob)%4:blob.append(0)
    doc['buffers'][0]['byteLength']=len(blob)
    encoded=json.dumps(doc,separators=(',',':')).encode();encoded+=b' '*(-len(encoded)%4)
    Path(path).write_bytes(struct.pack('<4sII',b'glTF',2,28+len(encoded)+len(blob))+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(blob),0x004e4942)+blob)

def companions(cfg,root,source):
    root=Path(root);model=root/'kit/starter'/cfg['hunter'].lower()/'altform_weighted4.glb'
    atlas=json.loads((root/'atlas-layout.json').read_text())['materials']
    for override in cfg.get('materialOverrides',[]):
        original=atlas[override['sourceMaterial']]['runtimeMaterial']
        for channel in ['albedo','normal','emissive','material']:
            src=root/'textures'/(original+'-'+channel+'.png');dst=root/'textures'/(override['nativeMaterial']+'-'+channel+'.png')
            if src.exists():shutil.copy2(src,dst)
    add_material_maps(root,model);add_recolors(cfg,root)
    doc,binary=load(model);blob=bytearray(binary);source_doc,_=load(source)
    transparent={m['name']:m for m in source_doc['materials'] if m.get('alphaMode','OPAQUE')!='OPAQUE' and m['name'] in atlas}
    report={}
    def new_indices(values):
        while len(blob)%4:blob.append(0)
        payload=struct.pack('<'+'I'*len(values),*values);view=len(doc['bufferViews']);doc['bufferViews'].append({'buffer':0,'byteOffset':len(blob),'byteLength':len(payload),'target':34963});blob.extend(payload)
        ix=len(doc['accessors']);doc['accessors'].append({'bufferView':view,'componentType':5125,'type':'SCALAR','count':len(values),'min':[min(values)],'max':[max(values)]});return ix
    for mesh in doc['meshes']:
        added=[]
        for pr in mesh['primitives']:
            native=doc['materials'][pr['material']]['name'];remaining=[v[0] for v in read(doc,binary,pr['indices'])];uv=read(doc,binary,pr['attributes']['TEXCOORD_0'])
            for name,mat in transparent.items():
                spec=atlas[name]
                if spec['runtimeMaterial']!=native:continue
                lo=[spec['uvLow'][k]*spec['atlasUVScale'][k]+spec['atlasUVOffset'][k] for k in range(2)];hi=[spec['uvHigh'][k]*spec['atlasUVScale'][k]+spec['atlasUVOffset'][k] for k in range(2)];lo[1],hi[1]=1-hi[1],1-lo[1]
                chosen=[];keep=[]
                for start in range(0,len(remaining),3):
                    face=remaining[start:start+3];target=chosen if all(lo[k]-1e-6<=uv[i][k]<=hi[k]+1e-6 for i in face for k in range(2)) else keep;target.extend(face)
                if not chosen:continue
                duplicate=copy.deepcopy(doc['materials'][pr['material']]);duplicate['alphaMode']=mat['alphaMode'];duplicate.setdefault('extras',{})['projectPrimeSourceMaterial']=name
                if 'alphaCutoff' in mat:duplicate['alphaCutoff']=mat['alphaCutoff']
                ix=len(doc['materials']);doc['materials'].append(duplicate);added.append(dict(pr,material=ix,indices=new_indices(chosen)));remaining=keep
                report[name]={'nativeIdentity':native,'alphaMode':mat['alphaMode'],'triangles':len(chosen)//3}
            if remaining:pr['indices']=new_indices(remaining)
            elif any(p['attributes']==pr['attributes'] for p in added):pr['_remove']=True
        mesh['primitives']=[p for p in mesh['primitives'] if not p.pop('_remove',False)]+added
    assert set(report)==set(transparent),'An authored transparent surface was omitted'
    assert bytes(blob[:len(binary)])==binary,'Material partition modified existing geometry/artwork'
    write_glb(model,doc,blob)
    (root/'source-alpha.json').write_text(json.dumps({'pass':True,'materials':report,'originalGeneratedPayloadUnchanged':True},indent=2)+'\n')
