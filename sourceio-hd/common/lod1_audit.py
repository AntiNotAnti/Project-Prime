"""Blender audit for a derived LOD1: skin, bind, material and sampled surface.

Separate decoded audits and real gameplay/capture review remain required.
Surface statistics sample every vertex and triangle center; they are not
exhaustive bounds or a guarantee of pixel-identical LOD transitions.
"""
import argparse, hashlib, json, math, sys
from collections import Counter
from pathlib import Path
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
from glb import load,read,image_bytes
p=argparse.ArgumentParser();p.add_argument('--config',required=True);p.add_argument('--output',required=True)
a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);root=Path(a.output).resolve();cp=Path(a.config).resolve()
cfg=json.loads(cp.read_text());build=json.loads((root/'build-result.json').read_text())
path=root/'kit/starter'/cfg['hunter'].lower()/'biped_lod1_weighted4.glb'
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
assert sha(path)==build['shippingGlbSha256'] and sha(cp)==build['configSha256']
doc,blob=load(path);skin=doc['skins'][0];names=[doc['nodes'][i]['name'] for i in skin['joints']]
entry=json.loads((root/'kit/starter/biped-lod1-weighted4-entry.json').read_text())
assert set(names)==set(entry['boneMap']) and len(names)<=32
convert=Matrix.Rotation(-math.pi/2,4,'X');bind_error=0.;hist=Counter();weight_error=0.;triangles=0
for name,values in zip(names,read(doc,blob,skin['inverseBindMatrices'])):
 inverse=Matrix([values[k*4:k*4+4] for k in range(4)]).transposed()
 closure=convert@Matrix(build['nativeBindMatrices'][name])@inverse
 bind_error=max(bind_error,max(abs(closure[r][c]-(r==c)) for r in range(4) for c in range(4)))
assert bind_error<1e-5
for mesh in doc['meshes']:
 for prim in mesh['primitives']:
  triangles+=len(read(doc,blob,prim['indices']))//3
  for js,ws in zip(read(doc,blob,prim['attributes']['JOINTS_0']),read(doc,blob,prim['attributes']['WEIGHTS_0'])):
   active=[(names[j],w) for j,w in zip(js,ws) if w>1e-6]
   assert 1<=len(active)<=4 and all(math.isfinite(w) and w>=0 for w in ws)
   weight_error=max(weight_error,abs(sum(ws)-1));hist[len(active)]+=1
  for key in ['POSITION','NORMAL','TEXCOORD_0','TEXCOORD_1']:
   assert all(math.isfinite(v) for row in read(doc,blob,prim['attributes'][key]) for v in row)
assert cfg['minimumTriangles']<=triangles<=cfg['maximumTriangles'] and triangles<build['sourceTriangles']
assert weight_error<1e-5 and sum(v for k,v in hist.items() if k>1)>0
surface=[]
def distances(points,faces,target):
 pts=[Vector(v) for v in points];centers=[sum((pts[i] for i in face),Vector())/3 for face in faces]
 # Shared glTF accessors may include vertices used only by another primitive.
 used=sorted({i for face in faces for i in face})
 return [target.find_nearest(p)[3] for p in [*[pts[i] for i in used],*centers]]
def stats(values):
 s=sorted(values);return {'samples':len(s),'rms':math.sqrt(sum(v*v for v in s)/len(s)),
  'p95':s[int(.95*(len(s)-1))],'p99':s[int(.99*(len(s)-1))],'maximum':s[-1]}
for pair in json.loads((root/'surface-samples.json').read_text()):
 old,old_faces=pair['source']
 # Measure the shipping GLB, including any triangulation/record changes by
 # the generated exporter, rather than only the pre-export authoring mesh.
 primitive=doc['meshes'][pair['primitive']]['primitives'][0]
 to_blender=Matrix.Rotation(math.pi/2,4,'X')
 new=[list(to_blender@Vector(v)) for v in read(doc,blob,primitive['attributes']['POSITION'])]
 exported_indices=[v[0] for v in read(doc,blob,primitive['indices'])]
 new_faces=[exported_indices[i:i+3] for i in range(0,len(exported_indices),3)]
 old_bvh=BVHTree.FromPolygons(old,old_faces,all_triangles=True)
 new_bvh=BVHTree.FromPolygons(new,new_faces,all_triangles=True)
 d0=stats(distances(old,old_faces,new_bvh));d1=stats(distances(new,new_faces,old_bvh))
 # Preserve the sampled surface relative to the complete character height.
 for d in [d0,d1]:
  assert d['rms']/build['sourceHeight']<.005,(pair['primitive'],d)
  assert d['maximum']/build['sourceHeight']<.025,(pair['primitive'],d)
 surface.append({'primitive':pair['primitive'],'lod0ToLod1':d0,'lod1ToLod0':d1})
report=dict(build,pass_=True)
report.pop('pass_',None);report['pass']=True
report.update(inverseBindMaximumError=bind_error,maximumWeightSumError=weight_error,
    authoringTriangles=build['triangles'],triangles=triangles,exportTriangleCountDifference=build['triangles']-triangles,
    exportedVerticesByInfluenceCount=dict(hist),exportedBlendedVertices=sum(v for k,v in hist.items() if k>1),
    surfaceDistances=surface,surfaceScope='All indexed vertices and triangle centers of the shipping GLB versus fitted accepted LOD0, per material/alpha primitive; sampled, not exhaustive.',
    materialContractPreserved=json.loads((root/'material-preservation.json').read_text())['pass'])
inputs=json.loads((root/'pipeline-inputs.json').read_text())
assert all(sha(path)==expected for path,expected in inputs.items()),'Conversion inputs changed during build/audit'
report['pipelineInputs']=inputs
(root/'audit.json').write_text(json.dumps(report,indent=2)+'\n')
print('LOD1_AUDIT_PASS',cfg['hunter'],triangles,dict(hist),bind_error)
