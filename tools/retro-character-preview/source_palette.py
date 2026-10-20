#!/usr/bin/env python3
"""Private own-source-rig draw-palette preflight; never a runtime pack.

Keep the complete source hierarchy/motion inventory. Only triangle draw batches
receive <=32-joint local palettes. No bone retargeting or positive-weight pruning.
"""
from __future__ import annotations

import copy
import hashlib
import json
import math
from pathlib import Path

from build_glb import CHARACTERS, check_sidecars, load_inputs, source_stem

MAX_PALETTE = 32  # Existing CharacterWeightedModelLoader.MaximumJoints.
MAX_VERTICES = 200_000
MAX_FACES = 200_000
MAX_BATCHES = 2048
MAX_DRAW_VERTICES = 600_000
MAX_PLAN_BYTES = 64 * 1024 * 1024
SUFFIXES = (".obj", ".skeleton.json", ".weights.json", ".motions.json")


def _integer(value, minimum, maximum, label):
    if type(value) is not int or not minimum <= value <= maximum:
        raise ValueError(f"Invalid {label}")
    return value


def _vector(value, size, label):
    if (not isinstance(value, list) or len(value) != size or any(
            isinstance(x, bool) or not isinstance(x, (int, float))
            or not math.isfinite(x) or abs(x) > 100000 for x in value)):
        raise ValueError(f"Invalid {label}")


def build_plan(character, vertices, uvs, faces, face_uvs, skeleton, weights, motions,
               *, palette_limit=MAX_PALETTE, part="body"):
    """Stable contiguous triangle batching; complexity is linear in corners.

    Ancestors need not occupy shader slots: evaluate the complete source rig
    first, then gather each batch's global skin matrices using source bone IDs.
    """
    source_stem(character, part)
    _integer(palette_limit, 1, MAX_PALETTE, "draw palette limit")
    if not 3 <= len(vertices) <= MAX_VERTICES or not 1 <= len(faces) <= MAX_FACES:
        raise ValueError("Source palette geometry exceeds its bounded budget")
    if len(uvs) > MAX_DRAW_VERTICES:
        raise ValueError("Source UV inventory exceeds its bounded budget")
    if len(face_uvs) != len(faces):
        raise ValueError("Source face/UV inventory differs")
    try:
        check_sidecars(character, skeleton, weights, motions, len(vertices), part=part)
    except (TypeError, AttributeError, KeyError, RecursionError) as error:
        raise ValueError("Malformed source rig/weight inventory") from error
    for bone in skeleton["Bones"]:
        _integer(bone["Id"], 0, 0xFFFFFFFE, "source bone ID")
        _integer(bone["Parent"], 0, 0xFFFFFFFF, "source parent ID")
        _vector(bone["Position"], 3, "source bone position")
    for point in vertices:
        _vector(point, 3, "source position")
    for uv in uvs:
        _vector(uv, 2, "source UV")
    groups = []
    ignored_zero_weights = 0
    for source in weights["groups"]:
        seen, influences = set(), []
        for item in source:
            bone = _integer(item["boneId"], 0, 0xFFFFFFFE, "influence source ID")
            weight = item["weight"]
            if isinstance(weight, bool) or not isinstance(weight, (int, float)) or not math.isfinite(weight):
                raise ValueError("Invalid source influence weight")
            if bone in seen:
                raise ValueError("Duplicate source bone influence; no implicit merge")
            seen.add(bone)
            if weight > 0:
                influences.append((bone, weight))
            else:
                ignored_zero_weights += 1
        if not 1 <= len(influences) <= 4:
            raise ValueError("Source draw vertex needs one to four positive influences")
        groups.append(influences)
    references = [_integer(x, 0, len(groups)-1, "source group reference") for x in weights["vertexGroupIds"]]
    batches, pending, used = [], [], set()
    total_draw_vertices = 0

    def flush():
        nonlocal total_draw_vertices
        if not pending:
            return
        if len(batches) >= MAX_BATCHES:
            raise ValueError("Source palette partition count exceeds budget")
        palette = sorted(used)
        local = {bone: index for index, bone in enumerate(palette)}
        corners, indices, lookup = [], [], {}
        for face_id in pending:
            for vertex, uv in zip(faces[face_id], face_uvs[face_id]):
                key = (vertex, uv)
                if key not in lookup:
                    lookup[key] = len(corners)
                    influences = groups[references[vertex]]
                    corners.append({"sourceVertex": vertex, "sourceUv": uv,
                        "position": list(vertices[vertex]), "uv": list(uvs[uv]) if uv >= 0 else None,
                        "joints": [local[bone] for bone, _ in influences],
                        "weights": [weight for _, weight in influences]})
                indices.append(lookup[key])
        total_draw_vertices += len(corners)
        if total_draw_vertices > MAX_DRAW_VERTICES:
            raise ValueError("Duplicated source draw vertex count exceeds budget")
        batches.append({"palette": palette, "sourceFaces": list(pending), "vertices": corners, "indices": indices})

    for face_id, (face, texcoords) in enumerate(zip(faces, face_uvs)):
        if not isinstance(face, list) or len(face) != 3 or not isinstance(texcoords, list) or len(texcoords) != 3:
            raise ValueError("Source palette requires exact triangle corner inventories")
        triangle = set()
        for vertex, uv in zip(face, texcoords):
            _integer(vertex, 0, len(vertices)-1, "source triangle vertex")
            _integer(uv, -1, len(uvs)-1, "source triangle UV")
            triangle.update(bone for bone, _ in groups[references[vertex]])
        if len(set(face)) != 3:
            raise ValueError("Degenerate source triangle")
        if len(triangle) > palette_limit:
            raise ValueError("One source triangle exceeds draw palette; influences cannot be pruned")
        if len(used | triangle) > palette_limit:
            flush()
            pending, used = [], set()
        pending.append(face_id)
        used.update(triangle)
    flush()
    plan = {"format": 1, "kind": "private-source-rig-palette-preflight", "character": character,
        "paletteLimit": palette_limit, "sourceRig": copy.deepcopy(skeleton),
        "sourceMotionInventory": copy.deepcopy(motions),
        "sourceVertexCount": len(vertices), "sourceTriangleCount": len(faces),
        "drawVertexCount": total_draw_vertices, "ignoredZeroWeightEntries": ignored_zero_weights,
        "matrixGather": "evaluate-full-source-hierarchy-then-gather-by-source-bone-id",
        "batches": batches,
        "acceptance": {"runtimeInstall": False, "sourceRigPlayback": False, "fullClipCoverage": False,
            "firstPerson": False, "alternateForm": False, "gameplay": False, "network": False,
            "deviceVisual": False}}
    if part == "ball":
        plan["kind"] = "private-source-part-rig-palette-preflight"
        plan["sourcePart"] = part
    return plan


def _stamps(root, character, *, part="body"):
    stem = source_stem(character, part)
    result = {}
    for suffix in SUFFIXES:
        path = root / (stem + suffix)
        if root.is_symlink() or path.is_symlink() or not path.is_file():
            raise ValueError("Local palette input must be a regular private file")
        maximum = 256*1024*1024 if suffix == ".obj" else 32*1024*1024
        if path.stat().st_size > maximum:
            raise ValueError("Local palette input exceeds file budget")
        digest = hashlib.sha256()
        with path.open("rb") as source:
            for block in iter(lambda: source.read(1024*1024), b""):
                digest.update(block)
        result[stem+suffix] = digest.hexdigest()
    return result


def write_private_plan(root: Path, character: str, output: Path, *, part="body"):
    source_stem(character, part)
    if output.is_symlink() or output.exists() or not output.resolve().is_relative_to(root.resolve()):
        raise ValueError("Private palette output must be a new contained regular path")
    before = _stamps(root, character, part=part)
    vertices, uvs, faces, face_uvs, skeleton, weights, motions = load_inputs(root, character, part=part)
    plan = build_plan(character, vertices, uvs, faces, face_uvs, skeleton, weights, motions, part=part)
    if before != _stamps(root, character, part=part):
        raise ValueError("Private source changed during palette planning")
    plan["sourceSha256"] = before
    encoded = json.dumps(plan, ensure_ascii=True, allow_nan=False, separators=(",", ":")) + "\n"
    if len(encoded.encode("utf-8")) > MAX_PLAN_BYTES:
        raise ValueError("Private palette plan exceeds output budget")
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("x", encoding="utf-8") as target:
        target.write(encoded)
    return plan
