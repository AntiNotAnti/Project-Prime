"""Build a NEW Source-material response candidate without changing accepted packs.

Only G/B in existing PNG metallicRoughness maps change. Source atlas layouts
provide named regions; masks come from the already accepted authored map, not
from color/normal heuristics. Albedo, recolors, normals, emission, alpha/culling,
samplers, native mappings and the original geometry blob remain byte-exact.
This is an authored forward response candidate, not measured physical PBR.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import io
import json
import math
import shutil
import struct
from pathlib import Path, PurePosixPath

import numpy as np
from PIL import Image

from glb import image_bytes, load
from source_material_profile import snapshot as material_contract_snapshot

CONTRACT_POLICY = Path(__file__).resolve().parents[1] / "source-material-profile.json"


def dependency_hashes():
    return {str(path): sha(path) for path in [Path(__file__).resolve(), Path(__file__).with_name("glb.py"),
            Path(__file__).with_name("source_material_profile.py"), Path(__file__).with_name("mobile_images.py"), CONTRACT_POLICY]}


def sha_bytes(value):
    return hashlib.sha256(value).hexdigest()


def sha(path):
    return sha_bytes(Path(path).read_bytes())


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"))


def within(root, relative):
    path = PurePosixPath(relative)
    root = Path(root).resolve()
    if not relative or "\\" in relative or path.is_absolute() or any(x in (".", "..") for x in relative.split("/")):
        raise ValueError(f"Unsafe pack path: {relative}")
    result = root.joinpath(*path.parts)
    if not result.resolve().is_relative_to(root):
        raise ValueError(f"Pack path leaves its root: {relative}")
    return result


def files(root):
    result = {}
    for path in sorted(Path(root).rglob("*")):
        if path.is_symlink():
            raise ValueError(f"Symlink in source pack: {path}")
        if path.is_file():
            result[path.relative_to(root).as_posix()] = sha(path)
    return result


def profile(path):
    result = json.loads(Path(path).read_text())
    if (result.get("format") != 1 or result.get("preserveHunters") != ["Samus"]
            or result.get("preserveSelfIllumMaterials") is not True
            or result.get("preserveChannels") != ["albedo", "normal", "emissive", "recolor"]):
        raise ValueError("Unexpected material response preservation policy")
    if set(result.get("hunters", [])) != {"Kanden", "Noxus", "Spire", "Sylux", "Trace", "Weavel"}:
        raise ValueError("Material response requires the six non-Samus hunters")
    for name, setting in result["materials"].items():
        exponent, strength = setting["forwardExponent"], setting["specularStrength"]
        if (not all(math.isfinite(x) for x in (exponent, strength))
                or not 4 <= exponent <= 72 or not 0 <= strength <= 1):
            raise ValueError(f"Unbounded response target: {name}")
    return result


def texture_payload(doc, blob, info):
    if not info:
        return None
    if info.get("texCoord", 0) != 0 or info.get("extensions"):
        raise ValueError("Response candidates require plain UV0 textures")
    texture = doc["textures"][info["index"]]
    if texture.get("extensions") or "source" not in texture:
        raise ValueError("Desktop response candidates require plain embedded PNG textures")
    return image_bytes(doc, blob, texture["source"])


def material_signature(doc, blob, material):
    pbr = material.get("pbrMetallicRoughness", {})
    return tuple(sha_bytes(payload) if payload is not None else None for payload in (
        texture_payload(doc, blob, pbr.get("baseColorTexture")),
        texture_payload(doc, blob, material.get("normalTexture")),
        texture_payload(doc, blob, pbr.get("metallicRoughnessTexture"))))


def evidence(roots):
    index = {}
    for root in roots:
        for path in sorted(Path(root).resolve().rglob("atlas-layout.json")):
            report_path = path.parent / "SOURCE-MATERIALS.json"
            if not report_path.is_file():
                continue
            layout, report = json.loads(path.read_text()), json.loads(report_path.read_text())
            for native, group in layout.get("groups", {}).items():
                texture_paths = {channel: path.parent / "textures" / f"{native}-{channel}.png"
                                 for channel in ("albedo", "normal", "material")}
                if not texture_paths["material"].is_file() or not texture_paths["albedo"].is_file():
                    continue
                hashes = tuple(sha(texture_paths[channel]) if texture_paths[channel].is_file() else None
                               for channel in ("albedo", "normal", "material"))
                map_size=list(Image.open(texture_paths["material"]).size)
                if not group.get("directPeriodicTexture") and map_size != group["size"]:
                    raise ValueError(f"Packed material atlas dimensions differ from layout: {path}")
                regions = []
                source_inputs = {}
                for source in group["sources"]:
                    spec = layout["materials"][source]
                    material = report["materials"][source]
                    for field, hash_field in (("vmt", "vmtSha256"), ("detail", "detailSha256"),
                                             ("phongMaskSource", "phongMaskSourceSha256")):
                        if material.get(field):
                            source_path = Path(material[field]).resolve()
                            actual = sha(source_path)
                            if actual != material[hash_field]:
                                raise ValueError(f"Original Source material input changed: {source_path}")
                            source_inputs[str(source_path)] = actual
                    if group.get("directPeriodicTexture"):
                        if len(group["sources"]) != 1:
                            raise ValueError("Direct periodic texture has multiple Source material owners")
                        # Direct groups retain the original independent channel
                        # resolutions. Their roughness map need not match albedo.
                        rect = [0, 0, *map_size]
                    else:
                        pad = layout["paddingPixels"]
                        x, y = spec["offsetPixels"]
                        w, h = spec["pixelSize"]
                        rect = [x-pad, y-pad, w+2*pad, h+2*pad]
                    regions.append({"sourceMaterial": source, "rect": rect,
                                    "selfIllum": bool(material.get("selfIllum")),
                                    "sourcePhongEnabled": bool(material.get("sourcePhongEnabled")),
                                    "boundedPhongStrength": material.get("boundedPhongStrength"),
                                    "forwardRoughness": material.get("forwardRoughness"),
                                    "maskSource": material.get("phongMask"),
                                    "maskRange": material.get("phongMaskSampleRange")})
                value = {"layoutPath": str(path), "layoutSha256": sha(path),
                         "sourceMaterialPath": str(report_path), "sourceMaterialSha256": sha(report_path),
                         "sourceSha256": layout["sourceSha256"], "size": map_size,
                         "nativeIdentity": native, "regions": sorted(regions, key=lambda x: x["sourceMaterial"]),
                         "sourceInputSha256": source_inputs,
                         "texturePaths": {k: str(v) for k, v in texture_paths.items() if v.is_file()},
                         "textureSha256": {k: sha(v) for k, v in texture_paths.items() if v.is_file()}}
                if hashes in index and canonical(index[hashes]["regions"]) != canonical(value["regions"]):
                    raise ValueError(f"Ambiguous Source region ownership for identical texture signatures: {path}")
                index.setdefault(hashes, value)
    return index


def check_evidence(value):
    if (sha(value["layoutPath"]) != value["layoutSha256"]
            or sha(value["sourceMaterialPath"]) != value["sourceMaterialSha256"]):
        raise ValueError("Source material/layout evidence changed")
    for channel, path in value["texturePaths"].items():
        if sha(path) != value["textureSha256"][channel]:
            raise ValueError("Source atlas pixels changed")
    for path, digest in value["sourceInputSha256"].items():
        if sha(path) != digest:
            raise ValueError(f"Original Source material/mask input changed: {path}")


def inventory(source_pack, roots, profile_path, destination):
    root = Path(source_pack).resolve()
    policy = profile(profile_path)
    manifest = json.loads((root / "characters.json").read_text())
    groups = evidence(roots)
    rows = {}
    for entry in manifest["models"]:
        if entry["hunter"] not in policy["hunters"]:
            continue
        doc, blob = load(within(root, entry["model"]))
        materials = {}
        for i, material in enumerate(doc.get("materials", [])):
            signature = material_signature(doc, blob, material)
            if signature[-1] is None:
                continue
            value = groups.get(signature)
            if value is None:
                raise ValueError(f"No exact Source atlas evidence for {entry['model']} material {material['name']}")
            materials[str(i)] = value
        rows[entry["model"]] = {"hunter": entry["hunter"], "part": entry["part"],
                                "lod": entry.get("lod", 0), "materials": materials}
    result = {"format": 1, "pass": True, "sourcePack": str(root), "files": files(root),
              "profilePath": str(Path(profile_path).resolve()), "profileSha256": sha(profile_path),
              "toolSha256": sha(__file__), "dependencySha256": dependency_hashes(), "assets": rows,
              "scope": "Input and exact atlas ownership lock only; no visual/material/gameplay acceptance."}
    destination = Path(destination).resolve()
    if destination.is_relative_to(root):
        raise ValueError("Input lock must be outside the source pack")
    destination.parent.mkdir(parents=True, exist_ok=True)
    with destination.open("x") as stream:
        json.dump(result, stream, indent=2); stream.write("\n")
    return result


def tuned_map(payload, group, policy):
    image = Image.open(io.BytesIO(payload)).convert("RGBA")
    if list(image.size) != group["size"]:
        raise ValueError(f"Atlas map dimensions differ from Source ownership evidence: {group['nativeIdentity']}")
    before = np.asarray(image, dtype=np.uint8)
    after = before.copy()
    ownership = np.zeros(before.shape[:2], dtype=bool)
    changes = []
    for region in group["regions"]:
        x, y, w, h = region["rect"]
        if min(x, y) < 0 or min(w, h) <= 0 or x+w > image.width or y+h > image.height:
            raise ValueError("Source atlas region exceeds material image")
        if ownership[y:y+h, x:x+w].any():
            raise ValueError("Overlapping Source atlas region ownership")
        ownership[y:y+h, x:x+w] = True
        name = region["sourceMaterial"]
        setting = policy["materials"].get(name)
        if not setting or region["selfIllum"] or not region["sourcePhongEnabled"]:
            continue
        bounded = region["boundedPhongStrength"]
        if bounded is None or bounded <= 0 or bounded > 1:
            raise ValueError(f"Cannot recover an existing Phong mask from {name}")
        original = before[y:y+h, x:x+w]
        old_roughness = round(region["forwardRoughness"]*255)
        if np.any(original[..., 1] != old_roughness) or np.any(original[..., 2] > math.ceil(bounded*255)):
            raise ValueError(f"Accepted Phong map differs from locked Source scalar/mask: {name}")
        roughness = min(1, max(.04, (72-setting["forwardExponent"])/68))
        after[y:y+h, x:x+w, 1] = round(roughness*255)
        after[y:y+h, x:x+w, 2] = np.round(np.clip(original[..., 2].astype(np.float64)
                                                * setting["specularStrength"]/bounded, 0, 255)).astype(np.uint8)
        changes.append({"sourceMaterial": name, "rect": region["rect"], "reason": setting["reason"],
                        "originalStrength": bounded, "targetStrength": setting["specularStrength"],
                        "originalRoughness": region["forwardRoughness"], "targetRoughness": roughness,
                        "targetForwardExponent": setting["forwardExponent"], "maskSource": region["maskSource"],
                        "maskRange": region["maskRange"], "maskPolicy": "Scale the accepted encoded Source alpha mask; no inferred color/normal segmentation."})
    if not np.array_equal(before[..., (0, 3)], after[..., (0, 3)]):
        raise ValueError("Unused material R/alpha changed")
    if np.array_equal(before, after):
        return payload, {"changed": False, "regions": changes}
    output = io.BytesIO(); Image.fromarray(after, "RGBA").save(output, format="PNG")
    return output.getvalue(), {"changed": True, "regions": changes,
                              "changedTexels": int(np.any(before != after, axis=2).sum()),
                              "originalPixelSha256": sha_bytes(before.tobytes()),
                              "candidatePixelSha256": sha_bytes(after.tobytes())}


def write_glb(path, doc, blob):
    doc["buffers"][0]["byteLength"] = len(blob)
    encoded = json.dumps(doc, separators=(",", ":")).encode(); encoded += b" "*(-len(encoded)%4)
    blob = bytes(blob)+b"\0"*(-len(blob)%4)
    data = (struct.pack("<4sII", b"glTF", 2, 28+len(encoded)+len(blob))
            + struct.pack("<I4s", len(encoded), b"JSON")+encoded
            + struct.pack("<I4s", len(blob), b"BIN\0")+blob)
    path.write_bytes(data)


def protected_images(doc, blob):
    # All original images remain embedded and byte-exact. Recolors/native extras
    # can use extension fields, so preserve the entire original image array too.
    return [sha_bytes(image_bytes(doc, blob, i)) for i in range(len(doc.get("images", [])))]


def build(source_pack, lock_path, profile_path, output):
    root, output = Path(source_pack).resolve(), Path(output).resolve()
    lock = json.loads(Path(lock_path).read_text()); policy = profile(profile_path)
    if output.exists() or output.is_relative_to(root) or root.is_relative_to(output):
        raise ValueError("Response output must be fresh and separate from the source pack")
    if (lock.get("format") != 1 or lock["sourcePack"] != str(root) or lock["files"] != files(root)
            or lock["profileSha256"] != sha(profile_path) or lock["toolSha256"] != sha(__file__)
            or lock["dependencySha256"] != dependency_hashes()):
        raise ValueError("Material candidate inputs/tool changed after inventory")
    for asset in lock["assets"].values():
        for group in asset["materials"].values():
            check_evidence(group)
    shutil.copytree(root, output)
    report = {"format": 1, "pass": True, "accepted": False, "profile": policy,
              "inputLockPath": str(Path(lock_path).resolve()), "inputLockSha256": sha(lock_path),
              "profileSha256": sha(profile_path), "sourcePack": str(root), "candidatePack": str(output),
              "assets": {}, "requiresMobileRebuild": True,
              "scope": policy["scope"]}
    cache = {}
    for relative, asset in lock["assets"].items():
        source = within(root, relative); target = within(output, relative)
        doc, original_blob = load(source); blob = bytearray(original_blob)
        original_doc = copy.deepcopy(doc)
        original_images = protected_images(doc, blob)
        rows = []
        embedded = {}
        for index, group in asset["materials"].items():
            material = doc["materials"][int(index)]; pbr = material["pbrMetallicRoughness"]
            if pbr.get("metallicFactor", 1) != 1 or pbr.get("roughnessFactor", 1) != 1:
                raise ValueError("Material response maps require identity glTF factors")
            info = pbr["metallicRoughnessTexture"]
            payload = texture_payload(doc, blob, info)
            identity = sha_bytes(payload)+sha_bytes(canonical(group["regions"]).encode())
            if identity not in cache:
                cache[identity] = tuned_map(payload, group, policy)
            candidate, detail = cache[identity]
            rows.append({"index": int(index), "nativeIdentity": material["name"], **detail,
                         "originalMapSha256": sha_bytes(payload), "candidateMapSha256": sha_bytes(candidate)})
            if candidate == payload:
                continue
            digest = sha_bytes(candidate)
            if digest not in embedded:
                blob.extend(b"\0"*(-len(blob)%4)); offset = len(blob); blob.extend(candidate)
                view = len(doc["bufferViews"])
                doc["bufferViews"].append({"buffer": 0, "byteOffset": offset, "byteLength": len(candidate)})
                image = len(doc["images"])
                doc["images"].append({"bufferView": view, "mimeType": "image/png", "name": f"authored-response-{digest}"})
                embedded[digest] = image
            texture = copy.deepcopy(doc["textures"][info["index"]]); texture["source"] = embedded[digest]
            replacement = len(doc["textures"]); doc["textures"].append(texture)
            info["index"] = replacement
        if any(row["changed"] for row in rows):
            write_glb(target, doc, blob)
        actual_doc, actual_blob = load(target)
        if (actual_blob[:len(original_blob)] != original_blob
                or protected_images(actual_doc, actual_blob)[:len(original_images)] != original_images):
            raise ValueError("Original embedded artwork/geometry bytes changed")
        # The only editable existing document field is the material-map texture
        # index. Everything else (skins, weights, UV0/UV1, factors, samplers,
        # source alpha/recolors/native material metadata) is unchanged.
        normalized = copy.deepcopy(actual_doc)
        normalized["images"] = normalized.get("images", [])[:len(original_doc.get("images", []))]
        normalized["textures"] = normalized.get("textures", [])[:len(original_doc.get("textures", []))]
        normalized["bufferViews"] = normalized["bufferViews"][:len(original_doc["bufferViews"])]
        normalized["buffers"] = original_doc["buffers"]
        for index in asset["materials"]:
            original_info = original_doc["materials"][int(index)]["pbrMetallicRoughness"]["metallicRoughnessTexture"]
            normalized["materials"][int(index)]["pbrMetallicRoughness"]["metallicRoughnessTexture"] = original_info
        if normalized != original_doc:
            raise ValueError("Response candidate changed a field beyond its material-map binding")
        report["assets"][relative] = {"hunter": asset["hunter"], "part": asset["part"], "lod": asset["lod"],
                                      "originalGlbSha256": sha(source), "candidateGlbSha256": sha(target),
                                      "originalBlobPrefixUnchanged": True, "originalImagesByteExact": True,
                                      "geometryNativeBindingsAndAllOtherFieldsUnchanged": True, "materials": rows}
    manifest = json.loads((output/"characters.json").read_text())
    original_manifest = json.loads((root/"characters.json").read_text())
    manifest["id"] = policy["id"]
    for entry in manifest["models"]:
        if entry["hunter"] in policy["hunters"]:
            entry.pop("mobileModel", None)  # Stale response tier is never selected.
    (output/"characters.json").write_text(json.dumps(manifest, indent=2)+"\n")
    if ([entry for entry in manifest["models"] if entry["hunter"] == "Samus"]
            != [entry for entry in original_manifest["models"] if entry["hunter"] == "Samus"]):
        raise ValueError("Samus manifest entries changed")
    preserved = {}
    for relative, digest in lock["files"].items():
        if relative.startswith(("samus/", "mobile/samus/")):
            if sha(within(output, relative)) != digest:
                raise ValueError(f"Samus baseline changed: {relative}")
            preserved[relative] = digest
    if (lock["files"] != files(root) or lock["profileSha256"] != sha(profile_path)
            or lock["toolSha256"] != sha(__file__) or lock["dependencySha256"] != dependency_hashes()
            or report["inputLockSha256"] != sha(lock_path)):
        raise ValueError("Source inputs changed during candidate generation")
    for asset in lock["assets"].values():
        for group in asset["materials"].values():
            check_evidence(group)
    report["samusPreservedFiles"] = preserved
    report["changedAssets"] = sum(any(m["changed"] for m in asset["materials"]) for asset in report["assets"].values())
    if report["changedAssets"] == 0:
        raise ValueError("No material response change was generated")
    contract = material_contract_snapshot(output, json.loads(CONTRACT_POLICY.read_text()))
    report["lodImagePeers"] = contract["lodPeers"]
    report["uniqueSemanticImagePayloads"] = contract["uniqueSemanticImagePayloads"]
    report["sharedVariantMaterialPayloads"] = {key: sha_bytes(value[0]) for key, value in cache.items()}
    if (lock["files"] != files(root) or lock["dependencySha256"] != dependency_hashes()
            or report["inputLockSha256"] != sha(lock_path)):
        raise ValueError("Source inputs changed during final candidate audit")
    report["candidatePackFiles"] = files(output)
    (output/"MATERIAL-RESPONSE-CANDIDATE.json").write_text(json.dumps(report, indent=2)+"\n")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("inventory", "build"))
    parser.add_argument("--source-pack", required=True)
    parser.add_argument("--profile", required=True)
    parser.add_argument("--evidence-root", action="append", default=[])
    parser.add_argument("--input-lock")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    if args.command == "inventory":
        if not args.evidence_root:
            parser.error("inventory needs at least one --evidence-root")
        result = inventory(args.source_pack, args.evidence_root, args.profile, args.output)
        print(json.dumps({"pass": result["pass"], "assets": len(result["assets"]), "output": args.output}))
    else:
        if not args.input_lock:
            parser.error("build needs --input-lock")
        result = build(args.source_pack, args.input_lock, args.profile, args.output)
        print(json.dumps({"pass": result["pass"], "accepted": False, "changedAssets": result["changedAssets"], "output": args.output}))


if __name__ == "__main__":
    main()
