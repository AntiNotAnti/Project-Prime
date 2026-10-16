#!/usr/bin/env python3
"""PORT-25: reproducible, *offline* visual acceptance matrix.

Only tests local Weighted4 cosmetic staging packs. A passing receipt is not
evidence that new Hunters, weapons, alternate forms, network code or maps work.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import subprocess
import sys

from local_visual_pack import sha256_file, inspect_candidate_glb
from retarget_contract import SOURCE_HUNTERS

MAX_JSON = 1024 * 1024
RUNTIME_TIMEOUT = 180
PART = "biped"
SKIN = "weighted4"

MANUAL_GATES = (
    "Third-person source-native bind pose and all locomotion clips visually accepted",
    "Native first-person viewmodel, muzzle and VFX authored and accepted",
    "Native alternate form, morph/unmorph and special movement authored and accepted",
    "Independent selectable character identity and gameplay implemented",
    "Trooper wall-cling, Scout Drone and Pirate Blaster server authority tested",
    "Cross-client character capability, fallback, replay seek and late join tested",
    "Real game-source visuals and all six Echoes arenas accepted",
    "Windows, Linux and physical Android performance/fps accepted",
)


def read_small_json(path):
    if path.is_symlink() or not path.is_file() or not 0 < path.stat().st_size <= MAX_JSON:
        raise ValueError("Missing, linked or oversized JSON test receipt")
    with path.open(encoding="utf-8") as stream:
        return json.load(stream)


def validate_staged_pack(path):
    if path.is_symlink() or not path.is_dir():
        raise ValueError("Expected a locally staged, real directory")
    manifest = read_small_json(path / "characters.json")
    receipt = read_small_json(path / "validation-receipt.json")
    if manifest.get("format") != 1 or receipt.get("format") != 1:
        raise ValueError("Unsupported local model-pack or receipt format")
    identity = receipt.get("sourceCharacter")
    if identity not in SOURCE_HUNTERS:
        raise ValueError("Unknown imported source character")
    hunter = SOURCE_HUNTERS[identity]
    if (receipt.get("nativeHunter") != hunter or not receipt.get("visualOnly") or
            receipt.get("approvedForGameplay") is not False or
            receipt.get("multiplayerEnabled") is not False or
            receipt.get("playerSelectionEnabled") is not False):
        raise ValueError("Test-only character and network invariants are absent")
    models = manifest.get("models")
    if not isinstance(models, list) or len(models) != 1:
        raise ValueError("Acceptance requires one isolated, native biped entry")
    entry = models[0]
    relative = hunter.lower() + "/biped.glb"
    if (entry.get("hunter") != hunter or entry.get("part") != PART or
            entry.get("skinning") != SKIN or entry.get("lod") != 0 or
            entry.get("model") != relative):
        raise ValueError("Test pack would alter more than one native biped")
    if manifest.get("id") != "retro-" + identity + "-local-biped-visual-test":
        raise ValueError("Noncanonical test pack identity")
    bone_map = entry.get("boneMap")
    if (not isinstance(bone_map, dict) or
            not 1 <= len(bone_map) <= 32 or any(key != value for key,value in bone_map.items())):
        raise ValueError("Biped GLB source joints must map identically to native joints")
    source_glb = path / hunter.lower() / "biped.glb"
    if sha256_file(source_glb) != receipt.get("sourceGlbSha256"):
        raise ValueError("Staged GLB changed since private provenance verification")
    original_glb = receipt.get("GLB")
    if not isinstance(original_glb, dict) or set(original_glb.get("skinJoints",())) != set(bone_map):
        raise ValueError("Staged candidate palette does not match receipt")
    actual = inspect_candidate_glb(source_glb,list(bone_map),original_glb.get("materials",[]))
    if actual["bytes"] != original_glb.get("bytes"):
        raise ValueError("Staged candidate changed since provenance record")
    if any(receipt.get(key) is not False for key in (
            "firstPersonVerified","alternateFormVerified",
            "sourceMotionRetargetVerified","devicePerformanceVerified")):
        raise ValueError("Staged preview must not claim unperformed acceptance")
    return {"sourceCharacter":identity,"fallbackHunter":hunter,
            "sourceGlbSha256":receipt["sourceGlbSha256"],
            "skinJoints":actual["skinJoints"],
            "materialNames":actual["materials"]}


def validate_runtime(executable, path):
    if not executable.is_file():
        raise ValueError("Project Prime validator executable is missing")
    # This is explicit developer CLI use of a local binary, never a shell,
    # never remote execution or an installer. Keep the subprocess bounded.
    try:
        result = subprocess.run(
            [str(executable.absolute()), "-charactermodelvalidate",
             str(path.absolute()), "-noupdate"],
            capture_output=True, text=True, timeout=RUNTIME_TIMEOUT, check=False,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        return {"status":"fail","exitCode":None,"diagnostic":str(exc)[:4096]}
    return {"status":"pass" if result.returncode == 0 else "fail",
            "exitCode":result.returncode,
            "diagnostic":(result.stdout+"\n"+result.stderr)[-4096:]}


def evaluate(packs, runtime=None):
    if not isinstance(packs, list) or not packs:
        raise ValueError("Specify one or more previously staged private test packs")
    if len(packs) > len(SOURCE_HUNTERS):
        raise ValueError("At most one test pack per source character")
    result = []
    seen = set()
    for path in packs:
        row={"packName":path.name,"assetPreflight":"fail","runtimeValidation":"not-run"}
        try:
            value = validate_staged_pack(path)
            if value["sourceCharacter"] in seen:
                raise ValueError("Duplicate source identity in acceptance matrix")
            seen.add(value["sourceCharacter"])
            row.update(value)
            row["assetPreflight"]="pass"
            if runtime is not None:
                run=validate_runtime(runtime,path)
                row["runtimeValidation"]=run["status"]
                row["runtimeExitCode"]=run["exitCode"]
                row["runtimeDiagnostic"]=run["diagnostic"]
        except (OSError,ValueError,TypeError,KeyError,IndexError) as exc:
            row["error"]=str(exc)[:1000]
        result.append(row)
    good=sum(x["assetPreflight"]=="pass" and x["runtimeValidation"]=="pass" for x in result)
    return {
        "schemaVersion":1,
        "scope":"offline-native-biped-visual-test-only",
        "assetPreflightPassed":sum(x["assetPreflight"]=="pass" for x in result),
        "runtimeBipedCandidatesPassed":good,
        "inputPacks":len(packs),
        "fullPortReady":False,
        "allCharacterAssetsAccepted":False,
        "allSixEchoesArenasAccepted":False,
        "onlineMultiplayerEnabled":False,
        "manualGatesRequired":list(MANUAL_GATES),
        "results":result,
    }


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--pack",type=Path,action="append",required=True,
                        help="Repeat for each staged Dark Samus / Trooper visual-test pack")
    parser.add_argument("--project-prime",type=Path,
                        help="Explicit local binary to run its existing -charactermodelvalidate command")
    parser.add_argument("--report",type=Path,required=True)
    args=parser.parse_args()
    if args.report.is_symlink() or args.report.suffix.lower()!=".json":
        raise ValueError("Acceptance output must be a local JSON report, not a symlink")
    if not args.report.parent.is_dir():
        raise ValueError("Acceptance report parent directory must exist")
    output=evaluate(args.pack,args.project_prime)
    args.report.write_text(json.dumps(output,indent=2)+"\n",encoding="utf-8")
    print("Asset preflight:",output["assetPreflightPassed"],"/",output["inputPacks"])
    print("Project Prime renderer validator:",output["runtimeBipedCandidatesPassed"],"/",output["inputPacks"])
    print("Visual/device/gameplay/manual acceptance remains required; fullPortReady=false")
    print("Local evidence:",args.report)
    if output["runtimeBipedCandidatesPassed"]!=output["inputPacks"]:
        return 1
    return 0

if __name__=="__main__":
    sys.exit(main())
