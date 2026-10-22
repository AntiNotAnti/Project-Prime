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
        if type(value) not in (int, float) or not math.isfinite(value) or abs(value) > 100000:
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
    if not all(math.isfinite(v) and abs(v) <= 100000 for v in anchor):
        raise ValueError("invalid canonical anchor")
    integer(row["ObservedFrame"])
    integer(row["BodyHp"], high=65535); integer(row["TurretHp"], high=65535)
    if type(row["TurretActive"]) is not bool or row["Phase"] not in PHASES.values():
        raise ValueError("invalid observed phase/presence")
    return identity, raw, dict(frame=frame, started=started, phase=PHASES[phase],
                               anchor=anchor, active=bool(active), hp=hp, damage=damage, body_hp=body_hp)


def lethal_ack_floor(row, identity, source, bases):
    """Exact retained lethal receipt, joined to an authority death outcome."""
    if row["BodyHp"] != 1 or source["body_hp"] <= 0: return False
    raw = row.get("BodyAckSource")
    if raw is None: return None
    if not isinstance(raw,str) or len(raw)!=46: raise ValueError("invalid retained CombatAck23")
    try: data=bytes.fromhex(raw)
    except ValueError: raise ValueError("invalid retained CombatAck23")
    claim,result,victim,generation,life,applied,hp,turret,sequence,flags,shot = struct.unpack("<HBBHHHHHIBI",data)
    if not (claim>0 and shot>0 and result in (0,1,13) and (victim,generation,life)==identity[2:]
            and applied>0 and hp==0 and 0<sequence<=65535 and sequence==row.get("ObservedDamageEvent")
            and newer(sequence,source["damage"],16) and flags & 2 and flags & 64 and flags<=127):
        raise ValueError("retained ACK lacks exact accepted lethal victim/outcome proof")
    return any(newer(basis["frame"],source["frame"]) and basis["body_hp"]==0 and basis["hp"]==turret==row["TurretHp"]
               and basis["active"]==row["TurretActive"] for basis in bases)


def terminal_ack_body_join(server, peer, row, identity, source, local_slot):
    """Independent authority terminal bytes plus this client's exact receipt key.

    A fast body ACK can precede the next form snapshot, including a native morph
    later in the authority step. This proves only the historical body outcome;
    turret presence/health, phase and anchor still use the unchanged form gates.
    Missing, ambiguous or unsupported evidence never supplies a basis.
    """
    if (type(row.get("BodyPredictionDebit")) is not int or row["BodyPredictionDebit"] != 0
            or type(row.get("BodyHp")) is not int or not 0 < row["BodyHp"] <= 65535
            or type(row.get("ObservedDamageEvent")) is not int or not 0 < row["ObservedDamageEvent"] <= 65535):
        return None
    raw = row.get("BodyAckSource")
    if raw is None: return None
    if not isinstance(raw, str) or len(raw) != 46:
        raise ValueError("invalid retained CombatAck23")
    data = bytes.fromhex(raw)
    fields = ("ClaimId", "Result", "VictimSlot", "VictimGeneration", "VictimLife",
              "DamageApplied", "HealthAfter", "HalfturretHealthAfter", "DamageSequence", "Flags", "ShotId")
    ack = dict(zip(fields, struct.unpack("<HBBHHHHHIBI", data)))
    if not (ack["ClaimId"] > 0 and ack["ShotId"] > 0 and ack["Result"] in (0, 1, 13)
            and tuple(ack[f] for f in fields[2:5]) == identity[2:]
            and ack["DamageApplied"] > 0 and ack["HealthAfter"] == row["BodyHp"]
            and ack["HalfturretHealthAfter"] == row["TurretHp"]
            and 0 < ack["DamageSequence"] <= 65535
            and ack["DamageSequence"] == row.get("ObservedDamageEvent")
            and newer(ack["DamageSequence"], source["damage"], 16)
            and ack["Flags"] & 64 and not ack["Flags"] & (2 | 32) and ack["Flags"] <= 127):
        return None
    joins, timeline = server.get("TimedCaseJoins", {}), peer.get("ShotTimeline", {})
    if not isinstance(joins, dict) or not isinstance(timeline, dict): return None
    if (type(joins.get("Schema")) is not int or joins["Schema"] != 1
            or type(timeline.get("Schema")) is not int or timeline["Schema"] != 2
            or timeline.get("Enabled") is not True or timeline.get("Complete") is not True
            or type(timeline.get("Overflow")) is not int or timeline["Overflow"] != 0
            or type(timeline.get("UnkeyedRows")) is not int or timeline["UnkeyedRows"] != 0):
        return None
    terminals = joins.get("Terminals")
    budgets = joins.get("Budgets", {})
    if not isinstance(budgets, dict): return None
    budget = budgets.get("Terminals", {})
    if (not isinstance(terminals, list) or not isinstance(budget, dict)
            or any(type(budget.get(f)) is not int for f in ("Dropped", "Count", "Observed"))
            or budget.get("Dropped") != 0
            or budget.get("Count") != len(terminals) or budget.get("Observed") != len(terminals)):
        return None
    receipt_rows = timeline.get("Rows")
    if (not isinstance(receipt_rows, list) or any(not isinstance(r, dict) for r in receipt_rows)
            or any(not isinstance(t, dict) or not isinstance(t.get("Outcome"), dict) for t in terminals)):
        return None
    def shot_key(value):
        if not isinstance(value, dict): raise ValueError("invalid terminal shot key")
        key = tuple(integer(value[f], 0 if f == "ShooterSlot" else 1,
                    7 if f == "ShooterSlot" else 2**64-1 if f == "AuthorityEpoch"
                    else 2**32-1 if f == "ShotId" else 65535)
                    for f in ("AuthorityEpoch", "MatchId", "ShooterSlot", "Generation", "LifeId", "ShotId"))
        return key
    receipts = []
    for receipt in receipt_rows:
        if (receipt.get("Stage") != "terminal-delivered" or receipt.get("ClaimId") != ack["ClaimId"]
                or tuple(receipt.get(f) for f in ("Victim", "VictimGeneration", "VictimLife")) != identity[2:]):
            continue
        key = shot_key(receipt["Key"])
        if key[:2] != identity[:2] or key[2] != local_slot or key[5] != ack["ShotId"]:
            continue
        receipts.append((receipt, key))
    # A repeated or competing receipt is not an unambiguous historical witness.
    if len(receipts) != 1: return None
    receipt, key = receipts[0]
    if (any(type(receipt.get(f)) is not int for f in ("ClaimId", "Victim", "VictimGeneration", "VictimLife", "DamageEventId", "Value", "Result"))
            or receipt.get("DamageEventId") != ack["DamageSequence"] or receipt.get("Value") != ack["DamageApplied"]
            or receipt.get("Result") != ack["Result"]
            or not (integer(receipt["LocalFrame"]) == row["ObservedFrame"]
                    or newer(row["ObservedFrame"], receipt["LocalFrame"]))):
        return None
    # The raw ACK carries claim/shot and recipient, but no shooter launch-life.
    # Establish uniqueness before filtering source keys, so an unkeyed or
    # competing-generation/life copy cannot silently disappear from the proof.
    candidates = [t for t in terminals if t["Outcome"].get("ClaimId") == ack["ClaimId"]
                  and ((t.get("Key") is not None and shot_key(t["Key"]) == key)
                       or (t.get("ShooterSlot") == key[2] and t["Outcome"].get("ShotId") == ack["ShotId"]))]
    if len(candidates) != 1: return None
    terminal = candidates[0]
    if terminal.get("Key") is None or shot_key(terminal["Key"]) != key: return None
    outcome = terminal.get("Outcome", {})
    if (type(terminal.get("ShooterSlot")) is not int or terminal["ShooterSlot"] != key[2]
            or not newer(integer(terminal["ObservedFrame"]), source["frame"])
            or any(type(outcome.get(f)) is not int or outcome[f] != ack[f] for f in fields)):
        return None
    return dict(ordinal=integer(terminal["Ordinal"], 1, 2**64-1), authorityFrame=terminal["ObservedFrame"],
                receiptFrame=receipt["LocalFrame"], shotKey=key, outcome=ack)


def audit(server, peers, summary, manifest, owner_slot=1):
    failures, missing = [], []
    metrics = {"peers": {}, "authoritySamples": 0, "independentAnchorWitnesses": 0,
               "healthDisagreements": [], "ownerPhaseDifferences": 0, "newerHealthJoins": 0, "predictionDebitJoins": 0, "inactiveAnchorNotLocationProofSamples": 0, "lethalAckFloorJoins": [], "terminalAckBodyJoins": []}
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
        membership, local_seen, spectating = {}, set(), {}
        try:
            for window in peer.get("Windows", []):
                context = (integer(window["Epoch"], 1, 2**64-1), integer(window["Match"], 1, 65535))
                slot = integer(window["LocalSlot"], 0, 7)
                if context in membership and membership[context] != slot:
                    raise ValueError("conflicting local-slot membership")
                membership[context] = slot
                spectating[context] = spectating.get(context, True) and window.get("Spectating", False) is True
            if peer.get("Overflow", 0): failures.append(name + ": evidence overflow")
            for row in peer["Forms"]:
                key, raw, form = decode(row)
                full = (key, raw)
                context = key[:2]
                if context not in membership: raise ValueError("local-slot membership evidence missing")
                local = key[2] == membership[context]
                # Bootstrap constructs every player through the remote path,
                # including the eventual local player. Its first self sample
                # therefore uses observer checks, never the owner phase waiver.
                bootstrap_self = local and context not in local_seen and row["Stage"] == "observer"
                role_fields = ("LocalSlot", "Spectating", "AuthoritySpectating")
                role_present = [field in row and row[field] is not None for field in role_fields]
                new_role = any(field in row for field in role_fields)
                if new_role:
                    if not all(role_present): raise ValueError("partial sample-time role metadata")
                    if integer(row["LocalSlot"], 0, 7) != membership[context]:
                        raise ValueError("sample-time local-slot membership mismatch")
                    if type(row["Spectating"]) is not bool or type(row["AuthoritySpectating"]) is not bool:
                        raise ValueError("invalid sample-time spectator boolean")
                expected_stage = "owner" if local and (new_role or not spectating[context]) else "observer"
                owner_phase_allowed = row["Stage"] == "owner" and local and (not new_role
                    or not row["Spectating"] and not row["AuthoritySpectating"])
                if row["Stage"] != expected_stage and not bootstrap_self:
                    raise ValueError("applied owner/observer role mismatch")
                if local: local_seen.add(context)
                if full in seen: raise ValueError("duplicate applied source")
                seen.add(full)
                if key in last and not newer(form["frame"], last[key]["frame"]): raise ValueError("stale applied source stream")
                if key in last and form["started"] != last[key]["started"] and not newer(form["started"], last[key]["started"]): raise ValueError("stale transition stream")
                last[key] = form
                source = authority.get(full)
                if source is None: raise ValueError("full identity/source absent from authority")
                matched += 1
                observed_anchor = vector(row, ("X", "Y", "Z"))
                if form["active"] and row["TurretActive"]:
                    if observed_anchor != form["anchor"]: raise ValueError("observed active turret anchor mismatch")
                else:
                    metrics["inactiveAnchorNotLocationProofSamples"] += 1
                if not owner_phase_allowed and row["Phase"] != form["phase"]: raise ValueError("observer phase mismatch")
                if owner_phase_allowed and row["Phase"] != form["phase"]: metrics["ownerPhaseDifferences"] += 1
                # Only Weavel installs coupled form BodyHealth. Other hunters
                # retain the fast health lane, checked by the base adjudicator.
                debit = row.get("BodyPredictionDebit")
                if debit is not None:
                    integer(debit, high=2**31-1)
                    if local and debit: raise ValueError("local body prediction debit must be zero")
                def actual_health(basis):
                    body_hp = basis["body_hp"]
                    if key[2] != owner_slot: body_hp = row["BodyHp"]
                    elif debit is not None and body_hp > 0: body_hp = max(1, body_hp-debit)
                    return body_hp, basis["hp"], basis["active"]
                actual = row["BodyHp"], row["TurretHp"], row["TurretActive"]
                if actual != actual_health(form):
                    observed_damage = row.get("ObservedDamageEvent")
                    detail = dict(peer=name, identity=key, sourceFrame=form["frame"], sourceDamage=form["damage"],
                                  observedDamage=observed_damage, bodyHp=row["BodyHp"], turretHp=row["TurretHp"],
                                  active=row["TurretActive"], predictionDebit=debit)
                    metrics["healthDisagreements"].append(detail)
                    only_body = actual[1:] == (form["hp"], form["active"])
                    if observed_damage is None:
                        missing.append(name + ": health/presence differs; observed damage revision unavailable")
                    elif not newer(integer(observed_damage, high=65535), form["damage"], 16):
                        if key[2] == owner_slot and debit is None and only_body:
                            missing.append(name + ": body health difference lacks pending prediction debit evidence")
                        else: raise ValueError("health/presence mismatch at same damage revision")
                    else:
                        bases = [other for candidate, other in by_life.get(key, [])
                                 if other["started"] == form["started"] and other["damage"] == observed_damage]
                        terminal_join = (terminal_ack_body_join(server, peer, row, key, form, membership[context])
                                         if not bases and key[2] == owner_slot and not local and only_body else None)
                        if any(actual_health(other) == actual for other in bases):
                            metrics["newerHealthJoins"] += 1
                            if debit: metrics["predictionDebitJoins"] += 1
                        elif terminal_join is not None:
                            metrics["terminalAckBodyJoins"].append(dict(peer=name, identity=key,
                                sourceFrame=form["frame"], sourceDamage=form["damage"], proof=terminal_join))
                        elif key[2] == owner_slot and bases and not local and row["BodyHp"] == 1 and form["body_hp"] > 0:
                            floor = lethal_ack_floor(row, key, form, bases)
                            if floor is True:
                                metrics["lethalAckFloorJoins"].append(dict(peer=name,identity=key,sourceFrame=form["frame"],
                                    sourceDamage=form["damage"],ackDamage=observed_damage,bodyAckSource=row["BodyAckSource"]))
                            elif floor is None:
                                missing.append(name + ": lethal ACK floor lacks retained exact receipt evidence")
                            else: raise ValueError("lethal ACK floor lacks matching authority death outcome")
                        elif not bases:
                            missing.append(name + ": newer health/presence lacks exact authority reconciliation")
                        elif debit is None and key[2] == owner_slot and any(actual[1:] == (other["hp"], other["active"]) for other in bases):
                            missing.append(name + ": newer body health difference lacks pending prediction debit evidence")
                        else:
                            raise ValueError("health/presence/debit mismatch against newer authority revision")
                elif key[2] == owner_slot and debit:
                    metrics["predictionDebitJoins"] += 1
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
                cycle_owner = observed["Stage"] == "owner" and (observed.get("LocalSlot") is None
                    or observed["Slot"] == observed["LocalSlot"] and not observed["Spectating"] and not observed["AuthoritySpectating"])
                cycle_phase = observed["Phase"] if cycle_owner else form["phase"]
                if cycle_phase == ("Morphing", "Alt", "Unmorphing", "Biped")[step]:
                    step += 1
                    if step == 4: complete_cycle = True; step = 0
                progress[key] = step
            if not complete_cycle: missing.append(name + ": ordered same-life morph/alt/unmorph/settled cycle incomplete")
            if not active: missing.append(name + ": active Weavel turret not observed")
            if not separated: missing.append(name + ": independent turret/body anchor evidence missing")
            late_initial = target_rows[0][1] if target_rows else None
            late_coverage = "NOT_RUN"
            if name == "peer-2" and settings.get("observerDelay", settings.get("observer_delay", 0)):
                late_coverage = "ACTIVE" if late_initial and late_initial["active"] else "INACTIVE" if late_initial else "MISSING"
            metrics["peers"][name] = dict(sourceJoins=matched, targetPhases=sorted(phases), completeCycle=complete_cycle, activeSamples=active,
                                          independentAnchorWitnesses=separated,
                                          initialTarget=late_initial, lateInitialAnchorCoverage=late_coverage)
            metrics["independentAnchorWitnesses"] += separated
        except (KeyError, TypeError, ValueError, struct.error) as error:
            failures.append(name + ": form evidence: " + str(error))
    if not authority: missing.append("nonempty authority form sources required")
    return dict(status="FAIL" if failures else "INCOMPLETE" if missing else "PASS", failures=failures,
                incomplete=list(dict.fromkeys(missing)), metrics=metrics,
                scope="exact accepted form-source identity, observer phase and turret anchor/presence/health; owner animation phase may lead; exact pending remote body debit may explain predicted health; inactive turret anchors are not location proof; exact lethal ACK may hold body1 until a death snapshot; otherwise a newer remote body-only ACK with zero prediction debit requires matching complete authority terminal23 fields and this client's exact full-key receipt; initial late-observer active/inactive form is reported separately (active bootstrap tested by asset-backed form-scene fixture); existing base health/score/drain gates retained")


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
