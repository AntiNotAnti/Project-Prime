#!/usr/bin/env python3
import hashlib
import json
import pathlib
import sys

MANIFEST = ".project-prime-files.json"

if len(sys.argv) != 2:
    raise SystemExit("usage: write-release-manifest.py <publish-directory>")

root = pathlib.Path(sys.argv[1]).resolve()
if not root.is_dir():
    raise SystemExit(f"not a directory: {root}")

files = sorted(
    p.relative_to(root).as_posix()
    for p in root.rglob("*")
    if p.is_file() and p.name != MANIFEST
)

# Match ReleaseInstallation.OwnedPath: a published manifest must never own
# updater-internal files or non-portable paths. Detect this before publishing
# another release that cannot be installed via the updater.
for relative in files:
    parts = relative.split("/")
    if (len(relative) > 1024 or relative.startswith("/")
            or any(part in ("", ".", "..") or part.endswith((" ", "."))
                   or ":" in part or "\\" in part for part in parts)
            or parts[0] in (".project-prime-update.lock",
                            ".project-prime-update-transaction")):
        raise SystemExit(f"unsafe file in release manifest: {relative!r}")
    path = root / relative
    if path.is_symlink():
        raise SystemExit(f"symlink is not allowed in a release manifest: {relative!r}")

hashes = {}
for relative in files:
    digest = hashlib.sha256()
    with (root / relative).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    hashes[relative] = digest.hexdigest()

(root / MANIFEST).write_text(
    json.dumps({"Version": 1, "Files": files, "Hashes": hashes}, separators=(",", ":")),
    encoding="utf-8",
)
print(f"{root}: release manifest contains {len(files)} files")
