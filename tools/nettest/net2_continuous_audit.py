"""Bounded N6 transaction audit; does not replace the campaign adjudicator."""
import argparse
import json
import re
from pathlib import Path
from net2_evidence import continuous_terminal_rejections, shot_key


def number(value):
    if type(value) is not int or value < 0:
        raise ValueError("expected nonnegative integer")
    return value


def read_verdict_log(path):
    """Read exact emitted-shot receipts; aggregate rejected counters cannot join payments."""
    pattern = re.compile(r"\[shot\] key=(\d+/\d+/\d+/\d+/\d+/\d+) stage=verdict weapon=(\w+) authorityFrame=\d+ id=(\d+) result=(.*)$")
    rows = []
    for line in path.read_text().splitlines():
        match = pattern.search(line)
        if match:
            rows.append(dict(Key=tuple(map(int, match[1].split('/'))),
                             ClaimId=int(match[3]), Weapon=match[2], Result=match[4]))
    return rows


def audit(server, peers, summary, mixed=True, verdicts=None):
    failures, missing = [], []
    metrics = {"authority": {}, "peers": {}}
    ticks = set()
    try:
        ledger = server["ContinuousPayments"]
        rows = ledger["Rows"]
        count, capacity = number(ledger["EvidenceCount"]), number(ledger["EvidenceCapacity"])
        if not isinstance(rows, list) or count != len(rows) or capacity == 0:
            raise ValueError("invalid payment evidence count/capacity")
        if count >= capacity:
            missing.append("authority payment evidence reached its ring capacity; history may be truncated")
        totals = dict(LifetimePayments=len(rows), LifetimeDamage=0, LifetimeTurretDamage=0,
                      LifetimeHealBody=0, LifetimeHealTurret=0)
        for row in rows:
            key, outcome = row["Key"], row["Outcome"]
            owner, victim = key["OwnerTick"], key["Victim"]
            tick = tuple(number(owner[field]) for field in
                         ("AuthorityEpoch", "MatchId", "ShooterSlot", "Generation", "LifeId", "ShotId"))
            if not all(tick[i] > 0 for i in (0, 1, 3, 4, 5)) or tick[2] >= 8 or tick[5] % 2:
                raise ValueError("invalid owner lifecycle/tick identity")
            target = number(victim["EncodedSlot"])
            generation, life = number(victim["Generation"]), number(victim["LifeId"])
            if target not in range(0x81, 0x89) or generation == 0 or life == 0 or key["Flags"] not in (0, 1):
                raise ValueError("invalid victim lifecycle/payment identity")
            if tick in ticks:
                failures.append("authority owner tick paid more than once: " + str(tick))
            ticks.add(tick)
            if (outcome["VictimSlot"], outcome["VictimGeneration"], outcome["VictimLife"]) != (target - 0x81, generation, life):
                failures.append("authority outcome victim differs from payment key: " + str(tick))
            damage = number(outcome["DamageApplied"])
            turret = number(row["DamageTurret"])
            body_heal, turret_heal = number(row["HealBody"]), number(row["HealTurret"])
            if damage + turret == 0:
                failures.append("authority paid a zero-damage tick: " + str(tick))
            # These short campaigns use ordinary, non-Balanced drain rules.
            if body_heal + turret_heal > damage + turret:
                failures.append("authority heal exceeds actual removed health: " + str(tick))
            totals["LifetimeDamage"] += damage
            totals["LifetimeTurretDamage"] += turret
            totals["LifetimeHealBody"] += body_heal
            totals["LifetimeHealTurret"] += turret_heal
        for field, total in totals.items():
            if number(ledger[field]) != total:
                failures.append("authority payment rows do not equal " + field)
        metrics["authority"] = dict(totals, uniqueOwnerTicks=len(ticks), evidenceCount=count,
                                    evidenceCapacity=capacity)
        if not totals["LifetimeDamage"] + totals["LifetimeTurretDamage"]:
            missing.append("no committed continuous damage exercised")
        if not totals["LifetimeHealBody"] + totals["LifetimeHealTurret"]:
            missing.append("no committed continuous healing exercised")
    except (KeyError, TypeError, ValueError) as error:
        failures.append("malformed/missing authority continuous evidence: " + str(error))

    # OwnerTick is the canonical phase; Outcome.ShotId names the emitted event.
    # Join on the entire lifecycle plus emitted ID, never on phase or aggregate HP.
    paid_emitted = set()
    for row in server.get("ContinuousPayments", {}).get("Rows", []):
        try:
            owner = row["Key"]["OwnerTick"]
            emitted = tuple(number(owner[f]) for f in
                            ("AuthorityEpoch", "MatchId", "ShooterSlot", "Generation", "LifeId")) + (number(row["Outcome"]["ShotId"]),)
            if emitted[-1] == 0:
                raise ValueError("zero emitted shot ID")
            paid_emitted.add(emitted)
        except (KeyError, TypeError, ValueError) as error:
            failures.append("payment lacks exact emitted shot identity: " + str(error))
    verdict_metrics = {}
    accepted_results = {"applied", "already resolved", "corrected"}
    refused_results = {"authority claim capacity", "wrong lifecycle", "hit point outside reconciliation radius",
                       "damage exceeds weapon limit", "launch frame follows hit frame",
                       "authority damage rules prevented the hit", "impact impulse exceeds weapon limit",
                       "shooter was already dead when it fired", "victim was already down", "refused", "older than the history"}
    for name, peer in peers.items():
        rows = (verdicts or {}).get(name)
        count = peer.get("CombatReconciliation", {}).get("CombatAcks", 0)
        if rows is None:
            if count or peer.get("CombatReconciliation", {}).get("Predicted", 0) or peer.get("CombatReconciliation", {}).get("Denied", 0):
                missing.append(name + ": exact CombatAck verdict evidence unavailable")
            continue
        rejected, contradictions = set(), set()
        # CombatAcks counts only receipts that retire a still-live debit.
        # NetHitClaims logs late receipts after expiry/lifecycle release too.
        expected_receipts = peer.get("CombatReconciliation", {}).get("Predicted", count)
        if len(rows) != expected_receipts or len(rows) < count:
            missing.append(name + ": verdict evidence count differs from predicted claims")
        for row in rows:
            try:
                key = tuple(number(v) for v in row["Key"])
                if len(key) != 6 or row["Result"] not in accepted_results | refused_results:
                    raise ValueError("invalid key or unknown verdict")
                if row["Result"] in refused_results:
                    rejected.add(key)
                    if key in paid_emitted:
                        contradictions.add(key)
            except (KeyError, TypeError, ValueError) as error:
                failures.append(name + ": malformed exact verdict evidence: " + str(error))
        for key in sorted(contradictions):
            failures.append(name + ": refused CombatAck emitted shot has authority damage/heal payment: " + str(key))
        verdict_metrics[name] = dict(rows=len(rows), refusedEmittedKeys=[list(k) for k in sorted(rejected)],
                                    contradictoryPaidKeys=[list(k) for k in sorted(contradictions)])
    metrics["exactCombatAckVerdicts"] = verdict_metrics

    accepted = {shot_key(row): row for row in server.get("Shots", []) if row.get("Stage") == "accepted"}
    terminal_counts = {}
    for name, report in {"server": server, **peers}.items():
        try:
            terminal, errors = continuous_terminal_rejections(report, accepted)
            failures.extend(name + ": " + error for error in errors)
            terminal_counts[name] = dict(count=len(terminal), keys=[list(k) for k in sorted(terminal)])
            if name == "server":
                for key, row in terminal.items():
                    if (*key[:5], row["ContinuousPhase"]) in ticks:
                        failures.append("authority terminal rejected tick has a damage/heal payment: " + str(key))
        except (KeyError, TypeError, ValueError) as error:
            failures.append(name + ": malformed terminal disposition evidence: " + str(error))
    metrics["terminalContinuousRejections"] = terminal_counts

    expected = {p["name"] for p in summary.get("processes", []) if p.get("name") != "server"}
    if not peers or not expected:
        missing.append("peer process inventory unavailable")
    if expected != set(peers):
        failures.append("peer evidence files differ from campaign process inventory")
    for name, peer in peers.items():
        try:
            receipt = peer["CombatReconciliation"]
            fields = ("DrainPredicted", "DrainConfirmed", "DrainRejected", "DrainExpired", "DrainCurrentPoints", "Outstanding")
            values = {field: number(receipt[field]) for field in fields}
            metrics["peers"][name] = values
            if values["DrainPredicted"] != sum(values[f] for f in fields[1:5]):
                failures.append(name + ": predicted drain accounting does not balance")
            if values["DrainCurrentPoints"] or values["Outstanding"]:
                failures.append(name + ": prediction remains outstanding after drain")
            parity = summary.get("parity", {}).get(name, {})
            if parity.get("healthMismatches", 0) > 0:
                failures.append(name + ": campaign records authoritative health mismatches")
            elif parity.get("healthMismatches") != 0 or parity.get("healthSamplesCompared", 0) <= 0:
                missing.append(name + ": exact authoritative health parity not established")
        except (KeyError, TypeError, ValueError) as error:
            failures.append(name + ": malformed/missing drain evidence: " + str(error))
    if summary.get("status") == "FAIL":
        failures.append("unchanged campaign adjudication failed")
    elif summary.get("status") != "PASS":
        missing.append("unchanged campaign adjudication did not pass")
    if summary.get("coverage", {}).get("boundedHeadlessDrain") != "PASS":
        missing.append("campaign drain barrier did not pass")

    shots = [row for peer in peers.values() for row in peer.get("Shots", []) if row.get("Stage") == "committed"]
    weapon_counts = {str(weapon): sum(row.get("Weapon") == weapon for row in shots) for weapon in (7, 4)}
    respawned = set()
    for peer in peers.values():
        lives = {}
        for state in peer.get("States", []):
            if state.get("Hp", 0) <= 0:
                continue
            context = tuple(state.get(k) for k in ("Epoch", "Match", "Slot", "Generation"))
            lives.setdefault(context, set()).add((state.get("Life"), state.get("Deaths", 0)))
        respawned.update(context for context, observed in lives.items()
                        if len({life for life, _ in observed}) > 1
                        and len({deaths for _, deaths in observed}) > 1)
    metrics.update(committedWeaponRows=weapon_counts, respawnedContexts=len(respawned))
    if mixed:
        if not all(weapon_counts.values()):
            missing.append("mixed scenario did not exercise both Shock Coil and Imperialist shots")
        if not respawned:
            missing.append("mixed scenario lacks living bodies across repeated lives with recorded deaths")
    status = "FAIL" if failures else "INCOMPLETE" if missing else "PASS"
    return dict(status=status, scope="bounded authority transactions and local credit accounting; non-Balanced campaign",
                metrics=metrics, failures=failures, missing=missing,
                coverage={"boundedContinuousPayments": status,
                          "boundedMixedCombat": status if mixed else "NOT_RUN",
                          "exactTickPaymentsAcrossAllClients": "NOT_RUN",
                          "extendedContinuousSoak": "NOT_RUN",
                          "renderedEndpointAgreement": "NOT_RUN"})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("campaign", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--coil-only", action="store_true")
    args = parser.parse_args()
    try:
        read = lambda path: json.loads(path.read_text())
        peers = {p.stem: read(p) for p in args.campaign.glob("peer-*.json")
                 if not p.name.endswith(".drain.json")}
        result = audit(read(args.campaign / "server.json"), peers,
                       read(args.campaign / "summary.json"), mixed=not args.coil_only,
                       verdicts={name: read_verdict_log(path) for name in peers
                                 if (path := args.campaign / name / ("netlog-" + name.replace("-", "_") + ".txt")).exists()})
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
