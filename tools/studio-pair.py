#!/usr/bin/env python3
"""Assemble and verify a compatible desktop game/Studio release without replacing shared files."""
import argparse
import filecmp
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

METADATA = ".project-prime-desktop.json"


def supported_version(version):
    return isinstance(version, str) and 0 < len(version) <= 128 and re.fullmatch(r"[A-Za-z0-9.\-+_]+", version) is not None


def require_release_version(version):
    if not supported_version(version) or not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version):
        raise ValueError("release version must be a supported X.Y.Z label of at most 128 ASCII characters")


def protocol_version():
    protocol = Path(__file__).resolve().parents[1] / "src/ProjectPrime.Studio.Protocol/StudioProtocol.cs"
    match = re.search(r"public const int StudioIpcVersion\s*=\s*([0-9]+);", protocol.read_text())
    if not match:
        raise ValueError("cannot determine the Studio IPC version")
    return int(match[1])


def metadata(root, version):
    require_release_version(version)
    result = {"Version": 1, "GameVersion": version, "StudioVersion": version, "IpcVersion": protocol_version()}
    root = Path(root)
    root.mkdir(parents=True, exist_ok=True)
    (root / METADATA).write_text(json.dumps(result, separators=(",", ":")) + "\n", encoding="utf-8")
    return result


def merge(game, studio, version):
    require_release_version(version)
    game, studio = Path(game).resolve(), Path(studio).resolve()
    if game == studio or game in studio.parents or studio in game.parents:
        raise ValueError("game and Studio must be published into separate directories")
    extension = ".exe" if (game / "ProjectPrime.exe").is_file() else ""
    if not (game / ("ProjectPrime" + extension)).is_file():
        raise ValueError("desktop game executable is missing")
    if not (studio / ("ProjectPrimeStudio" + extension)).is_file():
        raise ValueError("Studio executable is missing")
    # Preflight every conflict before copying. A second publish may never silently
    # replace the game's native dependencies or a different-version engine file.
    files = sorted(path for path in studio.rglob("*") if path.is_file())
    for source in files:
        if source.is_symlink():
            raise ValueError("portable Studio output contains a symbolic link")
        destination = game / source.relative_to(studio)
        if destination.exists() and (not destination.is_file() or not filecmp.cmp(source, destination, shallow=False)):
            raise ValueError("game/Studio shared publish file differs: " + source.relative_to(studio).as_posix())
    for source in files:
        destination = game / source.relative_to(studio)
        if not destination.exists():
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
    metadata(game, version)
    print(f"{game}: assembled game + Studio {version}")


def smoke(game, studio, expected):
    if not supported_version(expected):
        raise ValueError("unsupported IPC version label")
    # Dedicated user roots isolate diagnostics from an installed game or dirty Studio.
    import tempfile
    with tempfile.TemporaryDirectory(prefix="prime paired release ") as scratch:
        env = dict(os.environ, PROJECT_PRIME_USER_DATA=str(Path(scratch) / "game"),
                   PROJECT_PRIME_STUDIO_USER_DATA=str(Path(scratch) / "studio"))
        game_result = subprocess.run([str(Path(game).resolve()), "-studioversion"], cwd=scratch, env=env,
                                     text=True, capture_output=True, timeout=30, check=True)
        studio_result = subprocess.run([str(Path(studio).resolve()), "--version"], cwd=scratch, env=env,
                                       text=True, capture_output=True, timeout=30, check=True)
        game_data, studio_data = json.loads(game_result.stdout.strip()), json.loads(studio_result.stdout.strip())
        if (game_data.get("gameVersion") != expected or studio_data.get("studioVersion") != expected
                or studio_data.get("gameVersion") != expected
                or game_data.get("studioIpcVersion") != protocol_version()
                or game_data.get("studioIpcVersion") != studio_data.get("studioIpcVersion")):
            raise ValueError(f"published game/Studio version mismatch: {game_data}, {studio_data}; expected {expected}")
        print(f"Published game and Studio both report {expected}; IPC {game_data['studioIpcVersion']}.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    for command in ("merge", "smoke"):
        child = commands.add_parser(command)
        child.add_argument("game"); child.add_argument("studio"); child.add_argument("version")
    child = commands.add_parser("metadata")
    child.add_argument("root"); child.add_argument("version")
    args = parser.parse_args()
    if args.command == "merge": merge(args.game, args.studio, args.version)
    elif args.command == "smoke": smoke(args.game, args.studio, args.version)
    else: metadata(args.root, args.version)


if __name__ == "__main__":
    main()
