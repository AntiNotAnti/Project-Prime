#!/usr/bin/env python3
"""PORT-23: build a *private* native-space Weighted4 Retro biped candidate.

This is not a new playable Hunter. The native Samus/Weavel rig still owns all
animation, hitboxes, viewmodel and netcode. No assets are installed by this tool.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
import re
import sys

from build_glb import load_inputs
from retarget_contract import (
    SOURCE_HUNTERS, MAX_MAP_BYTES, MAX_NATIVE_BYTES, _bounded_json,
    transform_basis, transformed, validate_contract,
)

MAX_FACES = 500_000
MAX_NATIVE_MATERIALS = 256
MAX_ABS_MATRIX = 100000.0
MATRIX_EPS = 0.015


def read_source_groups(obj_path, face_count):
    """Track Project Prime's emitted 'g material_N' face groups exactly."""
    groups = []
    active = None
    with obj_path.open(encoding="utf-8") as stream:
        for line in stream:
            words = line.split()
            if not words or words[0].startswith("#"):
                continue
            if words[0] == "g":
                if len(words) != 2 or not re.fullmatch(r"material_(0|[1-9][0-9]{0,5})", words[1]):
                    raise ValueError("OBJ material group is not a bounded exporter material_N")
                active = words[1]
            elif words[0] == "f":
                if active is None:
                    raise ValueError("Source triangle has no declared source material group")
                if len(groups) >= MAX_FACES:
                    raise ValueError("Oversized OBJ material group table")
                groups.append(active)
    if len(groups) != face_count:
        raise ValueError("Source face/material correspondence was lost")
    return groups


def native_biped(native, character):
    if not isinstance(native, dict) or native.get("format") != 1:
        raise ValueError("Native LOD0 reference must have format 1")
    if native.get("hunter") != SOURCE_HUNTERS[character]:
        raise ValueError("Wrong native Hunter reference")
    models = native.get("models")
    if not isinstance(models, list):
        raise ValueError("Native model reference lacks entries")
    matching = [m for m in models if isinstance(m, dict) and m.get("part") == "biped"
                and m.get("lod") == 0]
    if len(matching) != 1:
        raise ValueError("Expected one native LOD0 biped reference")
    return matching[0]


def rigid_world_matrix(value):
    """Validate the exact OpenTK-to-column-vector 4x4 reference matrix."""
    if (not isinstance(value, list) or len(value) != 4 or
            any(not isinstance(row, list) or len(row) != 4 for row in value)):
        raise ValueError("Native bindWorldMatrix needs 4x4 matrix rows")
    m = []
    for row in value:
        nums = []
        for number in row:
            if isinstance(number, bool) or not isinstance(number, (int, float)):
                raise ValueError("Invalid native bind matrix component")
            component = float(number)
            if not math.isfinite(component) or abs(component) > MAX_ABS_MATRIX:
                raise ValueError("Nonfinite or oversized native bind matrix component")
            nums.append(component)
        m.append(tuple(nums))
    if any(abs(m[3][j] - (1.0 if j == 3 else 0.0)) > 1e-5 for j in range(4)):
        raise ValueError("Native bind matrix is not affine")
    for i in range(3):
        for j in range(3):
            dot = sum(m[k][i] * m[k][j] for k in range(3))
            if abs(dot - (1 if i == j else 0)) > MATRIX_EPS:
                raise ValueError("Native rest palette contains scale or shear requiring explicit acceptance")
    r = m
    det = (
        r[0][0]*(r[1][1]*r[2][2]-r[1][2]*r[2][1]) -
        r[0][1]*(r[1][0]*r[2][2]-r[1][2]*r[2][0]) +
        r[0][2]*(r[1][0]*r[2][1]-r[1][1]*r[2][0])
    )
    if abs(det - 1.0) > MATRIX_EPS:
        raise ValueError("Native rest palette is mirrored or nonrigid")
    return tuple(m)


def validate_native_candidate(root, character, native, mapping):
    vertices, uvs, faces, face_uvs, skeleton, weights, motions = load_inputs(root, character)
    diagnostic = validate_contract(skeleton, weights, native, mapping)
    selected = native_biped(native, character)
    nodes = selected.get("nodes")
    materials = selected.get("materials")
    if not isinstance(nodes, list) or not isinstance(materials, list):
        raise ValueError("Native kit lacks node/material references")
    if not 1 <= len(materials) <= MAX_NATIVE_MATERIALS:
        raise ValueError("Native biped has no usable bounded materials")
    names = [m.get("name") for m in materials if isinstance(m, dict)]
    if len(names) != len(materials) or any(not isinstance(n, str) or not n for n in names):
        raise ValueError("Native material list contains an unnamed or invalid entry")
    if len(set(names)) != len(names):
        raise ValueError("Native material names are not unique")
    declared = mapping.get("materialMap")
    if not isinstance(declared, dict) or not declared:
        raise ValueError("Explicit source-group to native-material mapping is required")
    used_groups = read_source_groups(root / (character + ".obj"), len(faces))
    if set(declared.keys()) != set(used_groups):
        raise ValueError("Every source material group must map once, without unused entries")
    if any(not isinstance(v, str) or v not in names for v in declared.values()):
        raise ValueError("Mapped material not present in the native biped")
    used_nodes = set(diagnostic["palette"])
    by_name = {n["name"]: n for n in nodes if isinstance(n, dict) and isinstance(n.get("name"), str)}
    if len(by_name) != len(nodes) or not used_nodes.issubset(by_name):
        raise ValueError("Native node list has duplicates or missing mapped joints")
    matrices = {}
    for name in used_nodes:
        record = by_name[name]
        matrix = rigid_world_matrix(record.get("bindWorldMatrix"))
        position = record["bindWorldPosition"]
        if any(abs(matrix[i][3] - position[i]) > 0.0001 for i in range(3)):
            raise ValueError("Native bind matrix and exported world position disagree")
        matrices[name] = matrix
    basis = mapping["sourceToNative"]["axis"]
    scale = mapping["sourceToNative"]["scale"]
    offset = mapping["sourceToNative"]["offset"]
    transform_basis(basis)
    positions = [transformed(v, basis, scale, offset) for v in vertices]
    reflect = diagnostic["coordinateHandednessFlip"]
    faces = [tuple((a, c, b) if reflect else (a, b, c)) for a, b, c in faces]
    face_uvs = [tuple((a, c, b) if reflect else (a, b, c)) for a, b, c in face_uvs]
    return {
        "vertices": positions, "uvs": uvs, "faces": faces, "face_uvs": face_uvs,
        "source_groups": used_groups, "materials": declared, "mat_names": names,
        "native_nodes": by_name, "world_matrices": matrices, "skin": diagnostic,
    }


def build_blender_native(preview, character, output):
    try:
        import bpy
        from mathutils import Matrix, Vector
    except ImportError as exc:
        raise RuntimeError("Use Blender 4.x -b --python for private rig candidate export") from exc

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    mesh = bpy.data.meshes.new(character + "_native_candidate_mesh")
    mesh.from_pydata(preview["vertices"], [], preview["faces"])
    mesh.validate(clean_customdata=False)
    mesh.update()
    if len(mesh.polygons) != len(preview["faces"]):
        raise ValueError("Blender sanitized source faces; native materials cannot be correlated")
    obj = bpy.data.objects.new(character + "_native_candidate", mesh)
    bpy.context.collection.objects.link(obj)

    # Retain source-face UV/variant identity while requiring authored native
    # material bindings; there are no synthetic shipped source textures.
    slot_lookup = {}
    for source_group in sorted(set(preview["source_groups"])):
        name = preview["materials"][source_group]
        if name not in slot_lookup:
            slot_lookup[name] = len(mesh.materials)
            mesh.materials.append(bpy.data.materials.new(name))
    if preview["uvs"]:
        layer = mesh.uv_layers.new(name="UVMap")
    for i, polygon in enumerate(mesh.polygons):
        name = preview["materials"][preview["source_groups"][i]]
        polygon.material_index = slot_lookup[name]
        if preview["uvs"]:
            for corner, loop_index in enumerate(polygon.loop_indices):
                uv_index = preview["face_uvs"][i][corner]
                if uv_index >= 0:
                    u, v = preview["uvs"][uv_index]
                    layer.data[loop_index].uv = (u, 1.0 - v)

    arm_data = bpy.data.armatures.new(character + "_native_palette")
    armature = bpy.data.objects.new(character + "_armature", arm_data)
    bpy.context.collection.objects.link(armature)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.select_all(action="DESELECT")
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bones = {}
    # Native joint names must not be remapped to fake glTF names.
    for name in preview["skin"]["palette"]:
        bone = arm_data.edit_bones.new(name)
        matrix = Matrix(preview["world_matrices"][name])
        bone.head = matrix.translation
        bone.tail = matrix.translation + matrix.to_3x3() @ Vector((0, 0.1, 0))
        bones[name] = bone
    for name, bone in bones.items():
        native_parent = preview["native_nodes"][name].get("parentName")
        if native_parent in bones and native_parent != name:
            bone.parent = bones[native_parent]
    # Assign exact composed native reference transforms *after* parenting.
    for name, bone in bones.items():
        bone.matrix = Matrix(preview["world_matrices"][name])
    bpy.ops.object.mode_set(mode="OBJECT")
    armature.select_set(False)

    obj.parent = armature
    modifier = obj.modifiers.new(name="NativeWeighted4", type="ARMATURE")
    modifier.object = armature
    by_group = {}
    for index, group_id in enumerate(preview["skin"]["sourceVertexGroupIds"]):
        by_group.setdefault(str(group_id), []).append(index)
    for group_id, assigned in preview["skin"]["sourceGroupToNativeWeights"].items():
        vertices = by_group.get(group_id, ())
        if not vertices:
            continue
        for item in assigned:
            vertex_group = obj.vertex_groups.get(item["nativeNode"])
            if vertex_group is None:
                vertex_group = obj.vertex_groups.new(name=item["nativeNode"])
            vertex_group.add(vertices, float(item["weight"]), "REPLACE")

    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    options = dict(
        filepath=str(output), export_format="GLB", use_selection=True,
        export_skins=True, export_animations=False, export_yup=True,
        export_influence_nb=4, export_all_influences=False,
    )
    available = bpy.ops.export_scene.gltf.get_rna_type().properties.keys()
    if "export_def_bones" in available:
        options["export_def_bones"] = True
    bpy.ops.export_scene.gltf(**options)
    if not output.is_file() or not 0 < output.stat().st_size <= 128*1024*1024:
        raise RuntimeError("Blender native-space candidate absent or exceeds runtime GLB budget")
    print("Wrote local-only native-space candidate:", output)


def main():
    command = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--character", required=True, choices=sorted(SOURCE_HUNTERS))
    parser.add_argument("--native-reference", type=Path, required=True)
    parser.add_argument("--mapping", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--validate", action="store_true")
    args = parser.parse_args(command)
    if args.source_root.is_symlink():
        raise ValueError("Local source-root symlink is not permitted")
    native = _bounded_json(args.native_reference, MAX_NATIVE_BYTES)
    mapping = _bounded_json(args.mapping, MAX_MAP_BYTES)
    if mapping.get("sourceCharacter") != args.character:
        raise ValueError("Candidate source identity differs from explicit mapping")
    data = validate_native_candidate(args.source_root, args.character, native, mapping)
    print("Validated native-space candidate", args.character, "vertices", len(data["vertices"]),
          "faces", len(data["faces"]), "joints", len(data["skin"]["palette"]))
    if not args.validate:
        output = args.output or args.source_root / (args.character + ".native-candidate.glb")
        if output.suffix.lower() != ".glb" or output.is_symlink():
            raise ValueError("Private native candidate must be a regular .glb")
        build_blender_native(data, args.character, output)

if __name__ == "__main__":
    main()
