"""Offline, fail-closed adjudication of N0 process evidence (no game imports)."""
from collections import Counter
import json
import math
from pathlib import Path
import re
import struct

FATAL = re.compile(r"semantic history overrun|reliable (?:channel |control )?(?:failed|gap)|"
                   r"timed out|\btimeout\b|\[sim\] step failed|could not (?:join|load|spectate)|"
                   r"connection lost|no word from the server|re-announcing|"
                   r"invalid resurrection=[1-9]\d*|\bcrashed:|unhandled exception|RESULT: FAIL", re.I)


def shot_key(row):
    key = row["Key"]
    return tuple(key[k] for k in ("AuthorityEpoch", "MatchId", "ShooterSlot", "Generation", "LifeId", "ShotId"))


def continuous_terminal_rejections(report, authority_accepted):
    """Only exact admitted Shock Coil ticks with explicit owner death/life proof.

    The raw accepted event supplies the immutable source signature. A reason
    string alone never excludes a shot from commitment coverage.
    """
    valid, errors = {}, []
    committed = {shot_key(row) for row in report["Shots"] if row["Stage"] == "committed"}
    locally_accepted = {shot_key(row): row for row in report["Shots"] if row["Stage"] == "accepted"}
    fields = ("SourceFrame", "ContinuousPhase", "Weapon", "Charge", "Kind")
    reasons = ("continuous owner dead", "continuous owner life retired")
    for row in report["Shots"]:
        if row["Stage"] != "rejected" or row.get("Reason") not in reasons:
            continue
        try:
            key = shot_key(row)
            signature = tuple(row[field] for field in fields)
            accepted = authority_accepted.get(key)
            health, life = row["OwnerHealth"], row["CurrentOwnerLife"]
            if any(type(value) is not int for value in (*key, *signature, health, life)):
                raise ValueError("non-integer identity/source/death evidence")
            if (not all(key[i] > 0 for i in (0, 1, 3, 4, 5)) or not 0 <= key[2] < 8
                or signature[0] <= 0 or signature[1] != signature[0]
                or signature[2] != 7 or signature[4] != 3 or not 0 <= signature[3] <= 255
                or not 0 <= health <= 65535 or not 0 < life <= 65535):
                raise ValueError("invalid continuous identity/source/death evidence")
            if accepted is None or signature != tuple(accepted[field] for field in fields):
                raise ValueError("rejection does not name its exact accepted fire event")
            local = locally_accepted.get(key)
            if local is None or signature != tuple(local[field] for field in fields):
                raise ValueError("rejection lacks its own exact accepted fire event")
            if row["Reason"] == reasons[0] and (health != 0 or life != key[4]):
                raise ValueError("owner-dead receipt lacks same-life zero health")
            if row["Reason"] == reasons[1] and life == key[4]:
                raise ValueError("owner-life-retired receipt still names current life")
            if key in committed:
                raise ValueError("continuous tick is both committed and rejected in one view")
            if key in valid and row != valid[key]:
                raise ValueError("conflicting continuous terminal rejection receipts")
            valid[key] = row
        except (KeyError, TypeError, ValueError) as error:
            errors.append("invalid continuous terminal rejection: " + str(error))
    return valid, errors


TURRET_RETIRED_REASON = "turret source retired before native emission"
TURRET_UNAVAILABLE_REASON = "turret unavailable throughout native recovery window"


def authority_turret_retirements(report, authority_accepted):
    """Authority-only native source retirement, separate from emission coverage."""
    valid, errors = {}, []
    fields = ("SourceFrame", "ContinuousPhase", "Weapon", "Charge", "Kind")
    committed = {shot_key(row) for row in report["Shots"] if row["Stage"] == "committed"}
    for row in report["Shots"]:
        if row["Stage"] != "rejected" or row.get("Reason") not in (TURRET_RETIRED_REASON,TURRET_UNAVAILABLE_REASON): continue
        try:
            if report.get("Role") != "server": raise ValueError("turret retirement is authority-only")
            key = shot_key(row); signature = tuple(row[f] for f in fields)
            life, hp, observed = row["CurrentOwnerLife"], row["OwnerHealth"], row["ObservedFrame"]
            if any(type(v) is not int for v in (*key, *signature, life, hp, observed)):
                raise ValueError("non-integer identity/source/retirement evidence")
            if not (0 < key[0] < 2**64 and 0 < key[1] <= 65535 and 0 <= key[2] < 8
                    and all(0 < key[i] <= 65535 for i in (3,4)) and 0 < key[5] < 2**32
                    and 0 < signature[0] < 2**32 and 0 <= observed < 2**32 and 0 <= signature[1] < 2**32
                    and signature[2] == 3 and 0 <= signature[3] <= 255 and signature[4] == 4
                    and 0 < life <= 65535 and 0 <= hp <= 65535):
                raise ValueError("invalid turret source identity/signature")
            accepted = authority_accepted.get(key)
            if accepted is None or signature != tuple(accepted[f] for f in fields):
                raise ValueError("retirement does not name exact authority accepted event")
            source = row["RetiredFormSource"]
            if not isinstance(source,str) or not re.fullmatch(r"[0-9a-fA-F]{56}",source):
                raise ValueError("missing exact retired Form28")
            raw = bytes.fromhex(source)
            frame, started, phase = struct.unpack_from("<IIB",raw)
            anchor = struct.unpack_from("<3f",raw,9)
            active, turret_hp, damage, body_hp = struct.unpack_from("<BHHH",raw,21)
            if frame != observed or started > frame or phase not in (1,2,3,4) or active > 1 or any(not math.isfinite(v) or abs(v)>100000 for v in anchor):
                raise ValueError("invalid retired form clock/state")
            unavailable_at_expiry = row.get("Reason") == TURRET_UNAVAILABLE_REASON
            if unavailable_at_expiry:
                accepted_at = row["AcceptedObservedFrame"]
                if (type(accepted_at) is not int or not 0 <= accepted_at < 2**32
                        or accepted_at != accepted.get("ObservedFrame")
                        or type(row["TurretRecoveryFrames"]) is not int or row["TurretRecoveryFrames"] != 64
                        or row["TurretEverAvailable"] is not False
                        or not 64 <= (observed-accepted_at) % 2**32 < 2**31
                        or life != key[4] or phase != 3 or active != 0 or turret_hp != 0):
                    raise ValueError("unavailable turret lacks exact full authority recovery-window proof")
                accepted_source = row["AcceptedFormSource"]
                if not isinstance(accepted_source,str) or not re.fullmatch(r"[0-9a-fA-F]{56}",accepted_source):
                    raise ValueError("missing exact accepted Form28")
                if (type(accepted.get("AcceptedObservedFrame")) is not int
                        or accepted["AcceptedObservedFrame"] != accepted_at
                        or not isinstance(accepted.get("AcceptedFormSource"),str)
                        or accepted["AcceptedFormSource"].upper() != accepted_source.upper()
                        or accepted.get("TurretEverAvailable") is not False
                        or type(accepted.get("TurretRecoveryFrames")) is not int
                        or accepted["TurretRecoveryFrames"] != 64):
                    raise ValueError("accepted event lacks matching unavailable recovery evidence")
                accepted_raw = bytes.fromhex(accepted_source)
                aframe, astarted, aphase = struct.unpack_from("<IIB",accepted_raw)
                aanchor = struct.unpack_from("<3f",accepted_raw,9)
                aactive, ahp, adamage, abody = struct.unpack_from("<BHHH",accepted_raw,21)
                if (aframe != accepted_at or astarted > aframe or aphase not in (1,2,3,4)
                        or aactive != 0 or ahp != 0
                        or any(not math.isfinite(v) or abs(v)>100000 for v in aanchor)):
                    raise ValueError("accepted turret was available or form clock/state is invalid")
                accepted_forms = [form for form in report.get("Forms",[]) if form.get("Stage") == "authority"
                        and tuple(form.get(f) for f in ("Epoch","Match","Slot","Generation","Life")) == key[:5]
                        and form.get("ObservedFrame") == accepted_at and isinstance(form.get("Source"),str)
                        and form["Source"].upper() == accepted_source.upper()]
                if not accepted_forms or any((form.get("BodyHp"),form.get("TurretHp"),form.get("TurretActive"),form.get("Phase"))
                        != (abody,ahp,False,{1:"Biped",2:"Morphing",3:"Alt",4:"Unmorphing"}[aphase]) for form in accepted_forms):
                    raise ValueError("accepted form lacks exact authority identity/clock/state join")
                for form in report.get("Forms",[]):
                    if (form.get("Stage") != "authority"
                            or tuple(form.get(f) for f in ("Epoch","Match","Slot","Generation","Life")) != key[:5]):
                        continue
                    ftime = form.get("ObservedFrame")
                    if type(ftime) is not int or not 0 <= ftime < 2**32:
                        raise ValueError("unavailable recovery has malformed authority form clock")
                    if (ftime-accepted_at) % 2**32 > (observed-accepted_at) % 2**32:
                        continue
                    fsource = form.get("Source")
                    if not isinstance(fsource,str) or not re.fullmatch(r"[0-9a-fA-F]{56}",fsource):
                        raise ValueError("unavailable recovery has malformed sampled authority form")
                    fraw = bytes.fromhex(fsource)
                    if fraw[21] != 0 or struct.unpack_from("<H",fraw,22)[0] != 0:
                        raise ValueError("authority form proves turret available during recovery")
            if body_hp != hp or (life == key[4] and phase in (2,3) and hp != 0
                                and not unavailable_at_expiry):
                raise ValueError("native turret source remains eligible")
            identity = key[:4] + (life,)
            matches = [form for form in report.get("Forms",[]) if form.get("Stage") == "authority"
                       and tuple(form.get(f) for f in ("Epoch","Match","Slot","Generation","Life")) == identity
                       and form.get("ObservedFrame") == observed and isinstance(form.get("Source"),str)
                       and form["Source"].upper() == source.upper()]
            if not matches or any((form.get("BodyHp"),form.get("TurretHp"),form.get("TurretActive"),form.get("Phase"))
                    != (body_hp,turret_hp,bool(active),{1:"Biped",2:"Morphing",3:"Alt",4:"Unmorphing"}[phase]) for form in matches):
                raise ValueError("retired form lacks exact authority identity/clock/state join")
            if life != key[4] and not 0 < (life-key[4]) % 65536 < 32768:
                raise ValueError("owner replacement life is not newer than accepted source life")
            if key in committed: raise ValueError("turret key both emitted and retired")
            ledger = report["CombatPayments"]
            if type(ledger["EvidenceOverflow"]) is not int or ledger["EvidenceOverflow"] != 0:
                raise ValueError("payment-key evidence overflow")
            if not isinstance(ledger["PaidShotKeys"],list): raise ValueError("missing payment-key evidence")
            paid = []
            for paid_key in ledger["PaidShotKeys"]:
                value = shot_key({"Key":paid_key})
                if any(type(v) is not int for v in value) or not (0 < value[0] < 2**64 and 0 < value[1] <= 65535
                        and 0 <= value[2] < 8 and all(0 < value[i] <= 65535 for i in (3,4)) and 0 < value[5] < 2**32):
                    raise ValueError("malformed authority paid shot identity")
                paid.append(value)
            if len(paid) != len(set(paid)) or len(paid)>16384: raise ValueError("invalid bounded payment-key evidence")
            if key in paid: raise ValueError("retired turret has authority payment")
            if key in valid and row != valid[key]: raise ValueError("conflicting turret retirement receipts")
            valid[key] = row
        except (KeyError, TypeError, ValueError,struct.error) as error:
            errors.append("invalid authority turret retirement: " + str(error))
    return valid, errors


def state_key(row):
    return tuple(row[k] for k in ("Epoch", "Match", "Frame", "Slot", "Generation", "Life"))


def health_value(row, schema=1):
    fields = ("Hp", "TurretHp", "DamageEvent") if schema == 2 else (
        "Hp", "TurretHp", "Kills", "Deaths", "DamageEvent")
    return tuple(row[k] for k in fields)


def slow_states(report, authority=False):
    """Validate exact PlayerSlowState payloads; its revision has its own clock."""
    # Protocols 46–48 append the continuous endpoint sidecar; protocol 49
    # adds observed form/turret state. Scores remain in the first 10 bytes.
    protocol = report.get("Protocol", 0)
    stride = 58 if protocol >= 49 else 28 if protocol >= 46 else 10
    rows = report.get("SlowStates")
    if not isinstance(rows, list) or not rows:
        raise ValueError("missing/nonempty slow-lane evidence")
    result, seen = [], set()
    for row in rows:
        limits = {"Epoch": (1, 2**64 - 1), "Match": (1, 65535),
                  "Revision": (1, 2**32 - 1), "Frame": (0, 2**32 - 1),
                  "Context": (0, 2**31 - 1)}
        for field, (low, high) in limits.items():
            if type(row[field]) is not int or not low <= row[field] <= high:
                raise ValueError("invalid slow-lane " + field)
        if authority and row["Context"] != 0:
            raise ValueError("authority slow-lane Context must be zero")
        payload = row["Payload"]
        if not isinstance(payload, str) or not re.fullmatch(r"(?:[0-9A-Fa-f]{2})+", payload):
            raise ValueError("invalid slow-lane payload hex")
        data = bytes.fromhex(payload)
        key = row["Epoch"], row["Match"], row["Revision"]
        observation = (*key, row["Context"])
        if len(data) < 15 or struct.unpack_from("<HQI", data) != (key[1], key[0], key[2]):
            raise ValueError("slow-lane row/payload identity mismatch")
        count = data[14]
        if count > 8 or len(data) != 15 + count * stride + 64 or observation in seen:
            raise ValueError("invalid slow-lane length/count/duplicate revision/context")
        seen.add(observation)
        scores = {}
        for at in range(15, 15 + count * stride, stride):
            slot, generation = data[at], struct.unpack_from("<H", data, at + 1)[0]
            if slot >= 8 or generation == 0 or any(s == slot for s, _ in scores):
                raise ValueError("invalid slow-lane slot/generation")
            scores[slot, generation] = data[at + 3:at + 10]
        if any(not math.isfinite(value) or value < -1
               for value in struct.unpack_from("<16f", data, 15 + count * stride)):
            raise ValueError("invalid slow-lane time state")
        result.append(dict(key=key, context=row["Context"], frame=row["Frame"], payload=data, scores=scores))
    return result


def slow_lane_parity(authority, peer):
    """No fast-frame pairing of cached scores; require exact lane receipts and final scores."""
    errors = []
    try:
        server, client = slow_states(authority, authority=True), slow_states(peer)
    except (KeyError, TypeError, ValueError, struct.error) as error:
        return {"slowSamplesCompared": 0, "slowLaneCompleteness": "FAIL",
                "finalScoreConvergence": "FAIL"}, ["malformed/missing slow-lane evidence: " + str(error)]
    indexed = {row["key"]: row for row in server}
    matched = 0
    for row in client:
        reference = indexed.get(row["key"])
        if reference is None:
            errors.append("unknown authority slow-lane revision " + str(row["key"]))
        elif reference["payload"] != row["payload"]:
            errors.append("slow-lane full payload mismatch " + str(row["key"]))
        else:
            matched += 1
    lane_ok = not errors
    known_fast = {state_key(row) for row in authority["States"]}
    streams = {(row["Epoch"], row["Match"]) for row in peer["States"]}
    for stream in streams:
        fast = [row for row in peer["States"] if (row["Epoch"], row["Match"]) == stream
                and state_key(row) in known_fast]
        if not fast:
            errors.append("no matched terminal fast state for slow-lane stream " + str(stream))
            continue
        end = max(row["Frame"] for row in fast)
        receipts = [row for row in client if row["key"][:2] == stream]
        latest = receipts[-1] if receipts else None
        emitted = indexed.get(latest["key"]) if latest else None
        # A validated slow receipt can describe membership after the final fast
        # sample (for example a graceful disconnect). Join its authority emission
        # clock, never its client-local receive clock. Older receipts cannot move
        # the terminal comparison backwards and hide a missed score update.
        anchor = max(end, emitted["frame"]) if emitted and emitted["payload"] == latest["payload"] else end
        references = [row for row in server if row["key"][:2] == stream and row["frame"] <= anchor]
        if not references or not receipts:
            errors.append("missing terminal slow-lane stream " + str(stream))
            continue
        # Receiver reset/bootstrap assignments can roll revision backwards in a new
        # context. The last recorded cache assignment, not the highest revision,
        # determines the terminal scores actually available to that client.
        reference = max(enumerate(references), key=lambda pair: (pair[1]["frame"], pair[0]))[1]
        active = (set(reference["scores"]) if anchor > end else
                  {(row["Slot"], row["Generation"]) for row in peer["States"]
                   if (row["Epoch"], row["Match"]) == stream and row["Frame"] == end})
        if any(identity not in reference["scores"] or identity not in latest["scores"]
               or reference["scores"][identity] != latest["scores"][identity] for identity in active):
            errors.append("stale/missing terminal slow-lane scores " + str(stream) + " at " + str(end))
    return {"slowSamplesCompared": matched, "slowLaneCompleteness": "PASS" if lane_ok else "FAIL",
            "finalScoreConvergence": "PASS" if not errors else "FAIL"}, errors


def drain_barrier(server, peers, require_empty=True):
    """Check gameplay quiescence; preparation/release additionally require empty queues."""
    try:
        identity = server["Epoch"], server["Match"]
        frontier = server["SemanticLatest"], server["AwardLatest"]
        if not server["Enabled"] or server["Role"] != "server" or not all(identity):
            return None, "authority drain status unavailable"
        if server["Error"] or server["ConnectionLost"]:
            return None, "authority drain error"
        # Older immutable campaigns did not expose this field. New authority
        # status must account for native respawns that can advance semantics
        # after projectile/claim queues are already quiet.
        if "PendingRespawns" in server:
            pending = server["PendingRespawns"]
            if type(pending) is not int or pending < 0:
                return None, "malformed authority pending respawns"
            if pending:
                return None, "authority native respawns remain"
        if any(server[key] for key in ("ActiveProjectiles", "ActiveBombs", "ClaimsPending")):
            return None, "authority flights/bombs/claims remain"
        bomb_frontier = server.get("BombFrontier")
        if bomb_frontier is not None and any(server[key] for key in ("BombPendingRequests", "BombPendingEvents", "BombFaulted")):
            return None, "authority bomb lane not settled"
        reliable = server["Reliable"]
        if not reliable or len(reliable) != len(peers) or any(p["Failed"] or p["Pending"] < 0 for p in reliable):
            return None, "authority reliable endpoints not healthy"
        if require_empty and any(p["Pending"] for p in reliable):
            return None, "authority reliable endpoints not empty/healthy"
        offered = set()
        for peer in peers:
            if not peer["Enabled"] or peer["Role"] != "client" or not peer["Quiescing"]:
                return None, "scripted actions still active"
            if peer["Error"] or peer["ConnectionLost"] or (peer["Epoch"], peer["Match"]) != identity:
                return None, "peer error or authority identity differs"
            if peer["ActiveProjectiles"] or peer["ActiveBombs"]:
                return None, "peer flights/bombs may still generate a final claim"
            if peer["ClaimsPending"]:
                return None, "peer claims remain pending"
            if bomb_frontier is not None and (peer["BombFrontier"] != bomb_frontier
                    or any(peer[key] for key in ("BombPendingRequests", "BombPendingEvents", "BombFaulted"))):
                return None, "peer bomb lane has not caught authority frontier"
            peer_reliable = peer["Reliable"]
            if not peer_reliable or len(peer_reliable) != 1 or any(p["Failed"] or p["Pending"] < 0 for p in peer_reliable):
                return None, "peer reliable endpoint not healthy"
            if require_empty and any(p["Pending"] for p in peer_reliable):
                return None, "peer reliable endpoint not empty"
            if (peer["SemanticLatest"], peer["SemanticPresented"]) != (frontier[0], frontier[0]) or (
                    peer["AwardLatest"], peer["AwardPresented"]) != (frontier[1], frontier[1]):
                return None, "peer presentation has not caught authority frontier"
            offered.update(peer["OfferedShotKeys"])
        if not offered <= set(server["AcceptedShotKeys"]):
            return None, "final offered shot has not reached authority"
        grace = server["ClaimGraceSeconds"]
        if not isinstance(grace, (float, int)) or grace < 2.4:
            return None, "authority claim grace unavailable"
        release = {"Status": "PASS", "Epoch": identity[0], "Match": identity[1],
                   "Semantic": frontier[0], "Award": frontier[1]}
        if bomb_frontier is not None:
            release["Bomb"] = bomb_frontier
        return release, None
    except (KeyError, TypeError, ValueError):
        return None, "missing/malformed drain status"


class DrainStability:
    """Grace follows actual gameplay frontiers, independent of periodic reliable state traffic."""
    def __init__(self):
        self.fingerprint = None
        self.started = None

    def observe(self, server, peers, now):
        candidate, reason = drain_barrier(server, list(peers.values()), require_empty=False)
        fingerprint = (tuple(candidate.values()), tuple((name, tuple(row["OfferedShotKeys"]))
                        for name, row in sorted(peers.items()))) if candidate else None
        if fingerprint != self.fingerprint or not candidate:
            self.fingerprint = fingerprint
            self.started = now if candidate else None
        return candidate, reason, now - self.started if self.started is not None else 0


def drain_prepared(server, release):
    """Authority must record the exact frontier before any peer receives its exit token."""
    try:
        if "PendingRespawns" in server and (type(server["PendingRespawns"]) is not int
                or server["PendingRespawns"] != 0):
            return False, "authority native respawns remain during release preparation"
        at = server["AtRelease"]
        if server["Release"] != release or release["Status"] != "PASS":
            return False, "waiting for authority preparation acknowledgement"
        identity = release["Epoch"], release["Match"]
        expected = release["Semantic"], release["Semantic"], release["Award"], release["Award"]
        for row in (server, at):
            if "Bomb" in release and row["BombFrontier"] != release["Bomb"]:
                return False, "authority bomb frontier changed during release preparation"
            if (row["Epoch"], row["Match"]) != identity or tuple(row[key] for key in (
                    "SemanticLatest", "SemanticPresented", "AwardLatest", "AwardPresented")) != expected:
                return False, "authority frontier changed during release preparation"
        return True, None
    except (KeyError, TypeError):
        return False, "missing authority preparation acknowledgement"


def drain_coverage(manifest, reports):
    """Check saved barrier and full frozen interval, without changing N0 tail guards."""
    if not manifest.get("settings", {}).get("drain_seconds", 0):
        return "NOT_RUN", []
    gaps = []
    try:
        if manifest["settings"].get("soak"):
            raise ValueError("soak drain unsupported")
        drain = manifest["drain"]
        if drain["status"] != "PASS":
            raise ValueError("bounded barrier did not pass: " + drain.get("reason", "unknown"))
        release = drain["release"]
        peers = drain["peers"]
        if len(peers) != manifest["settings"]["players"]:
            raise ValueError("missing independent peer statuses")
        candidate, reason = drain_barrier(drain["server"], list(peers.values()))
        if candidate != release:
            raise ValueError(reason or "saved release differs from observed barrier")
        if "prepareServer" in drain:
            candidate, reason = drain_barrier(drain["prepareServer"], list(drain["preparePeers"].values()))
            if candidate != release:
                raise ValueError(reason or "preparation did not observe the frozen empty-queue barrier")
            if drain["prepareStableSeconds"] < drain["prepareServer"]["ClaimGraceSeconds"]:
                raise ValueError("preparation preceded the full gameplay claim grace")
        prepared, reason = drain_prepared(drain["server"], release)
        if not prepared:
            raise ValueError(reason)
        if drain["stableSeconds"] < drain["server"]["ClaimGraceSeconds"]:
            raise ValueError("barrier did not retain the maximum claim grace")
        identity = release["Epoch"], release["Match"]
        authority = reports["server"]
        for name in ("server", *peers):
            exported = reports[name]["Drain"]
            if not exported["Enabled"] or exported["Error"] or exported["Release"] != release:
                gaps.append(f"{name}: missing/error/different exported release token")
                continue
            at = exported["AtRelease"]
            if (at["Epoch"], at["Match"]) != identity or (
                    at["SemanticLatest"], at["SemanticPresented"], at["AwardLatest"], at["AwardPresented"]) != (
                    release["Semantic"], release["Semantic"], release["Award"], release["Award"]):
                gaps.append(f"{name}: frontier changed or was not presented at release")
            if name == "server":
                continue
            if not exported["Quiescing"]:
                gaps.append(f"{name}: exported script was not quiesced")
            report = reports[name]
            baselines = [f for f in report["Facts"] if f["Baseline"] and (f["Epoch"], f["Match"]) == identity]
            if len(baselines) != 1:
                gaps.append(f"{name}: missing/ambiguous frozen-match baseline")
                continue
            baseline = baselines[0]
            expected = {(f["Award"], f["Id"]): f["Payload"] for f in authority["Facts"]
                        if not f["Baseline"] and (f["Epoch"], f["Match"]) == identity
                        and (baseline["AwardFrontier"] if f["Award"] else baseline["Id"]) < f["Id"]
                        <= (release["Award"] if f["Award"] else release["Semantic"])}
            received = {(f["Award"], f["Id"]): f["Payload"] for f in report["Facts"]
                        if not f["Baseline"] and (f["Epoch"], f["Match"]) == identity}
            for award, start, end in ((False, baseline["Id"], release["Semantic"]),
                                      (True, baseline["AwardFrontier"], release["Award"])):
                ids = sorted(key[1] for key in expected if key[0] == award)
                if start > end or len(ids) != end - start or any(value != start + index + 1 for index, value in enumerate(ids)):
                    gaps.append(f"{name}: authority frozen {'award' if award else 'semantic'} interval incomplete")
            missing = expected.keys() - received.keys()
            conflicts = sum(payload != received[key] for key, payload in expected.items() if key in received)
            if missing or conflicts:
                gaps.append(f"{name}: frozen semantic interval {len(missing)} missing, {conflicts} different")
    except (KeyError, TypeError, ValueError) as error:
        gaps.append("missing/malformed drain proof: " + str(error))
    return ("FAIL" if gaps else "PASS"), gaps


def rotation_coverage(reports, processes, settings):
    """A room label is not proof that a new match simulated on every peer."""
    authority = reports.get("server", {})
    server_windows = authority.get("Windows", [])
    identities = {(w.get("Epoch"), w.get("Match")) for w in server_windows
                  if w.get("Epoch") and w.get("Match")}
    room_labels = {w["Room"] for r in reports.values() for w in r["Windows"] if w.get("Room")}
    requested_rooms = {room.strip() for room in str(settings.get("rooms", "")).split(",") if room.strip()}
    attempted = len(identities) > 1 or len(room_labels) > 1 or len(requested_rooms) > 1
    if not attempted:
        return "NOT_RUN", []
    gaps = []
    if len(identities) < 2:
        return "INCOMPLETE", ["server: fewer than two actual authority matches"]
    states = {state_key(s) for s in authority.get("States", [])}
    for identity in sorted(identities):
        frames = [w.get("Frame", 0) for w in server_windows if (w.get("Epoch"), w.get("Match")) == identity]
        segments = [s for s in authority.get("ServerSegments", [])
                    if (s.get("AuthorityEpoch"), s.get("CurrentMatchId")) == identity]
        if max(frames) - min(frames) < 60 or not any(s.get("Frames", 0) >= 60 for s in segments):
            gaps.append(f"server: match {identity} lacks 60 frames of observed simulation")
    shared_transition = False
    records = {p["name"]: p for p in processes}
    for name, report in reports.items():
        if name == "server":
            continue
        seen = {(w.get("Epoch"), w.get("Match")) for w in report["Windows"]
                if w.get("Epoch") and w.get("Match")}
        shared_transition |= len(seen & identities) >= 2
        record = records[name]
        # Full-duration initial peers must survive the complete rotation. A late
        # join or deliberately short departure need only prove the matches it saw.
        full_duration = (settings.get("seconds", 0) > 0
                         and record.get("requestedSeconds", 0) >= settings["seconds"] - 2)
        required = identities if full_duration else seen & identities
        if not required:
            gaps.append(f"{name}: no observed authority match during rotation")
        for identity in sorted(required):
            frames = [w.get("Frame", 0) for w in report["Windows"]
                      if (w.get("Epoch"), w.get("Match")) == identity]
            common_frames = {s["Frame"] for s in report["States"]
                             if (s["Epoch"], s["Match"]) == identity and state_key(s) in states}
            if not frames or max(frames) - min(frames) < 60:
                gaps.append(f"{name}: match {identity} lacks 60 frames of observed simulation")
            if not common_frames or max(common_frames) - min(common_frames) < 60:
                gaps.append(f"{name}: match {identity} lacks health comparison across 60 authority frames")
    if not shared_transition:
        gaps.append("no independent client observed both authority matches")
    return ("INCOMPLETE" if gaps else "PASS"), gaps


def shot_gap_context(missing, authority_shots, report, death_frames):
    """Diagnostic context only. Phase skips and death proximity never waive gaps.

    View frames are local clocks; only authority commit/death frames are compared.
    Health is sampled every six frames, so proximity is an association, not proof
    of client-side death/intent ordering or visible projectile absence.
    """
    arrived = {shot_key(s) for s in report["Shots"] if s["Stage"] in ("accepted", "offered")}
    skipped = {shot_key(s) for s in report["Shots"] if s["Stage"] == "phase-skipped"}
    near_death = set()
    for key in missing:
        shot = authority_shots.get(key)
        death = death_frames.get(key[:5])
        if shot and death is not None and abs(death - shot["ObservedFrame"]) <= 12:
            near_death.add(key)
    return {"arrivedOrLocallyOffered": len(missing & arrived),
            "noArrivalEvidence": len(missing - arrived),
            "recordedPhaseSkips": len(missing & skipped),
            "nearAuthorityDeathWithin12Frames": len(near_death),
            "weaponKindCounts": dict(Counter(f"{authority_shots[k]['Weapon']}:{authority_shots[k]['Kind']}"
                for k in missing if k in authority_shots)),
            "examples": [{"key": list(k), "arrivalRecorded": k in arrived,
                "phaseSkipRecorded": k in skipped,
                "authorityObservationStage": authority_shots.get(k, {}).get("Stage"),
                "authorityObservedFrame": authority_shots.get(k, {}).get("ObservedFrame"),
                "authorityFirstDeadFrame": death_frames.get(k[:5])} for k in sorted(missing)[:32]]}


def adjudicate(out, processes, expected_protocol=41, require_shots=True):
    """FAIL outranks missing evidence. No process count/exit-code-only green path."""
    out = Path(out)
    failures, incomplete, reports, observations = [], [], {}, {}
    if len(processes) < 2 or not any(p["name"] == "server" for p in processes):
        incomplete.append("no server/client campaign")
    settings = {}
    manifest = {}
    if (out / "manifest.json").exists():
        try:
            manifest = json.loads((out / "manifest.json").read_text())
            settings = manifest["settings"]
            expected = manifest["settings"]["players"] + 1 + bool(manifest["settings"].get("soak"))
            if len(processes) != expected:
                incomplete.append(f"expected {expected} processes, received {len(processes)}")
            if manifest.get("runnerError"):
                failures.append("runner: " + manifest["runnerError"])
            if manifest["assemblySha256"] != manifest["assemblyAfterSha256"]:
                failures.append("running assembly changed")
        except (OSError, ValueError, KeyError, TypeError) as error:
            incomplete.append(f"missing/malformed run manifest ({error})")
    else:
        incomplete.append("missing run manifest and assembly identity")
    for process in processes:
        name = process["name"]
        if process.get("timedOut") or process.get("unexpectedExit") or process.get("exit") != 0:
            failures.append(f"{name}: exit={process.get('exit')} timeout={process.get('timedOut')}")
        logs = [out / f"{name}.log", *(out / name).glob("**/*.txt")]
        for log in logs:
            if not log.exists():
                incomplete.append(f"{name}: missing log {log.name}")
                continue
            match = FATAL.search(log.read_text(errors="replace"))
            if match:
                failures.append(f"{name}: {match.group(0)} in {log.relative_to(out)}")
        try:
            report = json.loads((out / f"{name}.json").read_text())
            if report["Schema"] not in (1, 2) or report["Protocol"] != expected_protocol:
                failures.append(f"{name}: schema/protocol mismatch")
            for field in ("Overflow", "ConnectionLost", "SemanticOverrun", "SemanticDisconnects", "LastError"):
                if report[field]:
                    failures.append(f"{name}: {field}={report[field]}")
            if not report["Frames"] or not report["Windows"] or not report["States"] or not report["Transport"]:
                incomplete.append(f"{name}: missing simulation, health, time-window or transport samples")
            if report["Transport"] and (report["Transport"]["QueueDrops"] or report["Transport"]["SocketErrors"]):
                failures.append(f"{name}: transport queue drop/socket error")
            shots = report["Shots"]
            # Validate every row before cross-process joins, including offered-only
            # rows. Truncated or mixed-version evidence cannot bypass adjudication.
            for row in shots:
                shot_key(row)
                for field in ("Stage", "Weapon", "Charge", "Kind", "ObservedFrame"):
                    row[field]
            for row in report["States"]:
                state_key(row), health_value(row)
            for row in report["Facts"]:
                for field in ("Type", "Payload", "Epoch", "Match", "Id", "Tick", "Baseline", "Award", "AwardFrontier"):
                    row[field]
            committed = Counter(shot_key(s) for s in shots if s["Stage"] == "committed")
            if any(n > 1 for n in committed.values()):
                failures.append(f"{name}: duplicate committed logical shots")
            if any(not all((k[0], k[1], k[3], k[4], k[5])) or not 0 <= k[2] < 8 for k in committed):
                failures.append(f"{name}: invalid committed shot identity")
            if require_shots and process.get("expectsShots", name != "server") and not any(s["Stage"] == "offered" for s in shots):
                incomplete.append(f"{name}: no offered shots; workload unexercised")
            if not any(report["AliveFrames"]):
                incomplete.append(f"{name}: no alive-player denominator")
            reports[name] = report
            observations[name] = {k: report[k] for k in ("Frames", "AliveFrames", "OfferedCarriers", "AcceptedCarriers",
                "AllocatedBytes", "HeapBytes", "WorkingSet", "Transport", "ServerSegments")}
            observations[name]["shotCounts"] = dict(Counter(s["Stage"] for s in shots))
        except (OSError, ValueError, KeyError, TypeError) as error:
            incomplete.append(f"{name}: missing/malformed evidence ({error})")
            reports.pop(name, None)
    if "server" not in reports or len(reports) != len(processes):
        incomplete.append("not every independent process supplied valid evidence")
    parity = {}
    if "server" in reports:
        authority = reports["server"]
        if not authority["ServerSegments"]:
            incomplete.append("server: no step distribution")
        if any(s["StepFailures"] for s in authority["ServerSegments"]):
            failures.append("server: simulation step failures")
        schema = authority["Schema"]
        states = {state_key(s): health_value(s, schema) for s in authority["States"]}
        committed = {shot_key(s): (s["Weapon"], s["Charge"], s["Kind"]) for s in authority["Shots"] if s["Stage"] == "committed"}
        authority_shots = {shot_key(s): s for s in authority["Shots"] if s["Stage"] == "committed"}
        authority_accepted = {shot_key(s): s for s in authority["Shots"] if s["Stage"] == "accepted"}
        authority_rejected, rejection_errors = continuous_terminal_rejections(authority, authority_accepted)
        failures.extend("server: " + error for error in rejection_errors)
        observations["server"]["continuousTerminalRejectedCount"] = len(authority_rejected)
        observations["server"]["continuousTerminalRejectedKeys"] = [list(k) for k in sorted(authority_rejected)]
        retired_turrets, turret_errors = authority_turret_retirements(authority, authority_accepted)
        failures.extend("server: " + error for error in turret_errors)
        observations["server"]["authorityRetiredTurretCount"] = len(retired_turrets)
        observations["server"]["authorityRetiredTurretKeys"] = [list(k) for k in sorted(retired_turrets)]
        unavailable_turrets = {k for k,row in retired_turrets.items() if row.get("Reason") == TURRET_UNAVAILABLE_REASON}
        observations["server"]["authorityUnavailableTurretCount"] = len(unavailable_turrets)
        observations["server"]["authorityUnavailableTurretKeys"] = [list(k) for k in sorted(unavailable_turrets)]
        # A dead-owner terminal receipt cannot coexist with an authoritative
        # damage/heal payment for the same full owner lifecycle and phase.
        paid_ticks = {tuple(row["Key"]["OwnerTick"][field] for field in
                           ("AuthorityEpoch", "MatchId", "ShooterSlot", "Generation", "LifeId", "ShotId"))
                      for row in authority.get("ContinuousPayments", {}).get("Rows", [])}
        for key, row in authority_rejected.items():
            if (*key[:5], row["ContinuousPhase"]) in paid_ticks:
                failures.append("server: rejected continuous tick has an authoritative payment " + str(key))
        accepted = authority_accepted.keys()
        death_frames = {}
        for state in authority["States"]:
            if state["Hp"] == 0:
                identity = tuple(state[k] for k in ("Epoch", "Match", "Slot", "Generation", "Life"))
                death_frames[identity] = min(death_frames.get(identity, state["Frame"]), state["Frame"])
        all_offered = {shot_key(s) for r in reports.values() for s in r["Shots"] if s["Stage"] == "offered"}
        facts = {(f["Type"], f["Payload"]) for f in authority["Facts"]}
        for name, report in reports.items():
            if name == "server":
                continue
            common = [(s, states[state_key(s)]) for s in report["States"] if state_key(s) in states]
            if report["Schema"] != schema:
                failures.append(f"{name}: evidence schema differs from authority")
            slow_parity = {}
            if schema == 2:
                slow_parity, slow_errors = slow_lane_parity(authority, report)
                failures.extend(f"{name}: {error}" for error in slow_errors)
            mismatch = sum(health_value(s, schema) != value for s, value in common)
            if not common:
                incomplete.append(f"{name}: no health samples with identical authority identity/frame")
            if mismatch:
                failures.append(f"{name}: {mismatch} authoritative health/event mismatches")
            offered = {shot_key(s): (s["Weapon"], s["Charge"], s["Kind"]) for s in report["Shots"] if s["Stage"] == "offered"}
            wrong = sum(value != committed[k] for k, value in offered.items() if k in committed)
            if wrong:
                failures.append(f"{name}: {wrong} shots committed with different weapon/charge/kind")
            missing_accepted = offered.keys() - accepted
            missing_committed = offered.keys() - committed.keys()
            terminal_offered = set()
            offered_rows = {shot_key(s): s for s in report["Shots"] if s["Stage"] == "offered"}
            for key in missing_committed & authority_rejected.keys():
                fields = ("SourceFrame", "ContinuousPhase", "Weapon", "Charge", "Kind")
                if tuple(offered_rows[key].get(f) for f in fields) == tuple(authority_rejected[key][f] for f in fields):
                    terminal_offered.add(key)
                else:
                    failures.append(f"{name}: offered event differs from terminal rejected event")
            missing_committed -= terminal_offered
            retired_offered = set()
            for key in missing_committed & retired_turrets.keys():
                fields = ("SourceFrame", "ContinuousPhase", "Weapon", "Charge", "Kind")
                if tuple(offered_rows[key].get(f) for f in fields) == tuple(retired_turrets[key][f] for f in fields):
                    retired_offered.add(key)
                else: failures.append(name + ": offered turret differs from retired authority event")
            missing_committed -= retired_offered
            if any(s.get("Reason") in (TURRET_RETIRED_REASON,TURRET_UNAVAILABLE_REASON) for s in report["Shots"]):
                failures.append(name + ": observer turret retirement cannot excuse authority emission presentation")
            # Generic expiry/skipped phases are still unexplained losses.
            if missing_accepted or missing_committed:
                failures.append(f"{name}: unexplained shot tail/loss: {len(missing_accepted)} unaccepted, {len(missing_committed)} uncommitted")
            matching_facts = sum((f["Type"], f["Payload"]) in facts for f in report["Facts"] if not f["Baseline"])
            semantic_expected = semantic_missing = semantic_conflicts = 0
            for baseline in (f for f in report["Facts"] if f["Baseline"]):
                identity = baseline["Epoch"], baseline["Match"]
                # Compare inside the observed authority interval, leaving one second
                # for delivery at shutdown. Record this bound, not an all-time claim.
                end = max((s["Frame"] for s, _ in common if (s["Epoch"], s["Match"]) == identity), default=0) - 60
                expected = {(f["Award"], f["Id"]): f["Payload"] for f in authority["Facts"]
                    if (f["Epoch"], f["Match"]) == identity and not f["Baseline"] and f["Tick"] <= end
                    and f["Id"] > (baseline["AwardFrontier"] if f["Award"] else baseline["Id"])}
                received = {(f["Award"], f["Id"]): f["Payload"] for f in report["Facts"]
                    if (f["Epoch"], f["Match"]) == identity and not f["Baseline"]}
                semantic_expected += len(expected)
                semantic_missing += len(expected.keys() - received.keys())
                semantic_conflicts += sum(value != received[key] for key, value in expected.items() if key in received)
            if semantic_missing or semantic_conflicts:
                failures.append(f"{name}: semantic gap/conflict: {semantic_missing} missing, {semantic_conflicts} different")
            if not matching_facts:
                incomplete.append(f"{name}: no comparable authority semantic facts")
            if not semantic_expected:
                incomplete.append(f"{name}: semantic completeness interval unexercised")
            received_commits = {shot_key(s) for s in report["Shots"] if s["Stage"] == "committed"}
            view_rejected, view_rejection_errors = continuous_terminal_rejections(report, authority_accepted)
            failures.extend(name + ": " + error for error in view_rejection_errors)
            expected_commits = set()
            intervals = {}
            for state, _ in common:
                identity = state["Epoch"], state["Match"]
                first, last = intervals.get(identity, (state["Frame"], state["Frame"]))
                intervals[identity] = min(first, state["Frame"]), max(last, state["Frame"])
            for key, shot in authority_shots.items():
                if key not in all_offered:
                    continue
                first, last = intervals.get(key[:2], (1, 0))
                if first + 30 <= shot["ObservedFrame"] <= last - 30:
                    expected_commits.add(key)
            terminal_view = expected_commits & view_rejected.keys()
            unobserved = expected_commits - received_commits - terminal_view
            if unobserved:
                failures.append(f"{name}: {len(unobserved)} authority shots not committed in this view")
            if not expected_commits:
                incomplete.append(f"{name}: no comparable shooter/observer shot interval")
            parity[name] = {"healthSamplesCompared": len(common), "healthMismatches": mismatch,
                **slow_parity,
                "offeredShots": len(offered), "acceptedShots": len(offered.keys() & accepted),
                "committedShots": len(offered.keys() & committed.keys()), "wrongWeaponChargeKind": wrong,
                "terminalRejectedOfferedShots": len(terminal_offered),
                "terminalRejectedOfferedKeys": [list(k) for k in sorted(terminal_offered)],
                "authorityRetiredOfferedTurretCount": len(retired_offered),
                "authorityRetiredOfferedTurretKeys": [list(k) for k in sorted(retired_offered)],
                "terminalRejectedViewShots": len(terminal_view),
                "terminalRejectedViewKeys": [list(k) for k in sorted(terminal_view)],
                "unacceptedKeys": [list(k) for k in sorted(missing_accepted)[:32]],
                "uncommittedKeys": [list(k) for k in sorted(missing_committed)[:32]],
                "authorityGapContext": shot_gap_context(missing_committed, authority_accepted, authority, {}),
                "viewGapContext": shot_gap_context(unobserved, authority_shots, report, death_frames),
                "matchingSemanticFacts": matching_facts,
                "viewExpectedCommits": len(expected_commits), "viewMissingCommits": len(unobserved),
                "viewMissingKeys": [list(key) for key in sorted(unobserved)[:32]],
                "viewTailExcludedFrames": 30,
                "semanticExpected": semantic_expected, "semanticMissing": semantic_missing,
                "semanticConflicts": semantic_conflicts, "semanticTailExcludedFrames": 60,
                "semanticCompleteness": "FAIL" if semantic_missing or semantic_conflicts else "PASS" if semantic_expected else "INCOMPLETE",
                "renderedProjectileAgreement": "NOT_RUN"}
    # Keep coverage in the offline adjudicator so re-reading raw evidence cannot
    # turn a short or frozen soak into a pass by losing the runner's final checks.
    windows = [w for report in reports.values() for w in report["Windows"]]
    rotation, rotation_gaps = rotation_coverage(reports, processes, settings)
    if rotation_gaps:
        incomplete.extend("map rotation: " + gap for gap in rotation_gaps)
    soak = settings.get("soak", False)
    sustained = soak and settings.get("players") == 8 and 1200 <= settings.get("seconds", 0) <= 1800 and all(
        record.get("requestedSeconds", 0) > 0
        and reports.get(record["name"], {}).get("Frames", 0) >= record["requestedSeconds"] * 60 * .9
        and max((w.get("Seconds", 0) for w in reports.get(record["name"], {}).get("Windows", [])), default=0) >= record["requestedSeconds"] - 2
        for record in processes if record["name"] != "server")
    coverage = {"renderedProjectileAgreement": "NOT_RUN",
        "botFills": "PASS" if any(w.get("BotMask", 0) for w in windows) else "NOT_RUN",
        "spectator": "PASS" if any(w.get("Spectating") for w in windows) else "NOT_RUN",
        "mapRotation": rotation,
        "shockCoilDrainKillParity": "NOT_RUN", "imperialistSpawnTrapCoverage": "NOT_RUN",
        "sustained20Minutes": "PASS" if sustained else "INCOMPLETE" if soak else "NOT_RUN"}
    drain_status, drain_gaps = drain_coverage(manifest, reports)
    coverage["boundedHeadlessDrain"] = drain_status
    failures.extend("campaign drain: " + gap for gap in drain_gaps)
    if soak and (not sustained or any(coverage[k] != "PASS" for k in ("botFills", "spectator", "mapRotation"))):
        incomplete.append("soak duration, load, bot, spectator or map-rotation coverage missing")
    return {"status": "FAIL" if failures else "INCOMPLETE" if incomplete else "PASS", "coverage": coverage,
            "mapRotationEvidenceGaps": rotation_gaps,
            "campaignDrainEvidenceGaps": drain_gaps,
            "failures": failures, "incomplete": incomplete, "processes": processes,
            "observations": observations, "parity": parity}
