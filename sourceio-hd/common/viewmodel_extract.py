"""Select the armed Source forearm/weapon through its audited bone collapse table."""
import sys,json,argparse,hashlib,bpy,re
from pathlib import Path
from collections import Counter
p=argparse.ArgumentParser();p.add_argument('--config',required=True);p.add_argument('--output',required=True);p.add_argument('--source-root',required=True);a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);cp=Path(a.config).resolve();cfg=json.loads(cp.read_text());root=Path(a.output).resolve();root.mkdir(parents=True,exist_ok=True);source=Path(a.source_root)/cfg['sourceRelative'];assert hashlib.sha256(source.read_bytes()).hexdigest()==cfg['sourceSha256'];mapping=json.loads((cp.parent/'bone-map.json').read_text())
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(source));scene=bpy.context.scene;original=list(scene.objects);vertices=[];faces=[];uvs=[];normals=[];materials=[];triangles=Counter();source_records=[]
for name in cfg['bodygroups']:
 o=bpy.data.objects[name];o.data.calc_loop_triangles()
 def belongs(v):
  if name in cfg.get('handOnlyBodygroups',[]):
   return sum(g.weight for g in v.groups if re.search(r'Hand|Finger|Thumb|Index|Pinky|Ring|Middle',o.vertex_groups[g.group].name,re.I) and re.search(r'_R(?:_|$)',o.vertex_groups[g.group].name,re.I))
  return sum(g.weight for g in v.groups if mapping[o.vertex_groups[g.group].name]==cfg['collapsedJoint'])
 for tri in o.data.loop_triangles:
  if sum(belongs(o.data.vertices[i]) for i in tri.vertices)/3<cfg['minimumAverageWeight']:continue
  mat=o.data.materials[o.data.polygons[tri.polygon_index].material_index]
  if mat.name not in [m.name for m in materials]:materials.append(mat)
  face=[]
  for vi,li in zip(tri.vertices,tri.loops):
   face.append(len(vertices));vertices.append(o.data.vertices[vi].co.copy());uvs.append(o.data.uv_layers.active.data[li].uv.copy());n=o.data.corner_normals[li].vector.copy();normals.append(n.normalized() if n.length>1e-6 else tri.normal.copy())
  faces.append(tuple(face));triangles[o.name]+=1;source_records.append({'object':o.name,'triangle':tri.index,'material':mat.name,'vertices':list(tri.vertices)})
assert len(faces)>50
mesh=bpy.data.meshes.new(cfg['hunter']+'_SourceWeapon');mesh.from_pydata(vertices,[],faces);mesh.update()
for m in materials:mesh.materials.append(m)
layer=mesh.uv_layers.new(name='UVMap')
for poly,record in zip(mesh.polygons,source_records):
 poly.material_index=[m.name for m in materials].index(record['material']);poly.use_smooth=True
 for li,vi in zip(poly.loop_indices,poly.vertices):layer.data[li].uv=uvs[vi]
mesh.normals_split_custom_set(normals);obj=bpy.data.objects.new(mesh.name,mesh);scene.collection.objects.link(obj)
for o in original:o.hide_render=True;o.hide_set(True)
bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
bpy.ops.export_scene.gltf(filepath=str(root/'source-weapon.glb'),export_format='GLB',use_selection=True,export_animations=False,export_yup=True)
bpy.ops.wm.save_as_mainfile(filepath=str(root/'Source_Weapon_Selection.blend'))
texture_cfg={'hunter':cfg['hunter'],'sourceMeshes':[mesh.name],'compactPeriodicUVs':True,'reuseSingleMaterialTextures':True,'powerOfTwoAtlases':False}
for key in ['sourceMaterialRoot','sourceDecodedTextureRoot','sourceMaterialMaxDimension']:
 if key in cfg:texture_cfg[key]=cfg[key]
(root/'atlas-config.json').write_text(json.dumps(texture_cfg,indent=2)+'\n');materialmap={m.name:cfg.get('sourceMaterialMap',{}).get(m.name,cfg['mainMaterial']) for m in materials};(root/'material-map.json').write_text(json.dumps(materialmap,indent=2)+'\n')
(root/'selection.json').write_text(json.dumps({'sourceSha256':cfg['sourceSha256'],'triangles':len(faces),'objects':dict(triangles),'materials':list(materialmap),'sourceRecords':source_records},indent=2)+'\n')
print('SOURCE WEAPON SELECTED',cfg['hunter'],dict(triangles),list(materialmap))
