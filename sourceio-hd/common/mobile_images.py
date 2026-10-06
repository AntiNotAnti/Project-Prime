"""Channel-aware bounded images and authored mips for character mobile tiers.

Only accepted embedded image payloads are read. Atlas layout, UVs and material
identities are not regenerated. Legacy Source gloss maps convert glTF B/G to
the renderer R/G packing. Explicit ORM maps retain linear RGB and their A=0
encoding marker. Authored companion factors are applied exactly once.
"""
import io
import math

import numpy as np
from PIL import Image

ALGORITHM = "sourceio-mobile-linear-area-mips-orm-v2"
CHANNELS = ("albedo", "normal", "material", "emissive")


def srgb_to_linear(value):
    return np.where(value <= .04045, value / 12.92, ((value + .055) / 1.055) ** 2.4)


def linear_to_srgb(value):
    value = np.maximum(value, 0)
    return np.where(value <= .0031308, value * 12.92, 1.055 * value ** (1 / 2.4) - .055)


def _factor(value, maximum=1):
    value = float(value)
    if not math.isfinite(value) or not 0 <= value <= maximum:
        raise ValueError(f"Material factor outside [0,{maximum}]: {value}")
    return value


def usages(material):
    """Yield mutable texture-info objects and their complete conversion meaning."""
    pbr = material.get("pbrMetallicRoughness", {})
    runtime = material.get("extras", {}).get("projectPrimeRuntimeMaps", False)
    if not isinstance(runtime, bool):
        raise ValueError("projectPrimeRuntimeMaps must be boolean")
    encoding = material.get("extras", {}).get("projectPrimeMaterialEncoding")
    if encoding not in (None, "orm"):
        raise ValueError("Unknown Project Prime character material encoding")
    opaque = material.get("alphaMode", "OPAQUE") == "OPAQUE"
    normal_scale = _factor(material.get("normalTexture", {}).get("scale", 1), 100)
    metallic = _factor(pbr.get("metallicFactor", 1))
    roughness = _factor(pbr.get("roughnessFactor", 1))
    emission = material.get("emissiveFactor", [0, 0, 0])
    if not isinstance(emission, list) or len(emission) != 3:
        raise ValueError("emissiveFactor must have three values")
    emission = [_factor(value) for value in emission]
    if runtime and (("normalTexture" in material and normal_scale != 1)
                    or ("metallicRoughnessTexture" in pbr and (metallic != 1 or roughness != 1))
                    or ("emissiveTexture" in material and emission != [1, 1, 1])):
        raise ValueError("Preconverted runtime maps must have identity factors")
    references = [(pbr.get("baseColorTexture"), "albedo"),
                  (material.get("normalTexture"), "normal"),
                  (pbr.get("metallicRoughnessTexture"), "material"),
                  (material.get("emissiveTexture"), "emissive")]
    recolors = material.get("extras", {}).get("projectPrimeRecolors", {})
    if not isinstance(recolors, dict):
        raise ValueError("Character recolors must be an object")
    if any(str(key) not in ["1", "2", "3", "4", "5"] for key in recolors):
        raise ValueError("Character recolors require native suit indices 1–5")
    references.extend((value, "albedo") for value in recolors.values())
    if any(info is not None for info, channel in references if channel != "albedo") and not pbr.get("baseColorTexture"):
        raise ValueError("Character companion maps require an albedo")
    for info, channel in references:
        if info is None:
            continue
        if not isinstance(info, dict) or info.get("texCoord", 0) != 0 or info.get("extensions"):
            raise ValueError("Character texture must use untransformed TEXCOORD_0")
        meaning = {"channel": channel, "opaque": opaque if channel == "albedo" else True}
        if channel == "normal":
            meaning["normalScale"] = normal_scale
        elif channel == "material":
            meaning.update(runtimeEncoded=runtime, metallicFactor=metallic, roughnessFactor=roughness)
            if encoding == "orm":
                meaning.update(materialEncoding="orm", markerAlpha=0,
                               colorSpace="linear data; RGB remains meaningful under A=0")
        elif channel == "emissive":
            meaning["emissiveFactor"] = emission
        yield info, meaning


def identity_factors(material):
    """Declare the offline runtime conversion without double-applying factors."""
    if not any(key in material for key in ("normalTexture", "emissiveTexture")) and not material.get("pbrMetallicRoughness", {}).get("metallicRoughnessTexture"):
        return
    material.setdefault("extras", {})["projectPrimeRuntimeMaps"] = True
    if "normalTexture" in material:
        material["normalTexture"]["scale"] = 1
    if "metallicRoughnessTexture" in material.get("pbrMetallicRoughness", {}):
        material["pbrMetallicRoughness"]["metallicFactor"] = 1
        material["pbrMetallicRoughness"]["roughnessFactor"] = 1
    if "emissiveTexture" in material:
        material["emissiveFactor"] = [1, 1, 1]


def _normalize(vectors):
    lengths = np.linalg.norm(vectors, axis=2, keepdims=True)
    safe = np.divide(vectors, np.maximum(lengths, 1e-8))
    return np.where(lengths < 1e-8, np.array([0, 0, 1], dtype=np.float32), safe)


def _resize(values, size, method):
    """Filter independent floating channels; never filter data maps as sRGB."""
    return np.stack([np.asarray(Image.fromarray(values[..., i]).resize(size, method), dtype=np.float32)
                     for i in range(values.shape[2])], axis=2)


def prepare(payload, meaning, maximum):
    """Return RGBA8 mips. Rectangular dimensions and transparency survive."""
    channel = meaning["channel"]
    if channel not in CHANNELS or not isinstance(maximum, int) or maximum < 1:
        raise ValueError("Invalid mobile channel/dimension cap")
    with Image.open(io.BytesIO(payload)) as image:
        original = image.size
        if max(original) > 16384:
            raise ValueError("Embedded character image exceeds source safety cap")
        rgba = np.asarray(image.convert("RGBA"), dtype=np.float32) / 255
    ratio = min(1, maximum / max(original))
    width, height = (max(1, int(round(value * ratio))) for value in original)
    rgb, alpha = rgba[..., :3], rgba[..., 3:4]
    if meaning["opaque"]:
        alpha = np.ones_like(alpha)
    if channel in ("albedo", "emissive"):
        linear = srgb_to_linear(rgb)
        if channel == "emissive":
            linear *= np.array(meaning["emissiveFactor"], dtype=np.float32)
        working = np.concatenate([linear * alpha, alpha], axis=2)
    elif channel == "normal":
        vectors = rgb * 2 - 1
        vectors[..., :2] *= meaning["normalScale"]
        working = _normalize(vectors)
    else:
        working = rgb.copy()
        if meaning.get("materialEncoding") == "orm":
            if np.any(rgba[..., 3] != 0):
                raise ValueError("Canonical ORM source must carry A=0 at every texel")
            if not meaning["runtimeEncoded"]:
                working[..., 1] *= meaning["roughnessFactor"]
                working[..., 2] *= meaning["metallicFactor"]
        elif not meaning["runtimeEncoded"]:
            working[..., 0] = rgb[..., 2] * meaning["metallicFactor"]
            working[..., 1] = rgb[..., 1] * meaning["roughnessFactor"]
            working[..., 2] = 0
    if (width, height) != original:
        working = _resize(working, (width, height), Image.Resampling.LANCZOS)
    levels = []
    while True:
        if channel in ("albedo", "emissive"):
            mip_alpha = np.clip(working[..., 3:4], 0, 1)
            linear = np.divide(np.maximum(working[..., :3], 0), np.maximum(mip_alpha, 1e-8))
            linear = np.where(mip_alpha < 1e-8, 0, linear)
            data = np.concatenate([linear_to_srgb(linear), mip_alpha], axis=2)
        elif channel == "normal":
            vectors = _normalize(working)
            working = vectors
            data = np.concatenate([vectors * .5 + .5, np.ones((height, width, 1), dtype=np.float32)], axis=2)
        else:
            marker = 0 if meaning.get("materialEncoding") == "orm" else 1
            data = np.concatenate([working, np.full((height, width, 1), marker, dtype=np.float32)], axis=2)
        levels.append(np.rint(np.clip(data, 0, 1) * 255).astype(np.uint8))
        if width == height == 1:
            break
        width, height = max(1, width // 2), max(1, height // 2)
        working = _resize(working, (width, height), Image.Resampling.BOX)
    return levels, {"originalSize": list(original), "size": [levels[0].shape[1], levels[0].shape[0]],
                    "mips": len(levels), "meaning": meaning,
                    "colorMipFiltering": "premultiplied linear light" if channel in ("albedo", "emissive") else "noncolor data",
                    "normalMipRenormalization": channel == "normal",
                    "atlasLayoutChanged": False}
