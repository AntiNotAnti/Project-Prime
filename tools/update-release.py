#!/usr/bin/env python3
"""Build and verify the public Project Prime player update contract.

This tool is deliberately distribution-only: it never reads source history and
never uploads anything. CI calls it after each publish directory is complete.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import io
import json
import re
import subprocess
import tarfile
import zipfile
from pathlib import Path


RIDS = ("win-x64", "linux-x64", "osx-x64", "osx-arm64", "android")
PLAYER_DIRECTORY_NAMES = {
    "files", "content", "settings", "saves", "screenshots", "_screenshots",
    "logs", "replays", "_replays",
}
MAX_FILES = 100_000
MAX_PATH = 1024
MAX_PACKAGE = 8 * 1024 * 1024 * 1024
SHA256 = re.compile(r"^[0-9a-fA-F]{64}$")
VERSION = re.compile(r"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$")


def fail(message: str) -> None:
    raise SystemExit(f"update release contract: {message}")


def safe_path(value: str) -> str:
    if not isinstance(value, str) or not value or len(value.encode("utf-8")) > MAX_PATH \
            or value.startswith(("/", "\\")):
        fail(f"unsafe managed path {value!r}")
    if "\\" in value or ":" in value or "\x00" in value:
        fail(f"unsafe managed path {value!r}")
    parts = value.split("/")
    if any(not part or part in (".", "..") for part in parts):
        fail(f"unsafe managed path {value!r}")
    reserved = {"con", "prn", "aux", "nul"}
    for part in parts:
        if part.endswith((".", " ")):
            fail(f"unsafe managed path {value!r}")
        stem = part.split(".", 1)[0].lower()
        if stem in reserved or (len(stem) == 4 and stem[:3] in ("com", "lpt") and stem[3] in "123456789"):
            fail(f"reserved managed path {value!r}")
        if any(ord(c) < 32 or c in '<>"|?*' for c in part):
            fail(f"unsafe managed path {value!r}")
    if parts[0].casefold() == ".update":
        fail(f"reserved managed path {value!r}")
    return "/".join(parts)


def never_managed(value: str) -> bool:
    normalized = value.replace("\\", "/").strip("/")
    if normalized.casefold() in {
        "paths.txt", "settings.json", "controls.txt", "launcher.txt",
    }:
        return True
    return any(part.casefold() in PLAYER_DIRECTORY_NAMES
               for part in normalized.split("/"))


def digest(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def required_desktop_components(directory: Path) -> tuple[str, str]:
    """Resolve and require one complete platform-specific desktop product."""
    if (directory / "ProjectPrime.exe").is_file():
        required = ("ProjectPrime.exe", "editor/ProjectPrime.Editor.exe")
    elif (directory / "ProjectPrime").is_file():
        required = ("ProjectPrime", "editor/ProjectPrime.Editor")
    else:
        fail(f"desktop package has no Project Prime client executable: {directory}")
    for relative in required:
        if not (directory / relative).is_file():
            fail(f"desktop package is missing required component {relative}")
    return required


def managed_files(directory: Path) -> list[dict[str, str]]:
    required_desktop_components(directory)
    entries: list[dict[str, str]] = []
    seen: set[str] = set()
    total = 0
    for path in sorted(directory.rglob("*")):
        if path.is_symlink():
            fail(f"package contains link {path}")
        if not path.is_file():
            continue
        if ".update" in path.relative_to(directory).parts:
            continue
        relative = safe_path(path.relative_to(directory).as_posix())
        if never_managed(relative):
            # A package may carry a paths.txt/settings template, but it is
            # deliberately absent from release-files.json and is never
            # installed over a player's copy.
            continue
        if relative.lower() == "release-files.json":
            continue
        key = relative.casefold()
        if key in seen:
            fail(f"duplicate managed path {relative}")
        seen.add(key)
        size = path.stat().st_size
        total += size
        if size > 512 * 1024 * 1024:
            fail(f"managed file is too large {relative}")
        if len(entries) >= MAX_FILES or total > 8 * 1024 * 1024 * 1024:
            fail("managed file count or extracted size exceeds cap")
        entries.append({"path": relative, "sha256": digest(path)})
    paths = {entry["path"].casefold() for entry in entries}
    for entry in entries:
        pieces = entry["path"].split("/")
        for index in range(1, len(pieces)):
            if "/".join(pieces[:index]).casefold() in paths:
                fail(f"managed path has an ancestor file {entry['path']}")
    if not entries:
        fail(f"publish directory is empty: {directory}")
    return entries


def write_release_files(directory: Path, version: str) -> None:
    if not VERSION.fullmatch(version):
        fail(f"version is not X.Y.Z: {version}")
    manifest = {"schemaVersion": 1, "version": version, "files": managed_files(directory)}
    (directory / "release-files.json").write_bytes(
        json.dumps(manifest, separators=(",", ":"), ensure_ascii=True).encode("utf-8")
    )


def normalize_archive_name(value: str) -> str:
    normalized = value.replace("\\", "/")
    while normalized.startswith("./"):
        normalized = normalized[2:]
    return normalized.rstrip("/")


def validate_release_files(raw: bytes, version: str) -> dict[str, str]:
    if not raw or len(raw) > 256 * 1024:
        fail("release-files.json is empty or too large")
    try:
        data = json.loads(raw)
    except json.JSONDecodeError as error:
        fail(f"release-files.json is invalid: {error}")
    if not isinstance(data, dict):
        fail("release-files.json root is invalid")
    if set(data) != {"schemaVersion", "version", "files"} or data["schemaVersion"] != 1:
        fail("release-files.json schema is invalid")
    if data["version"] != version or not VERSION.fullmatch(data["version"]):
        fail("release-files.json version does not match the release")
    if not isinstance(data["files"], list) or not data["files"] or len(data["files"]) > MAX_FILES:
        fail("release-files.json file list is invalid")
    files: dict[str, str] = {}
    total_path_bytes = 0
    for entry in data["files"]:
        if not isinstance(entry, dict) or set(entry) != {"path", "sha256"}:
            fail("release-files.json entry is invalid")
        path = entry["path"]
        safe_path(path)
        if path.casefold() == "release-files.json":
            fail("release-files.json cannot manage its own metadata")
        if never_managed(path):
            fail(f"release-files.json manages player-owned path {path!r}")
        if path.casefold() in files:
            fail(f"release-files.json contains duplicate path {path!r}")
        digest_value = entry["sha256"]
        if not isinstance(digest_value, str) or not SHA256.fullmatch(digest_value):
            fail(f"release-files.json hash is invalid for {path!r}")
        files[path.casefold()] = digest_value.lower()
        total_path_bytes += len(path)
        if total_path_bytes > MAX_FILES * MAX_PATH:
            fail("release-files.json metadata is too large")
    for path in files:
        pieces = path.split("/")
        for index in range(1, len(pieces)):
            if "/".join(pieces[:index]).casefold() in files:
                fail(f"release-files.json has an ancestor-file conflict for {path!r}")
    return files


def hash_stream(stream: io.BufferedIOBase) -> str:
    hasher = hashlib.sha256()
    total = 0
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
        total += len(chunk)
        if total > 512 * 1024 * 1024:
            fail("archive entry is too large")
        hasher.update(chunk)
    return hasher.hexdigest()


def verify_desktop_archive(package: Path, version: str, rid: str) -> None:
    executable = "ProjectPrime.exe" if rid == "win-x64" else "ProjectPrime"
    editor_executable = "editor/ProjectPrime.Editor.exe" if rid == "win-x64" \
        else "editor/ProjectPrime.Editor"
    entries: dict[str, tuple[str, object, int]] = {}
    archive_paths: dict[str, bool] = {}
    total = 0
    metadata: bytes | None = None

    def add_entry(name: str, info: object, size: int, link: bool,
                  is_directory: bool = False) -> None:
        nonlocal total, metadata
        normalized = normalize_archive_name(name)
        if not normalized:
            return
        safe_path(normalized)
        key = normalized.casefold()
        if key in archive_paths:
            fail(f"archive contains duplicate path {normalized!r}")
        if link:
            fail(f"archive contains link {normalized!r}")
        archive_paths[key] = is_directory
        if len(archive_paths) > MAX_FILES + 1024:
            fail("archive contains too many entries")
        if is_directory:
            return
        if size > 512 * 1024 * 1024:
            fail(f"archive entry is too large {normalized!r}")
        total += size
        if total > 8 * 1024 * 1024 * 1024:
            fail("archive extracted size exceeds cap")
        entries[key] = (normalized, info, size)

    if package.suffix == ".zip":
        with zipfile.ZipFile(package) as archive:
            for info in archive.infolist():
                name = normalize_archive_name(info.filename)
                if not name:
                    continue
                if info.is_dir():
                    add_entry(info.filename, info, 0, False, is_directory=True)
                    continue
                mode = (info.external_attr >> 16) & 0xF000
                add_entry(info.filename, info, info.file_size, mode == 0xA000)
            metadata_entry = entries.get("release-files.json")
            if metadata_entry is None:
                fail(f"{package.name} has no root release-files.json")
            with archive.open(metadata_entry[1]) as stream:  # type: ignore[arg-type]
                metadata = stream.read(256 * 1024 + 1)
            files = validate_release_files(metadata, version)
            for key, expected in files.items():
                entry = entries.get(key)
                if entry is None:
                    fail(f"{package.name} is missing managed file {key!r}")
                with archive.open(entry[1]) as stream:  # type: ignore[arg-type]
                    actual = hash_stream(stream)
                if actual != expected:
                    fail(f"{package.name} hash mismatch for {entry[0]!r}")
    else:
        with tarfile.open(package, mode="r:gz") as archive:
            for info in archive.getmembers():
                name = normalize_archive_name(info.name)
                if not name:
                    continue
                if info.isdir():
                    add_entry(info.name, info, 0, False, is_directory=True)
                    continue
                add_entry(info.name, info, info.size, not info.isfile())
            metadata_entry = entries.get("release-files.json")
            if metadata_entry is None:
                fail(f"{package.name} has no root release-files.json")
            metadata_info = metadata_entry[1]
            metadata_stream = archive.extractfile(metadata_info)  # type: ignore[arg-type]
            if metadata_stream is None:
                fail(f"{package.name} release-files.json is unreadable")
            with metadata_stream:
                metadata = metadata_stream.read(256 * 1024 + 1)
            files = validate_release_files(metadata, version)
            for key, expected in files.items():
                entry = entries.get(key)
                if entry is None:
                    fail(f"{package.name} is missing managed file {key!r}")
                stream = archive.extractfile(entry[1])  # type: ignore[arg-type]
                if stream is None:
                    fail(f"{package.name} managed file is unreadable {entry[0]!r}")
                with stream:
                    actual = hash_stream(stream)
                if actual != expected:
                    fail(f"{package.name} hash mismatch for {entry[0]!r}")

    for required in (executable, editor_executable):
        required_key = required.casefold()
        if required_key not in entries or required_key not in files:
            fail(f"{package.name} does not manage required component {required}")
    for key, (name, _, _) in entries.items():
        pieces = key.split("/")
        for index in range(1, len(pieces)):
            if "/".join(pieces[:index]) in entries:
                fail(f"{package.name} has an ancestor-file conflict for {name!r}")
    allowed = set(files) | {"release-files.json"}
    for key, (name, _, _) in entries.items():
        if key not in allowed and not never_managed(name):
            fail(f"{package.name} contains unlisted file {name!r}")


def package_metadata(dist: Path, version: str, output: Path, published_utc: str) -> dict:
    if not VERSION.fullmatch(version):
        fail(f"version is not X.Y.Z: {version}")
    packages = []
    for rid in RIDS:
        suffix = ".apk" if rid == "android" else (".zip" if rid == "win-x64" else ".tar.gz")
        candidates = sorted(dist.glob(f"ProjectPrime-v{version}-{rid}{suffix}"))
        if len(candidates) != 1:
            fail(f"expected exactly one {rid} package, found {len(candidates)}")
        package = candidates[0]
        size = package.stat().st_size
        if size <= 0 or size > MAX_PACKAGE:
            fail(f"invalid package size for {package.name}")
        if rid != "android":
            verify_desktop_archive(package, version, rid)
        packages.append({"rid": rid, "fileName": package.name, "size": size, "sha256": digest(package)})
    manifest = {
        "schemaVersion": 1,
        "channel": "stable",
        "version": version,
        "publishedUtc": published_utc,
        "packages": packages,
    }
    output.mkdir(parents=True, exist_ok=True)
    manifest_path = output / "update-manifest.json"
    manifest_path.write_bytes(json.dumps(manifest, separators=(",", ":"), ensure_ascii=True).encode("utf-8"))
    return manifest


def verify_manifest(path: Path, public_key: Path | None = None,
                    signature: Path | None = None) -> dict:
    raw = path.read_bytes()
    if len(raw) == 0 or len(raw) > 64 * 1024:
        fail("manifest size is outside bounds")
    try:
        data = json.loads(raw)
    except json.JSONDecodeError as error:
        fail(f"manifest JSON is invalid: {error}")
    if not isinstance(data, dict):
        fail("manifest root is invalid")
    if data.get("schemaVersion") != 1 or data.get("channel") != "stable":
        fail("manifest schema/channel is invalid")
    if not VERSION.fullmatch(data.get("version", "")):
        fail("manifest version is invalid")
    published_utc = data.get("publishedUtc", "")
    try:
        parsed_time = datetime.fromisoformat(published_utc.replace("Z", "+00:00"))
    except (AttributeError, TypeError, ValueError):
        fail("manifest publication time is not UTC")
    if parsed_time.tzinfo is None or parsed_time.utcoffset() != timezone.utc.utcoffset(parsed_time):
        fail("manifest publication time is not UTC")
    if parsed_time == datetime(1970, 1, 1, tzinfo=timezone.utc):
        fail("manifest publication time must be real, not the epoch sentinel")
    packages = data.get("packages")
    if not isinstance(packages, list) or len(packages) != len(RIDS):
        fail("manifest does not contain one package for every supported RID")
    seen: set[str] = set()
    for package in packages:
        if not isinstance(package, dict):
            fail("manifest package entry is invalid")
        if package.get("rid") not in RIDS or package["rid"] in seen:
            fail("manifest contains an unknown or duplicate RID")
        seen.add(package["rid"])
        safe_path(package.get("fileName", ""))
        if not isinstance(package.get("size"), int) or package["size"] <= 0 or package["size"] > MAX_PACKAGE:
            fail("manifest package size is invalid")
        if not SHA256.fullmatch(package.get("sha256", "")):
            fail("manifest package hash is invalid")
    if seen != set(RIDS):
        fail("manifest is missing a supported RID")
    if public_key is not None:
        signature = signature or path.with_suffix(".sig")
        if not signature.is_file() or signature.stat().st_size == 0 or signature.stat().st_size > 256:
            fail("manifest signature is missing or invalid")
        result = subprocess.run(
            ["openssl", "dgst", "-sha256", "-verify", str(public_key), "-signature", str(signature), str(path)],
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, check=False,
        )
        if result.returncode != 0 or result.stdout.strip() != "Verified OK":
            fail("manifest signature self-test failed")
    return data


def verify_manifest_assets(manifest_path: Path, asset_directory: Path,
                           public_key: Path, signature: Path | None = None) -> None:
    data = verify_manifest(manifest_path, public_key, signature)
    for package in data["packages"]:
        asset = asset_directory / package["fileName"]
        if not asset.is_file():
            fail(f"downloaded public draft is missing {package['fileName']}")
        if asset.stat().st_size != package["size"]:
            fail(f"public draft size mismatch for {asset.name}")
        if digest(asset) != package["sha256"]:
            fail(f"public draft hash mismatch for {asset.name}")
        if package["rid"] != "android":
            verify_desktop_archive(asset, data["version"], package["rid"])


def sign_manifest(path: Path, key: Path) -> None:
    signature = path.with_suffix(".sig")
    result = subprocess.run(
        ["openssl", "dgst", "-sha256", "-sign", str(key), "-out", str(signature), str(path)],
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False,
    )
    if result.returncode != 0:
        fail("openssl could not sign the exact manifest bytes")
    if not 0 < signature.stat().st_size <= 256:
        fail("generated signature is outside bounds")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--version")
    parser.add_argument("--dist", type=Path)
    parser.add_argument("--desktop-dir", action="append", type=Path, default=[])
    parser.add_argument("--output", type=Path)
    parser.add_argument("--signing-key", type=Path)
    parser.add_argument("--public-key", type=Path)
    parser.add_argument("--published-utc")
    parser.add_argument("--verify-manifest", type=Path)
    parser.add_argument("--signature", type=Path)
    parser.add_argument("--asset-dir", type=Path)
    args = parser.parse_args()
    if args.verify_manifest is not None:
        if args.public_key is None or args.asset_dir is None:
            parser.error("--verify-manifest requires --public-key and --asset-dir")
        verify_manifest_assets(args.verify_manifest, args.asset_dir,
                               args.public_key, args.signature)
        return
    if not args.version or not args.output:
        parser.error("--version and --output are required when creating a manifest")
    published_utc = args.published_utc or datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")
    try:
        parsed_time = datetime.fromisoformat(published_utc.replace("Z", "+00:00"))
    except ValueError:
        fail("--published-utc must be an RFC3339 UTC timestamp")
    if parsed_time.tzinfo is None or parsed_time.utcoffset() != timezone.utc.utcoffset(parsed_time):
        fail("--published-utc must be an RFC3339 UTC timestamp")
    for directory in args.desktop_dir:
        write_release_files(directory, args.version)
    if args.dist is None:
        return
    package_metadata(args.dist, args.version, args.output, published_utc)
    if args.signing_key is None:
        fail("official releases require --signing-key")
    sign_manifest(args.output / "update-manifest.json", args.signing_key)
    verify_manifest(args.output / "update-manifest.json", args.public_key)


if __name__ == "__main__":
    main()
