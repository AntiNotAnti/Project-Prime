"""Read-only final PBR evidence gate; never runs GPU checks or installs assets.

The desktop/mobile simulation, direct-light oracle and sampled visual
review are independent scopes. Their exact hashes must all bind the same pack
and executable before this pre-install gate can report PASS.
"""
from __future__ import annotations

import argparse
import base64
import copy
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image

from pbr_audit import SCHEMA, pack_files, require, sha, within


HUNTERS = {"samus", "kanden", "noxus", "spire", "sylux", "trace", "weavel"}
STATIC_SHA = "04449c8264e678a637695080405cb99dada880b3749107b431e1fded34add426"
BRIDGE_SHA = "902001c32bf6f7120aaa5bc7daa11633bd4b3a65ebda250ac8be8157bfd65c22"


def read(path):
    return json.loads(Path(path).read_text())


def gate(root, visual_path, output):
    root, visual_path, output = Path(root).resolve(), Path(visual_path).resolve(), Path(output).resolve()
    candidate = root/"mobile-build-v1"/"starter"
    require(not output.is_relative_to(candidate), "Evidence receipt may not mutate the candidate")
    output.unlink(missing_ok=True)
    evidence = {}
    def record(path, expected=None):
        path = Path(path).resolve()
        actual = sha(path)
        require(expected is None or actual == expected, f"Locked evidence changed: {path}")
        evidence[str(path)] = actual
        return read(path)
    static = record(root/"final-mixed-independent-audit.json", STATIC_SHA)
    bridges = record(root/"mixed-bridged-audits-v1"/"BRIDGED-AUDITS.json", BRIDGE_SHA)
    current_files = pack_files(candidate)
    require(static.get("schema") == SCHEMA and static.get("pass") is True and static.get("staticPass") is True
            and static["candidatePack"] == str(candidate) and static["candidateFiles"] == current_files,
            "Final static receipt belongs to a different candidate")
    require(sha(Path(__file__).with_name("pbr_audit.py")) == static["toolSha256"],
            "Final static audit tooling changed")
    evidence[str(Path(__file__).with_name("pbr_audit.py"))] = static["toolSha256"]
    for name, digest in static["dependencySha256"].items():
        path = Path(__file__).with_name(name)
        require(sha(path) == digest, "Final static recipe/accessor audit dependency changed")
        evidence[str(path)] = digest
    record(root/"inventory-v5"/"INVENTORY.json", static["inventorySha256"])
    record(root/"build-v5"/"PAINT-MANIFEST.json", static["paintManifestSha256"])
    require(bridges.get("pass") is True and len(bridges["assets"]) == 25, "Mixed audit index does not cover 25 non-Samus assets")
    require(sha(Path(__file__).with_name("pbr_acceptance_bridge.py")) == bridges["toolSha256"],
            "Mixed native bridge audit tooling changed")
    evidence[str(Path(__file__).with_name("pbr_acceptance_bridge.py"))] = bridges["toolSha256"]
    require(record(bridges["certificatePath"], bridges["certificateSha256"]).get("staticPass") is True,
            "Bridge source repaint certificate differs")
    require(record(bridges["originalIndexPath"], bridges["originalIndexSha256"]).get("pass") is True,
            "Frozen original native geometry index differs")
    mixed = bridges["mixedPackStaticProof"]
    require(mixed["mixedPackPath"] == str(candidate) and mixed["mixedPackFiles"] == current_files,
            "Mixed native/geometry proof differs")
    require(all(mixed.get(key) is True for key in ("all29DesktopRepaintHashesExact", "all29MobileAccessorBytesExact",
                                                 "nativeManifestMaterialSemanticsExact")), "Mixed native proof is incomplete")
    for path, digest in mixed["evidenceFiles"].items():
        require(sha(path) == digest, f"Mixed encoder/source/evidence bytes changed: {path}")
        evidence[path] = digest
    manifest = record(candidate/"characters.json")
    require(len(manifest["models"]) == 29 and {row["hunter"].lower() for row in manifest["models"]} == HUNTERS,
            "Final seven-hunter roster differs")
    entries = {(row["hunter"].lower(), row["part"], row.get("lod", 0)): row for row in manifest["models"]}
    require(len(entries) == 29, "Duplicate native roster entry")
    exe = root/"staged-app-v1"/"Project Prime.app"/"Contents"/"MacOS"/"ProjectPrime"
    exe_sha = sha(exe); evidence[str(exe)] = exe_sha
    def model_sha(hunter, part, lod, mobile):
        entry = entries[(hunter, part, lod)]
        return current_files[entry["mobileModel" if mobile else "model"]]
    for key, row in bridges["assets"].items():
        require(row["shippingGlbSha256"] == model_sha(row["hunter"].lower(), row["part"], row["lod"], False)
                and row["shippingMobileGlbSha256"] == model_sha(row["hunter"].lower(), row["part"], row["lod"], True),
                "Current native bridge uses different desktop/mobile models")
        record(row["auditPath"], row["auditSha256"])

    suites, covered = {}, {False: set(), True: set()}
    for mobile, folder in ((False, "desktop-acceptance-v1"), (True, "mobile-acceptance-v1")):
        base = root/folder
        suite = record(base/"SIGNED-PACK-CHECK.json")
        expected = {h+"/"+mode for h in HUNTERS for mode in
                    (["lod", "weapon"] if mobile else ["biped", "lod", "weapon", "weapon-pose"])}
        expected |= {h+"/alt" for h in HUNTERS if h != "samus"} | {"weavel/turret", "samus/material"}
        require(suite.get("pass") is True and suite.get("mobile") is mobile and suite.get("entries") == 29
                and suite["candidateFiles"] == current_files and suite["signedExecutableSha256"] == exe_sha
                and set(suite["checks"]) == expected, f"{folder} has incomplete or stale exact-candidate coverage")
        submitted_total = 0; launcher_total = 0
        for key, aggregate in suite["checks"].items():
            hunter, mode = key.split("/")
            dest = base/hunter/mode
            require(aggregate.get("pass") is True and aggregate.get("mobile") is mobile
                    and aggregate.get("signedExecutableSha256") == exe_sha, f"{key}: aggregate identity differs")
            test_manifest = copy.deepcopy(manifest)
            if mode == "alt" and hunter == "weavel":
                test_manifest["models"] = [row for row in test_manifest["models"]
                                           if not (row["hunter"] == "Weavel" and row["part"] == "halfturret")]
                import hashlib
                expected_manifest_sha = hashlib.sha256((json.dumps(test_manifest, indent=2)+"\n").encode()).hexdigest()
                require(aggregate["nativeTurretInSeparateUpperAltCheck"] is True, "Weavel upper-alt isolation missing")
            else:
                expected_manifest_sha = current_files["characters.json"]
                require(aggregate.get("nativeTurretInSeparateUpperAltCheck") is False, "Unexpected roster reduction")
            require(aggregate["testManifestSha256"] == expected_manifest_sha, f"{key}: executed manifest differs")
            if mode == "weapon-pose":
                require(aggregate["testedModelSha256"] == model_sha(hunter, "viewModel", 0, mobile)
                        and sha(dest/"source-fp.png") == aggregate["captureSha256"]
                        and (dest/"native-idle-frames.json").exists() and aggregate["submittedFrames"] == 0,
                        f"{key}: frozen pose evidence differs")
                evidence[str(dest/"source-fp.png")] = aggregate["captureSha256"]
                evidence[str(dest/"native-idle-frames.json")] = sha(dest/"native-idle-frames.json")
                continue
            receipt = record(dest/"acceptance.json", aggregate["receiptSha256"])
            require(receipt.get("pass", True) is True and not receipt.get("failures")
                    and receipt["backend"] == "Metal" and receipt["hunter"].lower() == hunter
                    and receipt["mobileTextureTier"] is mobile, f"{key}: runtime receipt failed or identity differs")
            if mode == "lod":
                expected_hashes = {str(lod): model_sha(hunter, "biped", lod, mobile) for lod in (0, 1)}
                require(receipt["lodModelHashes"] == aggregate["testedModelHashes"] == expected_hashes
                        and receipt["fallbackFramesByLod"] == [0, 0] and receipt["lodTransitions"] >= 12
                        and receipt["sharedLodTextureBindings"] is True
                        and receipt["residencyBeforeSecondLod"] == receipt["residencyAfterSecondLod"],
                        f"{key}: LOD continuity failed")
                thresholds = (60, 1000) if hunter == "samus" else (500, 500)
                require(all(actual > minimum for actual, minimum in zip(receipt["eligibleFramesByLod"], thresholds)),
                        f"{key}: LOD coverage incomplete")
                for lod in (0, 1): covered[mobile].add((hunter, "biped", lod))
                frames = sum(receipt["eligibleFramesByLod"])
            else:
                part = {"biped": "biped", "weapon": "viewModel", "alt": "alternateForm",
                        "turret": "halfturret", "material": "biped"}[mode]
                selected_hash = model_sha(hunter, part, 0, mobile)
                aggregate_hashes = {"0": selected_hash} if mode in ("biped", "material") else selected_hash
                require(receipt["testedModelSha256"] == selected_hash
                        and aggregate["testedModelHashes"] == aggregate_hashes,
                        f"{key}: exact selected model hash differs")
                covered[mobile].add((hunter, part, 0))
                frames = receipt.get("submitted", receipt.get("submittedFrames", 0))
                if mode == "weapon":
                    require(frames == 3090 and receipt["lateFrames"] == 990 and receipt["fired"] is True
                            and receipt["zoomed"] is True and receipt["muzzleAligned"] is True
                            and receipt["pbrLitAllStages"] is True and receipt["materialOnFrames"] == 3030
                            and receipt["materialOffFrames"] == 60, f"{key}: complete lit first-person coverage failed")
                    rates = {case["rate"] for case in receipt["cases"] if "rate" in case}
                    require(rates == {90, 120, 240, 540}, f"{key}: high-refresh simulation coverage incomplete")
                    for case in receipt["cases"]:
                        if case.get("Name") == "materials-off":
                            require(case["advancedMaterials"] is False and case["materialOnFrames"] == 0
                                    and case["materialOffFrames"] == 60, "Maps-off control differs")
                        else:
                            require(case["advancedMaterials"] is True and case["materialOffFrames"] == 0
                                    and case["materialOnFrames"] > 0, f"{key}: unlit action/high-refresh case")
                if mode in ("alt", "turret"):
                    bridge = bridges["assets"][f"{hunter}/{part}/0"]
                    require(receipt["fallbackFrames"] == 0 and receipt["sourceAuditSha256"] == bridge["auditSha256"]
                            and receipt["sourceAuditPath"] == bridge["auditPath"], f"{key}: native fallback or wrong source audit")
                    require(receipt.get("manifestSha256") == expected_manifest_sha, f"{key}: raw manifest binding differs")
                    require(sha(receipt["sourceGlb"]) == receipt["sourceGlbSha256"] == bridge["sourceGlbSha256"],
                            f"{key}: immutable original Source model differs")
                if mode == "material":
                    require(receipt["materialSweep"] is True and receipt["morphSweep"] is True
                            and receipt["ballFrames"] > 1200 and all(receipt[field] is True for field in
                            ("boosted", "bombed", "bombJumped", "altAirborne", "ballDied", "ballRespawned")),
                            "Samus final material/Morph Ball coverage is incomplete")
                    # The exact full candidate manifest selects SamusAlt. The
                    # Samus harness has no separate ball file-hash field; its
                    # registry resolution/primitive submission and 2,060-face
                    # checks run under the locked 29-entry executed manifest.
                    covered[mobile].add(("samus", "alternateForm", 0))
            require(frames == aggregate["submittedFrames"] and frames > 0, f"{key}: submitted counts differ")
            submitted_total += frames
            launcher_frames = 0
            for label in ("launcher-before", "launcher-after"):
                path = dest/(label+".json")
                if path.exists():
                    launcher = record(path)
                    require(launcher["fallbackFrames"] == 0
                            and launcher.get("weightedFrames", launcher.get("drawnFrames")) == 60,
                            f"{key}: launcher fallback detected")
                    launcher_frames += 60
            require(launcher_frames == aggregate.get("launcherFrames", 0), f"{key}: launcher counts differ")
            launcher_total += launcher_frames
        require(covered[mobile] == set(entries), f"{folder}: exact 29-asset coverage incomplete")
        require(submitted_total == suite["submittedFrames"] and launcher_total == suite["launcherFrames"],
                f"{folder}: aggregate frame counts differ")
        suites[folder] = {"checks": len(expected), "submittedFrames": submitted_total,
                          "launcherFrames": launcher_total, "all29AssetsCovered": True,
                          "litWeaponActionAndHighRefreshStagesVerified": True}

    probe = record(root/"gpu-textures-v1"/"PROBE-LOCK.json")
    require(probe.get("pass") is True and probe["candidateFiles"] == current_files
            and probe["executableSha256"] == exe_sha and set(probe["probes"]) == {"astc", "rgba"},
            "Desktop Metal mobile-texture lock differs")
    for name, reference in probe["probes"].items():
        report = record(reference["path"], reference["sha256"])
        require(report.get("pass") is True and report["images"] == 178 and report["physicalOrmImages"] == 30
                and report["releasedBytes"] == 0 and report["rmsError"] <= 8 and len(report["checks"]) == 178,
                "Mobile-image GPU admission/readback/release proof incomplete")
        if name == "rgba": require(report["maxError"] <= 1, "RGBA mobile probe exceeded byte allowance")
        for image in report["checks"]:
            if image["physicalOrm"]:
                require(image.get("materialEncoding") == "orm-alpha-zero",
                        "GPU ORM zero-alpha marker was not checked")

    response = record(root/"gpu-response-v1"/"response.json")
    record(root/"gpu-response-v1"/"config.json", response["configSha256"])
    require(response.get("pass") is True and response["backend"] == "Metal" and response["exeSha256"] == exe_sha
            and response["maximumChannelError"] == 2 and response["maximumError"] <= 2
            and response["directAoDifference"] == 0 and response["companionAlphaIndependentOfSurfaceOpacity"] is True
            and len(response["evidence"]) == 8 and response["checkedChannels"] == 1176,
            "Actual application PBR oracle proof differs")
    require(all(response[field] >= 4 for field in ("roughnessDifference", "metallicDifference",
                                                  "ambientAoDifference", "emissiveDifference")), "PBR parameter response is ineffective")
    response_error, response_squared, oracle_images = 0, 0, {}
    for case in response["evidence"]:
        capture = within(root/"gpu-response-v1", case["capture"])
        require(sha(capture) == case["captureSha256"], "PBR oracle PNG changed")
        evidence[str(capture)] = sha(capture)
        image = np.asarray(Image.open(capture).convert("RGBA"))
        oracle_images[case["Name"]] = image
        require(list(image.shape[:2]) == [64, 64] and len(case["samples"]) == 49, "PBR oracle sample scope changed")
        require({(sample["x"], sample["y"]) for sample in case["samples"]} ==
                {(x, y) for x in range(8, 57, 8) for y in range(8, 57, 8)}, "PBR oracle sample grid differs")
        case_error = 0
        for sample in case["samples"]:
            actual = np.frombuffer(base64.b64decode(sample["actualRgba"], validate=True), dtype=np.uint8)
            require(len(actual) == 4 and actual[3] == 255
                    and np.array_equal(actual, image[63-sample["y"], sample["x"]]),
                    "PBR oracle readback differs from saved PNG or lost draw alpha")
            expected = np.rint(np.clip(sample["expectedSrgb"], 0, 1)*255).astype(np.int16)
            error = np.abs(actual[:3].astype(np.int16)-expected)
            case_error = max(case_error, int(error.max())); response_squared += int((error.astype(np.int64)**2).sum())
        require(case_error == case["maximumChannelError"], "PBR case error aggregate differs")
        response_error = max(response_error, case_error)
    require(response_error == response["maximumError"] and math.isclose(
        math.sqrt(response_squared/1176), response["rmsError"], abs_tol=1e-12), "PBR oracle total error differs")
    comparisons = {
        "roughnessDifference": ("direct-r02-metal0-ao1", "direct-r08-metal0-ao1"),
        "metallicDifference": ("direct-r02-metal0-ao1", "direct-r02-metal1-ao1"),
        "ambientAoDifference": ("ambient-metal0-ao1", "ambient-metal0-ao0"),
        "directAoDifference": ("direct-r02-metal0-ao1", "direct-r02-metal0-ao0"),
        "emissiveDifference": ("direct-r02-metal0-ao1", "direct-r02-metal0-emission")}
    for field, (a, b) in comparisons.items():
        delta = np.abs(oracle_images[a][..., :3].astype(np.int16)-oracle_images[b][..., :3].astype(np.int16))
        require(int(delta.max()) == response[field], "Saved PNG PBR parameter response differs")

    visual = record(visual_path)
    require(visual.get("pass") is True and visual.get("visualPass") is True and set(visual["hunters"]) == HUNTERS
            and visual["desktopCheckSha256"] == sha(root/"desktop-acceptance-v1"/"SIGNED-PACK-CHECK.json")
            and visual["mobileCheckSha256"] == sha(root/"mobile-acceptance-v1"/"SIGNED-PACK-CHECK.json"),
            "Final sampled manual visual review is incomplete or belongs to stale GPU suites")
    reviewed = visual.get("reviewedFiles")
    require(isinstance(reviewed, list) and reviewed, "Visual review has no exact reviewed-file evidence")
    visual_sources = set()
    for reference in reviewed:
        path = Path(reference["path"]).resolve()
        # Registered comparisons also retain exact original v6 captures under
        # the sibling pass-b archive. Those are historical references only;
        # both fresh final suite capture roots remain independently required.
        require(path.is_relative_to(root.parent) and sha(path) == reference["sha256"],
                "Manual-review source capture/contact sheet/receipt bytes changed")
        evidence[str(path)] = reference["sha256"]
        for folder in ("desktop-acceptance-v1", "mobile-acceptance-v1"):
            if path.is_relative_to(root/folder): visual_sources.add(folder)
    require(visual_sources == {"desktop-acceptance-v1", "mobile-acceptance-v1"},
            "Manual review lacks source captures from a final GPU suite")
    runtime = record(root/"runtime-v2"/"RUNTIME-CONTRACT.json")
    repo = root.parents[2]
    require(runtime["runtimeImplementationComplete"] is True and runtime["build"]["passed"] is True
            and runtime["build"]["shaderGenerationPassed"] is True
            and response["fragmentShaderSha256"] == runtime["build"]["worldFragmentShaderSha256"],
            "Controlled response used a different forward shader contract")
    for kind in ("vertex", "fragment"):
        path = repo/"src/MphRead/Mods/Render/Generated"/("World."+kind+".wgsl")
        require(sha(path) == response["generated"+kind.title()+"ShaderSha256"],
                "Actual compiled PBR WGSL resource differs from the locked runtime")
        evidence[str(path)] = sha(path)
    for name, digest in runtime["sourceHashes"].items():
        require(sha(repo/name) == digest, "Runtime source contract changed after execution")
        evidence[str(repo/name)] = digest
    for name in ("freeze-pbr.py", "install-pbr.py"):
        evidence[str(root/name)] = sha(root/name)
        text = (root/name).read_text()
        tokens = ("FINAL-EVIDENCE-INDEPENDENT.json", "independent['desktopCheckSha256']",
                  "independent['mobileCheckSha256']", "independent['visualChecksSha256']") if name == "freeze-pbr.py" else (
                  "independentEvidencePath", "independentEvidenceSha256", "independent['candidateFiles']",
                  "independent['executableSha256']")
        require(all(token in text for token in tokens),
                "Freeze/install script omits the exact independent evidence gate")
    require(all(sha(path) == digest for path, digest in evidence.items()) and pack_files(candidate) == current_files,
            "Candidate/evidence bytes changed during the independent final review")
    result = {"format": 1, "pass": True, "candidateFiles": current_files, "candidatePack": str(candidate),
              "executableSha256": exe_sha,
              "desktopCheckSha256": sha(root/"desktop-acceptance-v1"/"SIGNED-PACK-CHECK.json"),
              "mobileCheckSha256": sha(root/"mobile-acceptance-v1"/"SIGNED-PACK-CHECK.json"),
              "visualChecksSha256": sha(visual_path), "visualChecksPath": str(visual_path),
              "staticCertificateSha256": sha(root/"final-mixed-independent-audit.json"),
              "mixedBridgeSha256": sha(root/"mixed-bridged-audits-v1"/"BRIDGED-AUDITS.json"),
              "toolSha256": sha(__file__), "evidenceFiles": evidence, "suites": suites,
              "controlledResponseSavedPixelsVerified": True, "textureProbeCandidateAndExecutableExact": True,
              "preInstallGateOnly": True, "installationAcceptanceInferred": False,
              "physicalAndroidAcceptance": "Deferred by user; not tested",
              "scope": "CPU review of immutable static/repaint/native and all-mip certificates, exact fresh desktop Metal "
                       "desktop/mobile selected-model simulation/LOD/first-person/high-refresh/material receipts, actual "
                       "forward-shader oracle saved pixels and final sampled manual visual review. Samus Morph Ball is "
                       "bound through the exact full executed manifest and morph/material harness; its receipt has no "
                       "separate ball payload-hash field. Mobile probes are desktop Metal, not Android hardware. "
                       "No exhaustive clipping proof, measured surfaces, IBL, physical latency/frame pacing or installation inferred."}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2)+"\n")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact-root", required=True)
    parser.add_argument("--visual", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    report = gate(args.artifact_root, args.visual, args.output)
    print(json.dumps({key: report[key] for key in ("pass", "executableSha256", "suites", "preInstallGateOnly")}))


if __name__ == "__main__":
    main()
