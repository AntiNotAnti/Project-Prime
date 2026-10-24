"""Bind an existing native receipt to source, fixture and explicit unknowns.

Does not infer source attestation or hardware acceptance from file hashes.
Run before/after native campaigns; never hash the fixture on the render thread.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
from datetime import datetime, timezone


def digest_file(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def source_snapshot(root, excluded_paths=()):
    # Full tracked inventory includes assets/project files, not merely a diff.
    tracked = subprocess.check_output(["git", "ls-files", "-z"], cwd=root).decode().split("\0")
    untracked = subprocess.check_output(["git", "ls-files", "--others", "--exclude-standard", "-z"], cwd=root).decode().split("\0")
    excluded = set()
    for path in excluded_paths:
        try:
            excluded.add(Path(path).resolve().relative_to(root.resolve()).as_posix())
        except ValueError:
            pass
    paths = sorted(set(path for path in tracked + untracked if path and path not in excluded
                       and not any(part in path.split("/") for part in ("bin", "obj", "__pycache__"))))
    files = {path: digest_file(root / path) if (root / path).is_file() else None for path in paths}
    tree = hashlib.sha256(json.dumps(files, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    return {"schema": 1, "baseCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip(),
            "capturedUtc": datetime.now(timezone.utc).isoformat(), "files": files, "sourceTreeSha256": tree,
            "excludedEvidencePaths": sorted(excluded),
            "scope": "All tracked files plus nonignored untracked files, excluding build/cache directories and listed evidence outputs; missing tracked files are null. Inventory alone is not build attestation."}


def verify_build_binding(path, snapshot, workload, frozen_build=None, end_snapshot=None):
    binding = json.loads(path.read_text(encoding="utf-8-sig"))
    expected = {"sourceSnapshotSha256": digest_file(snapshot),
                "clientSha256": (workload.get("clientBinary") or {}).get("sha256"),
                "harnessSha256": (workload.get("harnessBinary") or {}).get("sha256")}
    for key, value in expected.items():
        observed = binding.get(key)
        if not isinstance(value, str) or len(value) != 64 or any(c not in '0123456789abcdef' for c in value.lower()) \
                or not isinstance(observed, str) or observed.lower() != value.lower():
            raise ValueError("Explicit build binding mismatch: " + key)
    if 'buildInputMatch' in binding and binding['buildInputMatch'] is not True:
        raise ValueError("Explicit build metadata reports changed/unverified build inputs")
    inputs_verified = False
    if end_snapshot:
        if binding.get('endSnapshotSha256', '').lower() != digest_file(end_snapshot):
            raise ValueError("Explicit build binding mismatch: endSnapshotSha256")
        before = json.loads(snapshot.read_text(encoding='utf-8-sig'))['files']
        after = json.loads(end_snapshot.read_text(encoding='utf-8-sig'))['files']
        def inputs(files):
            return {name: digest for name, digest in files.items() if name.startswith(('src/', 'tools/', 'native/'))
                    or ('/' not in name and (name.endswith(('.props', '.targets', '.sln', '.slnx', '.csproj'))
                                             or name in ('global.json', 'NuGet.Config', 'NuGet.config', 'nuget.config', '.gitmodules')))}
        if inputs(before) != inputs(after):
            raise ValueError("Before/after build-input inventories differ")
        inputs_verified = True
    if frozen_build:
        for key, name in (("clientSha256", "ProjectPrime.dll"), ("harnessSha256", "native-render-check.dll")):
            if digest_file(frozen_build / name).lower() != expected[key].lower():
                raise ValueError("Frozen binary differs from loaded receipt: " + name)
    return {"metadataSha256": digest_file(path), "sourceSnapshotSha256": expected["sourceSnapshotSha256"],
            "status": "DIGESTS_MATCH", "metadata": binding,
            "buildInputsIndependentlyCompared": inputs_verified,
            "meaning": "Explicit supplied build metadata matches source-snapshot and observed binary digests; this tool does not manufacture build provenance."}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--native-result", type=Path)
    parser.add_argument("--assets", type=Path, help="Extracted immutable files only; exclude user settings/logs")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--phase", choices=("initial-diagnostic", "final-integrated"))
    parser.add_argument("--contention")
    parser.add_argument("--snapshot-source", action="store_true", help="Write full current source inventory only, before/after a build")
    parser.add_argument("--source-snapshot", type=Path, help="Explicit build-time full source inventory retained by the operator")
    parser.add_argument("--build-binding", type=Path, help="Explicit JSON: sourceSnapshotSha256, clientSha256, harnessSha256")
    parser.add_argument("--frozen-build", type=Path, help="Also verify frozen DLL digests against the observed receipt")
    parser.add_argument("--end-source-snapshot", type=Path, help="Verify explicit endSnapshotSha256 and compare src/tools/native/root build inputs before/after")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    if args.output.exists():
        parser.error("output must be fresh; previous evidence must not be overwritten")
    if args.snapshot_source:
        inventory = source_snapshot(root, [args.output])
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with args.output.open("x", encoding="utf-8") as stream:
            json.dump(inventory, stream, indent=2); stream.write("\n")
        return
    if not args.native_result or not args.assets or not args.phase or not args.contention:
        parser.error("native-result, assets, phase and contention are required for a native manifest")
    if args.build_binding and not args.source_snapshot:
        parser.error("build-binding requires source-snapshot")
    if args.end_source_snapshot and not args.build_binding:
        parser.error("end-source-snapshot requires build-binding")
    if not args.assets.is_dir():
        parser.error("assets must be an existing immutable extracted-files directory")
    receipt = json.loads(args.native_result.read_text(encoding="utf-8-sig"))
    fixture = hashlib.sha256()
    count = total = 0
    for path in sorted(args.assets.rglob("*"), key=lambda item: item.relative_to(args.assets).as_posix()):
        if path.is_symlink():
            raise ValueError("Asset identity requires physical files, not symbolic links")
        if path.is_file():
            relative = path.relative_to(args.assets).as_posix()
            size = path.stat().st_size
            fixture.update((relative + "\0" + str(size) + "\0" + digest_file(path) + "\n").encode())
            count += 1
            total += size
    current_source = source_snapshot(root, [args.output])
    workload = receipt.get("observedWorkload", {})
    manifest = {
        "schema": 1, "phase": args.phase,
        "auditSource": "07f23c014a408d134d1cc67e2f1d7074fc7f7942",
        "baseCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip(),
        "workingSourceSha256": current_source["files"],
        "workingSourceTreeSha256": current_source["sourceTreeSha256"],
        "sourceBindingScope": "Working files at manifest creation; loaded binaries are independently observed. No build attestation.",
        "nativeReceiptSha256": digest_file(args.native_result),
        "loadedClient": workload.get("clientBinary"), "loadedHarness": workload.get("harnessBinary"),
        "assets": {"sha256": fixture.hexdigest(), "files": count, "bytes": total,
                   "contract": "sorted relative path NUL size NUL content SHA256 newline"},
        "platform": {"system": platform.system(), "version": platform.version(),
                     "vendor": receipt.get("vendor"), "renderer": receipt.get("renderer"), "driver": receipt.get("glVersion")},
        "settings": {key: receipt.get(key) for key in ("room", "cap", "effectiveCap", "softwareFrequency", "vsync",
                    "shadows", "textureQuality", "lighting", "resolutionScale", "framebuffer", "renderSize")},
        "windowStart": receipt.get("initialWindow"), "windowEnd": receipt.get("finalWindow"),
        "flags": {key: value for key, value in sorted(os.environ.items())
                  if key.startswith(("PROJECT_PRIME_GL_", "PROJECT_PRIME_ANDROID_PERF_"))},
        "profiling": "Use native raw-capture condition metadata; no missing observation is inferred off",
        "contention": args.contention,
        "nativeAcceptance": "UNVERIFIED", "otherPlatforms": "NOT_RUN (Windows-only user constraint)",
    }
    if args.source_snapshot:
        manifest["sourceSnapshot"] = {"sha256": digest_file(args.source_snapshot),
                                      "scope": "Explicit supplied build-time snapshot; retained separately"}
    manifest["buildBinding"] = verify_build_binding(args.build_binding, args.source_snapshot, workload, args.frozen_build, args.end_source_snapshot) if args.build_binding else {
        "status": "UNVERIFIED", "meaning": "No explicit build metadata supplied; current source hashes do not attest loaded binaries."}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as stream:
        stream.write(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    main()
