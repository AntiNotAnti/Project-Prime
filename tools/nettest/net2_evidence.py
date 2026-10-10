"""Offline, fail-closed adjudication of N0 process evidence (no game imports)."""
from collections import Counter
import json
from pathlib import Path
import re

FATAL = re.compile(r"semantic history overrun|reliable (?:channel |control )?(?:failed|gap)|"
                   r"timed out|\btimeout\b|\[sim\] step failed|could not (?:join|load|spectate)|"
                   r"connection lost|\bcrashed:|unhandled exception|RESULT: FAIL", re.I)


def shot_key(row):
    key = row["Key"]
    return tuple(key[k] for k in ("AuthorityEpoch", "MatchId", "ShooterSlot", "Generation", "LifeId", "ShotId"))


def state_key(row):
    return tuple(row[k] for k in ("Epoch", "Match", "Frame", "Slot", "Generation", "Life"))


def health_value(row):
    return tuple(row[k] for k in ("Hp", "TurretHp", "Kills", "Deaths", "DamageEvent"))


def adjudicate(out, processes, expected_protocol=41, require_shots=True):
    """FAIL outranks missing evidence. No process count/exit-code-only green path."""
    out = Path(out)
    failures, incomplete, reports, observations = [], [], {}, {}
    for process in processes:
        name = process["name"]
        if process.get("timedOut") or process.get("exit") != 0:
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
            if report["Schema"] != 1 or report["Protocol"] != expected_protocol:
                failures.append(f"{name}: schema/protocol mismatch")
            for field in ("Overflow", "ConnectionLost", "SemanticOverrun", "SemanticDisconnects", "LastError"):
                if report[field]:
                    failures.append(f"{name}: {field}={report[field]}")
            if not report["Frames"] or not report["Windows"] or not report["States"] or not report["Transport"]:
                incomplete.append(f"{name}: missing simulation, health, time-window or transport samples")
            if report["Transport"] and (report["Transport"]["QueueDrops"] or report["Transport"]["SocketErrors"]):
                failures.append(f"{name}: transport queue drop/socket error")
            shots = report["Shots"]
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
        states = {state_key(s): health_value(s) for s in authority["States"]}
        committed = {shot_key(s): (s["Weapon"], s["Charge"], s["Kind"]) for s in authority["Shots"] if s["Stage"] == "committed"}
        accepted = {shot_key(s) for s in authority["Shots"] if s["Stage"] == "accepted"}
        facts = {(f["Type"], f["Payload"]) for f in authority["Facts"]}
        for name, report in reports.items():
            if name == "server":
                continue
            common = [(s, states[state_key(s)]) for s in report["States"] if state_key(s) in states]
            mismatch = sum(health_value(s) != value for s, value in common)
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
            # N0 observes missing final shots; it cannot invent expiry/rejection proof.
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
            parity[name] = {"healthSamplesCompared": len(common), "healthMismatches": mismatch,
                "offeredShots": len(offered), "acceptedShots": len(offered.keys() & accepted),
                "committedShots": len(offered.keys() & committed.keys()), "wrongWeaponChargeKind": wrong,
                "unacceptedKeys": [list(k) for k in sorted(missing_accepted)[:32]],
                "uncommittedKeys": [list(k) for k in sorted(missing_committed)[:32]],
                "matchingSemanticFacts": matching_facts,
                "semanticExpected": semantic_expected, "semanticMissing": semantic_missing,
                "semanticConflicts": semantic_conflicts, "semanticTailExcludedFrames": 60,
                "semanticCompleteness": "FAIL" if semantic_missing or semantic_conflicts else "PASS" if semantic_expected else "INCOMPLETE",
                "renderedProjectileAgreement": "NOT_RUN"}
    return {"status": "FAIL" if failures else "INCOMPLETE" if incomplete else "PASS",
            "failures": failures, "incomplete": incomplete, "processes": processes,
            "observations": observations, "parity": parity}
