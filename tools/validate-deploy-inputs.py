#!/usr/bin/env python3
"""Validate a Project Prime Linux deployment bundle and operator config."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path, PurePosixPath
import stat
import struct
import sys
from urllib.parse import urlparse
import uuid


MACHINES = {"linux-x64": 62, "linux-arm64": 183}
REQUIRED = (
    "FruityPrimeServer",
    "worker/FruityPrime.Server.Worker",
    "backend/PrimeHunters.Backend",
)


def remote_path(value: object, label: str) -> str:
    if not isinstance(value, str) or not value or any(ord(char) < 32 for char in value):
        raise ValueError(f"{label} must be a non-empty remote path without control characters")
    path = PurePosixPath(value)
    if not path.is_absolute() or ".." in path.parts:
        raise ValueError(f"{label} must be an absolute normalized remote path")
    if str(path) in ("/", "/home", "/srv", "/opt", "/usr", "/var", "/tmp"):
        raise ValueError(f"{label} is too broad for deployment")
    return str(path)


def read_elf_machine(path: Path) -> int:
    with path.open("rb") as handle:
        header = handle.read(20)
    if len(header) < 20 or header[:4] != b"\x7fELF":
        raise ValueError(f"{path} is not an ELF executable")
    if header[5] == 1:
        return struct.unpack_from("<H", header, 18)[0]
    if header[5] == 2:
        return struct.unpack_from(">H", header, 18)[0]
    raise ValueError(f"{path} has invalid ELF byte order")


def validate_bundle(bundle: Path, rid: str) -> None:
    if rid not in MACHINES:
        raise ValueError(f"unsupported deployment RID: {rid}")
    if not bundle.is_dir() or bundle.is_symlink():
        raise ValueError("bundle must be a real directory, not a symlink")
    for root, directories, files in os.walk(bundle, followlinks=False):
        root_path = Path(root)
        for name in directories + files:
            path = root_path / name
            relative = path.relative_to(bundle).as_posix()
            if any(ord(char) < 32 or ord(char) == 127 for char in relative):
                raise ValueError(f"bundle contains an unsafe file name: {relative!r}")
            mode = path.lstat().st_mode
            if stat.S_ISLNK(mode):
                raise ValueError(f"bundle contains a symlink: {path.relative_to(bundle)}")
            if not (stat.S_ISREG(mode) or stat.S_ISDIR(mode)):
                raise ValueError(f"bundle contains a special file: {path.relative_to(bundle)}")
    for relative in REQUIRED:
        path = bundle / relative
        if not path.is_file():
            raise ValueError(f"bundle is missing {relative}")
        if not os.access(path, os.X_OK):
            raise ValueError(f"bundle executable bit is missing: {relative}")
        machine = read_elf_machine(path)
        if machine != MACHINES[rid]:
            raise ValueError(f"bundle/RID mismatch for {relative}: ELF machine {machine}, expected {MACHINES[rid]}")
    maps = list((bundle / "maps").rglob("*.fpmap")) if (bundle / "maps").is_dir() else []
    if not maps:
        raise ValueError("bundle contains no custom .fpmap map bundles")


def validate_config(config_path: Path, deploy_dir: str, data_dir: str) -> list[str]:
    deploy_dir = remote_path(deploy_dir, "MPH_SERVER_DIR")
    data_dir = remote_path(data_dir, "MPH_SERVER_DATA")
    deploy_path = PurePosixPath(deploy_dir)
    release_root = deploy_path / "releases"
    current_root = deploy_path / "current"

    def persistent_path(value: str, label: str) -> str:
        checked = remote_path(value, label)
        path = PurePosixPath(checked)
        if path == release_root or release_root in path.parents \
                or path == current_root or current_root in path.parents:
            raise ValueError(f"{label} must remain outside the current/release tree")
        return checked

    persistent_path(data_dir, "MPH_SERVER_DATA")
    if not config_path.is_file() or config_path.is_symlink():
        raise ValueError("operator config must be a regular file, not a symlink")
    if config_path.stat().st_size == 0 or config_path.stat().st_size > 2 * 1024 * 1024:
        raise ValueError("operator config must be non-empty and at most 2 MiB")
    try:
        config = json.loads(config_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ValueError(f"operator config is not valid JSON: {error}") from error
    node = config.get("Node") if isinstance(config, dict) else None
    auth = node.get("Authentication") if isinstance(node, dict) else None
    if not isinstance(auth, dict):
        raise ValueError("operator config has no Node.Authentication object")
    for field in ("NodeId", "Issuer"):
        if not isinstance(auth.get(field), str) or not auth[field].strip():
            raise ValueError(f"operator config has no Node.Authentication.{field}")
    try:
        node_id = uuid.UUID(auth["NodeId"])
    except ValueError as error:
        raise ValueError("operator config Node.Authentication.NodeId must be a UUID") from error
    if node_id.int == 0:
        raise ValueError("operator config Node.Authentication.NodeId must be non-empty")
    issuer = urlparse(auth["Issuer"])
    if issuer.scheme.lower() != "https" or not issuer.hostname or issuer.username or issuer.password \
            or issuer.query or issuer.fragment:
        raise ValueError("operator config Node.Authentication.Issuer must be an HTTPS origin")
    keys = auth.get("Keys")
    if not isinstance(keys, list) or not keys:
        raise ValueError("operator config has no Node.Authentication.Keys")
    key_paths: list[str] = []
    for index, key in enumerate(keys):
        if not isinstance(key, dict) or not isinstance(key.get("KeyId"), str) or not key["KeyId"].strip():
            raise ValueError(f"operator config authentication key {index} has no KeyId")
        key_paths.append(persistent_path(key.get("PublicKeyPemPath"), f"authentication key {index} PublicKeyPemPath"))

    workers = node.get("Workers", {}).get("Processes") if isinstance(node.get("Workers"), dict) else None
    if not isinstance(workers, list) or not workers:
        raise ValueError("operator config has no Node.Workers.Processes")
    expected_working = str(PurePosixPath(deploy_dir) / "current")
    expected_maps = str(PurePosixPath(expected_working) / "maps")
    for index, worker in enumerate(workers):
        if not isinstance(worker, dict):
            raise ValueError(f"worker {index} is not an object")
        if worker.get("FileName") != "worker/FruityPrime.Server.Worker":
            raise ValueError(f"worker {index} does not launch the packaged Project Prime Worker")
        for field in ("WorkingDirectory", "ArtifactDirectory"):
            if worker.get(field) is not None:
                label = f"worker {index} {field}"
                checked = remote_path(worker[field], label)
                if field == "WorkingDirectory" and checked != expected_working:
                    raise ValueError(f"worker {index} WorkingDirectory must be {expected_working}")
                if field == "ArtifactDirectory":
                    persistent_path(checked, label)
        arguments = worker.get("Arguments", [])
        if not isinstance(arguments, list) or not all(isinstance(value, str) for value in arguments):
            raise ValueError(f"worker {index} Arguments must be strings")
        saw_content = False
        saw_maps = False
        for offset, value in enumerate(arguments[:-1]):
            if value in ("--content-dir", "--map-dir", "--replay-dir"):
                checked = remote_path(arguments[offset + 1], f"worker {index} {value}")
                if value == "--content-dir":
                    saw_content = True
                    if checked != data_dir:
                        raise ValueError(f"worker {index} --content-dir does not match MPH_SERVER_DATA")
                elif value == "--map-dir":
                    saw_maps = True
                    if checked != expected_maps:
                        raise ValueError(f"worker {index} --map-dir must be {expected_maps}")
                elif value == "--replay-dir":
                    persistent_path(checked, f"worker {index} {value}")
        if not saw_content:
            raise ValueError(f"worker {index} does not declare --content-dir")
        if not saw_maps:
            raise ValueError(f"worker {index} does not declare --map-dir")
    return key_paths


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", required=True, type=Path)
    parser.add_argument("--rid", required=True, choices=sorted(MACHINES))
    parser.add_argument("--config", required=True, type=Path)
    parser.add_argument("--deploy-dir", required=True)
    parser.add_argument("--data-dir", required=True)
    parser.add_argument("--key-path-output", type=Path)
    args = parser.parse_args()
    try:
        bundle = Path(os.path.abspath(args.bundle.expanduser()))
        config = Path(os.path.abspath(args.config.expanduser()))
        validate_bundle(bundle, args.rid)
        key_paths = validate_config(config, args.deploy_dir, args.data_dir)
    except ValueError as error:
        print(f"deployment validation: {error}", file=sys.stderr)
        return 1
    if args.key_path_output:
        args.key_path_output.write_text("".join(path + "\n" for path in key_paths), encoding="utf-8")
    print(f"deployment validation: {args.rid} bundle and operator config are valid")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
