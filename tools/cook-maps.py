#!/usr/bin/env python3
"""Cook first-party map packages once, incrementally and under one owner.

The map source tree is an input, not a publish directory.  This command writes
only to an artifact directory and records a content fingerprint beside the
bundles.  A process-wide advisory lock makes concurrent client/server publish
jobs share one owner; the fingerprint means an unchanged tree is a no-op.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
from typing import Iterable


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SOURCE = ROOT / "maps"
DEFAULT_OUTPUT = ROOT / "artifacts" / "maps" / "current"
MANIFEST_NAME = "cook-manifest.json"
MAP_BUNDLE_SUFFIX = ".fpmap"

# These are deliberately source-side dependencies rather than a broad build
# output glob.  Changing a packer, map schema, package writer, project version,
# or central package graph must invalidate a previously cooked artifact.
DEPENDENCY_GLOBS = (
    "Directory.Build.props",
    "Directory.Build.targets",
    "Directory.Packages.props",
    "global.json",
    "src/Game/Content/Maps/**/*.cs",
    "src/MapPlatform/**/*.cs",
    "src/Shared/ContentPreparation/**/*.cs",
    "src/Tools/Conversion/MapBundleTools.cs",
    "src/Tools/Program.cs",
    "tools/cook-maps.py",
)


def _relative(path: Path) -> str:
    return path.resolve().relative_to(ROOT).as_posix()


def _files_from_glob(pattern: str) -> Iterable[Path]:
    candidate = ROOT / pattern
    if "*" not in pattern:
        if candidate.is_file():
            yield candidate
        return
    yield from (path for path in ROOT.glob(pattern) if path.is_file())


def source_files(source: Path) -> list[Path]:
    if not source.is_dir():
        raise SystemExit(f"map source directory does not exist: {source}")
    files: list[Path] = []
    for path in source.rglob("*"):
        if not path.is_file():
            continue
        relative = path.relative_to(source)
        if any(part in {".git", "__pycache__"} for part in relative.parts):
            continue
        if path.name == ".DS_Store" or path.suffix.lower() == MAP_BUNDLE_SUFFIX:
            continue
        files.append(path)
    return files


def dependency_files() -> list[Path]:
    seen: set[Path] = set()
    result: list[Path] = []
    for pattern in DEPENDENCY_GLOBS:
        for path in _files_from_glob(pattern):
            resolved = path.resolve()
            if resolved not in seen:
                seen.add(resolved)
                result.append(resolved)
    return result


def digest_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()


def input_records(source: Path, compiler_version: str, schema_version: str,
                  package_version: str) -> list[dict[str, str]]:
    paths: dict[str, Path] = {}
    for path in source_files(source):
        paths[f"map:{path.relative_to(source).as_posix()}"] = path
    for path in dependency_files():
        paths[f"dependency:{_relative(path)}"] = path
    records = [
        {"kind": "value", "path": "compiler-version", "sha256": compiler_version},
        {"kind": "value", "path": "schema-version", "sha256": schema_version},
        {"kind": "value", "path": "package-version", "sha256": package_version},
    ]
    records.extend(
        {"kind": kind, "path": path, "sha256": digest_file(file)}
        for path, file in sorted(paths.items())
        for kind in (path.split(":", 1)[0],)
    )
    return records


def fingerprint(records: list[dict[str, str]]) -> str:
    payload = json.dumps(records, ensure_ascii=False, separators=(",", ":"),
                         sort_keys=True).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


class OutputLock:
    """Cross-process lock with a portable Unix/Windows implementation."""

    def __init__(self, path: Path) -> None:
        self.path = path
        self.handle = None

    def __enter__(self) -> "OutputLock":
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.handle = self.path.open("a+b")
        if os.name == "nt":
            import msvcrt
            self.handle.seek(0)
            msvcrt.locking(self.handle.fileno(), msvcrt.LK_LOCK, 1)
        else:
            import fcntl
            fcntl.flock(self.handle.fileno(), fcntl.LOCK_EX)
        return self

    def __exit__(self, *_: object) -> None:
        if self.handle is None:
            return
        if os.name == "nt":
            import msvcrt
            self.handle.seek(0)
            msvcrt.locking(self.handle.fileno(), msvcrt.LK_UNLCK, 1)
        else:
            import fcntl
            fcntl.flock(self.handle.fileno(), fcntl.LOCK_UN)
        self.handle.close()


def manifest_matches(path: Path, expected: dict[str, object]) -> bool:
    try:
        actual = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return False
    artifacts = actual.get("artifacts")
    return (
        all(actual.get(key) == value for key, value in expected.items())
        and isinstance(artifacts, list)
        and bool(artifacts)
        and all(
            isinstance(name, str)
            and name.endswith(MAP_BUNDLE_SUFFIX)
            and (path.parent / name).is_file()
            for name in artifacts
        )
    )


def _reserved_path(parent: Path) -> Path:
    """Reserve a sibling path without exposing a replacement target to races."""
    with tempfile.NamedTemporaryFile(prefix=".project-prime-map-old-",
                                     dir=parent, delete=False) as handle:
        reserved = Path(handle.name)
    reserved.unlink()
    return reserved


def _replace_artifact_set(output: Path, staging: Path, names: list[str],
                          manifest: dict[str, object] | None) -> None:
    """Install a complete cooked set while retaining the last good set on error.

    The compiler writes to ``staging``.  Only after it has completed and all
    expected files exist do we swap the directory into place.  A sibling
    backup makes the directory replacement recoverable if the second rename
    fails, and the lock held by ``cook`` prevents concurrent writers.
    """
    replacement: Path | None = Path(
        tempfile.mkdtemp(prefix=".project-prime-map-next-", dir=output.parent)
    )
    backup: Path | None = None
    try:
        for name in names:
            source = staging / name
            if not source.is_file():
                raise OSError(f"map cook staging output is missing: {source}")
            os.replace(source, replacement / name)

        if manifest is not None:
            manifest_path = replacement / MANIFEST_NAME
            manifest_path.write_text(
                json.dumps({**manifest, "artifacts": names}, indent=2,
                           sort_keys=True) + "\n",
                encoding="utf-8",
            )

        if output.exists():
            backup = _reserved_path(output.parent)
            os.replace(output, backup)
        try:
            os.replace(replacement, output)
        except BaseException:
            if backup is not None and backup.exists() and not output.exists():
                os.replace(backup, output)
                backup = None
            raise
        replacement = None
    finally:
        if replacement is not None and replacement.exists():
            shutil.rmtree(replacement)
        if backup is not None and backup.exists():
            # The new set is already live. Retain the old set only long enough
            # to prove the replacement succeeded; cleanup failure must not turn
            # a successful cook into a failed build.
            try:
                shutil.rmtree(backup)
            except OSError as error:
                print(f"map cook: unable to remove old artifact backup {backup}: {error}",
                      file=sys.stderr)


def run_cook(source: Path, output: Path, configuration: str,
             manifest: dict[str, object] | None = None) -> list[str]:
    with tempfile.TemporaryDirectory(prefix="project-prime-map-cook-",
                                      dir=output.parent) as temporary:
        temporary_root = Path(temporary)
        staging = temporary_root / "output"
        # CustomRooms intentionally gives an installed .fpmap precedence over
        # a loose recipe with the same name. A developer checkout can retain
        # ignored bundles from an earlier build, so cook against a private
        # source snapshot with generated bundles removed. This keeps the
        # source tree untouched while ensuring the explicit cook is always
        # based on map inputs rather than a stale output.
        isolated_source = temporary_root / "source"
        shutil.copytree(
            source,
            isolated_source,
            ignore=shutil.ignore_patterns(f"*{MAP_BUNDLE_SUFFIX}"),
            symlinks=True,
        )
        staging.mkdir()
        command = [
            "dotnet", "run", "--project", str(ROOT / "src/Tools/Tools.csproj"),
            "-c", configuration, "--",
            "-mapdir", str(isolated_source), "-mapbundle", "all",
            "-mapbundle-output", str(staging),
        ]
        subprocess.run(command, cwd=ROOT, check=True)
        bundles = sorted(staging.glob(f"*{MAP_BUNDLE_SUFFIX}"))
        if not bundles:
            raise SystemExit(f"map cook produced no {MAP_BUNDLE_SUFFIX} artifacts")
        names = [path.name for path in bundles]
        _replace_artifact_set(output, staging, names, manifest)
        return names


def cook(args: argparse.Namespace) -> int:
    source = Path(args.source).expanduser().resolve()
    output = Path(args.output).expanduser().resolve()
    if output == source or output in source.parents or source in output.parents:
        raise SystemExit(
            "map artifact output must not overlap the map source tree; "
            "choose an isolated artifact directory"
        )
    output.mkdir(parents=True, exist_ok=True)
    records = input_records(source, args.compiler_version, args.schema_version,
                             args.package_version)
    expected = {
        "format": 1,
        "fingerprint": fingerprint(records),
        "source": str(source),
        "compilerVersion": args.compiler_version,
        "schemaVersion": args.schema_version,
        "packageVersion": args.package_version,
        "inputs": records,
    }
    manifest = output / MANIFEST_NAME
    with OutputLock(output.parent / ".cook.lock"):
        if not args.force and manifest_matches(manifest, expected):
            print(f"map cook up to date: {expected['fingerprint'][:12]}")
            return 0
        names = run_cook(source, output, args.configuration, expected)
        print(f"cooked {len(names)} map artifact(s): {expected['fingerprint'][:12]}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--compiler-version", default="development")
    parser.add_argument("--schema-version", default="1")
    parser.add_argument("--package-version", default="local")
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()
    try:
        return cook(args)
    except subprocess.CalledProcessError as error:
        return error.returncode or 1
    except (OSError, ValueError) as error:
        print(f"map cook: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
