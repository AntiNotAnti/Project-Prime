#!/usr/bin/env python3
"""Validate authoritative rendered-client reports; peers' counters may differ.

Usage: compare-reports.py CLIENT.log [CLIENT.log ...]
The renderer owns gameplay pass/fail thresholds. This checks its final result,
required sections, value types and finiteness, and reports prediction metrics
without inventing latency-dependent correction or damage-count thresholds.
"""
import argparse
import math
from pathlib import Path
import re


SCHEMA = {
    "AUTHCHECK": {"slot": int, "frames": int, "snapshots": int, "localTravel": float,
                "movingRemotes": int, "lit": bool, "result": str},
    "PREDICTION": {"samples": int, "meanError": float, "worstError": float,
                   "corrections": int, "hard": int, "historyMisses": int},
    "REPLICATION": {"world": bool, "combatEvents": int, "damageEvents": int},
    "SPECTATOR": {"requested": bool, "observed": bool, "rejoined": bool},
}


def read_report(path):
    """Return one complete, typed report or raise ValueError with its cause."""
    text = Path(path).read_text(errors="replace")
    if re.search(r"Unhandled exception|Stack overflow\.|Fatal error\.", text):
        raise ValueError("client crashed")
    report = {}
    for line in text.splitlines():
        parts = line.split()
        if not parts or parts[0] not in SCHEMA:
            continue
        section = parts[0]
        if section in report:
            raise ValueError(f"duplicate {section} section")
        values = {}
        for part in parts[1:]:
            key, separator, value = part.partition("=")
            if not separator or key in values:
                raise ValueError(f"malformed {section} field: {part}")
            values[key] = value
        if values.keys() != SCHEMA[section].keys():
            raise ValueError(f"unexpected or missing {section} fields")
        for key, kind in SCHEMA[section].items():
            value = values[key]
            if kind is bool:
                if value not in ("True", "False"):
                    raise ValueError(f"invalid {section}.{key}")
                values[key] = value == "True"
            elif kind in (int, float):
                try:
                    values[key] = kind(value)
                except ValueError as error:
                    raise ValueError(f"invalid {section}.{key}") from error
                if values[key] < 0 or (kind is float and not math.isfinite(values[key])):
                    raise ValueError(f"invalid {section}.{key}")
        report[section] = values
    missing = SCHEMA.keys() - report.keys()
    if missing:
        raise ValueError("missing sections: " + ", ".join(sorted(missing)))
    check = report["AUTHCHECK"]
    if check["result"] != "PASS":
        raise ValueError("AUTHCHECK result=" + check["result"])
    if check["slot"] > 7 or not check["lit"] or not report["REPLICATION"]["world"]:
        raise ValueError("inconsistent successful client state")
    spectator = report["SPECTATOR"]
    if spectator["requested"] and not spectator["observed"]:
        raise ValueError("requested spectator transition was not observed")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("logs", type=Path, nargs="+")
    args = parser.parse_args()
    failures = 0
    for path in args.logs:
        try:
            report = read_report(path)
        except (OSError, ValueError) as error:
            print(f"FAIL {path}: {error}")
            failures += 1
            continue
        check, prediction = report["AUTHCHECK"], report["PREDICTION"]
        replication = report["REPLICATION"]
        print(f"PASS {path}: frames={check['frames']} snapshots={check['snapshots']} "
              f"meanError={prediction['meanError']:.3f} worstError={prediction['worstError']:.3f} "
              f"hard={prediction['hard']} historyMisses={prediction['historyMisses']} "
              f"combatEvents={replication['combatEvents']} damageEvents={replication['damageEvents']}")
    print(f"{len(args.logs) - failures}/{len(args.logs)} authoritative client reports passed")
    return int(failures != 0)


if __name__ == "__main__":
    raise SystemExit(main())
