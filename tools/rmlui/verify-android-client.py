#!/usr/bin/env python3
"""Audit the restored and packaged Android UI dependency boundary.

Assembly-store layout is the .NET for Android XABA format:
https://github.com/dotnet/android/blob/main/Documentation/project-docs/AssemblyStores.md
ELF wrappers expose the store in their `payload` section. No runtime code is
loaded while inspecting APKs. Unknown store formats fail the audit.
"""
import argparse
import json
from pathlib import Path
import struct
import sys
import zipfile


def require(condition, message):
    if not condition:
        raise ValueError(message)


def elf_payload(data):
    require(data[:4] == b"\x7fELF" and data[5] == 1, "Unsupported assembly-store ELF encoding")
    if data[4] == 2:
        offset = struct.unpack_from("<Q", data, 40)[0]
        size, count, strings = struct.unpack_from("<HHH", data, 58)
        def section(index):
            entry = offset + size * index
            return (struct.unpack_from("<I", data, entry)[0],
                    struct.unpack_from("<Q", data, entry + 24)[0],
                    struct.unpack_from("<Q", data, entry + 32)[0])
    elif data[4] == 1:
        offset = struct.unpack_from("<I", data, 32)[0]
        size, count, strings = struct.unpack_from("<HHH", data, 46)
        def section(index):
            entry = offset + size * index
            return (struct.unpack_from("<I", data, entry)[0],
                    struct.unpack_from("<I", data, entry + 16)[0],
                    struct.unpack_from("<I", data, entry + 20)[0])
    else:
        raise ValueError("Unsupported assembly-store ELF class")
    require(0 < count <= 65535 and strings < count and offset + count * size <= len(data), "Invalid ELF section table")
    _, start, length = section(strings)
    names = data[start:start + length]
    for index in range(count):
        name, start, length = section(index)
        end = names.find(b"\0", name)
        require(end >= name, "Invalid ELF section name")
        if names[name:end] in (b"payload", b".payload"):
            require(start + length <= len(data), "Assembly-store payload exceeds ELF image")
            return data[start:start + length]
    raise ValueError("Assembly-store ELF has no payload section")


def store_names(data):
    if data.startswith(b"\x7fELF"):
        data = elf_payload(data)
    require(len(data) >= 20, "Assembly store has no complete header")
    magic, version, count, index_count, index_size = struct.unpack_from("<5I", data)
    require(magic == 0x41424158, "Assembly store has no XABA header")
    # ABI and 64-bit flags occupy the upper version bits; the base format is
    # version 2 (MonoVM .NET 10) or 3 (CoreCLR/updated MonoVM).
    require(version & 0xFFFF in (2, 3), f"Unsupported assembly-store format {version:#x}")
    require(0 < count <= 10000 and count <= index_count <= count * 2, "Invalid assembly-store entry count")
    position = 20 + index_size + count * 28
    names = []
    for _ in range(count):
        require(position + 4 <= len(data), "Assembly names exceed store")
        length = struct.unpack_from("<I", data, position)[0]
        position += 4
        require(0 < length <= 4096 and position + length <= len(data), "Invalid assembly name length")
        name = data[position:position + length].decode("utf-8", "strict")
        require("\0" not in name, "Assembly name contains NUL")
        names.append(name)
        position += length
    require(len(set(names)) == len(names), "Assembly store contains duplicate names")
    return names


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--assets", type=Path, help="The exact build's project.assets.json")
    parser.add_argument("--expect-avalonia", choices=("true", "false"), default="false")
    args = parser.parse_args()
    expected = args.expect_avalonia == "true"
    if args.assets:
        assets = json.loads(args.assets.read_text())
        packages = [name for name in assets["libraries"] if name.lower().startswith("avalonia")]
        require(bool(packages) == expected, f"Restored Avalonia dependencies do not match expected={expected}: {packages}")
    stores = {}
    with zipfile.ZipFile(args.apk) as apk:
        forbidden = []
        for name in apk.namelist():
            lower = name.lower()
            if lower.endswith((".nds", ".narc", ".sdat", ".sbin", ".spc", ".brstm", ".arc", ".bin")) or any(
                "/" + folder + "/" in "/" + lower for folder in ("files", "thumbnails", "_archives", "savedata", "netcheck-shots")
            ) or Path(lower).name == "paths.txt":
                forbidden.append(name)
            if name.endswith(".dll"):
                stores.setdefault("individual assemblies", []).append(Path(name).name)
            elif name.endswith(".dll.so") and Path(name).name.startswith("lib_"):
                # Debug MonoVM packages wrap each PE image separately in ELF.
                assembly = Path(name).name[4:-3]
                stores.setdefault(str(Path(name).parent) + " individual ELF assemblies", []).append(assembly)
                if assembly == "ProjectPrime.dll":
                    payload = elf_payload(apk.read(name))
                    require(payload.startswith((b"MZ", b"XALZ")), "ProjectPrime wrapper contains no managed PE payload")
            elif "assembl" in name and name.endswith((".blob", ".blob.so", "libassembly-store.so")):
                stores[name] = store_names(apk.read(name))
        require(not forbidden, f"APK contains cartridge/extraction/cache paths: {forbidden}")
    require(stores, "APK contains no auditable embedded managed assemblies")
    for store, names in stores.items():
        require(any(Path(name).stem == "ProjectPrime" for name in names), f"{store} has no ProjectPrime client assembly")
        avalonia = [name for name in names if Path(name).name.lower().startswith("avalonia")]
        require(bool(avalonia) == expected, f"Packaged Avalonia assemblies do not match expected={expected}: {avalonia}")
        print(f"PASS {store}: {len(names)} managed assemblies; Avalonia={len(avalonia)}")
    print(f"PASS Android client dependency boundary: {args.apk}")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, OSError, struct.error, zipfile.BadZipFile) as error:
        print(f"FAIL Android client dependency boundary: {error}", file=sys.stderr)
        sys.exit(1)
