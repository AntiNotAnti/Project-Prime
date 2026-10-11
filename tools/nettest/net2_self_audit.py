"""Focused N7 self-splash payment/receipt audit; preserves base campaign verdict."""
import argparse
from collections import Counter, defaultdict
import json
import math
from pathlib import Path
from net2_continuous_audit import number, read_verdict_log


SHOT_FIELDS = ("AuthorityEpoch", "MatchId", "ShooterSlot", "Generation", "LifeId", "ShotId")
RESULTS = {0: "applied", 1: "already resolved", 13: "corrected"}
DESCRIPTIONS = {**RESULTS, 2: "shooter was already dead when it fired", 3: "victim was already down",
                4: "refused", 5: "older than the history", 6: "wrong lifecycle",
                7: "hit point outside reconciliation radius", 8: "damage exceeds weapon limit",
                9: "launch frame follows hit frame", 10: "authority damage rules prevented the hit",
                11: "impact impulse exceeds weapon limit", 12: "authority claim capacity"}


def payment_key(row):
    key = row["Key"]
    shot = tuple(number(key["Shot"][f]) for f in SHOT_FIELDS)
    tail = tuple(number(key[f]) for f in ("Victim", "Generation", "Life", "Component", "Kind"))
    if not all(shot[i] for i in (0, 1, 3, 4, 5)) or shot[2] >= 8:
        raise ValueError("invalid full shot identity")
    if tail[0] != shot[2] or tail[1:3] != shot[3:5] or not tail[3] or tail[4] not in (1, 2):
        raise ValueError("invalid self component/victim lifecycle")
    return shot + tail


def outcome(row, key):
    ack = row["Outcome"]
    for field in ("ShotId", "ClaimId", "Result", "VictimSlot", "VictimGeneration", "VictimLife",
                  "DamageApplied", "HealthAfter", "DamageSequence", "Flags"):
        number(ack[field])
    if (ack["ShotId"], ack["VictimSlot"], ack["VictimGeneration"], ack["VictimLife"]) != (key[5], *key[6:9]):
        raise ValueError("outcome differs from exact payment identity")
    if ack["Result"] > 13:
        raise ValueError("unknown outcome result")
    number(row["AuthorityFrame"])
    return ack


def vector(value):
    if not isinstance(value, list) or len(value) != 3 or any(type(v) not in (float, int) or not math.isfinite(v) for v in value):
        raise ValueError("invalid finite impulse vector")
    return value


def same_vector(a, b):
    return all(math.isclose(x, y, rel_tol=1e-5, abs_tol=1e-4) for x, y in zip(a, b))


def audit(server, peers, summary, scenario="self-moving", verdicts=None):
    failures, missing = [], []
    metrics = {"peers": {}, "authorityPayments": 0, "acceptedReceipts": 0, "feedback": {}}
    payments, feedback_payments = {}, defaultdict(list)
    accepted_keys = set()
    expected = {p["name"] for p in summary.get("processes", []) if p.get("name") != "server"}
    if not expected or not any(p.get("name") == "server" for p in summary.get("processes", [])):
        missing.append("complete server/peer process inventory unavailable")
    if expected != set(peers):
        failures.append("peer evidence files differ from campaign process inventory")
    if summary.get("status") == "FAIL":
        failures.append("base campaign adjudication failed")
    elif summary.get("status") != "PASS":
        missing.append("base campaign adjudication did not pass")
    if summary.get("coverage", {}).get("boundedHeadlessDrain") != "PASS":
        missing.append("bounded campaign drain did not pass")
    if scenario not in ("self-moving", "self-wall"):
        failures.append("unsupported self campaign scenario")
    try:
        ledger = server["SelfSplash"]
        if ledger["Enabled"] is not True:
            missing.append("authority self splash disabled")
        for field in ("Observed", "Verified", "Refused", "CapacityRefused", "EvidenceOverflow"):
            number(ledger[field])
        if ledger["CapacityRefused"] or ledger["EvidenceOverflow"]:
            failures.append("authority self evidence overflow/capacity refusal")
        for row in ledger["Paid"]:
            key = payment_key(row)
            ack = outcome(row, key)
            impulse = vector(row["Impulse"])
            if key in payments:
                failures.append("self component paid twice: " + str(key))
            payments[key] = ack
            if ack["Result"] not in RESULTS or not ack["DamageApplied"] or not ack["DamageSequence"]:
                failures.append("authority payment has refused/zero damage outcome: " + str(key))
            feedback_payments[(key[0], key[1], key[6], key[7], key[8], ack["DamageSequence"])].append((key, impulse))
        metrics["authorityPayments"] = len(payments)
        if not payments:
            missing.append("no authority self damage payment exercised")
    except (KeyError, TypeError, ValueError) as error:
        failures.append("malformed/missing authority self evidence: " + str(error))

    shooter_slots, all_slots = set(), set()
    for name, peer in peers.items():
        try:
            local = {w["LocalSlot"] for w in peer["Windows"] if 0 <= w.get("LocalSlot", -1) < 8}
            if len(local) != 1:
                raise ValueError("unavailable/ambiguous local slot")
            slot = next(iter(local))
            if slot in all_slots:
                failures.append(name + ": duplicate independent peer local slot")
            all_slots.add(slot)
            shooter = slot % 2 == 0
            if shooter:
                shooter_slots.add(slot)
            receipt = peer["CombatReconciliation"]
            fields = ("Predicted", "Outstanding", "SelfPredicted", "SelfConfirmed", "SelfDeathsPredicted",
                      "SelfOutstanding", "SelfPendingClaims")
            values = {f: number(receipt[f]) for f in fields}
            metrics["peers"][name] = dict(values, localSlot=slot)
            if values["SelfConfirmed"] > values["SelfPredicted"]:
                failures.append(name + ": confirmed self predictions exceed offered predictions")
            if values["Outstanding"] or values["SelfOutstanding"] or values["SelfPendingClaims"] or number(peer["Drain"]["ClaimsPending"]):
                failures.append(name + ": combat/self prediction or claim remains pending")
            if shooter and not values["SelfPredicted"]:
                missing.append(name + ": self prediction workload not exercised")
            parity = summary.get("parity", {}).get(name, {})
            if parity.get("healthMismatches", 0):
                failures.append(name + ": authoritative health/scoreboard mismatch")
            elif parity.get("healthMismatches") != 0 or parity.get("healthSamplesCompared", 0) <= 0:
                missing.append(name + ": exact authoritative health/scoreboard comparison unavailable")
            ledger = peer["SelfSplash"]
            if number(ledger["EvidenceOverflow"]) or number(ledger["CapacityRefused"]):
                failures.append(name + ": self evidence overflow/capacity refusal")
            logs = (verdicts or {}).get(name)
            log_available = logs is not None
            if logs is None:
                missing.append(name + ": exact verdict log unavailable")
                logs = []
            log_counts = Counter((tuple(r["Key"]), number(r["ClaimId"]), r["Result"]) for r in logs)
            recorded = Counter()
            receipts_seen = set()
            for row in ledger["Receipts"]:
                key = payment_key(row)
                ack = outcome(row, key)
                receipt_id = key, ack["ClaimId"]
                if receipt_id in receipts_seen:
                    failures.append(name + ": duplicate exact self receipt: " + str(receipt_id))
                receipts_seen.add(receipt_id)
                if key[2] != slot or ack["ClaimId"] == 0:
                    failures.append(name + ": self receipt has wrong owner/zero claim identity")
                if ack["Result"] in RESULTS:
                    accepted_keys.add(key)
                    metrics["acceptedReceipts"] += 1
                    paid = payments.get(key)
                    if not paid:
                        failures.append(name + ": accepted self ACK has no exact authority payment: " + str(key))
                    elif any(ack[f] != paid[f] for f in ("DamageApplied", "HealthAfter", "DamageSequence")):
                        failures.append(name + ": accepted self ACK outcome differs from authority payment: " + str(key))
                elif key in payments:
                    failures.append(name + ": refused exact self ACK contradicts paid component: " + str(key))
                recorded[(key[:6], ack["ClaimId"], DESCRIPTIONS[ack["Result"]])] += 1
                # Refusal descriptions are captured in logs but cannot alone
                # identify component. Exact receipt keys supply that join.
                matching = sum(n for (shot, claim, _), n in log_counts.items() if (shot, claim) == (key[:6], ack["ClaimId"]))
                if matching != 1:
                    missing.append(name + ": self receipt lacks one exact shot/claim verdict log")
                if any(r.get("Weapon") != "Missile" for r in logs
                       if (tuple(r["Key"]), r["ClaimId"]) == (key[:6], ack["ClaimId"])):
                    failures.append(name + ": self rig receipt weapon differs from Missile")
            for token, count in recorded.items():
                other_results = sum(n for (shot, claim, _), n in log_counts.items() if (shot, claim) == token[:2])
                if log_available and other_results and log_counts[token] != count:
                    failures.append(name + ": receipt/log terminal result differs")
            if values["Predicted"] == 0 and len(logs) != len(ledger["Receipts"]):
                missing.append(name + ": verdict log count differs from exact self receipts")
            elif values["Predicted"] and len(logs) > len(ledger["Receipts"]):
                missing.append(name + ": verdicts without exact self keys may describe remote components")
            seen_feedback, applied_feedback = set(), 0
            for row in ledger["Feedback"]:
                target = tuple(number(row[f]) for f in ("AuthorityEpoch", "MatchId", "VictimSlot", "Generation", "Life", "DamageSequence"))
                if target in seen_feedback:
                    failures.append(name + ": duplicate exact self feedback sequence")
                seen_feedback.add(target)
                candidates = feedback_payments.get(target, [])
                if len(candidates) != 1:
                    missing.append(name + ": self feedback lacks unambiguous authority sequence/payment")
                    continue
                key, impulse = candidates[0]
                authority, predicted, before, after = (vector(row[f]) for f in ("Authority", "Predicted", "Before", "After"))
                if target[2] != slot or not same_vector(authority, impulse):
                    failures.append(name + ": self feedback impulse/owner differs from authority payment")
                # Capture emits feedback only in the alive/applied branch.
                if not same_vector([a-b for a,b in zip(after,before)], [a-b for a,b in zip(authority,predicted)]):
                    failures.append(name + ": self impulse correction delta differs")
                applied_feedback += 1
            metrics["feedback"][name] = dict(rows=len(seen_feedback), applied=applied_feedback)
            if shooter and not applied_feedback:
                missing.append(name + ": no exact applied self impulse feedback exercised")
        except (KeyError, TypeError, ValueError) as error:
            failures.append(name + ": malformed/missing self reconciliation evidence: " + str(error))
    for slot in sorted(shooter_slots):
        if not any(k[2] == slot for k in payments):
            missing.append(f"shooter {slot}: no authority self payment")
        if not any(k[2] == slot for k in accepted_keys):
            missing.append(f"shooter {slot}: no exact accepted self ACK")
        if scenario == "self-wall":
            paid_lives = {(k[0], k[1], k[3], k[4]) for k in payments if k[2] == slot}
            living = defaultdict(list)
            for s in server.get("States", []):
                if s.get("Slot") == slot and s.get("Hp", 0) > 0:
                    living[(s["Epoch"], s["Match"], s["Generation"])].append((s["Life"], s["Deaths"]))
            respawn = any(len({life for life, _ in rows}) > 1 and len({deaths for _, deaths in rows}) > 1 for rows in living.values())
            lethal = any(k[2] == slot and a["Flags"] & 2 and a["HealthAfter"] == 0 for k, a in payments.items())
            if not respawn or not lethal or len(paid_lives) < 2:
                missing.append(f"shooter {slot}: self-wall lacks paid lethal damage and paid respawn lifecycle")
    if not shooter_slots:
        missing.append("no expected even-slot self shooter")
    status = "FAIL" if failures else "INCOMPLETE" if missing else "PASS"
    return dict(status=status, scenario=scenario, metrics=metrics, failures=failures, missing=missing,
                limitations=["Bounded native/self-rescue payment, exact terminal ACK and impulse correction evidence only.",
                             "Base campaign verifies sampled authority health, kills, deaths and damage sequence; not every intermediate change.",
                             "No all-weapon, burn-source, extended soak or rendered feedback agreement claim."],
                coverage={"boundedSelfPayments": status, "selfDeathRespawn": status if scenario == "self-wall" else "NOT_RUN",
                          "allSelfSplashWeapons": "NOT_RUN", "burnSourceParity": "NOT_RUN", "renderedSelfFeedback": "NOT_RUN"})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("campaign", type=Path)
    parser.add_argument("--scenario", choices=("self-moving", "self-wall"), default="self-moving")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        read = lambda p: json.loads(p.read_text())
        peers = {p.stem: read(p) for p in args.campaign.glob("peer-*.json") if not p.name.endswith(".drain.json")}
        logs = {n: read_verdict_log(path) for n in peers
                if (path := args.campaign / n / ("netlog-" + n.replace("-", "_") + ".txt")).exists()}
        result = audit(read(args.campaign / "server.json"), peers, read(args.campaign / "summary.json"), args.scenario, logs)
    except (OSError, KeyError, TypeError, ValueError) as error:
        result = dict(status="FAIL", failures=["unreadable campaign evidence: " + str(error)])
    text = json.dumps(result, indent=2) + "\n"
    if args.output:
        with args.output.open("x") as stream:
            stream.write(text)
    else:
        print(text, end="")
    return 0 if result["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
