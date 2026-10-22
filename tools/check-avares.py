#!/usr/bin/env python3
"""Every avares:// URI the launcher names has to be in the package it runs from.

Avalonia's asset scheme resolves against what a project declared as an
<AvaloniaResource>, and there are two projects declaring the same screens'
assets: the desktop one, where the files live, and the Android head, which
compiles the same sources.  A font the desktop ships and the head does not is
not a warning and not a missing picture -- Avalonia does not fall back for a
font, it throws `Could not create glyphTypeface` out of the measure pass, and
the app dies on its first layout on a device while every build stays green.

That is exactly what happened: the deck theme added three weights of Pixelify
Sans and two of JetBrains Mono, every label on every screen began naming one of
them, and the head's own list was not touched.

So: read the URIs out of the sources, ask MSBuild what the project actually
declares, and compare.  Run once per project.

    tools/check-avares.py src/MphRead/MphRead.csproj -p:RuntimeIdentifier=linux-x64
"""
import json
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
URI = re.compile(r'avares://[A-Za-z0-9_.]+/([^"\')\s]+)')
HUD_FONT_PREFIX = "Assets/HudFonts/"
HUD_FONT_TABLE = pathlib.Path("src/MphRead/Mods/Render/Hud/HudFonts.cs")
HUD_FONT_BLOCK = re.compile(
    r"public\s+static\s+readonly\s+Entry\s*\[\s*\]\s+All\s*=\s*\{(.*?)\};",
    re.DOTALL,
)
HUD_FONT_ENTRY_START = re.compile(
    r'new(?:\s+Entry)?\s*\(\s*"([^"]+)"\s*,',
    re.DOTALL,
)
DYNAMIC_HUD_FONT = re.compile(
    r'avares://ProjectPrime/Assets/HudFonts/"\s*\+\s*font\.Id\s*\+\s*"\.ttf#"\s*\+\s*font\.Family\b'
)


class AssetScanError(Exception):
    """The checker could not safely determine an asset path."""


def _without_csharp_comments(text, table):
    """Remove comments while preserving strings and initializer structure."""
    output = []
    index = 0
    state = "code"
    escaped = False
    while index < len(text):
        char = text[index]
        following = text[index:index + 2]
        if state == "code":
            if following == "//":
                output.extend("  ")
                index += 2
                state = "line-comment"
                continue
            if following == "/*":
                output.extend("  ")
                index += 2
                state = "block-comment"
                continue
            output.append(char)
            if char == '"':
                state = "string"
                escaped = False
            elif char == "'":
                state = "char"
                escaped = False
        elif state in ("string", "char"):
            output.append(char)
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif (state == "string" and char == '"') or (state == "char" and char == "'"):
                state = "code"
        elif state == "line-comment":
            if char == "\n":
                output.append(char)
                state = "code"
            else:
                output.append(" ")
        else:
            if following == "*/":
                output.extend("  ")
                index += 2
                state = "code"
                continue
            output.append("\n" if char == "\n" else " ")
        index += 1
    if state == "block-comment":
        raise AssetScanError(f"unterminated comment in HudFonts.All declaration at {table}")
    return "".join(output)


def _initializer_entries(body, table):
    """Split a C# initializer at commas outside constructor calls and strings."""
    entries = []
    start = 0
    depth = 0
    in_string = False
    in_char = False
    escaped = False
    for index, char in enumerate(body):
        if in_string or in_char:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif (in_string and char == '"') or (in_char and char == "'"):
                in_string = False
                in_char = False
            continue
        if char == '"':
            in_string = True
        elif char == "'":
            in_char = True
        elif char == "(":
            depth += 1
        elif char == ")":
            depth -= 1
            if depth < 0:
                raise AssetScanError(f"unbalanced constructor in HudFonts.All declaration at {table}")
        elif char == "," and depth == 0:
            entries.append(body[start:index].strip())
            start = index + 1
    if in_string or in_char or depth != 0:
        raise AssetScanError(f"unbalanced initializer in HudFonts.All declaration at {table}")
    entries.append(body[start:].strip())
    if entries[-1] == "":
        entries.pop()  # C# permits one trailing comma in an initializer.
    if any(not entry for entry in entries):
        raise AssetScanError(f"empty entry in HudFonts.All declaration at {table}")
    return entries


def _font_entry_id(entry, table):
    """Validate one complete supported constructor and return its literal ID."""
    match = HUD_FONT_ENTRY_START.match(entry)
    if match is None:
        raise AssetScanError(f"HudFonts.All has an unrecognized entry declaration in {table}")

    opening = entry.find("(")
    depth = 0
    in_string = False
    in_char = False
    escaped = False
    closing = None
    for index in range(opening, len(entry)):
        char = entry[index]
        if in_string or in_char:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif (in_string and char == '"') or (in_char and char == "'"):
                in_string = False
                in_char = False
            continue
        if char == '"':
            in_string = True
        elif char == "'":
            in_char = True
        elif char == "(":
            depth += 1
        elif char == ")":
            depth -= 1
            if depth == 0:
                closing = index
                break
            if depth < 0:
                break
    if closing is None or entry[closing + 1:].strip():
        raise AssetScanError(f"HudFonts.All has a malformed entry declaration in {table}")
    return match.group(1)


def hud_font_ids(root=ROOT):
    """Read the runtime HudFonts.All IDs used to build font asset URIs."""
    table = pathlib.Path(root) / HUD_FONT_TABLE
    try:
        text = table.read_text(errors="replace")
    except OSError as exc:
        raise AssetScanError(f"could not read HudFonts.All declaration at {table}: {exc}") from exc
    blocks = HUD_FONT_BLOCK.findall(text)
    if len(blocks) != 1:
        raise AssetScanError(
            f"expected one HudFonts.All entry block in {table}, found {len(blocks)}"
        )

    body = _without_csharp_comments(blocks[0], table)
    entries = _initializer_entries(body, table)
    if not entries:
        raise AssetScanError(f"HudFonts.All has no entries in {table}")
    ids = []
    for entry in entries:
        ids.append(_font_entry_id(entry, table))
    if any(not re.fullmatch(r"[A-Za-z0-9_-]+", font_id) for font_id in ids):
        raise AssetScanError(f"HudFonts.All contains an unsafe font ID in {table}")
    if len(set(ids)) != len(ids):
        raise AssetScanError(f"HudFonts.All contains duplicate font IDs in {table}")
    return ids


def wanted(root=ROOT):
    """Every avares path named in the shared sources, and who names it."""
    root = pathlib.Path(root)
    found = {}
    font_ids = None
    for source in sorted(root.glob("src/**/*.cs")):
        source_path = source.as_posix()
        if "/bin/" in source_path or "/obj/" in source_path:
            continue
        for line, text in enumerate(source.read_text(errors="replace").splitlines(), 1):
            for match in URI.finditer(text):
                # The fragment after "#" is the font family inside the file,
                # not part of the path.
                path = match.group(1).split("#")[0]
                normalized = path.replace("\\", "/")
                if normalized == HUD_FONT_PREFIX:
                    if not DYNAMIC_HUD_FONT.search(text):
                        raise AssetScanError(
                            "unrecognized dynamic HudFonts asset URI at "
                            f"{source.relative_to(root)}:{line}"
                        )
                    if font_ids is None:
                        font_ids = hud_font_ids(root)
                    for font_id in font_ids:
                        asset_path = f"{HUD_FONT_PREFIX}{font_id}.ttf"
                        found.setdefault(asset_path, []).append(
                            f"{source.relative_to(root)}:{line}"
                        )
                    continue
                if normalized.startswith(HUD_FONT_PREFIX) and "{" in normalized:
                    raise AssetScanError(
                        "unrecognized dynamic HudFonts asset URI at "
                        f"{source.relative_to(root)}:{line}"
                    )
                # Built at runtime from a variable -- nothing to check here.
                if "{" in path or not path:
                    continue
                found.setdefault(normalized, []).append(
                    f"{source.relative_to(root)}:{line}")
    return found


def missing_assets(wanted_assets, shipped_paths):
    """Return every named asset that the selected project does not ship."""
    return {
        path: where for path, where in wanted_assets.items()
        if path not in shipped_paths
    }


def declared(project, extra):
    """Every asset path the project ships, as the URI would spell it."""
    result = subprocess.run(
        ["dotnet", "msbuild", project, "-getItem:AvaloniaResource", *extra],
        capture_output=True, text=True, cwd=ROOT)
    if result.returncode != 0:
        print(result.stdout[-4000:], file=sys.stderr)
        print(result.stderr[-4000:], file=sys.stderr)
        sys.exit(f"could not read {project}'s items")
    # MSBuild prints the JSON after whatever the SDK had to say first.
    text = result.stdout[result.stdout.index("{"):]
    items = json.loads(text)["Items"].get("AvaloniaResource", [])
    paths = set()
    for item in items:
        # Link is where the file lands in the package; Identity is that too
        # when the file is already inside the project.
        path = item.get("Link") or item["Identity"]
        paths.add(path.replace("\\", "/"))
    return paths


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    project = sys.argv[1]
    try:
        required = wanted()
    except AssetScanError as exc:
        sys.exit(f"could not determine launcher assets: {exc}")
    ships = declared(project, sys.argv[2:])
    missing = missing_assets(required, ships)
    for path, where in sorted(missing.items()):
        print(f"{project} does not ship {path}")
        for one in where:
            print(f"    named at {one}")
    if missing:
        sys.exit(f"{len(missing)} asset(s) named by the launcher are not in this package")
    print(f"{project}: every avares:// asset the launcher names is shipped")


if __name__ == "__main__":
    main()
