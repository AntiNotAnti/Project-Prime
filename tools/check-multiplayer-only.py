#!/usr/bin/env python3
"""Reject retired campaign runtime code in C# sources (standard library only).

Run from any directory: python3 tools/check-multiplayer-only.py [--root REPOSITORY].
All src/**/*.cs and tests/**/*.cs files are checked, including future split projects such as src/Game.
Only generated bin/obj and .git directories are excluded. Exceptions below are exact
repository-relative files, scoped to one rule; they never exempt a directory or
allow an unrelated banned API. Raw schema names such as EnemySpawnEntityData and
ArtifactEntityEditor are distinct identifiers, not runtime class exceptions.

Comments and ordinary string/character literals are masked with line breaks intact.
Interpolated strings are conservatively checked, including embedded expressions.
Exit status: 0 clean, 1 violations, 2 invalid root or unreadable source.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
import os
from pathlib import Path
import re
import sys


@dataclass(frozen=True)
class Rule:
    name: str
    pattern: str
    allowed_files: frozenset[str] = frozenset()


# Negative tests deliberately name rejected enum values. The shared content packer
# preserves original cartridge layer selectors; neither exception permits campaign
# launches.
RAW_MODE_FILES = frozenset({
    "src/MapPlatform/ContentPreparation/RepackModelPacking.cs",
    "tests/Tests/Match/MatchDomainTests.cs",
    "tests/Tests/Match/RotationRulesTests.cs",
})
# These are raw HUD metadata and the independent asset viewer, not player scanning.
VIEWER_SCAN_FILES = frozenset({
    "src/Client/HUD/HudInfo.cs",
    "src/Client/Rendering/Renderer.cs",
    "src/Client/Rendering/Entities/ObjectEntityPresentation.cs",
})
RULES = (
    Rule("campaign-save", r"\bStorySave\b"),
    # Retired campaign memory layouts must not return to the multiplayer source.
    Rule("raw-story-layout", r"\bStorySaveData\b"),
    Rule("raw-enemy-identity", r"\bEnemyInstance\b", frozenset({
        "src/Game/Content/Formats/Enums.cs",
    })),
    # The codec and explicit export CLI survive; campaign movie playback does not.
    Rule("raw-movie-codec", r"\bVxDecoder\b", frozenset({
        "src/Tools/Conversion/Movie.cs", "src/Tools/Program.cs",
    })),
    Rule("adventure-mode-flow", r"\bModeStateAdventure\b"),
    Rule("campaign-launch", r"\bLaunchKind\s*\.\s*@?(?:Adventure|Offline)\b"),
    Rule("global-game-mode", r"\bGameState\s*\.\s*@?(?:SinglePlayer|Multiplayer|Mode)\b"),
    Rule("campaign-mode-selector", r"\bGameMode\s*\.\s*@?SinglePlayer\b", RAW_MODE_FILES),
    Rule("deleted-entity-runtime", r"\b(?:EnemyInstanceEntity|EnemySpawnEntity|ArtifactEntity)\b"),
    Rule("player-scan-dialog-runtime", r"\b(?:PlayerScan|PlayerDialog|DialogType|ResetCombatVisor|ResetScanVisor|ProcessScan|UpdateScan)\b"),
    Rule("player-scan-visor", r"\bScanVisor\b", VIEWER_SCAN_FILES),
    Rule("player-scan-input", r"\bKeybind\s+@?Scan\b"),
)
BANNED_FILE_NAMES = frozenset({"playerscan.cs", "playerdialog.cs"})
GENERATED_DIRECTORIES = frozenset({"bin", "obj", ".git"})


def mask_non_code(source: str) -> str:
    """Mask comments/literals without changing offsets used in diagnostics."""
    result = list(source)
    size = len(source)
    index = 0

    def mask(start: int, end: int) -> None:
        for offset in range(start, end):
            if result[offset] not in "\r\n":
                result[offset] = " "

    while index < size:
        start = index
        if source.startswith("//", index):
            end = source.find("\n", index + 2)
            index = size if end == -1 else end
            mask(start, index)
            continue
        if source.startswith("/*", index):
            end = source.find("*/", index + 2)
            index = size if end == -1 else end + 2
            mask(start, index)
            continue
        prefix = re.match(r'(?:\$+@?|@\$?)?"', source[index:]) if source[index] in '$@"' else None
        if prefix:
            prefix_text = prefix.group()
            quote = index + len(prefix_text) - 1
            quotes = 1
            while quote + quotes < size and source[quote + quotes] == '"':
                quotes += 1
            if quotes >= 3:
                end = source.find('"' * quotes, quote + quotes)
                index = size if end == -1 else end + quotes
            else:
                index = quote + 1
                verbatim = '@' in prefix_text
                interpolated = '$' in prefix_text
                depth = 0
                while index < size:
                    # Embedded expression strings may contain quotes without
                    # closing the surrounding interpolation. Keep this entire
                    # interpolation visible to the guard, conservatively.
                    if interpolated and depth and source[index] in "\"'":
                        delimiter = source[index]
                        inner_verbatim = index > 0 and source[index - 1] == '@'
                        index += 1
                        while index < size:
                            if source[index] == delimiter:
                                if inner_verbatim and source.startswith(delimiter * 2, index):
                                    index += 2
                                    continue
                                index += 1
                                break
                            index += 2 if source[index] == '\\' and not inner_verbatim else 1
                        continue
                    if interpolated and source[index] == '{':
                        if depth == 0 and source.startswith('{{', index):
                            index += 2
                            continue
                        depth += 1
                    elif interpolated and source[index] == '}' and depth:
                        depth -= 1
                    if source[index] == '"' and depth == 0:
                        if verbatim and source.startswith('""', index):
                            index += 2
                            continue
                        index += 1
                        break
                    if source[index] == '\\' and not verbatim:
                        index += 2
                    else:
                        index += 1
            if '$' not in prefix_text:
                mask(start, min(index, size))
            continue
        if source[index] == "'":
            index += 1
            while index < size:
                if source[index] == '\\':
                    index += 2
                elif source[index] == "'":
                    index += 1
                    break
                else:
                    index += 1
            mask(start, min(index, size))
            continue
        index += 1
    return ''.join(result)


def source_files(root: Path) -> list[Path]:
    source_root = root / "src"
    if not source_root.is_dir():
        raise ValueError(f"missing source directory: {source_root}")
    files = []
    def unreadable(error: OSError) -> None:
        raise error

    source_roots = [source_root]
    if (root / "tests").is_dir():
        source_roots.append(root / "tests")
    for source_root in source_roots:
        for directory, directories, names in os.walk(source_root, followlinks=False, onerror=unreadable):
            directories[:] = sorted(name for name in directories if name not in GENERATED_DIRECTORIES)
            for name in directories:
                path = Path(directory) / name
                if path.is_symlink():
                    raise ValueError(f"source symlink is not supported: {path.relative_to(root).as_posix()}")
            for name in sorted(names):
                if name.lower().endswith(".cs"):
                    path = Path(directory) / name
                    if path.is_symlink():
                        raise ValueError(f"source symlink is not supported: {path.relative_to(root).as_posix()}")
                    files.append(path)
    return sorted(files, key=lambda path: path.relative_to(root).as_posix())


def inspect_file(relative: str, source: str) -> list[tuple[str, int, str, str]]:
    violations = []
    if Path(relative).name.lower() in BANNED_FILE_NAMES:
        violations.append((relative, 1, "retired-player-file", Path(relative).name))
    code = mask_non_code(source)
    for rule in RULES:
        if relative in rule.allowed_files:
            continue
        for match in re.finditer(rule.pattern, code):
            line = code.count('\n', 0, match.start()) + 1
            token = ' '.join(match.group().split())
            violations.append((relative, line, rule.name, token))
    return sorted(violations)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1],
                        help="repository root containing src/ (defaults to this script's repository)")
    args = parser.parse_args(argv)
    root = args.root.resolve()
    try:
        files = source_files(root)
        if not files:
            raise ValueError("no C# sources found under src/")
        violations = []
        for path in files:
            violations.extend(inspect_file(path.relative_to(root).as_posix(), path.read_text(encoding="utf-8-sig")))
    except (OSError, UnicodeError, ValueError) as error:
        print(f"multiplayer-only source guard: {error}", file=sys.stderr)
        return 2
    for relative, line, rule, token in sorted(violations):
        print(f"{relative}:{line}: {rule}: {token}")
    print(f"multiplayer-only source guard: checked {len(files)} C# files; {len(violations)} violation(s)")
    return 1 if violations else 0


if __name__ == "__main__":
    sys.exit(main())
