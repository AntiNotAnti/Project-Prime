#!/usr/bin/env python3
"""Create and smoke-check the Project Prime macOS application bundle.

The desktop release remains an extracted self-contained directory so the
signed updater can replace managed files in place. The bundle is a small
launcher around that directory: it supplies Finder/Dock identity and starts
the adjacent published ``ProjectPrime`` executable without duplicating the
runtime, maps, or player-owned data.
"""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import plistlib
import re
import shutil
import stat
import struct
import sys


PRODUCT_NAME = "Project Prime"
BUNDLE_IDENTIFIER = "com.antinotanti.projectprime"
BUNDLE_EXECUTABLE = "ProjectPrime"
ICON_NAME = "project-prime.icns"
ICON_SOURCE_NAME = "project-prime-mark.png"
VERSION_PATTERN = re.compile(r"^(\d+)\.(\d+)\.(\d+)(?:[.-].*)?$")
ROOT = Path(__file__).resolve().parents[1]
DEFAULT_ICON = ROOT / "src" / "Client" / "Assets" / ICON_SOURCE_NAME


def fail(message: str) -> None:
    raise SystemExit(f"macOS package: {message}")


def bundle_version(value: str) -> str:
    """Return Apple's numeric three-part version for release or local builds."""
    match = VERSION_PATTERN.fullmatch(value.removeprefix("v"))
    if match is None:
        fail(f"version is not compatible with macOS bundle metadata: {value!r}")
    return ".".join(match.groups())


def plist_path(app: Path) -> Path:
    return app / "Contents" / "Info.plist"


def load_info(app: Path) -> dict[str, object]:
    info = plist_path(app)
    if not info.is_file():
        fail(f"bundle has no Contents/Info.plist: {app}")
    try:
        with info.open("rb") as handle:
            value = plistlib.load(handle)
    except (OSError, plistlib.InvalidFileException, ValueError) as error:
        fail(f"bundle Info.plist is invalid: {error}")
    if not isinstance(value, dict):
        fail("bundle Info.plist root is not a dictionary")
    return value


def check_bundle(app: Path, expected_version: str | None = None) -> None:
    app = app.resolve()
    if app.suffix != ".app" or not app.is_dir():
        fail(f"expected a .app directory: {app}")
    info = load_info(app)
    required = {
        "CFBundleName": PRODUCT_NAME,
        "CFBundleDisplayName": PRODUCT_NAME,
        "CFBundleIdentifier": BUNDLE_IDENTIFIER,
        "CFBundleExecutable": BUNDLE_EXECUTABLE,
        "CFBundlePackageType": "APPL",
    }
    for key, expected in required.items():
        if info.get(key) != expected:
            fail(f"{key} must be {expected!r}, got {info.get(key)!r}")
    for key in ("CFBundleShortVersionString", "CFBundleVersion"):
        value = info.get(key)
        if not isinstance(value, str) or re.fullmatch(r"\d+\.\d+\.\d+", value) is None:
            fail(f"{key} must be a numeric three-part version")
    if expected_version is not None:
        expected = bundle_version(expected_version)
        if info.get("CFBundleShortVersionString") != expected:
            fail(f"CFBundleShortVersionString does not match {expected}")
        if info.get("CFBundleVersion") != expected:
            fail(f"CFBundleVersion does not match {expected}")

    icon_file = info.get("CFBundleIconFile")
    icon_files = info.get("CFBundleIconFiles")
    if icon_file != ICON_NAME or not isinstance(icon_files, list) or ICON_NAME not in icon_files:
        fail("bundle icon metadata must name project-prime.icns")
    icon = app / "Contents" / "Resources" / ICON_NAME
    if not icon.is_file() or icon.stat().st_size == 0:
        fail("bundle icon file is missing or empty")
    raw_icon = icon.read_bytes()
    if not raw_icon.startswith(b"icns") or len(raw_icon) < 16:
        fail("bundle icon is not a valid ICNS container")
    declared_size = struct.unpack(">I", raw_icon[4:8])[0]
    if declared_size != len(raw_icon):
        fail("bundle icon has an invalid ICNS length")

    executable = app / "Contents" / "MacOS" / BUNDLE_EXECUTABLE
    if not executable.is_file() or not os.access(executable, os.X_OK):
        fail("bundle executable is missing or not executable")
    if not executable.read_bytes().startswith(b"#!/bin/sh\n"):
        fail("bundle executable is not the expected stable launcher wrapper")


def package_bundle(source: Path, output: Path, version: str,
                   icon: Path | None = None) -> Path:
    source = source.resolve()
    output = output.resolve()
    if not source.is_dir():
        fail(f"published macOS directory does not exist: {source}")
    if not (source / "ProjectPrime").is_file():
        fail(f"published macOS directory has no ProjectPrime executable: {source}")
    if output.exists():
        fail(f"refusing to overwrite existing bundle: {output}")
    icon = (icon or DEFAULT_ICON).resolve()
    if not icon.is_file() or icon.stat().st_size == 0:
        fail(f"icon source is missing or empty: {icon}")

    version_value = bundle_version(version)
    macos = output / "Contents" / "MacOS"
    resources = output / "Contents" / "Resources"
    macos.mkdir(parents=True)
    resources.mkdir(parents=True)

    # The wrapper deliberately resolves the published directory relative to
    # the bundle. That keeps a single updater-managed executable and lets a
    # bundle copied elsewhere continue to find its sibling runtime files.
    launcher = "#!/bin/sh\nset -eu\nROOT=$(CDPATH= cd -- \"$(dirname -- \"$0\")/../../..\" && pwd)\ncd -- \"$ROOT\"\nexec \"$ROOT/ProjectPrime\" \"$@\"\n"
    executable = macos / BUNDLE_EXECUTABLE
    executable.write_text(launcher, encoding="utf-8", newline="\n")
    executable.chmod(executable.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
    # ICNS is a small typed container around PNG payloads. Keeping this
    # conversion in the package step means Linux CI can produce the exact
    # same metadata and icon container as a macOS release runner, without a
    # platform-specific image dependency. The source mark is an owned asset.
    png = icon.read_bytes()
    entry = b"ic09" + struct.pack(">I", 8 + len(png)) + png
    icns = b"icns" + struct.pack(">I", 8 + len(entry)) + entry
    (resources / ICON_NAME).write_bytes(icns)

    info = {
        "CFBundleName": PRODUCT_NAME,
        "CFBundleDisplayName": PRODUCT_NAME,
        "CFBundleIdentifier": BUNDLE_IDENTIFIER,
        "CFBundleExecutable": BUNDLE_EXECUTABLE,
        "CFBundlePackageType": "APPL",
        "CFBundleSignature": "PRME",
        "CFBundleDevelopmentRegion": "en",
        "CFBundleShortVersionString": version_value,
        "CFBundleVersion": version_value,
        "CFBundleIconFile": ICON_NAME,
        "CFBundleIconName": "project-prime",
        "CFBundleIconFiles": [ICON_NAME],
        "NSHighResolutionCapable": True,
    }
    with plist_path(output).open("wb") as handle:
        plistlib.dump(info, handle, fmt=plistlib.FMT_XML, sort_keys=False)
    check_bundle(output, version)
    return output


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    package = commands.add_parser("package", help="create a .app bundle")
    package.add_argument("source", type=Path)
    package.add_argument("output", type=Path)
    package.add_argument("--version", required=True)
    package.add_argument("--icon", type=Path)
    check = commands.add_parser("check", help="validate a .app bundle")
    check.add_argument("app", type=Path)
    check.add_argument("--version")
    args = parser.parse_args(argv)
    if args.command == "package":
        path = package_bundle(args.source, args.output, args.version, args.icon)
        print(f"macOS package: validated {path}")
    else:
        check_bundle(args.app, args.version)
        print(f"macOS package: validated {args.app.resolve()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
