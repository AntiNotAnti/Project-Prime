#!/usr/bin/env python3
"""Run a paired real-map load campaign against two built game assemblies.

The runner records only loads whose game-written receipt proves that the map
reached its first successful presentation with all requested players alive.
Native asset roots are copied into a private temporary tree, and each
invocation uses a private PROJECT_PRIME_USER_DATA profile.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tempfile
import time
import uuid
from typing import Any, Callable, Sequence


SCHEMA = 1
PLAYERS = 8
SECONDS = 1
CACHE_KINDS = ("application-cold", "persistent-cache-warm")
ARMS = ("baseline", "candidate")
MAX_SUBPROCESS_RUNS = 1000
MAX_ASSET_ROOTS = 32
MAX_ASSET_FILES = 200_000
MAX_ASSET_BYTES = 4 * 1024 * 1024 * 1024
MAX_CAMPAIGN_ASSET_FILES = 400_000
MAX_CAMPAIGN_ASSET_BYTES = 8 * 1024 * 1024 * 1024
MAX_MAP_FILES = 20_000
MAX_MAP_PACKAGE_BYTES = 512 * 1024 * 1024
MAX_MAP_BYTES = 8 * 1024 * 1024 * 1024
MAX_MAP_CATALOG_ENTRIES = 50_000
ASSET_PATH_KEYS = frozenset({
    "AMFE0", "AMFP0", "A76E0", "AMHE0", "AMHE1",
    "AMHJ0", "AMHJ1", "AMHP0", "AMHP1", "AMHK0",
})
REQUIRED_STAGES = ("firstSuccessfulPresentWithAllPlayersLive",)


class CampaignError(RuntimeError):
    """A campaign could not produce trustworthy samples."""


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary.replace(path)


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        while block := source.read(1024 * 1024):
            digest.update(block)
    return digest.hexdigest()


def inventory_package_catalog(root: Path, *, require_packages_only: bool = False) -> tuple[list[dict[str, Any]], str, int]:
    """Hash only regular .ppmap files in a bounded, link-free source catalog."""
    if _is_link(root):
        raise CampaignError(f"maps directory cannot be a symbolic link or junction: {root}")
    if not root.is_dir():
        raise CampaignError(f"maps directory is not a directory: {root}")
    if root == Path(root.anchor) or root.parent == root:
        raise CampaignError(f"maps directory cannot be a disk root: {root}")
    entries: list[dict[str, Any]] = []
    visited_entries = 0
    catalog_bytes = 0
    package_bytes = 0
    for current, directories, files in os.walk(root, followlinks=False):
        current_path = Path(current)
        for name in list(directories):
            item = current_path / name
            visited_entries += 1
            if visited_entries > MAX_MAP_CATALOG_ENTRIES:
                raise CampaignError(
                    f"maps catalog exceeds the {MAX_MAP_CATALOG_ENTRIES}-entry scan limit: {root}"
                )
            if _is_link(item):
                raise CampaignError(f"maps input contains a symbolic link or junction: {item}")
        for name in files:
            item = current_path / name
            visited_entries += 1
            if visited_entries > MAX_MAP_CATALOG_ENTRIES:
                raise CampaignError(
                    f"maps catalog exceeds the {MAX_MAP_CATALOG_ENTRIES}-entry scan limit: {root}"
                )
            if _is_link(item) or not item.is_file():
                raise CampaignError(f"maps input contains a symbolic link, junction, or non-regular file: {item}")
            size = item.stat().st_size
            catalog_bytes += size
            if catalog_bytes > MAX_MAP_BYTES:
                raise CampaignError(f"maps catalog exceeds the {MAX_MAP_BYTES}-byte scan limit: {root}")
            if item.suffix.casefold() != ".ppmap":
                if require_packages_only:
                    raise CampaignError(f"private package catalog contains a non-.ppmap file: {item}")
                continue
            if size > MAX_MAP_PACKAGE_BYTES:
                raise CampaignError(
                    f".ppmap package exceeds the {MAX_MAP_PACKAGE_BYTES}-byte per-package limit: {item}"
                )
            package_bytes += size
            if len(entries) >= MAX_MAP_FILES:
                raise CampaignError(f"maps catalog exceeds the {MAX_MAP_FILES}-package limit: {root}")
            if package_bytes > MAX_MAP_BYTES:
                raise CampaignError(f".ppmap packages exceed the {MAX_MAP_BYTES}-byte limit: {root}")
            entries.append({
                "path": item.relative_to(root).as_posix(),
                "sizeBytes": size,
                "sha256": sha256_file(item),
            })
    if not entries:
        raise CampaignError(f"maps catalog contains no regular .ppmap packages: {root}")
    entries.sort(key=lambda entry: (entry["path"].casefold(), entry["path"]))
    payload = json.dumps(entries, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return entries, hashlib.sha256(payload).hexdigest(), package_bytes


def copy_package_catalog(
    source_root: Path,
    private_directory: Path,
    *,
    expected_manifest: Sequence[dict[str, Any]] | None = None,
    expected_hash: str | None = None,
    expected_bytes: int | None = None,
) -> dict[str, Any]:
    """Copy only standalone packages into a private catalog, without hard links."""
    if _is_link(source_root) or _is_link(private_directory):
        raise CampaignError("package catalog source and private destination cannot be symbolic links or junctions")
    source_root = source_root.resolve(strict=True)
    private_directory = private_directory.resolve(strict=False)
    if _is_link(source_root) or not source_root.is_dir():
        raise CampaignError(f"maps directory must be a regular directory: {source_root}")
    if _inside(private_directory, source_root) or _inside(source_root, private_directory):
        raise CampaignError(f"private package catalog overlaps its source: {source_root}")
    if expected_manifest is None or expected_hash is None or expected_bytes is None:
        manifest, source_hash, source_bytes = inventory_package_catalog(source_root)
    else:
        manifest, source_hash, source_bytes = list(expected_manifest), expected_hash, expected_bytes
    if not manifest or len(manifest) > MAX_MAP_FILES or source_bytes < 0 or source_bytes > MAX_MAP_BYTES:
        raise CampaignError("private package catalog snapshot exceeds its package count or byte budget")
    if private_directory.exists():
        raise CampaignError(f"private package catalog already exists: {private_directory}")
    copied = False
    try:
        private_directory.mkdir(parents=True, exist_ok=False)
        copied = True
        copied_manifest: list[dict[str, Any]] = []
        copied_bytes = 0
        for entry in manifest:
            relative = entry["path"].split("/")
            if (not relative or any(part in ("", ".", "..") or ":" in part or "\\" in part
                                    for part in relative)):
                raise CampaignError(f"maps catalog contains an unsafe package path: {entry['path']!r}")
            if Path(relative[-1]).suffix.casefold() != ".ppmap":
                raise CampaignError(f"private package catalog accepts only .ppmap files: {entry['path']!r}")
            if entry["sizeBytes"] < 0 or entry["sizeBytes"] > MAX_MAP_PACKAGE_BYTES:
                raise CampaignError(f"package exceeds the per-package byte limit: {entry['path']!r}")
            source = source_root.joinpath(*relative)
            target = private_directory.joinpath(*relative)
            if _is_link(source) or not source.is_file():
                raise CampaignError(f"package source changed during snapshot: {source}")
            resolved_source = source.resolve(strict=True)
            resolved_target = target.resolve(strict=False)
            if not _inside(resolved_source, source_root) or not _inside(resolved_target, private_directory):
                raise CampaignError(f"package path escapes its catalog: {entry['path']!r}")
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
            copied_size = target.stat().st_size
            copied_hash = sha256_file(target)
            if copied_size != entry["sizeBytes"] or copied_hash != entry["sha256"]:
                raise CampaignError(f"private package copy verification failed: {entry['path']}")
            copied_manifest.append({"path": entry["path"], "sizeBytes": copied_size, "sha256": copied_hash})
            copied_bytes += copied_size
        copied_manifest.sort(key=lambda entry: (entry["path"].casefold(), entry["path"]))
        payload = json.dumps(copied_manifest, sort_keys=True, separators=(",", ":")).encode("utf-8")
        copied_hash = hashlib.sha256(payload).hexdigest()
        if copied_hash != source_hash or copied_bytes != source_bytes or copied_manifest != manifest:
            raise CampaignError("private package catalog verification failed")
        return {
            "sourcePackageCatalogSha256": source_hash,
            "privateCopyPackageCatalogSha256": copied_hash,
            "privateCopyDirectory": str(private_directory),
            "files": len(manifest),
            "sizeBytes": copied_bytes,
            "removedAfterRun": False,
        }
    except BaseException:
        if copied:
            shutil.rmtree(private_directory, ignore_errors=True)
        raise


def verify_private_package_catalog(package_catalog: dict[str, Any]) -> str:
    directory = Path(package_catalog["privateCopyDirectory"])
    _, actual_hash, _ = inventory_package_catalog(directory, require_packages_only=True)
    if actual_hash != package_catalog["privateCopyPackageCatalogSha256"]:
        raise CampaignError("private .ppmap package catalog changed while the process was running")
    return actual_hash


def remove_private_package_catalog(package_catalog: dict[str, Any]) -> None:
    directory = Path(package_catalog["privateCopyDirectory"])
    exception_in_flight = sys.exc_info()[0] is not None
    try:
        shutil.rmtree(directory)
    except FileNotFoundError:
        pass
    except OSError as exc:
        package_catalog["cleanupError"] = str(exc)
        package_catalog["removedAfterRun"] = False
        if not exception_in_flight:
            raise CampaignError(f"cannot remove private cold package catalog {directory}: {exc}") from exc
    package_catalog["removedAfterRun"] = not directory.exists()
    if not package_catalog["removedAfterRun"]:
        package_catalog.setdefault("cleanupError", "private catalog remained after cleanup")
        if not exception_in_flight:
            raise CampaignError(f"private cold package catalog remains after cleanup: {directory}")


def _is_link(path: Path) -> bool:
    return path.is_symlink() or bool(getattr(path, "is_junction", lambda: False)())


def inventory_asset_tree(root: Path) -> tuple[list[dict[str, Any]], str, int]:
    """Bound and hash a native asset root without following links or special files."""
    if _is_link(root):
        raise CampaignError(f"native asset root cannot be a symbolic link or junction: {root}")
    if not root.is_dir():
        raise CampaignError(f"native asset root is not a directory: {root}")
    if root == Path(root.anchor) or root.parent == root:
        raise CampaignError(f"native asset root cannot be a disk root: {root}")
    entries: list[dict[str, Any]] = []
    total_bytes = 0
    for current, directories, files in os.walk(root, followlinks=False):
        current_path = Path(current)
        for name in list(directories):
            item = current_path / name
            if _is_link(item):
                raise CampaignError(f"native asset tree contains a link: {item}")
        for name in files:
            item = current_path / name
            if _is_link(item) or not item.is_file():
                raise CampaignError(f"native asset tree contains a non-regular file: {item}")
            size = item.stat().st_size
            total_bytes += size
            if len(entries) >= MAX_ASSET_FILES:
                raise CampaignError(f"native asset tree exceeds the {MAX_ASSET_FILES} file limit: {root}")
            if total_bytes > MAX_ASSET_BYTES:
                raise CampaignError(f"native asset tree exceeds the {MAX_ASSET_BYTES}-byte limit: {root}")
            entries.append({
                "path": item.relative_to(root).as_posix(),
                "sizeBytes": size,
                "sha256": sha256_file(item),
            })
    entries.sort(key=lambda entry: (entry["path"].casefold(), entry["path"]))
    payload = json.dumps(entries, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return entries, hashlib.sha256(payload).hexdigest(), total_bytes


def configured_asset_roots(paths_source: Path) -> list[dict[str, Any]]:
    """Resolve and inventory every configured native root used by Paths.AllPaths."""
    try:
        lines = paths_source.read_bytes().decode("utf-8-sig").splitlines()
    except (OSError, UnicodeError) as exc:
        raise CampaignError(f"cannot read --game-paths file: {exc}") from exc
    roots: list[dict[str, Any]] = []
    source_keys: dict[str, tuple[Path, str, int]] = {}
    total_bytes = 0
    total_files = 0
    for line_index, line in enumerate(lines):
        if line.count("=") != 1:
            continue
        raw_key, raw_value = line.split("=", 1)
        key, value = raw_key.strip(), raw_value.strip()
        if key not in ASSET_PATH_KEYS or not value:
            continue
        if len(roots) >= MAX_ASSET_ROOTS:
            raise CampaignError(f"paths.txt exceeds the {MAX_ASSET_ROOTS} configured native root limit")
        source = Path(value)
        if not source.is_absolute():
            source = paths_source.parent / source
        if _is_link(source):
            raise CampaignError(f"configured native asset root cannot be a symbolic link or junction: {source}")
        try:
            source = source.resolve(strict=True)
        except OSError as exc:
            raise CampaignError(f"cannot resolve configured native asset root {value!r}: {exc}") from exc
        identity = os.path.normcase(str(source))
        if identity not in source_keys:
            entries, tree_hash, size = inventory_asset_tree(source)
            source_keys[identity] = (source, tree_hash, size)
            total_bytes += size
            total_files += len(entries)
            if total_bytes > MAX_CAMPAIGN_ASSET_BYTES:
                raise CampaignError(
                    f"configured native asset roots exceed the {MAX_CAMPAIGN_ASSET_BYTES}-byte campaign limit"
                )
            if total_files > MAX_CAMPAIGN_ASSET_FILES:
                raise CampaignError(
                    f"configured native asset roots exceed the {MAX_CAMPAIGN_ASSET_FILES}-file campaign limit"
                )
        else:
            entries, tree_hash, size = inventory_asset_tree(source)
            _, previous_hash, previous_size = source_keys[identity]
            if tree_hash != previous_hash or size != previous_size:
                raise CampaignError(f"native asset root changed while the campaign was being prepared: {source}")
        roots.append({
            "lineIndex": line_index,
            "key": key,
            "source": source,
            "fileCount": len(entries),
            "treeSha256": tree_hash,
            "sizeBytes": size,
        })
    if not roots:
        raise CampaignError("paths.txt has no non-empty configured native asset roots")
    return roots


def copy_asset_roots(roots: Sequence[dict[str, Any]], private_root: Path) -> tuple[dict[int, Path], list[dict[str, Any]]]:
    """Copy configured native roots to a private tree and verify each copy."""
    private_root = private_root.resolve()
    for root in roots:
        source: Path = root["source"]
        if _inside(private_root, source) or _inside(source, private_root):
            raise CampaignError(f"private native asset tree overlaps its source: {source}")
    assets_root = private_root / "assets"
    assets_root.mkdir(parents=True, exist_ok=False)
    by_source: dict[str, tuple[Path, dict[str, Any]]] = {}
    rewrites: dict[int, Path] = {}
    copies: list[dict[str, Any]] = []
    for root in roots:
        source: Path = root["source"]
        identity = os.path.normcase(str(source))
        if identity not in by_source:
            target = assets_root / f"{_slug(root['key'])}-{root['lineIndex']:02d}"
            if _inside(target.resolve(strict=False), source) or _inside(source, target.resolve(strict=False)):
                raise CampaignError(f"private native asset copy overlaps its source: {source}")
            try:
                shutil.copytree(source, target, copy_function=shutil.copy2, symlinks=False)
            except (OSError, shutil.Error) as exc:
                raise CampaignError(f"cannot privately copy native asset root {root['key']}: {exc}") from exc
            _, copied_hash, copied_bytes = inventory_asset_tree(target)
            if copied_hash != root["treeSha256"] or copied_bytes != root["sizeBytes"]:
                raise CampaignError(f"private copy verification failed for native asset root {root['key']}")
            copy_info = {
                "keys": [root["key"]],
                "sourceTreeSha256": root["treeSha256"],
                "privateCopyTreeSha256BeforeRuns": copied_hash,
                "privateCopyDirectory": str(target),
                "files": root["fileCount"],
                "sizeBytes": copied_bytes,
            }
            by_source[identity] = (target, copy_info)
            copies.append(copy_info)
        else:
            target, copy_info = by_source[identity]
            if root["key"] not in copy_info["keys"]:
                copy_info["keys"].append(root["key"])
        rewrites[root["lineIndex"]] = by_source[identity][0]
    return rewrites, copies


def nearest_rank(values: Sequence[float], percentile: int) -> float:
    if not values:
        raise ValueError("percentile requires at least one value")
    if percentile < 1 or percentile > 100:
        raise ValueError("percentile must be between 1 and 100")
    ordered = sorted(float(value) for value in values)
    rank = math.ceil(percentile * len(ordered) / 100)
    return ordered[rank - 1]


def paired_order(trial_index: int) -> tuple[str, str]:
    """Return deterministic alternating baseline/candidate execution order."""
    if trial_index < 0:
        raise ValueError("trial index cannot be negative")
    return (ARMS if trial_index % 2 == 0 else tuple(reversed(ARMS)))


def _finite_number(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(float(value))


def validate_receipt(path: Path, room: str) -> dict[str, Any]:
    """Read and validate the runtime receipt; never infer readiness from logs."""
    try:
        receipt = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise CampaignError("map-load receipt was not written") from exc
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise CampaignError(f"map-load receipt is malformed or unreadable: {exc}") from exc
    if not isinstance(receipt, dict):
        raise CampaignError("map-load receipt root must be a JSON object")
    if receipt.get("schema") != 1 or receipt.get("kind") != "map-load-receipt":
        raise CampaignError("map-load receipt schema or kind is unsupported")
    if receipt.get("room") != room:
        raise CampaignError(f"receipt room does not match requested room {room!r}")
    loaded_room = receipt.get("loadedRoom")
    if not isinstance(loaded_room, str) or loaded_room.casefold() != room.casefold():
        raise CampaignError(f"receipt loadedRoom does not match requested room {room!r}")
    if receipt.get("loadedRoomCount") != 1:
        raise CampaignError("receipt does not confirm exactly one loaded room")
    mode = receipt.get("mode")
    if not isinstance(mode, str) or mode.casefold() != "battle":
        raise CampaignError("receipt mode is not Battle")
    if receipt.get("requestedPlayers") != PLAYERS:
        raise CampaignError(f"receipt requestedPlayers is not {PLAYERS}")
    if receipt.get("scenePlayerCount") != PLAYERS:
        raise CampaignError(f"receipt scenePlayerCount is not {PLAYERS}")
    if receipt.get("ready") is not True:
        reason = receipt.get("notReadyReason")
        suffix = f": {reason}" if isinstance(reason, str) and reason else ""
        raise CampaignError("receipt did not reach the required ready state" + suffix)
    if receipt.get("outcome") != "passed":
        raise CampaignError(f"receipt outcome is {receipt.get('outcome')!r}, expected 'passed'")
    if receipt.get("exitCode") != 0:
        raise CampaignError(f"receipt exitCode is {receipt.get('exitCode')!r}, expected 0")
    load_ms = receipt.get("readyElapsedMilliseconds")
    if not _finite_number(load_ms) or float(load_ms) <= 0:
        raise CampaignError("receipt readyElapsedMilliseconds must be a finite positive number")
    stages = receipt.get("stages")
    if not isinstance(stages, dict):
        raise CampaignError("receipt stages must be a JSON object")
    for stage_name in REQUIRED_STAGES:
        stage = stages.get(stage_name)
        if not isinstance(stage, dict) or not _finite_number(stage.get("elapsedMilliseconds")):
            raise CampaignError(f"receipt is missing valid stage {stage_name}")
        if float(stage["elapsedMilliseconds"]) <= 0:
            raise CampaignError(f"receipt stage {stage_name} must have positive elapsedMilliseconds")
        if not math.isclose(float(stage["elapsedMilliseconds"]), float(load_ms), rel_tol=1e-9, abs_tol=1e-6):
            raise CampaignError("receipt ready timing does not match the first successful present stage")
    return receipt


def rewrite_paths(source: Path, export_root: Path,
                  asset_rewrites: dict[int, Path]) -> tuple[str, str]:
    """Redirect native roots to private copies and exports to the private profile.

    Every non-empty runtime asset root must have a preflighted private copy;
    falling back to an operator path would let the game write generated files
    into the extracted source tree.
    """
    try:
        original = source.read_bytes().decode("utf-8-sig")
    except (OSError, UnicodeError) as exc:
        raise CampaignError(f"cannot read --game-paths file: {exc}") from exc
    lines = original.splitlines()
    rewritten: list[str] = []
    export_seen = False
    for line_index, line in enumerate(lines):
        if line.count("=") != 1:
            rewritten.append(line)
            continue
        key, separator, value = line.partition("=")
        if not separator:
            rewritten.append(line)
            continue
        if key.strip().casefold() == "export":
            rewritten.append(f"{key.strip()}={export_root}")
            export_seen = True
            continue
        root = value.strip()
        if not root:
            rewritten.append(line)
            continue
        if key.strip() in ASSET_PATH_KEYS:
            private_path = asset_rewrites.get(line_index)
            if private_path is None:
                raise CampaignError(f"configured native root on line {line_index + 1} has no private copy")
            rewritten.append(f"{key.strip()}={private_path}")
        else:
            rewritten.append(line)
    if not export_seen:
        rewritten.append(f"Export={export_root}")
    effective = "\n".join(rewritten) + "\n"
    return effective, hashlib.sha256(effective.encode("utf-8")).hexdigest()


def make_profile(profile_root: Path, paths_source: Path,
                 asset_rewrites: dict[int, Path]) -> str:
    profile_root.mkdir(parents=True, exist_ok=False)
    export_root = profile_root / "Export"
    export_root.mkdir()
    (profile_root / "Q3ImportCache").mkdir()
    effective_paths, digest = rewrite_paths(paths_source, export_root, asset_rewrites)
    (profile_root / "paths.txt").write_text(effective_paths, encoding="utf-8", newline="\n")
    return digest


def profile_environment(dotnet_env: dict[str, str], profile: Path) -> dict[str, str]:
    q3_cache = profile / "Q3ImportCache"
    if not profile.is_dir() or not q3_cache.is_dir():
        raise CampaignError(f"private profile or Q3 cache directory is missing: {profile}")
    return dict(
        dotnet_env,
        PROJECT_PRIME_USER_DATA=str(profile),
        PROJECT_PRIME_Q3_CACHE=str(q3_cache),
    )


def package_catalog_path_for(private_root: Path, cache_kind: str, room_slug: str,
                            arm: str, trial_index: int | None = None) -> Path:
    if cache_kind == "persistent-cache-warm":
        return private_root / "warm-maps" / room_slug / arm
    if cache_kind != "application-cold" or trial_index is None or trial_index < 1:
        raise CampaignError("application-cold package catalog requires a positive trial index")
    return private_root / "cold-maps" / room_slug / f"trial-{trial_index:03d}-{arm}"


def build_command(dotnet: str, dll: Path, room: str, maps_dir: Path,
                  report: Path, runtime_namespace: str) -> list[str]:
    try:
        if len(runtime_namespace) != 32 or uuid.UUID(runtime_namespace).hex != runtime_namespace:
            raise ValueError
    except (ValueError, AttributeError):
        raise CampaignError("custom runtime namespace must be a 32-character lowercase GUID") from None
    return [
        dotnet, str(dll),
        "-maptest", room,
        "-players", str(PLAYERS),
        "-seconds", str(SECONDS),
        "-bots",
        "-mapdir", str(maps_dir),
        "-maploadreport", str(report),
        "-customruntimenamespace", runtime_namespace,
        "-noupdate",
    ]


def runtime_namespace_for(campaign_id: uuid.UUID, cache_kind: str,
                          room: str, arm: str, trial_index: int | None = None) -> str:
    if cache_kind == "persistent-cache-warm":
        identity = f"warm/{room.casefold()}/{arm}"
    else:
        if trial_index is None:
            raise CampaignError("application-cold runtime namespace requires a trial index")
        identity = f"cold/{room.casefold()}/{arm}/{trial_index}"
    return uuid.uuid5(campaign_id, identity).hex


def _stop_owned_process(process: Any) -> tuple[int | None, bool]:
    """Terminate only the Popen instance created by this runner."""
    terminated = False
    if process.poll() is None:
        try:
            process.terminate()
            terminated = True
        except OSError:
            pass
    try:
        return process.wait(timeout=5), terminated
    except subprocess.TimeoutExpired:
        try:
            process.kill()
            terminated = True
        except OSError:
            pass
        # The private map catalog must stay in place until our child exits.
        # After a hard kill, wait without a timeout so cleanup cannot race a
        # still-running process that has not released its package files.
        return process.wait(), terminated


def run_process(
    command: Sequence[str],
    cwd: Path,
    env: dict[str, str],
    timeout_seconds: float,
    stdout_path: Path,
    stderr_path: Path,
    popen_factory: Callable[..., Any] | None = None,
) -> dict[str, Any]:
    """Run a bounded child with retained, separate stdout/stderr streams."""
    factory = popen_factory or subprocess.Popen
    started_utc = utc_now()
    started = time.perf_counter()
    result: dict[str, Any] = {
        "command": list(command),
        "cwd": str(cwd),
        "projectPrimeUserData": env.get("PROJECT_PRIME_USER_DATA"),
        "environmentOverrides": {
            "PROJECT_PRIME_USER_DATA": env.get("PROJECT_PRIME_USER_DATA"),
            "PROJECT_PRIME_Q3_CACHE": env.get("PROJECT_PRIME_Q3_CACHE"),
            "DOTNET_CLI_TELEMETRY_OPTOUT": env.get("DOTNET_CLI_TELEMETRY_OPTOUT"),
            "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": env.get("DOTNET_SKIP_FIRST_TIME_EXPERIENCE"),
        },
        "startedUtc": started_utc,
        "stdout": stdout_path.name,
        "stderr": stderr_path.name,
        "timeoutSeconds": timeout_seconds,
        "timedOut": False,
        "terminatedByRunner": False,
        "returnCode": None,
        "hostWallClockSeconds": None,
        "startError": None,
    }
    process = None
    try:
        with stdout_path.open("wb") as stdout, stderr_path.open("wb") as stderr:
            try:
                process = factory(
                    list(command),
                    cwd=str(cwd),
                    env=env,
                    stdout=stdout,
                    stderr=stderr,
                    shell=False,
                )
            except OSError as exc:
                result["startError"] = str(exc)
            else:
                try:
                    result["returnCode"] = process.wait(timeout=timeout_seconds)
                except subprocess.TimeoutExpired:
                    result["timedOut"] = True
                    result["returnCode"], result["terminatedByRunner"] = _stop_owned_process(process)
                except BaseException as exc:
                    # KeyboardInterrupt/SystemExit must never leave the game
                    # process running against the temporary private asset copy.
                    result["interrupted"] = True
                    result["interruptionType"] = type(exc).__name__
                    result["returnCode"], result["terminatedByRunner"] = _stop_owned_process(process)
                    try:
                        setattr(exc, "map_load_process_result", result)
                    except Exception:
                        pass
                    raise
    finally:
        result["finishedUtc"] = utc_now()
        result["hostWallClockSeconds"] = round(time.perf_counter() - started, 6)
    return result


def _slug(text: str) -> str:
    cleaned = "".join(char.lower() if char.isalnum() else "-" for char in text)
    cleaned = "-".join(part for part in cleaned.split("-") if part)
    return cleaned[:48] or "room"


def _inside(child: Path, parent: Path) -> bool:
    try:
        child.relative_to(parent)
        return True
    except ValueError:
        return False


def _resolve_dotnet(value: str | None) -> str:
    if value is not None:
        candidate = Path(value)
        if not candidate.is_absolute():
            raise CampaignError("--dotnet must be an absolute SDK executable path when supplied")
        candidate = candidate.resolve()
        if not candidate.is_file():
            raise CampaignError(f"--dotnet executable does not exist: {candidate}")
        return str(candidate)
    found = shutil.which("dotnet")
    if not found:
        raise CampaignError("dotnet was not found on PATH; pass --dotnet with an absolute SDK executable path")
    return str(Path(found).resolve())


def _validate_args(args: argparse.Namespace) -> dict[str, Any]:
    if _is_link(args.maps_dir):
        raise CampaignError("--maps-dir cannot be a symbolic link or junction")
    if _is_link(args.game_paths):
        raise CampaignError("--game-paths cannot be a symbolic link or junction")
    if _is_link(args.output):
        raise CampaignError("--output cannot be a symbolic link or junction")
    baseline = args.baseline.resolve()
    candidate = args.candidate.resolve()
    paths_file = args.game_paths.resolve()
    maps_dir = args.maps_dir.resolve()
    output = args.output.resolve()
    if output == Path(output.anchor) or output.parent == output:
        raise CampaignError("--output cannot be a disk root")
    if not baseline.is_file() or baseline.suffix.casefold() != ".dll":
        raise CampaignError(f"--baseline must name an existing .dll: {baseline}")
    if not candidate.is_file() or candidate.suffix.casefold() != ".dll":
        raise CampaignError(f"--candidate must name an existing .dll: {candidate}")
    if not paths_file.is_file():
        raise CampaignError(f"--game-paths must name an existing paths.txt file: {paths_file}")
    if paths_file.name.casefold() != "paths.txt":
        raise CampaignError("--game-paths must name a file called paths.txt")
    if not maps_dir.is_dir():
        raise CampaignError(f"--maps-dir must be an existing directory: {maps_dir}")
    if maps_dir == Path(maps_dir.anchor) or maps_dir.parent == maps_dir:
        raise CampaignError("--maps-dir cannot be a disk root")
    map_packages, map_packages_hash, map_package_bytes = inventory_package_catalog(maps_dir)
    asset_roots = configured_asset_roots(paths_file)
    unique_asset_sources = sorted(
        {root["source"] for root in asset_roots}, key=lambda path: os.path.normcase(str(path))
    )
    if not args.rooms:
        raise CampaignError("provide --rooms at least once")
    rooms: list[str] = []
    seen_rooms: set[str] = set()
    for room in args.rooms:
        if not room.strip() or room != room.strip() or room.startswith("-") or "\x00" in room or "\n" in room or "\r" in room:
            raise CampaignError(f"invalid room name: {room!r}")
        if room.casefold() in seen_rooms:
            raise CampaignError(f"room was supplied more than once: {room!r}")
        rooms.append(room)
        seen_rooms.add(room.casefold())
    if args.repeat < 1 or args.repeat > 100:
        raise CampaignError("--repeat must be between 1 and 100")
    if not math.isfinite(args.timeout_seconds) or args.timeout_seconds <= 0 or args.timeout_seconds > 3600:
        raise CampaignError("--timeout-seconds must be greater than 0 and at most 3600")
    launches = len(rooms) * (args.repeat * len(CACHE_KINDS) * len(ARMS) + len(ARMS))
    if launches > MAX_SUBPROCESS_RUNS:
        raise CampaignError(f"campaign would launch {launches} processes; limit is {MAX_SUBPROCESS_RUNS}")
    dotnet = _resolve_dotnet(args.dotnet)
    for source in (baseline, candidate, paths_file, maps_dir, *unique_asset_sources):
        if _inside(output, source) or _inside(source, output):
            raise CampaignError(f"--output must be outside campaign inputs: {source}")
    if output.exists():
        if not output.is_dir() or any(output.iterdir()):
            raise CampaignError(f"--output must be a fresh empty directory: {output}")
    else:
        output.mkdir(parents=True)
    return {
        "baseline": baseline,
        "candidate": candidate,
        "gamePaths": paths_file,
        "mapsDir": maps_dir,
        "mapPackages": map_packages,
        "mapPackagesSha256": map_packages_hash,
        "mapPackageBytes": map_package_bytes,
        "assetRoots": asset_roots,
        "assetSources": unique_asset_sources,
        "output": output,
        "rooms": rooms,
        "repeat": args.repeat,
        "timeoutSeconds": float(args.timeout_seconds),
        "dotnet": dotnet,
        "launches": launches,
    }


def _sample_groups(samples: list[dict[str, Any]], rooms: Sequence[str]) -> list[dict[str, Any]]:
    groups: list[dict[str, Any]] = []
    for room in rooms:
        for cache_kind in CACHE_KINDS:
            for arm in ARMS:
                values = [
                    sample["loadMilliseconds"] for sample in samples
                    if sample["room"] == room and sample["cacheKind"] == cache_kind and sample["arm"] == arm
                ]
                if values:
                    groups.append({
                        "room": room,
                        "cacheKind": cache_kind,
                        "arm": arm,
                        "sampleCount": len(values),
                        "rawLoadMilliseconds": values,
                        "p50Milliseconds": nearest_rank(values, 50),
                        "p95Milliseconds": nearest_rank(values, 95),
                        "p99Milliseconds": nearest_rank(values, 99),
                    })
    return groups


def _save_campaign(output: Path, campaign: dict[str, Any], summary: dict[str, Any]) -> None:
    summary["aggregates"] = _sample_groups(summary["samples"], campaign["configuration"]["rooms"])
    write_json(output / "summary.json", summary)
    write_json(output / "campaign.json", campaign)


def _asset_provenance(roots: Sequence[dict[str, Any]]) -> list[dict[str, Any]]:
    return [
        {
            "lineIndex": root["lineIndex"],
            "key": root["key"],
            "treeSha256": root["treeSha256"],
            "fileCount": root["fileCount"],
            "sizeBytes": root["sizeBytes"],
        }
        for root in roots
    ]


def _asset_provenance_hash(roots: Sequence[dict[str, Any]]) -> str:
    payload = json.dumps(_asset_provenance(roots), sort_keys=True,
                         separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def _final_input_hashes(config: dict[str, Any]) -> dict[str, Any]:
    roots = config["assetRoots"]
    current_assets: dict[str, tuple[str, int, int]] = {}
    current_provenance: list[dict[str, Any]] = []
    for root in roots:
        source: Path = root["source"]
        identity = os.path.normcase(str(source))
        if identity not in current_assets:
            entries, digest, size = inventory_asset_tree(source)
            current_assets[identity] = (digest, size, len(entries))
        digest, size, file_count = current_assets[identity]
        current_provenance.append({
            "lineIndex": root["lineIndex"],
            "key": root["key"],
            "treeSha256": digest,
            "fileCount": file_count,
            "sizeBytes": size,
        })
    payload = json.dumps(current_provenance, sort_keys=True,
                         separators=(",", ":")).encode("utf-8")
    return {
        "baselineDllSha256": sha256_file(config["baseline"]),
        "candidateDllSha256": sha256_file(config["candidate"]),
        "gamePathsSha256": sha256_file(config["gamePaths"]),
        "mapPackagesSha256": inventory_package_catalog(config["mapsDir"])[1],
        "nativeAssetRootsSha256": hashlib.sha256(payload).hexdigest(),
    }


def _record_final_provenance(campaign: dict[str, Any], config: dict[str, Any],
                             input_hashes: dict[str, Any]) -> bool:
    try:
        final_hashes = _final_input_hashes(config)
    except Exception as exc:
        campaign["provenanceUnchangedAfterCampaign"] = False
        campaign["inputHashVerificationError"] = str(exc) or type(exc).__name__
        return False
    campaign["inputHashesAfterCampaign"] = final_hashes
    unchanged = final_hashes == input_hashes
    campaign["provenanceUnchangedAfterCampaign"] = unchanged
    if not unchanged:
        campaign["inputHashVerificationError"] = "one or more benchmark inputs changed during the campaign"
    return unchanged


def _record_run(
    *,
    output: Path,
    run_dir: Path,
    command: Sequence[str],
    dll: Path,
    room: str,
    profile: Path,
    package_catalog: dict[str, Any],
    paths_hash: str,
    runtime_namespace: str,
    timeout_seconds: float,
    dotnet_env: dict[str, str],
    popen_factory: Callable[..., Any] | None,
) -> tuple[dict[str, Any], dict[str, Any] | None]:
    run_dir.mkdir(parents=True, exist_ok=False)
    report = run_dir / "map-load-receipt.json"
    try:
        result = run_process(
            command=command,
            cwd=dll.parent,
            env=profile_environment(dotnet_env, profile),
            timeout_seconds=timeout_seconds,
            stdout_path=run_dir / "stdout.log",
            stderr_path=run_dir / "stderr.log",
            popen_factory=popen_factory,
        )
    except BaseException as exc:
        result = getattr(exc, "map_load_process_result", None)
        if not isinstance(result, dict):
            raise
        record = {
            **result,
            "status": "interrupted",
            "room": room,
            "dll": str(dll),
            "mapDirectory": package_catalog["privateCopyDirectory"],
            "mapPackageCatalogSha256": package_catalog["privateCopyPackageCatalogSha256"],
            "mapPackageCount": package_catalog["files"],
            "mapPackageBytes": package_catalog["sizeBytes"],
            "receipt": str(report.relative_to(output)),
            "pathsSha256": paths_hash,
            "profileLabel": profile.name,
            "customRuntimeNamespace": runtime_namespace,
            "receiptValidationError": "campaign interrupted while waiting for the owned game process",
        }
        write_json(run_dir / "run.json", record)
        raise
    record: dict[str, Any] = {
        **result,
        "status": "failed",
        "room": room,
        "dll": str(dll),
        "mapDirectory": package_catalog["privateCopyDirectory"],
        "mapPackageCatalogSha256": package_catalog["privateCopyPackageCatalogSha256"],
        "mapPackageCount": package_catalog["files"],
        "mapPackageBytes": package_catalog["sizeBytes"],
        "receipt": str(report.relative_to(output)),
        "pathsSha256": paths_hash,
        "profileLabel": profile.name,
        "customRuntimeNamespace": runtime_namespace,
        "receiptValidationError": None,
    }
    receipt: dict[str, Any] | None = None
    if result["startError"]:
        record["receiptValidationError"] = f"could not start process: {result['startError']}"
    elif result["timedOut"]:
        record["receiptValidationError"] = f"process exceeded timeout of {timeout_seconds:g} seconds"
    elif result["returnCode"] != 0:
        record["receiptValidationError"] = f"process returned {result['returnCode']!r}, expected 0"
    else:
        try:
            receipt = validate_receipt(report, room)
        except CampaignError as exc:
            record["receiptValidationError"] = str(exc)
        else:
            record["status"] = "passed"
            record["loadMilliseconds"] = receipt["readyElapsedMilliseconds"]
            record["graphics"] = receipt.get("graphics")
            record["process"] = receipt.get("process")
    paths_after = profile / "paths.txt"
    try:
        after_hash = sha256_file(paths_after)
    except OSError:
        after_hash = None
    record["effectivePathsSha256After"] = after_hash
    if after_hash != paths_hash:
        record["status"] = "failed"
        record["receiptValidationError"] = "private paths.txt changed during the process"
        record.pop("loadMilliseconds", None)
        receipt = None
    try:
        record["mapPackageCatalogSha256After"] = verify_private_package_catalog(package_catalog)
        record["privateMapCatalogUnchanged"] = True
    except CampaignError as exc:
        record["privateMapCatalogUnchanged"] = False
        record["status"] = "failed"
        record["receiptValidationError"] = str(exc)
        record.pop("loadMilliseconds", None)
        receipt = None
    write_json(run_dir / "run.json", record)
    return record, receipt


def run_campaign(args: argparse.Namespace, popen_factory: Callable[..., Any] | None = None) -> int:
    config = _validate_args(args)
    output: Path = config["output"]
    baseline: Path = config["baseline"]
    candidate: Path = config["candidate"]
    paths_file: Path = config["gamePaths"]
    maps_dir: Path = config["mapsDir"]

    maps_manifest = config["mapPackages"]
    maps_hash = config["mapPackagesSha256"]
    maps_bytes = config["mapPackageBytes"]
    input_hashes = {
        "baselineDllSha256": sha256_file(baseline),
        "candidateDllSha256": sha256_file(candidate),
        "gamePathsSha256": sha256_file(paths_file),
        "mapPackagesSha256": maps_hash,
        "nativeAssetRootsSha256": _asset_provenance_hash(config["assetRoots"]),
    }
    write_json(output / "inputs.json", {
        "schema": SCHEMA,
        "kind": "map-load-inputs",
        **input_hashes,
        "baselineDll": str(baseline),
        "candidateDll": str(candidate),
        "gamePathsFile": str(paths_file),
        "mapsDirectory": str(maps_dir),
        "mapPackages": maps_manifest,
        "mapPackageBytes": maps_bytes,
        "mapCatalogScope": "regular .ppmap packages only; loose JSON recipes and .pk3 files are excluded",
        "nativeAssetRoots": _asset_provenance(config["assetRoots"]),
    })
    campaign_id = uuid.uuid4()
    campaign: dict[str, Any] = {
        "schema": SCHEMA,
        "kind": "map-load-campaign",
        "campaignId": str(campaign_id),
        "status": "running",
        "startedUtc": utc_now(),
        "finishedUtc": None,
        "configuration": {
            "baselineDll": str(baseline),
            "candidateDll": str(candidate),
            "dotnet": config["dotnet"],
            "gamePathsFile": str(paths_file),
            "mapsDirectory": str(maps_dir),
            "mapCatalogScope": "regular .ppmap packages only; loose JSON recipes and .pk3 files are excluded",
            "maxMapPackageCount": MAX_MAP_FILES,
            "maxMapPackageBytesEach": MAX_MAP_PACKAGE_BYTES,
            "maxMapPackageBytes": MAX_MAP_BYTES,
            "maxMapCatalogScanEntries": MAX_MAP_CATALOG_ENTRIES,
            "maxMapCatalogScanBytes": MAX_MAP_BYTES,
            "rooms": config["rooms"],
            "repeat": config["repeat"],
            "players": PLAYERS,
            "seconds": SECONDS,
            "timeoutSeconds": config["timeoutSeconds"],
            "cacheKinds": list(CACHE_KINDS),
            "maxSubprocessRuns": MAX_SUBPROCESS_RUNS,
            "nativeAssetRoots": sorted({root["key"] for root in config["assetRoots"]}),
            "nativeAssetCopyLimitBytes": MAX_CAMPAIGN_ASSET_BYTES,
        },
        "inputHashes": input_hashes,
        "measurement": {
            "loadMillisecondsField": "receipt.readyElapsedMilliseconds",
            "readyDefinition": "request through the first successful actual present after all eight players are spawned and alive; confirmed by the runtime receipt",
            "processStartupIncluded": False,
            "percentileMethod": "nearest-rank: sorted[ceil(percentile * sampleCount / 100) - 1]",
            "order": "paired deterministic sequence; baseline-first on even trials and candidate-first on odd trials",
            "applicationCold": "fresh private PROJECT_PRIME_USER_DATA and PROJECT_PRIME_Q3_CACHE per sample; unique custom runtime namespace; fresh private .ppmap catalog copy per sample; native asset roots are copied once per campaign and reused",
            "persistentCacheWarm": "one preseed process per arm and room, excluded from samples; later fresh processes reuse that arm/room profile, Q3 import cache, custom runtime namespace, and private .ppmap catalog",
            "mapCatalogIsolation": "only regular .ppmap files are copied to private -mapdir snapshots; loose JSON recipes and .pk3 sources never reach the game process",
            "nativeAssetIsolation": "all configured non-empty native asset roots are copied once to a campaign-private temporary tree and paths.txt is rewritten to those copies; Export stays in the private user-data directory",
            "q3ImportCacheIsolation": "PROJECT_PRIME_Q3_CACHE is explicitly set to a cache directory inside each private profile; ambient/global Q3 import caches are not used",
        },
        "runnerEnvironment": {
            "python": sys.version.split()[0],
            "platform": platform.platform(),
            "machine": platform.machine(),
        },
        "privatePackageCatalogs": [],
        "provenanceUnchangedAfterCampaign": None,
    }
    summary: dict[str, Any] = {
        "schema": SCHEMA,
        "kind": "map-load-summary",
        "status": "running",
        "campaign": "campaign.json",
        "inputs": "inputs.json",
        "samples": [],
        "preseeds": [],
        "runs": [],
        "aggregates": [],
        "failure": None,
    }
    _save_campaign(output, campaign, summary)
    run_number = 0
    dotnet_env = dict(os.environ)
    dotnet_env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    dotnet_env["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
    failure: str | None = None
    native_asset_rewrites: dict[int, Path] = {}
    try:
        with tempfile.TemporaryDirectory(prefix="project-prime-map-load-") as temporary_root:
            private_root = Path(temporary_root)
            try:
                private_path = private_root.resolve()
                protected_paths = (
                    baseline, candidate, paths_file, maps_dir, output,
                    *config["assetSources"],
                )
                for source in protected_paths:
                    if _inside(private_path, source) or _inside(source, private_path):
                        raise CampaignError(f"campaign temporary directory overlaps an input or evidence path: {source}")

                native_asset_rewrites, private_copies = copy_asset_roots(
                    config["assetRoots"], private_root
                )
                campaign["privateAssetCopies"] = private_copies
                write_json(output / "inputs.json", {
                    "schema": SCHEMA,
                    "kind": "map-load-inputs",
                    **input_hashes,
                    "baselineDll": str(baseline),
                    "candidateDll": str(candidate),
                    "gamePathsFile": str(paths_file),
                    "mapsDirectory": str(maps_dir),
                    "mapPackages": maps_manifest,
                    "mapPackageBytes": maps_bytes,
                    "mapCatalogScope": "regular .ppmap packages only; loose JSON recipes and .pk3 files are excluded",
                    "nativeAssetRoots": _asset_provenance(config["assetRoots"]),
                    "privateAssetCopies": private_copies,
                })

                for room_index, room in enumerate(config["rooms"], start=1):
                    room_slug = f"room-{room_index:02d}-{_slug(room)}"
                    for cache_kind in CACHE_KINDS:
                        warm_profiles: dict[str, tuple[Path, str, str, dict[str, Any]]] = {}
                        if cache_kind == "persistent-cache-warm":
                            for arm in ARMS:
                                profile = private_root / "warm" / room_slug / arm
                                paths_hash = make_profile(profile, paths_file, native_asset_rewrites)
                                package_catalog = copy_package_catalog(
                                    maps_dir,
                                    package_catalog_path_for(private_root, cache_kind, room_slug, arm),
                                    expected_manifest=maps_manifest,
                                    expected_hash=maps_hash,
                                    expected_bytes=maps_bytes,
                                )
                                package_catalog.update({
                                    "cacheKind": cache_kind,
                                    "room": room,
                                    "arm": arm,
                                    "lifetime": "preseed-and-samples",
                                })
                                campaign["privatePackageCatalogs"].append(package_catalog)
                                runtime_namespace = runtime_namespace_for(
                                    campaign_id, cache_kind, room, arm
                                )
                                warm_profiles[arm] = (profile, paths_hash, runtime_namespace, package_catalog)
                                run_number += 1
                                run_id = f"preseed-{room_slug}-{arm}"
                                run_dir = output / "runs" / cache_kind / room_slug / run_id
                                dll = baseline if arm == "baseline" else candidate
                                report = run_dir / "map-load-receipt.json"
                                command = build_command(
                                    config["dotnet"], dll, room,
                                    Path(package_catalog["privateCopyDirectory"]), report, runtime_namespace
                                )
                                record, _ = _record_run(
                                    output=output,
                                    run_dir=run_dir,
                                    command=command,
                                    dll=dll,
                                    room=room,
                                    profile=profile,
                                    package_catalog=package_catalog,
                                    paths_hash=paths_hash,
                                    runtime_namespace=runtime_namespace,
                                    timeout_seconds=config["timeoutSeconds"],
                                    dotnet_env=dotnet_env,
                                    popen_factory=popen_factory,
                                )
                                record.update({"cacheKind": cache_kind, "arm": arm, "runType": "preseed"})
                                write_json(run_dir / "run.json", record)
                                summary["runs"].append({
                                    "runId": run_id,
                                    "status": record["status"],
                                    "runMetadata": str((run_dir / "run.json").relative_to(output)),
                                    "receipt": record["receipt"],
                                    "cacheKind": cache_kind,
                                    "room": room,
                                    "arm": arm,
                                    "runType": "preseed",
                                })
                                if record["status"] != "passed":
                                    write_json(run_dir / "run.json", record)
                                    raise CampaignError(
                                        f"warm-cache preseed failed for {room!r} {arm}: {record['receiptValidationError']}"
                                    )
                                summary["preseeds"].append({
                                    "room": room,
                                    "arm": arm,
                                    "cacheKind": cache_kind,
                                    "runMetadata": str((run_dir / "run.json").relative_to(output)),
                                    "receipt": record["receipt"],
                                })
                                _save_campaign(output, campaign, summary)
                        for trial_index in range(config["repeat"]):
                            order = paired_order(trial_index)
                            for order_position, arm in enumerate(order, start=1):
                                run_number += 1
                                run_id = f"trial-{trial_index + 1:03d}-{order_position}-{arm}"
                                run_dir = output / "runs" / cache_kind / room_slug / run_id
                                dll = baseline if arm == "baseline" else candidate
                                if cache_kind == "application-cold":
                                    profile = private_root / "cold" / room_slug / run_id
                                    paths_hash = make_profile(profile, paths_file, native_asset_rewrites)
                                    package_catalog = copy_package_catalog(
                                        maps_dir,
                                        package_catalog_path_for(
                                            private_root, cache_kind, room_slug, arm, trial_index + 1
                                        ),
                                        expected_manifest=maps_manifest,
                                        expected_hash=maps_hash,
                                        expected_bytes=maps_bytes,
                                    )
                                    package_catalog.update({
                                        "cacheKind": cache_kind,
                                        "room": room,
                                        "arm": arm,
                                        "trialIndex": trial_index + 1,
                                        "lifetime": "single-sample",
                                    })
                                    campaign["privatePackageCatalogs"].append(package_catalog)
                                    runtime_namespace = runtime_namespace_for(
                                        campaign_id, cache_kind, room, arm, trial_index + 1
                                    )
                                else:
                                    profile, paths_hash, runtime_namespace, package_catalog = warm_profiles[arm]
                                report = run_dir / "map-load-receipt.json"
                                try:
                                    command = build_command(
                                        config["dotnet"], dll, room,
                                        Path(package_catalog["privateCopyDirectory"]), report, runtime_namespace
                                    )
                                    record, receipt = _record_run(
                                        output=output,
                                        run_dir=run_dir,
                                        command=command,
                                        dll=dll,
                                        room=room,
                                        profile=profile,
                                        package_catalog=package_catalog,
                                        paths_hash=paths_hash,
                                        runtime_namespace=runtime_namespace,
                                        timeout_seconds=config["timeoutSeconds"],
                                        dotnet_env=dotnet_env,
                                        popen_factory=popen_factory,
                                    )
                                finally:
                                    if cache_kind == "application-cold":
                                        remove_private_package_catalog(package_catalog)
                                record.update({
                                    "cacheKind": cache_kind,
                                    "arm": arm,
                                    "runType": "sample",
                                    "trialIndex": trial_index + 1,
                                    "pairedOrder": list(order),
                                    "orderPosition": order_position,
                                })
                                write_json(run_dir / "run.json", record)
                                run_ref = {
                                    "runId": run_id,
                                    "status": record["status"],
                                    "runMetadata": str((run_dir / "run.json").relative_to(output)),
                                    "receipt": record["receipt"],
                                    "cacheKind": cache_kind,
                                    "room": room,
                                    "arm": arm,
                                    "runType": "sample",
                                    "trialIndex": trial_index + 1,
                                    "pairedOrder": list(order),
                                    "orderPosition": order_position,
                                }
                                summary["runs"].append(run_ref)
                                if record["status"] != "passed" or receipt is None:
                                    _save_campaign(output, campaign, summary)
                                    raise CampaignError(
                                        f"sample {run_number}/{config['launches']} failed for {room!r} {arm} {cache_kind}: "
                                        f"{record['receiptValidationError']} (see {run_ref['runMetadata']})"
                                    )
                                sample = {
                                    "room": room,
                                    "arm": arm,
                                    "cacheKind": cache_kind,
                                    "trialIndex": trial_index + 1,
                                    "pairedOrder": list(order),
                                    "orderPosition": order_position,
                                    "customRuntimeNamespace": runtime_namespace,
                                    "loadMilliseconds": receipt["readyElapsedMilliseconds"],
                                    "receipt": record["receipt"],
                                    "runMetadata": run_ref["runMetadata"],
                                    "graphics": receipt.get("graphics"),
                                    "process": receipt.get("process"),
                                }
                                summary["samples"].append(sample)
                                _save_campaign(output, campaign, summary)
                                print(
                                    f"[{run_number}/{config['launches']}] {cache_kind} {room} {arm} "
                                    f"trial {trial_index + 1}: {sample['loadMilliseconds']:.3f} ms",
                                    flush=True,
                                )
                if not _record_final_provenance(campaign, config, input_hashes):
                    raise CampaignError(campaign.get("inputHashVerificationError", "input provenance changed"))
            except BaseException as exc:
                interrupted = isinstance(exc, (KeyboardInterrupt, SystemExit))
                failure = str(exc) or type(exc).__name__
                _record_final_provenance(campaign, config, input_hashes)
                campaign["finishedUtc"] = utc_now()
                campaign["status"] = "interrupted" if interrupted else "failed"
                summary["finishedUtc"] = campaign["finishedUtc"]
                summary["status"] = campaign["status"]
                summary["failure"] = failure
                _save_campaign(output, campaign, summary)
                if interrupted:
                    raise
            else:
                campaign["finishedUtc"] = utc_now()
                campaign["status"] = "complete"
                summary["finishedUtc"] = campaign["finishedUtc"]
                summary["status"] = campaign["status"]
                _save_campaign(output, campaign, summary)
    except BaseException as exc:
        if campaign["status"] == "running":
            failure = str(exc) or type(exc).__name__
            interrupted = isinstance(exc, (KeyboardInterrupt, SystemExit))
            _record_final_provenance(campaign, config, input_hashes)
            campaign["finishedUtc"] = utc_now()
            campaign["status"] = "interrupted" if interrupted else "failed"
            summary["finishedUtc"] = campaign["finishedUtc"]
            summary["status"] = campaign["status"]
            summary["failure"] = failure
            _save_campaign(output, campaign, summary)
        if isinstance(exc, (KeyboardInterrupt, SystemExit)):
            raise
    if failure:
        print(f"map-load-check: campaign failed: {failure}", file=sys.stderr)
        return 1
    if campaign["status"] != "complete":
        failure = summary.get("failure") or campaign.get("failure") or "campaign did not complete"
        print(f"map-load-check: campaign failed: {failure}", file=sys.stderr)
        return 1
    summary["finishedUtc"] = campaign["finishedUtc"]
    summary["status"] = campaign["status"]
    _save_campaign(output, campaign, summary)
    print(f"map-load-check: campaign complete; evidence: {output}", flush=True)
    return 0


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True, help="baseline ProjectPrime.dll")
    parser.add_argument("--candidate", type=Path, required=True, help="candidate ProjectPrime.dll")
    parser.add_argument("--dotnet", help="optional absolute path to the .NET SDK dotnet executable")
    parser.add_argument("--game-paths", type=Path, required=True, help="operator-owned paths.txt referencing extracted game data")
    parser.add_argument("--maps-dir", type=Path, required=True,
                        help="bounded source catalog of .ppmap packages; private snapshots are passed to -mapdir")
    parser.add_argument("--rooms", action="append", required=True, help="room name; repeat this option for each room")
    parser.add_argument("--repeat", type=int, default=20, help="paired samples per room and cache kind (default: 20; max: 100)")
    parser.add_argument("--timeout-seconds", type=float, default=180,
                        help="maximum time for each child process, including process startup (default: 180; max: 3600)")
    parser.add_argument("--output", type=Path, required=True, help="new or empty directory for logs, receipts, and summaries")
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        return run_campaign(args)
    except CampaignError as exc:
        print(f"map-load-check: {exc}", file=sys.stderr)
        return 2
    except KeyboardInterrupt:
        print("map-load-check: campaign interrupted; owned game process was stopped", file=sys.stderr)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
