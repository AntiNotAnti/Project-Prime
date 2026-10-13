#!/usr/bin/env python3
"""Stream outstanding native stacks into disjoint allocation-origin categories."""
import argparse
import hashlib
import json
from pathlib import Path
import re

from analyze_malloc_history import GROUP, FRAME

GRAPHICS = {"com.apple.AGXMetalG16X", "com.apple.AppleMetalOpenGLRenderer", "com.apple.IOGPU",
            "GLEngine", "com.apple.opengl", "com.apple.Metal"}


def category(frames):
    if any(re.search(r"\b(mmap|mach_vm_allocate|vm_allocate)\b", f["symbol"]) for f in frames[:2]):
        return "explicitMmapVm"
    if any("FragmentProgramVariant" in f["symbol"] for f in frames):
        return "agxFragmentVariant"
    if any("VertexProgramVariant" in f["symbol"] for f in frames):
        return "agxVertexVariant"
    modules = {f["module"] for f in frames}
    if "com.apple.IOSurface" in modules:
        return "ioSurface"
    if modules.intersection(GRAPHICS):
        return "otherGraphics"
    if "libcoreclr.dylib" in modules:
        return "coreclr"
    return "other"


def parse(lines):
    totals = {}
    group = None
    def flush():
        if group is None:
            return
        key = category(group["frames"])
        total = totals.setdefault(key, {"bytes": 0, "calls": 0, "groups": 0,
                                       "mallocPrefixBytes": 0, "mallocPrefixCalls": 0})
        for metric in ("bytes", "calls"):
            total[metric] += group[metric]
        # Surface/IOGPU resource records may also carry compiler callers. Keep
        # direct allocator frames distinct from those resource records; neither
        # the category total nor absence of this prefix is a heap census.
        if key != "explicitMmapVm" and any(f["module"] == "libsystem_malloc.dylib" for f in group["frames"][:3]):
            total["mallocPrefixBytes"] += group["bytes"]
            total["mallocPrefixCalls"] += group["calls"]
        total["groups"] += 1
    for line in lines:
        match = GROUP.match(line)
        if match:
            flush()
            group = {"calls": int(match[1]), "bytes": int(match[2]), "frames": []}
        elif group is not None:
            frame = FRAME.match(line)
            if frame:
                group["frames"].append({"module": frame[1].strip(), "symbol": frame[2].strip()})
    flush()
    return {"supported": bool(totals), "categories": totals,
            "meaning": "Disjoint first-match allocation-site partition: explicit mmap/VM in first two frames, fragment variant, vertex variant, IOSurface, other graphics modules, CLR, other. mallocPrefix counts require libsystem_malloc in first three frames and exclude explicit VM; they distinguish observed direct allocator stacks from platform resource records without claiming every allocation mechanism is classified. Input must be current outstanding -allBySize groups without highWaterMark/allEvents. Platform resource records must not be summed into malloc-zone bytes. Mapped/reserved lengths are not residency. Origin does not establish retaining owner, cache boundedness, or leak."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    with args.capture.open() as stream:
        result = parse(stream)
    digest = hashlib.sha256()
    with args.capture.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    result["source"] = {"path": str(args.capture.resolve()), "sha256": digest.hexdigest()}
    if args.output.exists():
        parser.error("output must be fresh to preserve prior evidence")
    args.output.write_text(json.dumps(result, indent=2) + "\n")


if __name__ == "__main__":
    main()
