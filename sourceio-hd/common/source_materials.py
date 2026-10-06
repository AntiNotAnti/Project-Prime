"""Bake authored Source detail layers into a bounded, periodic albedo texture.

The imported GLB contains base/normal maps but cannot serialize SourceIO's
secondary detail node groups. This module reads the original VMT contract.
It changes image content only: original UVs, topology and normal images stay
authoritative. Source mode 0 uses a linear base and a non-sRGB detail sample:
    linear_base * (1 - factor + 2 * detail * factor)
Reference: ValveSoftware/source-sdk-2013 common_ps_fxc.h TextureCombine,
and vertexlitgeneric_dx9_helper.cpp InitVertexLitGeneric_DX9 (detail sRGB).
"""
from __future__ import annotations

import hashlib
import io
import json
import math
import re
from pathlib import Path

import numpy as np
from PIL import Image

from glb import image_bytes, load

SEMANTICS_SOURCES = [
    "https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/materialsystem/stdshaders/common_ps_fxc.h#L634-L686",
    "https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/materialsystem/stdshaders/vertexlitgeneric_dx9_helper.cpp#L256-L286",
    "https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/materialsystem/stdshaders/skin_ps20b.fxc#L167-L179",
    "https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/materialsystem/stdshaders/skin_ps20b.fxc#L276-L281",
    "https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/materialsystem/stdshaders/skin_dx9_helper.cpp#L735-L830",
]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_vmt(path: Path) -> dict[str, str]:
    """Read the simple scalar/vector VMTs supplied by this model archive.

    Comments are stripped before tokenization; duplicate keys use the final
    authored value. Includes, proxies, patches and transformed detail layers
    are rejected instead of being silently approximated.
    """
    text = re.sub(r"//[^\r\n]*|/\*[\s\S]*?\*/", "", path.read_text())
    tokens = re.findall(r'"([^"\r\n]*)"|([^\s{}"]+)|([{}])', text)
    words = [a or b or c for a, b, c in tokens]
    if len(words) < 3 or words[1] != "{":
        raise ValueError(f"Invalid VMT: {path}")
    # This supplied archive's Sylux_Glow VMT has a complete scalar body but
    # omits its final brace. SourceIO accepts EOF; record the source defect.
    missing_close = words[-1] != "}"
    body = words[2:] if missing_close else words[2:-1]
    if "{" in body or "}" in body or words[0].casefold() == "patch":
        raise ValueError(f"Nested/patch VMT requires an explicit resolver: {path}")
    if len(body) % 2:
        raise ValueError(f"Unpaired VMT setting: {path}")
    values = {body[i].casefold(): body[i + 1] for i in range(0, len(body), 2)}
    values["shader"] = words[0]
    if missing_close:
        values["sourceSyntaxMissingClosingBrace"] = "true"
    return values


def _vector(value: str, default: tuple[float, ...]) -> tuple[float, ...]:
    if value is None:
        return default
    values = tuple(float(x) for x in re.findall(r"[-+]?(?:\d*\.\d+|\d+)(?:[eE][-+]?\d+)?", value))
    return values or default


def srgb_to_linear(rgb):
    rgb = np.asarray(rgb, dtype=np.float32)
    return np.where(rgb <= .04045, rgb / 12.92, ((rgb + .055) / 1.055) ** 2.4)


def linear_to_srgb(rgb):
    rgb = np.maximum(np.asarray(rgb, dtype=np.float32), 0)
    return np.where(rgb <= .0031308, rgb * 12.92, 1.055 * rgb ** (1 / 2.4) - .055)


def sample_periodic(image: Image.Image, u, v, *, srgb: bool = False):
    """Normalized bilinear repeat sampling in GLB/PIL top-to-bottom V space."""
    pixels = np.asarray(image.convert("RGBA"), dtype=np.float32) / 255
    if srgb:
        pixels[:, :, :3] = srgb_to_linear(pixels[:, :, :3])
    u, v = np.broadcast_arrays(np.asarray(u, dtype=np.float64), np.asarray(v, dtype=np.float64))
    x, y = np.mod(u, 1) * image.width - .5, np.mod(v, 1) * image.height - .5
    x0, y0 = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64)
    fx, fy = (x - x0)[..., None], (y - y0)[..., None]
    p00, p10 = pixels[y0 % image.height, x0 % image.width], pixels[y0 % image.height, (x0 + 1) % image.width]
    p01, p11 = pixels[(y0 + 1) % image.height, x0 % image.width], pixels[(y0 + 1) % image.height, (x0 + 1) % image.width]
    return (p00 * (1 - fx) + p10 * fx) * (1 - fy) + (p01 * (1 - fx) + p11 * fx) * fy


def combine_detail(base_linear, detail_linear_data, factor: float, tint=(1., 1., 1.)):
    """Source TCOMBINE_RGB_EQUALS_BASE_x_DETAILx2; alpha is unchanged."""
    result = np.asarray(base_linear).copy()
    result[..., :3] *= 1 - factor + 2 * np.asarray(detail_linear_data)[..., :3] * np.asarray(tint) * factor
    return result


class SourceMaterialLibrary:
    def __init__(self, material_root, decoded_root, hunter: str, *, max_dimension: int = 1024):
        self.material_root = Path(material_root).resolve()
        self.decoded_root = Path(decoded_root).resolve()
        self.hunter = hunter.casefold()
        self.max_dimension = int(max_dimension)
        if self.max_dimension < 64 or self.max_dimension > 4096:
            raise ValueError("Source material limit must be between 64 and 4096")
        self._files = {p.relative_to(self.material_root).as_posix().casefold(): p for p in self.material_root.rglob("*.vmt")}
        self._decoded = {p.relative_to(self.decoded_root).as_posix().casefold(): p for p in self.decoded_root.rglob("*.png")}
        self._specs = {}
        self.report = {"semanticsSources": SEMANTICS_SOURCES, "maxDimension": self.max_dimension,
                       "uvsChanged": False, "topologyChanged": False, "normalImagesChanged": False,
                       "materials": {}}

    @classmethod
    def from_config(cls, config, source_glb):
        material_root = config.get("sourceMaterialRoot")
        decoded_root = config.get("sourceDecodedTextureRoot")
        if not material_root or not decoded_root:
            source_glb = Path(source_glb).resolve()
            converted = next((p for p in source_glb.parents if p.name == "converted-sourceio"), None)
            if converted:
                material_root = material_root or converted.parent / "gmpublisher/materials"
                decoded_root = decoded_root or converted / "decoded-vtf"
        if not material_root or not decoded_root:
            raise ValueError("Source materials require sourceMaterialRoot and sourceDecodedTextureRoot in config")
        return cls(material_root, decoded_root, config["hunter"], max_dimension=config.get("sourceMaterialMaxDimension", 1024))

    def spec(self, material_name: str):
        if material_name in self._specs:
            return self._specs[material_name]
        relative = f"models/{self.hunter}/{material_name}.vmt".casefold()
        path = self._files.get(relative)
        if path is None:
            # A genuinely synthetic intermediate has no original VMT; callers
            # retain its embedded GLB maps and audit this explicit exception.
            result = {"sourceMaterial": material_name, "vmt": None, "detail": None,
                      "selfIllum": False, "originalVmtMissing": True}
        else:
            values = read_vmt(path)
            detail_path = None
            if values.get("$detail"):
                reference = values["$detail"].replace("\\", "/").casefold()
                reference = reference.removesuffix(".vtf").removesuffix(".png") + ".png"
                detail_path = self._decoded.get(reference)
                if detail_path is None:
                    raise FileNotFoundError(f"Missing authored detail map {reference}: {path}")
            scale = _vector(values.get("$detailscale"), (4., 4.))
            scale = (scale[0], scale[0]) if len(scale) == 1 else scale[:2]
            mode = int(float(values.get("$detailblendmode", "0")))
            factor = float(values.get("$detailblendfactor", "1"))
            tint = _vector(values.get("$detailtint"), (1., 1., 1.))
            tint = (tint[0],) * 3 if len(tint) == 1 else tint[:3]
            if detail_path and (mode != 0 or any(abs(x - round(x)) > 1e-9 or x <= 0 for x in scale)):
                raise ValueError(f"Only authored mode 0 positive integer-period detail is supported: {path}")
            if detail_path and "$detailtexturetransform" in values:
                raise ValueError(f"Transformed detail needs explicit phase handling: {path}")
            if not 0 <= factor <= 1 or len(tint) != 3 or any(not math.isfinite(x) or x < 0 for x in tint):
                raise ValueError(f"Invalid detail factors: {path}")
            result = {"sourceMaterial": material_name, "vmt": str(path), "vmtSha256": sha256(path),
                      "parameters": values, "detail": str(detail_path) if detail_path else None,
                      "detailSha256": sha256(detail_path) if detail_path else None,
                      "detailScale": list(scale), "detailBlendMode": mode,
                      "detailBlendFactor": factor, "detailTint": list(tint),
                      "integerUvFoldPhaseInvariant": bool(detail_path),
                      "selfIllum": float(values.get("$selfillum", "0")) != 0,
                      "selfIllumTint": list(_vector(values.get("$selfillumtint"), (1., 1., 1.)))}
        self._specs[material_name] = result
        self.report["materials"][material_name] = dict(result)
        return result

    def albedo(self, material, embedded_albedo: Image.Image, embedded_normal: Image.Image | None = None):
        name = material["name"] if isinstance(material, dict) else material
        spec = self.spec(name)
        base = embedded_albedo.convert("RGBA")
        audit = self.report["materials"][name]
        audit.update(baseSize=list(base.size), basePixelSha256=hashlib.sha256(base.tobytes()).hexdigest())
        if embedded_normal is not None:
            audit["normalSize"] = list(embedded_normal.size)
            audit["normalPixelSha256"] = hashlib.sha256(embedded_normal.convert("RGBA").tobytes()).hexdigest()
        if not spec["detail"]:
            audit.update(detailBaked=False, outputSize=list(base.size), baseTexelsUnchanged=True)
            return base
        detail = Image.open(spec["detail"]).convert("RGBA")
        scale = spec["detailScale"]
        required = [max(base.size[i], embedded_normal.size[i] if embedded_normal else 0,
                        detail.size[i] * int(scale[i])) for i in range(2)]
        # Aspect and dimensions remain powers of two for integer atlas channel
        # expansion. Cap sampling density, never the authored UV frequency.
        size = tuple(min(self.max_dimension, 1 << (int(x) - 1).bit_length()) for x in required)
        if any(size[i] < base.size[i] for i in range(2)):
            raise ValueError(f"Detail bake cap would degrade original albedo {name}: {base.size} -> {size}")
        output = np.empty((size[1], size[0], 4), dtype=np.uint8)
        # Process strips so all temporary arrays are bounded even at desktop cap.
        u = (np.arange(size[0], dtype=np.float64) + .5)[None, :] / size[0]
        maximum_error = 0.
        for y in range(0, size[1], 32):
            v = (np.arange(y, min(size[1], y + 32), dtype=np.float64) + .5)[:, None] / size[1]
            original = sample_periodic(base, u, v, srgb=True)
            detail_samples = sample_periodic(detail, u * scale[0], v * scale[1])
            composed = combine_detail(original, detail_samples, spec["detailBlendFactor"], spec["detailTint"])
            encoded = composed.copy()
            encoded[..., :3] = linear_to_srgb(composed[..., :3])
            output[y:y + len(v)] = np.round(np.clip(encoded, 0, 1) * 255).astype(np.uint8)
            maximum_error = max(maximum_error, float(np.abs(composed[..., 3] - original[..., 3]).max()))
        image = Image.fromarray(output, "RGBA")
        audit.update(detailBaked=True, detailSize=list(detail.size), idealUnboundedSize=required,
                     outputSize=list(size), outputPixelSha256=hashlib.sha256(image.tobytes()).hexdigest(),
                     baseTexelsUnchanged=False, alphaSampleError=maximum_error,
                     densityBoundedToLimit=any(required[i] > size[i] for i in range(2)),
                     formula="sRGBEncode(sRGBDecode(base.rgb) * (1-factor+2*detail.rgb*detailTint*factor)); base alpha retained",
                     detailColorSpace="non-sRGB data", baseColorSpace="sRGB")
        return image

    def emissive(self, material, embedded_albedo: Image.Image, embedded_emissive: Image.Image | None = None):
        name = material["name"] if isinstance(material, dict) else material
        spec = self.spec(name)
        if not spec["selfIllum"]:
            return embedded_emissive
        base = embedded_albedo.convert("RGBA")
        pixels = np.asarray(base, dtype=np.float32) / 255
        tint = np.asarray(spec["selfIllumTint"], dtype=np.float32)
        if tint.size == 1:
            tint = np.repeat(tint, 3)
        # The current character contract cannot store HDR factors > 1. Keep
        # this limitation visible; authored color/mask and hue survive.
        bounded_tint = tint / max(1., float(tint.max()))
        emitted = linear_to_srgb(srgb_to_linear(pixels[..., :3]) * pixels[..., 3:4] * bounded_tint)
        rgba = np.concatenate((np.clip(emitted, 0, 1), np.ones_like(pixels[..., 3:4])), axis=2)
        result = Image.fromarray(np.round(rgba * 255).astype(np.uint8), "RGBA")
        self.report["materials"][name].update(selfIllumRestored=True, emissiveSize=list(result.size),
             emissiveMask="original base alpha", emissivePixelSha256=hashlib.sha256(result.tobytes()).hexdigest(),
             hdrSelfIllumTintBounded=bool(tint.max() > 1))
        return result

    def material(self, material, embedded_albedo: Image.Image, embedded_normal: Image.Image | None = None):
        """Approximate authored Source Phong using the retained material map.

        Output is *glTF encoded*: R is unused, G is forward roughness, B is
        specular strength, A is opaque. Export metallic/roughness factors 1;
        CharacterModelTextures converts B into the runtime's specular R.
        This does not turn the Source surface into physical metallic PBR.

        Source normally masks Phong by original normal alpha; an explicit
        basemapalphaphongmask selects base alpha instead. Missing normal maps
        use an opaque mask. The source image alpha is data, never sRGB. This
        keeps the existing image inputs unchanged and records the scalar
        approximation: RGB tint becomes luminance, HDR strength is bounded,
        and the runtime has no Source Fresnel or reflection-Phong BRDF.
        """
        name = material["name"] if isinstance(material, dict) else material
        spec = self.spec(name)
        values = spec.get("parameters", {})
        audit = self.report["materials"][name]
        transforms = [key for key in values if key.casefold().endswith("transform")]
        if transforms:
            raise ValueError(f"Transformed source material needs explicit UV handling: {name}: {transforms}")

        def scalar(key, default):
            value = float(values.get(key, str(default)))
            if not math.isfinite(value):
                raise ValueError(f"Invalid Source Phong scalar {key}: {name}")
            return value

        enabled = scalar("$phong", 0) != 0
        if not enabled:
            audit.update(phongMaterialBaked=False, sourcePhongEnabled=False)
            return None
        unsupported = [key for key in ("$phongexponenttexture", "$phongwarptexture", "$phongtintmap",
                                        "$phongexponentfactor", "$phongalbedotint", "$phongalbedoboost")
                       if key in values]
        if unsupported:
            raise ValueError(f"Source Phong texture/control needs an explicit conversion: {name}: {unsupported}")
        exponent = scalar("$phongexponent", 0)
        if exponent <= 0:
            # Source's nonconstant exponent path samples an exponent texture.
            # This archive supplies constants; do not invent a replacement.
            raise ValueError(f"Source Phong requires a positive constant exponent: {name}")
        boost = scalar("$phongboost", 1)
        tint = _vector(values.get("$phongtint"), (1., 1., 1.))
        tint = (tint[0],) * 3 if len(tint) == 1 else tint
        if boost < 0 or len(tint) != 3 or any(not math.isfinite(x) or x < 0 for x in tint):
            raise ValueError(f"Invalid Source Phong boost/tint: {name}")
        # Source treats an explicitly all-zero constant tint as white unless
        # a tint map is present (the map path above is rejected).
        effective_tint = (1., 1., 1.) if all(x == 0 for x in tint) else tint
        tint_luminance = sum(c * w for c, w in zip(effective_tint, (.2126, .7152, .0722)))
        unbounded_strength = boost * tint_luminance
        bounded_strength = min(1., unbounded_strength)
        raw_roughness = (72. - exponent) / 68.
        roughness = min(1., max(.04, raw_roughness))
        base = embedded_albedo.convert("RGBA")
        use_base_mask = scalar("$basemapalphaphongmask", 0) != 0
        invert_mask = scalar("$invertphongmask", 0) != 0
        mask_path = None
        if use_base_mask:
            mask_image = base
            mask_kind = "explicit original base alpha ($basemapalphaphongmask)"
        elif values.get("$bumpmap"):
            reference = values["$bumpmap"].replace("\\", "/").casefold()
            reference = reference.removesuffix(".vtf").removesuffix(".png") + ".png"
            mask_path = self._decoded.get(reference)
            if mask_path is not None:
                mask_image = Image.open(mask_path).convert("RGBA")
                mask_kind = "original decoded VMT normal alpha"
            elif embedded_normal is not None:
                mask_image = embedded_normal.convert("RGBA")
                mask_kind = "embedded original normal alpha (decoded VMT image unavailable)"
            else:
                raise FileNotFoundError(f"Missing authored Phong normal-alpha mask {reference}: {name}")
        else:
            mask_image = Image.new("RGBA", (1, 1), (128, 128, 255, 255))
            mask_kind = "opaque default (no authored normal/base-alpha mask)"
        required = tuple(max(base.size[i], mask_image.size[i]) for i in range(2))
        size = tuple(min(self.max_dimension, 1 << (int(x) - 1).bit_length()) for x in required)
        output = np.empty((size[1], size[0], 4), dtype=np.uint8)
        output[..., 0] = 0
        output[..., 1] = round(roughness * 255)
        output[..., 3] = 255
        u = (np.arange(size[0], dtype=np.float64) + .5)[None, :] / size[0]
        mask_low, mask_high = 1., 0.
        for y in range(0, size[1], 32):
            v = (np.arange(y, min(size[1], y + 32), dtype=np.float64) + .5)[:, None] / size[1]
            mask = sample_periodic(mask_image, u, v)[..., 3]
            if invert_mask:
                mask = 1. - mask
            mask_low = min(mask_low, float(mask.min()))
            mask_high = max(mask_high, float(mask.max()))
            output[y:y + len(v), :, 2] = np.round(np.clip(mask * bounded_strength, 0, 1) * 255).astype(np.uint8)
        result = Image.fromarray(output, "RGBA")
        audit.update(phongMaterialBaked=True, sourcePhongEnabled=True,
            sourcePhongValue=scalar("$phong", 0), sourcePhongBoost=boost, sourcePhongExponent=exponent,
            sourcePhongTint=list(tint), effectivePhongTint=list(effective_tint), phongTintLuminance=tint_luminance,
            sourcePhongFresnelRanges=list(_vector(values.get("$phongfresnelranges"), (0., .5, 1.))),
            phongMask=mask_kind, phongMaskInverted=invert_mask, phongMaskSampleRange=[mask_low, mask_high],
            phongMaskSource=str(mask_path) if mask_path else None,
            phongMaskSourceSha256=sha256(mask_path) if mask_path else None,
            phongMaskSize=list(mask_image.size), phongMaskPixelSha256=hashlib.sha256(mask_image.tobytes()).hexdigest(),
            phongInputBasePixelSha256=hashlib.sha256(base.tobytes()).hexdigest(),
            unboundedPhongStrength=unbounded_strength, boundedPhongStrength=bounded_strength,
            phongStrengthClamped=unbounded_strength > 1,
            forwardRoughness=roughness, forwardRoughnessUnclamped=raw_roughness,
            forwardRoughnessClamped=roughness != raw_roughness,
            materialSize=list(size), materialPixelSha256=hashlib.sha256(result.tobytes()).hexdigest(),
            materialPacking="glTF R unused=0; G forward roughness; B scalar specular strength; A=255; factors=1",
            materialFormula="B=normal/base/default alpha * clamp(phongboost*Rec709(phongtint),0,1); G=clamp((72-phongexponent)/68,.04,1)",
            materialDensityBoundedToLimit=any(required[i] > size[i] for i in range(2)),
            sourceSkinShaderEligible=bool(use_base_mask or values.get("$bumpmap") or values.get("$lightwarptexture")),
            phongApproximation=["Source RGB tint reduced to scalar luminance", "Source view-dependent Fresnel omitted",
                "Source HDR boost bounded to unit strength", "Forward half-vector lobe uses authored exponent numerically; reflection-Phong lobe differs",
                "Forward highlight global gain remains 0.16; native diffuse lighting remains authoritative",
                "Authored Phong retained when Source would require a bump/base-alpha/lightwarp shader path"])
        return result

    def write_audit(self, path):
        path = Path(path)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(self.report, indent=2) + "\n")


def embedded_image(doc, blob, material, channel="albedo"):
    texture = material.get("pbrMetallicRoughness", {}).get("baseColorTexture") if channel == "albedo" else material.get(channel + "Texture")
    if not texture:
        return None
    index = doc["textures"][texture["index"]]["source"]
    return Image.open(io.BytesIO(image_bytes(doc, blob, index))).convert("RGBA")


def audit_roster(converted_root, material_root, installed_root):
    converted_root, installed_root = Path(converted_root), Path(installed_root)
    manifest = json.loads((installed_root / "characters.json").read_text())
    report = {"installedPackId": manifest["id"], "hunters": {}, "semanticsSources": SEMANTICS_SOURCES}
    for hunter in ("samus", "noxus", "kanden", "sylux", "trace", "weavel", "spire"):
        source = converted_root / "models" / hunter / (hunter + "_a.glb")
        doc, blob = load(source)
        library = SourceMaterialLibrary(material_root, converted_root / "decoded-vtf", hunter)
        materials = {}
        for material in doc["materials"]:
            spec = library.spec(material["name"])
            base = embedded_image(doc, blob, material)
            normal = embedded_image(doc, blob, material, "normal")
            pixels = np.asarray(base)
            materials[material["name"]] = dict(spec, baseSize=list(base.size),
                baseRgbMean=[float(v) for v in pixels[..., :3].mean(axis=(0, 1))],
                baseRgbStd=[float(v) for v in pixels[..., :3].std(axis=(0, 1))],
                normalSize=list(normal.size) if normal else None,
                glbEmissivePresent=bool(material.get("emissiveTexture")),
                sourceBasePixelSha256=hashlib.sha256(base.tobytes()).hexdigest())
        installed = []
        for entry in manifest["models"]:
            if entry["hunter"].casefold() != hunter:
                continue
            live_doc, _ = load(installed_root / entry["model"])
            count = {}
            for mesh in live_doc["meshes"]:
                for primitive in mesh["primitives"]:
                    index = primitive["material"]
                    count[index] = count.get(index, 0) + live_doc["accessors"][primitive["indices"]]["count"] // 3
            installed.append(dict(entry=entry, materials=[{
                "name": live_doc["materials"][index]["name"], "triangles": n,
                "embeddedAlbedo": bool(live_doc["materials"][index].get("pbrMetallicRoughness", {}).get("baseColorTexture")),
                "normal": bool(live_doc["materials"][index].get("normalTexture")),
                "emissive": bool(live_doc["materials"][index].get("emissiveTexture")),
                "alphaMode": live_doc["materials"][index].get("alphaMode", "OPAQUE")}
                for index, n in count.items()]))
        report["hunters"][hunter] = {"sourceSha256": sha256(source), "materials": materials, "installed": installed}
    return report
