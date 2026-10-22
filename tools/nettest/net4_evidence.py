#!/usr/bin/env python3
"""Read-only N4 instrumentation audit. Prints JSON; never modifies campaign evidence.

PASS means complete, internally consistent instrumentation, not enforcement readiness
or an honest-player false-reject result. Run: python net4_evidence.py CAMPAIGN_DIR
"""
import argparse
from collections import Counter, defaultdict
import hashlib
import json
import math
from pathlib import Path
import re

STATUSES = ("Verified", "Rejected", "Unavailable")
FAMILIES = {"Straight", "Projectile", "Splash", "Ricochet", "Homing", "Child",
            "Continuous", "Turret", "BombOrMelee"}
STAGES = {"arrival", "authority-match", "settlement"}
REASONS = """Verified UnknownShot ShotNotFired WrongLife WeaponMismatch InvalidRay StaleAck
SourceUnavailable SourceChanged UnownedWeapon InvalidCharge FireRate Ammunition
ModifierMismatch SourcePoseUnavailable MuzzleDistance BlockedMuzzle UnknownComponent
HistoryUnavailable MissedBody BlockedWorld WrongHeadshot Reach PhysicalWitnessRequired
UnsupportedContinuous UnsupportedTurret UnsupportedBombOrMelee DuplicateComponent
DamageMismatch Capacity ContextConflict SourceInputUnavailable SourceHistoryIncomplete
UnverifiedModifier ClaimEffectsMismatch Unexamined SourceClock SourceAimUnavailable""".split()
KEY_FIELDS = ("AuthorityEpoch", "MatchId", "ShooterSlot", "Generation", "LifeId", "ShotId")
REJECT_REASONS = set("""UnownedWeapon InvalidCharge FireRate Ammunition ModifierMismatch
InvalidRay MuzzleDistance BlockedMuzzle MissedBody BlockedWorld WrongHeadshot Reach
DamageMismatch ClaimEffectsMismatch SourceClock""".split())
CLAIM_REJECT_REASONS = REJECT_REASONS | {"WrongLife", "StaleAck", "WeaponMismatch", "DuplicateComponent"}


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def integer(value):
    return isinstance(value, int) and not isinstance(value, bool) and value >= 0


def finite(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def dimensions(rows):
    return {field: dict(sorted(Counter(row[field] for row in rows).items()))
            for field in ("Status", "Reason", "Source", "Geometry", "Family")}


def summarize(campaign):
    campaign = Path(campaign)
    failures, incomplete = [], []
    result = {"instrumentationStatus": "INCOMPLETE", "failures": failures, "incomplete": incomplete,
              "campaign": str(campaign.resolve()), "enforcementReadiness": "NOT_ESTABLISHED",
              "honestFalseRejectRate": "NOT_ESTABLISHED",
              "interpretation": ["PASS checks evidence completeness and internal consistency only.",
                 "Scripted fixtures mutate loadouts; rejected observations are not honest-player false rejects.",
                 "Claim stages repeat observations. Radius samples are observational counts, not independent shots."]}

    def read(name):
        try:
            def invalid_constant(value):
                raise ValueError(f"nonfinite JSON constant {value}")
            value = json.loads((campaign / name).read_text(encoding="utf-8"), parse_constant=invalid_constant)
            if not isinstance(value, dict):
                failures.append(f"{name}: root must be an object")
                return {}
            return value
        except FileNotFoundError:
            incomplete.append(f"missing {name}")
        except (OSError, ValueError) as error:
            failures.append(f"{name}: {error}")
        return {}

    server, manifest = read("server.json"), read("manifest.json")
    result["identity"] = {"serverAssemblySha256": manifest.get("assemblySha256"),
                          "serverAssemblyAfterSha256": manifest.get("assemblyAfterSha256"),
                          "campaignHarnessSha256": manifest.get("harnessSha256"),
                          "source": manifest.get("source"), "protocol": server.get("Protocol"),
                          "serverVersion": server.get("Version"),
                          "summarizerSha256": digest(Path(__file__))}
    if (campaign / "server.json").is_file():
        result["identity"]["serverEvidenceSha256"] = digest(campaign / "server.json")
    hashes = manifest.get("harnessSha256")
    sha = manifest.get("assemblySha256")
    valid_sha = lambda value: isinstance(value, str) and re.fullmatch(r"[0-9a-fA-F]{64}", value) is not None
    if not valid_sha(sha) or not isinstance(hashes, dict) or not hashes or not all(valid_sha(v) for v in hashes.values()):
        incomplete.append("manifest lacks valid server assembly/harness SHA256 identities")
    after = manifest.get("assemblyAfterSha256")
    if not valid_sha(after):
        incomplete.append("manifest lacks final server assembly SHA256")
    elif valid_sha(sha) and after != sha:
        failures.append("server assembly changed during campaign")
    if server.get("Overflow") != 0:
        (incomplete if "Overflow" not in server else failures).append("server evidence overflow absent or nonzero")
    proof = server.get("ShotProof")
    if not isinstance(proof, dict):
        incomplete.append("server ShotProof missing")
        result["instrumentationStatus"] = "FAIL" if failures else "INCOMPLETE"
        return result
    result["mode"] = proof.get("Mode")
    if not isinstance(proof.get("Mode"), str) or proof["Mode"] not in {"Off", "Shadow", "Enforce"}:
        failures.append("invalid ShotProof mode")
    if proof.get("Mode") == "Off":
        incomplete.append("ShotProof instrumentation was off")
    names = proof.get("ReasonNames")
    if names != REASONS:
        failures.append("ReasonNames do not match supported N4 enum order; update this audit for a new schema")
    for name in ("CapacityRefused", "EvidenceOverflow", "PhysicalDuplicates", "RadiusSamples",
                 "LegacyAllowedRejected", "LegacyAllowedUnavailable"):
        if name not in proof:
            incomplete.append(f"missing counter {name}")
        elif not integer(proof[name]):
            failures.append(f"invalid counter {name}")
    for name in ("CapacityRefused", "EvidenceOverflow"):
        if integer(proof.get(name)) and proof[name] != 0:
            failures.append(f"{name} is nonzero; proof evidence is incomplete")

    def audit_rows(prefix):
        row_name, reason_name = ("EmissionRows", "EmissionReasons") if prefix else ("Rows", "Reasons")
        rows, reasons = proof.get(row_name), proof.get(reason_name)
        counts = {status: proof.get(prefix + status) for status in STATUSES}
        for status, count in counts.items():
            if count is None:
                incomplete.append(f"missing counter {prefix + status}")
            elif not integer(count):
                failures.append(f"invalid counter {prefix + status}")
        if not isinstance(rows, list):
            incomplete.append(f"missing array {row_name}")
            rows = []
        if not isinstance(reasons, list) or len(reasons) != len(REASONS) or not all(integer(x) for x in reasons):
            failures.append(f"invalid reason counters {reason_name}")
            reasons = None
        valid = []
        for index, row in enumerate(rows):
            label = f"{row_name}[{index}]"
            if not isinstance(row, dict):
                failures.append(f"{label}: row must be an object")
                continue
            required = {"Key", "AuthorityFrame", "Status", "Reason"}
            if not prefix:
                required |= {"Stage", "ClaimId", "ComponentId", "VictimSlot", "VictimGeneration",
                             "VictimLifeId", "Beam", "Flags", "Damage", "AckFrame", "LaunchFrame",
                             "Legacy", "Family", "Source", "Geometry", "PhysicalMatch", "BodyOffset"}
            if required - row.keys():
                incomplete.append(f"{label}: missing {', '.join(sorted(required - row.keys()))}")
                continue
            key = row["Key"]
            if not isinstance(key, dict) or any(not integer(key.get(k)) for k in KEY_FIELDS):
                failures.append(f"{label}: incomplete or invalid full ShotKey")
                continue
            numeric = {"AuthorityFrame"} if prefix else {"AuthorityFrame", "ClaimId", "ComponentId", "VictimSlot",
                      "VictimGeneration", "VictimLifeId", "Beam", "Flags", "Damage", "AckFrame", "LaunchFrame", "Legacy"}
            if any(not integer(row[k]) for k in numeric) or key["ShooterSlot"] >= 8:
                failures.append(f"{label}: invalid integer identity/metadata")
                continue
            if not isinstance(row["Status"], str) or not isinstance(row["Reason"], str) or row["Status"] not in STATUSES or row["Reason"] not in REASONS:
                failures.append(f"{label}: invalid status/reason enum")
                continue
            if not prefix and (any(not isinstance(row[k], str) for k in ("Stage", "Family", "Source", "Geometry"))
                    or row["Stage"] not in STAGES or row["Family"] not in FAMILIES
                    or row["Source"] not in REASONS or row["Geometry"] not in REASONS
                    or not isinstance(row["PhysicalMatch"], bool) or not finite(row["BodyOffset"])):
                failures.append(f"{label}: invalid stage/dimension enum or physical/radius metadata")
                continue
            expected = "Verified" if row["Reason"] == "Verified" else "Rejected" if row["Reason"] in (
                REJECT_REASONS if prefix else CLAIM_REJECT_REASONS) else "Unavailable"
            # UnknownShot is pending evidence at arrival, rejection at final evaluation.
            if not prefix and row["Reason"] == "UnknownShot":
                expected = "Unavailable" if row["Stage"] == "arrival" else "Rejected"
            if row["Status"] != expected:
                failures.append(f"{label}: reason/status correspondence differs")
            if not prefix and row["Status"] == "Verified" and (not row["PhysicalMatch"]
                    or row["Source"] != "Verified" or row["Geometry"] != "Verified"):
                failures.append(f"{label}: Verified lacks physical match and verified source/geometry")
            valid.append(row)
        observed = Counter(row["Status"] for row in valid)
        if all(integer(x) for x in counts.values()) and dict(observed) != {k: v for k, v in counts.items() if v}:
            failures.append(f"{row_name}: rows do not cover status counters")
        if reasons is not None and [Counter(row["Reason"] for row in valid)[r] for r in REASONS] != reasons:
            failures.append(f"{row_name}: rows do not cover reason counters")
        result["emission" if prefix else "claimObservations"] = {"counts": counts, "rows": len(rows),
             "reasonCounts": dict(zip(REASONS, reasons or []))}
        return valid

    rows, emission = audit_rows(""), audit_rows("Emission")
    if not rows or not emission:
        incomplete.append("campaign needs nonempty claim and emission observations")
    stages = defaultdict(list)
    claims = defaultdict(list)
    for row in rows:
        stages[row["Stage"]].append(row)
        claims[tuple(row["Key"][k] for k in KEY_FIELDS) + (row["ClaimId"],)].append(row)
    for key, observations in claims.items():
        targets = {tuple(row[k] for k in ("ComponentId", "VictimSlot", "VictimGeneration", "VictimLifeId", "Flags")) for row in observations}
        if len(targets) != 1:
            failures.append(f"claim {key}: component/victim/life/kind changed across stages")
    latest = [observations[-1] for observations in claims.values()]
    result["stages"] = {stage: {"observations": len(group), **dimensions(group)} for stage, group in sorted(stages.items())}
    result["uniqueClaims"] = {"count": len(claims), "definition": "full ShotKey plus ClaimId",
         "summaryBasis": "last recorded observation per claim, not terminal damage verdict",
         "latest": dimensions(latest), "observationsPerClaim": dict(sorted(Counter(map(len, claims.values())).items()))}
    result["allVerifiedHavePhysicalMatch"] = all(row["PhysicalMatch"] and row["Source"] == row["Geometry"] == "Verified"
                                               for row in rows if row["Status"] == "Verified")
    offsets = [row["BodyOffset"] for row in rows if row["BodyOffset"] >= 0]
    radius = proof.get("RadiusSamples")
    buckets = proof.get("RadiusBuckets")
    total, maximum = proof.get("RadiusSum"), proof.get("RadiusMax")
    if not isinstance(buckets, list) or len(buckets) != 9 or not all(integer(x) for x in buckets):
        failures.append("invalid RadiusBuckets")
    elif integer(radius) and sum(buckets) != radius:
        failures.append("radius bucket counts differ from RadiusSamples")
    if not finite(total) or total < 0 or not finite(maximum) or maximum < 0:
        failures.append("invalid radius sum/max")
    elif integer(radius) and (len(offsets) != radius or not math.isclose(sum(offsets), total, rel_tol=1e-5, abs_tol=1e-4)
                            or not math.isclose(max(offsets, default=0), maximum, rel_tol=1e-5, abs_tol=1e-4)):
        failures.append("radius rows do not cover samples/sum/max")
    if isinstance(buckets, list) and all(integer(x) for x in buckets) and len(buckets) == 9:
        if [Counter(min(8, int(x * 4)) for x in offsets)[i] for i in range(9)] != buckets:
            failures.append("radius rows do not cover bucket counts")
    result["radiusObservations"] = {"samples": radius, "sum": total, "maximum": maximum, "buckets": buckets,
         "bucketDefinition": "floor(offset * 4), capped at 8; final bucket includes all offsets >= 2",
         "independentHonestShotDistribution": "NOT_ESTABLISHED"}
    for name, status in (("LegacyAllowedRejected", "Rejected"), ("LegacyAllowedUnavailable", "Unavailable")):
        if integer(proof.get(name)) and proof[name] != sum(row["Legacy"] == 0 and row["Status"] == status for row in rows):
            failures.append(f"{name}: rows do not cover counter")
    result["instrumentationStatus"] = "FAIL" if failures else "INCOMPLETE" if incomplete else "PASS"
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("campaign", type=Path)
    args = parser.parse_args()
    result = summarize(args.campaign)
    print(json.dumps(result, indent=2, allow_nan=False))
    return 0 if result["instrumentationStatus"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
