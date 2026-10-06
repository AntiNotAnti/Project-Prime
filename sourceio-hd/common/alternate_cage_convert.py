"""Blender authoring worker for native-driven Source cage Weighted4 alternates."""
import argparse,json,runpy,sys,hashlib
from collections import defaultdict
from pathlib import Path
import bpy,numpy as np
from mathutils import Matrix,Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from alternate_native import import_alternate
from alternate_cage_geometry import fitted_triangles
from alternate_cage_normals import restore
from alternate_rigid_materials import copies
p=argparse.ArgumentParser();p.add_argument('--config',required=True);p.add_argument('--source',required=True);p.add_argument('--output',required=True);p.add_argument('--repo',required=True);a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);cfg=json.loads(Path(a.config).read_text());root=Path(a.output);kit=root/'kit';repo=Path(a.repo);hunter=cfg['hunter'];source=Path(a.source)
assert cfg['skinning']=='weighted4'
contract,oldrig,native,authoritative=import_alternate(kit,hunter,repo/'src/MphRead/Export/modules/mph_common.py');bpy.ops.wm.save_as_mainfile(filepath=str(root/'Native_Baseline.blend'))
reference=bpy.data.collections.new('REFERENCE_Native_MPH');bpy.context.scene.collection.children.link(reference)
for obj in [*native,oldrig]:
    obj.name='REFERENCE_'+obj.name
    if obj.type=='MESH':obj.parent=None;obj.modifiers.clear();obj.vertex_groups.clear()
    for c in list(obj.users_collection):c.objects.unlink(obj)
    reference.objects.link(obj);obj.hide_render=True;obj.hide_set(True)
for m in bpy.data.materials:m.name='REFERENCE_'+m.name
atlas=json.loads((root/'atlas-layout.json').read_text())['materials'];contract,frames,triangles,report=fitted_triangles(cfg,source,kit,atlas);copies(cfg,root)
T=Matrix(((1,0,0,0),(0,0,-1,0),(0,1,0,0),(0,0,0,1)));scene=bpy.context.scene
# Reuse the authoritative generated armature, including its bind metadata.
rig=oldrig;rig.name='Armature'
for collection in list(rig.users_collection):collection.objects.unlink(rig)
scene.collection.objects.link(rig);rig.hide_set(False);rig.hide_render=False
for bone in rig.pose.bones:bone.matrix_basis=Matrix.Identity(4)
native_bind={name:Matrix(values) for name,values in json.loads(rig['projectPrimeNativeRestMatrices']).items()}
materials={}
for name in sorted({t['material'] for t in triangles}):
    mat=bpy.data.materials.new(name);mat.use_nodes=True;materials[name]=mat
    if name in cfg.get('nativeSupplementMaterials',[]):continue
    nodes=mat.node_tree.nodes;links=mat.node_tree.links;bs=nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.6
    for channel,target in [('albedo','Base Color'),('emissive','Emission Color')]:
        file=root/'textures'/(name+'-'+channel+'.png')
        if file.exists():
            image=nodes.new('ShaderNodeTexImage');image.image=bpy.data.images.load(str(file));image.image.pack();links.new(image.outputs['Color'],bs.inputs[target])
            if channel=='emissive':bs.inputs['Emission Strength'].default_value=1
    file=root/'textures'/(name+'-normal.png');assert file.exists();image=nodes.new('ShaderNodeTexImage');image.image=bpy.data.images.load(str(file));image.image.colorspace_settings.name='Non-Color';image.image.pack();normal=nodes.new('ShaderNodeNormalMap');links.new(image.outputs['Color'],normal.inputs['Color']);links.new(normal.outputs['Normal'],bs.inputs['Normal'])
label=hunter+'_SourceIO_Alternate_ContinuousShell';mesh=bpy.data.meshes.new(label);points=[tuple(T.to_3x3()@Vector(p)) for t in triangles for p in t['positions']];mesh.from_pydata(points,[],[(i,i+1,i+2) for i in range(0,len(points),3)]);mesh.update();names=list(dict.fromkeys(t['material'] for t in triangles))
for n in names:mesh.materials.append(materials[n])
uv=mesh.uv_layers.new(name='UVMap');srcuv=mesh.uv_layers.new(name='SOURCE_UV_Original');normals=[]
for poly,t in zip(mesh.polygons,triangles):
    poly.use_smooth=True;poly.material_index=names.index(t['material'])
    for k,li in enumerate(poly.loop_indices):uv.data[li].uv=(t['uvs'][k][0],1-t['uvs'][k][1]);srcuv.data[li].uv=t['sourceUvs'][k]
    normals.extend(tuple(T.to_3x3()@Vector(n)) for n in t['normals'])
mesh.uv_layers.active_index=0;uv.active_render=True;mesh.normals_split_custom_set(normals);obj=bpy.data.objects.new(label,mesh);scene.collection.objects.link(obj);obj.parent=rig;obj.matrix_parent_inverse=Matrix.Identity(4)
groups={n['name']:obj.vertex_groups.new(name=n['name']) for n in contract['nodes']}
for ti,t in enumerate(triangles):
    for corner,weights in enumerate(t['weights']):
        for name,w in weights.items():groups[name].add([ti*3+corner],w,'REPLACE')
modifier=obj.modifiers.new('Native MPH continuous Weighted4 shell','ARMATURE');modifier.object=rig
previous=set(scene.objects);bpy.ops.import_scene.gltf(filepath=str(source));source_reference=bpy.data.collections.new('REFERENCE_SourceIO_Original');scene.collection.children.link(source_reference)
for o in set(scene.objects)-previous:
    for c in list(o.users_collection):c.objects.unlink(o)
    source_reference.objects.link(o);o.hide_render=True;o.hide_set(True)
report.update(hunter=hunter,skinning='weighted4',sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(),nativeFramesGame={n:m.tolist() for n,m in frames.items()},nativeBindMatricesUnchanged=True,sourceRemeshed=False,sourceSubdivided=False)
(root/'retarget-report.json').write_text(json.dumps(report,indent=2)+'\n');bpy.ops.wm.save_as_mainfile(filepath=str(root/(hunter+'_SourceIO_Alternate_Cage_Weighted4.blend')))
scene.collection.children.unlink(source_reference)
helper=kit/'prepare-altform-weighted4.py';before=hashlib.sha256(helper.read_bytes()).hexdigest()
try:runpy.run_path(str(helper),run_name='__main__')
finally:scene.collection.children.link(source_reference)
assert hashlib.sha256(helper.read_bytes()).hexdigest()==before
model=kit/'starter'/hunter.lower()/'altform_weighted4.glb'
from binds import correct_native_binds
correct_native_binds(model,native_bind,root);restored=restore(root,cfg,source);entry=json.loads((kit/'starter/alternate-form-weighted4-entry.json').read_text());assert set(entry['boneMap'])==set(frames)
(kit/'starter/characters.json').write_text(json.dumps({'format':1,'id':hunter.lower()+'-sourceio-alternate-cage-weighted-v1','models':[entry]},indent=2)+'\n');print('ALTERNATE_CAGE_EXPORTED',hunter,len(triangles),restored)
