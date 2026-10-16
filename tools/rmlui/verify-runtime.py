#!/usr/bin/env python3
"""Validate the pinned RmlUi bridge and the loose files required at bootstrap."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import struct
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
RML_REVISION = "ba95ffe8bfb6370efb2cdcca927eaad4710c5413"
FT_REVISION = "42608f77f20749dd6ddc9e0536788eaad70ea4b5"
MANIFEST = "PRIME-RMLUI.json"
LICENSES = ("RmlUi-LICENSE.txt", "FreeType-LICENSE.txt", "FreeType-FTL.txt", "FreeType-GPLv2.txt")
ASSETS = ("prime_home.rml", "prime_home.rcss", "fonts/Rajdhani-SemiBold.ttf",
          "fonts/Rajdhani-Bold.ttf", "fonts/JetBrainsMono-Regular.ttf",
          "fonts/Rajdhani-OFL.txt", "fonts/JetBrainsMono-OFL.txt", "fonts/NotoSansJP-Regular.ttf", "fonts/NotoSansJP-OFL.txt")
EXPORTS = ("pp_rmlui_initialize", "pp_rmlui_shutdown", "pp_rmlui_update", "pp_rmlui_render",
           "pp_rmlui_key", "pp_rmlui_text", "pp_rmlui_take_action", "pp_rmlui_protocol_version",
           "pp_rmlui_initialize_backend", "pp_rmlui_document_open", "pp_rmlui_document_close",
           "pp_rmlui_document_count", "pp_rmlui_take_intent", "pp_rmlui_draw_command_count",
           "pp_rmlui_text_input_state", "pp_rmlui_composition", "pp_rmlui_text_selection_utf16",
           "pp_rmlui_document_accessibility_snapshot", "pp_rmlui_accessibility_action", "pp_rmlui_accessibility_set_text",
           "pp_rmlui_set_clipboard", "pp_rmlui_read_clipboard", "pp_rmlui_draw_geometry_count")
NAMES = {"win-x64": "ProjectPrime.RmlUi.Native.dll", "linux-x64": "libProjectPrime.RmlUi.Native.so",
         "osx-arm64": "libProjectPrime.RmlUi.Native.dylib", "osx-x64": "libProjectPrime.RmlUi.Native.dylib",
         "android-arm64": "libProjectPrime.RmlUi.Native.so", "android-x64": "libProjectPrime.RmlUi.Native.so"}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def fingerprint() -> str:
    sources = sorted((ROOT / "native/rmlui-poc").glob("*.cpp")) + sorted((ROOT / "native/rmlui-poc").glob("*.h"))
    sources += [ROOT / "native/rmlui-poc/CMakeLists.txt"]
    result = hashlib.sha256()
    for source in sources:
        # Git for Windows can checkout the very same pinned .cpp/.h files
        # with CRLF, while the Linux release packager checks out LF. The
        # provenance fingerprint must represent source, not the host's EOL
        # convention. Binary library SHA-256 remains byte-exact separately.
        normalized = source.read_bytes().replace(b"\r\n", b"\n")
        result.update(source.name.encode() + b"\0" + normalized + b"\0")
    return result.hexdigest()


def architecture(data: bytes, target: str) -> None:
    if target.startswith("win"):
        require(data[:2] == b"MZ" and len(data) >= 64, "expected a Windows PE DLL")
        offset = struct.unpack_from("<I", data, 60)[0]
        require(data[offset:offset + 4] == b"PE\0\0", "invalid PE header")
        require(struct.unpack_from("<H", data, offset + 4)[0] == 0x8664, "expected an x64 PE DLL")
    elif target.startswith("osx"):
        require(data[:4] == b"\xcf\xfa\xed\xfe", "expected a thin 64-bit Mach-O library")
        expected = 0x100000C if target == "osx-arm64" else 0x1000007
        require(struct.unpack_from("<I", data, 4)[0] == expected, "Mach-O CPU does not match the RID")
    else:
        require(data[:6] == b"\x7fELF\x02\x01", "expected a little-endian 64-bit ELF library")
        expected = 183 if target == "android-arm64" else 62
        require(struct.unpack_from("<H", data, 18)[0] == expected, "ELF CPU does not match the RID")
        if target.startswith("android"):
            offset = struct.unpack_from("<Q", data, 32)[0]
            entry_size, count = struct.unpack_from("<HH", data, 54)
            for index in range(count):
                header = offset + index * entry_size
                if struct.unpack_from("<I", data, header)[0] == 1:  # PT_LOAD
                    alignment = struct.unpack_from("<Q", data, header + 48)[0]
                    require(alignment >= 16384, "Android ELF load segment is not aligned for 16 KB pages")


def verify(library: Path, target: str) -> dict:
    require(target in NAMES, f"unsupported RmlUi RID: {target}")
    require(library.name == NAMES[target], "library filename does not match the RID")
    metadata = json.loads((library.parent / MANIFEST).read_text())
    require(metadata["manifest_version"] == 1, "unsupported native manifest")
    require(metadata["rmlui_revision"] == RML_REVISION and metadata["freetype_revision"] == FT_REVISION,
            "native dependencies do not match the pinned versions")
    require(metadata["target"] == target and metadata["library"] == library.name,
            "native manifest target/library does not match the package")
    require(metadata["source_fingerprint"] == fingerprint(), "native bridge is stale; rebuild it from these sources")
    require(metadata["renderer"] in ("gl2", "draw-list"), "unknown renderer adapter")
    require(not target.startswith("android") or metadata["renderer"] == "draw-list", "Android cannot package desktop GL2")
    data = library.read_bytes()
    require(metadata["sha256"] == hashlib.sha256(data).hexdigest(), "native library hash does not match its manifest")
    architecture(data, target)
    for export in EXPORTS:
        require(export.encode() + b"\0" in data, f"native export missing: {export}")
    for license_name in LICENSES:
        require((library.parent / "licenses" / license_name).is_file(), f"native attribution missing: {license_name}")
    return metadata


def main(args: list[str]) -> None:
    if args == ["--fingerprint"]:
        print(fingerprint())
        return
    if args and args[0] == "--apk":
        require(len(args) in (2, 3), "usage: verify-runtime.py --apk <package.apk> [android-arm64|android-x64]")
        selected = args[2] if len(args) == 3 else None
        require(selected is None or selected in ("android-arm64", "android-x64"), "unsupported APK RID")
        with zipfile.ZipFile(args[1]) as package:
            files = set(package.namelist())
            for asset in ASSETS:
                require("assets/rmlui/" + asset in files, f"APK bootstrap asset missing: {asset}")
            for license_name in LICENSES:
                require("assets/rmlui/licenses/" + license_name in files,
                        f"APK attribution missing: {license_name}")
            for abi, target in (("arm64-v8a", "android-arm64"), ("x86_64", "android-x64")):
                if selected and selected != target:
                    continue
                name = f"lib/{abi}/{NAMES[target]}"
                require(name in files, f"APK RmlUi ABI missing: {abi}")
                manifest_name = f"assets/rmlui/native-manifests/{abi}/{MANIFEST}"
                require(manifest_name in files, f"APK native manifest missing: {abi}")
                metadata = json.loads(package.read(manifest_name))
                require(metadata["target"] == target and metadata["renderer"] == "draw-list",
                        f"APK native adapter/target mismatch: {abi}")
                require(metadata["rmlui_revision"] == RML_REVISION and metadata["freetype_revision"] == FT_REVISION,
                        f"APK native dependencies do not match their pins: {abi}")
                require(metadata["source_fingerprint"] == fingerprint(), f"APK bridge sources are stale: {abi}")
                data = package.read(name)
                require(metadata["sha256"] == hashlib.sha256(data).hexdigest(),
                        f"APK native library hash does not match its manifest: {abi}")
                architecture(data, target)
                for export in EXPORTS:
                    require(export.encode() + b"\0" in data, f"APK native export missing: {export} ({abi})")
        print(f"verified RmlUi foundation payload in APK: {args[1]} ({selected or 'arm64-v8a, x86_64'})")
        return
    if args and args[0] == "--write":
        require(len(args) in (4, 5), "usage: verify-runtime.py --write <library> <rid> <gl2|draw-list> [source-fingerprint]")
        library, target, renderer = Path(args[1]), args[2], args[3]
        require(target in NAMES and renderer in ("gl2", "draw-list"), "unsupported target or adapter")
        source_fingerprint = fingerprint()
        require(len(args) == 4 or args[4] == source_fingerprint,
                "bridge sources changed during the native build; rebuild after edits finish")
        metadata = {"manifest_version": 1, "rmlui_version": "6.3", "rmlui_revision": RML_REVISION,
                    "freetype_version": "2.13.3", "freetype_revision": FT_REVISION, "target": target,
                    "library": library.name, "renderer": renderer, "source_fingerprint": source_fingerprint,
                    "sha256": hashlib.sha256(library.read_bytes()).hexdigest()}
        (library.parent / MANIFEST).write_text(json.dumps(metadata, indent=2) + "\n")
        return
    if args and args[0] == "--package":
        require(len(args) == 3, "usage: verify-runtime.py --package <publish-directory> <rid>")
        package, target = Path(args[1]), args[2]
        require(target in NAMES, f"unsupported RmlUi RID: {target}")
        library = package / NAMES[target]
        metadata = verify(library, target)
        for asset in ASSETS:
            require((package / "rmlui" / asset).is_file(), f"RmlUi bootstrap asset missing: {asset}")
        print(f"verified RmlUi package: {package} ({target}, {metadata['renderer']})")
        return
    require(len(args) in (1, 2), "usage: verify-runtime.py <library> [rid]")
    library = Path(args[0])
    target = args[1] if len(args) == 2 else json.loads((library.parent / MANIFEST).read_text())["target"]
    metadata = verify(library, target)
    print(f"verified RmlUi runtime: {library} ({target}, {metadata['renderer']})")


if __name__ == "__main__":
    try:
        main(sys.argv[1:])
    except (ValueError, OSError, KeyError, struct.error, zipfile.BadZipFile) as error:
        print(f"error: RmlUi runtime verification failed: {error}", file=sys.stderr)
        sys.exit(1)
