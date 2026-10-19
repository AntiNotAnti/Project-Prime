#!/usr/bin/env python3
"""Explicit seven-Hunter transition evidence; legacy Weavel audit remains unchanged.

The native-owner parser/provenance contract is derived from the reviewed RML75
auditor (SHA256 197cf1a613aeee7015c490ff763dbecc1a1c27b3135679643b764abb8816cd71).
No accepted-source owner row can substitute for an actual OwnerForms V1 sample.
"""
import argparse
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import struct
from typing import NamedTuple

_legacy_path = Path(__file__).with_name("form-audit.py")
LEGACY_SHA256 = "6c1f28fc10408a4d2f65d2cc97ab6a89a2aad966c47e538b98a0ad121faedc3d"
if hashlib.sha256(_legacy_path.read_bytes()).hexdigest() != LEGACY_SHA256:
    raise RuntimeError("legacy Weavel auditor changed; review before changing generic audit policy")
_spec = importlib.util.spec_from_file_location("hunter_form_legacy", _legacy_path)
legacy = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(legacy)
integer, newer, vector, decode = legacy.integer, legacy.newer, legacy.vector, legacy.decode
IDENTITY, CYCLE = legacy.IDENTITY, ("Morphing", "Alt", "Unmorphing", "Biped")
HUNTERS = ("Samus", "Kanden", "Trace", "Sylux", "Noxus", "Spire", "Weavel")
HUNTER_SCOPE = "NativeScenePlayerHunterAtFormCaptureV1"
OWNER_SCOPE = "LiveLocalOwnerNativeAfterSimulationV1"
OWNER_UNIT = "One native local-owner sample per processed network frame; independent of accepted-source Forms"
MARKERS = ("OwnerFormScope", "OwnerFormCountingUnit", "OwnerForms")
CAPTURE_PATH = "src/MphRead/Mods/Network/NetCampaignEvidence.cs"
NATIVE_FIELDS = set(IDENTITY) | {"Stage", "Frame", "SceneFrame", "Hunter", "NativeForm", "Phase",
    "BodyHp", "TurretHp", "TurretActive", "X", "Y", "Z", "BodyX", "BodyY", "BodyZ", "LocalSlot", "Spectating"}


class OwnerProvenance(NamedTuple):
    clientDllSha256: str
    sourceManifestSha256: str
    captureSourceSha256: str


def owner_provenance(value):
    if type(value) is not dict or set(value) != set(OwnerProvenance._fields):
        raise ValueError("exact independently supplied owner provenance pins required")
    if any(type(v) is not str or not re.fullmatch("[0-9a-f]{64}", v) for v in value.values()):
        raise ValueError("invalid expected SHA256")
    return OwnerProvenance(**value)


def check_provenance(expected, source_bytes, manifest):
    if type(expected) is not OwnerProvenance:
        raise ValueError("explicit independently pinned native-owner provenance required")
    owner_provenance(expected._asdict())
    if type(source_bytes) is not bytes or hashlib.sha256(source_bytes).hexdigest() != expected.sourceManifestSha256:
        raise ValueError("source-manifest bytes/hash mismatch")
    source = json.loads(source_bytes)
    captures = [row for row in source["files"] if row.get("path") == CAPTURE_PATH]
    if len(captures) != 1 or captures[0].get("sha256") != expected.captureSourceSha256:
        raise ValueError("pinned native capture source absent or conflicting")
    head = source["head"]
    if type(head) is not str or not re.fullmatch("[0-9a-f]{40}", head):
        raise ValueError("invalid source manifest head")
    identity = head + "+dirty-manifest:" + expected.sourceManifestSha256
    if manifest.get("source") != identity or manifest.get("settings", {}).get("source", identity) != identity:
        raise ValueError("campaign source identity does not match independent pins")
    if manifest.get("assemblySha256") != expected.clientDllSha256 or manifest.get("assemblyAfterSha256") != expected.clientDllSha256:
        raise ValueError("campaign before/after client DLL identity differs from independent pins")
    return expected._asdict()


def membership(peer):
    result = {}
    for row in peer["Windows"]:
        key = (integer(row["Epoch"], 1, 2**64-1), integer(row["Match"], 1, 65535))
        slot = integer(row["LocalSlot"], 0, 7)
        if key in result and result[key] != slot:
            raise ValueError("conflicting local-slot membership")
        result[key] = slot
    return result


def native_rows(report):
    if not all(field in report for field in MARKERS):
        raise ValueError("missing/mixed native-owner schema markers")
    if report["OwnerFormScope"] != OWNER_SCOPE or report["OwnerFormCountingUnit"] != OWNER_UNIT:
        raise ValueError("invalid native-owner scope/counting unit")
    rows = report["OwnerForms"]
    if rows is not None and type(rows) is not list:
        raise ValueError("OwnerForms must be an exact list or null")
    return rows or []


def native_row(row):
    if type(row) is not dict or set(row) != NATIVE_FIELDS or row["Stage"] != "owner-native-after-simulation":
        raise ValueError("native-owner schema/stage mismatch; accepted-source rows are not native evidence")
    frame = integer(row["Frame"])
    integer(row["SceneFrame"], 0, 2**64-1)
    if type(row["Spectating"]) is not bool or row["Spectating"]:
        raise ValueError("native owner must be a non-spectator")
    if integer(row["LocalSlot"], 0, 7) != integer(row["Slot"], 0, 7):
        raise ValueError("native owner local-slot mismatch")
    if row["Hunter"] not in (*HUNTERS, "Guardian", "Random"):
        raise ValueError("invalid native Hunter enum")
    raw = row["NativeForm"]
    if type(raw) is not str or not re.fullmatch("[0-9a-fA-F]{56}", raw):
        raise ValueError("invalid exact native form bytes")
    key, _, form = decode(dict(row, Source=raw, ObservedFrame=frame))
    if frame != form["frame"] or form["damage"] != 0 or row["BodyHp"] <= 0:
        raise ValueError("native form frame/default damage revision/alive mismatch")
    if (row["Phase"], row["BodyHp"], row["TurretHp"], row["TurretActive"]) != (form["phase"], form["body_hp"], form["hp"], form["active"]):
        raise ValueError("native bytes disagree with typed phase/health/presence")
    if vector(row, ("X", "Y", "Z")) != form["anchor"]:
        raise ValueError("native bytes disagree with typed anchor")
    vector(row, ("BodyX", "BodyY", "BodyZ"))
    return key, form


def progress_cycle(progress, completed, key, phase):
    step = progress.get(key, 0)
    if phase == CYCLE[step]:
        step += 1
        if step == len(CYCLE):
            completed.add(key)
            step = 0
    progress[key] = step


def audit(server, peers, summary, manifest, hunter=None, owner_slot=1, *, expected_owner_provenance=None, source_manifest_bytes=None):
    if hunter is None:
        return legacy.audit(server, peers, summary, manifest, owner_slot)
    result = copy.deepcopy(legacy.audit(server, peers, summary, manifest, owner_slot)) if hunter == "Weavel" else dict(
        status="INCOMPLETE", failures=[], incomplete=[], metrics={})
    failures, missing, metrics = result["failures"], result["incomplete"], result["metrics"]
    result["hunter"] = hunter
    result["scope"] = "Explicit native-owner, authority and two independent-observer same-life transition cycles; gameplay health/score/drain retained; no attack, pixel, replay-playback or device acceptance"
    try:
        if hunter not in HUNTERS:
            raise ValueError("explicit supported Hunter required")
        integer(owner_slot, 0, 7)
        settings = manifest.get("settings", manifest)
        if settings.get("scenario") != "alt-morph" or settings.get("form_hunter", "Weavel") != hunter:
            raise ValueError("selected Hunter/scenario differs from explicit dispatch")
        names = [row["name"] for row in summary["processes"]]
        if len(names) != len(set(names)) or "server" not in names or set(names) - {"server"} != set(peers):
            raise ValueError("complete unique process evidence required")
        if summary.get("status") == "FAIL": failures.append("base campaign failed")
        elif summary.get("status") != "PASS": missing.append("base campaign did not pass")
        if summary.get("coverage", {}).get("boundedHeadlessDrain") != "PASS": missing.append("bounded campaign drain did not pass")
        if any(report.get("Overflow", 0) for report in (server, *peers.values())):
            raise ValueError("campaign evidence overflow")
        check_provenance(expected_owner_provenance, source_manifest_bytes, manifest)
        authority, lives, authority_complete, authority_progress, authority_last = {}, set(), set(), {}, {}
        authority_previous_rows, repeated_authority_observations = {}, 0
        hunter_missing = False
        setup_rows, selected_lives = {}, {}
        def captured_hunter(report, row, process, key, source_phase):
            nonlocal hunter_missing
            marker = report.get("FormHunterScope")
            if marker is not None and marker != HUNTER_SCOPE:
                raise ValueError("unknown native Hunter capture scope")
            if marker is None or row.get("Hunter") is None:
                hunter_missing = True
                return False
            if row["Hunter"] != hunter:
                # Native slots begin as Biped Samus before the selected Hunter's
                # resources are installed. These rows cannot contribute to a
                # transition cycle; a mismatch during/after selection is fatal.
                if row["Hunter"] in HUNTERS and row["Phase"] == source_phase == "Biped" and key not in selected_lives.setdefault(process, set()):
                    setup_rows[process] = setup_rows.get(process, 0) + 1
                    return False
                raise ValueError("actual native target Hunter or capture scope disagrees with explicit selection")
            selected_lives.setdefault(process, set()).add(key)
            return True
        for row in server["Forms"]:
            key, raw, form = decode(row)
            if row["Stage"] != "authority" or (row["Phase"], row["BodyHp"], row["TurretHp"], row["TurretActive"], vector(row, ("X", "Y", "Z"))) != (
                    form["phase"], form["body_hp"], form["hp"], form["active"], form["anchor"]):
                raise ValueError("authority form differs from exact source")
            if key in authority_previous_rows and row == authority_previous_rows[key]:
                # Form() also observes repeated serializations of the current
                # state. Only the immediately previous complete row for this
                # life may repeat; it supplies no additional cycle activity.
                repeated_authority_observations += 1
                continue
            if key in authority_last and (not newer(form["frame"], authority_last[key]["frame"])
                    or form["started"] != authority_last[key]["started"] and not newer(form["started"], authority_last[key]["started"])):
                raise ValueError("duplicate/stale authority form stream")
            authority_last[key] = form
            authority_previous_rows[key] = row
            authority[key, raw] = form
            lives.add(key)
            if key[2] == owner_slot:
                selected = captured_hunter(server, row, "server", key, form["phase"])
                if hunter != "Weavel" and (form["active"] or form["hp"]):
                    raise ValueError("non-Weavel target cannot own a Weavel turret")
                if selected: progress_cycle(authority_progress, authority_complete, key, form["phase"])
        if not authority_complete: missing.append("authority ordered same-life target cycle incomplete")
        if any(field in server for field in MARKERS) and native_rows(server):
            raise ValueError("authority cannot emit native local-owner samples")
        native_complete, observer_complete, owner_processes = set(), {}, set()
        peer_metrics = {}
        for name, peer in peers.items():
            slots = membership(peer)
            if peer.get("Role") not in ("simulation-client", "rendered-client"):
                raise ValueError(name + ": unsupported actual client role")
            target_count, last, seen, observer_progress, completed, local_seen = 0, {}, set(), {}, set(), set()
            for row in peer["Forms"]:
                key, raw, form = decode(row)
                if key[:2] not in slots or (key, raw) not in authority:
                    raise ValueError(name + ": complete identity/source not joined to authority and membership")
                if (key, raw) in seen or key in last and (not newer(form["frame"], last[key]["frame"])
                        or form["started"] != last[key]["started"] and not newer(form["started"], last[key]["started"])):
                    raise ValueError(name + ": duplicate/stale applied form stream")
                seen.add((key, raw)); last[key] = form
                local = key[2] == slots[key[:2]]
                role = (row.get("LocalSlot"), row.get("Spectating"), row.get("AuthoritySpectating"))
                if type(role[0]) is not int or role[0] != slots[key[:2]] or any(type(v) is not bool for v in role[1:]):
                    raise ValueError(name + ": exact sample-time membership/spectator metadata required")
                owner = local and not role[1] and not role[2]
                bootstrap = local and key[:2] not in local_seen and row["Stage"] == "observer"
                if row["Stage"] != ("owner" if owner else "observer") and not bootstrap:
                    raise ValueError(name + ": applied owner/observer role mismatch")
                if local: local_seen.add(key[:2])
                if not owner and row["Phase"] != form["phase"]:
                    raise ValueError(name + ": independent observer phase disagrees with exact source")
                if key[2] != owner_slot: continue
                target_count += 1
                selected = captured_hunter(peer, row, name, key, form["phase"])
                if hunter != "Weavel" and (row["TurretActive"] or row["TurretHp"] or form["active"] or form["hp"]):
                    raise ValueError("non-Weavel target has observed or source turret presence/health")
                if selected and not local:
                    progress_cycle(observer_progress, completed, key, row["Phase"])
            observer_complete[name] = completed
            if not any(field in peer for field in MARKERS):
                missing.append(name + ": actual native-owner schema absent")
                rows = []
            else:
                rows = native_rows(peer)
            native_last, native_progress, native_target = {}, {}, 0
            local_completed = set()
            for row in rows:
                key, form = native_row(row)
                if key[:2] not in slots or slots[key[:2]] != key[2] or key not in lives:
                    raise ValueError(name + ": native owner identity not joined to actual membership/authority life")
                if key in native_last:
                    prior_frame, prior_scene, prior_start = native_last[key]
                    if not newer(form["frame"], prior_frame) or row["SceneFrame"] <= prior_scene or (
                            form["started"] != prior_start and not newer(form["started"], prior_start)):
                        raise ValueError(name + ": stale native Frame/SceneFrame/transition")
                native_last[key] = form["frame"], row["SceneFrame"], form["started"]
                if key[2] != owner_slot: continue
                if row["Hunter"] != hunter: raise ValueError("actual native owner Hunter differs from selected target")
                native_target += 1
                owner_processes.add(name)
                progress_cycle(native_progress, local_completed, key, row["Phase"])
            native_complete |= local_completed
            if owner_slot in slots.values() and not local_completed:
                missing.append(name + ": actual native-owner ordered same-life cycle incomplete")
            if hunter == "Weavel" and local_completed:
                reason = name + ": ordered same-life morph/alt/unmorph/settled cycle incomplete"
                missing[:] = [item for item in missing if item != reason]
            peer_metrics[name] = dict(appliedTargetSamples=target_count, nativeTargetSamples=native_target,
                nativeCompletedLives=[list(key) for key in sorted(local_completed)],
                independentObserverCompletedLives=[list(key) for key in sorted(completed)])
        if hunter_missing:
            missing.append("actual authority/observer target Hunter identity absent; require FormHunterScope and captured Hunter producer fields")
        eligible = authority_complete & native_complete
        shared = []
        for key in sorted(eligible):
            observers = sorted(name for name, keys in observer_complete.items() if key in keys and name not in owner_processes)
            if len(observers) >= 2: shared.append(dict(identity=list(key), independentObservers=observers))
        if not shared: missing.append("no target life has complete authority/native-owner and two independent-observer cycles")
        metrics["hunterTransitions"] = dict(ownerSlot=owner_slot, peers=peer_metrics, qualifiedLives=shared,
            ownerProvenance=expected_owner_provenance._asdict(), formHunterScope=HUNTER_SCOPE,
            preselectionBipedSetupRows=setup_rows,
            repeatedAuthorityObservations=repeated_authority_observations,
            acceptedSourceOwnerRowsAreNativeProof=False)
    except (KeyError, TypeError, ValueError, AttributeError, struct.error, OverflowError) as error:
        failures.append("Hunter transition evidence: " + str(error))
    result["incomplete"] = list(dict.fromkeys(missing))
    result["status"] = "FAIL" if failures else "INCOMPLETE" if missing else "PASS"
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("campaign", type=Path)
    parser.add_argument("--hunter", choices=HUNTERS, required=True)
    parser.add_argument("--owner-provenance", type=Path, required=True)
    parser.add_argument("--owner-source-manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    def load(name): return json.loads((args.campaign / name).read_text())
    summary = load("summary.json")
    names = [row["name"] for row in summary["processes"] if row["name"] != "server"]
    if any(type(name) is not str or Path(name).name != name for name in names):
        raise ValueError("unsafe process report name")
    peers = {name: load(name + ".json") for name in names}
    report = audit(load("server.json"), peers, summary, load("manifest.json"), args.hunter,
        expected_owner_provenance=owner_provenance(json.loads(args.owner_provenance.read_text())),
        source_manifest_bytes=args.owner_source_manifest.read_bytes())
    text = json.dumps(report, indent=2) + "\n"
    if args.output:
        with args.output.open("x") as stream: stream.write(text)
    else: print(text, end="")
    return 0 if report["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
