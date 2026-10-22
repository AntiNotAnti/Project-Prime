"""Import the generated native geometry and bake the legacy reference pose once.

No animation helper is run: runtime animation remains owned by Project Prime.
The generated Weighted4 exporter is used unchanged by convert.py.
"""
import json, math, sys, runpy, tempfile, xml.etree.ElementTree as ET
from pathlib import Path
import bpy
from mathutils import Matrix, Euler, Vector

def reference_bind(kit):
    reference = json.loads((kit / 'native-reference.json').read_text())
    contract = next(m for m in reference['models'] if m['part'] == 'biped' and m['lod'] == 0)
    world = {}; nodes = {n['index']: n for n in contract['nodes']}
    def transform(index):
        if index in world: return world[index]
        n = nodes[index]
        local = Euler(n['rotationRadians'], 'XYZ').to_matrix().to_4x4() @ Matrix.Diagonal(Vector((*n['scale'], 1)))
        local.translation = Vector([v/s for v,s in zip(n['position'], contract['modelScale'])])
        world[index] = transform(n['parentIndex']) @ local if n['parentIndex'] >= 0 else local
        return world[index]
    to_blender = Matrix.Rotation(math.pi/2, 4, 'X')
    return {n['name']: to_blender @ transform(n['index']) for n in nodes.values()}

def load_native(kit, hunter, common_module):
    reference = json.loads((kit / 'native-reference.json').read_text())
    contract = next(m for m in reference['models'] if m['part'] == 'biped' and m['lod'] == 0)
    model = contract['model']
    folder = kit / 'reference' / model
    sys.path.insert(0, str(common_module.parent))
    ns = runpy.run_path(str(folder / ('import_' + model + '.py')))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    dae = folder / (model + '_pal_01.dae')
    tree = ET.parse(dae); nsxml = '{http://www.collada.org/2005/11/COLLADASchema}'
    if tree.getroot().find(nsxml + 'scene') is None:
        scene = ET.SubElement(tree.getroot(), nsxml + 'scene')
        ET.SubElement(scene, nsxml + 'instance_visual_scene', url='#Scene')
    with tempfile.TemporaryDirectory() as temp:
        adapted = Path(temp) / 'reference.dae'; tree.write(adapted)
        try:
            bpy.ops.wm.collada_import.get_rna_type()
            importer = bpy.ops.wm.collada_import
        except (RuntimeError, AttributeError, KeyError):
            bpy.ops.preferences.addon_enable(module='bl_ext.user_default.collada_support')
            importer = bpy.ops.import_scene.collada
        importer(filepath=str(adapted))
    for obj in list(bpy.context.scene.objects):
        if obj.type == 'MESH' and obj.name.startswith('geometry'):
            obj.name = 'geom' + obj.name[len('geometry'):] + '_obj'
    for material in bpy.data.materials:
        if material.name.endswith('-material'):
            material.name = material.name[:-9] + '_mat'
    for material in contract['materials']:
        name = material['name']
        mat = bpy.data.materials.get(name + '_mat') or bpy.data.materials.new(name + '_mat')
        mat.use_nodes = True
        for mesh_id in material['meshIds']:
            obj = bpy.data.objects['geom' + str(mesh_id + 1) + '_obj']
            obj.data.materials.clear(); obj.data.materials.append(mat)
    # bone_setup only needs meshes/vertex groups; skip old texture/animation APIs.
    bpy.context.view_layer.objects.active = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
    ns['bone_setup']()
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    rig = bpy.data.objects['Armature']
    for obj in meshes:
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True); bpy.context.view_layer.objects.active = obj
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.context.view_layer.update()
    poses = {b.name: b.matrix.copy() for b in rig.pose.bones}
    # EditBone.matrix discards inherited scale. Preserve the actual native bind
    # separately so export and audit can target the runtime skeleton exactly.
    rig['projectPrimeNativeRestMatrices'] = json.dumps({n: [list(r) for r in m] for n, m in poses.items()})
    dg = bpy.context.evaluated_depsgraph_get()
    for obj in meshes:
        ev = obj.evaluated_get(dg); mesh = ev.to_mesh()
        coords = [v.co.copy() for v in mesh.vertices]; ev.to_mesh_clear()
        assert len(coords) == len(obj.data.vertices)
        for vertex, co in zip(obj.data.vertices, coords): vertex.co = co
    bpy.ops.object.select_all(action='DESELECT'); rig.select_set(True)
    bpy.context.view_layer.objects.active = rig; bpy.ops.object.mode_set(mode='EDIT')
    for bone in rig.data.edit_bones: bone.matrix = poses[bone.name]
    bpy.ops.object.mode_set(mode='OBJECT')
    for bone in rig.pose.bones: bone.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    for material in bpy.data.materials:
        if material.name.endswith('_mat'): material.name = material.name[:-4]
        if material.node_tree:
            for node in list(material.node_tree.nodes):
                if node.type == 'TEX_IMAGE': material.node_tree.nodes.remove(node)
    return rig, meshes
