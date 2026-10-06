"""Build a fresh 29-entry PBR repaint pack from locked authoring inputs.

All original geometry/UV/bind data and embedded image bytes remain in the BIN
prefix. Registered art and data maps are appended with declared texture and
material patches. The result requires independent and GPU acceptance.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import io
import json
import shutil
import struct
from pathlib import Path

import numpy as np
from PIL import Image

from glb import image_bytes, load
from pbr_maps import build_tiles, rgba, save, sha, PROFILES


def digest(value):
    return hashlib.sha256(value).hexdigest()


def payload(array):
    output = io.BytesIO(); Image.fromarray(array).save(output,format="PNG")
    return output.getvalue()


def write_glb(path, doc, blob):
    doc["buffers"][0]["byteLength"] = len(blob)
    encoded = json.dumps(doc,separators=(",",":")).encode(); encoded += b" "*(-len(encoded)%4)
    binary = bytes(blob)+b"\0"*(-len(blob)%4)
    path.write_bytes(struct.pack("<4sII",b"glTF",2,28+len(encoded)+len(binary))
        +struct.pack("<I4s",len(encoded),b"JSON")+encoded+struct.pack("<I4s",len(binary),b"BIN\0")+binary)


def source_image(doc, blob, info):
    index = doc["textures"][info["index"]]["source"]
    return index,image_bytes(doc,blob,index)


def values(path,size,normal=False):
    if str(path).endswith(".npy"):
        original = np.load(path)
        return np.stack([np.asarray(Image.fromarray(original[...,i]).resize(tuple(size),Image.Resampling.BILINEAR))
                         for i in range(original.shape[2])],axis=2)
    # rgba() filters each independent channel. In particular ORM A=0 and
    # original Source normal-alpha/gloss masks must never premultiply RGB.
    return rgba(path,size,Image.Resampling.NEAREST if normal else Image.Resampling.LANCZOS)


def apply_region(destination, group, source, tile):
    if group["directPeriodicTexture"]:
        destination[...] = tile
        return
    x,y = source["offsetPixels"]; w,h = source["pixelSize"]; tw,th = source["tileSize"]; pad = group["paddingPixels"]
    xs = np.arange(-pad,w+pad)%tw; ys = np.arange(-pad,h+pad)%th
    destination[y-pad:y+h+pad,x-pad:x+w+pad] = tile[ys[:,None],xs[None,:]]


def tile_size(group,source,array):
    return list(array.shape[1::-1]) if group["directPeriodicTexture"] else source["tileSize"]


def recipe_proof(tiles, keys, recipe, reused=False, output_sha=None):
    selected = [tiles[key] for key in keys]
    proof = {"recipePath":str(recipe),"recipeSha256":sha(recipe),
        "semanticMaterials":sorted({surface for tile in selected for surface in tile["semanticMaterials"]}),
        "generatedImages":[value for tile in selected for value in tile["generatedImages"]]}
    if reused:
        prior = next(tile["priorGeneratedSamusArt"] for tile in selected if tile["operation"] == "reusedAuthoredPaint")
        proof.update(authoringReceiptPath=prior["knownGoodTextureBuild"],
                     authoringReceiptSha256=prior["knownGoodTextureBuildSha256"],authoredOutputSha256=output_sha)
    return proof


def compose_material(asset, material, tiles, folder):
    group = material["sourceOwnership"]; old = rgba(material["channels"]["albedo"]["path"])
    color = old.copy(); protected = np.zeros(old.shape[:2],np.uint8)
    orm = np.empty_like(old); orm[...] = [255,180,0,0]
    micro = np.zeros((*old.shape[:2],2),np.float32)
    keys = []
    for source in group["sources"]:
        key = asset["hunter"]+"/"+source["sourceMaterial"]; tile = tiles[key]; keys.append(key)
        size = tile_size(group,source,old)
        # Preserve glow, hair and flat black color/alpha exactly at existing
        # texel density; those regions receive only authored material response.
        if tile["operation"] != "preserve":
            apply_region(color,group,source,values(tile["albedo"]["path"],size))
            mask = values(tile["protectedMask"]["path"],size,True)[...,0]
            apply_region(protected,group,source,mask)
            apply_region(micro,group,source,values(tile["microXYPath"],size))
        else:
            apply_region(protected,group,source,np.full((size[1],size[0]),255,np.uint8))
        apply_region(orm,group,source,values(tile["orm"]["path"],size))
    # Region resampling may round alpha; draw alpha is an exact native/source
    # contract and does not inherit generated artistic transparency.
    color[...,3] = old[...,3]
    color[protected != 0] = old[protected != 0]
    outputs = {"albedo":save(color,folder/"albedo.png"),"orm":save(orm,folder/"orm.png"),
               "protectedMask":save(protected,folder/"protected.png")}
    if "normal" in material["channels"]:
        normal = rgba(material["channels"]["normal"]["path"])
        # Normal and albedo resolutions may differ for direct periodic images.
        # Sample the finish field in the identical normalized UV domain.
        refined = np.stack([np.asarray(Image.fromarray(micro[...,i]).resize(tuple(normal.shape[1::-1]),Image.Resampling.BILINEAR))
                            for i in range(2)],axis=2)
        original = normal[...,:3].astype(np.float32)/127.5-1
        base_direction = original / np.maximum(np.linalg.norm(original,axis=2,keepdims=True),1e-8)
        result = base_direction.copy(); result[...,:2] += refined
        result /= np.maximum(np.linalg.norm(result,axis=2,keepdims=True),1e-8)
        after = normal.copy(); after[...,:3] = np.rint(np.clip(result*.5+.5,0,1)*255).astype(np.uint8)
        # Unowned atlas background is preserved exactly; maps use same extent
        # for packed groups, direct groups own the entire periodic image.
        owner = np.asarray(Image.open(group["albedoOwnershipMask"]["path"]).resize(tuple(normal.shape[1::-1]),Image.Resampling.NEAREST)) != 0
        after[~owner] = normal[~owner]
        if not any(tiles[key]["operation"] != "preserve" for key in keys):
            after = normal
        outputs["normal"] = save(after,folder/"normal.png")
        before_direction = base_direction
        after_direction = after[...,:3].astype(float)/127.5-1
        after_direction /= np.maximum(np.linalg.norm(after_direction,axis=2,keepdims=True),1e-8)
        angles = np.degrees(np.arccos(np.clip(np.sum(before_direction*after_direction,axis=2),-1,1)))
        outputs["normal"]["refinementMetrics"] = {"changedTexelFraction":float(np.any(normal != after,axis=2)[owner].mean()),
            "meanAngularChangeDegrees":float(angles[owner].mean()),"p99AngularChangeDegrees":float(np.percentile(angles[owner],99)),
            "maximumAngularChangeDegrees":float(angles[owner].max()),
            "maximumAddedXYNorm":float(np.linalg.norm(refined,axis=2)[owner].max()),"sourceAlphaPreserved":bool(np.array_equal(normal[...,3],after[...,3]))}
    recolors = {}
    luminance_old = old[...,:3].astype(float) @ np.array([.2126,.7152,.0722])
    luminance_new = color[...,:3].astype(float) @ np.array([.2126,.7152,.0722])
    ratio = np.clip(np.divide(luminance_new,np.maximum(luminance_old,8)),.65,1.45)
    ratio[protected != 0] = 1
    for suit,record in material["recolors"].items():
        variant = rgba(record["path"])
        if variant.shape[:2] != old.shape[:2]:
            raise ValueError("Native recolor extent differs from neutral Source artwork")
        factor = np.minimum(ratio,255/np.maximum(variant[...,:3].max(axis=2),1))
        new_variant = variant.copy(); new_variant[...,:3] = np.rint(variant[...,:3]*factor[...,None]).astype(np.uint8)
        new_variant[protected != 0] = variant[protected != 0]
        recolors[suit] = save(new_variant,folder/("recolor-"+suit+".png"))
    recipe = {"format":1,"hunter":asset["hunter"],"model":asset["model"],"nativeIdentity":material["nativeIdentity"],
        "tileRecipes":[{"path":tiles[key]["recipePath"],"sha256":tiles[key]["recipeSha256"]} for key in keys],
        "atlasOwnership":group,"recolorRecipe":"Achromatic neutral repaint luminance ratio clamped .65–1.45, native RGB scaled equally with no clipping; protected hue/marking pixels exact",
        "normalRecipe":"Normalize original Source tangent XYZ first, add bounded periodic generated finish XY, normalize again; original alpha and normalTexture.scale remain exact",
        "emissiveRecipe":"Original payload/factor/mask retained",
        "desktopEncoding":"ORM R AO/G roughness/B metalness/A0; runtimeMaps=false; normal/emissive original factors retained",
        "rgbaResampling":"Independent channels, no RGBA alpha premultiplication; source mask/ORM marker alpha cannot erase RGB",
        "outputs":outputs,"recolors":recolors,"toolSha256":sha(__file__)}
    recipe_path = folder/"recipe.json"; recipe_path.write_text(json.dumps(recipe,indent=2)+"\n")
    return outputs,recolors,keys,recipe_path


def roles(doc):
    result = {}
    for mat in doc.get("materials",[]):
        pbr = mat.get("pbrMetallicRoughness",{})
        refs = [(pbr.get("baseColorTexture"),"albedo"),(pbr.get("metallicRoughnessTexture"),"orm"),
                (mat.get("normalTexture"),"normal"),(mat.get("emissiveTexture"),"emissive")]
        refs += [(info,"recolor") for info in mat.get("extras",{}).get("projectPrimeRecolors",{}).values()]
        for info,role in refs:
            if info:
                result.setdefault(doc["textures"][info["index"]]["source"],set()).add(role)
    return result


def build(inventory_path, art_root, output):
    inventory_path = Path(inventory_path).resolve(); inventory = json.loads(inventory_path.read_text())
    output = Path(output).resolve(); source_pack = Path(inventory["sourcePack"])
    if output.exists() or output.is_relative_to(source_pack):
        raise ValueError("PBR output must be fresh and separate from accepted packs")
    for path,expected in inventory["lockedFiles"].items():
        if sha(path) != expected:
            raise ValueError(f"Immutable PBR input changed: {path}")
    output.mkdir(parents=True)
    tiles = build_tiles(inventory,art_root,output/"authoring-tiles")
    candidate = output/"starter"; shutil.copytree(source_pack,candidate)
    for path in candidate.rglob("*"):
        if path.is_file():
            path.chmod(0o644)
    manifest = json.loads((candidate/"characters.json").read_text()); manifest["id"] = "sourceio-hd-roster-v7-pbr"
    for entry in manifest["models"]:
        entry.pop("mobileModel",None)
    (candidate/"characters.json").write_text(json.dumps(manifest,indent=2)+"\n")
    paint = {"format":1,"inputInventory":{"path":str(inventory_path),"sha256":sha(inventory_path)},
        "sourcePack":str(source_pack),"candidatePack":str(candidate),"requiresMobileRebuild":True,
        "accepted":False,"models":[],"toolSha256":sha(__file__),"mapToolSha256":sha(Path(__file__).with_name("pbr_maps.py"))}
    for asset in inventory["assets"]:
        path = source_pack/asset["model"]; doc,original_blob = load(path); original_doc = copy.deepcopy(doc)
        blob = bytearray(original_blob); material_changes = []; texture_changes = []; overrides = {}
        old_roles = roles(doc); originals = {}
        model_folder = output/"model-maps"/asset["hunter"].lower()/(asset["part"]+"-lod"+str(asset["lod"]))
        for i in range(len(doc.get("images",[]))):
            data = image_bytes(doc,original_blob,i); extracted = model_folder/"original"/(str(i)+".png")
            extracted.parent.mkdir(parents=True,exist_ok=True); extracted.write_bytes(data)
            originals[i] = {"oldImageIndex":i,"newImageIndex":i,"operation":"preserve",
                "sourcePayloadSha256":digest(data),"outputImage":{"path":str(extracted),"sha256":sha(extracted)},
                "outputPayloadSha256":digest(data)}
        def change(index,path,after):
            value = doc["materials"][index]
            for field in path[:-1]:
                value = value.setdefault(field,{})
            before = copy.deepcopy(value.get(path[-1]))
            if before != after:
                value[path[-1]] = copy.deepcopy(after)
                material_changes.append({"index":index,"path":path,"before":before,"after":after})
        def bind(info,data,record):
            old_index,old_payload = source_image(original_doc,original_blob,info) if info else (None,None)
            if old_payload == data:
                index = old_index
            else:
                blob.extend(b"\0"*(-len(blob)%4)); offset = len(blob); blob.extend(data)
                view = len(doc["bufferViews"]); doc["bufferViews"].append({"buffer":0,"byteOffset":offset,"byteLength":len(data)})
                index = len(doc["images"]); doc["images"].append({"bufferView":view,"mimeType":"image/png","name":"pbr-"+digest(data)})
            record.update(oldImageIndex=old_index,newImageIndex=index,outputPayloadSha256=digest(data))
            if old_payload is not None:
                record["sourcePayloadSha256"] = digest(old_payload)
            overrides[index] = record
            if old_payload == data:
                return copy.deepcopy(info)
            texture = copy.deepcopy(original_doc["textures"][info["index"]]) if info else {"sampler":0}
            texture["source"] = index
            ti = len(doc["textures"]); doc["textures"].append(texture)
            texture_changes.append({"index":ti,"before":None,"after":copy.deepcopy(texture)})
            return {**(copy.deepcopy(info) if info else {}),"index":ti}
        for material in asset["materials"]:
            if not material["sourceOwnership"]:
                continue
            i = material["index"]; mat = doc["materials"][i]; group = material["sourceOwnership"]
            folder = model_folder/("material-"+str(i)); folder.mkdir(parents=True,exist_ok=True)
            results,recolors,keys,recipe = compose_material(asset,material,tiles,folder)
            labels = [int(key) for key in group["albedoOwnershipMask"]["labels"] if int(key) > 0]
            ownership = {**group["albedoOwnershipMask"],"labels":labels}
            common = {"ownership":ownership,"ownershipSourceImageIndex":material["channels"]["albedo"]["imageIndex"],
                "variantReason":"Source material class/registered periodic tiles shared across each hunter's LOD/body/weapon/alt; native identity may differ"}
            operations = [tiles[key]["operation"] for key in keys]
            operation = "repaint" if "repaint" in operations else "reusedAuthoredPaint" if "reusedAuthoredPaint" in operations else "preserve"
            info = mat["pbrMetallicRoughness"]["baseColorTexture"]
            albedo_data = Path(results["albedo"]["path"]).read_bytes()
            row = {**common,"role":"albedo","operation":operation,"outputImage":results["albedo"],"protectedMask":results["protectedMask"]}
            if operation == "preserve":
                original_index,albedo_data = source_image(original_doc,original_blob,info)
                row["outputImage"] = originals[original_index]["outputImage"]
            if operation != "preserve":
                row["paintProof"] = recipe_proof(tiles,keys,recipe,operation == "reusedAuthoredPaint",digest(albedo_data))
            updated = bind(info,albedo_data,row)
            change(i,["pbrMetallicRoughness","baseColorTexture"],updated)
            for suit,image in recolors.items():
                info = mat["extras"]["projectPrimeRecolors"][suit]
                data = Path(image["path"]).read_bytes(); old_index,old_data = source_image(original_doc,original_blob,info)
                if rgba(material["recolors"][suit]["path"]).tobytes() == rgba(image["path"]).tobytes():
                    continue
                row = {**common,"role":"recolor","operation":"derive","outputImage":image,"protectedMask":results["protectedMask"],
                    "derivation":{"recipePath":str(recipe),"recipeSha256":sha(recipe),"method":"achromatic native-hue luminance transfer"}}
                updated = bind(info,data,row); change(i,["extras","projectPrimeRecolors",suit],updated)
            if "normal" in results:
                info = mat["normalTexture"]; data = Path(results["normal"]["path"]).read_bytes()
                if rgba(material["channels"]["normal"]["path"]).tobytes() != rgba(results["normal"]["path"]).tobytes():
                    row = {**common,"role":"normal","operation":"derive","outputImage":results["normal"],
                        "derivation":{"recipePath":str(recipe),"recipeSha256":sha(recipe),"method":"Source-dominant registered micro-normal finish"}}
                    updated = bind(info,data,row); change(i,["normalTexture"],updated)
            info = mat["pbrMetallicRoughness"].get("metallicRoughnessTexture")
            data = Path(results["orm"]["path"]).read_bytes()
            row = {**common,"role":"orm","operation":"derive","outputImage":results["orm"],
                "derivation":{"recipePath":str(recipe),"recipeSha256":sha(recipe),"method":"authored surface-class AO/roughness/metalness"}}
            # This is a new encoding, not a modification of an old Phong map.
            # Keep the legacy image preserved; claim new Source-owned data.
            row["ownershipSourceImageIndex"] = material["channels"]["albedo"]["imageIndex"]
            updated = bind(None,data,row)
            # Preserve the original material map's sampler (or albedo sampler).
            prior_texture = original_doc["textures"][(info or original_doc["materials"][i]["pbrMetallicRoughness"]["baseColorTexture"])["index"]]
            if "sampler" in prior_texture:
                doc["textures"][updated["index"]]["sampler"] = prior_texture["sampler"]
                texture_changes[-1]["after"] = copy.deepcopy(doc["textures"][updated["index"]])
            change(i,["pbrMetallicRoughness","metallicRoughnessTexture"],updated)
            change(i,["pbrMetallicRoughness","metallicFactor"],1)
            change(i,["pbrMetallicRoughness","roughnessFactor"],1)
            change(i,["extras","projectPrimeMaterialEncoding"],"orm")
            change(i,["extras","projectPrimeRuntimeMaps"],False)
        destination = candidate/asset["model"]; write_glb(destination,doc,blob)
        new_roles = roles(doc); records = []
        for index in range(len(doc["images"])):
            record = overrides.get(index,originals.get(index))
            if not record:
                raise ValueError("Unreported authored image")
            if index not in new_roles:
                if record["operation"] != "preserve" or index not in originals:
                    raise ValueError("New authored image was not bound to a native material")
                record.update(role="historicalUnused",historicalUnused=True)
            elif "role" not in record:
                record["role"] = sorted(new_roles[index])[0]
            records.append(record)
        paint["models"].append({"model":asset["model"],"hunter":asset["hunter"],"part":asset["part"],"lod":asset["lod"],
            "sourceSha256":asset["modelSha256"],"candidateSha256":sha(destination),
            "materialChanges":material_changes,"textureChanges":texture_changes,"maps":records,
            "originalBinPrefixUnchanged":bytes(blob[:len(original_blob)]) == original_blob})
        print("PBR_CANDIDATE_MODEL",asset["model"],flush=True)
    for path,expected in inventory["lockedFiles"].items():
        if sha(path) != expected:
            raise ValueError("Source pack/evidence changed while painting")
    path = output/"PAINT-MANIFEST.json"; path.write_text(json.dumps(paint,indent=2)+"\n")
    print(json.dumps({"pass":True,"accepted":False,"models":len(paint["models"]),"candidate":str(candidate),"paintManifest":str(path)}))
    return paint


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory",required=True,type=Path)
    parser.add_argument("--art-root",required=True,type=Path)
    parser.add_argument("--output",required=True,type=Path)
    args = parser.parse_args(); build(args.inventory,args.art_root,args.output)


if __name__ == "__main__":
    main()
