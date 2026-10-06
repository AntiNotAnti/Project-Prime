"""Extract immutable PBR authoring inputs and exact shipping atlas ownership.

This tool does not repaint, rescale geometry, rewrite GLBs or install a pack.
It groups shared Source texture tiles, locks all shipping image semantics and
exports source masks/normals plus accepted artwork for constrained authoring.
"""
from __future__ import annotations

import argparse
import collections
import hashlib
import io
import json
import math
import re
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from glb import image_bytes, load, read
from source_materials import SourceMaterialLibrary

REPO = Path(__file__).resolve().parents[2]
HUNTERS = ("Samus", "Noxus", "Kanden", "Sylux", "Trace", "Weavel", "Spire")
CHANNELS = {"albedo": ("pbrMetallicRoughness", "baseColorTexture"),
            "material": ("pbrMetallicRoughness", "metallicRoughnessTexture"),
            "normal": (None, "normalTexture"), "emissive": (None, "emissiveTexture")}


def digest(value):
    return hashlib.sha256(value).hexdigest()


def sha(path):
    return digest(Path(path).read_bytes())


def slug(value):
    return re.sub(r"[^A-Za-z0-9_.-]+", "_", value)


def image_record(image):
    image = image.convert("RGBA")
    return {"size": list(image.size), "pixelSha256": digest(image.tobytes()),
            "rgbaExtrema": [list(pair) for pair in image.getextrema()]}


def texture_record(doc, blob, info, output, cache):
    if info is None:
        return None
    texture = doc["textures"][info["index"]]
    if "source" not in texture or texture.get("extensions"):
        raise ValueError("Inventory requires plain embedded desktop textures")
    image_index = texture["source"]
    payload = image_bytes(doc, blob, image_index)
    key = digest(payload)
    if key not in cache:
        image = Image.open(io.BytesIO(payload)).convert("RGBA")
        path = output / "embedded" / (key + ".png")
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(payload)
        cache[key] = {"payloadSha256": key, "path": str(path), **image_record(image)}
    sampler = doc.get("samplers", [])[texture["sampler"]] if "sampler" in texture else {}
    return {"imageIndex": image_index, "textureIndex": info["index"],
            "textureInfo": info, "sampler": sampler, **cache[key]}


def group_signature(layout_path, layout, native, group):
    result = []
    for channel in ("albedo", "normal"):
        path = layout_path.parent / "textures" / f"{native}-{channel}.png"
        result.append(sha(path) if path.is_file() else None)
    return tuple(result)


def source_registry(root, output, locked_files):
    records, by_sha = collections.defaultdict(list), {}
    for hunter in HUNTERS:
        for source in sorted((root / "models" / hunter.lower()).glob("*.glb")):
            source_sha = sha(source); locked_files[str(source)] = source_sha
            doc, blob = load(source); by_sha[source_sha] = source
            for index, material in enumerate(doc.get("materials", [])):
                key = (hunter, material["name"])
                channels = {}
                for channel, (parent, field) in CHANNELS.items():
                    info = material.get(parent, {}).get(field) if parent else material.get(field)
                    if not info:
                        continue
                    payload = image_bytes(doc, blob, doc["textures"][info["index"]]["source"])
                    image = Image.open(io.BytesIO(payload)).convert("RGBA")
                    channels[channel] = {"payloadSha256": digest(payload), **image_record(image), "payload": payload}
                if "albedo" not in channels:
                    continue
                identity = channels["albedo"]["pixelSha256"]
                same = next((row for row in records[key] if row["channels"]["albedo"]["pixelSha256"] == identity), None)
                if same:
                    same["sourceReferences"].append({"path": str(source), "sha256": source_sha, "materialIndex": index})
                else:
                    records[key].append({"hunter": hunter, "sourceMaterial": material["name"], "channels": channels,
                        "sourceReferences": [{"path": str(source), "sha256": source_sha, "materialIndex": index}],
                        "sourceMaterialJson": material})
    return records, by_sha


def atlas_index(paths, preferred, locked_files):
    index = collections.defaultdict(list)
    for path in sorted(set(paths)):
        layout = json.loads(path.read_text()); locked_files[str(path)] = sha(path)
        report = path.parent / "SOURCE-MATERIALS.json"
        if report.is_file():
            locked_files[str(report)] = sha(report)
        for native, group in layout.get("groups", {}).items():
            signature = group_signature(path, layout, native, group)
            if signature[0] is None:
                continue
            sources = []
            for name in group["sources"]:
                spec = layout["materials"][name]
                sources.append({"sourceMaterial": name, **spec})
            index[signature].append({"layoutPath": str(path), "layoutSha256": sha(path),
                "sourceSha256": layout["sourceSha256"], "paddingPixels": layout["paddingPixels"],
                "atlasSize": group["size"], "nativeIdentity": native,
                "directPeriodicTexture": bool(group.get("directPeriodicTexture")), "sources": sources,
                "sourceMaterialEvidencePath": str(report) if report.is_file() else None,
                "preferredAcceptedLayout": str(path) in preferred})
    return index


def ownership_group(signature, atlas, hunter, material):
    source = material.get("extras", {}).get("sourceMaterial")
    if source:
        return {"directPeriodicTexture": True, "nativeIdentity": material["name"],
                "sources": [{"sourceMaterial": source}], "authority": "shipping material extras.sourceMaterial"}
    candidates = atlas.get(signature, [])
    if not candidates:
        return None
    exact_native = [row for row in candidates if row["nativeIdentity"] == material["name"]]
    if not exact_native:
        lod_config = REPO / "sourceio-hd" / hunter.lower() / "lod1-config.json"
        aliases = json.loads(lod_config.read_text()).get("materialNames", {}) if lod_config.is_file() else {}
        previous = {old for old,new in aliases.items() if new == material["name"]}
        exact_native = [row for row in candidates if row["nativeIdentity"] in previous]
    candidates = exact_native or candidates
    preferred = [row for row in candidates if row["preferredAcceptedLayout"]]
    candidates = preferred or candidates
    def meaning(row):
        # Direct periodic images are shared across source meshes with different
        # UV extents. Those extents do not change texel ownership. Source-local
        # material indexes also differ across A/B GLBs and are not atlas meaning.
        fields = ("sourceMaterial",) if row["directPeriodicTexture"] else (
            "sourceMaterial", "tileSize", "pixelSize", "offsetPixels", "atlasUVScale", "atlasUVOffset")
        return json.dumps({"sources":[{key:source[key] for key in fields if key in source} for source in row["sources"]],
            **{key:row[key] for key in ("directPeriodicTexture", "atlasSize", "paddingPixels")}}, sort_keys=True)
    if len(set(meaning(row) for row in candidates)) != 1:
        raise ValueError(f"Ambiguous exact atlas ownership for {hunter}/{material['name']}")
    return dict(candidates[0], equivalentEvidencePaths=[row["layoutPath"] for row in candidates])


def ownership_mask(group, size, output, key):
    width, height = size
    image = Image.new("I;16", (width, height), 0)
    pixels = np.zeros((height, width), np.uint16); labels = {"0": "unused atlas background"}
    for label, source in enumerate(group["sources"], 1):
        labels[str(label)] = source["sourceMaterial"]
        if group["directPeriodicTexture"]:
            x = y = 0; w = width; h = height
        else:
            pad = group["paddingPixels"]; px, py = source["offsetPixels"]; w, h = source["pixelSize"]
            x, y, w, h = px - pad, py - pad, w + pad * 2, h + pad * 2
        if x < 0 or y < 0 or x+w > width or y+h > height:
            raise ValueError("Source atlas owner rectangle leaves the image")
        if np.any(pixels[y:y+h, x:x+w]):
            raise ValueError("Source atlas ownership rectangles overlap")
        pixels[y:y+h, x:x+w] = label
        source["ownedRectPixelsIncludingPadding"] = [x, y, w, h]
    path = output / "ownership" / (key + ".png"); path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(pixels).save(path)
    return {"path": str(path), "sha256": sha(path), "labels": labels,
            "zeroTexels": int((pixels == 0).sum()), "coordinates": "PNG top-left; atlas region including periodic padding"}


def accessor_hash(doc, blob, index):
    accessor = doc["accessors"][index]
    return digest(json.dumps(read(doc, blob, index), separators=(",", ":")).encode())


def periodic_uv_occupancy(source_path, material_name, size, output):
    """All original Source bodygroups, not a shipping geometry selection mask."""
    doc, blob = load(source_path); image = Image.new("L", size, 0); draw = ImageDraw.Draw(image)
    triangles = zero_area = 0; extent = [math.inf, math.inf, -math.inf, -math.inf]
    for mesh in doc["meshes"]:
        for primitive in mesh["primitives"]:
            if doc["materials"][primitive["material"]]["name"] != material_name:
                continue
            uv = np.asarray(read(doc, blob, primitive["attributes"]["TEXCOORD_0"]), dtype=np.float64)
            indices = np.asarray(read(doc, blob, primitive["indices"]), dtype=np.int64).reshape(-1, 3)
            for tri in indices:
                points = uv[tri]; low = points.min(axis=0); high = points.max(axis=0)
                extent = [min(extent[0],low[0]), min(extent[1],low[1]), max(extent[2],high[0]),max(extent[3],high[1])]
                area = np.linalg.det(np.stack((points[1]-points[0],points[2]-points[0])))
                triangles += 1
                if abs(area) < 1e-12:
                    zero_area += 1; continue
                points -= np.floor(low)
                high = points.max(axis=0)
                if high.max() > 16:
                    raise ValueError("Source triangle has an unusually large periodic UV span")
                for x in range(-math.ceil(high[0]), 1):
                    for y in range(-math.ceil(high[1]), 1):
                        shift = points + (x,y)
                        draw.polygon([(float(u*size[0]),float(v*size[1])) for u,v in shift], fill=255)
    path = output / "source-uv-occupancy.png"; image.save(path)
    return {"path": str(path), "sha256": sha(path), "triangles": triangles,
            "zeroUvAreaTriangles": zero_area, "originalUvExtent": extent,
            "coverage": float(np.count_nonzero(np.asarray(image)) / (size[0]*size[1])),
            "scope": "Original Source GLB all bodygroups periodic UV0 occupancy; a source authoring guide, not a shipping mesh-selection audit",
            "orientation": "glTF texture UV0; PNG top-left; integer UV repeats folded without changing island position"}


def extract_target(target, registry, converted, material_root, output, locked_files):
    hunter, name = target["hunter"], target["sourceMaterial"]
    originals = registry.get((hunter,name), [])
    if len(originals) != 1:
        raise ValueError(f"Expected one unique original Source base for {hunter}/{name}, got {len(originals)}")
    original = originals[0]; folder = output / "authoring" / hunter.lower() / slug(name)
    folder.mkdir(parents=True, exist_ok=True)
    channels = {}
    for channel, row in original["channels"].items():
        path = folder / f"source-{channel}.png"; path.write_bytes(row["payload"])
        channels[channel] = {key: value for key,value in row.items() if key != "payload"}
        channels[channel]["path"] = str(path)
    library = SourceMaterialLibrary(material_root, converted / "decoded-vtf", hunter)
    spec = library.spec(name)
    base = Image.open(folder / "source-albedo.png").convert("RGBA")
    normal = Image.open(folder / "source-normal.png").convert("RGBA") if "normal" in channels else None
    authored = library.albedo(original["sourceMaterialJson"], base, normal)
    authored_path = folder / "source-authored-albedo.png"; authored.save(authored_path)
    source_mask = np.asarray(base)[...,3]
    Image.fromarray(source_mask).save(folder / "source-base-alpha-mask.png")
    if normal is not None:
        Image.fromarray(np.asarray(normal)[...,3]).save(folder / "source-normal-alpha-mask.png")
    for field,hash_field in (("vmt","vmtSha256"),("detail","detailSha256")):
        if spec.get(field):
            locked_files[spec[field]] = spec[hash_field]
    actual_art = None
    if hunter == "Samus":
        art_root = REPO / "samus-hd-kit/sourceio-materials"
        path = art_root / "art-source" / (name + "-repaint.png")
        if path.is_file():
            actual_art = {"generatedBitmap": str(path), "sha256": sha(path),
                "promptFile": str(art_root / "art-source/PROMPTS.json"),
                "promptFileSha256": sha(art_root / "art-source/PROMPTS.json"),
                "knownGoodTextureBuild": str(art_root / "texture-build.json"),
                "knownGoodTextureBuildSha256": sha(art_root / "texture-build.json")}
            locked_files[str(path)] = sha(path)
            locked_files[actual_art["promptFile"]] = actual_art["promptFileSha256"]
        accepted = art_root / "textures" / (name+"-albedo.png")
        if accepted.is_file():
            actual_art = actual_art or {}
            actual_art.update(acceptedArtworkPath=str(accepted), acceptedArtworkSha256=sha(accepted))
            locked_files[str(accepted)] = sha(accepted)
            jpeg = accepted.with_suffix(".jpg")
            if jpeg.is_file():
                actual_art.update(acceptedJpegArtworkPath=str(jpeg),acceptedJpegArtworkSha256=sha(jpeg),
                    sourceTextureBuildScript=str(art_root/"build-textures.py"),
                    sourceTextureBuildScriptSha256=sha(art_root/"build-textures.py"),
                    sourceGlbWriterScript=str(art_root/"glb.py"),sourceGlbWriterScriptSha256=sha(art_root/"glb.py"),
                    jpegEncoding="Pillow JPEG quality94/subsampling0/optimizeTrue from accepted PNG paint")
                locked_files[str(jpeg)] = sha(jpeg)
                for script in (art_root/"build-textures.py",art_root/"glb.py"):
                    locked_files[str(script)] = sha(script)
    current_tiles = {}
    for use in target["shippingUses"]:
        atlas_image = Image.open(use["channels"]["albedo"]["path"]).convert("RGBA")
        group = use["atlasGroup"]
        source = next(row for row in group["sources"] if row["sourceMaterial"] == name)
        if group["directPeriodicTexture"]:
            tile = atlas_image
        else:
            px,py = source["offsetPixels"]; tw,th = source["tileSize"]
            tile = atlas_image.crop((px,py,px+tw,py+th))
        key = digest(tile.tobytes())
        if key not in current_tiles:
            path = folder / ("shipping-authored-albedo-" + key[:12] + ".png"); tile.save(path)
            current_tiles[key] = {"path":str(path),"sha256":sha(path),**image_record(tile),"uses":[]}
        current_tiles[key]["uses"].append({"model":use["model"],"materialIndex":use["materialIndex"]})
    if actual_art and actual_art.get("acceptedArtworkPath"):
        old = Image.open(actual_art["acceptedArtworkPath"]).convert("RGBA")
        actual_art["allShippingUsesExactlyMatchKnownGoodArtwork"] = (
            len(current_tiles) == 1 and next(iter(current_tiles.values()))["pixelSha256"] == digest(old.tobytes()))
        if actual_art.get("acceptedJpegArtworkSha256"):
            actual_art["allShippingUsesExactlyMatchKnownGoodJpeg"] = all(
                use["channels"]["albedo"]["payloadSha256"] == actual_art["acceptedJpegArtworkSha256"] for use in target["shippingUses"])
    occupancy = periodic_uv_occupancy(Path(original["sourceReferences"][0]["path"]),name,base.size,folder)
    selfillum = bool(spec.get("selfIllum"))
    return {**target, "folder": str(folder), "sourceReferences": original["sourceReferences"],
            "originalChannels": channels, "sourceAuthoredAlbedo": {"path": str(authored_path), **image_record(authored), "sha256": sha(authored_path)},
            "shippingAuthoredAlbedoTiles": current_tiles,
            "sourceMaterialContract": spec, "sourceUvOccupancy": occupancy, "priorGeneratedSamusArt": actual_art,
            "protectedMasks": {path.name: {"path": str(path), "sha256": sha(path)} for path in folder.glob("*alpha-mask.png")},
            "repaintGuidance": {"selfIllum": selfillum, "protectOriginalAlphaExactly": True,
                "preserveEmissiveSupport": selfillum, "retainOriginalNormalsForRefinement": normal is not None,
                "paintedLightingAssessment": "Original color contains authored surface/cavity/highlight artwork; no automatic de-lighting claim. Review each sheet and model against source before selective removal.",
                "priority": "constrained glow/alpha polish" if selfillum or "hair" in name.lower() else "surface color de-lighting/repaint with UV islands and markings locked"}}


def inventory(pack, converted, output):
    pack, converted, output = Path(pack).resolve(), Path(converted).resolve(), Path(output).resolve()
    if output.exists() or output.is_relative_to(pack):
        raise ValueError("Inventory output must be fresh and separate from the pack")
    output.mkdir(parents=True)
    locked_files = {str(pack / "characters.json"): sha(pack / "characters.json")}
    manifest = json.loads((pack / "characters.json").read_text())
    material_input = REPO / "artifacts/sourceio-hd/pass-b-alternates-v1/roster-v6-material-input.json"
    preferred = set()
    if material_input.is_file():
        for row in json.loads(material_input.read_text())["assets"].values():
            preferred.update(group["layoutPath"] for group in row["materials"].values())
    atlas_paths = list((REPO / "artifacts/sourceio-hd").rglob("atlas-layout.json"))
    atlas = atlas_index(atlas_paths,preferred,locked_files)
    registry, source_by_sha = source_registry(converted,output,locked_files)
    assets, cache, targets = [], {}, {}
    for entry in manifest["models"]:
        path = pack / entry["model"]; model_sha = sha(path); locked_files[str(path)] = model_sha
        doc, blob = load(path); materials = []
        for index, material in enumerate(doc.get("materials", [])):
            channels = {}
            for channel,(parent,field) in CHANNELS.items():
                info = material.get(parent,{}).get(field) if parent else material.get(field)
                row = texture_record(doc,blob,info,output,cache)
                if row:
                    channels[channel] = row
            recolors = {suit:texture_record(doc,blob,info,output,cache)
                for suit,info in material.get("extras",{}).get("projectPrimeRecolors",{}).items()}
            signature = tuple(channels.get(channel,{}).get("payloadSha256") for channel in ("albedo","normal"))
            group = ownership_group(signature,atlas,entry["hunter"],material) if "albedo" in channels else None
            if "albedo" in channels and group is None:
                raise ValueError(f"Missing exact Source ownership: {entry['model']}/{material['name']}")
            if group:
                size = channels["albedo"]["size"]
                group["albedoOwnershipMask"] = ownership_mask(group,size,output,signature[0])
                for source in group["sources"]:
                    name = source["sourceMaterial"]; key = entry["hunter"] + "/" + name
                    target = targets.setdefault(key,{"hunter":entry["hunter"],"sourceMaterial":name,"shippingUses":[]})
                    target["shippingUses"].append({"model":entry["model"],"materialIndex":index,
                        "nativeIdentity":material["name"],"atlasGroup":group,"channels":channels,
                        "recolorSuits":sorted(recolors)})
            materials.append({"index":index,"nativeIdentity":material["name"],"materialJson":material,
                "channels":channels,"recolors":recolors,"sourceOwnership":group,
                "nativeEffectPreserved":not bool(channels)})
        assets.append({"hunter":entry["hunter"],"part":entry["part"],"lod":entry.get("lod",0),
            "model":entry["model"],"modelPath":str(path),"modelSha256":model_sha,
            "entry":entry,"geometryAccessorSha256":{str(i):accessor_hash(doc,blob,i) for i in range(len(doc.get("accessors",[])))},
            "embeddedImagePayloadSha256":[digest(image_bytes(doc,blob,i)) for i in range(len(doc.get("images",[])))],
            "documentSha256":digest(json.dumps(doc,sort_keys=True,separators=(",",":")).encode()),
            "materials":materials})
    authoring = {}
    for key,target in sorted(targets.items()):
        authoring[key] = extract_target(target,registry,converted,converted.parent / "gmpublisher/materials",output,locked_files)
    if any(sha(path) != expected for path,expected in locked_files.items()):
        raise ValueError("Inputs changed during read-only inventory extraction")
    result = {"format":1,"pass":True,"toolPath":str(Path(__file__).resolve()),"toolSha256":sha(__file__),
        "packId":manifest["id"],"sourcePack":str(pack),"manifestSha256":sha(pack/"characters.json"),
        "assets":assets,"authoringTargets":authoring,"assetCount":len(assets),
        "uniqueSourceAuthoringTargets":len(authoring),"uniqueShippingImagePayloads":len(cache),"lockedFiles":locked_files,
        "channelSemantics":{"albedo":"sRGB, original alpha unchanged", "normal":"linear tangent normals with original alpha retained separately",
            "material":"Current Project Prime desktop maps G=forward roughness/B=bounded scalar specular; not physical metallic yet",
            "emissive":"sRGB source color/mask; factor linear/native gameplay lighting authoritative"},
        "scope":"Read-only Source/shipping texture and UV/atlas ownership inventory. Extracted inputs are not a repaint or visual/GPU acceptance."}
    (output/"INVENTORY.json").write_text(json.dumps(result,indent=2)+"\n")
    compact = {"assetCount":len(assets),"uniqueSourceAuthoringTargets":len(authoring),"packId":manifest["id"],
        "targets":{key:{"sourceSize":row["originalChannels"]["albedo"]["size"],
            "authoredSize":row["sourceAuthoredAlbedo"]["size"],"repaintInput":row["sourceAuthoredAlbedo"]["path"],
            "selfIllum":row["sourceMaterialContract"].get("selfIllum",False),"uses":len(row["shippingUses"]),
            "priorSamusArt":row["priorGeneratedSamusArt"]} for key,row in authoring.items()}}
    (output/"AUTHORING-TARGETS.json").write_text(json.dumps(compact,indent=2)+"\n")
    print(json.dumps({"pass":True,"assets":len(assets),"sourceTargets":len(authoring),"shippingImages":len(cache),"output":str(output)}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-pack",required=True,type=Path)
    parser.add_argument("--converted-source",required=True,type=Path)
    parser.add_argument("--output",required=True,type=Path)
    args = parser.parse_args()
    inventory(args.source_pack,args.converted_source,args.output)


if __name__ == "__main__":
    main()
