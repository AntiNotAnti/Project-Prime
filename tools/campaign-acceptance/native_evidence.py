#!/usr/bin/env python3
"""Generate and validate honest CAMP-07 human/native-device acceptance receipts.

A synthetic self-test validates the checker, not the campaign. The real
manifest must contain reviewed physical evidence for all mandatory gates.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import tempfile

PLATFORMS = ("windows", "linux", "macos", "android")
PER_PLATFORM = (
    "cold-warm-boot",
    "new-slot-scan-and-visor",
    "board-ship-and-star-map",
    "unlock-travel-save-reload",
    "bad-and-good-gorea-endings",
    "movies-skip-fallback",
    "music-sfx-pause-resume",
    "map-and-controls",
    "memory-and-frame-pacing",
)
CROSS_SYSTEM = (
    "multiplayer-protocol-compatibility",
    "historical-replay-compatibility",
    "save-backup-and-rollback",
    "no-licensed-assets-in-build",
)
REQUIRED = tuple(
    f"{system}/{gate}" for system in PLATFORMS for gate in PER_PLATFORM
) + tuple(f"shared/{gate}" for gate in CROSS_SYSTEM)


def blank(commit: str) -> dict:
    return {
        "schema": 1,
        "commit": commit,
        "status": "NOT_RUN",
        "scope": "actual cartridge-backed in-game device verification",
        "cases": [
            {
                "id": item, "status": "NOT_RUN",
                "deviceOrConfiguration": "",
                "tester": "", "notes": "",
                "evidenceFile": "", "evidenceSha256": "",
            }
            for item in REQUIRED
        ],
    }


def validate(obj: object, expected_sha: str, root: Path) -> list[str]:
    errors = []
    if not isinstance(obj, dict) or obj.get("schema") != 1:
        return ["Unrecognized native evidence schema."]
    if not expected_sha or obj.get("commit") != expected_sha:
        errors.append("Native evidence belongs to a different commit.")
    rows = obj.get("cases")
    if not isinstance(rows, list):
        return errors + ["Expected a cases array."]
    seen = set()
    for row in rows:
        if not isinstance(row, dict):
            errors.append("Malformed evidence row.")
            continue
        case_id = row.get("id")
        if not isinstance(case_id, str) or case_id not in REQUIRED:
            errors.append(f"Unknown acceptance case: {case_id!r}")
            continue
        if case_id in seen:
            errors.append(f"Duplicate acceptance case: {case_id}")
        seen.add(case_id)
        if row.get("status") != "PASS":
            errors.append(f"{case_id}: not verified on a real device")
            continue
        if not row.get("deviceOrConfiguration") or not row.get("tester") or not row.get("notes"):
            errors.append(f"{case_id}: platform, tester and observations are required")
        file_name = row.get("evidenceFile")
        digest = row.get("evidenceSha256")
        if not isinstance(file_name, str) or not file_name:
            errors.append(f"{case_id}: missing evidence file")
        elif not isinstance(digest, str) or len(digest) != 64 or any(
            c not in "0123456789abcdefABCDEF" for c in digest
        ):
            errors.append(f"{case_id}: invalid evidence digest")
        else:
            evidence = (root / file_name).resolve()
            # Never accept a purported screenshot/recording that is itself
            # a cartridge/archive asset or other raw proprietary game data.
            if evidence.suffix.lower() in {
                ".nds", ".vx", ".bin", ".ncsf", ".minincsf", ".sdat",
                ".pak", ".pk3", ".ppmap", ".zip", ".7z", ".tar"
            }:
                errors.append(f"{case_id}: raw game assets cannot be evidence")
            elif not evidence.is_file():
                errors.append(f"{case_id}: proof file is not accessible")
            elif hashlib.sha256(evidence.read_bytes()).hexdigest() != digest.lower():
                errors.append(f"{case_id}: proof digest mismatch")
    for required in REQUIRED:
        if required not in seen:
            errors.append(f"{required}: mandatory acceptance missing")
    if obj.get("status") != "PASS":
        errors.append("Manifest is not explicitly marked PASS.")
    return errors


def self_test() -> None:
    with tempfile.TemporaryDirectory(prefix="prime-camp07-proof-") as temporary:
        root = Path(temporary)
        sample = root / "sample.txt"
        sample.write_text("Synthetic checker fixture, NOT a gameplay receipt.\n")
        digest = hashlib.sha256(sample.read_bytes()).hexdigest()
        good = blank("synthetic-test-sha")
        good["status"] = "PASS"
        for row in good["cases"]:
            row.update({
                "status": "PASS", "deviceOrConfiguration": "synthetic",
                "tester": "validator selftest", "notes": "synthetic data",
                "evidenceFile": "sample.txt", "evidenceSha256": digest,
            })
        assert not validate(good, "synthetic-test-sha", root)
        assert validate(good, "changed-head", root)
        good["cases"][0]["status"] = "NOT_RUN"
        assert validate(good, "synthetic-test-sha", root)
        good["cases"][0]["status"] = "PASS"
        good["cases"][0]["evidenceSha256"] = "0" * 64
        assert validate(good, "synthetic-test-sha", root)
        good["cases"][0]["evidenceSha256"] = digest
        good["cases"].append(dict(good["cases"][0]))
        assert validate(good, "synthetic-test-sha", root)
    print("CAMP-07 EVIDENCE CHECKER PASS (synthetic validation only; NO native tests run)")


def main() -> int:
    parser = argparse.ArgumentParser(description="CAMP-07 native acceptance evidence")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--template", type=Path)
    parser.add_argument("--validate", type=Path)
    parser.add_argument("--commit", help="Exact git SHA being accepted")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0
    if not args.commit:
        parser.error("--commit is required for a real manifest")
    if args.template:
        if args.template.exists():
            parser.error("Template already exists. Do not replace prior test evidence.")
        args.template.parent.mkdir(parents=True, exist_ok=True)
        with args.template.open("x", encoding="utf-8") as file:
            json.dump(blank(args.commit), file, indent=2)
            file.write("\n")
        print(f"CAMP-07 NOT_RUN template created: {args.template}")
        return 0
    if not args.validate:
        parser.error("Choose --template, --validate or --self-test")
    try:
        record = json.loads(args.validate.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        print(f"CAMP-07 NATIVE NOT ACCEPTED: {exc}")
        return 1
    issues = validate(record, args.commit, args.validate.parent.resolve())
    for issue in issues:
        print("  FAIL " + issue)
    if issues:
        print(f"CAMP-07 NATIVE NOT ACCEPTED: {len(issues)} unverified requirements")
        return 1
    print(f"CAMP-07 NATIVE EVIDENCE COMPLETE ({len(REQUIRED)} proof-linked gates;"
          " independent review still required)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
