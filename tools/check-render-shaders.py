#!/usr/bin/env python3
"""Fail if checked-in renderer artifacts are stale or missing."""
from __future__ import annotations

import hashlib
import json
import re
import sys
from pathlib import Path


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def main() -> int:
    root = Path(__file__).resolve().parents[1]
    source = root / "src/Client/Rendering/Shaders/scene_triangle.hlsl"
    manifest_path = root / "src/Client/Rendering/Shaders/Generated/manifest.json"
    if not source.is_file() or not manifest_path.is_file():
        print("renderer shader source or manifest is missing", file=sys.stderr)
        return 1
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("source") != source.name:
        print("renderer shader manifest source does not match scene_triangle.hlsl", file=sys.stderr)
        return 1
    actual_source = sha256(source)
    if manifest.get("source_sha256") != actual_source:
        print(f"renderer shader source is stale: manifest={manifest.get('source_sha256')} actual={actual_source}", file=sys.stderr)
        return 1
    source_text = source.read_text(encoding="utf-8")
    if "TEXCOORD0" not in source_text or "TEXCOORD1" not in source_text:
        print("renderer shader source must use TEXCOORD0/TEXCOORD1 for SDL GPU semantic compatibility", file=sys.stderr)
        return 1
    if re.search(r":\s*(POSITION|COLOR0)\b", source_text, re.IGNORECASE):
        print("renderer shader source contains unsupported POSITION/COLOR0 semantics", file=sys.stderr)
        return 1
    provenance = manifest.get("provenance", {})
    for field in ("sdl_shadercross_commit", "cli_sha256", "native_package", "native_package_version", "native_package_sha256"):
        if not provenance.get(field):
            print(f"renderer shader manifest is missing pinned provenance field: {field}", file=sys.stderr)
            return 1
    required_formats = {"SPIRV", "MSL", "DXIL"}
    actual_formats = {artifact.get("format") for artifact in manifest.get("artifacts", {}).values()}
    missing_formats = required_formats - actual_formats
    if missing_formats:
        print(f"renderer shader manifest is missing generated formats: {sorted(missing_formats)}", file=sys.stderr)
        return 1
    if "dxil" in manifest.get("blocked_artifacts", {}):
        print("renderer shader manifest incorrectly reports DXIL as blocked", file=sys.stderr)
        return 1
    for name, artifact in manifest.get("artifacts", {}).items():
        path = manifest_path.parent / artifact["path"]
        if not path.is_file():
            print(f"renderer shader artifact {name} is missing: {path}", file=sys.stderr)
            return 1
        actual = sha256(path)
        if artifact.get("sha256") != actual:
            print(f"renderer shader artifact {name} is stale: manifest={artifact.get('sha256')} actual={actual}", file=sys.stderr)
            return 1
    scene_source = root / "src/Client/Rendering/Shaders/scene.hlsl"
    scene_manifest_path = root / "src/Client/Rendering/Shaders/Generated/scene_manifest.json"
    if not scene_source.is_file() or not scene_manifest_path.is_file():
        print("scene shader source or manifest is missing", file=sys.stderr)
        return 1
    scene_manifest = json.loads(scene_manifest_path.read_text(encoding="utf-8"))
    if scene_manifest.get("source") != scene_source.name:
        print("scene shader manifest source does not match scene.hlsl", file=sys.stderr)
        return 1
    if scene_manifest.get("source_sha256") != sha256(scene_source):
        print("scene shader source is stale", file=sys.stderr)
        return 1
    scene_provenance = scene_manifest.get("provenance", {})
    for field in ("sdl_shadercross_commit", "cli_sha256", "native_package", "native_package_version", "native_package_sha256"):
        if not scene_provenance.get(field):
            print(f"scene shader manifest is missing pinned provenance field: {field}", file=sys.stderr)
            return 1
    scene_formats = {artifact.get("format") for artifact in scene_manifest.get("artifacts", {}).values()}
    missing_scene_formats = required_formats - scene_formats
    if missing_scene_formats:
        print(f"scene shader manifest is missing generated formats: {sorted(missing_scene_formats)}", file=sys.stderr)
        return 1
    for name, artifact in scene_manifest.get("artifacts", {}).items():
        path = scene_manifest_path.parent / artifact["path"]
        if not path.is_file():
            print(f"scene shader artifact {name} is missing: {path}", file=sys.stderr)
            return 1
        actual = sha256(path)
        if artifact.get("sha256") != actual:
            print(f"scene shader artifact {name} is stale: manifest={artifact.get('sha256')} actual={actual}", file=sys.stderr)
            return 1

    def check_family(stem: str, required_tokens: tuple[str, ...]) -> int:
        family_source = root / "src/Client/Rendering/Shaders" / f"{stem}.hlsl"
        family_manifest_path = root / "src/Client/Rendering/Shaders/Generated" / f"{stem}_manifest.json"
        if not family_source.is_file() or not family_manifest_path.is_file():
            print(f"{stem} shader source or manifest is missing", file=sys.stderr)
            return 1
        family_manifest = json.loads(family_manifest_path.read_text(encoding="utf-8"))
        if family_manifest.get("source") != family_source.name:
            print(f"{stem} shader manifest source does not match {family_source.name}", file=sys.stderr)
            return 1
        source_text = family_source.read_text(encoding="utf-8")
        if family_manifest.get("source_sha256") != sha256(family_source):
            print(f"{stem} shader source is stale", file=sys.stderr)
            return 1
        if "TEXCOORD0" not in source_text or "SV_Position" not in source_text:
            print(f"{stem} shader source is missing the SDL GPU fullscreen vertex contract", file=sys.stderr)
            return 1
        for token in required_tokens:
            if token not in source_text:
                print(f"{stem} shader source is missing required semantic/formula token: {token}", file=sys.stderr)
                return 1
        family_provenance = family_manifest.get("provenance", {})
        for field in ("sdl_shadercross_commit", "cli_sha256", "native_package", "native_package_version", "native_package_sha256"):
            if not family_provenance.get(field):
                print(f"{stem} shader manifest is missing pinned provenance field: {field}", file=sys.stderr)
                return 1
        family_artifacts = family_manifest.get("artifacts", {})
        family_formats = {artifact.get("format") for artifact in family_artifacts.values()}
        missing_family_formats = required_formats - family_formats
        if missing_family_formats:
            print(f"{stem} shader manifest is missing generated formats: {sorted(missing_family_formats)}", file=sys.stderr)
            return 1
        for name, artifact in family_artifacts.items():
            path = family_manifest_path.parent / artifact["path"]
            if not path.is_file():
                print(f"{stem} shader artifact {name} is missing: {path}", file=sys.stderr)
                return 1
            actual = sha256(path)
            if artifact.get("sha256") != actual:
                print(f"{stem} shader artifact {name} is stale: manifest={artifact.get('sha256')} actual={actual}", file=sys.stderr)
                return 1
        return 0

    for family, tokens in {
        "fullscreen": ("LegacyMaskTexcoord", "fadeColor", "operation"),
        "hud": ("LegacyMaskTexcoord", "hudOptions", "hudTexture"),
        "disruption": ("ShiftValue", "WhiteoutValue", "disruptionOptions"),
        "cel": ("KinkAbs", "EdgeAt", "depthTexture"),
        "bloom": ("BlurSample", "bloomOptions", "sourceTexture"),
        "tone_map": ("ToneMapAces", "LinearToSRGB", "toneMapOptions"),
        "color_grade": ("LutUv", "LutTextureSize", "colorGradeOptions", "lutTexture"),
        "surface": ("EncodeOctNormal", "ResolveAlpha", "viewDepth", "normalTexture"),
        "ssao": ("RawOcclusion", "Bilateral", "CoordinateRotation", "surfaceTexture"),
        "shadow": ("shadowViewProjection", "SV_Depth", "albedoTexture"),
        "distortion": ("distortionOptions", "matrixStack", "SV_Target0"),
        "distortion_warp": ("distortionTexture", "warpedUv", "sourceTexture"),
        "visor": ("VisorConstants", "EdgeMask", "visorDamage", "sourceTexture"),
        "sky": ("SkyConstants", "WorldDirection", "SampleCubeFaces", "skyTexture0"),
    }.items():
        if check_family(family, tokens) != 0:
            return 1
    print("renderer shader manifest is fresh")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
