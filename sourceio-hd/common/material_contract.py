"""Keep Source alpha surfaces separate under their existing native identity.

Only primitive index lists/material metadata change. Exported positions, normals,
UVs, skin weights, native joints, image payloads and triangle inventory stay intact.
"""
import copy,json,struct,hashlib
from pathlib import Path
from glb import load,read

def preserve_alpha(config,root,source):
 if not config.get('preserveSourceMaterialAlpha'):return
 path=root/'kit/starter'/config['hunter'].lower()/'biped_weighted4.glb';doc,original=load(path);blob=bytearray(original)
 source_doc,_=load(source);atlas=json.loads((root/'atlas-layout.json').read_text())['materials'];retarget=json.loads((root/'retarget-report.json').read_text())
 transparent={m['name']:m for m in source_doc['materials'] if m.get('alphaMode','OPAQUE')!='OPAQUE' and m['name'] in atlas}
 changed={}
 def indices(values):
  while len(blob)%4:blob.append(0)
  payload=struct.pack('<'+'I'*len(values),*values);view=len(doc['bufferViews']);doc['bufferViews'].append({'buffer':0,'byteOffset':len(blob),'byteLength':len(payload),'target':34963});blob.extend(payload)
  accessor=len(doc['accessors']);doc['accessors'].append({'bufferView':view,'componentType':5125,'type':'SCALAR','count':len(values),'min':[min(values)],'max':[max(values)]});return accessor
 for model in doc['meshes']:
  additional=[]
  for primitive in model['primitives']:
   native=doc['materials'][primitive['material']]['name'];uv=read(doc,original,primitive['attributes']['TEXCOORD_0']);old=[row[0] for row in read(doc,original,primitive['indices'])];remaining=old[:]
   for name,material in transparent.items():
    spec=atlas[name]
    if spec['runtimeMaterial']!=native:continue
    lower=[spec['uvLow'][k]*spec['atlasUVScale'][k]+spec['atlasUVOffset'][k] for k in range(2)];upper=[spec['uvHigh'][k]*spec['atlasUVScale'][k]+spec['atlasUVOffset'][k] for k in range(2)]
    lower[1],upper[1]=1-upper[1],1-lower[1]
    chosen=[];keep=[]
    for start in range(0,len(remaining),3):
     tri=remaining[start:start+3]
     dest=chosen if all(lower[k]-1e-6<=uv[v][k]<=upper[k]+1e-6 for v in tri for k in range(2)) else keep
     dest.extend(tri)
    assert len(chosen)//3==retarget['sourceMaterialTriangles'][name], 'Transparent triangle selection differs from Source'
    duplicate=copy.deepcopy(doc['materials'][primitive['material']]);duplicate['alphaMode']=material['alphaMode'];duplicate.setdefault('extras',{})['projectPrimeSourceMaterial']=name
    if 'alphaCutoff' in material:duplicate['alphaCutoff']=material['alphaCutoff']
    material_index=len(doc['materials']);doc['materials'].append(duplicate)
    part=dict(primitive,material=material_index,indices=indices(chosen));additional.append(part);remaining=keep
    changed[name]={'nativeIdentity':native,'alphaMode':material['alphaMode'],'triangles':len(chosen)//3,'originalAlphaRetained':True}
   if remaining!=old:
    assert remaining,'Atlas mixing expected both opaque and transparent geometry'
    primitive['indices']=indices(remaining)
  model['primitives']+=additional
 if changed:
  while len(blob)%4:blob.append(0)
  doc['buffers'][0]['byteLength']=len(blob);encoded=json.dumps(doc,separators=(',',':')).encode();encoded+=b' '*((-len(encoded))%4)
  path.write_bytes(struct.pack('<4sII',b'glTF',2,28+len(encoded)+len(blob))+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(blob),0x004e4942)+blob)
 assert bytes(blob[:len(original)])==original,'Source geometry/image bytes changed'
 assert set(changed)==set(transparent),'An authored transparent material was lost'
 (root/'source-alpha.json').write_text(json.dumps({'pass':True,'generatedAttributeAndImageBytesUnchanged':True,'nativeMaterialIdentitiesPreserved':True,'materials':changed},indent=2)+'\n')
