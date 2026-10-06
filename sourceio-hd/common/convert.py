"""Blender worker: config-driven Source topology/weights -> native MPH rest rig."""
import argparse, bpy, json, math, runpy, sys, hashlib
from pathlib import Path
from collections import defaultdict, Counter
from mathutils import Matrix, Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from native import load_native,reference_bind
parser=argparse.ArgumentParser();parser.add_argument('--config',required=True);parser.add_argument('--source',required=True);parser.add_argument('--output',required=True);parser.add_argument('--repo',required=True)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
ROOT=Path(args.output).resolve();SOURCE=Path(args.source).resolve();config_path=Path(args.config).resolve();cfg=json.loads(config_path.read_text());repo=Path(args.repo)
MAP=json.loads((config_path.parent/'bone-map.json').read_text());kit=ROOT/'kit'
entry=json.loads((kit/'starter/biped-weighted4-entry.json').read_text());EXPECTED=set(entry['boneMap']);hunter=cfg['hunter']
assert set(MAP.values())<=EXPECTED
if (ROOT/'RELEASE-LOCK.json').exists():raise RuntimeError('Frozen output: choose a new output directory.')
baseline=ROOT/'Native_Baseline.blend'
if cfg.get('nativeBaseline'):
 bpy.ops.wm.open_mainfile(filepath=str(repo/cfg['nativeBaseline']));rig=bpy.data.objects['Armature'];native_meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
else:rig,native_meshes=load_native(kit,hunter,repo/'src/MphRead/Export/modules/mph_common.py')
if not rig.get('projectPrimeNativeRestMatrices'):
 rig['projectPrimeNativeRestMatrices']=json.dumps({n:[list(r) for r in m] for n,m in reference_bind(kit).items()})
bpy.ops.wm.save_as_mainfile(filepath=str(baseline))
scene=bpy.context.scene;native_rest={b.name:b.matrix_local.copy() for b in rig.data.bones};heads={n:m.translation.copy() for n,m in native_rest.items()}
native_bind={n:Matrix(m) for n,m in json.loads(rig.get('projectPrimeNativeRestMatrices',json.dumps({n:[list(r) for r in m] for n,m in native_rest.items()}))).items()}
native_low=min(v.co.z for o in native_meshes for v in o.data.vertices);native_high=max(v.co.z for o in native_meshes for v in o.data.vertices)
reference=bpy.data.collections.new('REFERENCE_Native_MPH');scene.collection.children.link(reference)
for o in native_meshes:
 o.parent=None;o.modifiers.clear();o.vertex_groups.clear();o.name='REFERENCE_'+o.name
 for c in list(o.users_collection):c.objects.unlink(o)
 reference.objects.link(o);o.hide_render=True;o.hide_set(True)
for mat in list(bpy.data.materials):mat.name='REFERENCE_'+mat.name
previous=set(scene.objects);bpy.ops.import_scene.gltf(filepath=str(SOURCE));imported=[o for o in scene.objects if o not in previous]
source=next(o for o in imported if o.type=='ARMATURE');source_bones=source.data.bones
meshes=[bpy.data.objects[n] for n in cfg['bodygroups']];assert all(o in imported and o.type=='MESH' for o in meshes)
assert len(source_bones)==cfg['expectedSourceBones']
assert source.matrix_world==Matrix.Identity(4) and all(o.matrix_local==Matrix.Identity(4) for o in meshes),'Unbaked Source object transforms'
source_low=min(v.co.z for o in meshes for v in o.data.vertices);source_high=max(v.co.z for o in meshes for v in o.data.vertices)
scale=(native_high-native_low)/(source_high-source_low)
global_matrix=(Matrix.Rotation(math.radians(cfg.get('yawDegrees',180)),3,'Z')*scale).to_4x4()
global_matrix.translation=heads[cfg['rootNative']]-global_matrix.to_3x3()@source_bones[cfg['rootSource']].head_local
if cfg.get('globalAnchor')=='floor':
 global_matrix.translation.z=native_low-scale*source_low
frames={};fit_report={};muzzle_report=None
# The explicit muzzle landmark may be a documented front-cap centroid or a
# geometry-derived front-band centroid. Both must have one collapsed native joint.
constraint=cfg.get('muzzle')
if constraint:
 if 'sourcePoint' in constraint:source_muzzle=Vector(constraint['sourcePoint']);indices=[]
 else:
  o=bpy.data.objects[constraint['object']];axis=Vector(constraint['axis']).normalized();origin=source_bones[constraint['sourceBone']].head_local
  candidates=[v for v in o.data.vertices if any(o.vertex_groups[g.group].name==constraint['sourceBone'] and g.weight>0.99999 for g in v.groups)]
  front=max((v.co-origin).dot(axis) for v in candidates)
  chosen=[v for v in candidates if (v.co-origin).dot(axis)>front-constraint['frontBand']]
  source_muzzle=sum((v.co for v in chosen),Vector())/len(chosen);indices=[v.index for v in chosen]
  for v in chosen:
   assert {MAP[o.vertex_groups[g.group].name] for g in v.groups if g.weight>1e-6}=={constraint['nativeBone']}
 if 'nativeMesh' in constraint:
  tip=bpy.data.objects['REFERENCE_'+constraint['nativeMesh']];native_muzzle=sum((v.co for v in tip.data.vertices),Vector())/len(tip.data.vertices)
 else:native_muzzle=native_bind[constraint['nativeBone']]@Vector(constraint['nativeLocalPoint'])
 muzzle_report={'sourceCenter':list(source_muzzle),'nativeCenter':list(native_muzzle),'nativeJoint':constraint['nativeBone'],'sourceLandmarkVertices':indices,'method':constraint['method']}
for name,spec in cfg['fit'].items():
 anchor=spec['anchor'];origin=global_matrix@source_bones[anchor].head_local;local=Matrix.Identity(3)
 if spec.get('preserveSourceShape'):
  frames[name]=global_matrix.copy()
  fit_report[name]={'sourceAnchor':anchor,'bakedSourceToNative':[list(r) for r in frames[name]],'sourceShapePreserved':True,'nativePivotOffset':list(origin-heads[name]),'anchorRequired':False,'anchorError':(origin-heads[name]).length}
  continue
 if spec.get('muzzle'):
  endpoint=source_muzzle;target_end=native_muzzle
 elif 'endSource' in spec:
  endpoint=source_bones[spec['endSource']].head_local
  target_end=heads[spec['endNative']]
 else:endpoint=None
 if endpoint is not None:
  direction=global_matrix.to_3x3()@(endpoint-source_bones[anchor].head_local);target_direction=target_end-heads[name]
  unit=direction.normalized();ratio=target_direction.length/direction.length if spec.get('stretch',True) else 1.;stretch=Matrix.Identity(3)
  for r in range(3):
   for c in range(3):stretch[r][c]+=(ratio-1)*unit[r]*unit[c]
  local=unit.rotation_difference(target_direction.normalized()).to_matrix()@stretch
 if spec.get('orientFrom'):
  direction=global_matrix.to_3x3()@(source_bones[anchor].head_local-source_bones[spec['orientFrom']].head_local)
  local=direction.normalized().rotation_difference((heads[name]-heads[spec['orientNative']]).normalized()).to_matrix()
 # Fit vertical silhouette at configured end joints; no post-export scaling.
 if spec.get('boundZ'):
  points=[v.co for o in meshes for v in o.data.vertices if MAP[o.vertex_groups[max(v.groups,key=lambda g:g.weight).group].name]==name]
  zs=[(local@global_matrix.to_3x3()@(v-source_bones[anchor].head_local)).z for v in points]
  actual=max(zs) if spec['boundZ'].startswith('top') else min(zs)
  target=(native_high if spec['boundZ'].startswith('top') else native_low)-heads[name].z
  correction=Matrix.Identity(3);correction[2][2]=min(1.,target/actual) if spec['boundZ']=='topClamp' else target/actual;local=correction@local
 fit=local.to_4x4();fit.translation=heads[name]-local@origin;frames[name]=fit@global_matrix
 if spec.get('groundShape'):
  points=[v.co for o in meshes for v in o.data.vertices if MAP[o.vertex_groups[max(v.groups,key=lambda g:g.weight).group].name]==name]
  # Preserve the complete foot/claw rather than flattening it to the native
  # ankle height. Record its pivot offset and translate its floor contact.
  frames[name].translation.z+=native_low-min((frames[name]@v).z for v in points)

 fit_report[name]={'sourceAnchor':anchor,'bakedSourceToNative':[list(r) for r in frames[name]],'anchorError':(frames[name]@source_bones[anchor].head_local-heads[name]).length,'anchorRequired':not spec.get('groundShape',False),'nativePivotOffset':list(frames[name]@source_bones[anchor].head_local-heads[name]),'footShapePreserved':spec.get('groundShape',False)}
if cfg.get('sourceShapeChains'):
 from shape_fit import fit_chains
 fit_chains(cfg,source_bones,meshes,MAP,global_matrix,heads,native_low,frames,fit_report,source_muzzle if constraint else None,native_muzzle if constraint else None)
assert set(frames)==EXPECTED and max((s['anchorError'] for s in fit_report.values() if s.get('anchorRequired',True)),default=0)<1e-5
if constraint:
 error=(frames[constraint['nativeBone']]@source_muzzle-native_muzzle).length;assert error<1e-5
 muzzle_report['maximumError']=error
atlas=json.loads((ROOT/'atlas-layout.json').read_text())['materials'];order=list(dict.fromkeys(s['runtimeMaterial'] for s in atlas.values()));materials={}
for name in order:
 mat=bpy.data.materials.new(name);mat.use_nodes=True;nodes=mat.node_tree.nodes;links=mat.node_tree.links;bs=nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.6
 albedo=nodes.new('ShaderNodeTexImage');albedo.image=bpy.data.images.load(str(ROOT/'textures'/(name+'-albedo.png')));albedo.image.pack();links.new(albedo.outputs['Color'],bs.inputs['Base Color'])
 normal=nodes.new('ShaderNodeTexImage');normal.image=bpy.data.images.load(str(ROOT/'textures'/(name+'-normal.png')));normal.image.colorspace_settings.name='Non-Color';normal.image.pack()
 nm=nodes.new('ShaderNodeNormalMap');links.new(normal.outputs['Color'],nm.inputs['Color']);links.new(nm.outputs['Normal'],bs.inputs['Normal'])
 emission_path=ROOT/'textures'/(name+'-emissive.png')
 if emission_path.exists():
  emission=nodes.new('ShaderNodeTexImage');emission.image=bpy.data.images.load(str(emission_path));emission.image.pack();links.new(emission.outputs['Color'],bs.inputs['Emission Color']);bs.inputs['Emission Strength'].default_value=1.
 elif name in cfg.get('emissiveMaterials',[]):links.new(albedo.outputs['Color'],bs.inputs['Emission Color']);bs.inputs['Emission Strength'].default_value=1.
 materials[name]=mat
verts=[];weights=[];faces=[];uvs=[];face_mats=[];authored_normals=[];normal_fallbacks=0;source_hist=Counter();target_hist=Counter();counts={}
for o in meshes:
 start=len(verts);o.data.calc_loop_triangles();counts[o.name]={'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles)}
 for v in o.data.vertices:
  raw=[(o.vertex_groups[g.group].name,g.weight) for g in v.groups if g.weight>1e-6];source_hist[len(raw)]+=1;merged=defaultdict(float)
  for name,w in raw:
   assert name in MAP,'Unmapped source bone: '+name;merged[MAP[name]]+=w
  ws=sorted(merged.items(),key=lambda p:p[1],reverse=True);assert len(ws)<=4,'More than 4 influences: fix map; no silent trimming'
  total=sum(w for n,w in ws);assert total>1e-6;ws=[(n,w/total) for n,w in ws]
  verts.append(sum(((frames[n]@v.co)*w for n,w in ws),Vector()));weights.append(ws);target_hist[len(ws)]+=1
 for triangle in o.data.loop_triangles:
  matname=o.data.materials[o.data.polygons[triangle.polygon_index].material_index].name
  if cfg.get('preserveSourceNormals'):
   for source_loop,vertex_id in zip(triangle.loops,triangle.vertices):
    ws=weights[start+vertex_id];jac=Matrix(((0,0,0),(0,0,0),(0,0,0)))
    for n,w in ws:jac+=frames[n].to_3x3()*w
    normal=jac.inverted().transposed()@o.data.corner_normals[source_loop].vector
    if normal.length<1e-6:
     normal=jac.inverted().transposed()@o.data.polygons[triangle.polygon_index].normal
     if normal.length<1e-6:normal=Vector((0,0,1))
     normal_fallbacks+=1
    authored_normals.append(normal.normalized())
  faces.append(tuple(start+i for i in triangle.vertices));uvs.append([o.data.uv_layers.active.data[i].uv.copy() for i in triangle.loops]);face_mats.append(matname)
assert len(faces)==cfg['expectedTriangles']
mesh=bpy.data.meshes.new(hunter+'_SourceIO_LOD0');mesh.from_pydata(verts,[],faces);mesh.update()
for name in order:mesh.materials.append(materials[name])
runtime_uv=mesh.uv_layers.new(name='UVMap');original_uv=mesh.uv_layers.new(name='SOURCE_UV_Original')
for polygon,coords,matname in zip(mesh.polygons,uvs,face_mats):
 spec=atlas[matname];polygon.material_index=order.index(spec['runtimeMaterial']);polygon.use_smooth=True
 shift=[math.floor(min(uv[k] for uv in coords)) for k in range(2)] if spec.get('compactPeriodicUVs') else [0,0]
 for loop,uv in zip(polygon.loop_indices,coords):
  original_uv.data[loop].uv=uv;runtime_uv.data[loop].uv=[(uv[i]-shift[i])*spec['atlasUVScale'][i]+spec['atlasUVOffset'][i] for i in range(2)]
runtime_uv.active_render=True;mesh.uv_layers.active_index=0
if cfg.get('preserveSourceNormals'):mesh.normals_split_custom_set(authored_normals)
obj=bpy.data.objects.new(hunter+'_SourceIO_LOD0',mesh);scene.collection.objects.link(obj);obj.parent=rig;obj.matrix_parent_inverse=Matrix.Identity(4)
groups={n:obj.vertex_groups.new(name=n) for n in sorted(EXPECTED)}
for i,ws in enumerate(weights):
 for n,w in ws:groups[n].add([i],w,'REPLACE')
modifier=obj.modifiers.new('Native MPH Weighted4','ARMATURE');modifier.object=rig
for b in rig.data.bones:b.use_deform=b.name in EXPECTED
for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
source_reference=bpy.data.collections.new('REFERENCE_SourceIO_Original');scene.collection.children.link(source_reference)
for o in imported:
 for c in list(o.users_collection):c.objects.unlink(o)
 source_reference.objects.link(o);o.hide_render=True;o.hide_set(True)
source.matrix_world=global_matrix
assert native_rest=={b.name:b.matrix_local for b in rig.data.bones}
report={'hunter':hunter,'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'bodygroups':counts,'sourceBones':len(source_bones),'nativeDeformBones':len(EXPECTED),'nativeBindMatrices':{n:[list(r) for r in m] for n,m in native_bind.items()},'globalConversionMatrix':[list(r) for r in global_matrix],'globalHeightScale':scale,'bindFitting':fit_report,'boneMap':MAP,'sourceVerticesByInfluenceCount':dict(source_hist),'nativeVerticesByInfluenceCount':dict(target_hist),'vertices':len(verts),'triangles':len(faces),'nativeBoundsZ':[native_low,native_high],'replacementBoundsZ':[min(v.z for v in verts),max(v.z for v in verts)],'muzzleFit':muzzle_report,'sourceMaterialTriangles':dict(Counter(face_mats)),'sourceNormalsPreserved':cfg.get('preserveSourceNormals',False),'degenerateSourceNormalFallbackCorners':normal_fallbacks,'remeshed':False,'subdivided':False}
(ROOT/'retarget-report.json').write_text(json.dumps(report,indent=2)+'\n')
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/(hunter+'_SourceIO_LOD0.blend')))
# Keep original Source groups in the work file for independent audit, but
# temporarily unlink references: future sources may use native bone names too.
scene.collection.children.unlink(source_reference)
try:runpy.run_path(str(kit/'prepare-biped-weighted4.py'),run_name='__main__')
finally:scene.collection.children.link(source_reference)
from binds import correct_native_binds
correct_native_binds(ROOT/'kit/starter'/hunter.lower()/'biped_weighted4.glb',native_bind,ROOT)
(ROOT/'kit/starter/characters.json').write_text(json.dumps({'format':1,'id':hunter.lower()+'-sourceio-lod0-v1','models':[entry]},indent=2)+'\n')
print('SOURCEIO_RETARGET',hunter,len(verts),len(faces),dict(target_hist))
