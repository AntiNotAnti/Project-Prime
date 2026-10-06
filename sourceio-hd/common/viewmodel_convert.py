"""Fit Source weapon surfaces to native nodes using the generated rigid helper."""
import bpy,sys,argparse,json,math,runpy,hashlib,struct
from pathlib import Path
from collections import defaultdict,Counter
from mathutils import Matrix,Vector
from mathutils.kdtree import KDTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
from viewmodel_native import read_native,TO_BLENDER
from glb import load,read
p=argparse.ArgumentParser();p.add_argument('--config',required=True);p.add_argument('--output',required=True);p.add_argument('--kit',required=True);a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);cp=Path(a.config).resolve();cfg=json.loads(cp.read_text());root=Path(a.output).resolve();kit=Path(a.kit).resolve();hunter=cfg['hunter'];bpy.ops.wm.open_mainfile(filepath=str(root/'Source_Weapon_Selection.blend'));scene=bpy.context.scene;source=bpy.data.objects[hunter+'_SourceWeapon'];source.data.calc_loop_triangles();source_matnames=[m.name for m in source.data.materials]
model,animate,native=read_native(kit);frames=animate();T=TO_BLENDER;Ti=T.inverted();atlas=json.loads((root/'atlas-layout.json').read_text())['materials'];primary=cfg['mainMaterial'];excluded=set(cfg.get('partitionExcludedNodes',[]));fxmats=set(cfg['nativeEffectMaterials']);fxnodes=set(cfg.get('nativeEffectNodes',[]))
# Native surfaces define fit and mechanical region seeds. Supporting effect/core
# surfaces remain native instead of being used as a body shell region.
surfaces=[s for s in native if s['material'] not in fxmats and s['node'] not in excluded]
points=[frames[s['node']]@s['vertices'][i] for s in surfaces for f in s['faces'] for i in f];bounds=[[min(v[i] for v in points),max(v[i] for v in points)] for i in range(3)]
front=[v for v in points if v.z>bounds[2][1]-.003];visual_front=Vector(((min(v.x for v in front)+max(v.x for v in front))/2,(min(v.y for v in front)+max(v.y for v in front))/2,bounds[2][1]));target=Vector(json.loads((kit/'viewmodel-native-idle.json').read_text())['gameplayEmitter']);axis=Vector(cfg['sourceAxis']).normalized();up=Vector((0,0,1))-axis*axis.z
if up.length<1e-4:up=Vector((0,1,0))-axis*axis.y
up.normalize();side=up.cross(axis).normalized();up=axis.cross(side).normalized()
roll=Matrix.Rotation(math.radians(cfg.get('sourceRollDegrees',0)),3,axis);side=roll@side;up=roll@up
origin=Vector(cfg['sourceMuzzle']);coords=[v.co for v in source.data.vertices];rear=min((v-origin).dot(axis) for v in coords);width=max(v.dot(side) for v in coords)-min(v.dot(side) for v in coords);height=max(v.dot(up) for v in coords)-min(v.dot(up) for v in coords);radial=min((bounds[0][1]-bounds[0][0])/width,(bounds[1][1]-bounds[1][0])/height);axial=(target.z-bounds[2][0])/-rear
assert radial>0 and axial>0
fit=Matrix.Identity(4)
for c in range(3):fit[0][c]=side[c]*radial;fit[1][c]=up[c]*radial;fit[2][c]=axis[c]*axial
fit.translation=target-fit.to_3x3()@origin;nf=fit.to_3x3().inverted().transposed();assert (fit@origin-target).length<1e-6
seeds={};material_seeds=defaultdict(dict)
for node in sorted({s['node'] for s in surfaces}):
 members=[s for s in surfaces if s['node']==node];total=0.;center=Vector()
 for s in members:
  for face in s['faces']:
   aa,bb,cc=[frames[node]@s['vertices'][i] for i in face];area=(bb-aa).cross(cc-aa).length/2;center+=(aa+bb+cc)*(area/3);total+=area
 assert total>0;seeds[node]=center/total
 for material in {s['material'] for s in members}:material_seeds[material][node]=seeds[node]
# A convex nearest-seed cell is intersected with every triangle. The cuts keep
# every selected Source surface point/UV and preserve total area at idle.
def clip(poly,normal,constant):
 out=[]
 for i,aa in enumerate(poly):
  bb=poly[(i+1)%len(poly)];da=constant-normal.dot(aa[0]);db=constant-normal.dot(bb[0])
  if da>=-1e-8:out.append(aa)
  if (da>1e-8 and db< -1e-8) or (da< -1e-8 and db>1e-8):
   t=da/(da-db);out.append(tuple(aa[k].lerp(bb[k],t) for k in range(3)))
 return out
parts=defaultdict(lambda:{'verts':[],'faces':[],'uvs':[],'normals':[],'materials':[],'faceMaterials':[]});source_area=0.;cut_area=0.;source_tris=0
for tri in source.data.loop_triangles:
 matname=source_matnames[source.data.polygons[tri.polygon_index].material_index];spec=atlas[matname];runtime=spec['runtimeMaterial'];poly=[];uvs=[source.data.uv_layers.active.data[l].uv.copy() for l in tri.loops];shift=[math.floor(min(v[k] for v in uvs)) for k in range(2)] if spec.get('compactPeriodicUVs') else [0,0]
 for vi,li,uv in zip(tri.vertices,tri.loops,uvs):
  n=nf@source.data.corner_normals[li].vector
  if n.length<1e-6:n=nf@tri.normal
  poly.append((fit@source.data.vertices[vi].co,Vector([(uv[k]-shift[k])*spec['atlasUVScale'][k]+spec['atlasUVOffset'][k] for k in range(2)]),n.normalized()))
 source_area+=(poly[1][0]-poly[0][0]).cross(poly[2][0]-poly[0][0]).length/2;source_tris+=1
 # Continuous Source shells cannot be cut onto unrelated moving native parts:
 # coincident seam vertices separate as soon as those parts animate. A whole
 # weapon/hand can instead follow one native presentation node coherently.
 rigid_node=cfg.get('sourceRigidNode')
 if rigid_node:
  assert rigid_node in frames,('Unknown coherent Source attachment',rigid_node)
  candidates={rigid_node:Vector()}
 else:candidates=material_seeds[runtime] if cfg.get('sourceMaterialMap') and material_seeds[runtime] else seeds
 for node,center in candidates.items():
  piece=poly
  for other,point in candidates.items():
   if other==node:continue
   piece=clip(piece,2*(point-center),point.length_squared-center.length_squared)
   if len(piece)<3:break
  if len(piece)<3:continue
  part=parts[node]
  for i in range(1,len(piece)-1):
   corners=[piece[0],piece[i],piece[i+1]];area=(corners[1][0]-corners[0][0]).cross(corners[2][0]-corners[0][0]).length/2
   if area<1e-12:continue
   cut_area+=area;start=len(part['verts']);part['faces'].append((start,start+1,start+2));part['faceMaterials'].append(runtime)
   for xyz,uv,n in corners:part['verts'].append(T@xyz);part['uvs'].append(uv);part['normals'].append((T.to_3x3()@n).normalized())
assert abs(source_area-cut_area)<max(1e-7,source_area*1e-5),(source_area,cut_area)
for obj in list(scene.objects):obj.hide_render=True;obj.hide_set(True)
for mat in list(bpy.data.materials):mat.name='REFERENCE_'+mat.name
materials={}
for name in sorted(set(s['runtimeMaterial'] for s in atlas.values())):
 mat=bpy.data.materials.new(name);mat.use_nodes=True;nodes=mat.node_tree.nodes;links=mat.node_tree.links;bs=nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.6
 albedo=nodes.new('ShaderNodeTexImage');albedo.image=bpy.data.images.load(str(root/'textures'/(name+'-albedo.png')));albedo.image.pack();links.new(albedo.outputs['Color'],bs.inputs['Base Color']);normal=nodes.new('ShaderNodeTexImage');normal.image=bpy.data.images.load(str(root/'textures'/(name+'-normal.png')));normal.image.colorspace_settings.name='Non-Color';normal.image.pack();nm=nodes.new('ShaderNodeNormalMap');links.new(normal.outputs['Color'],nm.inputs['Color']);links.new(nm.outputs['Normal'],bs.inputs['Normal']);materials[name]=mat
 emission_path=root/'textures'/(name+'-emissive.png')
 if emission_path.exists():
  emission=nodes.new('ShaderNodeTexImage');emission.image=bpy.data.images.load(str(emission_path));emission.image.pack();links.new(emission.outputs['Color'],bs.inputs['Emission Color']);bs.inputs['Emission Strength'].default_value=1.
# Match the Source finish on retained interior mechanical surfaces. Their
# animation/material identities remain native; artwork and UVs follow the
# nearest Source shell instead of showing a contrasting DS filler texture.
interior_sources=cfg.get('internalMaterialSources',{})
for name,source_name in interior_sources.items():
 mat=materials[source_name].copy();mat.name=name;materials[name]=mat
 for channel in ['albedo','normal']:
  import shutil
  shutil.copy2(root/'textures'/(source_name+'-'+channel+'.png'),root/'textures'/(name+'-'+channel+'.png'))
rigdata=bpy.data.armatures.new('Native_'+model['model']);rig=bpy.data.objects.new(rigdata.name,rigdata);scene.collection.objects.link(rig);rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
for n in model['nodes']:
 bone=rigdata.edit_bones.new(n['name']);bone.head=(0,0,0);bone.tail=(0,.025,0);bone.matrix=T@frames[n['name']]@Ti
 if n['parentName']:bone.parent=rigdata.edit_bones[n['parentName']]
bpy.ops.object.mode_set(mode='OBJECT')
# No Armature modifier: authoring surfaces are already in the native idle pose.
# Full pose frames retain any scale the edit-bone matrices cannot encode.
for n in model['nodes']:
 rig.pose.bones[n['name']].matrix=T@frames[n['name']]@Ti
 bpy.context.view_layer.update()
for n in model['nodes']:
 desired=T@frames[n['name']]@Ti;actual=rig.pose.bones[n['name']].matrix
 error=max(abs(actual[r][c]-desired[r][c]) for r in range(4) for c in range(4))
 assert error<1e-5,('Native rigid authoring frame changed',hunter,n['name'],error)
source_objects={};face_records=defaultdict(list)
def make(node,points,faces,uvs,normals,face_mats,label):
 mesh=bpy.data.meshes.new(label);mesh.from_pydata(points,[],faces);mesh.update();names=list(dict.fromkeys(face_mats))
 for name in names:mesh.materials.append(materials.get(name) or bpy.data.materials.get(name) or bpy.data.materials.new(name))
 uv=mesh.uv_layers.new(name='UVMap')
 for poly,mat in zip(mesh.polygons,face_mats):
  poly.use_smooth=True;poly.material_index=names.index(mat)
  for li,vi in zip(poly.loop_indices,poly.vertices):uv.data[li].uv=uvs[vi]
 if normals:mesh.normals_split_custom_set(normals)
 obj=bpy.data.objects.new(label,mesh);scene.collection.objects.link(obj);obj.parent=rig;obj.vertex_groups.new(name=node).add(list(range(len(mesh.vertices))),1.,'REPLACE')
 return obj
for node,part in parts.items():source_objects[node]=make(node,part['verts'],part['faces'],part['uvs'],part['normals'],part['faceMaterials'],'Source_Weapon_'+node)
native_effect_count=0;effect_nodes=set();native_color_records=defaultdict(list)
for s in native:
 if s['material'] not in fxmats and s['node'] not in fxnodes:continue
 if s['node'] in cfg.get('nativeEffectExcludedNodes',[]):continue
 if s['material'] in cfg.get('nativeEffectExcludedMaterials',[]):continue
 if 'nativeEffectAllowedNodes' in cfg and s['node'] not in cfg['nativeEffectAllowedNodes']:continue
 for face,values,colors in zip(s['faces'],s['uvs'],s['colors']):
  for vi,uv,color in zip(face,values,colors):native_color_records[(s['node'],s['material'])].append((s['vertices'][vi],Vector((uv[0],1-uv[1])),color))
 pts=[];faces=[];uvs=[]
 for face,values in zip(s['faces'],s['uvs']):
  start=len(pts);pts.extend(T@frames[s['node']]@s['vertices'][i] for i in face);faces.append((start,start+1,start+2))
  if s['material'] in interior_sources:
   shell=[(xyz,uv) for p in parts.values() for xyz,uv in zip(p['verts'],p['uvs'])]
   uvs.extend(min(shell,key=lambda pair:(pair[0]-xyz).length_squared)[1] for xyz in pts[start:start+3])
  else:uvs.extend(Vector(v) for v in values)
 make(s['node'],pts,faces,uvs,None,[s['material']]*len(faces),'Native_Effect_'+s['node']+'_'+s['name']);effect_nodes.add(s['node']);native_effect_count+=len(faces)
blend=root/(hunter+'_SourceIO_FirstPerson_Rigid.blend');bpy.ops.wm.save_as_mainfile(filepath=str(blend));helper=root/'prepare-viewmodel-rigid.py';helper_sha=hashlib.sha256(helper.read_bytes()).hexdigest();assert helper.read_bytes()==(kit/'prepare-viewmodel-rigid.py').read_bytes();runpy.run_path(str(helper),run_name='__main__');assert hashlib.sha256(helper.read_bytes()).hexdigest()==helper_sha
# Add authored normals after the strict generated geometry/group validator.
# Positions/UVs are copied exactly from the generated file; vertices are split
# per corner so independently authored seam normals cannot be welded away.
path=root/'starter'/hunter.lower()/'viewmodel.glb';doc,blob=load(path);binary=bytearray(blob)
def add(values,kind,fmt):
 while len(binary)%4:binary.append(0)
 count={'VEC3':3,'VEC2':2,'SCALAR':1}[kind];view=len(doc['bufferViews']);data=struct.pack('<'+fmt*count*len(values),*(v for row in values for v in row));doc['bufferViews'].append({'buffer':0,'byteOffset':len(binary),'byteLength':len(data)});binary.extend(data);access=len(doc['accessors']);entry={'bufferView':view,'componentType':5126 if fmt=='f' else 5125,'count':len(values),'type':kind}
 if kind=='VEC3':entry.update(min=[min(v[k] for v in values) for k in range(3)],max=[max(v[k] for v in values) for k in range(3)])
 doc['accessors'].append(entry);return access
maximum=0.;patched=0;triangle_count=0;normal_error=0.
for node in doc['nodes']:
 if 'mesh' not in node:continue
 name=node['name']
 for primitive in doc['meshes'][node['mesh']]['primitives']:
  indices=[v[0] for v in read(doc,blob,primitive['indices'])];triangle_count+=len(indices)//3;matname=doc['materials'][primitive['material']]['name']
  if name not in source_objects or matname not in materials or matname in interior_sources:continue
  part=parts[name];obj=source_objects[name];byface=defaultdict(list);inverse=frames[name].inverted()
  def key(xyz,uv):return tuple(round(v,5) for v in (*xyz,*uv))
  for face,fmat in zip(part['faces'],part['faceMaterials']):
   if fmat!=matname:continue
   records=[(inverse@Ti@part['verts'][i],Vector((part['uvs'][i].x,1-part['uvs'][i].y)),(frames[name].to_3x3().transposed()@Ti.to_3x3()@part['normals'][i]).normalized()) for i in face]
   byface[tuple(sorted(key(x,u) for x,u,n in records))].append(records)
  facekeys=list(byface);tree=KDTree(len(facekeys))
  for ix,fkey in enumerate(facekeys):tree.insert(sum((r[0] for r in byface[fkey][0]),Vector())/3,ix)
  tree.balance()
  positions=read(doc,blob,primitive['attributes']['POSITION']);texcoords=read(doc,blob,primitive['attributes']['TEXCOORD_0']);newp=[];newuv=[];newnorm=[]
  for start in range(0,len(indices),3):
   ids=indices[start:start+3];keys=[key(positions[i],texcoords[i]) for i in ids];bucket=byface.get(tuple(sorted(keys)))
   if not bucket:
    centroid=sum((Vector(positions[i]) for i in ids),Vector())/3;near=tree.find_range(centroid,3e-5);matches=[]
    for point,index,distance in near:
     b=byface[facekeys[index]]
     if not b:continue
     score=max(min((r[0]-Vector(positions[i])).length+(r[1]-Vector(texcoords[i])).length for r in b[-1]) for i in ids)
     matches.append((score,index))
    assert matches,('No matching exported Source triangle',hunter,name,keys,tree.find(centroid))
    score,index=min(matches);assert score<3e-5,('Exported Source surface changed',hunter,name,score);bucket=byface[facekeys[index]]
   records=bucket.pop()
   for i in ids:
    x,u,n=min(records,key=lambda r:(r[0]-Vector(positions[i])).length+(r[1]-Vector(texcoords[i])).length);maximum=max(maximum,(x-Vector(positions[i])).length);newp.append(positions[i]);newuv.append(texcoords[i]);newnorm.append(tuple(n))
  assert all(not b for b in byface.values()),'Generated exporter dropped Source triangles'
  primitive['attributes']={'POSITION':add(newp,'VEC3','f'),'TEXCOORD_0':add(newuv,'VEC2','f'),'NORMAL':add(newnorm,'VEC3','f')};primitive['indices']=add([(i,) for i in range(len(newp))],'SCALAR','I');patched+=len(newp)
assert maximum<1e-5
# Native effect colors are authored per vertex, including untextured glowing
# details. Preserve them instead of turning those surfaces uniformly white.
color_corners=0
for node in doc['nodes']:
 if 'mesh' not in node:continue
 for primitive in doc['meshes'][node['mesh']]['primitives']:
  matname=doc['materials'][primitive['material']]['name'];records=native_color_records.get((node['name'],matname))
  if not records or matname in interior_sources:continue
  positions=read(doc,blob,primitive['attributes']['POSITION']);uvs=read(doc,blob,primitive['attributes']['TEXCOORD_0']);indices=[v[0] for v in read(doc,blob,primitive['indices'])];normals=read(doc,blob,primitive['attributes']['NORMAL']) if 'NORMAL' in primitive['attributes'] else [(0,1,0)]*len(positions)
  tree=KDTree(len(records))
  for i,r in enumerate(records):tree.insert(r[0],i)
  tree.balance();outp=[];outu=[];outn=[];outc=[]
  for i in indices:
   near=tree.find_range(Vector(positions[i]),1e-5);assert near,('Native effect position changed',hunter,node['name'],matname)
   candidates=[records[ix] for point,ix,distance in near];record=min(candidates,key=lambda r:(r[1]-Vector(uvs[i])).length)
   assert (record[1]-Vector(uvs[i])).length<1e-5,('Native effect UV changed',hunter,node['name'],matname)
   outp.append(positions[i]);outu.append(uvs[i]);outn.append(normals[i]);outc.append(record[2]);color_corners+=1
  primitive['attributes']={'POSITION':add(outp,'VEC3','f'),'TEXCOORD_0':add(outu,'VEC2','f'),'NORMAL':add(outn,'VEC3','f'),'COLOR_0':add(outc,'VEC3','f')};primitive['indices']=add([(i,) for i in range(len(outp))],'SCALAR','I')
# Preserve native-only ammo/effect bindings even when their native material
# identity is also used by a Source crystal surface.
for node in doc['nodes']:
 if 'mesh' not in node or node['name'] in source_objects:continue
 for primitive in doc['meshes'][node['mesh']]['primitives']:
  material=doc['materials'][primitive['material']]
  if material['name'] in materials and material['name'] not in interior_sources:
   primitive['material']=len(doc['materials']);doc['materials'].append({'name':material['name']})
while len(binary)%4:binary.append(0)
doc['buffers'][0]['byteLength']=len(binary);encoded=json.dumps(doc,separators=(',',':')).encode();encoded+=b' '*((-len(encoded))%4);path.write_bytes(struct.pack('<4sII',b'glTF',2,28+len(encoded)+len(binary))+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(binary),0x004e4942)+binary)
manifest={'format':1,'id':'sourceio-'+hunter.lower()+'-viewmodel-v1','models':[{'hunter':hunter,'part':'viewModel','model':hunter.lower()+'/viewmodel.glb','skinning':'rigidNodes','boneMap':{n:n for n in sorted(set(parts)|effect_nodes)}}]};(root/'starter/characters.json').write_text(json.dumps(manifest,indent=2)+'\n')
report={'pass':True,'hunter':hunter,'sourceSha256':cfg['sourceSha256'],'sourceSelectedTriangles':source_tris,'sourceCutTriangles':sum(len(p['faces']) for p in parts.values()),'nativeEffectTriangles':native_effect_count,'sourceMaterialNames':sorted(set(s['runtimeMaterial'] for s in atlas.values())),'internalMaterialSources':interior_sources,'totalTriangles':triangle_count,'sourceClosedSurfaceArea':source_area,'cutClosedSurfaceArea':cut_area,'sourceSurfaceAreaError':abs(source_area-cut_area),'rigidSourceNodes':{n:len(v['faces']) for n,v in parts.items()},'coherentSourceAttachment':cfg.get('sourceRigidNode'),'nativeEffectNodes':sorted(effect_nodes),'sourceToNativeFit':[list(r) for r in fit],'nativeClosedBounds':bounds,'nativeMuzzle':list(target),'sourceMuzzle':list(origin),'muzzleFitError':(fit@origin-target).length,'radialScale':radial,'axialScale':axial,'exportLocalPositionError':maximum,'authoredNormalsPatchedCorners':patched,'sourceUvAndGeneratedPositionsUnchanged':True,'nativeEffectColorCorners':color_corners,'nativeIdlePoseSha256':hashlib.sha256((kit/'viewmodel-native-idle.json').read_bytes()).hexdigest(),'nativeVisualFrontLandmark':list(visual_front),'nativeGameplayEmitter':list(target),'nativeGameplayEmitterOffsetUnchanged':True,'exporterUnchanged':True,'exporterSha256':helper_sha,'viewmodelSha256':hashlib.sha256(path.read_bytes()).hexdigest()};(root/'audit.json').write_text(json.dumps(report,indent=2)+'\n');print('VIEWMODEL BUILD PASS',hunter,report)
