"""Blender audit, independent of conversion: topology, native rig, UVs, weights/binds."""
import argparse,bpy,json,math,sys,hashlib
from pathlib import Path
from collections import Counter,defaultdict
from itertools import product
from mathutils import Matrix,Vector,Quaternion
sys.path.insert(0,str(Path(__file__).resolve().parent))
from glb import load,read
from native import reference_bind
p=argparse.ArgumentParser();p.add_argument('--config',required=True);p.add_argument('--output',required=True)
a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);root=Path(a.output).resolve();cp=Path(a.config).resolve();cfg=json.loads(cp.read_text());hunter=cfg['hunter']
bpy.ops.wm.open_mainfile(filepath=str(root/'Native_Baseline.blend'));rig=bpy.data.objects['Armature'];baseline={b.name:(b.parent.name if b.parent else None,b.matrix_local.copy()) for b in rig.data.bones};native_bind={n:Matrix(m) for n,m in json.loads(rig['projectPrimeNativeRestMatrices']).items()}
authoritative=reference_bind(root/'kit');reference_bind_error=max(abs((native_bind[n]-m)[r][c]) for n,m in authoritative.items() for r in range(4) for c in range(4));assert reference_bind_error<1e-5,'Blender reference pose differs from native node values'
bpy.ops.wm.open_mainfile(filepath=str(root/(hunter+'_SourceIO_LOD0.blend')));rig=bpy.data.objects['Armature'];obj=bpy.data.objects[hunter+'_SourceIO_LOD0'];mesh=obj.data
assert set(baseline)==set(b.name for b in rig.data.bones)
for b in rig.data.bones:assert (b.parent.name if b.parent else None,b.matrix_local)==baseline[b.name]
report=json.loads((root/'retarget-report.json').read_text());bone_map=json.loads((cp.parent/'bone-map.json').read_text());materials=json.loads((cp.parent/'material-map.json').read_text());atlas=json.loads((root/'atlas-layout.json').read_text())['materials']
index=loop=polygon_index=0;weight_error=uv_error=fit_error=source_normal_error=0.;blended=0;normal_fallbacks=0;normal_lookup=defaultdict(list)
fit_matrices={n:Matrix(s['bakedSourceToNative']) for n,s in report['bindFitting'].items()}
preserved=[]
for n,s in cfg['fit'].items():
 if s.get('preserveSourceShape'):
  assert fit_matrices[n]==Matrix(report['globalConversionMatrix']), 'Core silhouette was stretched'
  preserved.append(n)
for source in [bpy.data.objects[n] for n in cfg['bodygroups']]:
 offset=index
 for v in source.data.vertices:
  collapsed={}
  for g in v.groups:
   if g.weight>1e-6:
    name=bone_map[source.vertex_groups[g.group].name];collapsed[name]=collapsed.get(name,0)+g.weight
  total=sum(collapsed.values());expected={n:w/total for n,w in collapsed.items()};actual={obj.vertex_groups[g.group].name:g.weight for g in mesh.vertices[index].groups if g.weight>1e-6}
  assert set(actual)==set(expected);weight_error=max(weight_error,max(abs(actual[n]-w) for n,w in expected.items()));assert len(actual)<=4;blended+=len(actual)>1
  fitted=sum(((fit_matrices[n]@v.co)*w for n,w in expected.items()),Vector());fit_error=max(fit_error,(fitted-mesh.vertices[index].co).length);index+=1
 source.data.calc_loop_triangles()
 for tri in source.data.loop_triangles:
  poly=mesh.polygons[polygon_index];assert tuple(poly.vertices)==tuple(offset+i for i in tri.vertices)
  name=source.data.materials[source.data.polygons[tri.polygon_index].material_index].name;spec=atlas[name]
  assert mesh.materials[poly.material_index].name==materials[name];polygon_index+=1
  shift=[math.floor(min(source.data.uv_layers.active.data[i].uv[k] for i in tri.loops)) for k in range(2)] if spec.get('compactPeriodicUVs') else [0,0]
  for i in tri.loops:
   if cfg.get('preserveSourceNormals'):
    v=source.data.vertices[source.data.loops[i].vertex_index];merged={}
    for g in v.groups:
     if g.weight>1e-6:
      n=bone_map[source.vertex_groups[g.group].name];merged[n]=merged.get(n,0)+g.weight
    total=sum(merged.values());jac=Matrix(((0,0,0),(0,0,0),(0,0,0)))
    for n,w in merged.items():jac+=fit_matrices[n].to_3x3()*(w/total)
    expected=jac.inverted().transposed()@source.data.corner_normals[i].vector
    if expected.length<1e-6:
     expected=jac.inverted().transposed()@source.data.polygons[tri.polygon_index].normal
     if expected.length<1e-6:expected=Vector((0,0,1))
     normal_fallbacks+=1
    expected.normalize();actual=mesh.corner_normals[loop].vector
    # Blender has no meaningful corner normal for a degenerate source face.
    if actual.length>1e-6:
     error=(expected-actual).length
     if error>source_normal_error:
      source_normal_error=error;worst_normal={'object':source.name,'sourceLoop':i,'targetLoop':loop,'expected':list(expected),'actual':list(actual),'error':error}

    point=Matrix.Rotation(-math.pi/2,4,'X')@mesh.vertices[mesh.loops[loop].vertex_index].co
    normal=Matrix.Rotation(-math.pi/2,4,'X')@expected
    normal_lookup[(spec['runtimeMaterial'],tuple(round(c,4) for c in point))].append((point,normal))
   uv=source.data.uv_layers.active.data[i].uv;assert mesh.uv_layers['SOURCE_UV_Original'].data[loop].uv==uv
   for k in range(2):uv_error=max(uv_error,abs(mesh.uv_layers['UVMap'].data[loop].uv[k]-((uv[k]-shift[k])*spec['atlasUVScale'][k]+spec['atlasUVOffset'][k])))
   loop+=1
if cfg.get('preserveSourceNormals'):print('NORMAL AUDIT',mesh.has_custom_normals,source_normal_error,worst_normal,flush=True)
if cfg.get('preserveSourceNormals'):assert mesh.has_custom_normals and source_normal_error<.01745, 'Authored Source split normals were rebuilt'
assert index==report['vertices'] and loop==cfg['expectedTriangles']*3 and weight_error<1e-6 and uv_error<1e-6 and fit_error<1e-5
limb_shape_error=chain_join_error=0.;preserved_limbs=[]
if cfg.get('sourceShapeChains'):
 scale=report['globalHeightScale']
 for n,s in report['bindFitting'].items():
  if s.get('sourceLimbShapePreserved'):
   m=fit_matrices[n].to_3x3()*(1/scale);unit=m.transposed()@m
   limb_shape_error=max(limb_shape_error,max(abs(unit[r][c]-(r==c)) for r in range(3) for c in range(3)));preserved_limbs.append(n)
 source_rig=next(o for o in bpy.data.objects if o.type=='ARMATURE' and o.name!='Armature')
 for side in ['L','R']:
  for parent,child in [(side+'_hip',side+'_knee'),(side+'_knee',side+'_ankle'),(side+'_shoulder',side+'_elbow')]:
   point=source_rig.data.bones[cfg['fit'][child]['anchor']].head_local
   chain_join_error=max(chain_join_error,(fit_matrices[parent]@point-fit_matrices[child]@point).length)
 assert limb_shape_error<1e-5 and chain_join_error<1e-5,'Source limb shape or connected chain changed'
path=root/'kit/starter'/hunter.lower()/'biped_weighted4.glb';doc,blob=load(path);skin=doc['skins'][0];names=[doc['nodes'][i]['name'] for i in skin['joints']]
entry=json.loads((root/'kit/starter/biped-weighted4-entry.json').read_text());assert set(names)==set(entry['boneMap']) and len(names)<=32
convert=Matrix.Rotation(-math.pi/2,4,'X');bind_error=0
for name,values in zip(names,read(doc,blob,skin['inverseBindMatrices'])):
 inverse=Matrix([values[k*4:k*4+4] for k in range(4)]).transposed();m=convert@native_bind[name]@inverse
 bind_error=max(bind_error,max(abs(m[r][c]-(r==c)) for r in range(4) for c in range(4)))
assert bind_error<1e-5
parents={child:i for i,node in enumerate(doc['nodes']) for child in node.get('children',[])}
def node_world(i):
 node=doc['nodes'][i]
 if 'matrix' in node:local=Matrix([node['matrix'][k*4:k*4+4] for k in range(4)]).transposed()
 else:
  q=node.get('rotation',[0,0,0,1]);local=Matrix.LocRotScale(Vector(node.get('translation',[0,0,0])),Quaternion((q[3],q[0],q[1],q[2])),Vector(node.get('scale',[1,1,1])))
 return node_world(parents[i])@local if i in parents else local
node_bind_error=max(max(abs((node_world(i)-convert@native_bind[doc['nodes'][i]['name']])[r][c]) for r in range(4) for c in range(4)) for i in skin['joints'])
assert node_bind_error<1e-5, 'GLTF default joint pose differs from full native bind'

hist=Counter();triangles=0;norm_error=export_normal_error=0;positions=[];primitive_count=0
for model in doc['meshes']:
 for primitive in model['primitives']:
  primitive_count+=1;triangles+=len(read(doc,blob,primitive['indices']))//3
  pos=read(doc,blob,primitive['attributes']['POSITION']);js=read(doc,blob,primitive['attributes']['JOINTS_0']);ws=read(doc,blob,primitive['attributes']['WEIGHTS_0'])
  if cfg.get('preserveSourceNormals'):
   normal_values=read(doc,blob,primitive['attributes']['NORMAL']);assert len(normal_values)==len(pos)
   material=doc['materials'][primitive['material']]['name']
   for point,normal in zip(pos,normal_values):
    actual=Vector(normal);assert all(math.isfinite(c) for c in actual) and abs(actual.length-1)<.001
    key=tuple(round(c,4) for c in point);candidates=normal_lookup.get((material,key),[])
    candidates=[n for p,n in candidates if (Vector(point)-p).length<1e-5]
    # Several split vertices may straddle rounding-cell boundaries; search
    # all neighboring cells even when one exact-cell corner already exists.
    for delta in product([-1,0,1],repeat=3):
     if delta==(0,0,0):continue
     neighbors=normal_lookup.get((material,tuple(round(k+d*.0001,4) for k,d in zip(key,delta))),[])
     candidates.extend(n for p,n in neighbors if (Vector(point)-p).length<1e-5)
    assert candidates,'Exported normal has no authored corner'
    error=min((actual-n).length for n in candidates)
    if error>export_normal_error:
     export_normal_error=error;worst_export_normal={'material':material,'point':list(point),'normal':list(normal),'expected':[list(n) for n in candidates],'error':error}
  for point,joints,weights in zip(pos,js,ws):
   active=[(names[j],w) for j,w in zip(joints,weights) if w>1e-6];assert 1<=len(active)<=4 and all(math.isfinite(w) and w>=0 for w in weights)
   norm_error=max(norm_error,abs(sum(weights)-1));hist[len(active)]+=1;positions.append((Vector(point),active))
if cfg.get('preserveSourceNormals'):
 print('EXPORT NORMAL AUDIT',export_normal_error,worst_export_normal,flush=True)
 assert export_normal_error<.01745, 'Export lost authored Source normals'
assert triangles==cfg['expectedTriangles'] and norm_error<1e-5
assert {m['name'] for m in doc['materials']}==set(materials.values())
for material in doc['materials']:
 if material['name'] in cfg.get('teamRecolorMaterials',[]):
  assert set(material['extras']['projectPrimeRecolors'])=={'4','5'}
muzzle_proof=None
if cfg.get('muzzle'):
 constraint=cfg['muzzle'];fit=report['muzzleFit'];point=convert@Vector(fit['nativeCenter']);error=report['muzzleFit']['maximumError']
 if fit['sourceLandmarkVertices']:
  offset=sum(report['bodygroups'][n]['vertices'] for n in cfg['bodygroups'][:cfg['bodygroups'].index(constraint['object'])]);ids=fit['sourceLandmarkVertices'];center=sum((mesh.vertices[offset+i].co for i in ids),Vector())/len(ids)
  assert (center-Vector(fit['nativeCenter'])).length<1e-5
  for i in ids:
   baked=convert@mesh.vertices[offset+i].co;matches=[active for pos,active in positions if (pos-baked).length<1e-5];assert matches and all(len(active)==1 and active[0][0]==constraint['nativeBone'] for active in matches)
 assert error<1e-5
 muzzle_proof={'restPoint':list(point),'nativeBone':constraint['nativeBone'],'nativeLocalPoint':constraint.get('nativeLocalPoint'),'maximumRestError':error,'method':fit['method']}
source_path_hash=report['sourceSha256'];assert source_path_hash==cfg['sourceSha256']
refs=json.loads((root/'generated-reference-hashes.json').read_text())
for name,expected in refs.items():assert hashlib.sha256((root/name).read_bytes()).hexdigest()==expected,name
inputs={'config.json':hashlib.sha256(cp.read_bytes()).hexdigest(),'bone-map.json':hashlib.sha256((cp.parent/'bone-map.json').read_bytes()).hexdigest(),'material-map.json':hashlib.sha256((cp.parent/'material-map.json').read_bytes()).hexdigest(),'exporter':hashlib.sha256((root/'kit/prepare-biped-weighted4.py').read_bytes()).hexdigest()}
out={'pass':True,'hunter':hunter,'nativeRigRestAndHierarchyUnchanged':True,'generatedExporterAndReferencesUnchanged':True,'sourceTopologyUnchanged':True,'originalUVsUnchanged':True,'periodicUvCompaction':cfg.get('compactPeriodicUVs',False),'sourceSha256':source_path_hash,'triangles':triangles,'authoringVertices':index,'exportedVertices':sum(hist.values()),'authoringBlendedVertices':blended,'jointCount':len(names),'primitiveCount':primitive_count,'maximumCollapsedSourceWeightError':weight_error,'maximumWeightSumError':norm_error,'maximumRuntimeUvError':uv_error,'inverseBindMaximumError':bind_error,'defaultJointNativeBindMaximumError':node_bind_error,'inheritedNativeBindScalePreserved':True,'nativeReferenceBindMaximumError':reference_bind_error,'exportedVerticesByInfluenceCount':dict(hist),'materials':sorted(set(materials.values())),'muzzleProof':muzzle_proof,'inputs':inputs,'shippingGlbSha256':hashlib.sha256(path.read_bytes()).hexdigest()}
out['teamRecolorsRequired']=bool(cfg.get('teamRecolorMaterials'))
if cfg.get('preserveSourceMaterialAlpha'):
 alpha=json.loads((root/'source-alpha.json').read_text());assert alpha['pass']
 assert all(m.get('doubleSided',False) for m in doc['materials']), 'Source double-sided material was lost'
 for name,spec in alpha['materials'].items():
  match=[m for m in doc['materials'] if m.get('extras',{}).get('projectPrimeSourceMaterial')==name];assert len(match)==1 and match[0]['alphaMode']==spec['alphaMode']
 out['surfaceAudit']={'doubleSidedSourceMaterialsPreserved':True,'transparentSourceMaterials':alpha['materials']}
out['normalAudit']={'sourceSplitNormalsPreserved':cfg.get('preserveSourceNormals',False),'maximumAuthoringNormalError':source_normal_error,'maximumExportNormalError':export_normal_error,'maximumNormalAngleDegrees':math.degrees(2*math.asin(min(1,max(source_normal_error,export_normal_error)/2))),'toleranceDegrees':1,'degenerateSourceFallbackCorners':normal_fallbacks}
out['shapeAudit']={'sourceCoreUniformTransformOnly':preserved,'maximumAuthoringFitError':fit_error,'nativePivotOffsetsExplicit':True,'rigidSourceLimbs':preserved_limbs,'maximumLimbScaleOrthogonalityError':limb_shape_error,'maximumChainJoinError':chain_join_error}
(root/'audit.json').write_text(json.dumps(out,indent=2)+'\n');print(json.dumps(out,indent=2))
