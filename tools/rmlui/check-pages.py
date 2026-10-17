#!/usr/bin/env python3
"""Check restored RmlUi page assets without proprietary-game content."""
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[2] / "src/MphRead/Mods/Launcher/RmlUi"
pages = root / "Pages"
expected = (
    "home/home.rml", "play/browser.rml", "lobby/lobby.rml",
    "settings/settings.rml", "community/browser.rml", "license/license.rml",
    "social/social.rml", "news/news.rml", "offline/setup.rml",
    "adventure/saves.rml", "studio/entry.rml", "theatre/library.rml",
    "hud/editor.rml", "ingame/pause.rml", "start/splash.rml"
)
errors = []
for name in expected:
    if not (pages / name).is_file():
        errors.append(f"Missing authored page: {name}")
files = sorted(root.rglob("*.rml")) + sorted(root.rglob("*.rcss"))
for file in files:
    data = file.read_text(encoding="utf-8")
    for relative in re.findall(r'\bhref\s*=\s*["\']([^"\']+)["\']', data):
        if "://" in relative or relative.startswith("#"):
            continue
        target = (file.parent / relative).resolve()
        if root.resolve() not in target.parents and target != root.resolve():
            errors.append(f"{file.relative_to(root)}: link escapes root: {relative}")
            continue
        # Authored links use the lower-case published paths (pages/,
        # components/, themes/). Source control retains the historical
        # capitalized folders on case-sensitive Linux. Validate each link
        # against the exact path that MSBuild will publish, not the source
        # folder's spelling.
        parts = target.relative_to(root.resolve()).parts
        source_root = {"pages": "Pages", "components": "Components",
                       "themes": "Themes"}
        if parts:
            target = root / source_root.get(parts[0].lower(), parts[0]) / Path(*parts[1:])
        if not target.is_file():
            errors.append(f"{file.relative_to(root)}: missing referenced asset: {relative}")
if len(files) < 70:
    errors.append(f"Only {len(files)} sourced RmlUi files available; expected 70+")
if errors:
    for error in errors:
        print("RML ASSET ERROR: " + error, file=sys.stderr)
    sys.exit(1)
print(f"RML ASSETS PASS: {len(files)} markup/styles; all local links resolve")
