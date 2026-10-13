#!/usr/bin/env python3
"""Compare rounded macOS VM categories; categories are not allocation stacks."""
import argparse
import hashlib
import json
from pathlib import Path
import re

NUMBER = r"\d+(?:\.\d+)?[KMGT]?"
ROW = re.compile(r"^(?P<name>.+?)\s{2,}(?P<values>" + NUMBER + r"(?:\s+" + NUMBER + r"){3,})")
UNITS = {"": 1, "K": 1024, "M": 1024**2, "G": 1024**3, "T": 1024**4}


def byte_value(value):
    match = re.fullmatch(r"(\d+(?:\.\d+)?)([KMGT]?)", value)
    if not match:
        raise ValueError("unsupported vmmap quantity: " + value)
    return float(match[1]) * UNITS[match[2]]


def parse(text):
    result = {"regions": {}, "mallocZones": {}, "physicalFootprintBytes": None, "supported": False}
    section = None
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("Physical footprint:"):
            result["physicalFootprintBytes"] = byte_value(stripped.split(":", 1)[1].strip())
        if stripped.startswith("REGION TYPE"):
            section = "regions"
            continue
        if stripped.startswith("MALLOC ZONE"):
            section = "mallocZones"
            continue
        if not section or stripped.startswith(("=", "TOTAL")):
            continue
        match = ROW.match(stripped)
        if not match:
            continue
        values = match["values"].split()
        name = re.sub(r"_0x[0-9a-fA-F]+$", "", match["name"])
        fields = {key: byte_value(value) for key, value in zip(
            ("virtualBytes", "residentBytes", "dirtyBytes", "swappedBytes"), values)}
        if section == "mallocZones" and len(values) >= 6:
            fields["allocationCount"] = int(float(values[4]))
            fields["allocatedBytes"] = byte_value(values[5])
        result[section][name] = fields
    result["supported"] = bool(result["regions"])
    return result


def compare(baseline, candidate):
    def section_delta(section):
        result = {}
        for name in sorted(baseline[section].keys() | candidate[section].keys()):
            a, b = baseline[section].get(name), candidate[section].get(name)
            result[name] = {"baseline": a, "candidate": b,
                            "candidateMinusBaseline": {key: b[key] - a[key] for key in a.keys() & b.keys()} if a and b else None}
        return result
    return {"schema": 1, "supported": baseline["supported"] and candidate["supported"],
            "physicalFootprintBytes": {"baseline": baseline["physicalFootprintBytes"], "candidate": candidate["physicalFootprintBytes"]},
            "regions": section_delta("regions"), "mallocZones": section_delta("mallocZones"),
            "meaning": "Rounded OS category/allocator observations. Resident, dirty and allocated are different columns; missing categories remain unavailable. No exact allocation owner, driver residency or leak classification is inferred. Sampling times can differ from JSON samples."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = compare(parse(args.baseline.read_text()), parse(args.candidate.read_text()))
    result["sources"] = [{"path": str(path.resolve()), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()} for path in [args.baseline, args.candidate]]
    text = json.dumps(result, indent=2) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text)
    else:
        print(text, end="")


if __name__ == "__main__":
    main()
