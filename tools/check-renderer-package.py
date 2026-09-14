#!/usr/bin/env python3
"""Validate the SDL GPU runtime and offline shaders in a desktop publish."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct
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
WINDOWS_PREREQUISITES = "WINDOWS_PREREQUISITES.txt"
WINDOWS_PREREQUISITE_URL = (
    "https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170"
)


def _pe_imports(path: Path) -> set[str]:
    """Return imported DLL names from a PE image without platform-specific tools."""
    data = path.read_bytes()
    if len(data) < 64 or data[:2] != b"MZ":
        return set()
    try:
        pe_offset = struct.unpack_from("<I", data, 0x3C)[0]
        if data[pe_offset:pe_offset + 4] != b"PE\0\0":
            raise ValueError("invalid PE signature")
        coff = pe_offset + 4
        section_count = struct.unpack_from("<H", data, coff + 2)[0]
        optional_size = struct.unpack_from("<H", data, coff + 16)[0]
        optional = coff + 20
        magic = struct.unpack_from("<H", data, optional)[0]
        data_directory = optional + (112 if magic == 0x20B else 96 if magic == 0x10B else 0)
        if data_directory == optional:
            raise ValueError("unsupported PE optional header")
        if data_directory + 16 > optional + optional_size:
            raise ValueError("missing PE import directory")
        import_rva, import_size = struct.unpack_from("<II", data, data_directory + 8)
        if import_rva == 0 or import_size == 0:
            return set()
        sections = []
        section_table = optional + optional_size
        for index in range(section_count):
            entry = section_table + index * 40
            virtual_size, virtual_address, raw_size, raw_offset = struct.unpack_from(
                "<IIII", data, entry + 8
            )
            sections.append((virtual_address, max(virtual_size, raw_size), raw_offset))

        def file_offset(rva: int) -> int:
            for virtual_address, size, raw_offset in sections:
                if virtual_address <= rva < virtual_address + size:
                    result = raw_offset + rva - virtual_address
                    if result >= len(data):
                        break
                    return result
            raise ValueError(f"unmapped PE RVA 0x{rva:x}")

        imports: set[str] = set()
        descriptor = file_offset(import_rva)
        limit = min(len(data), descriptor + import_size)
        while descriptor + 20 <= limit:
            values = struct.unpack_from("<IIIII", data, descriptor)
            if not any(values):
                break
            name_offset = file_offset(values[3])
            end = data.find(b"\0", name_offset, min(len(data), name_offset + 260))
            if end < 0:
                raise ValueError("unterminated PE import name")
            imports.add(data[name_offset:end].decode("ascii").upper())
            descriptor += 20
        return imports
    except (IndexError, struct.error) as error:
        raise ValueError("truncated PE image") from error


def _windows_runtime_errors(root: Path) -> list[str]:
    errors: list[str] = []
    required: dict[str, set[str]] = {}
    for path in sorted(root.rglob("*")):
        if not path.is_file() or path.suffix.lower() not in {".dll", ".exe"}:
            continue
        try:
            imports = _pe_imports(path)
        except (OSError, ValueError) as error:
            errors.append(f"cannot inspect PE imports for {path.relative_to(root)}: {error}")
            continue
        runtimes = {
            name for name in imports
            if name.endswith(".DLL") and name.startswith(("MSVCP", "VCRUNTIME"))
        }
        if runtimes:
            required[str(path.relative_to(root))] = runtimes
    if not required:
        return errors

    declaration = root / WINDOWS_PREREQUISITES
    if not declaration.is_file():
        errors.append(
            f"native Microsoft runtime imports require {WINDOWS_PREREQUISITES}"
        )
        return errors
    try:
        text = declaration.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as error:
        errors.append(f"invalid {WINDOWS_PREREQUISITES}: {error}")
        return errors
    upper = text.upper()
    if WINDOWS_PREREQUISITE_URL not in text or "VISUAL C++" not in upper or "X64" not in upper:
        errors.append(
            f"incomplete {WINDOWS_PREREQUISITES}: missing Microsoft Visual C++ v14 x64 guidance"
        )
    for runtime in sorted(set().union(*required.values())):
        if runtime not in upper:
            errors.append(
                f"incomplete {WINDOWS_PREREQUISITES}: does not declare {runtime}"
            )
    return errors


def inspect_package(root: Path, rid: str, executable_override: str | None = None) -> list[str]:
    errors: list[str] = []
    if rid not in RUNTIME_FILES:
        return [f"unsupported desktop RID: {rid}"]
    executable, sdl_library, required_format = RUNTIME_FILES[rid]
    executable = executable_override or executable
    for relative in (executable, sdl_library):
        if not (root / relative).is_file():
            errors.append(f"missing {relative}")
    for assembly in sorted(FORBIDDEN_DESKTOP_ASSEMBLIES):
        if (root / assembly).exists():
            errors.append(f"legacy renderer assembly remains: {assembly}")
    if rid.startswith("win-"):
        errors.extend(_windows_runtime_errors(root))

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
    parser.add_argument("--executable", help="expected executable name (defaults to the game client)")
    parser.add_argument("package", type=Path)
    args = parser.parse_args()
    errors = inspect_package(args.package.resolve(), args.rid, args.executable)
    for error in errors:
        print(f"renderer package: {error}", file=sys.stderr)
    if errors:
        return 1
    print(f"renderer package: {args.rid} has SDL3 and verified offline shaders")
    return 0


if __name__ == "__main__":
    sys.exit(main())
