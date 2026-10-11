#!/usr/bin/env python3
"""Opt-in N10 cosmetic provenance audit. Draw submissions are not pixels."""
import argparse
import json
import math
from pathlib import Path
import struct


def decode(source):
    if not isinstance(source, str) or len(source) != 106:
        raise ValueError("missing/invalid 53-byte impact source")
    try:
        raw = bytes.fromhex(source)
        values = struct.unpack("<QHBHHIIIBHHHIBBB3f", raw)
    except (ValueError, struct.error):
        raise ValueError("invalid impact source encoding")
    epoch, match, shooter, generation, life, shot, component, phase, victim, vg, vl, damage, frame, weapon, flags, provenance, x, y, z = values
    if not (epoch and match and shooter < 8 and generation and life and victim < 8 and vg and vl and damage
            and (weapon <= 8 or weapon == 255) and provenance in (1, 2, 3)
            and (shot != 0 or component == 0 and provenance == 3)
            and (flags & 32 or phase == 0)
            and all(math.isfinite(v) and abs(v) < 100000 for v in (x, y, z))):
        raise ValueError("invalid canonical impact identity/endpoint")
    return raw, (epoch, match, shooter, generation, life, shot, component, phase, victim, vg, vl, damage)


def count(report, name):
    value = report[name]
    if type(value) is not int or not 0 <= value <= 2**63-1:
        raise ValueError("invalid " + name + " counter")
    return value


def audit(server, peers, summary, manifest):
    failures, incomplete = [], []
    metrics = {"authorityPublished": 0, "peers": {}}
    settings = manifest.get("settings", manifest)
    enabled = settings.get("confirmed_impacts", settings.get("confirmedImpacts"))
    if type(enabled) is not bool: incomplete.append("explicit confirmed_impacts setting unavailable")
    expected = {p["name"] for p in summary.get("processes", []) if p.get("name") != "server"}
    if not expected or set(peers) != expected: incomplete.append("complete expected peer evidence unavailable")
    if summary.get("status") == "FAIL": failures.append("base campaign failed")
    elif summary.get("status") != "PASS": incomplete.append("base campaign did not pass")
    if summary.get("coverage", {}).get("boundedHeadlessDrain") != "PASS": incomplete.append("bounded campaign drain did not pass")
    published = set()

    def inspect(name, evidence, authority):
        report = evidence.get("ConfirmedImpacts")
        if not isinstance(report, dict):
            incomplete.append(name + ": confirmed impact evidence missing"); return None
        try:
            if type(report["Enabled"]) is not bool: raise ValueError("invalid enabled field")
            if type(enabled) is bool and report["Enabled"] != enabled: raise ValueError("feature enabled state disagrees with manifest")
            for field in ("Published", "Accepted", "Duplicates", "Rejected", "Overflow", "EvidenceOverflow", "CueDrawSubmissions", "ExactProjectileDrawSubmissions"):
                count(report, field)
            if report["Overflow"] or report["EvidenceOverflow"]: raise ValueError("bounded cosmetic/evidence overflow")
            rows = {}
            for field, counter in (("PublishedFacts", "Published"), ("AcceptedFacts", "Accepted")):
                if not isinstance(report[field], list): raise ValueError("missing source array")
                if len(report[field]) != report[counter]: raise ValueError(counter + " count disagrees with retained exact sources")
                rows[field] = [decode(source) for source in report[field]]
            if authority and rows["AcceptedFacts"]: raise ValueError("authority cannot accept client cosmetic facts")
            if not authority and rows["PublishedFacts"]: raise ValueError("client cannot publish authoritative facts")
            if enabled is False and any(report[f] for f in ("Published", "Accepted", "Duplicates", "Rejected", "Overflow", "CueDrawSubmissions", "ExactProjectileDrawSubmissions")):
                raise ValueError("off control issued, accepted or drew cosmetic work")
            if enabled is True and not rows["PublishedFacts" if authority else "AcceptedFacts"]:
                incomplete.append(name + ": no live confirmed impact coverage")
            return report, rows
        except (KeyError, TypeError, ValueError) as exc:
            failures.append(name + ": " + str(exc)); return None

    authority = inspect("server", server, True)
    if authority:
        published = {raw for raw, _ in authority[1]["PublishedFacts"]}
        metrics["authorityPublished"] = authority[0]["Published"]
        metrics["authorityDistinctPublished"] = len(published)
    for name, evidence in peers.items():
        peer = inspect(name, evidence, False)
        if peer is None: continue
        report, rows = peer
        seen, accepted = set(), set()
        for raw, key in rows["AcceptedFacts"]:
            if key in seen: failures.append(name + ": duplicate accepted full identity/component/phase/victim/damage")
            seen.add(key); accepted.add(raw)
            if authority and raw not in published: failures.append(name + ": accepted impact lacks exact authority source")
        metrics["peers"][name] = {"accepted": report["Accepted"], "distinctAccepted": len(accepted),
            "duplicatesRejected": report["Duplicates"], "rejected": report["Rejected"],
            "missingDeliveryCount": len(published - accepted) if authority else None,
            "cueDrawSubmissions": report["CueDrawSubmissions"], "exactProjectileDrawSubmissions": report["ExactProjectileDrawSubmissions"]}
    return {"status": "FAIL" if failures else "INCOMPLETE" if incomplete else "PASS",
        "failures": failures, "incomplete": incomplete, "metrics": metrics,
        "scope": "exact accepted cosmetic source provenance and bounded live coverage; unreliable missing deliveries are reported, not successful delivery or gameplay failures; draw submission counts are not visible-pixel metrics"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("campaign", type=Path); parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    def load(name):
        try: return json.loads((args.campaign / name).read_text())
        except (OSError, ValueError): return {}
    summary = load("summary.json")
    peers = {p["name"]: load(p["name"] + ".json") for p in summary.get("processes", []) if p.get("name") != "server"}
    result = audit(load("server.json"), peers, summary, load("manifest.json"))
    text = json.dumps(result, indent=2) + "\n"
    if args.output: args.output.write_text(text)
    else: print(text, end="")
    return 0 if result["status"] == "PASS" else 1


if __name__ == "__main__": raise SystemExit(main())
