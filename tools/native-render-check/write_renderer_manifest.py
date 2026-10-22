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


def digest_file(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--native-result", type=Path, required=True)
    parser.add_argument("--assets", type=Path, required=True, help="Extracted immutable files only; exclude user settings/logs")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--phase", required=True, choices=("initial-diagnostic", "final-integrated"))
    parser.add_argument("--contention", required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
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
    changed = subprocess.check_output(["git", "diff", "--name-only", "HEAD"], cwd=root, text=True).splitlines()
    untracked = subprocess.check_output(["git", "ls-files", "--others", "--exclude-standard"], cwd=root, text=True).splitlines()
    source_files = sorted(set(path for path in changed + untracked if path.startswith(("src/", "tools/"))
                              and not any(part in path.split("/") for part in ("bin", "obj", "__pycache__"))))
    source = {path: digest_file(root / path) for path in source_files if (root / path).is_file()}
    workload = receipt.get("observedWorkload", {})
    manifest = {
        "schema": 1, "phase": args.phase,
        "auditSource": "07f23c014a408d134d1cc67e2f1d7074fc7f7942",
        "baseCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip(),
        "workingSourceSha256": source,
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
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
