"""Independent byte/semantic audit of a candidate built by mobile_pack.py.

This audit does not render, install or access Android. Compression decode is an
explicit separate option. Neither option proves texture resize appearance,
Android driver compatibility, process residency or physical frame pacing.
"""
import argparse
import copy
import json
import math
import tempfile
from pathlib import Path

from glb import image_bytes, load
from mobile_images import ALGORITHM, identity_factors, prepare, usages


def _require(condition, description):
    if not condition:
        raise ValueError(description)


def _accessor_bytes(doc, blob, index):
    accessor = doc["accessors"][index]
    _require("sparse" not in accessor, "Sparse character accessor")
    view = doc["bufferViews"][accessor["bufferView"]]
    components = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}[accessor["type"]]
    component_size = {5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4}[accessor["componentType"]]
    size = components * component_size
    stride = view.get("byteStride", size)
    start = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    _require(view.get("buffer", 0) == 0 and stride >= size, "Invalid accessor buffer/stride")
    _require(start >= view.get("byteOffset", 0) and
             start + (max(0, accessor["count"] - 1)) * stride + (size if accessor["count"] else 0)
             <= view.get("byteOffset", 0) + view["byteLength"], "Accessor exceeds buffer view")
    return b"".join(blob[start + i * stride:start + i * stride + size] for i in range(accessor["count"]))


def _geometry(old, old_blob, new, new_blob):
    for key in ("nodes", "skins", "meshes", "scenes", "scene", "animations", "samplers", "asset"):
        _require(old.get(key) == new.get(key), f"Mobile conversion changed {key}")
    _require(len(old.get("accessors", [])) == len(new.get("accessors", [])), "Accessor count changed")
    for i, (a, b) in enumerate(zip(old.get("accessors", []), new.get("accessors", []))):
        _require({k: v for k, v in a.items() if k != "bufferView"} ==
                 {k: v for k, v in b.items() if k != "bufferView"}, f"Accessor metadata changed: {i}")
        _require(_accessor_bytes(old, old_blob, i) == _accessor_bytes(new, new_blob, i), f"Geometry/UV/bind bytes changed: {i}")
        av, bv = old["bufferViews"][a["bufferView"]], new["bufferViews"][b["bufferView"]]
        _require({k: v for k, v in av.items() if k != "byteOffset"} ==
                 {k: v for k, v in bv.items() if k != "byteOffset"}, f"Geometry buffer view metadata changed: {i}")


def _material_meanings(old, new):
    expected = copy.deepcopy(old)
    old_uses, new_uses = [], []
    for material in expected.get("materials", []):
        old_uses.extend(usages(material))
        identity_factors(material)
    for material in new.get("materials", []):
        new_uses.extend(usages(material))
    _require(len(old_uses) == len(new_uses), "Texture usage count changed")
    pairs = []
    for (old_info, meaning), (new_info, _) in zip(old_uses, new_uses):
        pairs.append((old_info["index"], new_info["index"], meaning))
        old_info["index"] = new_info["index"]
    _require(expected.get("materials", []) == new.get("materials", []), "Native materials, alpha modes, factors, recolors or flags changed unexpectedly")
    return pairs


def audit(root):
    from mobile_pack import check_lock, check_tier, ktx_shape, pack_files, sha_bytes, sha_file, texture_source, within
    root = Path(root).resolve()
    # An old PASS must not survive a failed replay of this independent audit.
    destination = root / "audit.json"
    destination.unlink(missing_ok=True)
    _require(not (root / "build-failure.json").exists(), "Candidate has a failed build receipt")
    result = json.loads((root / "build-result.json").read_text())
    _require(result.get("buildCompleted") is True, "Candidate build did not complete")
    lock_path, tier_path = root / "source-lock.json", root / "tier.json"
    lock, tier = json.loads(lock_path.read_text()), json.loads(tier_path.read_text())
    check_tier(tier)
    _require(sha_file(lock_path) == result["sourceLockSha256"] and sha_file(tier_path) == result["tierConfigSha256"], "Candidate input lock/tier hashes changed")
    _require(result["pipelineInputs"] == {path.name: sha_file(path) for path in Path(__file__).parent.glob("mobile_*.py")}, "Pipeline source differs from executed build")
    encoder_receipt = result["encoder"]
    encoder_path = Path(encoder_receipt.get("encoder", root / "encoder/mobile-encode-basis"))
    _require(sha_file(encoder_path) == encoder_receipt["encoderSha256"], "Encoder differs from executed build")
    if "ktxLibrary" in encoder_receipt:
        _require(sha_file(encoder_receipt["ktxLibrary"]) == encoder_receipt["ktxLibrarySha256"], "Pinned KTX library changed")
        _require(sha_file(root / "encoder/mobile-encode-basis.cpp") == encoder_receipt["encoderSourceSha256"], "Encoder source changed")
        _require(json.loads((root / "encoder/encoder.json").read_text()) == encoder_receipt, "Encoder receipt changed")
    source = Path(result["sourcePack"]).resolve()
    check_lock(source, lock)
    mixed, android = root / "starter", root / "android-pack"
    _require(pack_files(mixed) == result["mixedPackFiles"] and pack_files(android) == result["androidPackFiles"], "Candidate pack hashes changed")
    original = json.loads((source / "characters.json").read_text())
    current = json.loads((mixed / "characters.json").read_text())
    device = json.loads((android / "characters.json").read_text())
    _require(current["id"] == tier["tierId"] + "-mixed" and device["id"] == tier["tierId"] + "-android-only", "Unexpected tier identity")
    _require(len(original["models"]) == len(current["models"]) == len(device["models"]) == len(result["models"]), "Manifest entry count changed")
    for path, digest in lock["files"].items():
        if path != "characters.json":
            _require(sha_file(within(mixed, path)) == digest, f"Original desktop/preserved file changed: {path}")
    images = {image["key"]: image for image in result["uniqueImageConversions"]}
    _require(len(images) == len(result["uniqueImageConversions"]), "Duplicate conversion keys")
    for key, image in images.items():
        base = root / "image-cache" / key
        _require(sha_file(base.with_suffix(".ktx2")) == image["encodedSha256"], "Encoded image cache changed")
        _require(sha_file(base.with_suffix(".mips")) == image["referenceMipSha256"], "Authored reference mips changed")
        _require(sha_file(base.with_suffix(".png")) == image["referencePngSha256"], "Visual reference PNG changed")
        width, height, levels = ktx_shape(base.with_suffix(".ktx2").read_bytes())
        _require([width, height] == image["size"] and levels == image["mips"], "Encoded image shape differs from reference")
        _require(max(width, height) <= tier["maximumDimensions"][image["meaning"]["channel"]], "Channel cap exceeded")
        ow, oh = image["originalSize"]
        ratio = min(1, tier["maximumDimensions"][image["meaning"]["channel"]] / max(ow, oh))
        _require([width, height] == [max(1, round(ow * ratio)), max(1, round(oh * ratio))], "Rectangular atlas aspect fit changed")
        expected_sizes = [[max(1, width >> i), max(1, height >> i)] for i in range(levels)]
        _require(image["mipSizes"] == expected_sizes, "Authored mip shapes differ from KTX dimensions")
    rows, recomputed_images = [], set()
    for before, after, android_entry, receipt in zip(original["models"], current["models"], device["models"], result["models"]):
        expected_entry = copy.deepcopy(before)
        expected_entry["mobileModel"] = receipt["mobileModel"]
        _require(expected_entry == after, "Manifest contract changed beyond mobileModel")
        expected_device = copy.deepcopy(after)
        selected = expected_device.pop("mobileModel")
        _require(expected_device == android_entry, "Device-only manifest changed native contract")
        desktop, mobile = within(mixed, after["model"]), within(mixed, selected)
        _require(sha_file(desktop) == receipt["sourceSha256"] == sha_file(within(source, before["model"])), "Desktop model bytes changed")
        _require(sha_file(mobile) == receipt["mobileSha256"] == sha_file(within(android, android_entry["model"])), "Selected device payload differs from mixed tier")
        if receipt["preserved"]:
            _require(before == after and sha_file(mobile) == sha_file(within(source, before["mobileModel"])), "Preserved hunter changed")
            rows.append({"hunter": before["hunter"], "part": before["part"], "lod": before.get("lod", 0),
                         "preservedDesktopAndMobileBytes": True, "mobileSha256": receipt["mobileSha256"]})
            continue
        old, old_blob = load(desktop)
        new, new_blob = load(mobile)
        _geometry(old, old_blob, new, new_blob)
        pairs = _material_meanings(old, new)
        _require(len(pairs) == len(receipt["imageUses"]), "Image usage receipt count changed")
        used_images = set()
        for (old_index, new_index, meaning), use in zip(pairs, receipt["imageUses"]):
            old_texture, new_texture = old["textures"][old_index], new["textures"][new_index]
            expected_texture = copy.deepcopy(old_texture)
            old_source = texture_source(old_texture)
            expected_texture.pop("source", None)
            expected_texture["extensions"] = {"KHR_texture_basisu": {"source": texture_source(new_texture)}}
            _require(expected_texture == new_texture, "Texture sampler/properties changed")
            _require(use["meaning"] == meaning and use["sourceTexture"] == old_index and use["mobileTexture"] == new_index and use["sourceImage"] == old_source,
                     "Image semantic mapping differs from receipt")
            cached = images[use["imageKey"]]
            source_payload = image_bytes(old, old_blob, old_source)
            _require(cached["meaning"] == meaning and cached["sourceSha256"] == sha_bytes(source_payload), "Wrong source channel/factors supplied to conversion")
            identity = {"sourceSha256": sha_bytes(source_payload), "meaning": meaning,
                        "maximum": tier["maximumDimensions"][meaning["channel"]], "algorithm": ALGORITHM}
            _require(use["imageKey"] == sha_bytes(json.dumps(identity, sort_keys=True, separators=(",", ":")).encode()), "Conversion cache key differs from its exact semantics")
            if use["imageKey"] not in recomputed_images:
                levels, metadata = prepare(source_payload, meaning, identity["maximum"])
                import hashlib
                reference_digest = hashlib.sha256()
                for level in levels:
                    reference_digest.update(level.tobytes())
                _require(reference_digest.hexdigest() == cached["referenceMipSha256"], "Bounded pixels/mips differ from the declared Source material conversion")
                _require(all(cached.get(key) == value for key, value in metadata.items()), "Image metadata differs from independently recomputed preparation")
                recomputed_images.add(use["imageKey"])
            mobile_index = texture_source(new_texture)
            used_images.add(mobile_index)
            image = new["images"][mobile_index]
            _require(image["mimeType"] == "image/ktx2" and "uri" not in image, "Mobile image is not embedded KTX2")
            _require(sha_bytes(image_bytes(new, new_blob, mobile_index)) == cached["encodedSha256"], "Mobile material references the wrong encoded image")
        _require(used_images == set(range(len(new["images"]))), "Mobile GLB embeds unused images")
        _require("KHR_texture_basisu" in new.get("extensionsRequired", []), "Mobile GLB is missing its required extension")
        rows.append({"hunter": before["hunter"], "part": before["part"], "lod": before.get("lod", 0),
                     "geometryUVSkinAccessorBytesPreserved": True, "nativeTransformsPreserved": True,
                     "nativeMaterialSemanticsPreserved": True, "desktopBytesPreserved": True,
                     "runtimeMapsConvertedExactlyOnce": True, "imageUses": len(pairs),
                     "mobileSha256": receipt["mobileSha256"]})
    expected_device_files = {"characters.json"} | {entry["model"] for entry in device["models"]}
    _require(set(result["androidPackFiles"]) == expected_device_files, "Device pack contains duplicate desktop assets or extra files")
    _require(recomputed_images == set(images), "Candidate has unused conversion-cache records")
    unique_encoded = {image["encodedSha256"]: image for image in images.values()}
    report = {"format": 1, "pass": True, "buildResultSha256": sha_file(root / "build-result.json"),
              "models": rows, "newUniqueConversions": len(images), "newUniqueEncodedPayloads": len(unique_encoded),
              "newImagesEncodedBytes": sum(image["encodedBytes"] for image in unique_encoded.values()),
              "newImagesAstc4x4Bytes": sum(image["gpuAstc4x4Bytes"] for image in unique_encoded.values()),
              "newImagesRgbaBytes": sum(image["gpuRgbaBytes"] for image in unique_encoded.values()),
              "devicePackOnlySelectedModels": True, "gpuAcceptance": "pending", "physicalAndroidAcceptance": "pending",
              "scope": "Exact offline image/geometry/native-contract audit only. Memory sums are deduplicated image-block/texel estimates for NEW conversions, excluding preserved Samus. They are not process/driver memory or scene residency. Color resize quality and real shader appearance require visual acceptance."}
    destination.write_text(json.dumps(report, indent=2) + "\n")
    return report


def compression(root, encoder=None):
    """Decode every mip and compare with the exact bounded input to UASTC."""
    import numpy as np
    from mobile_encoder import decode
    from mobile_pack import sha_file
    root = Path(root).resolve()
    destination = root / "compression-audit.json"
    destination.unlink(missing_ok=True)
    byte_audit = audit(root)
    result = json.loads((root / "build-result.json").read_text())
    tier = json.loads((root / "tier.json").read_text())
    budget = tier["compressionAcceptance"]
    encoder = Path(encoder).resolve() if encoder else Path(result["encoder"].get("encoder", root / "encoder/mobile-encode-basis"))
    _require(sha_file(encoder) == result["encoder"]["encoderSha256"], "Compression audit encoder differs from the build")
    checks = []
    with tempfile.TemporaryDirectory(prefix="mobile-compression-", dir=root) as scratch:
        for image in result["uniqueImageConversions"]:
            base = root / "image-cache" / image["key"]
            reference = base.with_suffix(".mips").read_bytes()
            offset = 0
            for level, (width, height) in enumerate(image["mipSizes"]):
                count = width * height * 4
                expected = np.frombuffer(reference[offset:offset + count], dtype=np.uint8).reshape(height, width, 4).astype(np.float32) / 255
                offset += count
                raw = Path(scratch) / "decoded.raw"
                decode(encoder, base.with_suffix(".ktx2"), raw, level)
                _require(raw.stat().st_size == count, "Decoder returned the wrong mip size")
                actual = np.frombuffer(raw.read_bytes(), dtype=np.uint8).reshape(height, width, 4).astype(np.float32) / 255
                mse = float(np.mean((expected[..., :3] - actual[..., :3]) ** 2))
                alpha_mse = float(np.mean((expected[..., 3] - actual[..., 3]) ** 2))
                rgb_psnr = -10 * math.log10(max(mse, 1e-12))
                alpha_psnr = -10 * math.log10(max(alpha_mse, 1e-12))
                check = {"imageKey": image["key"], "channel": image["meaning"]["channel"], "mip": level,
                         "size": [width, height], "rgbPsnrDb": rgb_psnr, "alphaPsnrDb": alpha_psnr}
                if image["meaning"]["channel"] == "normal":
                    a, b = actual[..., :3] * 2 - 1, expected[..., :3] * 2 - 1
                    a /= np.maximum(np.linalg.norm(a, axis=2, keepdims=True), 1e-8)
                    b /= np.maximum(np.linalg.norm(b, axis=2, keepdims=True), 1e-8)
                    angles = np.degrees(np.arccos(np.clip(np.sum(a * b, axis=2), -1, 1)))
                    check.update(meanAngularErrorDegrees=float(np.mean(angles)), p99AngularErrorDegrees=float(np.percentile(angles, 99)))
                    _require(check["meanAngularErrorDegrees"] <= budget["maximumMeanNormalAngleDegrees"] and
                             check["p99AngularErrorDegrees"] <= budget["maximumP99NormalAngleDegrees"], "UASTC normal error exceeded the configured budget")
                _require(rgb_psnr >= budget["minimumRgbPsnrDb"] and alpha_psnr >= budget["minimumAlphaPsnrDb"], "UASTC image error exceeded the configured budget")
                checks.append(check)
            _require(offset == len(reference), "Authored mip reference has trailing data")
    audit(root)
    _require(sha_file(encoder) == result["encoder"]["encoderSha256"], "Decoder changed during compression audit")
    report = {"pass": True, "offlineByteAuditPass": byte_audit["pass"], "buildResultSha256": sha_file(root / "build-result.json"),
              "images": len(result["uniqueImageConversions"]), "mips": len(checks), "checks": checks,
              "worstRgbPsnrDb": min(check["rgbPsnrDb"] for check in checks),
              "worstAlphaPsnrDb": min(check["alphaPsnrDb"] for check in checks),
              "scope": "All authored mip UASTC RGBA decode versus exact bounded reference texels. This proves compression error only; no GPU or Android measurements or original-resolution resize-quality claim."}
    destination.write_text(json.dumps(report, indent=2) + "\n")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, help="Existing fresh mobile_pack candidate directory")
    parser.add_argument("--compression", action="store_true", help="Decode all UASTC images/mips without a GPU")
    parser.add_argument("--encoder", help="Same rectangular encoder as the build, when supplied externally")
    args = parser.parse_args()
    result = compression(args.output, args.encoder) if args.compression else audit(args.output)
    print(json.dumps({key: value for key, value in result.items() if key not in ("models", "checks")}, indent=2))


if __name__ == "__main__":
    main()
