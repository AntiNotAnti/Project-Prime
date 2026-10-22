#!/usr/bin/env python3
"""Summarize malloc_history -allBySize outstanding allocation-site groups."""
import argparse
import hashlib
import json
from pathlib import Path
import re

GROUP = re.compile(r"(?m)^(\d+) calls for (\d+) bytes:\s*\n")
FRAME = re.compile(r"^\d+\s+(.+?)\s+0x[0-9a-fA-F]+\s+(.+)$")


def parse(text):
    matches = list(GROUP.finditer(text))
    groups = []
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        frames = []
        for line in text[match.end():end].splitlines():
            frame = FRAME.match(line)
            if frame:
                frames.append({"module": frame[1].strip(), "symbol": frame[2].strip()})
        vm = any(re.search(r"\b(mmap|mach_vm_allocate|vm_allocate)\b", f["symbol"]) for f in frames[:2])
        openal = any(f["module"] == "com.apple.audio.OpenAL" or "libopenal" in f["module"].lower() for f in frames)
        vectors = openal and any("BufferInfo" in f["symbol"] and ("vector<" in f["symbol"] or "allocator<" in f["symbol"]) for f in frames)
        groups.append({"calls": int(match[1]), "bytes": int(match[2]), "virtualMapping": vm,
                       "openAl": openal, "sourceBufferInfoVectors": vectors, "frames": frames})
    def total(predicate, key):
        return sum(g[key] for g in groups if predicate(g))
    return {"supported": bool(groups), "groups": len(groups),
            "openAlMallocBytes": total(lambda g: g["openAl"] and not g["virtualMapping"], "bytes"),
            "openAlMallocCalls": total(lambda g: g["openAl"] and not g["virtualMapping"], "calls"),
            "sourceVectorBytes": total(lambda g: g["sourceBufferInfoVectors"] and not g["virtualMapping"], "bytes"),
            "sourceVectorCalls": total(lambda g: g["sourceBufferInfoVectors"] and not g["virtualMapping"], "calls"),
            "virtualMappingGroupBytes": total(lambda g: g["virtualMapping"], "bytes"),
            "topGroups": sorted(groups, key=lambda g: g["bytes"], reverse=True)[:30],
            "meaning": "Input must be malloc_history -allBySize without -highWaterMark: currently outstanding groups, not cumulative allocation traffic. mmap/VM-region group bytes are reserved/mapped sizes, not resident bytes. Stack origin identifies allocation site, not whether storage is leaked or a library cache. Stack logging perturbs allocator behavior."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = parse(args.capture.read_text())
    result["source"] = {"path": str(args.capture.resolve()), "sha256": hashlib.sha256(args.capture.read_bytes()).hexdigest(),
                        "requiredCaptureCommand": "malloc_history PID -allBySize -fullStacks (no -highWaterMark)"}
    text = json.dumps(result, indent=2) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text)
    else:
        print(text, end="")


if __name__ == "__main__":
    main()
