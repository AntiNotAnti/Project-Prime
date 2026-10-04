"""Retarget the SourceIO Samus A body and cannon arm to Project Prime's rig.

The SourceIO GLB is an intermediate Source-format conversion. This script
retargets its weighted geometry, projects its UVs onto the native Samus texture
surfaces, and saves a separate Blender working file plus a Weighted4 GLB.
"""

import bpy
import math
import runpy
from collections import defaultdict
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from pathlib import Path


ROOT = Path(__file__).resolve().parent
WORK_BLEND = ROOT / "Samus_HD_Work.blend"
SOURCE_GLTF = Path("/Users/jarrett/Downloads/mph/converted-sourceio/models/samus/samus_a.glb")
OUTPUT_BLEND = ROOT / "Samus_SourceIO_Retargeted.blend"
OUTPUT_GLB = ROOT / "starter/samus/biped_sourceio_weighted4.glb"
EXPORT_HELPER = ROOT / "prepare-biped-sourceio-weighted4.py"

BODYGROUP_OBJECTS = {"Samus.smd", "Samus_Arm1.smd"}
REFERENCE_OBJECTS = {"geom1_obj", "geom2_obj", "geom3_obj", "geom4_obj"}
EXPECTED_BONES = {
    "Pelvis", "L_hip", "L_knee", "L_ankle", "R_hip", "R_knee", "R_ankle",
    "Spine_1", "Spine_2", "Head_1", "L_shoulder", "L_elbow", "L_wrist",
    "L_varias2_SDK", "R_shoulder", "R_elbow", "R_varias2_SDK",
}
MATERIAL_ORDER = ["body", "body_full_bright", "Gun", "GunTip"]
SOURCE_MATERIAL_TO_NATIVE = {
    "Samus_Armor": "body",
    "Samus_Armor2": "body",
    "Samus_Armor3": "body",
    "Samus_Armor4": "body",
    "Samus_Light": "body_full_bright",
    "ArmCanon1": "Gun",
    "ArmCanon2": "Gun",
    "ArmCanon3": "GunTip",
}
WEIGHT_EPSILON = 1.0e-6
MAX_INFLUENCES = 4
TARGET_TRIANGLES = 39_000


def fail(message):
    raise RuntimeError("[Samus SourceIO retarget] " + message)


def bone_map():
    result = {
        "ValveBiped.Bip01_Pelvis": "Pelvis",
        "ValveBiped.Bip01_L_Thigh": "L_hip",
        "ValveBiped.Bip01_L_Calf": "L_knee",
        "ValveBiped.Bip01_L_Foot": "L_ankle",
        "ValveBiped.Bip01_R_Thigh": "R_hip",
        "ValveBiped.Bip01_R_Calf": "R_knee",
        "ValveBiped.Bip01_R_Foot": "R_ankle",
        "ValveBiped.Bip01_Spine": "Spine_1",
        "ValveBiped.Bip01_Spine1": "Spine_2",
        "ValveBiped.Bip01_Spine2": "Spine_2",
        "ValveBiped.Bip01_Neck1": "Head_1",
        "ValveBiped.Bip01_Head1": "Head_1",
        "Shoulder_L": "L_shoulder",
        "ValveBiped.Bip01_L_UpperArm": "L_shoulder",
        "ValveBiped.Bip01_L_Forearm": "L_elbow",
        "bip_hand_L": "L_wrist",
        "Shoulder_R": "R_shoulder",
        "ValveBiped.Bip01_R_UpperArm": "R_shoulder",
        "ValveBiped.Bip01_R_Forearm": "R_elbow",
        "bip_hand_R": "R_varias2_SDK",
    }
    for side, native in (("L", "L_wrist"), ("R", "R_varias2_SDK")):
        for finger in ("index", "middle", "pinky", "ring", "thumb"):
            for segment in range(3):
                result[f"bip_{finger}_{segment}_{side}"] = native
    return result


SOURCE_TO_TARGET_BONE = bone_map()


def barycentric(point, a, b, c):
    ab = b - a
    ac = c - a
    ap = point - a
    d00 = ab.dot(ab)
    d01 = ab.dot(ac)
    d11 = ac.dot(ac)
    d20 = ap.dot(ab)
    d21 = ap.dot(ac)
    denom = d00 * d11 - d01 * d01
    if abs(denom) < 1.0e-12:
        return (1.0, 0.0, 0.0)
    v = (d11 * d20 - d01 * d21) / denom
    w = (d00 * d21 - d01 * d20) / denom
    values = [max(0.0, 1.0 - v - w), max(0.0, v), max(0.0, w)]
    total = sum(values)
    return tuple(value / total for value in values)


def surface_info(source):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = source.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    try:
        mesh.calc_loop_triangles()
        vertices = [evaluated.matrix_world @ vertex.co for vertex in mesh.vertices]
        triangles = [list(tri.vertices) for tri in mesh.loop_triangles]
        if not triangles:
            fail(f"native UV source {source.name} has no triangles")
        uv_layer = mesh.uv_layers.active
        if uv_layer is None:
            fail(f"native UV source {source.name} has no UVMap")
        # BVH face indices follow the triangle list. Save each triangle's UVs
        # from its own loop indices (the polygon UV may be split at seams).
        triangle_uvs = []
        for tri in mesh.loop_triangles:
            triangle_uvs.append([Vector(uv_layer.data[index].uv) for index in tri.loops])
        bvh = BVHTree.FromPolygons(vertices, triangles, all_triangles=True)
        return {"vertices": vertices, "triangles": triangles,
                "triangle_uvs": triangle_uvs, "bvh": bvh}
    finally:
        evaluated.to_mesh_clear()


def nearest_hit(point, surface):
    hit = surface["bvh"].find_nearest(point)
    if hit is None or hit[2] is None:
        return None
    nearest, _normal, triangle_index, distance = hit
    return nearest, triangle_index, distance


def projected_uv(point, surface):
    result = nearest_hit(point, surface)
    if result is None:
        return Vector((0.5, 0.5))
    nearest, triangle_index, _distance = result
    triangle = surface["triangles"][triangle_index]
    points = [surface["vertices"][index] for index in triangle]
    weights = barycentric(nearest, points[0], points[1], points[2])
    uvs = surface["triangle_uvs"][triangle_index]
    return sum((uvs[i] * weights[i] for i in range(3)), Vector((0.0, 0.0)))


def source_group_influences(obj, vertex, source_bones):
    by_target = defaultdict(list)
    group_by_index = {group.index: group.name for group in obj.vertex_groups}
    for assignment in vertex.groups:
        if assignment.weight <= WEIGHT_EPSILON:
            continue
        source_name = group_by_index.get(assignment.group)
        target_name = SOURCE_TO_TARGET_BONE.get(source_name)
        if target_name is None:
            fail(f"{obj.name} vertex {vertex.index} has unmapped influence {source_name!r}")
        if source_name not in source_bones:
            fail(f"source armature is missing bone {source_name!r}")
        by_target[target_name].append((source_name, assignment.weight))
    totals = sorted(((sum(weight for _name, weight in entries), name, entries)
                     for name, entries in by_target.items()), reverse=True)
    totals = totals[:MAX_INFLUENCES]
    total_weight = sum(value[0] for value in totals)
    if total_weight <= WEIGHT_EPSILON:
        fail(f"{obj.name} vertex {vertex.index} has no usable source influences")
    normalized = []
    for raw_weight, target_name, entries in totals:
        target_weight = raw_weight / total_weight
        normalized.append((target_name, target_weight, entries, raw_weight))
    return normalized


def retarget_vertex(obj, vertex, source_armature, target_armature, source_bones,
                    target_pose_matrices, target_rest_matrices, source_retarget_frames,
                    target_heads):
    source_point = (source_armature.matrix_world.inverted() @
                    obj.matrix_world @ vertex.co)
    influences = source_group_influences(obj, vertex, source_bones)
    target_pose_point = Vector((0.0, 0.0, 0.0))
    for target_name, target_weight, entries, raw_weight in influences:
        target_head = target_heads[target_name]
        # Multiple Source joints (e.g. fingers) may collapse into one native
        # group. Reposition each contribution at the native joint and align
        # its physical bone direction, without carrying over SourceIO's
        # arbitrary bone roll into the native model coordinate system.
        for source_name, source_weight in entries:
            frame_name, rotation = source_retarget_frames[source_name]
            source_head = source_bones[frame_name].head_local
            source_local_offset = source_point - source_head
            target_pose_point += (target_head + rotation @ source_local_offset) * (
                source_weight / raw_weight * target_weight)
    # A weighted sum of distinct joint rotations can be singular (the classic
    # linear-blend-skinning candy-wrapper case). Convert back through each
    # retained joint separately and average those stable local coordinates.
    # Rigid vertices remain exact, while blended seams avoid explosive inverse
    # matrices at elbows, knees, and the torso.
    target_rest_point = Vector((0.0, 0.0, 0.0))
    for target_name, target_weight, _entries, _raw_weight in influences:
        target_rest_point += (
            target_rest_matrices[target_name] @
            target_pose_matrices[target_name].inverted() @ target_pose_point
        ) * target_weight
    return target_rest_point, target_pose_point, influences


def build_retarget_frames(source_bones, target_armature):
    """Align Source bone segment directions to the native pose's joint chain."""
    target_heads = {name: target_armature.pose.bones[name].head.copy()
                    for name in EXPECTED_BONES}
    target_next = {
        "Pelvis": "Spine_1",
        "L_hip": "L_knee", "L_knee": "L_ankle", "L_ankle": "L_knee",
        "R_hip": "R_knee", "R_knee": "R_ankle", "R_ankle": "R_knee",
        "Spine_1": "Spine_2", "Spine_2": "Head_1", "Head_1": "Head_1",
        "L_shoulder": "L_elbow", "L_elbow": "L_wrist", "L_wrist": "L_elbow",
        "R_shoulder": "R_elbow", "R_elbow": "R_shoulder",
        "L_varias2_SDK": "L_shoulder", "R_varias2_SDK": "R_elbow",
    }
    target_directions = {}
    for name, next_name in target_next.items():
        if name == next_name:
            direction = target_armature.pose.bones[name].matrix.to_3x3() @ Vector((0, 1, 0))
        else:
            direction = target_heads[next_name] - target_heads[name]
        if direction.length < 1.0e-5:
            direction = target_armature.pose.bones[name].matrix.to_3x3() @ Vector((0, 1, 0))
        target_directions[name] = direction.normalized()

    # Hand and finger vertices share the native wrist joint. Use the hand
    # segment's frame for all finger groups instead of turning each finger
    # toward the forearm axis independently.
    frame_alias = {}
    for source_name, target_name in SOURCE_TO_TARGET_BONE.items():
        if source_name.startswith("bip_") and source_name.rsplit("_", 1)[-1] in ("L", "R"):
            side = source_name.rsplit("_", 1)[-1]
            frame_alias[source_name] = f"bip_hand_{side}"
        else:
            frame_alias[source_name] = source_name

    frames = {}
    for source_name, target_name in SOURCE_TO_TARGET_BONE.items():
        frame_name = frame_alias[source_name]
        source_bone = source_bones.get(frame_name)
        if source_bone is None:
            continue
        source_direction = source_bone.tail_local - source_bone.head_local
        if source_direction.length < 1.0e-5:
            source_direction = source_bone.matrix_local.to_3x3() @ Vector((0, 1, 0))
        rotation = source_direction.normalized().rotation_difference(
            target_directions[target_name])
        frames[source_name] = (frame_name, rotation)
    missing = sorted(set(SOURCE_TO_TARGET_BONE) - set(frames))
    if missing:
        fail(f"source bone remap frames are incomplete: {missing}")
    return frames, target_heads


def clean_weight_groups(obj):
    groups = {group.index: group for group in obj.vertex_groups}
    removed = 0
    for vertex in obj.data.vertices:
        assignments = [(assignment.group, assignment.weight)
                       for assignment in vertex.groups
                       if assignment.weight > WEIGHT_EPSILON]
        assignments.sort(key=lambda item: item[1], reverse=True)
        kept = assignments[:MAX_INFLUENCES]
        total = sum(weight for _index, weight in kept)
        if total <= WEIGHT_EPSILON:
            fail(f"{obj.name} vertex {vertex.index} lost all weights during subdivision")
        kept_indices = {index for index, _weight in kept}
        for index, _weight in assignments:
            if index not in kept_indices:
                groups[index].remove([vertex.index])
                removed += 1
        for index, weight in kept:
            groups[index].add([vertex.index], weight / total, "REPLACE")
    return removed


def apply_geometry_density(obj):
    # A simple subdivision preserves the source silhouette while placing the
    # premade base inside the requested 25k-40k biped geometry budget.
    subdivision = obj.modifiers.new("SourceIO density pass", "SUBSURF")
    subdivision.subdivision_type = "SIMPLE"
    subdivision.levels = 1
    subdivision.render_levels = 1
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier=subdivision.name)
    obj.select_set(False)

    obj.data.calc_loop_triangles()
    triangles = len(obj.data.loop_triangles)
    if triangles > 40_000:
        decimate = obj.modifiers.new("SourceIO budget trim", "DECIMATE")
        decimate.decimate_type = "COLLAPSE"
        decimate.ratio = TARGET_TRIANGLES / triangles
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.modifier_apply(modifier=decimate.name)
        obj.select_set(False)
    obj.data.calc_loop_triangles()
    final_triangles = len(obj.data.loop_triangles)
    if not 25_000 <= final_triangles <= 40_000:
        fail(f"density pass produced {final_triangles:,} triangles, outside 25k-40k")
    return final_triangles


def build():
    if not SOURCE_GLTF.is_file():
        fail(f"SourceIO model not found: {SOURCE_GLTF}")
    bpy.ops.wm.open_mainfile(filepath=str(WORK_BLEND))
    scene = bpy.context.scene
    target_armature = bpy.data.objects.get("Armature")
    if target_armature is None or target_armature.type != "ARMATURE":
        fail("native Samus Armature was not found in the working file")

    native_surfaces = {}
    native_materials = {}
    for name in sorted(REFERENCE_OBJECTS):
        source = bpy.data.objects.get(name)
        if source is None:
            fail(f"native reference surface {name} was not found")
        material_names = {slot.material.name for slot in source.material_slots if slot.material}
        if len(material_names) != 1:
            fail(f"{name} should have exactly one native material, found {sorted(material_names)}")
        material_name = next(iter(material_names))
        native_materials[material_name] = source.material_slots[0].material
        native_surfaces[material_name] = surface_info(source)
    if set(native_materials) != set(MATERIAL_ORDER):
        fail(f"native material set mismatch: {sorted(native_materials)}")

    objects_before_import = set(scene.objects)
    bpy.ops.import_scene.gltf(filepath=str(SOURCE_GLTF))
    imported = [obj for obj in scene.objects if obj not in objects_before_import]
    source_armature = next((obj for obj in imported if obj.type == "ARMATURE"), None)
    if source_armature is None:
        fail("SourceIO GLB import did not create an armature")
    source_meshes = [obj for obj in imported
                     if obj.type == "MESH" and obj.name in BODYGROUP_OBJECTS]
    found = {obj.name for obj in source_meshes}
    if found != BODYGROUP_OBJECTS:
        fail(f"expected body plus Samus_Arm1 bodygroup; found {sorted(found)}")

    target_pose_matrices = {
        name: target_armature.pose.bones[name].matrix.copy()
        for name in EXPECTED_BONES
    }
    target_rest_matrices = {
        name: target_armature.data.bones[name].matrix_local.copy()
        for name in EXPECTED_BONES
    }
    source_bones = {bone.name: bone for bone in source_armature.data.bones}
    source_retarget_frames, target_heads = build_retarget_frames(source_bones, target_armature)

    points_rest = []
    points_pose_world = []
    source_points_world = []
    points_pose_local = []
    faces = []
    face_material_names = []
    source_vertices = 0
    source_triangles = 0
    max_source_influences = 0
    dropped_source_weight = 0.0
    group_names_used = set()

    for obj in source_meshes:
        vertex_offset = len(points_rest)
        source_vertices += len(obj.data.vertices)
        for vertex in obj.data.vertices:
            source_points_world.append(obj.matrix_world @ vertex.co)
            rest_point, pose_point, influences = retarget_vertex(
                obj, vertex, source_armature, target_armature, source_bones,
                target_pose_matrices, target_rest_matrices,
                source_retarget_frames, target_heads)
            points_rest.append(rest_point)
            points_pose_local.append(pose_point)
            points_pose_world.append(target_armature.matrix_world @ pose_point)
            max_source_influences = max(max_source_influences, len(vertex.groups))
            group_names_used.update(name for name, _weight, _entries, _raw in influences)

        obj.data.calc_loop_triangles()
        source_triangles += len(obj.data.loop_triangles)
        for tri in obj.data.loop_triangles:
            polygon = obj.data.polygons[tri.polygon_index]
            if polygon.material_index >= len(obj.data.materials):
                fail(f"{obj.name} has an invalid material slot on polygon {polygon.index}")
            source_material = obj.data.materials[polygon.material_index]
            source_material_name = source_material.name if source_material else ""
            native_material_name = SOURCE_MATERIAL_TO_NATIVE.get(source_material_name)
            if native_material_name is None:
                fail(f"no native material mapping for SourceIO material {source_material_name!r}")
            source_indices = list(tri.vertices)
            faces.append(tuple(vertex_offset + index for index in source_indices))
            face_material_names.append(native_material_name)

    if not points_rest or not faces:
        fail("no source geometry was collected")

    def bounds(values):
        return (tuple(round(min(point[axis] for point in values), 4) for axis in range(3)),
                tuple(round(max(point[axis] for point in values), 4) for axis in range(3)))
    print(f"[Samus SourceIO retarget] source bind bounds={bounds(source_points_world)}")
    print(f"[Samus SourceIO retarget] target pose bounds={bounds(points_pose_local)}")
    print(f"[Samus SourceIO retarget] target export bounds={bounds(points_rest)}")

    mesh = bpy.data.meshes.new("Samus_SourceIO_BipedMesh")
    mesh.from_pydata(points_rest, [], faces)
    mesh.update()
    for name in MATERIAL_ORDER:
        mesh.materials.append(native_materials[name])
    mesh.uv_layers.new(name="UVMap")

    # Pick a compatible native surface for each triangle, then project the
    # transformed triangle corners through that same surface to avoid mixing
    # native texture bindings across a face.
    poly_surface_names = []
    for poly_index, poly in enumerate(mesh.polygons):
        material_name = face_material_names[poly_index]
        surface = native_surfaces.get(material_name)
        if surface is None:
            fail(f"native projection source {material_name!r} is missing")
        center = sum((points_pose_world[mesh.loops[loop].vertex_index]
                      for loop in poly.loop_indices), Vector((0.0, 0.0, 0.0))) / len(poly.loop_indices)
        hit = nearest_hit(center, surface)
        if hit is None:
            fail(f"could not project face {poly_index} onto native {material_name}")
        poly.material_index = MATERIAL_ORDER.index(material_name)
        poly.use_smooth = True
        poly_surface_names.append(material_name)

    uv_layer = mesh.uv_layers.active
    for poly_index, poly in enumerate(mesh.polygons):
        surface = native_surfaces[poly_surface_names[poly_index]]
        for loop_index in poly.loop_indices:
            vertex_index = mesh.loops[loop_index].vertex_index
            uv = projected_uv(points_pose_world[vertex_index], surface)
            uv_layer.data[loop_index].uv = uv

    output = bpy.data.objects.new("Samus_SourceIO_Biped", mesh)
    scene.collection.objects.link(output)
    output.parent = target_armature
    output.matrix_parent_inverse = Matrix.Identity(4)
    output.location = (0.0, 0.0, 0.0)
    output.rotation_euler = (0.0, 0.0, 0.0)
    output.scale = (1.0, 1.0, 1.0)

    # Create only the native groups actually used by the remapped source.
    groups = {name: output.vertex_groups.new(name=name) for name in sorted(group_names_used)}
    # We must repeat the source-weight mapping to populate the native groups.
    point_cursor = 0
    for obj in source_meshes:
        for vertex in obj.data.vertices:
            influences = source_group_influences(obj, vertex, source_bones)
            out_index = point_cursor
            for target_name, weight, _entries, _raw in influences:
                groups[target_name].add([out_index], weight, "REPLACE")
            point_cursor += 1

    triangles = apply_geometry_density(output)
    cleaned = clean_weight_groups(output)
    modifier = output.modifiers.new("Native Samus Weighted4", "ARMATURE")
    modifier.object = target_armature
    modifier.use_vertex_groups = True

    # The reference geometry is useful only during UV projection. Remove it
    # from this separate retargeted file so the exporter sees one weighted mesh.
    for obj in list(scene.objects):
        if obj != target_armature and obj != output and obj.type == "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)
        elif obj != target_armature and obj != output and obj.type == "ARMATURE":
            bpy.data.objects.remove(obj, do_unlink=True)

    # Keep output material slots in their canonical order and save this model
    # as a new file; the original proof and work blend remain intact.
    output.data.materials.clear()
    for name in MATERIAL_ORDER:
        output.data.materials.append(native_materials[name])
    bpy.context.view_layer.objects.active = output
    bpy.ops.object.select_all(action="DESELECT")
    target_armature.select_set(True)
    output.select_set(True)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_BLEND))

    print(f"[Samus SourceIO retarget] meshes={len(source_meshes)} verts={source_vertices} "
          f"source_tris={source_triangles} output_tris={triangles} cleaned_weights={cleaned}")
    print(f"[Samus SourceIO retarget] native materials={MATERIAL_ORDER}; "
          f"used joints={sorted(group_names_used)}")
    print(f"[Samus SourceIO retarget] saved {OUTPUT_BLEND}")

    # The dedicated exporter writes a side-by-side candidate GLB. The original
    # starter proof is untouched until the candidate has passed validation.
    exporter = EXPORT_HELPER
    exporter.write_text(
        (ROOT / "prepare-biped-weighted4.py").read_text()
        .replace('ASSET_LABEL = "Samus/Biped"', 'ASSET_LABEL = "Samus SourceIO/Biped"')
        .replace('RELATIVE_OUTPUT = "starter/samus/biped_weighted4.glb"',
                 'RELATIVE_OUTPUT = "starter/samus/biped_sourceio_weighted4.glb"'),
        encoding="utf-8")
    runpy.run_path(str(exporter), run_name="__main__")
    if not OUTPUT_GLB.is_file():
        fail(f"candidate GLB was not written: {OUTPUT_GLB}")
    print(f"[Samus SourceIO retarget] candidate GLB: {OUTPUT_GLB}")


build()
