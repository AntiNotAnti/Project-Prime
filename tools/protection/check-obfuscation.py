#!/usr/bin/env python3
"""Fail-closed checks for protected assemblies and public client packages."""

from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import tarfile
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from collections import defaultdict
from pathlib import Path, PurePosixPath


EXPECTED_MODULES = (
    "ProjectPrime.dll",
    "ProjectPrime.Game.dll",
    "Server.Shared.dll",
    "ProjectPrime.Replay.dll",
    "Audio.Ncsf.dll",
)

ANDROID_RID_TO_ABI = {
    "android-arm": "armeabi-v7a",
    "android-arm64": "arm64-v8a",
    "android-x86": "x86",
    "android-x64": "x86_64",
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def canonical_module(name: str) -> str | None:
    leaf = name.rsplit(",", 1)[0].strip()
    for expected in EXPECTED_MODULES:
        if leaf.casefold() in {expected.casefold(), Path(expected).stem.casefold()}:
            return expected
    return None


def mapping_stats(path: Path) -> tuple[dict[str, dict[str, int]], list[tuple[str, str]]]:
    stats = {name: {"types": 0, "methods": 0, "fields": 0} for name in EXPECTED_MODULES}
    type_renames: list[tuple[str, str]] = []
    current: str | None = None
    for raw in path.read_text(encoding="utf-8-sig", errors="replace").splitlines():
        line = raw.strip()
        if " -> " not in line or line.lower().startswith("skipped"):
            continue
        before, after = line.split(" -> ", 1)
        if not raw[:1].isspace() and before.startswith("[") and "]" in before:
            identity, original = before[1:].split("]", 1)
            current = canonical_module(identity)
            if current and after.startswith("[") and "]" in after:
                renamed = after.split("]", 1)[1].strip()
                if original.strip() != renamed:
                    stats[current]["types"] += 1
                    type_renames.append((original.strip(), renamed))
                continue
        # Obfuscar v3 MapWriter emits indented members underneath the current
        # [Assembly]Type block. Methods contain a parameter list; fields do not.
        if current and raw[:1].isspace():
            # MapWriter records preserved members in the same indented shape,
            # but their target is an explanation beginning with "skipped".
            # Those entries prove that a rule ran; they are not renames and
            # must never satisfy the fail-closed rename thresholds.
            if after.strip().casefold().startswith("skipped"):
                continue
            kind = "methods" if "(" in before else "fields"
            stats[current][kind] += 1
    return stats, type_renames


def preserved_patterns(config: Path) -> list[str]:
    patterns: list[str] = []
    root = ET.parse(config).getroot()
    for module in root.findall("Module"):
        if Path(module.attrib.get("file", "")).name != "ProjectPrime.dll":
            continue
        patterns.extend(rule.attrib["name"] for rule in module.findall("SkipType") if "name" in rule.attrib)
        patterns.extend(rule.attrib["name"] for rule in module.findall("SkipNamespace") if "name" in rule.attrib)
    return patterns


def preserve_violations(mapping: Path, patterns: list[str]) -> list[str]:
    violations: list[str] = []
    current_type: str | None = None
    current_module: str | None = None
    for raw in mapping.read_text(encoding="utf-8-sig", errors="replace").splitlines():
        line = raw.strip()
        if " -> " not in line:
            continue
        before, after = line.split(" -> ", 1)
        if not raw[:1].isspace() and before.startswith("[") and "]" in before:
            identity, original = before[1:].split("]", 1)
            current_module = canonical_module(identity)
            current_type = original.strip()
            if (current_module == "ProjectPrime.dll" and after.startswith("[")
                    and any(fnmatch.fnmatchcase(current_type, pattern) for pattern in patterns)):
                violations.append(current_type)
        elif (raw[:1].isspace() and current_module == "ProjectPrime.dll" and current_type
              and any(fnmatch.fnmatchcase(current_type, pattern) for pattern in patterns)
              and not after.lower().startswith("skipped")):
            violations.append(f"{current_type} member {before.strip()}")
    return sorted(set(violations))


def configured_preserve_violations(mapping: Path, config: Path) -> list[str]:
    type_patterns: dict[str, list[str]] = defaultdict(list)
    method_patterns: dict[str, list[tuple[str, str]]] = defaultdict(list)
    for module in ET.parse(config).getroot().findall("Module"):
        module_name = Path(module.attrib.get("file", "")).name
        for rule in module:
            if rule.tag in {"SkipType", "SkipNamespace"} and rule.attrib.get("name"):
                type_patterns[module_name].append(rule.attrib["name"])
            elif rule.tag == "SkipMethod" and rule.attrib.get("name"):
                method_patterns[module_name].append(
                    (rule.attrib.get("type", "*"), rule.attrib["name"]))

    violations: list[str] = []
    current_type: str | None = None
    current_module: str | None = None
    for raw in mapping.read_text(encoding="utf-8-sig", errors="replace").splitlines():
        if " -> " not in raw:
            continue
        before, after = raw.strip().split(" -> ", 1)
        skipped = after.strip().casefold().startswith("skipped")
        if not raw[:1].isspace() and before.startswith("[") and "]" in before:
            identity, original = before[1:].split("]", 1)
            current_module = canonical_module(identity)
            current_type = original.strip()
            if (not skipped and current_module
                    and any(fnmatch.fnmatchcase(current_type, pattern)
                            for pattern in type_patterns[current_module])):
                violations.append(f"{current_module}:{current_type}")
            continue
        if not raw[:1].isspace() or skipped or not current_module or not current_type:
            continue
        if any(fnmatch.fnmatchcase(current_type, pattern)
               for pattern in type_patterns[current_module]):
            violations.append(f"{current_module}:{current_type} member {before.strip()}")
            continue
        if "(" not in before:
            continue
        signature = before.rsplit("::", 1)[-1]
        method_name = signature.split("[", 1)[0].split("(", 1)[0].strip()
        if any(fnmatch.fnmatchcase(current_type, type_pattern)
               and method_name == expected_name
               for type_pattern, expected_name in method_patterns[current_module]):
            violations.append(f"{current_module}:{current_type}.{method_name}")
    return sorted(set(violations))


def pe_debug_kinds(path: Path) -> set[int]:
    data = path.read_bytes()
    if len(data) < 0x40 or data[:2] != b"MZ":
        raise ValueError(f"not a PE-format managed assembly: {path}")
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    if pe + 24 > len(data) or data[pe:pe + 4] != b"PE\0\0":
        raise ValueError(f"invalid PE header: {path}")
    sections = struct.unpack_from("<H", data, pe + 6)[0]
    optional_size = struct.unpack_from("<H", data, pe + 20)[0]
    optional = pe + 24
    magic = struct.unpack_from("<H", data, optional)[0]
    directory_base = optional + (112 if magic == 0x20B else 96 if magic == 0x10B else -1)
    if directory_base < optional:
        raise ValueError(f"unknown PE optional-header format: {path}")
    debug_rva, debug_size = struct.unpack_from("<II", data, directory_base + 6 * 8)
    if not debug_rva or not debug_size:
        return set()
    section_base = optional + optional_size
    debug_offset = None
    for index in range(sections):
        offset = section_base + index * 40
        virtual_size, virtual_address, raw_size, raw_pointer = struct.unpack_from("<IIII", data, offset + 8)
        if virtual_address <= debug_rva < virtual_address + max(virtual_size, raw_size):
            debug_offset = raw_pointer + debug_rva - virtual_address
            break
    if debug_offset is None or debug_offset + debug_size > len(data):
        raise ValueError(f"invalid PE debug directory: {path}")
    return {
        struct.unpack_from("<I", data, offset + 12)[0]
        for offset in range(debug_offset, debug_offset + debug_size, 28)
        if offset + 28 <= len(data)
    }


def check_protected(args: argparse.Namespace) -> int:
    input_dir, output_dir = Path(args.input_dir), Path(args.output_dir)
    mapping, config = Path(args.mapping), Path(args.config)
    for path, label in ((mapping, "Mapping.txt"), (config, "Obfuscar config")):
        if not path.is_file() or path.stat().st_size == 0:
            raise ValueError(f"{label} is missing or empty: {path}")
    configured = [Path(item.attrib["file"]).name for item in ET.parse(config).getroot().findall("Module")]
    if sorted(configured) != sorted(EXPECTED_MODULES) or len(configured) != len(EXPECTED_MODULES):
        raise ValueError(f"configured modules are not exactly the expected five: {configured}")
    for module in EXPECTED_MODULES:
        source, protected = input_dir / module, output_dir / module
        if not source.is_file() or not protected.is_file():
            raise ValueError(f"missing input or protected output for {module}")
        if sha256(source) == sha256(protected):
            raise ValueError(f"input and protected output hashes are identical for {module}")
        debug = pe_debug_kinds(protected)
        if debug.intersection({2, 17, 19}):
            raise ValueError(f"protected {module} retains CodeView, embedded-PDB, or PDB-checksum debug metadata")
    extra = sorted(path.name for path in output_dir.glob("*.dll") if path.name not in EXPECTED_MODULES)
    if extra:
        raise ValueError(f"unexpected protected modules: {', '.join(extra)}")
    stats, _ = mapping_stats(mapping)
    violations = configured_preserve_violations(mapping, config)
    if violations:
        raise ValueError(f"preserved types were renamed: {', '.join(violations[:10])}")
    absent = [name for name, values in stats.items() if sum(values.values()) < args.min_module_renames]
    if absent:
        raise ValueError(f"implausibly low/no rename results for: {', '.join(absent)}")
    if sum(value["types"] for value in stats.values()) < args.min_total_types:
        raise ValueError("implausibly low aggregate renamed-type count")
    if sum(value["methods"] for value in stats.values()) < args.min_total_methods:
        raise ValueError("implausibly low aggregate renamed-method count")
    if sum(value["fields"] for value in stats.values()) < args.min_total_fields:
        raise ValueError("implausibly low aggregate renamed-field count")
    for module, values in stats.items():
        print(module)
        print(f"  renamed types:   {values['types']}")
        print(f"  renamed methods: {values['methods']}")
        print(f"  renamed fields:  {values['fields']}")
    return 0


def prohibited(name: str) -> bool:
    path = PurePosixPath(name.replace("\\", "/"))
    lowered = [part.casefold() for part in path.parts]
    leaf = path.name.casefold()
    return (leaf in {"mapping.txt", "obfuscar.xml"}
            or leaf.endswith((".pdb", ".mdb"))
            or "prime-protection" in lowered)


def check_public(args: argparse.Namespace) -> int:
    leaks: list[str] = []
    for value in args.path:
        path = Path(value)
        if not path.exists():
            raise ValueError(f"public package path does not exist: {path}")
        if path.is_dir():
            leaks.extend(str(item) for item in path.rglob("*") if item.is_file() and prohibited(str(item.relative_to(path))))
        elif zipfile.is_zipfile(path):
            with zipfile.ZipFile(path) as archive:
                bad = archive.testzip()
                if bad:
                    raise ValueError(f"corrupt ZIP/APK entry {bad} in {path}")
                leaks.extend(f"{path}:{name}" for name in archive.namelist() if prohibited(name))
        elif tarfile.is_tarfile(path):
            with tarfile.open(path, "r:*") as archive:
                leaks.extend(f"{path}:{item.name}" for item in archive.getmembers()
                             if item.isfile() and prohibited(item.name))
        elif prohibited(path.name):
            leaks.append(str(path))
    if leaks:
        raise ValueError("public package contains protection/debug data: " + ", ".join(leaks[:20]))
    print("Public client package contains no mapping, config, staging, PDB, or MDB files.")
    return 0


def stage_android(args: argparse.Namespace) -> int:
    sources = [Path(value).resolve() for value in args.source]
    grouped: dict[str, dict[str, Path]] = defaultdict(dict)
    for path in sources:
        if path.name in EXPECTED_MODULES:
            rid = next((part for part in reversed(path.parts) if part.startswith("android-")), None)
            if rid is None:
                raise ValueError(f"cannot identify Android RID from linked input path: {path}")
            if rid in grouped[path.name]:
                raise ValueError(f"more than one {path.name} linked input resolved for {rid}")
            grouped[path.name][rid] = path
    missing = [name for name in EXPECTED_MODULES if not grouped[name]]
    if missing:
        raise ValueError(f"Android linker omitted expected modules: {', '.join(missing)}")
    input_root = Path(args.input_root).resolve()
    if input_root.exists():
        shutil.rmtree(input_root)
    input_root.mkdir(parents=True)
    expected_rids = set(grouped[EXPECTED_MODULES[0]])
    routing: dict[str, dict[str, str]] = {rid: {} for rid in sorted(expected_rids)}
    for name in EXPECTED_MODULES:
        if set(grouped[name]) != expected_rids:
            raise ValueError(f"Android RID coverage differs for {name}")
        for rid in sorted(expected_rids):
            source = grouped[name][rid]
            if not source.is_file():
                raise ValueError(f"Android linked input is missing for {name} ({rid})")
            destination = input_root / rid / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
            routing[rid][name] = str(source)
    route_file = Path(args.routing_file).resolve()
    route_file.parent.mkdir(parents=True, exist_ok=True)
    route_file.write_text(json.dumps(routing, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(
        f"Staged {len(EXPECTED_MODULES)} first-party modules independently for "
        f"{len(expected_rids)} Android RIDs from {len(sources)} linked inputs.")
    return 0


def package_android(args: argparse.Namespace) -> int:
    output_root = Path(args.output_root).resolve()
    package_root = Path(args.package_root).resolve()
    routing_file = Path(args.routing_file).resolve()
    if not routing_file.is_file():
        raise ValueError(f"Android routing inventory is missing: {routing_file}")
    routing = json.loads(routing_file.read_text(encoding="utf-8"))
    if not isinstance(routing, dict) or not routing:
        raise ValueError("Android routing inventory contains no RIDs")
    if package_root.exists():
        shutil.rmtree(package_root)
    package_root.mkdir(parents=True)
    copied = 0
    for rid, modules in sorted(routing.items()):
        if rid not in ANDROID_RID_TO_ABI:
            raise ValueError(f"Android routing inventory contains unsupported RID {rid}")
        if not isinstance(modules, dict) or set(modules) != set(EXPECTED_MODULES):
            raise ValueError(f"Android routing inventory is not exactly five modules for {rid}")
        for name in EXPECTED_MODULES:
            protected = output_root / rid / name
            if not protected.is_file():
                raise ValueError(f"per-RID Obfuscar output is missing: {protected}")
            destination = package_root / rid / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(protected, destination)
            if sha256(destination) != sha256(protected):
                raise ValueError(f"protected Android package copy hash mismatch: {destination}")
            copied += 1
    print(
        f"Copied and verified {copied} protected Android modules into one shared "
        f"package root across {len(routing)} RIDs.")
    return 0


def verify_android_routing(args: argparse.Namespace) -> int:
    package_root = Path(args.package_root).resolve()
    output_root = Path(args.output_root).resolve()
    expected_rids = set(args.rid)
    if not expected_rids:
        raise ValueError("no active Android RIDs were supplied")

    grouped: dict[str, dict[str, Path]] = defaultdict(dict)
    sources = [Path(value).resolve() for value in args.source]
    for path in sources:
        try:
            relative = path.relative_to(package_root)
        except ValueError as error:
            raise ValueError(f"Android compression input is outside protection package root: {path}") from error
        if len(relative.parts) != 2:
            raise ValueError(f"Android compression input is not package/<rid>/<module>: {path}")
        rid, name = relative.parts
        if rid not in expected_rids:
            raise ValueError(f"Android compression input uses inactive RID {rid}: {path}")
        if name not in EXPECTED_MODULES:
            raise ValueError(f"unexpected first-party Android compression input: {path}")
        if name in grouped[rid]:
            raise ValueError(f"more than one {name} compression input resolved for {rid}")
        if not path.is_file():
            raise ValueError(f"Android compression input is missing: {path}")
        protected = output_root / rid / name
        if not protected.is_file():
            raise ValueError(f"corresponding Obfuscar output is missing: {protected}")
        if sha256(path) != sha256(protected):
            raise ValueError(f"Android compression input hash does not match Obfuscar output: {path}")
        grouped[rid][name] = path

    if set(grouped) != expected_rids:
        missing_rids = sorted(expected_rids - set(grouped))
        extra_rids = sorted(set(grouped) - expected_rids)
        raise ValueError(
            "Android compression RID coverage differs from active RIDs"
            f"; missing={missing_rids}, extra={extra_rids}")
    for rid in sorted(expected_rids):
        names = set(grouped[rid])
        if names != set(EXPECTED_MODULES) or len(grouped[rid]) != len(EXPECTED_MODULES):
            missing = sorted(set(EXPECTED_MODULES) - names)
            extra = sorted(names - set(EXPECTED_MODULES))
            raise ValueError(
                f"Android compression inputs are not exactly the expected five for {rid}"
                f"; missing={missing}, extra={extra}")

    expected_count = len(expected_rids) * len(EXPECTED_MODULES)
    if len(sources) != expected_count:
        raise ValueError(f"expected {expected_count} Android compression inputs, found {len(sources)}")
    print(
        f"Verified {expected_count} protected pre-compression inputs across "
        f"{len(expected_rids)} Android RIDs.")
    return 0


def lz4_block_decompress(data: bytes, expected_size: int) -> bytes:
    source = 0
    output = bytearray()
    while source < len(data):
        token = data[source]
        source += 1
        literal_length = token >> 4
        if literal_length == 15:
            while True:
                if source >= len(data):
                    raise ValueError("truncated LZ4 literal length")
                value = data[source]
                source += 1
                literal_length += value
                if value != 255:
                    break
        if source + literal_length > len(data):
            raise ValueError("truncated LZ4 literal data")
        output.extend(data[source:source + literal_length])
        source += literal_length
        if source == len(data):
            break
        if source + 2 > len(data):
            raise ValueError("truncated LZ4 match offset")
        offset = struct.unpack_from("<H", data, source)[0]
        source += 2
        if offset == 0 or offset > len(output):
            raise ValueError("invalid LZ4 match offset")
        match_length = token & 0x0F
        if match_length == 15:
            while True:
                if source >= len(data):
                    raise ValueError("truncated LZ4 match length")
                value = data[source]
                source += 1
                match_length += value
                if value != 255:
                    break
        match_length += 4
        for _ in range(match_length):
            output.append(output[-offset])
        if len(output) > expected_size:
            raise ValueError("LZ4 output exceeds declared assembly size")
    if len(output) != expected_size:
        raise ValueError(
            f"LZ4 output length {len(output)} does not match declared size {expected_size}")
    return bytes(output)


def decode_stored_assembly(data: bytes) -> bytes:
    if not data.startswith(b"XALZ"):
        return data
    if len(data) < 12:
        raise ValueError("truncated XALZ assembly header")
    expected_size = struct.unpack_from("<I", data, 8)[0]
    return lz4_block_decompress(data[12:], expected_size)


def parse_assembly_store(data: bytes) -> dict[str, bytes]:
    # Microsoft.Android.Sdk 36.1.69 emits the packed 20-byte Mono store header:
    # magic, version, entry count, index entry count, and index byte size.
    if len(data) < 20:
        raise ValueError("assembly store is smaller than its header")
    magic, version, entry_count, _index_count, index_size = struct.unpack_from("<IIIII", data)
    if magic != 0x41424158:
        raise ValueError("assembly store payload does not begin with XABA")
    if version & 0xFFFF != 3:
        raise ValueError(f"unsupported Android assembly store version 0x{version:08x}")
    if entry_count == 0 or entry_count > 10000:
        raise ValueError(f"implausible Android assembly store entry count: {entry_count}")
    descriptor_offset = 20 + index_size
    names_offset = descriptor_offset + entry_count * 28
    if names_offset > len(data):
        raise ValueError("assembly store descriptors extend beyond payload")
    descriptors = [
        struct.unpack_from("<IIIIIII", data, descriptor_offset + index * 28)
        for index in range(entry_count)
    ]
    names: list[str] = []
    cursor = names_offset
    for _ in range(entry_count):
        if cursor + 4 > len(data):
            raise ValueError("truncated assembly store name length")
        length = struct.unpack_from("<I", data, cursor)[0]
        cursor += 4
        if length == 0 or cursor + length > len(data):
            raise ValueError("invalid assembly store name length")
        try:
            names.append(data[cursor:cursor + length].decode("utf-8"))
        except UnicodeDecodeError as error:
            raise ValueError("assembly store contains an invalid UTF-8 name") from error
        cursor += length
    assemblies: dict[str, bytes] = {}
    for name, descriptor in zip(names, descriptors, strict=True):
        _mapping, data_offset, data_size, debug_offset, debug_size, config_offset, config_size = descriptor
        if debug_offset != 0 or debug_size != 0:
            raise ValueError(f"protected assembly store carries forbidden debug payload for {name}")
        if config_offset != 0 or config_size != 0:
            raise ValueError(f"protected assembly store carries forbidden config payload for {name}")
        if data_size == 0 or data_offset + data_size > len(data):
            raise ValueError(f"invalid assembly store data range for {name}")
        if name in assemblies:
            raise ValueError(f"duplicate Android assembly store entry: {name}")
        assemblies[name] = decode_stored_assembly(data[data_offset:data_offset + data_size])
    return assemblies


def discover_sdk_objcopy() -> Path:
    try:
        result = subprocess.run(
            ["dotnet", "--list-sdks"], capture_output=True, text=True,
            timeout=30, check=False)
    except (OSError, subprocess.TimeoutExpired) as error:
        raise ValueError(f"cannot locate the .NET Android SDK llvm-objcopy: {error}") from error
    if result.returncode != 0:
        raise ValueError(
            "dotnet --list-sdks failed while locating Android SDK llvm-objcopy: "
            + (result.stderr or result.stdout).strip())
    roots: set[Path] = set()
    for line in result.stdout.splitlines():
        match = line.rsplit("[", 1)
        if len(match) == 2 and match[1].endswith("]"):
            sdk_directory = Path(match[1][:-1].strip()).resolve()
            roots.add(sdk_directory.parent)
    candidates: list[Path] = []
    for root in roots:
        candidates.extend(root.glob("packs/Microsoft.Android.Sdk.*/**/llvm-objcopy"))
    executable = sorted(
        (path.resolve() for path in candidates if path.is_file() and os.access(path, os.X_OK)),
        key=str,
    )
    if not executable:
        raise ValueError("installed .NET Android SDK contains no executable llvm-objcopy")
    return executable[-1]


def extract_store_payload(objcopy: Path, wrapper: Path, payload: Path) -> bytes:
    result = subprocess.run(
        [str(objcopy), "--dump-section", f"payload={payload}", str(wrapper)],
        capture_output=True, text=True, timeout=60, check=False)
    if result.returncode != 0 or not payload.is_file():
        raise ValueError(
            f"SDK llvm-objcopy could not extract assembly-store payload from {wrapper}: "
            f"{(result.stderr or result.stdout).strip()}")
    return payload.read_bytes()


def verify_android_apk(args: argparse.Namespace) -> int:
    apk = Path(args.apk).resolve()
    output_root = Path(args.output_root).resolve()
    objcopy = Path(args.objcopy).resolve() if args.objcopy else discover_sdk_objcopy()
    if not apk.is_file() or not zipfile.is_zipfile(apk):
        raise ValueError(f"signed Android package is missing or is not an APK: {apk}")
    if not objcopy.is_file() or not os.access(objcopy, os.X_OK):
        raise ValueError(f"SDK llvm-objcopy is missing or not executable: {objcopy}")
    rids = sorted(path.name for path in output_root.iterdir() if path.is_dir())
    if not rids:
        raise ValueError(f"no per-RID Obfuscar outputs found under {output_root}")
    unsupported = sorted(set(rids) - set(ANDROID_RID_TO_ABI))
    if unsupported:
        raise ValueError(f"unsupported protected Android RIDs: {', '.join(unsupported)}")

    with zipfile.ZipFile(apk) as archive, tempfile.TemporaryDirectory(
            prefix="prime-assembly-store-") as temporary:
        entries = set(archive.namelist())
        expected_stores = {
            rid: f"lib/{ANDROID_RID_TO_ABI[rid]}/libassembly-store.so" for rid in rids
        }
        actual_stores = [
            name for name in archive.namelist()
            if re.fullmatch(r"lib/[^/]+/libassembly-store\.so", name)
        ]
        expected_store_set = set(expected_stores.values())
        if set(actual_stores) != expected_store_set or len(actual_stores) != len(expected_store_set):
            missing = sorted(expected_store_set - set(actual_stores))
            unexpected = sorted(set(actual_stores) - expected_store_set)
            raise ValueError(
                "APK assembly-store ABI set differs from protected outputs"
                f"; missing={missing}, unexpected={unexpected}")
        for rid, entry in expected_stores.items():
            directory = Path(temporary) / rid
            directory.mkdir(parents=True)
            wrapper = directory / "libassembly-store.so"
            payload = directory / "payload.bin"
            wrapper.write_bytes(archive.read(entry))
            assemblies = parse_assembly_store(extract_store_payload(objcopy, wrapper, payload))
            for name in EXPECTED_MODULES:
                protected = output_root / rid / name
                if not protected.is_file():
                    raise ValueError(f"missing corresponding per-RID Obfuscar output: {protected}")
                embedded = assemblies.get(name)
                if embedded is None:
                    raise ValueError(f"{entry} omitted protected module {name}")
                if hashlib.sha256(embedded).digest() != hashlib.sha256(protected.read_bytes()).digest():
                    raise ValueError(f"embedded {name} hash does not match {rid} Obfuscar output")
            print(f"{rid}: verified five protected assemblies in {entry}")
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)
    protected = commands.add_parser("protected")
    protected.add_argument("--input-dir", required=True)
    protected.add_argument("--output-dir", required=True)
    protected.add_argument("--mapping", required=True)
    protected.add_argument("--config", required=True)
    protected.add_argument("--min-module-renames", type=int, default=1)
    protected.add_argument("--min-total-types", type=int, default=5)
    protected.add_argument("--min-total-methods", type=int, default=5)
    protected.add_argument("--min-total-fields", type=int, default=1)
    public = commands.add_parser("public")
    public.add_argument("path", nargs="+")
    android = commands.add_parser("stage-android")
    android.add_argument("--input-root", required=True)
    android.add_argument("--routing-file", required=True)
    android.add_argument("--source", action="append", required=True)
    package = commands.add_parser("package-android")
    package.add_argument("--output-root", required=True)
    package.add_argument("--package-root", required=True)
    package.add_argument("--routing-file", required=True)
    routing = commands.add_parser("verify-android-routing")
    routing.add_argument("--package-root", required=True)
    routing.add_argument("--output-root", required=True)
    routing.add_argument("--rid", action="append", required=True)
    routing.add_argument("--source", action="append", required=True)
    apk = commands.add_parser("verify-android-apk")
    apk.add_argument("--apk", required=True)
    apk.add_argument("--output-root", required=True)
    apk.add_argument(
        "--objcopy",
        help="SDK llvm-objcopy executable (auto-discovered from dotnet --list-sdks when omitted)")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        return {"protected": check_protected, "public": check_public,
                "stage-android": stage_android,
                "package-android": package_android,
                "verify-android-routing": verify_android_routing,
                "verify-android-apk": verify_android_apk}[args.command](args)
    except (OSError, ValueError, ET.ParseError, zipfile.BadZipFile, tarfile.TarError) as exc:
        print(f"client protection check failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
