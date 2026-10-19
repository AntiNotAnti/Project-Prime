#!/usr/bin/env python3
"""PORT-22: source/native rig calibration and Weighted4 palette preflight.

Offline authoring diagnostics only; this never installs a playable Hunter.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

SOURCE_HUNTERS = {
    "darksamus-echoes": "Samus",
    "trooper-power": "Weavel",
    "trooper-ice": "Weavel",
    "trooper-wave": "Weavel",
    "trooper-plasma": "Weavel",
}
# Stable catalog names, not Hunter enum or network IDs. MP3 is reserved only.
SOURCE_APPEARANCES = {
    "darksamus-echoes": ("darksamus", "MP2DarkSamus"),
    "trooper-power": ("trooper", "MP1PowerTrooper"),
    "trooper-ice": ("trooper", "MP1IceTrooper"),
    "trooper-wave": ("trooper", "MP1WaveTrooper"),
    "trooper-plasma": ("trooper", "MP1PlasmaTrooper"),
}
MAX_MAP_BYTES = 128 * 1024
MAX_NATIVE_BYTES = 8 * 1024 * 1024
MAX_PALETTE = 32
MAX_ANCHORS = 128
MAX_BONES = 1024
MAX_GROUPS = 65536
MAX_ERROR = 1.0


def finite(value, what, bound=100000.0):
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"{what} must be a real number")
    number = float(value)
    if not math.isfinite(number) or abs(number) > bound:
        raise ValueError(f"{what} must be finite and bounded")
    return number


def vector(value, what):
    if not isinstance(value, list) or len(value) != 3:
        raise ValueError(f"{what} requires exactly three components")
    return tuple(finite(x, what) for x in value)


def source_id(value, what):
    if type(value) is not int or not 0 <= value <= 0xFFFFFFFF:
        raise ValueError(f"{what} must be an unsigned source bone ID")
    return value


def transform_basis(basis):
    if (not isinstance(basis, list) or len(basis) != 3 or
            any(not isinstance(row, list) or len(row) != 3 for row in basis)):
        raise ValueError("Axis conversion requires a signed 3x3 permutation matrix")
    for row in basis:
        if any(type(x) is not int or x not in (-1, 0, 1) for x in row):
            raise ValueError("Axis conversion must use signed unit axes")
        if sum(abs(x) for x in row) != 1:
            raise ValueError("Each axis-conversion row must select one source axis")
    if any(sum(abs(basis[i][j]) for i in range(3)) != 1 for j in range(3)):
        raise ValueError("Every source axis must be used exactly once")
    determinant = (
        basis[0][0] * (basis[1][1] * basis[2][2] - basis[1][2] * basis[2][1])
        - basis[0][1] * (basis[1][0] * basis[2][2] - basis[1][2] * basis[2][0])
        + basis[0][2] * (basis[1][0] * basis[2][1] - basis[1][1] * basis[2][0])
    )
    if abs(determinant) != 1:
        raise ValueError("Axis conversion must be invertible")
    return determinant


def transformed(point, basis, scale, offset):
    return tuple(offset[i] + scale * sum(basis[i][j] * point[j] for j in range(3))
                 for i in range(3))


def _noncollinear(points):
    a = points[0]
    for j in range(1, len(points)):
        u = tuple(points[j][k] - a[k] for k in range(3))
        uu = sum(x*x for x in u)
        if uu <= 1e-12:
            continue
        for m in range(j + 1, len(points)):
            v = tuple(points[m][k] - a[k] for k in range(3))
            vv = sum(x*x for x in v)
            if vv <= 1e-12:
                continue
            dot = sum(u[k]*v[k] for k in range(3))
            if max(0.0, uu*vv-dot*dot) > 1e-4 * uu * vv:
                return True
    return False


def _native_biped(native, hunter):
    if not isinstance(native, dict) or native.get("format") != 1 or native.get("hunter") != hunter:
        raise ValueError("Native kit must match the selected fallback Hunter")
    entries = native.get("models")
    if not isinstance(entries, list) or not 1 <= len(entries) <= 8:
        raise ValueError("Native reference model inventory missing or oversized")
    matches = [m for m in entries if isinstance(m, dict) and m.get("part") == "biped" and m.get("lod") == 0]
    if len(matches) != 1:
        raise ValueError("Native kit requires exactly one LOD0 biped reference")
    model = matches[0]
    nodes = model.get("nodes")
    palette = model.get("matrixPalette")
    if (not isinstance(nodes, list) or not 1 <= len(nodes) <= 4096 or
            not isinstance(palette, list) or not 1 <= len(palette) <= 64):
        raise ValueError("Native node or matrix-palette inventory invalid")
    by_name = {}
    for node in nodes:
        if not isinstance(node, dict) or not isinstance(node.get("name"), str):
            raise ValueError("Malformed native skeleton node")
        name = node["name"]
        if not 1 <= len(name) <= 128 or name in by_name:
            raise ValueError("Empty or duplicate native bone name")
        by_name[name] = node
        # Emitted from Model.ComputeNodeMatrices into native-reference.json.
        vector(node.get("bindWorldPosition"), "native engine bind-world position")
    indexed_palette = {}
    for item in palette:
        if not isinstance(item, dict) or type(item.get("slot")) is not int:
            raise ValueError("Invalid native matrix-palette entry")
        slot = item["slot"]
        name = item.get("nodeName")
        if slot < 0 or slot >= len(palette) or slot in indexed_palette or name not in by_name:
            raise ValueError("Missing or duplicate native palette node")
        indexed_palette[slot] = name
    if len(set(indexed_palette.values())) != len(indexed_palette):
        raise ValueError("Duplicate native matrix-palette joint")
    return by_name, {name: slot for slot, name in indexed_palette.items()}


def validate_contract(skeleton, weights, native, mapping):
    if not isinstance(mapping, dict) or mapping.get("format") != 1:
        raise ValueError("Unsupported explicit retarget-map format")
    ident = mapping.get("sourceCharacter")
    if ident not in SOURCE_HUNTERS or mapping.get("nativeHunter") != SOURCE_HUNTERS[ident]:
        raise ValueError("Source identity does not match the expected gameplay fallback Hunter")
    if mapping.get("nativePart") != "biped":
        raise ValueError("This slice only supports native LOD0 biped reference mapping")
    if not isinstance(skeleton, dict) or skeleton.get("Id") != ident:
        raise ValueError("CINF source skeleton belongs to a different character")
    bones = skeleton.get("Bones")
    if not isinstance(bones, list) or not 1 <= len(bones) <= MAX_BONES:
        raise ValueError("Source skeleton has an invalid bone count")
    by_id = {}
    for bone in bones:
        if not isinstance(bone, dict):
            raise ValueError("Malformed CINF bone")
        ident_bone = source_id(bone.get("Id"), "CINF bone")
        if ident_bone in by_id:
            raise ValueError("Duplicate CINF bone identifier")
        source_id(bone.get("Parent"), "CINF parent")
        by_id[ident_bone] = vector(bone.get("Position"), "source CINF bind position")
    parents = {bone["Id"]: bone["Parent"] for bone in bones}
    for bone_id in parents:
        ancestors = set()
        cursor = bone_id
        while cursor != 0xFFFFFFFF:
            if cursor not in parents or cursor in ancestors:
                raise ValueError("Missing parent or cycle in source CINF hierarchy")
            ancestors.add(cursor)
            cursor = parents[cursor]
    native_nodes, native_slots = _native_biped(native, mapping["nativeHunter"])
    bone_map = mapping.get("boneMap")
    if not isinstance(bone_map, dict) or not 1 <= len(bone_map) <= MAX_BONES:
        raise ValueError("Explicit CINF-to-native map is absent or oversized")
    mapped = {}
    for id_text, name in bone_map.items():
        if not isinstance(id_text, str) or not id_text.isascii() or not id_text.isdecimal():
            raise ValueError("Source map keys must be decimal CINF IDs")
        bone_id = int(id_text)
        if bone_id not in by_id or id_text != str(bone_id):
            raise ValueError("CINF mapping references a missing source bone")
        if not isinstance(name, str) or name not in native_slots:
            raise ValueError("Retarget mapping references an absent native palette node")
        mapped[bone_id] = name

    refs = weights.get("vertexGroupIds") if isinstance(weights, dict) else None
    groups = weights.get("groups") if isinstance(weights, dict) else None
    vertex_count = weights.get("vertexCount") if isinstance(weights, dict) else None
    if (not isinstance(weights, dict) or weights.get("format") != 1
            or type(vertex_count) is not int or vertex_count < 3 or vertex_count > 2000000
            or not isinstance(refs, list) or len(refs) != vertex_count
            or not isinstance(groups, list) or not 1 <= len(groups) <= MAX_GROUPS):
        raise ValueError("Invalid source CSKR sidecar")
    selected = set()
    for value in refs:
        if type(value) is not int or not 0 <= value < len(groups):
            raise ValueError("Source vertex references an invalid CSKR group")
        selected.add(value)

    rewritten = {}
    used = set()
    for group_id in sorted(selected):
        group = groups[group_id]
        if not isinstance(group, list) or not 1 <= len(group) <= 64:
            raise ValueError("Malformed source CSKR group")
        totals = {}
        total = 0.0
        nonzero = 0
        for item in group:
            if not isinstance(item, dict):
                raise ValueError("Malformed source CSKR influence")
            bone = source_id(item.get("boneId"), "source CSKR influence")
            if bone not in by_id:
                raise ValueError("CSKR refers to missing CINF bone")
            weight = finite(item.get("weight"), "source weight", 1.0)
            if weight < 0:
                raise ValueError("Negative CSKR weight")
            if weight:
                nonzero += 1
                if bone not in mapped:
                    raise ValueError(f"Source skin bone {bone} has no explicit native mapping")
                used.add(mapped[bone])
                totals[mapped[bone]] = totals.get(mapped[bone], 0.0) + weight
                total += weight
        if not 1 <= nonzero <= 4 or total <= 0:
            raise ValueError("Runtime candidate requires 1-4 positive source influences")
        if abs(total - 1.0) > 0.02:
            raise ValueError("Source CSKR weights are not sufficiently normalized for a native candidate")
        if len(totals) > 4:
            raise ValueError("Weighted4 candidate has more than four native influences")
        rewritten[str(group_id)] = [
            {"nativeNode": node, "weight": totals[node] / total}
            for node in sorted(totals, key=lambda n: native_slots[n])
        ]
    if not 1 <= len(used) <= MAX_PALETTE:
        raise ValueError("Retargeted model exceeds the native 32-joint Weighted4 palette")

    geometry = mapping.get("sourceToNative")
    if not isinstance(geometry, dict):
        raise ValueError("No explicit source-to-native coordinate calibration")
    basis = geometry.get("axis")
    determinant = transform_basis(basis)
    scale = finite(geometry.get("scale"), "uniform calibration scale")
    if not 1e-5 <= scale <= 1000:
        raise ValueError("Calibration scale must be positive and bounded")
    offset = vector(geometry.get("offset"), "calibration offset")
    threshold = finite(mapping.get("maxAnchorError"), "maximum anchor error")
    if not 0 < threshold <= MAX_ERROR:
        raise ValueError("Explicit maximum anchor error is missing or too permissive")
    anchors = mapping.get("anchors")
    if not isinstance(anchors, list) or not 3 <= len(anchors) <= MAX_ANCHORS:
        raise ValueError("At least three distinct rig-calibration anchors are required")
    seen = set()
    src_points = []
    native_points = []
    errors = []
    for item in anchors:
        if not isinstance(item, dict):
            raise ValueError("Malformed native rig-calibration anchor")
        bone = source_id(item.get("sourceBoneId"), "anchor source bone")
        if bone not in mapped or bone in seen:
            raise ValueError("Anchor must map an unused, known CINF bone ID")
        seen.add(bone)
        target = vector(native_nodes[mapped[bone]]["bindWorldPosition"], "native calibrated bind position")
        error = math.dist(transformed(by_id[bone], basis, scale, offset), target)
        if not math.isfinite(error):
            raise ValueError("Nonfinite calibration residual")
        errors.append(error)
        src_points.append(by_id[bone])
        native_points.append(target)
    if not _noncollinear(src_points) or not _noncollinear(native_points):
        raise ValueError("Calibration needs noncollinear source and native anchors")
    rms = math.sqrt(sum(e*e for e in errors) / len(errors))
    maximum = max(errors)
    if maximum > threshold:
        raise ValueError(f"Native bind-pose calibration error {maximum:.6g} exceeds {threshold:.6g}")

    return {
        "format": 1, "kind": "local-retro-native-biped-retarget-diagnostic",
        "sourceCharacter": ident, "nativeHunter": mapping["nativeHunter"],
        "nativePart": "biped", "approvedForGameplay": False,
        "visualAcceptance": "required", "sampledAnimationsForGameplay": False,
        "palette": sorted(used, key=lambda n: native_slots[n]),
        "sourceToNative": geometry,
        "boneMap": {str(b): mapped[b] for b in sorted(mapped)},
        "sourceGroupToNativeWeights": rewritten,
        "sourceVertexGroupIds": refs,
        "coordinateHandednessFlip": determinant < 0,
        "calibration": {"anchors": len(anchors), "rmsError": rms,
                        "maximumError": maximum, "allowedError": threshold},
    }


def _bounded_json(path, max_bytes):
    if path.is_symlink() or not path.is_file() or not 0 < path.stat().st_size <= max_bytes:
        raise ValueError("Missing, linked or oversized private authoring input: " + str(path))
    with path.open(encoding="utf-8") as fh:
        return json.load(fh)


def main():
    parser = argparse.ArgumentParser(description="Offline private Retro/native Weighted4 rig correspondence preflight")
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--character", choices=sorted(SOURCE_HUNTERS), required=True)
    parser.add_argument("--native-reference", type=Path, required=True)
    parser.add_argument("--mapping", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if args.source_root.is_symlink():
        parser.error("Source preview root cannot be a symlink")
    from build_glb import load_inputs
    _, _, _, _, skeleton, weights, _ = load_inputs(args.source_root, args.character)
    native = _bounded_json(args.native_reference, MAX_NATIVE_BYTES)
    mapping = _bounded_json(args.mapping, MAX_MAP_BYTES)
    if mapping.get("sourceCharacter") != args.character:
        raise ValueError("Requested source character differs from explicit retarget mapping")
    diagnostic = validate_contract(skeleton, weights, native, mapping)
    print("Validated authoring-only LOD0 biped rig mapping:", args.character,
          "used palette", len(diagnostic["palette"]),
          "max bind-anchor error", diagnostic["calibration"]["maximumError"])
    if args.output is not None:
        if args.output.is_symlink() or args.output.suffix != ".json":
            raise ValueError("Private diagnostic output must be a regular JSON path, not a symlink")
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(diagnostic, indent=2) + "\n", encoding="utf-8")
        print("Authored private diagnostic (not installable):", args.output)


if __name__ == "__main__":
    main()
