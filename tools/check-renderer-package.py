#!/usr/bin/env python3
"""Validate the SDL GPU runtime and offline shaders in a desktop publish."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import sys


RUNTIME_FILES = {
    "win-x64": ("ProjectPrime.exe", "SDL3.dll", "dxil"),
    "win-arm64": ("ProjectPrime.exe", "SDL3.dll", "dxil"),
    "linux-x64": ("ProjectPrime", "libSDL3.so", "spirv"),
    "linux-arm64": ("ProjectPrime", "libSDL3.so", "spirv"),
    "osx-x64": ("ProjectPrime", "libSDL3.dylib", "msl"),
    "osx-arm64": ("ProjectPrime", "libSDL3.dylib", "msl"),
}
MANIFESTS = {
    "manifest.json",
    "scene_manifest.json",
    "fullscreen_manifest.json",
    "hud_manifest.json",
    "disruption_manifest.json",
    "cel_manifest.json",
}
FORBIDDEN_DESKTOP_ASSEMBLIES = {
    "OpenTK.Graphics.dll",
    "OpenTK.Windowing.Desktop.dll",
}


def inspect_package(root: Path, rid: str) -> list[str]:
    errors: list[str] = []
    if rid not in RUNTIME_FILES:
        return [f"unsupported desktop RID: {rid}"]
    executable, sdl_library, required_format = RUNTIME_FILES[rid]
    for relative in (executable, sdl_library):
        if not (root / relative).is_file():
            errors.append(f"missing {relative}")
    for assembly in sorted(FORBIDDEN_DESKTOP_ASSEMBLIES):
        if (root / assembly).exists():
            errors.append(f"legacy renderer assembly remains: {assembly}")

    shader_root = root / "Rendering/Shaders/Generated"
    if not shader_root.is_dir():
        return errors + ["missing Rendering/Shaders/Generated"]
    present_manifests = {path.name for path in shader_root.glob("*manifest.json")}
    for missing in sorted(MANIFESTS - present_manifests):
        errors.append(f"missing shader manifest {missing}")

    saw_platform_format = False
    for manifest_name in sorted(MANIFESTS & present_manifests):
        manifest_path = shader_root / manifest_name
        try:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        except (OSError, ValueError) as error:
            errors.append(f"invalid shader manifest {manifest_name}: {error}")
            continue
        artifacts = manifest.get("artifacts")
        if not isinstance(artifacts, dict):
            errors.append(f"invalid shader manifest {manifest_name}: no artifacts object")
            continue
        manifest_has_platform_format = False
        for artifact_name, metadata in artifacts.items():
            if not isinstance(metadata, dict):
                errors.append(f"invalid artifact {manifest_name}:{artifact_name}")
                continue
            relative = metadata.get("path")
            expected_hash = metadata.get("sha256")
            shader_format = str(metadata.get("format", "")).lower()
            if shader_format == required_format:
                manifest_has_platform_format = True
                saw_platform_format = True
            if not isinstance(relative, str) or not isinstance(expected_hash, str):
                errors.append(f"incomplete artifact {manifest_name}:{artifact_name}")
                continue
            artifact_path = shader_root / relative
            if not artifact_path.is_file():
                errors.append(f"missing shader artifact {relative}")
                continue
            actual_hash = hashlib.sha256(artifact_path.read_bytes()).hexdigest()
            if actual_hash != expected_hash:
                errors.append(f"stale shader artifact {relative}")
        if not manifest_has_platform_format:
            errors.append(f"{manifest_name} has no {required_format.upper()} artifact for {rid}")
    if not saw_platform_format:
        errors.append(f"package has no {required_format.upper()} shaders for {rid}")
    return sorted(set(errors))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True, choices=sorted(RUNTIME_FILES))
    parser.add_argument("package", type=Path)
    args = parser.parse_args()
    errors = inspect_package(args.package.resolve(), args.rid)
    for error in errors:
        print(f"renderer package: {error}", file=sys.stderr)
    if errors:
        return 1
    print(f"renderer package: {args.rid} has SDL3 and verified offline shaders")
    return 0


if __name__ == "__main__":
    sys.exit(main())
