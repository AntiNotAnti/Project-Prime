"""Fit SourceIO Samus to the native bind pose without discarding UVs/textures.

Blender: --background --python samus-hd-kit/retarget-samus-source.py
The native reference uses coincident rest bones and carries its actual bind pose
in pose channels. Bake that pose into the rest skeleton before exporting: a
weighted average of inverse pose transforms is NOT an inverse skin transform.
"""
import bpy
import math
from collections import defaultdict
from mathutils import Matrix, Vector
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SOURCE = Path('/Users/jarrett/Downloads/mph/converted-sourceio/models/samus/samus_a.glb')
OUTPUT = ROOT / 'starter/samus/biped_sourceio_weighted4.glb'
BONES = set(['Pelvis','L_hip','L_knee','L_ankle','R_hip','R_knee','R_ankle',
             'Spine_1','Spine_2','Head_1','L_shoulder','L_elbow','L_wrist',
             'L_varias2_SDK','R_shoulder','R_elbow','R_varias2_SDK'])
MATERIALS = {'Samus_Armor':'body','Samus_Armor2':'body','Samus_Armor3':'body',
             'Samus_Armor4':'body','Samus_Light':'body_full_bright',
             'ArmCanon1':'Gun','ArmCanon2':'Gun','ArmCanon3':'GunTip'}
MAP = {'ValveBiped.Bip01_Pelvis':'Pelvis','ValveBiped.Bip01_Spine':'Spine_1',
       'ValveBiped.Bip01_Spine1':'Spine_2','ValveBiped.Bip01_Spine2':'Spine_2',
       'ValveBiped.Bip01_Neck1':'Head_1','ValveBiped.Bip01_Head1':'Head_1'}
for side in ('L','R'):
    for src,dst in [('Thigh','hip'),('Calf','knee'),('Foot','ankle'),
                    ('UpperArm','shoulder'),('Forearm','elbow')]:
        MAP[f'ValveBiped.Bip01_{side}_{src}'] = f'{side}_{dst}'
    MAP[f'Shoulder_{side}'] = f'{side}_varias2_SDK'
    hand = 'L_wrist' if side == 'L' else 'R_elbow'
    MAP[f'bip_hand_{side}'] = hand
    for finger in ('index','middle','pinky','ring','thumb'):
        for segment in range(3): MAP[f'bip_{finger}_{segment}_{side}'] = hand

# Source faces -Y while the native reference faces +Y in Blender; turn the
# whole source first so left/right limbs and the armor's front remain coherent.
TURN = Matrix.Rotation(math.pi, 3, 'Z')

def influences(obj, vertex):
    result = defaultdict(float)
    for a in vertex.groups:
        if a.weight < 1e-6: continue
        name = obj.vertex_groups[a.group].name
        if name not in MAP: raise RuntimeError(f'Unmapped influence {name}')
        result[MAP[name]] += a.weight
    result = sorted(result.items(),key=lambda v:v[1],reverse=True)[:4]
    total = sum(w for _,w in result)
    if total < 1e-6: raise RuntimeError('Unweighted vertex')
    return [(n,w/total) for n,w in result]

def main():
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/'Samus_HD_Work.blend'))
    scene = bpy.context.scene
    native = bpy.data.objects['Armature']
    poses = {b.name:b.matrix.copy() for b in native.pose.bones}
    heads = {n:poses[n].translation for n in poses}
    previous = set(scene.objects)
    bpy.ops.import_scene.gltf(filepath=str(SOURCE))
    imported = [o for o in scene.objects if o not in previous]
    source = next(o for o in imported if o.type=='ARMATURE')
    meshes = [o for o in imported if o.name in {'Samus.smd','Samus_Arm1.smd'}]
    if len(meshes)!=2: raise RuntimeError('Body and armed bodygroup required')
    sb = source.data.bones

    # One frame per target joint. Collapsed spine/neck/finger groups share an
    # anchor, rather than moving portions of the same shell to different heads.
    anchors = {'Pelvis':'ValveBiped.Bip01_Pelvis','Spine_1':'ValveBiped.Bip01_Spine',
               'Spine_2':'ValveBiped.Bip01_Spine1','Head_1':'ValveBiped.Bip01_Head1'}
    chains = {}
    for side in ('L','R'):
        for dst,src in [('hip','Thigh'),('knee','Calf'),('ankle','Foot'),
                        ('shoulder','UpperArm'),('elbow','Forearm')]:
            anchors[f'{side}_{dst}']=f'ValveBiped.Bip01_{side}_{src}'
        anchors[f'{side}_varias2_SDK']=f'Shoulder_{side}'
        chains[f'{side}_hip']=(f'ValveBiped.Bip01_{side}_Calf',heads[f'{side}_knee'])
        chains[f'{side}_knee']=(f'ValveBiped.Bip01_{side}_Foot',heads[f'{side}_ankle'])
        chains[f'{side}_shoulder']=(f'ValveBiped.Bip01_{side}_Forearm',heads[f'{side}_elbow'])
        wrist = heads['L_wrist'] if side=='L' else (heads['R_elbow']+
            (heads['R_elbow']-heads['R_shoulder']).normalized()*0.28125)
        chains[f'{side}_elbow']=(f'bip_hand_{side}',wrist)
    anchors['L_wrist']='bip_hand_L'
    frames={}
    for name,anchor in anchors.items():
        origin=TURN @ sb[anchor].head_local
        rotation=TURN
        length_scale=1.0
        if name in chains:
            end,target_end=chains[name]
            direction=TURN @ (sb[end].head_local-sb[anchor].head_local)
            target_direction=target_end-heads[name]
            rotation=direction.normalized().rotation_difference(target_direction.normalized()).to_matrix() @ TURN
            length_scale=target_direction.length/direction.length
        elif name=='L_wrist':
            # Extend the hand outwards along the forearm, not back to the elbow.
            direction=TURN @ (sb['bip_hand_L'].head_local-sb['ValveBiped.Bip01_L_Forearm'].head_local)
            target_direction=heads['L_wrist']-heads['L_elbow']
            rotation=direction.normalized().rotation_difference(target_direction.normalized()).to_matrix() @ TURN
        frames[name]=(sb[anchor].head_local.copy(),rotation,length_scale)

    vertices=[]; weights=[]; faces=[]; uv_faces=[]; mat_faces=[]
    source_materials={}
    for obj in meshes:
        offset=len(vertices)
        for v in obj.data.vertices:
            ws=influences(obj,v)
            point=Vector((0,0,0))
            for name,weight in ws:
                origin,rotation,scale=frames[name]
                point += (heads[name]+rotation @ (v.co-origin)*scale)*weight
            vertices.append(point); weights.append(ws)
        obj.data.calc_loop_triangles()
        uv=obj.data.uv_layers.active
        for tri in obj.data.loop_triangles:
            mat=obj.data.materials[obj.data.polygons[tri.polygon_index].material_index]
            source_materials[mat.name]=mat
            faces.append(tuple(offset+i for i in tri.vertices))
            uv_faces.append([uv.data[i].uv.copy() for i in tri.loops])
            mat_faces.append(mat.name)
    # The old exporter also cleared/re-added material slots after assigning
    # polygon indices, silently turning every triangle into the body material.
    ordered=sorted(source_materials)
    mesh=bpy.data.meshes.new('Samus_SourceIO_BipedMesh'); mesh.from_pydata(vertices,[],faces); mesh.update()
    for name in ordered:
        original=source_materials[name]
        mat=original.copy(); mat.name='SourceIO_'+name
        mesh.materials.append(mat)
    uv=mesh.uv_layers.new(name='UVMap')
    for p,coords,mat_name in zip(mesh.polygons,uv_faces,mat_faces):
        p.material_index=ordered.index(mat_name); p.use_smooth=True
        for loop,coord in zip(p.loop_indices,coords): uv.data[loop].uv=coord
    obj=bpy.data.objects.new('Samus_SourceIO_Biped',mesh);scene.collection.objects.link(obj)
    obj.parent=native; obj.matrix_parent_inverse=Matrix.Identity(4)
    groups={n:obj.vertex_groups.new(name=n) for n in BONES}
    for index,ws in enumerate(weights):
        for name,weight in ws: groups[name].add([index],weight,'REPLACE')

    # Replace coincident placeholder rest bones with the actual native bind
    # frames. This makes each inverse bind undo exactly its own native frame.
    bpy.ops.object.select_all(action='DESELECT'); native.select_set(True)
    bpy.context.view_layer.objects.active=native
    bpy.ops.object.mode_set(mode='EDIT')
    for b in native.data.edit_bones: b.matrix=poses[b.name]
    bpy.ops.object.mode_set(mode='OBJECT')
    for b in native.pose.bones: b.matrix_basis=Matrix.Identity(4)
    for b in native.data.bones: b.use_deform=b.name in BONES
    bpy.context.view_layer.update()
    # Ensure the baked rest hierarchy exactly reconstructs the reference pose.
    error=max(max(abs(native.data.bones[n].matrix_local[r][c]-poses[n][r][c])
                  for r in range(4) for c in range(4)) for n in BONES)
    if error>1e-5: raise RuntimeError(f'Native bind frame drift: {error}')

    bpy.context.view_layer.objects.active=obj; obj.select_set(True); native.select_set(False)
    subdivision=obj.modifiers.new('Source geometry density','SUBSURF')
    subdivision.subdivision_type='SIMPLE';subdivision.levels=1
    bpy.ops.object.modifier_apply(modifier=subdivision.name)
    mesh.calc_loop_triangles()
    if len(mesh.loop_triangles)>40000:
        trim=obj.modifiers.new('Geometry budget','DECIMATE');trim.ratio=39000/len(mesh.loop_triangles)
        bpy.ops.object.modifier_apply(modifier=trim.name)
    # Subdivision/decimation may interpolate >4 groups at a joint.
    for v in mesh.vertices:
        ws=sorted([(a.group,a.weight) for a in v.groups if a.weight>1e-6],key=lambda a:a[1],reverse=True)
        keep=ws[:4];total=sum(w for _,w in keep)
        for group,_ in ws[4:]: obj.vertex_groups[group].remove([v.index])
        for group,w in keep: obj.vertex_groups[group].add([v.index],w/total,'REPLACE')
    arm=obj.modifiers.new('Native Samus skin','ARMATURE');arm.object=native
    for other in list(scene.objects):
        if other not in (obj,native): bpy.data.objects.remove(other,do_unlink=True)
    bpy.ops.object.select_all(action='DESELECT');native.select_set(True);obj.select_set(True)
    bpy.context.view_layer.objects.active=native
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Samus_SourceIO_Retargeted.blend'))
    bpy.ops.export_scene.gltf(filepath=str(OUTPUT),export_format='GLB',use_selection=True,
                             export_animations=False,export_def_bones=True,export_yup=True)
    # Keep each source texture/UV primitive, while using native render policy.
    import json,struct
    data=OUTPUT.read_bytes(); length=struct.unpack_from('<I',data,12)[0]
    doc=json.loads(data[20:20+length]); binary=data[28+length:]
    for mat in doc['materials']:
        mat['name']=MATERIALS[mat['name'].removeprefix('SourceIO_')]
    payload=json.dumps(doc,separators=(',',':')).encode();payload+=b' '*((-len(payload))%4)
    OUTPUT.write_bytes(struct.pack('<III',0x46546c67,2,28+len(payload)+len(binary))+
                      struct.pack('<II',len(payload),0x4e4f534a)+payload+
                      struct.pack('<II',len(binary),0x004e4942)+binary)
    # Verify the shipping inverse binds against the exact native frame, not
    # merely against the list of allowed joint names.
    convert=Matrix.Rotation(-math.pi/2,4,'X')
    skin=doc['skins'][0]
    accessor=doc['accessors'][skin['inverseBindMatrices']]
    view=doc['bufferViews'][accessor['bufferView']]
    offset=view.get('byteOffset',0)+accessor.get('byteOffset',0)
    bind_error=0.0
    for i,node_index in enumerate(skin['joints']):
        name=doc['nodes'][node_index]['name']
        values=struct.unpack_from('<16f',binary,offset+i*64)
        inverse=Matrix([values[j*4:j*4+4] for j in range(4)]).transposed()
        reconstructed=(convert @ poses[name]) @ inverse
        bind_error=max(bind_error,max(abs(reconstructed[r][c]-(r==c))
                       for r in range(4) for c in range(4)))
    if bind_error>1e-4: raise RuntimeError(f'Exported native bind error: {bind_error}')
    print(f'Exported inverse bind reconstruction error={bind_error}')
    print(f'Baked native bind error={error}; preserved {len(ordered)} source materials/UVs')
    print(f'Wrote {OUTPUT}')
main()
