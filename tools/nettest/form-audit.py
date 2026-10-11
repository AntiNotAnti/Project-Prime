#!/usr/bin/env python3
"""Opt-in N9 applied-form audit; authority payloads remain the source of truth."""
import argparse
import json
import math
from pathlib import Path
import struct

PHASES = {0: "Unknown", 1: "Biped", 2: "Morphing", 3: "Alt", 4: "Unmorphing"}
IDENTITY = ("Epoch", "Match", "Slot", "Generation", "Life")


def integer(value, low=0, high=2**32-1):
    if type(value) is not int or not low <= value <= high:
        raise ValueError("invalid integer")
    return value


def newer(a, b, bits=32):
    delta = (a-b) % 2**bits
    return 0 < delta < 2**(bits-1)


def vector(row, fields):
    result = []
    for field in fields:
        value = row[field]
        if type(value) not in (int, float) or not math.isfinite(value) or abs(value) > 32768:
            raise ValueError("invalid finite position")
        result.append(struct.unpack("<f", struct.pack("<f", value))[0])
    return tuple(result)


def decode(row):
    identity = tuple(integer(row[f], 0 if f == "Slot" else 1,
                             7 if f == "Slot" else 2**64-1 if f == "Epoch" else 65535) for f in IDENTITY)
    source = row["Source"]
    if not isinstance(source, str) or len(source) != 56:
        raise ValueError("missing/invalid 28-byte source")
    raw = bytes.fromhex(source)
    frame, started, phase = struct.unpack_from("<IIB", raw)
    anchor = struct.unpack_from("<3f", raw, 9)
    active, hp, damage, body_hp = struct.unpack_from("<BHHH", raw, 21)
    if phase not in PHASES or phase == 0 or active > 1 or newer(started, frame):
        raise ValueError("invalid canonical form")
    if not all(math.isfinite(v) and abs(v) <= 32768 for v in anchor):
        raise ValueError("invalid canonical anchor")
    integer(row["ObservedFrame"])
    integer(row["BodyHp"], high=65535); integer(row["TurretHp"], high=65535)
    if type(row["TurretActive"]) is not bool or row["Phase"] not in PHASES.values():
        raise ValueError("invalid observed phase/presence")
    return identity, raw, dict(frame=frame, started=started, phase=PHASES[phase],
                               anchor=anchor, active=bool(active), hp=hp, damage=damage, body_hp=body_hp)


def audit(server, peers, summary, manifest, owner_slot=1):
    failures, missing = [], []
    metrics = {"peers": {}, "authoritySamples": 0, "independentAnchorWitnesses": 0,
               "healthDisagreements": [], "ownerPhaseDifferences": 0, "newerHealthJoins": 0}
    expected = {p["name"] for p in summary.get("processes", []) if p.get("name") != "server"}
    if not expected or expected != set(peers): missing.append("complete expected peer evidence unavailable")
    if summary.get("status") == "FAIL": failures.append("base campaign failed")
    elif summary.get("status") != "PASS": missing.append("base campaign did not pass")
    if summary.get("coverage", {}).get("boundedHeadlessDrain") != "PASS": missing.append("bounded campaign drain did not pass")
    if manifest.get("settings", manifest).get("scenario") != "alt-morph": failures.append("not an opt-in alt-morph campaign")
    settings = manifest.get("settings", manifest)
    authority = {}
    by_life = {}
    try:
        if server.get("Overflow", 0): failures.append("authority evidence overflow")
        for row in server["Forms"]:
            key, raw, form = decode(row)
            if row["Stage"] != "authority": raise ValueError("invalid authority stage")
            if vector(row, ("X", "Y", "Z")) != form["anchor"] or row["BodyHp"] != form["body_hp"] or row["TurretHp"] != form["hp"] or row["TurretActive"] != form["active"] or row["Phase"] != form["phase"]:
                raise ValueError("authority observed state differs from its source")
            full = (key, raw)
            if full in authority and authority[full]["BodyHp"] != row["BodyHp"]:
                raise ValueError("conflicting authority source")
            authority[full] = row
            by_life.setdefault(key, []).append((row, form))
        metrics["authoritySamples"] = len(authority)
    except (KeyError, TypeError, ValueError, struct.error) as error:
        failures.append("authority form evidence: " + str(error))
    for name, peer in peers.items():
        matched, phases, active, separated = 0, set(), 0, 0
        target_rows, last, seen, anchor_motion = [], {}, set(), {}
        try:
            if peer.get("Overflow", 0): failures.append(name + ": evidence overflow")
            for row in peer["Forms"]:
                key, raw, form = decode(row)
                full = (key, raw)
                expected_stage = "owner" if name == "peer-" + str(key[2]) else "observer"
                if row["Stage"] != expected_stage: raise ValueError("applied owner/observer role mismatch")
                if full in seen: raise ValueError("duplicate applied source")
                seen.add(full)
                if key in last and not newer(form["frame"], last[key]["frame"]): raise ValueError("stale applied source stream")
                if key in last and form["started"] != last[key]["started"] and not newer(form["started"], last[key]["started"]): raise ValueError("stale transition stream")
                last[key] = form
                source = authority.get(full)
                if source is None: raise ValueError("full identity/source absent from authority")
                matched += 1
                if vector(row, ("X", "Y", "Z")) != form["anchor"]: raise ValueError("observed turret anchor mismatch")
                if row["Stage"] == "observer" and row["Phase"] != form["phase"]: raise ValueError("observer phase mismatch")
                if row["Stage"] == "owner" and row["Phase"] != form["phase"]: metrics["ownerPhaseDifferences"] += 1
                hp_diff = (row["BodyHp"], row["TurretHp"], row["TurretActive"]) != (form["body_hp"], form["hp"], form["active"])
                if hp_diff:
                    observed_damage = row.get("ObservedDamageEvent")
                    detail = dict(peer=name, identity=key, sourceFrame=form["frame"], sourceDamage=form["damage"],
                                  observedDamage=observed_damage, bodyHp=row["BodyHp"], turretHp=row["TurretHp"], active=row["TurretActive"])
                    metrics["healthDisagreements"].append(detail)
                    if observed_damage is None:
                        missing.append(name + ": health/presence differs; observed damage revision unavailable")
                    elif not newer(integer(observed_damage, high=65535), form["damage"], 16):
                        raise ValueError("health/presence mismatch at same damage revision")
                    elif any(other["started"] == form["started"] and other["damage"] == observed_damage
                             and (other["body_hp"], other["hp"], other["active"]) ==
                                 (row["BodyHp"], row["TurretHp"], row["TurretActive"])
                             for candidate, other in by_life.get(key, [])):
                        metrics["newerHealthJoins"] += 1
                    else:
                        missing.append(name + ": newer health/presence lacks exact authority reconciliation")
                if key[2] != owner_slot: continue
                target_rows.append((row, form)); phases.add(form["phase"])
                if form["active"] and row["TurretActive"]:
                    active += 1
                    if all(f in row for f in ("BodyX", "BodyY", "BodyZ")):
                        body = vector(row, ("BodyX", "BodyY", "BodyZ"))
                        movement_key = (key, form["started"], form["anchor"])
                        previous_body = anchor_motion.setdefault(movement_key, body)
                        if math.dist(body, form["anchor"]) > .5 and math.dist(body, previous_body) > .25:
                            separated += 1
            if not matched: missing.append(name + ": no applied source joins")
            required = {"Biped", "Morphing", "Alt", "Unmorphing"}
            if not required <= phases: missing.append(name + ": target morph/unmorph/settled phase coverage incomplete")
            complete_cycle = False
            progress = {}
            for observed, form in target_rows:
                key = tuple(observed[f] for f in IDENTITY)
                step = progress.get(key, 0)
                if form["phase"] == ("Morphing", "Alt", "Unmorphing", "Biped")[step]:
                    step += 1
                    if step == 4: complete_cycle = True; step = 0
                progress[key] = step
            if not complete_cycle: missing.append(name + ": ordered same-life morph/alt/unmorph/settled cycle incomplete")
            if not active: missing.append(name + ": active Weavel turret not observed")
            if not separated: missing.append(name + ": independent turret/body anchor evidence missing")
            if name == "peer-2" and settings.get("observerDelay", settings.get("observer_delay", 0)):
                if not target_rows or not target_rows[0][1]["active"]:
                    missing.append(name + ": initial late-observer active anchor not exercised")
            metrics["peers"][name] = dict(sourceJoins=matched, targetPhases=sorted(phases), completeCycle=complete_cycle, activeSamples=active,
                                          independentAnchorWitnesses=separated,
                                          initialTarget=target_rows[0][1] if target_rows else None)
            metrics["independentAnchorWitnesses"] += separated
        except (KeyError, TypeError, ValueError, struct.error) as error:
            failures.append(name + ": form evidence: " + str(error))
    if not authority: missing.append("nonempty authority form sources required")
    return dict(status="FAIL" if failures else "INCOMPLETE" if missing else "PASS", failures=failures,
                incomplete=list(dict.fromkeys(missing)), metrics=metrics,
                scope="exact accepted form-source identity, observer phase and turret anchor/presence/health; owner animation phase may lead; existing base health/score/drain gates retained")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("campaign", type=Path); parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    def load(name): return json.loads((args.campaign / name).read_text())
    summary = load("summary.json")
    peers = {p["name"]: load(p["name"] + ".json") for p in summary["processes"] if p["name"] != "server"}
    result = audit(load("server.json"), peers, summary, load("manifest.json"))
    text = json.dumps(result, indent=2) + "\n"
    if args.output: args.output.write_text(text)
    else: print(text, end="")
    return 0 if result["status"] == "PASS" else 1


if __name__ == "__main__": raise SystemExit(main())
