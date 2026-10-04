# Project Prime rigid character exporter
#
# Workflow:
#   1. Run the generated import_<native model>.py from this kit's reference folder.
#   2. Replace/refine the mesh while keeping its native Armature and rigid vertex groups.
#   3. Apply topology-changing modifiers before running this file.
#   4. Run this file. It creates a temporary ProjectPrimeRigid collection and exports GLB.
#
# A triangle whose vertices belong to different native bone groups is rejected.
# That boundary needs to be remodeled for rigidNodes or left for the future Weighted4 path.

import bpy
from mathutils import Vector
from pathlib import Path

ASSET_LABEL = "Samus/Biped"
EXPECTED_BONES = set(["Pelvis","L_hip","L_knee","L_ankle","R_hip","R_knee","R_ankle","Spine_1","Spine_2","Head_1","L_shoulder","L_elbow","L_wrist","L_varias2_SDK","R_shoulder","R_elbow","R_varias2_SDK"])
EXPECTED_MATERIALS = set(["body","body_full_bright","Gun","GunTip"])
RELATIVE_OUTPUT = "starter/samus/biped_lod1.glb"
RIGID_COLLECTION = "ProjectPrimeRigid"
WEIGHT_EPSILON = 1.0e-4
RIGID_WEIGHT = 0.999

def fail(message):
    raise RuntimeError("[Project Prime rigid export] " + message)

def script_root():
    value = globals().get("__file__")
    if value:
        return Path(value).resolve().parent
    if bpy.data.filepath:
        return Path(bpy.data.filepath).resolve().parent
    return Path.cwd()

def armature_for(obj):
    for modifier in obj.modifiers:
        if modifier.type == 'ARMATURE' and modifier.object is not None:
            return modifier.object
    if obj.parent is not None and obj.parent.type == 'ARMATURE':
        return obj.parent
    return None

def rigid_bone(obj, vertex):
    names = []
    for assignment in vertex.groups:
        if assignment.weight <= WEIGHT_EPSILON:
            continue
        group = obj.vertex_groups[assignment.group]
        if group.name in EXPECTED_BONES:
            names.append((group.name, assignment.weight))
    names.sort(key=lambda item: item[1], reverse=True)
    if not names:
        return None
    if names[0][1] < RIGID_WEIGHT:
        fail(f"{obj.name}: vertex {vertex.index} is not rigid "
             f"({names[0][0]} weight={names[0][1]:.4f})")
    if len(names) > 1 and names[1][1] > WEIGHT_EPSILON:
        fail(f"{obj.name}: vertex {vertex.index} has multiple native bone weights: {names[:4]}")
    return names[0][0]

def clear_old_output():
    collection = bpy.data.collections.get(RIGID_COLLECTION)
    if collection is None:
        collection = bpy.data.collections.new(RIGID_COLLECTION)
        bpy.context.scene.collection.children.link(collection)
        return collection
    for obj in list(collection.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    return collection

def build():
    collection = clear_old_output()
    sources = []
    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH' or obj.name in collection.objects:
            continue
        group_names = {group.name for group in obj.vertex_groups}
        if group_names.intersection(EXPECTED_BONES):
            sources.append(obj)
    if not sources:
        fail("No mesh object has a vertex group matching the expected native nodes. "
             "Run the generated native import script first and keep its rigid groups.")

    depsgraph = bpy.context.evaluated_depsgraph_get()
    segments = {}
    cross_bone = []
    missing_bone = []
    used_materials = set()

    for source in sources:
        armature = armature_for(source)
        if armature is None:
            fail(f"{source.name}: no Armature modifier/parent was found.")
        evaluated = source.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
        try:
            if len(mesh.vertices) != len(source.data.vertices):
                fail(f"{source.name}: evaluated topology changed from "
                     f"{len(source.data.vertices)} to {len(mesh.vertices)} vertices. "
                     "Apply subdivision/remesh/topology modifiers before rigid export.")

            mesh.calc_loop_triangles()
            uv_layer = mesh.uv_layers.active
            armature_inverse = armature.matrix_world.inverted()
            group_by_vertex = {}
            for vertex in source.data.vertices:
                group_by_vertex[vertex.index] = rigid_bone(source, vertex)

            for triangle in mesh.loop_triangles:
                vertex_indices = [mesh.loops[loop].vertex_index for loop in triangle.loops]
                bone_names = [group_by_vertex.get(index) for index in vertex_indices]
                if any(name is None for name in bone_names):
                    missing_bone.append((source.name, triangle.index, vertex_indices))
                    continue
                if len(set(bone_names)) != 1:
                    cross_bone.append((source.name, triangle.index, bone_names))
                    continue

                bone_name = bone_names[0]
                pose_bone = armature.pose.bones.get(bone_name)
                if pose_bone is None:
                    fail(f"{source.name}: Armature has no pose bone named {bone_name}.")
                bone_inverse = pose_bone.matrix.inverted()

                polygon = mesh.polygons[triangle.polygon_index]
                material_name = None
                if polygon.material_index < len(source.material_slots):
                    material = source.material_slots[polygon.material_index].material
                    if material is not None:
                        material_name = material.name
                if not material_name:
                    fail(f"{source.name}: triangle {triangle.index} has no named material.")
                if material_name not in EXPECTED_MATERIALS:
                    fail(f"{source.name}: material '{material_name}' is not a native material "
                         f"for {ASSET_LABEL}. Rename it to one of: {sorted(EXPECTED_MATERIALS)}")
                used_materials.add(material_name)

                segment = segments.setdefault(bone_name, {
                    "vertices": [], "faces": [], "uvs": [], "lookup": {},
                    "face_materials": [], "materials": []
                })
                if material_name not in segment["materials"]:
                    segment["materials"].append(material_name)
                material_slot = segment["materials"].index(material_name)

                face = []
                for loop_index in triangle.loops:
                    loop = mesh.loops[loop_index]
                    vertex_index = loop.vertex_index
                    uv = (0.0, 0.0)
                    if uv_layer is not None:
                        value = uv_layer.data[loop_index].uv
                        uv = (float(value.x), float(value.y))

                    key = (source.name, vertex_index, round(uv[0], 7), round(uv[1], 7))
                    out_index = segment["lookup"].get(key)
                    if out_index is None:
                        world = evaluated.matrix_world @ mesh.vertices[vertex_index].co
                        armature_space = armature_inverse @ world
                        local = bone_inverse @ armature_space
                        out_index = len(segment["vertices"])
                        segment["lookup"][key] = out_index
                        segment["vertices"].append((float(local.x), float(local.y), float(local.z)))
                        segment["uvs"].append(uv)
                    face.append(out_index)

                segment["faces"].append(tuple(face))
                segment["face_materials"].append(material_slot)
        finally:
            evaluated.to_mesh_clear()

    if missing_bone:
        example = missing_bone[:8]
        fail(f"{len(missing_bone)} triangle(s) contain vertices with no expected native bone. "
             f"Examples: {example}")
    if cross_bone:
        example = cross_bone[:8]
        fail(f"{len(cross_bone)} triangle(s) cross rigid bone boundaries. "
             "Split/remodel those joint triangles or use the later Weighted4 path. "
             f"Examples: {example}")
    if not segments:
        fail("No rigid triangles were produced.")

    output_objects = []
    for bone_name in sorted(segments):
        data = segments[bone_name]
        mesh = bpy.data.meshes.new("PP_" + bone_name)
        mesh.from_pydata(data["vertices"], [], data["faces"])
        mesh.update()

        uv = mesh.uv_layers.new(name="UVMap")
        for polygon in mesh.polygons:
            polygon.use_smooth = True
            polygon.material_index = data["face_materials"][polygon.index]
            source_face = data["faces"][polygon.index]
            for corner, loop_index in enumerate(polygon.loop_indices):
                uv.data[loop_index].uv = data["uvs"][source_face[corner]]

        obj = bpy.data.objects.new(bone_name, mesh)
        collection.objects.link(obj)
        for material_name in data["materials"]:
            material = bpy.data.materials.get(material_name)
            if material is None:
                material = bpy.data.materials.new(material_name)
            obj.data.materials.append(material)
        obj.select_set(False)
        output_objects.append(obj)

    bpy.ops.object.select_all(action='DESELECT')
    for obj in output_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = output_objects[0]

    output_path = (script_root() / RELATIVE_OUTPUT).resolve()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(output_path),
        export_format='GLB',
        use_selection=True,
        export_animations=False,
        export_materials='EXPORT',
        export_yup=True
    )
    print(f"[Project Prime rigid export] {ASSET_LABEL}: "
          f"{len(output_objects)} segments, materials={sorted(used_materials)}")
    print(f"[Project Prime rigid export] wrote {output_path}")
    return str(output_path)

if __name__ == "__main__":
    build()