"""Bridge immutable v6 geometric evidence to independently audited PBR pixels.

This deliberately does not use the scalar material-response bridge. It creates
new audit files at the new whole-model hashes, retaining only old geometric,
native bind, Source identity and muzzle facts proved byte-exact by pbr_audit.
Original art/material/runtime claims are historical and are not copied as
current claims. Fresh GPU/visual acceptance is still required.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
from pathlib import Path

from pbr_audit import SCHEMA, geometry, load, pack_files, require, sha, within


GEOMETRY_FIELDS = {
    "hunter", "part", "sourceSha256", "sourceGlbSha256", "configSha256", "generatedExporterSha256",
    "sourceTriangles", "totalTriangles", "nativeSupplementTriangles", "indexedExportedSourceRecords",
    "blendedIndexedSourceRecords", "nativeJoints", "inverseBindMaximumError", "defaultJointNativeBindMaximumError",
    "maximumSourcePositionError", "maximumRuntimeUvError", "maximumCollapsedSourceWeightError", "maximumWeightSumError",
    "maximumSourceNormalAngleDegrees", "sourceMaterialTriangles", "runtimeMaterialTriangles", "nativeOnlyNormalizedWeighted4",
    "sourceTopologyAndSplitNormalsPreserved", "inheritedNativeBindScalePreserved", "teamRecolorsRequired",
    "nativeEffectsRetained", "nativeSupplementMaterials", "remeshed", "subdivided", "muzzleProof",
    "sourceWeaponTriangles", "shippingWeaponTriangles", "nativeNodes", "nativeNodeMappings", "poseAlignment",
    "nativePoseFit", "sourceWeaponFit", "nativeWeaponMuzzleAlignment", "sourceVertexCount", "sourceIndexCount",
}
SCOPE = (
    "Static PBR repaint acceptance bridge. Original Source identity, geometry/UV/split-normal/skin/bind data, "
    "native mappings and muzzle facts are inherited only after exact independent v6-to-candidate accessor/native-contract "
    "proof. Color artwork, normals, AO, roughness and metalness have new authored provenance and exact new embedded-image "
    "checks; original Source-atlas-lossless, scalar-response and runtime/visual receipts are historical and do not establish "
    "new artwork or runtime acceptance. Fresh exact-candidate rendering/gameplay/material and mobile acceptance remain required. "
    "No physically measured surfaces, environment/IBL reflections, exhaustive clipping proof or Android hardware acceptance is inferred."
)


def read(path):
    return json.loads(Path(path).read_text())


def checked(path, expected, label):
    path = Path(path).resolve()
    require(sha(path) == expected, f"{label} hash differs")
    result = read(path)
    require(result.get("pass") is True, f"{label} did not pass")
    return path, result


def identity(entry):
    return f"{entry['hunter'].lower()}/{entry['part']}/{entry.get('lod', 0)}"


def mobile_proof(root, candidate):
    """A mobile bridge requires explicit current static + all-mip certificates."""
    if root is None:
        return None
    root = Path(root).resolve()
    build_path, audit_path, compression_path = root/"build-result.json", root/"audit.json", root/"compression-audit.json"
    build, audit, compression = read(build_path), read(audit_path), read(compression_path)
    require(build.get("buildCompleted") is True and build.get("sourcePack") == str(candidate), "Mobile input candidate differs")
    require(audit.get("pass") is True and audit.get("buildResultSha256") == sha(build_path), "Mobile static certificate differs")
    require(compression.get("pass") is True and compression.get("offlineByteAuditPass") is True
            and compression.get("buildResultSha256") == sha(build_path),
            "All-authored-mip compression certificate did not pass for this build")
    # The exact declared quality budgets cannot be replaced by new relaxed
    # thresholds. Its independently decoded per-mip report is retained intact.
    tier = read(root/"tier.json")
    require(tier.get("compressionAcceptance") == {
        "minimumRgbPsnrDb": 35, "minimumAlphaPsnrDb": 35,
        "maximumMeanNormalAngleDegrees": 1.5, "maximumP99NormalAngleDegrees": 6}, "Mobile quality limits changed")
    budget = tier["compressionAcceptance"]
    require(compression.get("fixedCompressionGuards") == audit.get("fixedCompressionGuards") == budget,
            "Mobile compression reports use different limits")
    require(tier.get("preserveHunters") == audit.get("preservedHunters") == [],
            "Current ORM companions require a fresh seven-hunter mobile build")
    require(build.get("tierConfigSha256") == sha(root/"tier.json"), "Mobile tier differs from the build")
    pipeline = {path.name: sha(path) for path in Path(__file__).parent.glob("mobile_*.py")}
    require(build.get("pipelineInputs") == pipeline, "Mobile build tooling changed")
    require(sha(build["encoder"]["encoder"]) == build["encoder"]["encoderSha256"], "Mobile encoder changed")
    lock_path = root/"source-lock.json"
    lock = read(lock_path)
    require(sha(lock_path) == build["sourceLockSha256"] and lock.get("sourcePack") == str(candidate)
            and lock.get("files") == pack_files(candidate), "Mobile source lock differs")
    require(pack_files(root/"starter") == build["mixedPackFiles"]
            and pack_files(root/"android-pack") == build["androidPackFiles"], "Mobile pack bytes changed")
    original, mixed, android = (read(base/"characters.json") for base in
                                (candidate, root/"starter", root/"android-pack"))
    expected_mixed = copy.deepcopy(original)
    expected_android = copy.deepcopy(original)
    require(len(original["models"]) == len(build["models"]) == 29, "Mobile roster size differs")
    for original_entry, mixed_entry, android_entry, row in zip(
            original["models"], expected_mixed["models"], expected_android["models"], build["models"]):
        require((row["hunter"], row["part"].lower(), row.get("lod", 0)) ==
                (original_entry["hunter"], original_entry["part"].lower(), original_entry.get("lod", 0)),
                "Mobile build changes model order/identity")
        require(sha(within(root/"starter", original_entry["model"])) ==
                sha(within(candidate, original_entry["model"])) == row["sourceSha256"],
                "Mixed desktop pixels differ from the independently audited repaint")
        require(sha(within(root/"android-pack", original_entry["model"])) == row["mobileSha256"],
                "Android-only bytes differ from selected mobile bytes")
        mixed_entry["mobileModel"] = row["mobileModel"]
        desktop_doc, desktop_blob = load(within(candidate, original_entry["model"]))
        mobile_doc, mobile_blob = load(within(root/"starter", row["mobileModel"]))
        geometry(desktop_doc, desktop_blob, mobile_doc, mobile_blob)
        # Replay the finite, documented factor bake and texture-index remap
        # without importing the builder's material mutation implementation.
        expected_materials = copy.deepcopy(desktop_doc.get("materials", []))
        require(len(expected_materials) == len(mobile_doc.get("materials", [])),
                "Mobile material count differs")
        for old, new in zip(expected_materials, mobile_doc["materials"]):
            pbr = old.get("pbrMetallicRoughness", {})
            if "normalTexture" in old or "emissiveTexture" in old or "metallicRoughnessTexture" in pbr:
                old.setdefault("extras", {})["projectPrimeRuntimeMaps"] = True
            if "normalTexture" in old:
                old["normalTexture"]["scale"] = 1
            if "emissiveTexture" in old:
                old["emissiveFactor"] = [1, 1, 1]
            if "metallicRoughnessTexture" in pbr:
                pbr["metallicFactor"], pbr["roughnessFactor"] = 1, 1
            def remap(before, after):
                if before is not None:
                    require(isinstance(after, dict) and "index" in after, "Mobile map reference disappeared")
                    before["index"] = after["index"]
            for field in ("baseColorTexture", "metallicRoughnessTexture"):
                remap(pbr.get(field), new.get("pbrMetallicRoughness", {}).get(field))
            for field in ("normalTexture", "emissiveTexture"):
                remap(old.get(field), new.get(field))
            recolors = old.get("extras", {}).get("projectPrimeRecolors", {})
            for key, info in recolors.items():
                remap(info, new.get("extras", {}).get("projectPrimeRecolors", {}).get(key))
            require(old == new, "Mobile conversion changes alpha/culling/native identity or ORM semantics")
    expected_mixed["id"] = tier["tierId"]+"-mixed"
    expected_android["id"] = tier["tierId"]+"-android-only"
    require(mixed == expected_mixed and android == expected_android,
            "Mixed/native manifest contract changed beyond tier ID and selected mobileModel")
    # Bind every decoded-mip claim to the exact encoded payload and reference
    # mip hashes. A previous PASS, omitted mip, or relaxed budget cannot bridge.
    image_rows = {image["key"]: image for image in build["uniqueImageConversions"]}
    require(len(image_rows) == len(build["uniqueImageConversions"]) == compression.get("images"),
            "Mobile conversion/compression image coverage differs")
    checks = {(check["imageKey"], check["mip"]): check for check in compression["checks"]}
    expected_checks = {(key, mip) for key, image in image_rows.items() for mip in range(image["mips"])}
    require(len(checks) == len(compression["checks"]) == compression.get("mips")
            and set(checks) == expected_checks, "Compression certificate omits/duplicates an authored mip")
    for key, image in image_rows.items():
        base = root/"image-cache"/key
        for suffix, field in ((".ktx2", "encodedSha256"), (".mips", "referenceMipSha256"),
                              (".png", "referencePngSha256")):
            require(sha(base.with_suffix(suffix)) == image[field], "Mobile encoded/reference bytes changed")
        require(len(image["compressionQuality"]["checks"]) == image["mips"],
                "Selected encoding quality omits an authored mip")
        for mip, size in enumerate(image["mipSizes"]):
            check = checks[(key, mip)]
            require(check["size"] == size and check["channel"] == image["meaning"]["channel"]
                    and check["encodingMode"] == image["encodingMode"], "Decoded mip source/shape/meaning differs")
            limits = {"rgbPsnrDb": (budget["minimumRgbPsnrDb"], True),
                      "alphaPsnrDb": (budget["minimumAlphaPsnrDb"], True)}
            if check["channel"] == "normal":
                limits.update(meanAngularErrorDegrees=(budget["maximumMeanNormalAngleDegrees"], False),
                              p99AngularErrorDegrees=(budget["maximumP99NormalAngleDegrees"], False))
            for field, (limit, minimum) in limits.items():
                value = check[field]
                require(isinstance(value, (int, float)) and math.isfinite(value) and value >= 0
                        and (value >= limit if minimum else value <= limit), "Decoded-mip quality limit failed")
                require(math.isclose(value, image["compressionQuality"]["checks"][mip][field],
                                     abs_tol=1e-8, rel_tol=1e-8), "Decoded mip differs from selected encoder quality")
            if image["meaning"].get("materialEncoding") == "orm":
                require(check.get("ormZeroAlphaMarkerExact") is True, "Decoded ORM marker differs")
            if image["encodingMode"] == "rgba8-zstd":
                require(check.get("rgbaMipBytesExact") is True, "Lossless fallback mip is not byte exact")
    rows = {(row["hunter"], row["part"].lower(), row.get("lod", 0)): row for row in build["models"]}
    static_rows = {(row["hunter"], row["part"].lower(), row.get("lod", 0)): row for row in audit["models"]}
    require(len(rows) == len(static_rows) == 29 and set(rows) == set(static_rows), "Mobile audit omits a model")
    for key, row in rows.items():
        proof = static_rows[key]
        require(row.get("preserved") is False, "New PBR Samus/mobile companions must be rebuilt, not preserved as legacy")
        require(all(proof.get(flag) is True for flag in (
            "geometryUVSkinAccessorBytesPreserved", "nativeTransformsPreserved", "nativeMaterialSemanticsPreserved",
            "desktopBytesPreserved", "runtimeMapsConvertedExactlyOnce")), "Mobile native/material/UV semantics did not pass")
        require(proof["mobileSha256"] == row["mobileSha256"]
                == sha(within(root/"starter", row["mobileModel"])), "Mobile selected payload hash differs")
    evidence_paths = [build_path, audit_path, compression_path, root/"tier.json", lock_path,
                      Path(build["encoder"]["encoder"])]
    evidence_paths.extend(Path(__file__).parent.glob("mobile_*.py"))
    for key in image_rows:
        evidence_paths.extend((root/"image-cache"/key).with_suffix(suffix) for suffix in (".ktx2", ".mips", ".png"))
    return {"root": str(root), "buildResultPath": str(build_path), "buildResultSha256": sha(build_path),
            "auditPath": str(audit_path), "auditSha256": sha(audit_path),
            "compressionAuditPath": str(compression_path), "compressionAuditSha256": sha(compression_path),
            "sourceLockSha256": sha(lock_path), "models": rows,
            "mixedPackPath": str(root/"starter"), "mixedPackFiles": build["mixedPackFiles"],
            "mixedManifestSha256": sha(root/"starter"/"characters.json"),
            "all29DesktopRepaintHashesExact": True, "all29MobileAccessorBytesExact": True,
            "nativeManifestMaterialSemanticsExact": True,
            "decodedImages": len(image_rows), "decodedMips": len(checks),
            "fixedCompressionGuards": budget, "gpuAcceptanceInferred": False,
            "evidenceFiles": {str(path.resolve()): sha(path) for path in evidence_paths},
            "physicalAndroidAcceptanceInferred": False}


def bridge(args):
    old_index_path, old_index = checked(args.old_bridges, args.old_bridges_sha256, "Frozen v6 geometry audit index")
    certificate_path, certificate = checked(args.certificate, args.certificate_sha256, "Independent PBR repaint certificate")
    require(certificate.get("schema") == SCHEMA and certificate.get("staticPass") is True,
            "PBR bridge requires independent exact native/UV/accessor/pixel proof")
    source, candidate = Path(certificate["sourcePack"]).resolve(), Path(args.candidate).resolve()
    require(certificate.get("candidatePack") == str(candidate)
            and certificate.get("candidateFiles") == pack_files(candidate), "Independent certificate candidate differs")
    output = Path(args.output).resolve()
    require(not output.exists() and not output.is_relative_to(source) and not output.is_relative_to(candidate),
            "Audit bridge output must be fresh and outside the packs")
    manifest = read(candidate/"characters.json")
    source_manifest = read(source/"characters.json")
    originals = {identity(entry): entry for entry in source_manifest["models"]}
    entries = {identity(entry): entry for entry in manifest["models"]}
    require(len(entries) == 29 and set(entries) == set(originals), "PBR bridge roster/native entry coverage differs")
    proof_rows = {row["model"]: row for row in certificate["models"]}
    require(set(proof_rows) == {entry["model"] for entry in entries.values()}, "Independent proof omits a current model")
    old_rows = old_index["assets"]
    expected_non_samus = {key for key, entry in entries.items() if entry["hunter"] != "Samus"}
    require(set(old_rows) == expected_non_samus and len(old_rows) == 25, "Frozen v6 index must cover all 25 non-Samus models")
    mobile = mobile_proof(args.mobile_build, candidate)
    result = {"format": 1, "pass": True, "accepted": False, "scope": SCOPE,
              "sourcePack": str(source), "candidatePack": str(candidate),
              "sourceManifestSha256": sha(source/"characters.json"), "candidateManifestSha256": sha(candidate/"characters.json"),
              "certificatePath": str(certificate_path), "certificateSha256": sha(certificate_path),
              "originalIndexPath": str(old_index_path), "originalIndexSha256": sha(old_index_path),
              "toolSha256": sha(__file__), "auditToolSha256": sha(Path(__file__).with_name("pbr_audit.py")),
              "assets": {}, "samusNativeGeometryProof": {}, "newHashRuntimeAcceptanceInferred": False,
              "newHashArtworkAcceptanceInferred": False, "physicalAndroidAcceptanceInferred": False}
    prepared, locked = [], {str(old_index_path): sha(old_index_path), str(certificate_path): sha(certificate_path)}
    if mobile:
        result["mixedPackStaticProof"] = {k: v for k, v in mobile.items() if k != "models"}
        locked.update(mobile["evidenceFiles"])
    for key, entry in entries.items():
        proof = proof_rows[entry["model"]]
        require(proof.get("nativeGeometrySkinUVBytesExact") is True
                and proof["sourceSha256"] == sha(within(source, entry["model"]))
                and proof["candidateSha256"] == sha(within(candidate, entry["model"])), "Independent per-model exact hash/native proof differs")
        # Replay accessor equality rather than trusting a stale top-level PASS.
        old_doc, old_blob = load(within(source, entry["model"]))
        new_doc, new_blob = load(within(candidate, entry["model"]))
        require(geometry(old_doc, old_blob, new_doc, new_blob) == proof["accessorSha256"], "Decoded accessor bridge differs")
        if entry["hunter"] == "Samus":
            result["samusNativeGeometryProof"][entry["model"]] = {
                "originalGlbSha256": proof["sourceSha256"], "shippingGlbSha256": proof["candidateSha256"],
                "geometryAndUvAccessorBytesExact": True, "nativeManifestEntryPreserved": True,
                "scope": "Same native contract; fresh Samus runtime acceptance required."}
            continue
        reference = old_rows[key]
        require(reference["shippingGlbSha256"] == proof["sourceSha256"], "Frozen audit index belongs to a different v6 model")
        audit_path, old = checked(reference["auditPath"], reference["auditSha256"], "Frozen v6 static geometry audit")
        require(old["hunter"] == entry["hunter"] and old["shippingGlbSha256"] == proof["sourceSha256"], "Frozen original audit identity/hash differs")
        locked[str(audit_path)] = sha(audit_path)
        inherited = {field: copy.deepcopy(old[field]) for field in GEOMETRY_FIELDS if field in old}
        inherited.update({"pass": True, "accepted": False, "scope": SCOPE, "shippingGlbSha256": proof["candidateSha256"]})
        if entry["part"].lower() == "viewmodel":
            inherited["viewmodelSha256"] = proof["candidateSha256"]
        inherited["historicalOriginalAudit"] = {
            "path": str(audit_path), "sha256": sha(audit_path), "scope": old.get("scope"),
            "wholeFileSha256": proof["sourceSha256"], "currentArtworkClaimsCopied": False,
            "originalRuntimeAndVisualEvidenceIsHistorical": True}
        inherited["pbrRepaintBridge"] = {
            "certificatePath": str(certificate_path), "certificateSha256": sha(certificate_path),
            "originalGlbSha256": proof["sourceSha256"], "shippingGlbSha256": proof["candidateSha256"],
            "nativeGeometrySkinUVBytesExact": True, "accessorSha256": proof["accessorSha256"],
            "embeddedImageChecks": proof["maps"], "originalSourceAtlasArtworkExactClaim": False,
            "newRuntimeAcceptanceInferred": False, "newArtworkVisualAcceptanceInferred": False,
            "scope": SCOPE}
        if mobile:
            row = mobile["models"][(entry["hunter"], entry["part"].lower(), entry.get("lod", 0))]
            require(row["sourceSha256"] == proof["candidateSha256"], "Mobile build uses a different PBR desktop model")
            inherited["shippingMobileGlbSha256"] = row["mobileSha256"]
            inherited["mobilePbrBridge"] = {k: v for k, v in mobile.items() if k != "models"}
        source_glb = reference.get("sourceGlb")
        if source_glb:
            require(sha(source_glb) == reference["sourceGlbSha256"], "Original Source model changed")
            locked[source_glb] = reference["sourceGlbSha256"]
        prepared.append((key, entry, proof, reference, inherited, audit_path))
    output.mkdir(parents=True)
    try:
        for key, entry, proof, reference, inherited, audit_path in prepared:
            target = output/entry["hunter"].lower()/entry["part"].lower()/f"lod{entry.get('lod', 0)}"/"audit.json"
            target.parent.mkdir(parents=True)
            target.write_text(json.dumps(inherited, indent=2)+"\n")
            row = {"hunter": entry["hunter"], "part": entry["part"], "lod": entry.get("lod", 0),
                   "model": entry["model"], "auditPath": str(target), "auditSha256": sha(target),
                   "originalAuditPath": str(audit_path), "originalAuditSha256": sha(audit_path),
                   "originalGlbSha256": proof["sourceSha256"], "shippingGlbSha256": proof["candidateSha256"],
                   "sourceGlbSha256": reference.get("sourceGlbSha256"),
                   "nativeGeometrySkinUVBytesExact": True}
            if reference.get("sourceGlb"):
                row["sourceGlb"] = reference["sourceGlb"]
            if mobile:
                row["shippingMobileGlbSha256"] = inherited["shippingMobileGlbSha256"]
            result["assets"][key] = row
        require(all(sha(path) == digest for path, digest in locked.items())
                and certificate["candidateFiles"] == pack_files(candidate), "Input pack/proof changed during bridging")
        if mobile:
            require(mobile["mixedPackFiles"] == pack_files(Path(mobile["mixedPackPath"])),
                    "Mixed pack changed during bridging")
        result["bridgeFiles"] = pack_files(output)
        (output/"BRIDGED-AUDITS.json").write_text(json.dumps(result, indent=2)+"\n")
    except Exception:
        (output/"FAILED.json").write_text(json.dumps({"pass": False, "scope": "Partial bridge; must not be used."})+"\n")
        raise
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("certificate", "certificate-sha256", "old-bridges", "old-bridges-sha256", "candidate", "output"):
        parser.add_argument("--"+name, required=True)
    parser.add_argument("--mobile-build")
    result = bridge(parser.parse_args())
    print(json.dumps({"pass": True, "accepted": False, "assets": len(result["assets"]),
                      "newHashRuntimeAcceptanceInferred": False, "newHashArtworkAcceptanceInferred": False}))


if __name__ == "__main__":
    main()
