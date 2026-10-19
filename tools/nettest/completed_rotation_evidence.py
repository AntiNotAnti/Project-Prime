"""Opt-in completed applied-client rotation audit. Legacy smoke remains separate.

This consumes owner-thread RotationLifecycle witnesses and a post-exit
production-reader archive sidecar. Missing witnesses remain INCOMPLETE; legacy
server labels cannot establish completed applied-client rotations.
Synthetic test fixtures prove only this consumer's refusal/identity contracts.
"""
import re

SHA = re.compile(r"[0-9a-f]{64}\Z")
LIFECYCLE_SCOPE = "OwnerThreadAppliedRotationV1"
ARCHIVE_SCOPE = "ProductionReaderFinalizedRotationV1"


def positive(value):
    return type(value) is int and value > 0


def identity(row, require_zero_policy=True):
    if not isinstance(row, dict) or not all(positive(row.get(k)) for k in ("Epoch", "Match")):
        return None
    generation = row.get("StartGeneration")
    if type(generation) is not int or generation < 0:
        return None
    # Initial Continuous worlds precede NetMatchStart.Begin and legitimately
    # retain generation zero. Only explicit live session metadata establishes
    # that case; missing policy/stage is never treated as a legacy exemption.
    if generation == 0 and require_zero_policy and (
            row.get("SessionPolicy") != "Continuous" or row.get("StartStage") != "None"):
        return None
    room = row.get("Room")
    if not isinstance(room, str) or not room.strip():
        return None
    if row["Epoch"] > 2**64 - 1 or row["Match"] > 65535 or row["StartGeneration"] > 2**32 - 1:
        return None
    return row["Epoch"], row["Match"], row["StartGeneration"], room


def completed_rotation_coverage(reports, processes, required_completed_matches, archive_inspection=None,
                                expected_client_sha256=None):
    """Return status/gaps for N completed matches and an applied successor world.

    No late/departing client exemption is implicit. Every actual client process
    must supply the requested sequence; membership-specific turnover campaigns
    need a separately declared contract rather than weakening this denominator.
    """
    gaps = []
    if not positive(required_completed_matches):
        return "INCOMPLETE", ["required_completed_matches must be a positive integer"]
    names = [p.get("name") for p in processes]
    if len(set(names)) != len(names) or names.count("server") != 1 or len(names) < 2:
        return "INCOMPLETE", ["unique authority and actual client process identities required"]
    if not isinstance(reports, dict) or set(reports) != set(names) or any(not isinstance(row, dict) for row in reports.values()):
        return "INCOMPLETE", ["every process must supply an applied-world report"]
    if any(p.get("exit") != 0 or p.get("timedOut") or p.get("unexpectedExit") for p in processes):
        gaps.append("every process must exit normally without timeout")
    events = {}
    for name in names:
        witness = reports[name].get("RotationLifecycle")
        if not isinstance(witness, dict) or type(witness.get("Schema")) is not int or witness.get("Schema") != 1 or witness.get("Scope") != LIFECYCLE_SCOPE:
            gaps.append(name + ": missing owner-thread completed-rotation witness")
            continue
        rows = witness.get("Events")
        if type(witness.get("Overflow")) is not int or witness["Overflow"] != 0 or not isinstance(rows, list):
            gaps.append(name + ": missing/overflowed lifecycle event census")
            continue
        allowed = {"authority-started", "authority-completed"} if name == "server" else {"client-applied-confirmed", "client-completed"}
        seen = set()
        sequence = 0
        valid = True
        performance_gaps = []
        for row in rows:
            key = identity(row)
            stage = row.get("Stage") if isinstance(row, dict) else None
            number = row.get("Sequence") if isinstance(row, dict) else None
            if key is None or stage not in allowed or not positive(number) or number <= sequence or (key, stage) in seen:
                valid = False
                break
            seen.add((key, stage)); sequence = number
            if not positive(row.get("AuthorityFrame")):
                valid = False
                break
            if name != "server" and (type(row.get("Slot")) is not int or not 0 <= row["Slot"] < 8
                    or not positive(row.get("Generation")) or not positive(row.get("BootstrapRevision"))
                    or any(row.get(k) is not True for k in ("AppliedWorld", "GameplayReady", "WorldAdmissionConfirmed"))):
                valid = False
                break
            if stage.endswith("completed") and (row.get("Completed") is not True
                    or not isinstance(row.get("EndReason"), str) or not row["EndReason"].strip()):
                valid = False
                break
            if name == "server" and stage == "authority-completed" and (
                    not positive(row.get("SimFrames")) or row["SimFrames"] < 60
                    or any(type(row.get(k)) is not int or row[k] < 0 for k in ("StepFailures", "DroppedSteps", "Stalls"))):
                valid = False
                break
            if name == "server" and stage == "authority-completed":
                for metric in ("StepFailures", "DroppedSteps", "Stalls"):
                    if row[metric] != 0:
                        performance_gaps.append(
                            f"{name}: authority performance gate {metric}={row[metric]} "
                            f"for Epoch={key[0]} Match={key[1]} StartGeneration={key[2]} Room={key[3]}")
        if not valid:
            gaps.append(name + ": malformed, duplicate, reordered or incomplete lifecycle identity")
        else:
            # Keep structurally valid observations available for exact world and
            # archive coverage checks. Performance violations still fail closed,
            # but must not disguise valid identities as malformed metadata.
            events[name] = rows
            gaps.extend(performance_gaps)
    if "server" not in events:
        return "INCOMPLETE", gaps
    authority = events["server"]
    starts = [row for row in authority if row["Stage"] == "authority-started"]
    completions = [row for row in authority if row["Stage"] == "authority-completed"]
    count = required_completed_matches
    if len(completions) < count or len(starts) < count + 1:
        gaps.append("authority: fewer explicit completed matches and applied successor starts than required")
        return "INCOMPLETE", gaps
    required = [identity(row) for row in completions[:count]]
    applied = [identity(row) for row in starts[:count + 1]]
    if len(set(applied)) != count + 1 or required != applied[:count]:
        gaps.append("authority: completed identities do not match its ordered started-world prefix")
    for index, complete in enumerate(completions[:count]):
        start, successor = starts[index], starts[index + 1]
        if not start["Sequence"] < complete["Sequence"] < successor["Sequence"] or complete["AuthorityFrame"] - start["AuthorityFrame"] < 60:
            gaps.append("authority: match completion lacks ordered positive native activity before its successor")
    for name in names:
        if name == "server" or name not in events:
            continue
        rows = events[name]
        admissions = [row for row in rows if row["Stage"] == "client-applied-confirmed"]
        endings = [row for row in rows if row["Stage"] == "client-completed"]
        if [identity(row) for row in admissions[:count + 1]] != applied or [identity(row) for row in endings[:count]] != required:
            gaps.append(name + ": actual applied/completed world sequence differs from authority or lacks successor")
            continue
        for index, complete in enumerate(endings[:count]):
            start, successor = admissions[index], admissions[index + 1]
            if (not start["Sequence"] < complete["Sequence"] < successor["Sequence"]
                    or complete["AuthorityFrame"] - start["AuthorityFrame"] < 60
                    or any(complete[k] != start[k] for k in ("Slot", "Generation", "BootstrapRevision"))):
                gaps.append(name + ": completed world is not the same confirmed local incarnation/admission")
    archives = archive_inspection
    if (not isinstance(archives, dict) or type(archives.get("Schema")) is not int or archives.get("Schema") != 1 or archives.get("Scope") != ARCHIVE_SCOPE
            or not isinstance(expected_client_sha256, str) or not SHA.fullmatch(expected_client_sha256)
            or archives.get("ProducerSha256") != expected_client_sha256 or archives.get("ReaderSha256") != expected_client_sha256
            or not isinstance(archives.get("Files"), list)):
        gaps.append("missing source-bound finalized production-reader archive inspections")
    else:
        covered = {name: set() for name in names}
        paths = set()
        for file in archives["Files"]:
            if not isinstance(file, dict):
                gaps.append("malformed archive inspection row"); continue
            process, path, before, after = (file.get(k) for k in ("Process", "Path", "Sha256Before", "Sha256After"))
            matches = file.get("CompletedMatches")
            if (process not in covered or not isinstance(path, str) or not path or path in paths
                    or not isinstance(before, str) or not SHA.fullmatch(before) or after != before
                    or file.get("Health") != "Healthy" or file.get("FullRecordRead") is not True
                    or file.get("Finalized") is not True or not isinstance(matches, list)
                    or any(identity(row, require_zero_policy=False) is None for row in matches)):
                gaps.append("malformed, changed, unfinalized or unbound archive inspection")
                continue
            paths.add(path)
            # Archive rows are only identities. A zero generation here is useful
            # solely when it exactly matches an already-validated authority
            # completion carrying the explicit Continuous/None witness.
            keys = [identity(row, require_zero_policy=False) for row in matches]
            if len(set(keys)) != len(keys):
                gaps.append("archive repeats a completed identity"); continue
            all_completed = [identity(row) for row in completions]
            if any(key not in all_completed for key in keys) or [all_completed.index(key) for key in keys] != sorted(all_completed.index(key) for key in keys):
                gaps.append("archive completed identities do not follow authority completion order"); continue
            covered[process].update(keys)
        for name in names:
            if not set(required).issubset(covered[name]):
                gaps.append(name + ": finalized archives do not cover every required completed world")
    return ("INCOMPLETE" if gaps else "PASS"), gaps
