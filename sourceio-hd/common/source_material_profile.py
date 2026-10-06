"""Capture/audit a source-faithful desktop material profile without asset writes.

The profile binds actual GLB/image bytes, factors, wrapping, alpha, native
overlay exceptions, current renderer code and original VMT evidence. It keeps
Source restoration separate from pending artist de-lighting/repaint work.
Use the bundled Python/Pillow/NumPy runtime. No GPU, encoder, or installer.
"""
from __future__ import annotations

import argparse
import collections
import hashlib
import io
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image

from glb import image_bytes, load
from mobile_images import usages

REPO = Path(__file__).resolve().parents[2]
POLICY = REPO / "sourceio-hd/source-material-profile.json"
CODE = ["sourceio-hd/common/source_material_profile.py", "sourceio-hd/common/source_materials.py",
        "sourceio-hd/common/material_maps.py", "sourceio-hd/common/mobile_images.py",
        "src/MphRead/Mods/Render/Characters/CharacterEmbeddedMaterialLoader.cs",
        "src/MphRead/Mods/Render/Characters/CharacterModelTextures.cs",
        "src/MphRead/Entities/Players/PlayerDraw.cs", "src/MphRead/Entities/Players/HalfturretEntity.cs",
        "src/MphRead/Mods/Render/EsShaders.cs", "src/MphRead/Mods/Render/ModernGraphicsShaders.cs"]


def digest(value):
    return hashlib.sha256(value).hexdigest()


def sha(path):
    return digest(Path(path).read_bytes())


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"))


def contained(root, relative):
    path = root / relative
    if not isinstance(relative, str) or Path(relative).is_absolute() or ".." in Path(relative).parts:
        raise ValueError("Material profile references an unsafe pack path")
    if not path.resolve().is_relative_to(root) or any(p.is_symlink() for p in [path, *path.parents] if p.is_relative_to(root)):
        raise ValueError("Material profile pack references must be contained regular files")
    if not path.is_file():
        raise FileNotFoundError(path)
    return path


def image_stats(payload, meaning):
    with Image.open(io.BytesIO(payload)) as image:
        if max(image.size) > 16384:
            raise ValueError("Material profile image exceeds the runtime dimension cap")
        image = image.convert("RGBA")
        result = {"size": list(image.size), "pixelSha256": digest(image.tobytes()),
                  "rgbaExtrema": [list(v) for v in image.getextrema()]}
        channel = meaning["channel"]
        if channel == "normal":
            minimum, maximum, count, total = math.inf, 0., 0, 0.
            for y in range(0, image.height, 32):
                xyz = np.asarray(image.crop((0, y, image.width, min(y + 32, image.height))), dtype=np.float32)[..., :3] / 127.5 - 1
                xyz[..., :2] *= meaning["normalScale"]
                lengths = np.linalg.norm(xyz, axis=2)
                minimum = min(minimum, float(lengths.min())); maximum = max(maximum, float(lengths.max()))
                total += float(lengths.sum(dtype=np.float64)); count += lengths.size
            result.update(normalLengthRange=[minimum, maximum], meanNormalLength=total / count,
                          normalsAreOriginalData=True, tangentBasisVisualProof=False)
        elif channel == "material":
            spec = 0 if meaning["runtimeEncoded"] else 2
            result.update(specularChannel="R" if spec == 0 else "B", roughnessChannel="G",
                          specularByteRange=result["rgbaExtrema"][spec], roughnessByteRange=result["rgbaExtrema"][1],
                          roughnessIsForwardExponentControl=True, physicalMetalnessAuthored=False)
        elif channel == "emissive":
            result.update(hdrEnergyPreserved=False, factorLinear=meaning["emissiveFactor"],
                          effectiveEmissionDisabled=not any(meaning["emissiveFactor"]))
        return result


def texture_use(doc, blob, info, meaning, stats_cache):
    texture = doc["textures"][info["index"]]
    if texture.get("extensions") or "source" not in texture:
        raise ValueError("Desktop profiles require embedded uncompressed Source textures")
    payload = image_bytes(doc, blob, texture["source"])
    sampler = doc.get("samplers", [])[texture["sampler"]] if "sampler" in texture else {}
    state = {k: sampler.get(k, 10497) for k in ["wrapS", "wrapT"]}
    state.update({k: sampler[k] for k in ["minFilter", "magFilter"] if k in sampler})
    key = (digest(payload), canonical(meaning))
    if key not in stats_cache:
        stats_cache[key] = image_stats(payload, meaning)
    return {"imageSha256": key[0], "sampler": state, "meaning": meaning, "stats": stats_cache[key]}


def snapshot(root, policy):
    root = Path(root).resolve()
    if root.is_symlink():
        raise ValueError("Pack root must not be a symlink")
    manifest_path = contained(root, "characters.json")
    manifest_hash = sha(manifest_path)
    manifest = json.loads(manifest_path.read_text())
    rows, preserved, files, cache = {}, {}, {}, {}
    for entry in manifest["models"]:
        key = f"{entry['hunter']}/{entry['part']}/lod{entry.get('lod', 0)}"
        if key in rows or key in preserved:
            raise ValueError("Duplicate profile asset identity")
        path = contained(root, entry["model"])
        files[str(path)] = sha(path)
        if entry["hunter"] in policy["preserveHunters"]:
            references = {name: sha(contained(root, entry[name])) for name in ["model", "mobileModel"] if name in entry}
            preserved[key] = {"entry": entry, "references": references}
            continue
        if entry["hunter"] not in policy["hunters"]:
            raise ValueError("Unknown material profile hunter")
        doc, blob = load(path)
        materials = []
        for index, material in enumerate(doc.get("materials", [])):
            pbr = material.get("pbrMetallicRoughness", {})
            if pbr.get("baseColorTexture") and pbr.get("baseColorFactor", [1, 1, 1, 1]) != [1, 1, 1, 1]:
                raise ValueError(f"Runtime ignores nonidentity baseColorFactor: {key}/{material['name']}")
            if material.get("alphaMode", "OPAQUE") not in ["OPAQUE", "BLEND"]:
                raise ValueError("Character runtime does not implement a faithful MASK cutoff")
            uses = [texture_use(doc, blob, info, meaning, cache) for info, meaning in usages(material)]
            albedo = next((u for u in uses if u["meaning"]["channel"] == "albedo"), None)
            if albedo and any({k: u["sampler"][k] for k in ["wrapS", "wrapT"]} !=
                              {k: albedo["sampler"][k] for k in ["wrapS", "wrapT"]}
                              for u in uses if u["meaning"]["channel"] != "albedo"):
                raise ValueError("Companion map wrapping differs from albedo")
            if any(u["stats"].get("effectiveEmissionDisabled") for u in uses):
                raise ValueError("An emissive texture is silently disabled by its zero factor")
            materials.append({"index": index, "nativeIdentity": material.get("name"),
                "sourceAlphaIdentity": material.get("extras", {}).get("projectPrimeSourceMaterial"),
                "alphaMode": material.get("alphaMode", "OPAQUE"), "doubleSided": material.get("doubleSided", False),
                "uses": uses, "recolorSuits": sorted(material.get("extras", {}).get("projectPrimeRecolors", {}))})
        rows[key] = {"modelSha256": files[str(path)], "skinning": entry.get("skinning", "rigidNodes"),
                     "boneMap": entry["boneMap"], "nativeSupplementMaterials": entry.get("nativeSupplementMaterials", []),
                     "materials": materials}
    peers = {}
    def signatures(row):
        return collections.Counter(canonical({k: v for k, v in m.items() if k not in ["index", "nativeIdentity"]}) for m in row["materials"])
    for hunter in policy["hunters"]:
        low, high = (f"{hunter}/biped/lod{i}" for i in [0, 1])
        if low in rows and high in rows:
            matches = signatures(rows[low]) == signatures(rows[high])
            peers[hunter] = {"imagePayloadsFactorsAlphaAndSamplerPeers": matches,
                             "nativeMaterialNamesMayDiffer": True}
            if policy["requireExactLodImagePeers"] and not matches:
                raise ValueError(f"{hunter} LOD0/LOD1 material peers differ")
    # Detect files changed during a lengthy image decode without rewriting them.
    if any(sha(p) != h for p, h in files.items()) or sha(manifest_path) != manifest_hash:
        raise ValueError("Pack files changed during material profiling")
    return {"desktopAssets": rows, "preservedAssets": preserved, "lodPeers": peers,
            "uniqueSemanticImagePayloads": len(cache)}


def evidence(roots):
    records, files, limitations = {}, {}, collections.defaultdict(set)
    for root in roots:
        paths = [root] if root.is_file() else sorted(root.rglob("SOURCE-MATERIALS.json"))
        for path in paths:
            report = json.loads(path.read_text()); files[str(path.resolve())] = sha(path)
            if report.get("normalImagesChanged") or report.get("uvsChanged") or report.get("topologyChanged"):
                raise ValueError("Source material evidence changed normals, UVs or topology")
            for name, m in report["materials"].items():
                identity = f"{name}/{m.get('vmtSha256', 'missing')}"
                record = {k: m.get(k) for k in ["sourceMaterial", "vmt", "vmtSha256", "parameters", "detailBaked",
                    "detailSha256", "detailScale", "outputSize", "densityBoundedToLimit", "selfIllum", "selfIllumTint",
                    "hdrSelfIllumTintBounded", "phongMaterialBaked", "sourcePhongExponent", "unboundedPhongStrength",
                    "boundedPhongStrength", "phongStrengthClamped", "forwardRoughness", "forwardRoughnessClamped",
                    "phongMaskSampleRange", "phongApproximation", "normalPixelSha256"]}
                previous = records.get(identity)
                if previous and previous.get("phongMaterialBaked") and not record.get("phongMaterialBaked"):
                    continue  # Prefer the reviewed Phong-inclusive restoration.
                records[identity] = record
                for key, label in [("densityBoundedToLimit", "Source detail sampling density bounded"),
                    ("phongStrengthClamped", "Source Phong scalar strength clipped to unit range"),
                    ("forwardRoughnessClamped", "Source exponent outside supported forward range"),
                    ("hdrSelfIllumTintBounded", "HDR selfillum tint hue retained but energy bounded")]:
                    if m.get(key): limitations[label].add(identity)
                if m.get("phongMaskSampleRange") and m["phongMaskSampleRange"][0] == m["phongMaskSampleRange"][1]:
                    limitations["Source Phong mask constant; region polish remains authored work"].add(identity)
                for key, expected in [("vmt", "vmtSha256"), ("detail", "detailSha256"), ("phongMaskSource", "phongMaskSourceSha256")]:
                    if m.get(key):
                        source = Path(m[key]); current = sha(source)
                        if current != m.get(expected):
                            raise ValueError(f"Original material evidence changed: {source}")
                        files[str(source.resolve())] = current
    return {"materials": records, "files": files,
            "limitations": {k: sorted(v) for k, v in limitations.items()}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["capture", "audit"])
    parser.add_argument("--source-pack", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--policy", type=Path, default=POLICY)
    parser.add_argument("--profile", type=Path)
    parser.add_argument("--evidence-root", action="append", type=Path, default=[])
    args = parser.parse_args()
    if args.output.exists() or args.output.resolve().is_relative_to(args.source_pack.resolve()):
        raise ValueError("Write a fresh material profile/receipt outside the source pack")
    policy = json.loads(args.policy.read_text())
    if policy["format"] != 1:
        raise ValueError("Unsupported material policy format")
    code = {p: sha(REPO / p) for p in CODE}
    actual = snapshot(args.source_pack, policy)
    if args.command == "capture":
        result = {"format": 1, "profileId": policy["id"], "policy": policy, "policySha256": sha(args.policy),
                  "codeSha256": code, "sourcePack": str(args.source_pack.resolve()),
                  "materialBaseline": actual, "sourceEvidence": evidence(args.evidence_root),
                  "pass": True, "scope": policy["scope"]}
    else:
        if args.profile is None:
            raise ValueError("audit requires --profile")
        profile = json.loads(args.profile.read_text())
        if profile["policy"] != policy or profile["policySha256"] != sha(args.policy) or profile["codeSha256"] != code:
            raise ValueError("Material policy/renderer interpretation changed since the captured profile")
        if profile["materialBaseline"] != actual:
            raise ValueError("Desktop artwork, companion maps, factors, sampler, alpha, native bindings or Samus baseline differ")
        for path, expected in profile["sourceEvidence"]["files"].items():
            if sha(path) != expected:
                raise ValueError(f"Source material evidence changed: {path}")
        result = {"pass": True, "profileSha256": sha(args.profile), "policySha256": sha(args.policy),
                  "desktopAssets": len(actual["desktopAssets"]), "preservedSamusAssets": len(actual["preservedAssets"]),
                  "exactArtworkMapsFactorsAlphaSamplersAndNativeBindings": True, "lodPeers": actual["lodPeers"],
                  "scope": policy["scope"]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    if {p: sha(REPO / p) for p in CODE} != code:
        raise ValueError("Material renderer/converter code changed during the profile operation")
    with args.output.open("x") as stream:
        stream.write(json.dumps(result, indent=2) + "\n")
    print("SOURCE_MATERIAL_PROFILE_PASS", args.command, args.output)


if __name__ == "__main__":
    main()
