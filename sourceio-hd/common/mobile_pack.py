"""Generate a NEW mixed/device-only roster texture tier; never install it.

inventory records exact input bytes, not visual acceptance. build requires that
lock and a fresh output directory. Existing desktop/Samus assets are copied
byte for byte. Every converted material use has its own semantic identity, so
shared images with differing factors/alpha rules cannot accidentally collide.
"""
import argparse
import copy
import hashlib
import json
import shutil
import struct
from pathlib import Path, PurePosixPath

from glb import image_bytes, load
from mobile_encoder import (ENCODING_POLICY, KTX_REVISION, build as build_encoder,
                            check_protocol, encode_guarded)
from mobile_images import ALGORITHM, CHANNELS, identity_factors, prepare, usages


def sha_bytes(data):
    return hashlib.sha256(data).hexdigest()


def sha_file(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        while block := stream.read(1024 * 1024):
            digest.update(block)
    return digest.hexdigest()


def within(root, relative):
    part = PurePosixPath(relative)
    if not relative or "\\" in relative or part.is_absolute() or any(x in (".", "..") for x in relative.split("/")):
        raise ValueError(f"Unsafe pack path: {relative}")
    root = Path(root).resolve()
    path = root.joinpath(*part.parts)
    if not path.resolve().is_relative_to(root):
        raise ValueError(f"Pack path leaves its root: {relative}")
    return path


def pack_files(root):
    root = Path(root).resolve()
    result = {}
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise ValueError(f"Pack contains a symlink: {path}")
        if path.is_file():
            result[path.relative_to(root).as_posix()] = sha_file(path)
    if "characters.json" not in result:
        raise ValueError("Pack is missing characters.json")
    return result


def manifest(root):
    value = json.loads((Path(root) / "characters.json").read_text())
    if value.get("format") != 1 or not isinstance(value.get("models"), list) or not value["models"]:
        raise ValueError("Expected a nonempty character manifest format 1")
    keys = set()
    for entry in value["models"]:
        key = (entry["hunter"], entry["part"], entry.get("lod", 0))
        if key in keys:
            raise ValueError(f"Duplicate character entry: {key}")
        keys.add(key)
        for field in ("model", "mobileModel"):
            if entry.get(field) and not within(root, entry[field]).is_file():
                raise ValueError(f"Missing manifest asset: {entry[field]}")
    return value


def snapshot(root, destination):
    root = Path(root).resolve()
    value = manifest(root)
    lock = {"format": 1, "sourcePack": str(root), "sourcePackId": value.get("id"),
            "files": pack_files(root),
            "scope": "Exact input snapshot only. This lock is not gameplay, visual or Android acceptance."}
    destination = Path(destination).resolve()
    if destination.is_relative_to(root):
        raise ValueError("The input lock must be outside the source pack")
    destination.parent.mkdir(parents=True, exist_ok=True)
    with destination.open("x") as stream:
        json.dump(lock, stream, indent=2)
        stream.write("\n")
    return lock


def check_lock(root, lock):
    current = pack_files(root)
    if lock.get("format") != 1 or lock.get("files") != current:
        raise ValueError("Source pack differs from the explicit input lock")
    if lock.get("sourcePackId") != manifest(root).get("id"):
        raise ValueError("Source pack manifest identity differs from its lock")
    return current


def check_tier(tier):
    if (tier.get("format") != 1 or tier.get("ktxRevision") != KTX_REVISION
            or tier.get("encodingPolicy") != ENCODING_POLICY or tier.get("uastcLevels") != [2, 4]
            or tier.get("losslessFallback") != "rgba8-zstd" or tier.get("zstdLevel") != 9):
        raise ValueError("Unsupported tier/encoder contract")
    if tier.get("compressionAcceptance") != {"minimumRgbPsnrDb": 35, "minimumAlphaPsnrDb": 35,
            "maximumMeanNormalAngleDegrees": 1.5, "maximumP99NormalAngleDegrees": 6}:
        raise ValueError("The guarded mobile fidelity budgets must remain unchanged")
    if set(tier.get("maximumDimensions", {})) != set(CHANNELS):
        raise ValueError("Tier requires caps for all four image channels")
    if any(not isinstance(value, int) or not 1 <= value <= 4096 for value in tier["maximumDimensions"].values()):
        raise ValueError("Mobile image caps must be integers in [1,4096]")
    known = {"Samus", "Spire", "Noxus", "Kanden", "Sylux", "Trace", "Weavel"}
    if any(not isinstance(tier.get(field),list) or any(not isinstance(name,str) for name in tier[field])
           or len(set(tier[field])) != len(tier[field]) for field in ("preserveHunters","optimizeHunters")):
        raise ValueError("Hunter policies must be explicit lists without duplicate names")
    preserved, optimized = set(tier["preserveHunters"]), set(tier["optimizeHunters"])
    if preserved & optimized or not optimized or preserved | optimized != known:
        raise ValueError("Hunter policies must be disjoint and cover the complete known roster")
    within(Path("/"), tier["mobilePrefix"])


def ktx_metadata(payload):
    if payload[:12] != bytes.fromhex("ab4b5458203230bb0d0a1a0a") or len(payload) < 80:
        raise ValueError("Encoder did not produce a KTX2 payload")
    fields = struct.unpack_from("<9I", payload, 12)
    vk, _, width, height, depth, layers, faces, levels, compression = fields
    if vk not in (0, 37, 43) or not width or not height or depth or layers or faces != 1 or compression != 2:
        raise ValueError("Expected 2D UASTC or lossless RGBA8 KTX2 with Zstd")
    if levels != max(width, height).bit_length():
        raise ValueError("KTX2 does not contain the complete authored mip chain")
    return {"width": width, "height": height, "levels": levels, "vkFormat": vk,
            "basis": vk == 0, "encoding": "uastc" if vk == 0 else "rgba8-zstd"}


def ktx_shape(payload):
    shape = ktx_metadata(payload)
    return shape["width"], shape["height"], shape["levels"]


def conversion_identity(payload, meaning, tier):
    return {"sourceSha256": sha_bytes(payload), "meaning": meaning,
            "maximum": tier["maximumDimensions"][meaning["channel"]], "algorithm": ALGORITHM,
            "encodingPolicy": tier["encodingPolicy"], "compressionAcceptance": tier["compressionAcceptance"]}


def write_glb(path, doc, blob):
    encoded = json.dumps(doc, separators=(",", ":")).encode()
    encoded += b" " * (-len(encoded) % 4)
    blob = bytes(blob) + b"\0" * (-len(blob) % 4)
    payload = (struct.pack("<4sII", b"glTF", 2, 28 + len(encoded) + len(blob))
               + struct.pack("<I4s", len(encoded), b"JSON") + encoded
               + struct.pack("<I4s", len(blob), b"BIN\0") + blob)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("xb") as stream:
        stream.write(payload)


def texture_source(texture):
    if texture.get("extensions"):
        extensions = texture["extensions"]
        if set(extensions) != {"KHR_texture_basisu"}:
            raise ValueError("Unsupported texture extension")
        return extensions["KHR_texture_basisu"]["source"]
    return texture["source"]


class ImageCache:
    def __init__(self, root, encoder, tier):
        self.root = root
        self.encoder = encoder
        self.tier = tier
        self.records = {}
        self.contents = {}
        root.mkdir(parents=True, exist_ok=False)

    def convert(self, payload, meaning):
        maximum = self.tier["maximumDimensions"][meaning["channel"]]
        identity = conversion_identity(payload, meaning, self.tier)
        key = sha_bytes(json.dumps(identity, sort_keys=True, separators=(",", ":")).encode())
        if key not in self.records:
            levels, record = prepare(payload, meaning, maximum)
            base = self.root / key
            from PIL import Image
            Image.fromarray(levels[0]).save(base.with_suffix(".png"))
            # Keep uncompressed authored mips as replayable audit references.
            raw = base.with_suffix(".mips")
            raw.write_bytes(b"".join(level.tobytes() for level in levels))
            target = base.with_suffix(".ktx2")
            policy = encode_guarded(self.encoder, levels, target, meaning["channel"] in ("albedo", "emissive"),
                                    meaning["channel"], self.tier["compressionAcceptance"])
            data = target.read_bytes()
            width, height, count = ktx_shape(data)
            if [width, height] != record["size"] or count != len(levels) or len(data) > 32 * 1024 * 1024:
                raise ValueError("Encoded image shape/size differs from its channel contract")
            dimensions = [(level.shape[1], level.shape[0]) for level in levels]
            record.update(identity, **policy, key=key, encodedSha256=sha_bytes(data), encodedBytes=len(data),
                          referenceMipSha256=sha_file(raw), referencePngSha256=sha_file(base.with_suffix(".png")),
                          mipSizes=[list(size) for size in dimensions],
                          gpuAstc4x4Bytes=sum(((w + 3) // 4) * ((h + 3) // 4) * 16 for w, h in dimensions),
                          gpuRgbaBytes=sum(w * h * 4 for w, h in dimensions))
            record["gpuSelectedAstcAdapterBytes"] = (record["gpuRgbaBytes"] if policy["encodingMode"] == "rgba8-zstd"
                                                    else record["gpuAstc4x4Bytes"])
            self.records[key] = record
            self.contents[key] = data
        return key, self.contents[key]


def convert_glb(source, destination, cache):
    old, old_blob = load(source)
    if len(old.get("buffers", [])) != 1 or old["buffers"][0].get("uri"):
        raise ValueError("Only embedded single-buffer character GLBs are supported")
    if any("uri" in image for image in old.get("images", [])):
        raise ValueError("Mobile conversion requires embedded input images")
    doc = copy.deepcopy(old)
    old_textures = old.get("textures", [])
    texture_mapping = {}
    images, textures, image_indices, image_records = [], [], {}, []
    for material in doc.get("materials", []):
        for info, meaning in usages(material):
            index = info["index"]
            if not isinstance(index, int) or not 0 <= index < len(old_textures):
                raise ValueError("Material texture index is invalid")
            source_index = texture_source(old_textures[index])
            if not isinstance(source_index, int) or not 0 <= source_index < len(old.get("images", [])):
                raise ValueError("Texture image index is invalid")
            key, payload = cache.convert(image_bytes(old, old_blob, source_index), meaning)
            if key not in image_indices:
                # Equal final payloads share one embedded view even when different
                # input semantics happened to produce the same pixels.
                payload_hash = sha_bytes(payload)
                image_index = next((i for i, image in enumerate(images) if image["name"] == payload_hash), None)
                if image_index is None:
                    image_index = len(images)
                    images.append({"name": payload_hash, "mimeType": "image/ktx2"})
                image_indices[key] = image_index
            texture_key = (index, key)
            if texture_key not in texture_mapping:
                replacement = copy.deepcopy(old_textures[index])
                replacement.pop("source", None)
                replacement.pop("extensions", None)
                if cache.records[key]["encodingMode"] == "rgba8-zstd":
                    # Raw RGBA KTX2 is an existing Project Prime MIME path.
                    # It is not Basis and must not claim KHR_texture_basisu.
                    replacement["source"] = image_indices[key]
                else:
                    replacement["extensions"] = {"KHR_texture_basisu": {"source": image_indices[key]}}
                texture_mapping[texture_key] = len(textures)
                textures.append(replacement)
            info["index"] = texture_mapping[texture_key]
            image_records.append({"nativeMaterial": material.get("name"), "sourceImage": source_index,
                                  "sourceTexture": index, "mobileTexture": info["index"], "imageKey": key,
                                  "meaning": meaning})
        identity_factors(material)
    if not images:
        raise ValueError("Character GLB contains no runtime material image uses")
    image_views = {image["bufferView"] for image in old.get("images", [])}
    views, view_mapping, binary = [], {}, bytearray()

    def append(data, template=None):
        binary.extend(b"\0" * (-len(binary) % 4))
        value = dict(template or {}, buffer=0, byteOffset=len(binary), byteLength=len(data))
        views.append(value)
        binary.extend(data)
        return len(views) - 1

    for i, view in enumerate(old.get("bufferViews", [])):
        if i in image_views:
            continue
        start = view.get("byteOffset", 0)
        if view.get("buffer", 0) != 0 or start < 0 or start + view["byteLength"] > len(old_blob):
            raise ValueError("Input buffer view exceeds its embedded data")
        view_mapping[i] = append(old_blob[start:start + view["byteLength"]], view)
    for accessor in doc.get("accessors", []):
        if "sparse" in accessor or accessor.get("bufferView") not in view_mapping:
            raise ValueError("Character accessor is sparse or shares an image buffer view")
        accessor["bufferView"] = view_mapping[accessor["bufferView"]]
    for image_index, image in enumerate(images):
        key = next(key for key, i in image_indices.items() if i == image_index)
        image["bufferView"] = append(cache.contents[key])
    doc.update(images=images, textures=textures, bufferViews=views, buffers=[{"byteLength": len(binary)}])
    for field in ("extensionsUsed", "extensionsRequired"):
        values = set(doc.get(field, [])) - {"KHR_texture_basisu"}
        if any("KHR_texture_basisu" in texture.get("extensions", {}) for texture in textures):
            values.add("KHR_texture_basisu")
        if values:
            doc[field] = sorted(values)
        else:
            doc.pop(field, None)
    write_glb(destination, doc, binary)
    return {"sourceSha256": sha_file(source), "mobileSha256": sha_file(destination),
            "sourceBytes": source.stat().st_size, "mobileBytes": destination.stat().st_size,
            "sourceImages": len(old.get("images", [])), "embeddedMobileImages": len(images),
            "imageUses": image_records}


def build(source, lock_path, tier_path, output, encoder=None):
    source, output = Path(source).resolve(), Path(output).resolve()
    lock_path, tier_path = Path(lock_path).resolve(), Path(tier_path).resolve()
    lock_bytes, tier_bytes = lock_path.read_bytes(), tier_path.read_bytes()
    lock, tier = json.loads(lock_bytes), json.loads(tier_bytes)
    check_tier(tier)
    original_files = check_lock(source, lock)
    code_inputs = {path.name: sha_file(path) for path in Path(__file__).parent.glob("mobile_*.py")}
    if output.is_relative_to(source) or source.is_relative_to(output) or output.exists():
        raise ValueError("Use a fresh candidate output outside the source pack")
    output.mkdir(parents=True, exist_ok=False)
    try:
        (output / "source-lock.json").write_bytes(lock_bytes)
        (output / "tier.json").write_bytes(tier_bytes)
        if encoder:
            encoder = Path(encoder).resolve()
            encoder_receipt = {"encoder": str(encoder), "encoderSha256": sha_file(encoder),
                               "sourceSha256": sha_bytes(__import__("mobile_encoder").SOURCE.encode()),
                               "encodingPolicy": ENCODING_POLICY, "protocol": check_protocol(encoder),
                               "scope": "Caller-supplied rectangular encoder; verify it uses the pinned KTX runtime."}
        else:
            encoder, encoder_receipt = build_encoder(output / "encoder")
        mixed = output / "starter"
        shutil.copytree(source, mixed)
        result_manifest = manifest(mixed)
        source_manifest = copy.deepcopy(result_manifest)
        result_manifest["id"] = tier["tierId"] + "-mixed"
        cache = ImageCache(output / "image-cache", encoder, tier)
        rows = []
        optimized, preserved = set(tier["optimizeHunters"]), set(tier["preserveHunters"])
        for entry in result_manifest["models"]:
            if entry["hunter"] in preserved:
                if not entry.get("mobileModel"):
                    raise ValueError(f"Preserved hunter is missing its existing mobile tier: {entry['hunter']}")
                rows.append({"hunter": entry["hunter"], "part": entry["part"], "lod": entry.get("lod", 0),
                             "model": entry["model"], "mobileModel": entry["mobileModel"], "preserved": True,
                             "sourceSha256": sha_file(within(source, entry["model"])),
                             "mobileSha256": sha_file(within(source, entry["mobileModel"]))})
            elif entry["hunter"] in optimized:
                target = tier["mobilePrefix"] + "/" + entry["model"]
                dest = within(mixed, target)
                if dest.exists():
                    raise ValueError(f"New tier would overwrite an input path: {target}")
                result = convert_glb(within(source, entry["model"]), dest, cache)
                entry["mobileModel"] = target
                rows.append(dict(result, hunter=entry["hunter"], part=entry["part"], lod=entry.get("lod", 0),
                                 model=entry["model"], mobileModel=target, preserved=False))
            else:
                raise ValueError(f"Hunter is not declared by this tier: {entry['hunter']}")
        (mixed / "characters.json").write_text(json.dumps(result_manifest, indent=2) + "\n")
        android = output / "android-pack"
        android.mkdir()
        device_manifest = copy.deepcopy(result_manifest)
        device_manifest["id"] = tier["tierId"] + "-android-only"
        for entry in device_manifest["models"]:
            selected = entry.pop("mobileModel")
            dest = within(android, entry["model"])
            dest.parent.mkdir(parents=True, exist_ok=True)
            if dest.exists() and dest.read_bytes() != within(mixed, selected).read_bytes():
                raise ValueError("Conflicting canonical device model paths")
            shutil.copy2(within(mixed, selected), dest)
        (android / "characters.json").write_text(json.dumps(device_manifest, indent=2) + "\n")
        if check_lock(source, lock) != original_files or lock_path.read_bytes() != lock_bytes or tier_path.read_bytes() != tier_bytes:
            raise ValueError("Source/config/lock changed during conversion")
        if sha_file(encoder) != encoder_receipt["encoderSha256"]:
            raise ValueError("Encoder changed during conversion")
        if code_inputs != {path.name: sha_file(path) for path in Path(__file__).parent.glob("mobile_*.py")}:
            raise ValueError("Mobile pipeline source changed during conversion")
        report = {"format": 1, "buildCompleted": True, "sourcePack": str(source),
                  "sourcePackId": source_manifest.get("id"), "sourceLockSha256": sha_bytes(lock_bytes),
                  "tierConfigSha256": sha_bytes(tier_bytes), "encoder": encoder_receipt,
                  "pipelineInputs": code_inputs, "algorithm": ALGORITHM, "models": rows,
                  "uniqueImageConversions": list(cache.records.values()),
                  "encodingPolicy": ENCODING_POLICY,
                  "mixedPackFiles": pack_files(mixed), "androidPackFiles": pack_files(android),
                  "physicalAndroidAcceptance": "pending", "gpuAcceptance": "pending",
                  "scope": "Offline texture-tier build only. Compression/geometry audit and real-render/device acceptance are separate. Existing rectangular atlases are resized, not repacked. Embedded payload repetition across GLBs remains; runtime interning handles identical images."}
        (output / "build-result.json").write_text(json.dumps(report, indent=2) + "\n")
        from mobile_audit import audit
        result = audit(output)
        return {"output": str(output), "models": len(rows), "newUniqueImages": len(cache.records), "offlineAuditPass": result["pass"],
                "gpuAcceptance": "pending", "physicalAndroidAcceptance": "pending"}
    except BaseException as error:
        (output / "build-failure.json").write_text(json.dumps({"buildCompleted": False, "error": str(error)}, indent=2) + "\n")
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    inventory = sub.add_parser("inventory", help="Snapshot bytes of the reviewed input pack; does not declare acceptance")
    inventory.add_argument("--source-pack", required=True)
    inventory.add_argument("--output", required=True, help="Fresh JSON lock outside the input pack")
    candidate = sub.add_parser("build", help="Build a fresh candidate; never installs or accesses a device")
    candidate.add_argument("--source-pack", required=True)
    candidate.add_argument("--source-lock", required=True)
    candidate.add_argument("--tier", default=str(Path(__file__).resolve().parents[1] / "mobile-tier.json"))
    candidate.add_argument("--output", required=True)
    candidate.add_argument("--encoder", help="Optional rectangular encoder built from mobile_encoder.SOURCE")
    args = parser.parse_args()
    if args.command == "inventory":
        result = snapshot(args.source_pack, args.output)
        print(json.dumps({"sourcePackId": result["sourcePackId"], "lockedFiles": len(result["files"]), "visualAcceptance": "not established by inventory"}))
    else:
        print(json.dumps(build(args.source_pack, args.source_lock, args.tier, args.output, args.encoder), indent=2))


if __name__ == "__main__":
    main()
