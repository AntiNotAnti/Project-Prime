#!/usr/bin/env python3
"""Build/verify manifest-exact authoritative server update ZIPs; never publishes or executes them."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import struct
import tempfile
import zipfile

FAMILY = 2
# Keep update manifests tied to the live authoritative wire identity.  The
# current source protocol is 15; a stale package policy must fail closed
# rather than producing an artifact that cannot be admitted by the server.
PROTOCOL = 15
PACKAGE_LIMIT = 256 * 1024 * 1024
EXPANDED_LIMIT = 512 * 1024 * 1024
FILE_LIMIT = 2048
METADATA_LIMIT = 1024 * 1024
RIDS = {"win-x64": "ProjectPrimeServer.exe", "linux-x64": "ProjectPrimeServer", "linux-arm64": "ProjectPrimeServer"}


def reject(message):
    raise ValueError(message)


def version(tag):
    if not re.fullmatch(r"v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", tag) or tag == "v1.0.0":
        reject("Use a stamped plain vMAJOR.MINOR.PATCH release tag (not the unstamped v1.0.0).")
    return tag


def safe_path(name):
    if not isinstance(name, str) or not name or len(name) > 240 or any(ord(c) < 32 or ord(c) == 127 for c in name):
        reject("Invalid update path.")
    if "\\" in name or ":" in name or name.startswith("/"):
        reject("Absolute/alternate update path.")
    for part in name.split("/"):
        if not part or part.startswith(".") or part.endswith((".", " ")) or re.match(r"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", part, re.I):
            reject("Unsafe update path component.")
    return name


def allowed_publish_file(name, rid):
    safe_path(name)
    if name.startswith("maps/"):
        return Path(name).suffix.lower() in {".fpmap", ".bsp", ".tex", ".json"}
    if "/" in name:
        return False
    return (name in {RIDS[rid], "LICENSE", "SERVER.txt"}
            or name.endswith((".dll", ".deps.json", ".runtimeconfig.json", ".dylib"))
            or re.fullmatch(r"[A-Za-z0-9_.+-]+\.so(?:\.[0-9]+)*", name) is not None)


def sha_stream(stream, limit):
    digest = hashlib.sha256()
    total = 0
    while chunk := stream.read(64 * 1024):
        total += len(chunk)
        if total > limit:
            reject("File exceeds its declared bound.")
        digest.update(chunk)
    return total, digest.hexdigest()


def sha_file(path, limit):
    with path.open("rb") as stream:
        return sha_stream(stream, limit)


def check_binary(path, rid):
    """Read bounded native headers: server PE console subsystem or 64-bit Linux ELF architecture."""
    with path.open("rb") as stream:
        header = stream.read(64)
        if rid.startswith("linux-"):
            expected = 62 if rid == "linux-x64" else 183
            if (len(header) < 64 or header[:6] != b"\x7fELF\x02\x01" or header[7] not in {0, 3}
                    or struct.unpack_from("<H", header, 16)[0] not in {2, 3}
                    or struct.unpack_from("<H", header, 18)[0] != expected):
                reject("Published binary architecture does not match the requested Linux RID.")
        else:
            if len(header) < 64 or header[:2] != b"MZ":
                reject("Windows server binary is not PE.")
            offset = struct.unpack_from("<I", header, 60)[0]
            if offset > 1024 * 1024:
                reject("Unbounded PE header offset.")
            stream.seek(offset)
            pe = stream.read(94)
            if (len(pe) < 94 or pe[:4] != b"PE\0\0" or struct.unpack_from("<H", pe, 4)[0] != 0x8664
                    or struct.unpack_from("<H", pe, 22)[0] & 0x2002 != 2
                    or struct.unpack_from("<H", pe, 24)[0] != 0x20B or struct.unpack_from("<H", pe, 92)[0] != 3):
                reject("Windows update needs an x64 console server executable.")


def check_source_identity():
    root = Path(__file__).resolve().parents[1]
    header = (root / "src/Game/Protocol/NetHeader.cs").read_text()
    identity = (root / "src/Game/Protocol/NetWireIdentity.cs").read_text()
    if not re.search(rf"public const byte Version\s*=\s*{PROTOCOL}\s*;", header) or "Family = NetWireFamily.Authoritative" not in identity:
        reject("Update manifest policy does not match this source wire identity; review the protocol migration.")


def build(publish, output, rid, tag, repository):
    version(tag)
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) or repository.lower() == "antinotanti/project-prime":
        reject("Configure an explicit authoritative fork release repository.")
    if rid not in RIDS:
        reject("Unsupported server update RID.")
    check_source_identity()
    if publish.is_symlink() or not publish.is_dir():
        reject("Publish output must be a regular directory.")
    executable = publish / RIDS[rid]
    if not executable.is_file() or executable.is_symlink():
        reject("Expected published server executable is missing.")
    check_binary(executable, rid)
    files = []
    names = set()
    total = 0
    for path in sorted(publish.rglob("*")):
        if path.is_symlink():
            reject("Publish output contains a link.")
        if path.is_dir():
            continue
        if not stat.S_ISREG(path.stat().st_mode):
            reject("Publish output contains a nonregular file.")
        relative = path.relative_to(publish).as_posix()
        if not allowed_publish_file(relative, rid) or relative.casefold() in names:
            reject("Unexpected or duplicate publish file: " + relative)
        names.add(relative.casefold())
        length, digest = sha_file(path, EXPANDED_LIMIT)
        total += length
        if total > EXPANDED_LIMIT or len(files) >= FILE_LIMIT:
            reject("Publish output exceeds update bounds.")
        files.append({"Path": relative, "Bytes": length, "Sha256": digest})
    output.mkdir(parents=True, exist_ok=True)
    package_name = f"ProjectPrime-authoritative-{tag}-server-{rid}.zip"
    manifest_name = f"authoritative-update-{rid}-server.json"
    if (output / package_name).exists() or (output / manifest_name).exists():
        reject("Refusing to overwrite an existing update artifact.")
    with tempfile.TemporaryDirectory(prefix=".server-update-package-", dir=output) as work:
        temporary = Path(work)
        archive = temporary / package_name
        with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as package:
            for file in files:
                info = zipfile.ZipInfo(file["Path"], date_time=(1980, 1, 1, 0, 0, 0))
                info.create_system = 3
                info.external_attr = (stat.S_IFREG | (0o755 if file["Path"] == RIDS[rid] else 0o644)) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                with (publish / file["Path"]).open("rb") as source, package.open(info, "w") as target:
                    while chunk := source.read(64 * 1024):
                        target.write(chunk)
        length, digest = sha_file(archive, PACKAGE_LIMIT)
        manifest = {"Format": 1, "Family": FAMILY, "Protocol": PROTOCOL, "Version": tag,
                    "Rid": rid, "Package": package_name, "PackageBytes": length, "PackageSha256": digest,
                    "Executable": RIDS[rid], "Files": files}
        manifest_path = temporary / manifest_name
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        verify(manifest_path, temporary, rid, tag)
        os.replace(archive, output / package_name)
        os.replace(manifest_path, output / manifest_name)
    return output / manifest_name


def verify(manifest_path, directory, rid, tag):
    version(tag)
    if rid not in RIDS or manifest_path.is_symlink() or manifest_path.stat().st_size > METADATA_LIMIT:
        reject("Invalid manifest source or RID.")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if (any(type(manifest.get(field)) is not int for field in ("Format", "Family", "Protocol"))
            or manifest.get("Format") != 1 or manifest.get("Family") != FAMILY or manifest.get("Protocol") != PROTOCOL
            or manifest.get("Version") != tag or manifest.get("Rid") != rid or manifest.get("Executable") != RIDS[rid]):
        reject("Manifest identity does not match authoritative build/version/RID.")
    name = safe_path(manifest.get("Package"))
    if "/" in name or not name.endswith(".zip"):
        reject("Invalid package asset name.")
    package = directory / name
    if package.is_symlink():
        reject("Update package cannot be a link.")
    length, digest = sha_file(package, PACKAGE_LIMIT)
    if length != manifest.get("PackageBytes") or digest != manifest.get("PackageSha256"):
        reject("Package size/hash mismatch.")
    files = manifest.get("Files")
    if not isinstance(files, list) or not 1 <= len(files) <= FILE_LIMIT:
        reject("Invalid manifest file count.")
    expected = {}
    names = set()
    total = 0
    for file in files:
        name = safe_path(file.get("Path"))
        if (not allowed_publish_file(name, rid) or name.casefold() in names or type(file.get("Bytes")) is not int
                or not 0 <= file["Bytes"] <= EXPANDED_LIMIT or not re.fullmatch(r"[a-f0-9]{64}", file.get("Sha256", ""))):
            reject("Invalid manifest file.")
        total += file["Bytes"]
        names.add(name.casefold())
        expected[name] = file
    if total > EXPANDED_LIMIT or RIDS[rid] not in expected:
        reject("Invalid expanded package bounds or missing executable.")
    with zipfile.ZipFile(package) as archive:
        if len(archive.infolist()) != len(expected):
            reject("Archive and manifest entries differ.")
        for entry in archive.infolist():
            safe_path(entry.filename)
            mode = entry.external_attr >> 16
            if stat.S_IFMT(mode) not in {0, stat.S_IFREG} or entry.external_attr & 1024:
                reject("Archive contains a link/nonregular entry.")
            file = expected.pop(entry.filename, None)
            if file is None or entry.file_size != file["Bytes"]:
                reject("Unexpected archive entry or declared size.")
            with archive.open(entry) as source:
                length, digest = sha_stream(source, file["Bytes"])
            if length != file["Bytes"] or digest != file["Sha256"]:
                reject("Archive file hash mismatch.")
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    create = commands.add_parser("build")
    create.add_argument("--publish", type=Path, required=True)
    create.add_argument("--output", type=Path, required=True)
    create.add_argument("--repository", required=True)
    check = commands.add_parser("verify")
    check.add_argument("--manifest", type=Path, required=True)
    check.add_argument("--directory", type=Path, required=True)
    for command in (create, check):
        command.add_argument("--rid", choices=RIDS, required=True)
        command.add_argument("--tag", required=True)
    args = parser.parse_args()
    try:
        if args.command == "build":
            result = build(args.publish, args.output, args.rid, args.tag, args.repository)
            print(f"Verified authoritative update manifest: {result}")
        else:
            verify(args.manifest, args.directory, args.rid, args.tag)
            print(f"Verified authoritative update: {args.rid} {args.tag}")
    except (ValueError, OSError, KeyError, TypeError, zipfile.BadZipFile) as error:
        parser.exit(1, f"Refused update artifact: {error}\n")


if __name__ == "__main__":
    main()
