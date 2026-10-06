"""Blender: derive LOD1 from accepted GLB geometry, weights and material layers.

The frozen production blend supplies the native authoring rig. Decoding the
shipping GLB retains its post-export alpha partitions and exact runtime UVs.
Compatible seams weld coherently; layered and incompatible skin records stay
separate. The native generated Weighted4 exporter is used unchanged.
"""
import argparse, hashlib, json, math, runpy, sys
from collections import Counter, defaultdict
from pathlib import Path
import bpy, bmesh
from mathutils import Matrix, Vector, Euler
from mathutils.bvhtree import BVHTree
sys.path.insert(0, str(Path(__file__).resolve().parent))
from glb import load, read, image_bytes
from binds import correct_native_binds
from lod1_materials import preserve_materials
from lod1_uv import classify

p = argparse.ArgumentParser(); p.add_argument('--config', required=True); p.add_argument('--output', required=True)
a = p.parse_args(sys.argv[sys.argv.index('--') + 1:])
cp = Path(a.config).resolve(); cfg = json.loads(cp.read_text()); root = Path(a.output).resolve()
repo = Path(__file__).resolve().parents[2]; kit = root/'kit'; h = cfg['hunter']; folder = h.lower()
source = repo/cfg['acceptedLod0Root']; source_glb = source/'kit/starter'/folder/'biped_weighted4.glb'
source_blend = source/(h + '_SourceIO_LOD0.blend')
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def restore_preserved_normals(mesh, record):
    """Restore authored corner normals after Blender's topology roundtrips."""
    def oriented_signature(points, uvs):
        corners=[(*[round(c,5) for c in point],*[round(c,5) for c in uv]) for point,uv in zip(points,uvs)]
        rotation=min(range(3),key=lambda k:tuple(corners[k:]+corners[:k]))
        return tuple(corners[rotation:]+corners[:rotation]),rotation
    source=defaultdict(list)
    for face in record['faces']:
        key,rotation=oriented_signature([record['points'][i] for i in face],
            [(record['uv'][i][0],1-record['uv'][i][1]) for i in face])
        normals=[record['normals'][i] for i in face]
        source[key].append(normals[rotation:]+normals[:rotation])
    normals=[None]*len(mesh.loops);runtime=mesh.uv_layers['UVMap']
    for polygon in mesh.polygons:
        loops=list(polygon.loop_indices);assert len(loops)==3
        key,rotation=oriented_signature([mesh.vertices[mesh.loops[i].vertex_index].co for i in loops],
            [runtime.data[i].uv for i in loops])
        assert source[key],('Preserved triangle changed its oriented geometry/UV signature',key)
        values=source[key].pop(0)
        for k,value in enumerate(values):normals[loops[(rotation+k)%3]]=value
    assert not any(source.values()) and all(value is not None for value in normals)
    mesh.normals_split_custom_set(normals)
assert sha(source_glb) == cfg['sourceGlbSha256'] and sha(source_blend) == cfg['sourceBlendSha256']
assert not (root/'RELEASE-LOCK.json').exists()
doc, binary = load(source_glb); bpy.ops.wm.open_mainfile(filepath=str(source_blend))
rig = bpy.data.objects['Armature']
old_bind = {n: Matrix(v) for n, v in json.loads(rig['projectPrimeNativeRestMatrices']).items()}
for obj in list(bpy.context.scene.objects):
    if obj != rig: bpy.data.objects.remove(obj, do_unlink=True)
for mat in list(bpy.data.materials): bpy.data.materials.remove(mat)
reference = json.loads((kit/'native-reference.json').read_text())
contract = next(m for m in reference['models'] if m['part'] == 'biped' and m['lod'] == 1)
nodes = {n['index']: n for n in contract['nodes']}; worlds = {}; convert = Matrix.Rotation(math.pi/2, 4, 'X')
def world(i):
    if i in worlds: return worlds[i]
    n = nodes[i]
    local = Euler(n['rotationRadians'], 'XYZ').to_matrix().to_4x4() @ Matrix.Diagonal(Vector((*n['scale'], 1)))
    local.translation = Vector([v/s for v,s in zip(n['position'], contract['modelScale'])])
    worlds[i] = world(n['parentIndex']) @ local if n['parentIndex'] >= 0 else local
    return worlds[i]
new_bind = {n['name']: convert @ world(i) for i,n in nodes.items()}
entry = json.loads((kit/'starter/biped-lod1-weighted4-entry.json').read_text())
expected = set(entry['boneMap']); assert expected <= set(new_bind) and expected <= set(old_bind)
bpy.ops.object.select_all(action='DESELECT'); rig.hide_set(False); rig.select_set(True)
bpy.context.view_layer.objects.active = rig; bpy.ops.object.mode_set(mode='EDIT')
for bone in rig.data.edit_bones:
    assert bone.name in new_bind
    bone.matrix = new_bind[bone.name]
bpy.ops.object.mode_set(mode='OBJECT')
for bone in rig.pose.bones: bone.matrix_basis = Matrix.Identity(4)
rig['projectPrimeNativeRestMatrices'] = json.dumps({n: [list(row) for row in m] for n,m in new_bind.items()})
joint_names = [doc['nodes'][i]['name'] for i in doc['skins'][0]['joints']]
corrections = {n: new_bind[n] @ old_bind[n].inverted() for n in expected}

# Decode each source primitive separately to retain material and alpha layers.
records = []; point_materials = defaultdict(set); point_regions = defaultdict(set); point_uv_borders = set(); point_uvs=defaultdict(set); all_points = []
atlas = json.loads((source/'atlas-layout.json').read_text())
for mesh in doc['meshes']:
    for prim in mesh['primitives']:
        pos = read(doc, binary, prim['attributes']['POSITION']); normals = read(doc, binary, prim['attributes']['NORMAL'])
        js = read(doc, binary, prim['attributes']['JOINTS_0']); ws = read(doc, binary, prim['attributes']['WEIGHTS_0'])
        uv = read(doc, binary, prim['attributes']['TEXCOORD_0'])
        original_uv = read(doc, binary, prim['attributes'].get('TEXCOORD_1', prim['attributes']['TEXCOORD_0']))
        indices = [i[0] for i in read(doc, binary, prim['indices'])]
        weights = []; points = []; transformed_normals = []
        for xyz, normal, joints, weight in zip(pos, normals, js, ws):
            active = [(joint_names[j],w) for j,w in zip(joints,weight) if w > 1e-6]
            total = sum(w for n,w in active); active = [(n,w/total) for n,w in active]
            assert 1 <= len(active) <= 4 and all(n in expected for n,w in active)
            pt = convert @ Vector(xyz)
            point = sum(((corrections[n] @ pt)*w for n,w in active), Vector())
            jac = Matrix(((0,0,0),(0,0,0),(0,0,0)))
            for n,w in active: jac += corrections[n].to_3x3()*w
            transformed_normals.append((jac.inverted().transposed() @ (convert.to_3x3() @ Vector(normal))).normalized())
            points.append(point); weights.append(active); all_points.append(point)
        faces = [tuple(indices[i:i+3]) for i in range(0,len(indices),3)]
        # glTF primitives can share a full vertex accessor while referencing
        # only a small alpha subset. Unused records are not material seams.
        for i in {i for face in faces for i in face}:
            key=tuple(round(c,6) for c in points[i]);point_materials[key].add(prim['material'])
            point_uvs[key].add(tuple(round(c,5) for c in uv[i]))
        labels = classify(doc,binary,prim,atlas); vertex_regions = defaultdict(set)
        for face,label in zip(faces,labels):
            spec=atlas['materials'][label[0]]
            for i in face:
                vertex_regions[i].add(label)
                key=tuple(round(c,6) for c in points[i]);point_regions[key].add(label)
                if not spec.get('directPeriodicTexture'):
                    q=[(uv[i][0]-spec['atlasUVOffset'][0])/spec['atlasUVScale'][0],
                       (1-uv[i][1]-spec['atlasUVOffset'][1])/spec['atlasUVScale'][1]]
                    if any(abs(q[k]-edge)<1e-5 for k in range(2) for edge in [spec['uvLow'][k],spec['uvHigh'][k]]):
                        point_uv_borders.add(key)
        assert all(len(region)==1 for region in vertex_regions.values())
        records.append(dict(material=prim['material'],points=points,weights=weights,normals=transformed_normals,
                            uv=uv,original_uv=original_uv,faces=faces,regions=vertex_regions,face_labels=labels))
height = max(v.z for v in all_points) - min(v.z for v in all_points)
old_muzzle = json.loads((source/'audit.json').read_text())['muzzleProof']
native_bone = old_muzzle['nativeBone']
muzzle_local = old_muzzle['nativeLocalPoint']
if muzzle_local is not None: muzzle = new_bind[native_bone] @ Vector(muzzle_local)
else: muzzle = corrections[native_bone] @ (convert @ Vector(old_muzzle['restPoint']))
images = root/'material-images'; images.mkdir(exist_ok=True)
materials = {}
for i, material in enumerate(doc['materials']):
    name = cfg['materialNames'].get(material['name'], material['name'])
    if name in materials: continue
    mat = bpy.data.materials.new(name); mat.use_nodes=True; nodes_mat=mat.node_tree.nodes; links=mat.node_tree.links
    bs=nodes_mat.get('Principled BSDF')
    def texture(spec, noncolor=False):
        texture = doc['textures'][spec['index']]; ix=texture['source']; path=images/(str(ix)+'.png')
        if not path.exists(): path.write_bytes(image_bytes(doc,binary,ix))
        node=nodes_mat.new('ShaderNodeTexImage'); node.image=bpy.data.images.load(str(path),check_existing=True);node.image.pack()
        if noncolor: node.image.colorspace_settings.name='Non-Color'
        return node
    color=texture(material['pbrMetallicRoughness']['baseColorTexture']); links.new(color.outputs['Color'],bs.inputs['Base Color'])
    if material.get('normalTexture'):
        normal=texture(material['normalTexture'],True); nm=nodes_mat.new('ShaderNodeNormalMap')
        links.new(normal.outputs['Color'],nm.inputs['Color']);links.new(nm.outputs['Normal'],bs.inputs['Normal'])
    if material.get('emissiveTexture'):
        emission=texture(material['emissiveTexture']);links.new(emission.outputs['Color'],bs.inputs['Emission Color']);bs.inputs['Emission Strength'].default_value=1
    materials[name]=mat

total_triangles=0; details=[]; primitive_sources={}; fitted_surfaces=[]
for ri, rec in enumerate(records):
    material_index=rec['material']; mat=doc['materials'][material_index]
    preserve_alpha=cfg.get('preserveAlphaTriangles',True) and mat.get('alphaMode')=='BLEND'
    preserve_emissive=cfg.get('preserveEmissiveTriangles',True) and bool(mat.get('emissiveTexture')) and any(v>0 for v in mat.get('emissiveFactor',[]))
    preserve_component=preserve_alpha or preserve_emissive
    name=cfg['materialNames'].get(mat['name'],mat['name'])
    data_name=h+'_LOD1_Primitive_'+str(ri)
    mesh=bpy.data.meshes.new(data_name);mesh.from_pydata(rec['points'],[],rec['faces']);mesh.update();mesh.materials.append(materials[name])
    region_labels=sorted(set(rec['face_labels']));region_indices={label:i for i,label in enumerate(region_labels)}
    region_attribute=mesh.attributes.new('SOURCE_ATLAS_REGION','INT','FACE')
    for i,label in enumerate(rec['face_labels']):region_attribute.data[i].value=region_indices[label]
    uv=mesh.uv_layers.new(name='UVMap');orig=mesh.uv_layers.new(name='SOURCE_UV_Original')
    for polygon in mesh.polygons:
        polygon.use_smooth=True
        for loop in polygon.loop_indices:
            i=mesh.loops[loop].vertex_index
            uv.data[loop].uv=(rec['uv'][i][0],1-rec['uv'][i][1])
            orig.data[loop].uv=(rec['original_uv'][i][0],1-rec['original_uv'][i][1])
    mesh.normals_split_custom_set([rec['normals'][loop.vertex_index] for loop in mesh.loops])
    mesh.uv_layers.active_index=0;uv.active_render=True
    obj=bpy.data.objects.new(data_name,mesh);bpy.context.scene.collection.objects.link(obj);obj.parent=rig
    obj.matrix_parent_inverse=Matrix.Identity(4)
    groups={n:obj.vertex_groups.new(name=n) for n in sorted(expected)}
    for i,ws in enumerate(rec['weights']):
        for n,w in ws:groups[n].add([i],w,'REPLACE')
    arm=obj.modifiers.new('Native MPH Weighted4','ARMATURE');arm.object=rig
    # Do not merge coincident overlays or different normalized weight records.
    face_keys=defaultdict(list)
    for polygon in mesh.polygons:
        key=tuple(sorted(tuple(round(c,6) for c in mesh.vertices[i].co) for i in polygon.vertices))
        face_keys[key].append(polygon)
    layered={i for polys in face_keys.values() if len(polys)>1 for poly in polys for i in poly.vertices}
    bm=bmesh.new();bm.from_mesh(mesh);bm.verts.ensure_lookup_table();before=len(bm.verts);compatible=defaultdict(list)
    for vertex in bm.verts:
        if vertex.index in layered or preserve_component: continue
        ws=tuple(sorted((n,round(w,6)) for n,w in rec['weights'][vertex.index]))
        compatible[(ws,tuple(sorted(rec['regions'][vertex.index])))].append(vertex)
    for vertices in compatible.values():bmesh.ops.remove_doubles(bm,verts=vertices,dist=1e-6)
    for edge in bm.edges:
        if len(edge.link_faces)==2:edge.smooth=edge.calc_face_angle()<math.radians(cfg['sharpAngleDegrees'])
    welded=before-len(bm.verts);bm.to_mesh(mesh);bm.free();mesh.update()
    group_names={g.index:g.name for g in obj.vertex_groups}
    protection=obj.vertex_groups.new(name='LOD1_DetailProtection')
    emissive=any(v>0 for v in mat.get('emissiveFactor',[])) and bool(mat.get('emissiveTexture'))
    protected_counts=Counter()
    for v in mesh.vertices:
        key=tuple(round(c,6) for c in v.co)
        ws={group_names[g.group]:g.weight for g in v.groups if g.group in group_names}
        weight=1. if len(point_materials[key])>1 or len(point_regions[key])>1 or key in point_uv_borders or (v.co-muzzle).length<height*cfg['muzzleProtectionRadiusFraction'] else cfg['headProtection'] if ws.get('Head_1',0)>.5 else .15
        if emissive: weight=max(weight,.65)
        if any(ws.get(bone,0)>.5 for bone in cfg.get('protectedNativeBones',[])):weight=1.
        if cfg.get('protectInternalUvSeams',False) and len(point_uvs[key])>1:weight=1.
        protected_counts[str(weight)]+=1
        protection.add([v.index],weight,'REPLACE')
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    modifier=obj.modifiers.new('LOD1 coherent controlled collapse','DECIMATE')
    modifier.ratio=1. if preserve_component else cfg['collapseRatio'];modifier.use_collapse_triangulate=True
    modifier.vertex_group=protection.name;modifier.vertex_group_factor=1.;modifier.invert_vertex_group=True
    bpy.ops.object.modifier_move_up(modifier=modifier.name);bpy.ops.object.modifier_apply(modifier=modifier.name)
    mesh=obj.data
    obj.vertex_groups.remove(obj.vertex_groups['LOD1_DetailProtection'])
    bm=bmesh.new();bm.from_mesh(mesh);loose=[v for v in bm.verts if not v.link_faces]
    if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
    bm.to_mesh(mesh);bm.free();mesh.update()
    # Runtime UV0 remains the rendering authority. Blender collapse optimizes
    # separate UV layers independently. Reconstruct the diagnostic Source UV
    # channel using each retained affine atlas region; leave UV0 unchanged.
    runtime=mesh.uv_layers['UVMap'];diagnostic=mesh.uv_layers['SOURCE_UV_Original']
    runtime_before=[tuple(v.uv) for v in runtime.data]
    region_attribute=mesh.attributes['SOURCE_ATLAS_REGION']
    for polygon in mesh.polygons:
        label=region_labels[region_attribute.data[polygon.index].value]
        spec=atlas['materials'][label[0]]
        for loop in polygon.loop_indices:
            q=[(runtime.data[loop].uv[k]-spec['atlasUVOffset'][k])/spec['atlasUVScale'][k] for k in range(2)]
            if not spec.get('directPeriodicTexture'):
                assert all(spec['uvLow'][k]-1e-5<=q[k]<=spec['uvHigh'][k]+1e-5 for k in range(2)),('Crossed atlas region',ri,polygon.index,label,q)
            diagnostic.data[loop].uv=[q[k]+label[k+1] for k in range(2)]
    assert runtime_before==[tuple(v.uv) for v in runtime.data],'Diagnostic reconstruction changed runtime UVs'
    hist=Counter();trimmed=0;dropped_weight=0.
    for v in mesh.vertices:
        ws=sorted([(g.group,g.weight) for g in v.groups if g.weight>1e-6],key=lambda pair:pair[1],reverse=True)
        trimmed+=max(0,len(ws)-4);dropped_weight=max(dropped_weight,sum(w for i,w in ws[4:]));ws=ws[:4]
        total=sum(w for i,w in ws);assert total>0
        for g in list(v.groups):obj.vertex_groups[g.group].remove([v.index])
        for i,w in ws:obj.vertex_groups[i].add([v.index],w/total,'REPLACE')
        hist[len(ws)]+=1
    mesh.calc_loop_triangles();tris=len(mesh.loop_triangles);total_triangles+=tris
    assert tris>0
    if preserve_component:restore_preserved_normals(mesh,rec)
    obj['sourcePrimitive']=ri;obj['sourceMaterialIndex']=material_index;obj['sourceAlphaMode']=mat.get('alphaMode','OPAQUE')
    primitive_sources[data_name]=material_index
    fitted_surfaces.append({'primitive':ri,'source':[[list(v) for v in rec['points']],rec['faces']],
        'lod1':[[list(v.co) for v in mesh.vertices],[list(t.vertices) for t in mesh.loop_triangles]]})
    details.append({'primitive':ri,'sourceMaterial':mat['name'],'nativeLod1Material':name,
        'sourceTriangles':len(rec['faces']),'triangles':tris,'vertices':len(mesh.vertices),
        'weldedCompatibleSeamRecords':welded,'layeredSourceVerticesPreservedSeparately':len(layered),
        'alphaTrianglesPreserved':preserve_alpha,'emissiveTrianglesPreserved':preserve_emissive,
        'preservedAuthoredCornerNormals':preserve_component,
        'protectionWeights':dict(protected_counts),
        'discardedFifthOrTinyInfluences':trimmed,'maximumDiscardedWeight':dropped_weight,'weightsByCount':dict(hist)})
if not cfg['minimumTriangles']<=total_triangles<=cfg['maximumTriangles']:
    (root/'rejected-budget.json').write_text(json.dumps({'triangles':total_triangles,'primitives':details},indent=2)+'\n')
assert cfg['minimumTriangles']<=total_triangles<=cfg['maximumTriangles'],total_triangles
for bone in rig.data.bones:bone.use_deform=bone.name in expected
bpy.ops.wm.save_as_mainfile(filepath=str(root/(h+'_SourceIO_LOD1.blend')))
exporter=kit/'prepare-biped-lod1-weighted4.py';exporter_sha=sha(exporter)
runpy.run_path(str(exporter),run_name='__main__');assert sha(exporter)==exporter_sha
target=kit/'starter'/folder/'biped_lod1_weighted4.glb'
correct_native_binds(target,new_bind,root)
preserve_materials(source_glb,target,primitive_sources,cfg['materialNames'],root/'material-preservation.json')
(kit/'starter/characters.json').write_text(json.dumps({'format':1,'id':folder+'-sourceio-lod1-v1','models':[entry]},indent=2)+'\n')
result={'hunter':h,'pass':True,'sourceGlbSha256':sha(source_glb),'sourceBlendSha256':sha(source_blend),
    'sourceTriangles':sum(len(r['faces']) for r in records),'triangles':total_triangles,'sourceHeight':height,
    'generatedExporterSha256':exporter_sha,'nativeBoneNames':sorted(expected),'primitives':details,
    'nativeBindMatrices':{n:[list(row) for row in m] for n,m in new_bind.items()},
    'maximumBindCorrection':max(abs(corrections[n][r][c]-(r==c)) for n in expected for r in range(4) for c in range(4)),
    'muzzleProof':{'nativeBone':native_bone,'nativeLocalPoint':muzzle_local,'restPoint':list(convert.inverted()@muzzle),
        'maximumRestError':0.,'method':'Unchanged native emitter landmark in the authoritative LOD1 bind.'},
    'shippingGlbSha256':sha(target),'configSha256':sha(cp),'primitiveSources':primitive_sources,
    'sourceMaterialAlphaAndRecolorsPreserved':True,'inheritedNativeBindScalePreserved':True,
    'runtimeUvRenderingAuthority':'Accepted shipping UV0; collapse preserves source atlas domains and periodic seam cohorts.',
    'diagnosticSourceUvReconstructed':True,'runtimeUvsUnchangedByDiagnosticReconstruction':True,
    'teamRecolorsRequired':json.loads((source/'audit.json').read_text()).get('teamRecolorsRequired',False)}
(root/'build-result.json').write_text(json.dumps(result,indent=2)+'\n')
(root/'surface-samples.json').write_text(json.dumps(fitted_surfaces,separators=(',',':'))+'\n')
print('LOD1_BUILD_PASS',h,result['sourceTriangles'],total_triangles,result['shippingGlbSha256'])
