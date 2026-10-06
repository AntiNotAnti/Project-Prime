"""Independent replay of the fixed registered repaint/ORM authoring recipes.

No converter/paint implementation is imported. Recipe parameters are artist
choices; replay proves what pixels they produce, not measured physical values.
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

from pbr_audit import checked_file, fit, require, sha


LUMA = np.array([.2126, .7152, .0722])


def rgba(path, size=None):
    with Image.open(path) as image:
        result = np.asarray(image.convert("RGBA"), dtype=np.uint8).copy()
    if size is not None and list(result.shape[1::-1]) != list(size):
        # A data/Source shader-mask alpha is independent of RGB; do not use
        # Pillow's implicit premultiplied RGBA resize for these authoring maps.
        result = np.stack([np.asarray(Image.fromarray(result[..., i]).resize(tuple(size), Image.Resampling.LANCZOS))
                           for i in range(4)], axis=2)
    return result


def known_rgba(record, size=None):
    return rgba(checked_file(record if "sha256" in record else {**record, "sha256": record["payloadSha256"]}), size)


def tile_replay(path, target):
    recipe = json.loads(Path(path).read_text())
    require(recipe["sourceMaterial"] == target["sourceMaterial"] and recipe["hunter"] == target["hunter"], "Tile recipe target differs")
    require(recipe["oldShippingTiles"] == list(target["shippingAuthoredAlbedoTiles"].values()), "Tile uses different original shipping artwork")
    require(recipe["sourceOriginalAlbedo"] == target["originalChannels"]["albedo"], "Tile original Source reference differs")
    tiles = recipe["oldShippingTiles"]
    largest = max(tiles, key=lambda row: row["size"][0]*row["size"][1])
    size = largest["size"]
    old = known_rgba(largest, size)
    original = known_rgba(recipe["sourceOriginalAlbedo"])
    rgb = original[..., :3].astype(np.float64)/255
    gray = rgb @ LUMA
    edge = np.maximum(np.max(np.abs(rgb-np.roll(rgb, 1, axis=0)), axis=2),
                      np.max(np.abs(rgb-np.roll(rgb, 1, axis=1)), axis=2))
    protected = (edge >= .15) | (gray <= .055)
    name = recipe["sourceMaterial"]
    if name in {"Weavel_Body", "Weavel_Handgun", "Spire_Torso"}:
        orange = (rgb[..., 0] >= .45) & (rgb[..., 1] >= .15) & (rgb[..., 2] <= .25)
        orange &= rgb[..., 0]-rgb[..., 2] >= .30
        protected |= orange
        if name == "Spire_Torso":
            protected |= gray >= .94
    protected = np.asarray(Image.fromarray(protected.astype(np.uint8)*255)
                           .filter(ImageFilter.MaxFilter(3)).resize(tuple(size), Image.Resampling.NEAREST)) != 0
    actual_mask = known_rgba(recipe["albedoComposite"]["protectedMask"])[..., 0] != 0
    require(np.array_equal(protected, actual_mask), "Protected edge/cavity/marking mask differs from fixed registered recipe")
    output = old.copy()
    operation = recipe["operation"]
    registration_error = None
    if operation == "repaint":
        require(len(recipe["generatedImages"]) == 1, "Tile repaint needs its exact generated bitmap")
        generated = recipe["generatedImages"][0]
        art_path = checked_file(generated)
        prompt_path = checked_file(generated, "promptPath", "promptSha256")
        prompt = json.loads(prompt_path.read_text())
        registration = recipe["registration"]
        reference = Path(prompt.get("reference", prompt.get("referencePath", ""))).resolve()
        require(reference == Path(registration["referencePath"]).resolve()
                and sha(reference) == registration["referenceSha256"], "Prompt reference correspondence differs")
        actual_reference = rgba(reference)
        basis = Path(registration["basisPath"]).resolve()
        permitted = {str(Path(target["sourceAuthoredAlbedo"]["path"]).resolve()),
                     str(Path(target["originalChannels"]["albedo"]["path"]).resolve())}
        require(str(basis) in permitted and sha(basis) == registration["basisSha256"], "Paint registration uses a different Source basis")
        source = rgba(basis, actual_reference.shape[1::-1])
        transform = registration["generatedToGltfTransform"]
        require(transform in {"identity", "vertical flip"}, "Undeclared Source reference orientation")
        if transform == "vertical flip":
            source = source[::-1]
        registration_error = float(np.abs(source[..., :3].astype(np.float64)-actual_reference[..., :3]).mean())
        require(registration_error <= .02, "Generated Source reference orientation differs")
        art = rgba(art_path, size)
        if transform == "vertical flip":
            art = art[::-1]
        residual = np.clip(art[..., :3].astype(np.float32)-old[..., :3].astype(np.float32), -72, 72)
        output[..., :3] = np.rint(np.clip(old[..., :3].astype(np.float32)+.72*residual, 0, 255)).astype(np.uint8)
        output[protected] = old[protected]
    elif operation == "reusedAuthoredPaint":
        previous = target.get("priorGeneratedSamusArt")
        require(previous and (previous.get("allShippingUsesExactlyMatchKnownGoodArtwork")
                             or previous.get("allShippingUsesExactlyMatchKnownGoodJpeg")), "Reused Samus artwork lacks exact earlier authoring correspondence")
    else:
        require(operation == "preserve", "Unknown authoring tile operation")
    actual = known_rgba(recipe["outputs"]["albedo"])
    require(np.array_equal(output, actual), "Generated albedo compositing/Source alpha/pigment registration differs")
    finish = output
    if operation == "reusedAuthoredPaint":
        previous = target["priorGeneratedSamusArt"]
        require(sha(previous["acceptedArtworkPath"]) == previous["acceptedArtworkSha256"], "Lossless Samus finish master differs")
        finish = rgba(previous["acceptedArtworkPath"], size)
    luminance = finish[..., :3].astype(np.float32) @ LUMA/255
    stencil = (luminance+np.roll(luminance, 1, 0)+np.roll(luminance, -1, 0)
               +np.roll(luminance, 1, 1)+np.roll(luminance, -1, 1))/5
    height = np.clip(luminance-stencil, -.08, .08)
    dx = (np.roll(height, -1, 1)-np.roll(height, 1, 1))*.5
    dy = (np.roll(height, -1, 0)-np.roll(height, 1, 0))*.5
    refinement = recipe["normalRefinement"]
    amplitude = refinement["amplitude"]
    surface = recipe["orm"]["materialClass"]
    expected_amplitude = .35 if "organic" in surface else .50 if "rock" in surface else .40
    if operation == "preserve" or "emitter" in surface or "hair" in surface:
        expected_amplitude = 0
    require(amplitude == expected_amplitude and refinement["maximumAddedXYNorm"] == .025, "Approved normal finish amplitude/bound differs")
    expected_micro = np.stack([-dx*amplitude, -dy*amplitude], axis=2).astype(np.float32)
    expected_micro *= np.minimum(1, .025/np.maximum(np.linalg.norm(expected_micro, axis=2, keepdims=True), 1e-8))
    micro_path = checked_file(refinement, "microXYPath", "microXYSha256")
    micro = np.load(micro_path, allow_pickle=False)
    require(np.array_equal(micro, expected_micro), "Normal finish signal differs from actual generated/lossless authoring pixels")
    max_xy = float(np.linalg.norm(micro, axis=2).max())
    require(max_xy <= .025+1e-7, "Authored finish XY magnitude exceeds .025")
    base_luminance = old[..., :3].astype(np.float32) @ LUMA/255
    cavity = np.clip((.20-base_luminance)/.20, 0, 1)
    ao = 1-.08*cavity
    if "emitter" in surface:
        ao = np.ones_like(ao)
    roughness = np.clip(recipe["orm"]["roughnessBase"]+.10*cavity+.035*np.clip(height/.08, -1, 1), .08, .96)
    metal = np.zeros_like(ao)
    if name in {"Metal", "Weavel_Handgun2"}:
        metal = np.full_like(ao, .78)
    elif name in {"Weavel_Handgun", "ArmCanon2"}:
        rgb = old[..., :3].astype(np.float64)/255
        chroma = rgb.max(axis=2)-rgb.min(axis=2)
        neutral = np.clip((.14-chroma)/.07, 0, 1)
        middle = np.clip((base_luminance-.16)/.16, 0, 1)*np.clip((.88-base_luminance)/.18, 0, 1)
        metal = .72*neutral*middle
    expected_orm = np.rint(np.clip(np.stack([ao, roughness, metal, np.zeros_like(ao)], axis=2), 0, 1)*255).astype(np.uint8)
    actual_orm = known_rgba(recipe["outputs"]["orm"])
    require(np.array_equal(expected_orm, actual_orm), "AO/roughness/coated-versus-exposed-metal mask differs from declared source-owned recipe")
    return {"sourceMaterial": name, "hunter": recipe["hunter"], "operation": operation,
            "recipePath": str(Path(path).resolve()), "recipeSha256": sha(path),
            "registrationReferenceRgbMeanError255": registration_error,
            "originalStrongEdgesAndProtectedMarkingsMaskExact": True, "actualGeneratedCompositePixelsExact": True,
            "microFinishFromGeneratedOrLosslessMasterExact": True, "maximumAddedXYNorm": max_xy,
            "canonicalOrmMaterialMaskPixelsExact": True, "materialClass": surface,
            "metalnessIsAuthoredNotPhysicallyMeasured": True}


def atlas_replay(path, asset):
    """Prove that tile identity, periodic UV repeats and padding were packed correctly."""
    recipe = json.loads(Path(path).read_text())
    require(recipe["model"] == asset["model"] and recipe["hunter"] == asset["hunter"], "Packed recipe model identity differs")
    group = recipe["atlasOwnership"]
    # Some Samus rigid node variants have multiple material slots sharing the
    # same Source texture/ownership. The immutable authoring location carries
    # the actual material index; do not invent uniqueness from image bytes.
    component = Path(path).parent.name
    require(component.startswith("material-") and component[9:].isdigit(), "Packed recipe lacks its material slot")
    material_index = int(component[9:])
    owners = [material for material in asset["materials"] if material["index"] == material_index
              and material.get("sourceOwnership") == group and material["nativeIdentity"] == recipe["nativeIdentity"]]
    require(len(owners) == 1, f"Packed material slot differs from immutable Source ownership: {path}")
    material = owners[0]
    old = known_rgba(material["channels"]["albedo"])
    color = old.copy()
    mask = np.zeros(old.shape[:2], dtype=np.uint8)
    orm = np.empty_like(old); orm[...] = [255, 180, 0, 0]
    micro = np.zeros((*old.shape[:2], 2), dtype=np.float32)
    tile_recipes = {}
    for reference in recipe["tileRecipes"]:
        tile_path = checked_file(reference)
        tile = json.loads(tile_path.read_text())
        tile_recipes[tile["sourceMaterial"]] = tile
    require(set(tile_recipes) == {source["sourceMaterial"] for source in group["sources"]}, "Packed tile set differs from immutable Source owners")

    def place(destination, source, tile):
        if group["directPeriodicTexture"]:
            destination[...] = tile
            return
        left, top = source["offsetPixels"]
        width, height = source["pixelSize"]
        tw, th = source["tileSize"]
        pad = group["paddingPixels"]
        # Explicit periodic indexing protects the original UV domain and
        # exact wrap edge; no gutter smearing or moving an island is allowed.
        x = (np.arange(width+2*pad)-pad) % tw
        y = (np.arange(height+2*pad)-pad) % th
        destination[top-pad:top+height+pad, left-pad:left+width+pad] = tile[y[:, None], x[None, :]]

    for source in group["sources"]:
        tile = tile_recipes[source["sourceMaterial"]]
        size = old.shape[1::-1] if group["directPeriodicTexture"] else source["tileSize"]
        if tile["operation"] != "preserve":
            place(color, source, known_rgba(tile["outputs"]["albedo"], size))
            tile_mask = Image.open(checked_file(tile["albedoComposite"]["protectedMask"])).convert("L")
            place(mask, source, np.asarray(tile_mask.resize(tuple(size), Image.Resampling.NEAREST)))
            signal = np.load(checked_file(tile["normalRefinement"], "microXYPath", "microXYSha256"), allow_pickle=False)
            signal = np.stack([np.asarray(Image.fromarray(signal[..., i]).resize(tuple(size), Image.Resampling.BILINEAR))
                               for i in range(2)], axis=2)
            place(micro, source, signal)
        else:
            place(mask, source, np.full((size[1], size[0]), 255, dtype=np.uint8))
        place(orm, source, known_rgba(tile["outputs"]["orm"], size))
    color[..., 3] = old[..., 3]
    color[mask != 0] = old[mask != 0]
    require(np.array_equal(color, known_rgba(recipe["outputs"]["albedo"])), "Packed albedo has wrong periodic Source ownership/paint registration")
    require(np.array_equal(orm, known_rgba(recipe["outputs"]["orm"])), "Packed ORM lost RGB under A0 or uses wrong material/periodic padding")
    require(np.array_equal(mask, known_rgba(recipe["outputs"]["protectedMask"])[..., 0]), "Packed protected source-edge/marking mask differs")
    max_xy, meaningful_fraction = 0., None
    if "normal" in recipe["outputs"]:
        original = known_rgba(material["channels"]["normal"])
        size = original.shape[1::-1]
        signal = np.stack([np.asarray(Image.fromarray(micro[..., i]).resize(tuple(size), Image.Resampling.BILINEAR))
                           for i in range(2)], axis=2)
        base = original[..., :3].astype(np.float32)/127.5-1
        base /= np.maximum(np.linalg.norm(base, axis=2, keepdims=True), 1e-8)
        directions = base.copy(); directions[..., :2] += signal
        directions /= np.maximum(np.linalg.norm(directions, axis=2, keepdims=True), 1e-8)
        result = original.copy(); result[..., :3] = np.rint(np.clip(directions*.5+.5, 0, 1)*255).astype(np.uint8)
        owner = np.asarray(Image.open(checked_file(group["albedoOwnershipMask"]))
                           .resize(tuple(size), Image.Resampling.NEAREST)) != 0
        result[~owner] = original[~owner]
        if all(tile["operation"] == "preserve" for tile in tile_recipes.values()):
            result = original
        require(np.array_equal(result, known_rgba(recipe["outputs"]["normal"])), "Packed normal signal/direction/alpha/ownership differs")
        max_xy = float(np.linalg.norm(signal, axis=2)[owner].max())
        require(max_xy <= .025+1e-7, "Repacked normal finish exceeds approved unit-space bound")
        base_quantized = np.rint(np.clip(base*.5+.5, 0, 1)*255).astype(np.uint8)
        meaningful_fraction = float(np.mean(np.any(result[..., :3] != base_quantized, axis=2)[owner]))
    old_luminance = old[..., :3].astype(np.float64) @ LUMA
    new_luminance = color[..., :3].astype(np.float64) @ LUMA
    ratio = np.clip(new_luminance/np.maximum(old_luminance, 8), .65, 1.45)
    ratio[mask != 0] = 1
    for suit, reference in recipe["recolors"].items():
        previous = known_rgba(material["recolors"][suit])
        scale = np.minimum(ratio, 255/np.maximum(previous[..., :3].max(axis=2), 1))
        after = previous.copy(); after[..., :3] = np.rint(previous[..., :3]*scale[..., None]).astype(np.uint8)
        after[mask != 0] = previous[mask != 0]
        require(np.array_equal(after, known_rgba(reference)), "Native team/bright hue, alpha or registered repaint transfer differs")
    return {"model": asset["model"], "nativeIdentity": material["nativeIdentity"],
            "recipePath": str(Path(path).resolve()), "recipeSha256": sha(path),
            "periodicSourceOwnershipUvAndPaddingReplayExact": True, "canonicalOrmRgbUnderZeroAlphaPreserved": True,
            "sourceNormalAlphaAndUnitSpaceFinishReplayExact": True, "nativeRecolorHueTransferReplayExact": True,
            "maximumAddedXYNorm": max_xy, "quantizedFinishTexelFraction": meaningful_fraction}


def audit(paint, inventory):
    """Replay each distinct Source-authoring tile once, independent of packing."""
    tiles = {}
    recipes = set()
    packed = {}
    assets = {asset["model"]: asset for asset in inventory["assets"]}
    for model in paint["models"]:
        for image in model["maps"]:
            proof = image.get("paintProof") or image.get("derivation")
            if not proof:
                continue
            model_recipe_path = checked_file(proof, "recipePath", "recipeSha256")
            model_recipe = json.loads(model_recipe_path.read_text())
            packed.setdefault(model_recipe_path, assets[model["model"]])
            for reference in model_recipe["tileRecipes"]:
                path = checked_file(reference)
                recipes.add(path)
    for path in sorted(recipes):
        recipe = json.loads(path.read_text())
        key = recipe["hunter"]+"/"+recipe["sourceMaterial"]
        require(key not in tiles, "Duplicate Source authoring tile recipe")
        tiles[key] = tile_replay(path, inventory["authoringTargets"][key])
    require(set(tiles) == set(inventory["authoringTargets"]), "Registered authoring replay omits a Source material target")
    atlases = [atlas_replay(path, asset) for path, asset in sorted(packed.items())]
    return {"pass": True, "authoringTargets": tiles, "targets": len(tiles), "packedAtlases": atlases,
            "generatedRepaints": sum(row["operation"] == "repaint" for row in tiles.values()),
            "maximumAddedXYNorm": max(row["maximumAddedXYNorm"] for row in tiles.values()),
            "scope": "Independent fixed-formula Source registration/strong-edge mask/albedo composite/finish signal/ORM material-class pixel replay. Art choices and values are authored, not physical measurements; fresh rendered quality review remains required."}
