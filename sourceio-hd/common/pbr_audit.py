"""Independent checks for an authored PBR repaint candidate.

The authoring manifest is evidence, not an acceptance assertion. This tool locks
the original native contract and checks embedded candidate pixels, image-gen
provenance, paint coverage, masks and ORM semantics. A static PASS cannot prove
that markings or shading look good: those require model-bound saved-image
reviews and runtime acceptance. In particular, new roughness scalars, a global
color transform, or an upscale alone are not counted as a repaint.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import io
import json
import math
from collections import defaultdict
from pathlib import Path, PurePosixPath

import numpy as np
from PIL import Image, ImageFilter

from glb import image_bytes, load


SCHEMA = "sourceio-pbr-independent-audit-v1"
HUNTERS = {"Samus", "Kanden", "Noxus", "Spire", "Sylux", "Trace", "Weavel"}
ROLES = {"albedo", "recolor", "normal", "orm", "emissive", "historicalUnused"}
OPERATIONS = {"repaint", "reusedAuthoredPaint", "derive", "preserve"}
LIMITS = {
    "minimumChangedInteriorFraction": 0.01,
    "minimumNonAffineAlbedoRms255": 1.5,
    "changedPixelMinimumChannelDelta255": 2,
    "maximumChangedNormalMeanUnitError": 0.02,
    "maximumChangedNormalP99UnitError": 0.08,
    "maximumProtectedPixelChannelError255": 0,
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha_bytes(payload):
    return hashlib.sha256(payload).hexdigest()


def sha(path):
    return sha_bytes(Path(path).read_bytes())


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"))


def within(root, relative):
    name = PurePosixPath(relative)
    require(isinstance(relative, str) and relative and "\\" not in relative
            and not name.is_absolute() and all(x not in ("", ".", "..") for x in relative.split("/")),
            f"Unsafe pack-relative path: {relative}")
    root = Path(root).resolve()
    result = root.joinpath(*name.parts)
    require(result.resolve().is_relative_to(root) and not result.is_symlink(),
            f"Pack path leaves its root or is a symlink: {relative}")
    return result


def pack_files(root):
    result = {}
    root = Path(root).resolve()
    for path in sorted(root.rglob("*")):
        require(not path.is_symlink(), f"Symlink in pack: {path}")
        if path.is_file():
            result[path.relative_to(root).as_posix()] = sha(path)
    return result


def checked_file(record, field="path", hash_field="sha256"):
    require(isinstance(record, dict) and record.get(field) and record.get(hash_field),
            f"Missing immutable file reference: {field}/{hash_field}")
    path = Path(record[field]).resolve()
    require(path.is_file() and sha(path) == record[hash_field], f"Evidence hash differs: {path}")
    return path


def pixels(payload, allow_jpeg=False):
    with Image.open(io.BytesIO(payload)) as image:
        require(image.format == "PNG" or (allow_jpeg and image.format == "JPEG"),
                "New desktop paint must be PNG; only exact accepted Source/history may be JPEG")
        return np.asarray(image.convert("RGBA"), dtype=np.uint8).copy()


def fit(array, size, resample=Image.Resampling.LANCZOS):
    if list(array.shape[1::-1]) == list(size):
        return array.copy()
    return np.asarray(Image.fromarray(array).resize(tuple(size), resample), dtype=np.uint8).copy()


def _accessor_bytes(doc, blob, index):
    accessor = doc["accessors"][index]
    require("sparse" not in accessor, "Sparse character accessor unsupported")
    view = doc["bufferViews"][accessor["bufferView"]]
    components = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}[accessor["type"]]
    component_size = {5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4}[accessor["componentType"]]
    size = components * component_size
    stride = view.get("byteStride", size)
    start = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    count = accessor["count"]
    require(view.get("buffer", 0) == 0 and stride >= size and start >= view.get("byteOffset", 0)
            and start + max(0, count - 1) * stride + (size if count else 0)
            <= view.get("byteOffset", 0) + view["byteLength"], "Accessor exceeds its view")
    return b"".join(blob[start + i * stride:start + i * stride + size] for i in range(count))


def geometry(old, old_blob, new, new_blob):
    # Includes primitive indices/material identity, split normals, UV0, node
    # parenting, rigid transforms, skeleton names and all inverse bind matrices.
    for key in ("asset", "nodes", "skins", "meshes", "scenes", "scene", "animations", "samplers"):
        require(old.get(key) == new.get(key), f"Repaint changed geometry/native semantics: {key}")
    require(len(old.get("accessors", [])) == len(new.get("accessors", [])), "Accessor count changed")
    digests = []
    for i, (a, b) in enumerate(zip(old.get("accessors", []), new.get("accessors", []))):
        require({k: v for k, v in a.items() if k != "bufferView"}
                == {k: v for k, v in b.items() if k != "bufferView"}, f"Accessor metadata changed: {i}")
        before, after = _accessor_bytes(old, old_blob, i), _accessor_bytes(new, new_blob, i)
        require(before == after, f"Positions/indices/UVs/normals/weights/native binds changed: {i}")
        av, bv = old["bufferViews"][a["bufferView"]], new["bufferViews"][b["bufferView"]]
        require({k: v for k, v in av.items() if k != "byteOffset"}
                == {k: v for k, v in bv.items() if k != "byteOffset"}, f"Accessor view metadata changed: {i}")
        digests.append(sha_bytes(after))
    return digests


def _path_value(value, path):
    for key in path:
        if not isinstance(value, dict) or key not in value:
            return None
        value = value[key]
    return value


def _set_path(value, path, after):
    for key in path[:-1]:
        require(key not in value or isinstance(value[key], dict), "Material patch crosses a scalar")
        value = value.setdefault(key, {})
    if after is None:
        value.pop(path[-1], None)
    else:
        value[path[-1]] = copy.deepcopy(after)


def material_contract(old, new, patches):
    require(len(old.get("materials", [])) == len(new.get("materials", [])), "Native material count changed")
    expected = copy.deepcopy(old.get("materials", []))
    used = set()
    allowed = {"pbrMetallicRoughness", "normalTexture", "emissiveTexture", "emissiveFactor", "occlusionTexture", "extras", "extensions"}
    for patch in patches:
        index, path = patch.get("index"), patch.get("path")
        require(isinstance(index, int) and 0 <= index < len(expected) and isinstance(path, list)
                and path and all(isinstance(x, str) for x in path), "Invalid declared material patch")
        require(path[0] in allowed, f"Material patch may not change identity/culling/alpha: {path}")
        require(not (path[0] == "extras" and path[1:] not in
                     (["projectPrimeMaterialEncoding"], ["projectPrimeRuntimeMaps"], ["sourcePhongApproximation"])
                     and path[1:2] != ["projectPrimeRecolors"]), "Undeclared native extra semantics")
        key = (index, tuple(path))
        require(key not in used, "Duplicate material patch")
        used.add(key)
        require(_path_value(expected[index], path) == patch.get("before"), f"Material patch before value differs: {key}")
        _set_path(expected[index], path, patch.get("after"))
    require(expected == new.get("materials", []), "Candidate material differs beyond explicitly declared patches")
    for i, (a, b) in enumerate(zip(old.get("materials", []), new.get("materials", []))):
        for field in ("name", "alphaMode", "alphaCutoff", "doubleSided"):
            require(a.get(field) == b.get(field), f"Native material identity/alpha/culling changed: {i}/{field}")
        old_recolors = a.get("extras", {}).get("projectPrimeRecolors", {})
        new_recolors = b.get("extras", {}).get("projectPrimeRecolors", {})
        require(set(old_recolors) == set(new_recolors), f"Native recolor eligibility changed: {i}")


def texture_roles(doc):
    roles = defaultdict(set)
    for material in doc.get("materials", []):
        pbr = material.get("pbrMetallicRoughness", {})
        fields = [(pbr.get("baseColorTexture"), "albedo"), (pbr.get("metallicRoughnessTexture"), "orm"),
                  (material.get("normalTexture"), "normal"), (material.get("emissiveTexture"), "emissive")]
        fields += [(v, "recolor") for v in material.get("extras", {}).get("projectPrimeRecolors", {}).values()]
        for info, role in fields:
            if info is None:
                continue
            require(info.get("texCoord", 0) == 0 and not info.get("extensions"), "Paint changed texture transform/UV channel")
            texture = doc["textures"][info["index"]]
            require("source" in texture and not texture.get("extensions"), "Desktop PBR needs plain texture source")
            roles[texture["source"]].add(role)
    return roles


def mask_pixels(reference, size, labels=None):
    path = (checked_file(reference, "maskPath", "maskSha256")
            if "maskPath" in reference else checked_file(reference))
    with Image.open(path) as image:
        require(image.format == "PNG", "Paint masks must be lossless PNG")
        # 16-bit IDs are allowed: preserve integer ownership instead of RGB
        # conversion, which would destroy labels above 255.
        image = image.resize(tuple(size), Image.Resampling.NEAREST)
        values = np.asarray(image)
        if values.ndim == 3:
            require(np.array_equal(values[..., 0], values[..., 1]) and
                    np.array_equal(values[..., 0], values[..., 2]), "Ownership mask must be integer grayscale IDs")
            values = values[..., 0]
    return np.isin(values, labels) if labels is not None else values != 0


def paint_provenance(proof, reused=False):
    require(isinstance(proof, dict), "Paint operation has no provenance")
    recipe = checked_file(proof, "recipePath", "recipeSha256")
    # The actual recipe is immutable and inspectable. Do not pretend that a
    # hash alone independently replays an arbitrary artist's program.
    require(proof.get("semanticMaterials") and all(isinstance(x, str) and x for x in proof["semanticMaterials"]),
            "Paint must declare authored surface classes")
    images = []
    for generated in proof.get("generatedImages", []):
        require(generated.get("tool") == "image_gen", "Generated-paint evidence must identify image_gen")
        path = checked_file(generated)
        checked_file(generated, "promptPath", "promptSha256")
        with Image.open(path) as image:
            require(image.width >= 128 and image.height >= 128, "Generated paint source is too small")
        images.append(generated["sha256"])
    if reused:
        # Authorized earlier Samus repaint is allowed only with its actual
        # immutable authoring receipt and output/input link. An assertion of
        # "already painted" is insufficient.
        receipt = checked_file(proof, "authoringReceiptPath", "authoringReceiptSha256")
        require(proof.get("authoredOutputSha256"), "Reused paint needs its earlier output hash")
        return {"mode": "reusedAuthoredPaint", "recipeSha256": sha(recipe),
                "authoringReceiptSha256": sha(receipt), "generatedBitmapSha256": images}
    require(images, "Repaint has no actual generated bitmap")
    return {"mode": "generatedRepaint", "recipeSha256": sha(recipe), "generatedBitmapSha256": images}


def change_metrics(before, after, mask):
    # A bounded fit rules out a scalar, RGB matrix/tint or mere resize. It does
    # not classify which residual details are attractive or semantically right.
    require(mask.any(), "Paint has no owned texels")
    a = before[..., :3].astype(np.float32)
    b = after[..., :3].astype(np.float32)
    delta = np.max(np.abs(b-a), axis=-1)
    changed = float(np.mean(delta[mask] >= LIMITS["changedPixelMinimumChannelDelta255"]))
    flat = np.flatnonzero(mask.reshape(-1))
    step = max(1, math.ceil(len(flat) / 262144))
    indices = flat[::step]
    x = a.reshape(-1, 3)[indices]
    y = b.reshape(-1, 3)[indices]
    design = np.column_stack((x, np.ones(len(x), dtype=np.float32)))
    coefficients = np.linalg.lstsq(design, y, rcond=None)[0]
    residual = float(np.sqrt(np.mean((design @ coefficients-y)**2)))
    return {"changedInteriorFraction": changed, "nonAffineAlbedoRms255": residual,
            "maximumChannelChange255": int(delta[mask].max()), "fitTexels": len(indices)}


def audit_map(before_payload, after_payload, record, role_set):
    role, operation = record.get("role"), record.get("operation")
    require(role in ROLES and operation in OPERATIONS and role in role_set, "Invalid image role/operation")
    before = pixels(before_payload, allow_jpeg=True) if before_payload is not None else None
    after = pixels(after_payload, allow_jpeg=operation == "preserve")
    size = list(after.shape[1::-1])
    output = checked_file(record["outputImage"])
    require(np.array_equal(after, pixels(output.read_bytes(), allow_jpeg=operation == "preserve")), "Embedded candidate pixels differ from authored output")
    require(record.get("outputPayloadSha256", sha_bytes(after_payload)) == sha_bytes(after_payload), "Candidate embedded payload hash differs")
    row = {"role": role, "operation": operation, "size": size,
           "outputPixelSha256": sha_bytes(after.tobytes()), "outputPayloadSha256": sha_bytes(after_payload)}
    if before is not None:
        before = fit(before, size)
    if operation == "preserve":
        require(before_payload is not None and before_payload == after_payload, "Preserve operation changed pixels/payload")
        if role == "historicalUnused":
            require(record.get("historicalUnused") is True, "Preserved unused history must be declared explicitly")
            row["historicalUnused"] = True
        row["preservedBytes"] = True
        return row
    require("ownership" in record and record["ownership"].get("labels"), "Authored map needs immutable per-texel ownership labels")
    ownership = record["ownership"]
    mask = mask_pixels(ownership, size, ownership["labels"])
    require(mask.any(), "Authored map has empty Source ownership")
    interior = np.asarray(Image.fromarray(mask.astype(np.uint8)*255).filter(ImageFilter.MinFilter(3))) != 0
    if not interior.any():
        interior = mask
    row["ownedTexels"] = int(mask.sum())
    row["interiorTexels"] = int(interior.sum())
    row["ownershipSha256"] = ownership.get("sha256", ownership.get("maskSha256"))
    if before is not None and role != "orm":
        require(np.array_equal(after[~mask], before[~mask]), "Paint changed unowned atlas texels/neighbor padding")
        require(np.array_equal(after[..., 3], before[..., 3]), "Albedo/normal/emissive alpha changed")
        row["alphaPreserved"] = True
        row["unownedPixelsPreserved"] = True
    if record.get("protectedMask"):
        protected = mask_pixels(record["protectedMask"], size)
        require(before is not None, "Protected marking mask has no original artwork")
        require(np.array_equal(after[protected], before[protected]), "Paint moved/changed protected markings, seams or cutouts")
        row["protectedMarkingPixels"] = int(protected.sum())
        if not np.any(protected & mask):
            # Flat original Kanden/Sylux sheets have no high-contrast edge or
            # cavity to mask. The independent fixed-policy recipe replay below
            # must reproduce this empty mask; do not fabricate markings or
            # count an empty mask as visual proof of their retention.
            row["protectedMaskIsEmpty"] = True
            row["protectedMarkingsAcceptance"] = "original fixed-policy mask is empty; manual model fidelity review required"
        row["protectedMaskSha256"] = record["protectedMask"].get("sha256", record["protectedMask"].get("maskSha256"))
    else:
        row["protectedMarkingsAcceptance"] = "requires manual bitmap/model review"
    if operation == "repaint":
        require(role in {"albedo", "recolor"} and before is not None, "Only color artwork can claim repaint")
        row["provenance"] = paint_provenance(record.get("paintProof"))
        row["changeMetrics"] = change_metrics(before, after, interior)
        require(row["changeMetrics"]["changedInteriorFraction"] >= LIMITS["minimumChangedInteriorFraction"]
                and row["changeMetrics"]["nonAffineAlbedoRms255"] >= LIMITS["minimumNonAffineAlbedoRms255"],
                "Result is unchanged, resize-only or a global affine/scalar color change, not a proven repaint")
    elif operation == "reusedAuthoredPaint":
        require(role in {"albedo", "recolor"}, "Reused paint is only color artwork")
        require(before is not None and np.array_equal(before, after),
                "Reused authored Samus paint must preserve the accepted decoded artwork exactly")
        row["provenance"] = paint_provenance(record.get("paintProof"), reused=True)
        proof = record["paintProof"]
        require(proof["authoredOutputSha256"] in (sha_bytes(after_payload), sha_bytes(after.tobytes())),
                "Earlier repaint output is not the embedded candidate")
    else:
        require(record.get("derivation"), "Derived normal/ORM/emissive needs an inspectable derivation")
        checked_file(record["derivation"], "recipePath", "recipeSha256")
        row["derivation"] = record["derivation"]
    if role == "normal":
        vectors = after[..., :3].astype(np.float32)/127.5-1
        length = np.linalg.norm(vectors, axis=-1)
        error = np.abs(length[mask]-1)
        require(float(error.mean()) <= LIMITS["maximumChangedNormalMeanUnitError"]
                and float(np.percentile(error, 99)) <= LIMITS["maximumChangedNormalP99UnitError"], "Authored normal map is not normalized")
        backwards = (vectors[..., 2] < -.02) & mask
        inherited = np.zeros(mask.shape, dtype=bool)
        if before is not None:
            original = before[..., :3].astype(np.float32)/127.5-1
            original /= np.maximum(np.linalg.norm(original, axis=-1, keepdims=True), 1e-8)
            inherited = (original[..., 2] < -.02) & mask
        require(not np.any(backwards & ~inherited), "Normal refinement introduced a backward tangent normal")
        row["inheritedBackwardSourceNormalTexels"] = int(inherited.sum())
        row["newBackwardNormalTexels"] = 0
        if before is not None:
            before_direction = original.astype(np.float64)
            before_direction /= np.maximum(np.linalg.norm(before_direction, axis=-1, keepdims=True), 1e-8)
            after_direction = vectors.astype(np.float64)
            after_direction /= np.maximum(np.linalg.norm(after_direction, axis=-1, keepdims=True), 1e-8)
            angular = np.degrees(np.arccos(np.clip(np.sum(before_direction*after_direction, axis=-1), -1, 1)))
            quantized_base = np.rint(np.clip(before_direction*.5+.5, 0, 1)*255).astype(np.uint8)
            row["normalAngularChangeDegrees"] = {"mean": float(angular[mask].mean()),
                                                 "p99": float(np.percentile(angular[mask], 99)),
                                                 "maximum": float(angular[mask].max())}
            row["quantizedFinishTexelFraction"] = float(np.mean(np.any(after[..., :3] != quantized_base, axis=-1)[mask]))
        row["normalMeanUnitError"] = float(error.mean())
        row["normalP99UnitError"] = float(np.percentile(error, 99))
    elif role == "orm":
        require(np.all(after[..., 3] == 0), "Every ORM texel must carry the agreed runtime A=0 encoding marker")
        roughness = after[..., 1][interior]
        require(roughness.min() >= 10, "Authored roughness is below the .04 stability floor")
        row["ormMeaning"] = "R=AO, G=roughness, B=metalness; A=0 opt-in marker independent of draw alpha"
        row["aoRange255"] = [int(after[..., 0][mask].min()), int(after[..., 0][mask].max())]
        row["roughnessRange255"] = [int(roughness.min()), int(roughness.max())]
        row["metalnessRange255"] = [int(after[..., 2][mask].min()), int(after[..., 2][mask].max())]
        row["roughnessStd255"] = float(roughness.astype(np.float32).std())
    return row


def _inventory_assets(inventory):
    assets = inventory["assets"]
    if isinstance(assets, dict):
        return [{"model": key, **value} for key, value in assets.items()]
    require(isinstance(assets, list), "Inventory assets must be a list or model-keyed object")
    return assets


def inventory_ownership(asset, record):
    """Bind authoring claims to the inventory's actual Source atlas authority."""
    if record["operation"] == "preserve":
        return []
    source_image = record.get("ownershipSourceImageIndex", record.get("oldImageIndex"))
    require(source_image is not None, "New image must name its original ownershipSourceImageIndex")
    offered = record.get("ownership", {})
    path = offered.get("path", offered.get("maskPath"))
    digest = offered.get("sha256", offered.get("maskSha256"))
    matches = []
    for material in asset["materials"]:
        slots = list(material.get("channels", {}).values()) + list(material.get("recolors", {}).values())
        if not any(slot.get("imageIndex") == source_image for slot in slots):
            continue
        ownership = material.get("sourceOwnership") or {}
        mask = ownership.get("albedoOwnershipMask")
        if mask and str(Path(mask["path"]).resolve()) == str(Path(path or ".").resolve()) and mask["sha256"] == digest:
            matches.append(mask)
    require(matches, "Paint mask is not the immutable original material's Source ownership authority")
    labels = offered.get("labels", [])
    require(labels and all(isinstance(label, int) and label > 0 for label in labels), "Only positive Source owner IDs can be painted")
    require(any(set(labels) <= {int(key) for key in mask["labels"] if int(key) > 0} for mask in matches),
            "Paint mask labels include a foreign material or unused background")
    return sorted({mask["labels"][str(label)] for mask in matches for label in labels if str(label) in mask["labels"]})


def audit(inventory_path, paint_manifest_path, candidate_root, output_path):
    inventory_path, paint_manifest_path = Path(inventory_path).resolve(), Path(paint_manifest_path).resolve()
    candidate = Path(candidate_root).resolve()
    output = Path(output_path).resolve()
    require(not output.is_relative_to(candidate), "Audit output must not mutate the candidate pack")
    output.unlink(missing_ok=True)  # Never retain a prior PASS after failure.
    inventory = json.loads(inventory_path.read_text())
    paint = json.loads(paint_manifest_path.read_text())
    require(paint.get("format") == 1, "Unsupported repaint authoring manifest")
    reference = checked_file(paint["inputInventory"])
    require(reference == inventory_path, "Paint manifest references a different immutable inventory")
    require(inventory.get("pass") is True, "Input inventory was not accepted")
    for path, digest in inventory.get("lockedFiles", {}).items():
        require(sha(path) == digest, f"Immutable inventory evidence changed: {path}")
    source = Path(inventory["sourcePack"]).resolve()
    old_manifest = json.loads((source/"characters.json").read_text())
    new_manifest = json.loads((candidate/"characters.json").read_text())
    require(len(old_manifest["models"]) == len(new_manifest["models"]) == 29, "Repaint requires all 29 roster entries")
    require(set(entry["hunter"] for entry in new_manifest["models"]) == HUNTERS, "Hunter roster changed")
    for before, after in zip(old_manifest["models"], new_manifest["models"]):
        require({k: v for k, v in before.items() if k != "mobileModel"}
                == {k: v for k, v in after.items() if k != "mobileModel"}, "Native entry/bone/part/LOD contract changed")
    assets = {row["model"]: row for row in _inventory_assets(inventory)}
    models = {row["model"]: row for row in paint["models"]}
    require(len(assets) == len(models) == 29 and set(assets) == set(models)
            == {entry["model"] for entry in new_manifest["models"]}, "Paint/inventory model coverage differs")
    rows, coherent, source_hashes = [], {}, {}
    repaint_hunters, orm_materials = set(), 0
    for entry in new_manifest["models"]:
        name = entry["model"]
        author, locked = models[name], assets[name]
        original = within(source, name)
        require(sha(original) == author["sourceSha256"] == locked["modelSha256"], f"Frozen original model differs: {name}")
        current = within(candidate, name)
        old, old_blob = load(original)
        new, new_blob = load(current)
        accessor_hashes = geometry(old, old_blob, new, new_blob)
        material_contract(old, new, author.get("materialChanges", []))
        roles = texture_roles(new)
        originals = {i: image_bytes(old, old_blob, i) for i in range(len(old.get("images", [])))}
        records = author["maps"]
        index = {row["newImageIndex"]: row for row in records}
        require(len(index) == len(records) and set(index) == set(range(len(new.get("images", [])))),
                "Paint manifest must describe every embedded image exactly once, including preserved images")
        mapped_old = {row.get("oldImageIndex") for row in records if row.get("oldImageIndex") is not None}
        require(mapped_old == set(originals), "Original embedded image coverage was lost")
        texture_patches = author.get("textureChanges", [])
        expected_textures = copy.deepcopy(old.get("textures", []))
        for patch in texture_patches:
            texture_index = patch["index"]
            require(0 <= texture_index <= len(expected_textures), "Invalid declared texture patch")
            previous = expected_textures[texture_index] if texture_index < len(expected_textures) else None
            require(previous == patch.get("before"), "Texture patch before value differs")
            after = patch["after"]
            require(isinstance(after, dict) and "source" in after and "extensions" not in after, "Desktop texture patch must preserve PNG semantics")
            if previous is not None:
                require({k: v for k, v in previous.items() if k != "source"}
                        == {k: v for k, v in after.items() if k != "source"}, "Sampler/texture flags changed")
            else:
                require(set(after) <= {"source", "sampler", "name"}, "Unexpected new texture properties")
            if texture_index == len(expected_textures):
                expected_textures.append(after)
            else:
                expected_textures[texture_index] = after
        require(expected_textures == new.get("textures", []), "Candidate texture bindings differ beyond declarations")
        maps = []
        model_has_paint = False
        for image_index, record in sorted(index.items()):
            old_index = record.get("oldImageIndex")
            before_payload = originals.get(old_index)
            if old_index is not None:
                require(record.get("sourcePayloadSha256", sha_bytes(before_payload)) == sha_bytes(before_payload), "Original image payload hash differs")
            after_payload = image_bytes(new, new_blob, image_index)
            role_set = roles.get(image_index)
            if not role_set:
                require(old_index is not None and record["operation"] == "preserve"
                        and record["role"] == "historicalUnused" and record.get("historicalUnused") is True,
                        "New/modified image is unused; only byte-exact declared original history may be retained")
                role_set = {"historicalUnused"}
            require(record["role"] != "historicalUnused" or role_set == {"historicalUnused"},
                    "Historical image may not masquerade as a used runtime texture")
            owned_names = inventory_ownership(locked, record)
            try:
                checked = audit_map(before_payload, after_payload, record, role_set)
            except ValueError as error:
                raise ValueError(f"{name} image {image_index} ({record.get('role')}): {error}") from error
            checked.update(oldImageIndex=old_index, newImageIndex=image_index, roles=sorted(role_set))
            if checked["operation"] in {"repaint", "reusedAuthoredPaint"}:
                repaint_hunters.add(entry["hunter"])
                model_has_paint = True
            if before_payload is not None and record["operation"] != "preserve":
                # Identical flat normal maps belonging to different materials
                # can acquire distinct authored surface detail. Coherence is
                # required for the same hunter/Source ownership across LODs
                # and parts, not across unrelated owners of identical bytes.
                key = (entry["hunter"], tuple(owned_names), sha_bytes(before_payload), record["role"])
                if key in coherent and coherent[key] != checked["outputPixelSha256"]:
                    require(record.get("variantReason"), "Identical Source artwork has inconsistent LOD/part output without authored variant rationale")
                else:
                    coherent[key] = checked["outputPixelSha256"]
            maps.append(checked)
        require(model_has_paint, f"Model has no actual repaint/reused authored paint proof: {name}")
        exceptions = {exception["index"]: exception for exception in author.get("materialExceptions", [])}
        require(len(exceptions) == len(author.get("materialExceptions", [])), "Duplicate PBR material exception")
        for material_index, material in enumerate(new.get("materials", [])):
            extras = material.get("extras", {})
            if extras.get("projectPrimeMaterialEncoding") != "orm":
                if material.get("pbrMetallicRoughness", {}).get("metallicRoughnessTexture"):
                    exception = exceptions.get(material_index)
                    require(exception and exception.get("reason") and exception.get("nativeReference"),
                            "Authored legacy material map has no PBR conversion or explicit native/effect preservation evidence")
                continue
            require(isinstance(extras.get("projectPrimeRuntimeMaps"), bool),
                    "ORM materials must explicitly declare whether companions/factors are runtime-prepared")
            pbr = material.get("pbrMetallicRoughness", {})
            require(pbr.get("metallicFactor") == 1 and pbr.get("roughnessFactor") == 1,
                    "Authored ORM factors must be identity, avoiding double conversion")
            info = pbr.get("metallicRoughnessTexture")
            require(info and "orm" in roles[new["textures"][info["index"]]["source"]], "ORM material has no authored ORM map")
            row = index[new["textures"][info["index"]]["source"]]
            require(row["role"] == "orm" and row["operation"] == "derive", "PBR material uses a preserved legacy map")
            orm_materials += 1
        rows.append({"model": name, "hunter": entry["hunter"], "part": entry["part"], "lod": entry.get("lod", 0),
                     "sourceSha256": sha(original), "candidateSha256": sha(current),
                     "nativeGeometrySkinUVBytesExact": True, "accessorSha256": accessor_hashes,
                     "maps": maps})
        source_hashes[name] = sha(original)
    require(repaint_hunters == HUNTERS, "Every hunter needs actual authored color repaint provenance; scalar material polish does not count")
    require(orm_materials > 0, "Candidate has no physically based material opt-in")
    from pbr_recipe_audit import audit as replay_recipes
    recipe_replay = replay_recipes(paint, inventory)
    result = {"schema": SCHEMA, "format": 1, "pass": True, "staticPass": True,
              "toolSha256": sha(__file__), "dependencySha256": {
                  "glb.py": sha(Path(__file__).with_name("glb.py")),
                  "pbr_recipe_audit.py": sha(Path(__file__).with_name("pbr_recipe_audit.py"))},
              "inventorySha256": sha(inventory_path), "paintManifestSha256": sha(paint_manifest_path),
              "sourcePack": str(source), "candidatePack": str(candidate), "candidateFiles": pack_files(candidate),
              "sourceModelSha256": source_hashes, "models": rows, "repaintHunters": sorted(repaint_hunters),
              "canonicalOrmMaterials": orm_materials, "limits": LIMITS, "registeredRecipeReplay": recipe_replay,
              "visualAcceptance": "pending separate model-bound bitmap and maps-on runtime review",
              "runtimeAcceptance": "pending fresh exact-candidate desktop/mobile receipts",
              "physicalAndroidAcceptance": "deferred by user",
              "scope": "Independent static geometry/native/UV/pixel/alpha/ownership/provenance and canonical ORM checks, plus independent fixed-formula Source registration, strong-edge/marking mask, generated albedo composite, bounded finish signal and ORM material-class recipe replay. Non-affine spatial change distinguishes repaint evidence from scalar tuning or upscaling; it does not prove attractive artwork, removal of every painted light, or physically measured surface values. Saved-image lighting comparisons and runtime validation remain required."}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2)+"\n")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", required=True)
    parser.add_argument("--paint-manifest", required=True)
    parser.add_argument("--candidate", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    result = audit(args.inventory, args.paint_manifest, args.candidate, args.output)
    print(json.dumps({"pass": result["pass"], "models": len(result["models"]),
                      "repaintHunters": result["repaintHunters"], "canonicalOrmMaterials": result["canonicalOrmMaterials"],
                      "visualAcceptance": result["visualAcceptance"]}))


if __name__ == "__main__":
    main()
