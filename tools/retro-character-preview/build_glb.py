#!/usr/bin/env python3
"""Assemble user-owned Retro OBJ/CINF/CSKR into a *preview* skinned GLB.

Run with Blender 4.x:
  blender -b --python tools/retro-character-preview/build_glb.py -- \
      --root PATH_TO_LOCAL_PREVIEWS --character darksamus-echoes

For an asset-free structural check (no Blender needed):
  python3 tools/retro-character-preview/build_glb.py --validate --root ... --character ...

This is an artist authoring tool. Source armature placement/axis convention
requires visual acceptance before any model could enter Project Prime.
"""
from __future__ import annotations
import argparse
import json
import math
from pathlib import Path
import re
import sys

CHARACTERS = {
    "darksamus-echoes",
    "trooper-power",
    "trooper-ice",
    "trooper-wave",
    "trooper-plasma",
}
MAX_VERTICES = 2_000_000
MAX_FACES = 500_000
MAX_BONES = 1024
MAX_GROUPS = 65536


def finite(values, size):
    return (
        isinstance(values, list) and len(values) == size
        and all(isinstance(x, (int, float)) and math.isfinite(x) and abs(x) <= 100000 for x in values)
    )


def load_inputs(root: Path, character: str):
    if character not in CHARACTERS:
        raise ValueError("Unknown local Retro character identifier")
    paths = [root / (character + suffix) for suffix in (
        ".obj", ".skeleton.json", ".weights.json", ".motions.json"
    )]
    if root.is_symlink() or any(p.is_symlink() for p in paths):
        raise ValueError("Local input paths must not be symlinks")
    if any(not p.is_file() for p in paths):
        raise ValueError("Local preview lacks OBJ, skeleton, weights or motion sidecar")
    # Refuse accidentally enormous or forged sidecars before JSON decoding.
    if paths[0].stat().st_size > 256 * 1024 * 1024 or any(
            p.stat().st_size > 32 * 1024 * 1024 for p in paths[1:]):
        raise ValueError("Local source preview exceeds the authoring file budget")
    obj, skeleton_path, weights_path, motions_path = paths
    with skeleton_path.open(encoding="utf-8") as f:
        skeleton = json.load(f)
    with weights_path.open(encoding="utf-8") as f:
        weights = json.load(f)
    with motions_path.open(encoding="utf-8") as f:
        motions = json.load(f)
    vertices, uvs, faces, face_uvs = read_obj(obj)
    check_sidecars(character, skeleton, weights, motions, len(vertices))
    return vertices, uvs, faces, face_uvs, skeleton, weights, motions


def read_obj(path: Path):
    """Parse only Project Prime's generated OBJ subset, never arbitrary commands."""
    vertices, uvs, faces, face_uvs = [], [], [], []
    with path.open(encoding="utf-8") as handle:
        for line in handle:
            parts = line.strip().split()
            if not parts or parts[0].startswith("#"):
                continue
            if parts[0] == "v":
                v = [float(x) for x in parts[1:]]
                if not finite(v, 3) or len(vertices) >= MAX_VERTICES:
                    raise ValueError("Invalid or oversized source vertex")
                vertices.append(v)
            elif parts[0] == "vt":
                uv = [float(x) for x in parts[1:]]
                if not finite(uv, 2):
                    raise ValueError("Invalid source UV")
                uvs.append(uv)
            elif parts[0] == "f":
                if len(parts) != 4 or len(faces) >= MAX_FACES:
                    raise ValueError("Only bounded triangle source faces are supported")
                corners, texcoords = [], []
                for field in parts[1:]:
                    pieces = field.split("/")
                    if not 1 <= len(pieces) <= 3:
                        raise ValueError("Invalid source OBJ corner")
                    i = int(pieces[0]) - 1
                    t = int(pieces[1]) - 1 if len(pieces) > 1 and pieces[1] else -1
                    if i < 0 or i >= len(vertices) or t >= len(uvs):
                        raise ValueError("Source OBJ index points beyond its vertices or UVs")
                    if t < -1:
                        raise ValueError("Source OBJ UV index is invalid")
                    corners.append(i)
                    texcoords.append(t)
                if len(set(corners)) != 3:
                    raise ValueError("Degenerate source OBJ triangle")
                faces.append(corners)
                face_uvs.append(texcoords)
            elif parts[0] in {"vn", "o", "g", "s"}:
                # Imported normals/material grouping are untrusted preview
                # decoration; geometry and source CSKR determine the rig.
                continue
            else:
                raise ValueError("Unsupported command in source-only OBJ")
    if len(vertices) < 3 or not faces:
        raise ValueError("Source preview has no usable triangles")
    return vertices, uvs, faces, face_uvs


def check_sidecars(character, skeleton, weights, motions, vertex_count):
    if skeleton.get("Id") != character or not isinstance(skeleton.get("Bones"), list):
        raise ValueError("Skeleton does not match its source character")
    bones = skeleton["Bones"]
    if not 1 <= len(bones) <= MAX_BONES:
        raise ValueError("Invalid CINF source bone count")
    known = {}
    for b in bones:
        ident = b.get("Id")
        if not isinstance(ident, int) or ident < 0 or ident in known or not finite(b.get("Position"), 3):
            raise ValueError("Invalid or duplicate source CINF bone")
        known[ident] = b
    for bone_id, b in known.items():
        parent = b.get("Parent")
        if not isinstance(parent, int):
            raise ValueError("Invalid source CINF parent ID")
        if parent != 0xFFFFFFFF and parent not in known:
            raise ValueError("Missing CINF source bone parent")
        if parent == bone_id:
            raise ValueError("CINF bone cannot parent itself")

    # Longer parent cycles must also be refused. Blender armatures cannot
    # represent them and should never be allowed to guess a valid hierarchy.
    visited = set()
    active = set()

    def visit(bone_id):
        if bone_id in active:
            raise ValueError("CINF skeleton contains a parent cycle")
        if bone_id in visited:
            return
        active.add(bone_id)
        parent = known[bone_id]["Parent"]
        if parent in known:
            visit(parent)
        active.remove(bone_id)
        visited.add(bone_id)

    for bone_id in known:
        visit(bone_id)

    if weights.get("format") != 1 or weights.get("vertexCount") != vertex_count:
        raise ValueError("CSKR source vertex count and OBJ vertices differ")
    groups = weights.get("groups")
    references = weights.get("vertexGroupIds")
    if (not isinstance(groups, list) or not 1 <= len(groups) <= MAX_GROUPS
            or not isinstance(references, list) or len(references) != vertex_count):
        raise ValueError("Missing or oversized source CSKR groups")
    for group in groups:
        if not isinstance(group, list) or not 1 <= len(group) <= 64:
            raise ValueError("Invalid source CSKR group")
        # Project Prime's runtime weighted-character contract supports up
        # to four vertex influences. Never silently truncate more when
        # authoring a preview GLB for possible future retargeting.
        if sum(item.get("weight", 0) > 0 for item in group) > 4:
            raise ValueError("This source skin has more than four bone influences per vertex")
        if sum(item.get("weight", 0) for item in group) <= 0:
            raise ValueError("Source CSKR group has zero useful bone weight")
        for influence in group:
            bone_id = influence.get("boneId")
            weight = influence.get("weight")
            if (not isinstance(bone_id, int) or bone_id not in known
                    or not isinstance(weight, (int, float))
                    or not math.isfinite(weight) or weight < 0 or weight > 1):
                raise ValueError("Unknown or nonfinite CSKR bone influence")
    if any(not isinstance(v, int) or not 0 <= v < len(groups) for v in references):
        raise ValueError("CSKR group reference exceeds source weights")
    if motions.get("Format") != 1 or not isinstance(motions.get("Clips"), list):
        raise ValueError("Source motion sidecar is not a supported preview inventory")
    # No untrusted motion is executed or implicitly applied to the rig.
    return len(bones), len(groups), len(motions["Clips"])


def build_blender(vertices, uvs, faces, face_uvs, skeleton, weights, character, output, motion_clips=None):
    try:
        import bpy
    except ImportError as exc:
        raise RuntimeError("Use Blender with -b --python to assemble the skinned GLB") from exc

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    mesh = bpy.data.meshes.new(character + "_mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.validate(clean_customdata=False)
    mesh.update()
    mesh_object = bpy.data.objects.new(character + "_local_mesh", mesh)
    bpy.context.collection.objects.link(mesh_object)

    if uvs:
        layer = mesh.uv_layers.new(name="UVMap")
        for polygon in mesh.polygons:
            for corner, loop_idx in enumerate(polygon.loop_indices):
                uv_idx = face_uvs[polygon.index][corner]
                if uv_idx >= 0:
                    u, v = uvs[uv_idx]
                    layer.data[loop_idx].uv = (u, 1 - v)

    rig = bpy.data.armatures.new(character + "_source_rig")
    armature = bpy.data.objects.new(character + "_armature", rig)
    bpy.context.collection.objects.link(armature)
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    mapping = {}
    for bone in skeleton["Bones"]:
        ident = bone["Id"]
        display = re.sub(r"[^a-zA-Z0-9_]", "_", str(bone.get("Name") or "bone"))[:36]
        name = "bone_" + str(ident) + "_" + display
        b = rig.edit_bones.new(name)
        b.head = bone["Position"]
        b.tail = (b.head.x, b.head.y, b.head.z + 0.1)
        mapping[ident] = name
    for bone in skeleton["Bones"]:
        parent = bone["Parent"]
        if parent in mapping and parent != bone["Id"]:
            rig.edit_bones[mapping[bone["Id"]]].parent = rig.edit_bones[mapping[parent]]
    bpy.ops.object.mode_set(mode="OBJECT")
    armature.select_set(False)

    mesh_object.parent = armature
    modifier = mesh_object.modifiers.new(name="RetroSourceSkin", type="ARMATURE")
    modifier.object = armature
    groups = weights["groups"]
    by_group = [[] for _ in groups]
    for index, group_id in enumerate(weights["vertexGroupIds"]):
        by_group[group_id].append(index)
    for group_id, source_weights in enumerate(groups):
        positions = by_group[group_id]
        if not positions:
            continue
        for influence in source_weights:
            if influence["weight"] == 0:
                continue
            name = mapping[influence["boneId"]]
            vertex_group = mesh_object.vertex_groups.get(name)
            if vertex_group is None:
                vertex_group = mesh_object.vertex_groups.new(name=name)
            vertex_group.add(positions, float(influence["weight"]), "REPLACE")

    if motion_clips:
        from motion_bake import apply_blender_motion
        apply_blender_motion(armature, mapping, motion_clips)

    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    mesh_object.select_set(True)
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    export_options = dict(
        filepath=str(output), export_format="GLB", use_selection=True,
        export_skins=True, export_animations=bool(motion_clips),
        export_influence_nb=4, export_all_influences=False,
    )
    if motion_clips:
        properties = bpy.ops.export_scene.gltf.get_rna_type().properties.keys()
        if "export_animation_mode" in properties:
            export_options["export_animation_mode"] = "NLA_TRACKS"
        elif "export_nla_strips" in properties:
            export_options["export_nla_strips"] = True
        else:
            raise RuntimeError("Blender glTF exporter does not support separate NLA animations")
    bpy.ops.export_scene.gltf(**export_options)
    if not output.is_file() or output.stat().st_size <= 0:
        raise RuntimeError("Blender did not produce a skinned GLB preview")
    print("Local-only rig preview:", output)
    print("Retarget source animation and inspect original bone placement before gameplay use")


def main():
    userargs = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--character", choices=sorted(CHARACTERS), required=True)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--validate", action="store_true")
    parser.add_argument("--animate-source", action="store_true",
                        help="Bake five-pose source-local diagnostic animation clips; never a gameplay rig")
    parser.add_argument("--clip", help="Limit source-motion preview to an exact sampled primitive name")
    args = parser.parse_args(userargs)
    if args.clip and not args.animate_source:
        parser.error("--clip requires --animate-source")
    vertices, uvs, faces, face_uvs, skeleton, weights, motions = load_inputs(
        args.root, args.character
    )
    clips = None
    if args.animate_source:
        from motion_bake import validate_motion_inventory
        clips = validate_motion_inventory(skeleton, motions, args.clip)
        print("Validated", len(clips), "safe sparse source-motion diagnostic clips")
    print(
        "Validated local source: ", args.character,
        "vertices", len(vertices), "triangles", len(faces),
        "bones", len(skeleton["Bones"]), "motions indexed", len(motions["Clips"])
    )
    if not args.validate:
        suffix = ".source-motion.preview.glb" if args.animate_source else ".preview.glb"
        target = args.output or (args.root / (args.character + suffix))
        if target.is_symlink():
            raise ValueError("The preview output cannot be a symbolic link")
        build_blender(vertices, uvs, faces, face_uvs, skeleton, weights,
                      args.character, target, motion_clips=clips)


if __name__ == "__main__":
    main()
