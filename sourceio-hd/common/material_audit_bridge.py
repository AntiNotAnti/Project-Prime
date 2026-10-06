"""Create fresh, exact-hash static audit bridges after independent preservation proof.

Original conversion/acceptance receipts remain immutable and retain their original
whole-file hashes. A bridge carries their geometry/source/native-contract evidence
into a material-response candidate only after a separately supplied, SHA-bound
independent certificate proves unchanged geometry and artwork. New runtime,
visual, Android/device acceptance is never inferred from an old receipt.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
from pathlib import Path, PurePosixPath

HUNTERS = {"Kanden", "Noxus", "Spire", "Sylux", "Trace", "Weavel"}
GEOMETRY_FLAGS = (
    "allDecodedAccessorsByteExact", "allPrimitiveIndicesAndAssignmentsExact",
    "nativeNodesSkinsAnimationsAndBindAccessorsExact", "allSourceUVColorNormalAndWeightValuesExact",
    "allRecolorsOriginalImagePayloadsAndMetadataExact", "allSamplersAndCullingAlphaFactorsExact",
)
MOBILE_FLAGS = ("geometryUVSkinAccessorBytesPreserved", "nativeTransformsPreserved",
                "nativeMaterialSemanticsPreserved", "desktopBytesPreserved", "runtimeMapsConvertedExactlyOnce")
BUDGET = {"minimumRgbPsnrDb": 35, "minimumAlphaPsnrDb": 35,
          "maximumMeanNormalAngleDegrees": 1.5, "maximumP99NormalAngleDegrees": 6}
SCOPE = ("Static material-response bridge only. Unchanged geometry, native contract and original "
         "albedo/normal/emissive/recolor artwork are certified independently against the original exact "
         "conversion audit. Only declared Source-mask-derived forward roughness/specular response changes "
         "are admitted. Original audits and acceptance evidence retain their old shipping hashes; new-hash "
         "runtime/material/visual and physical Android acceptance require fresh checks. This is not a "
         "4K repaint, de-lighting or measured physical PBR claim.")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path):
    return json.loads(Path(path).read_text())


def guarded(root, relative):
    root = Path(root).resolve()
    require(isinstance(relative, str) and relative and "\\" not in relative,
            f"Invalid pack path: {relative}")
    value = PurePosixPath(relative)
    require(not value.is_absolute() and all(x not in ("", ".", "..") for x in relative.split("/")),
            f"Unsafe pack path: {relative}")
    result = root.joinpath(*value.parts)
    require(result.resolve().is_relative_to(root), f"Path escapes pack: {relative}")
    return result


def files(root):
    result = {}
    for path in sorted(Path(root).rglob("*")):
        require(not path.is_symlink(), f"Symlink in input pack: {path}")
        if path.is_file():
            result[path.relative_to(root).as_posix()] = sha(path)
    return result


def checked_certificate(path, expected, label):
    path = Path(path).resolve()
    require(isinstance(expected, str) and len(expected) == 64 and sha(path) == expected.lower(),
            f"{label} certificate SHA differs")
    value = read(path)
    require(value.get("pass") is True, f"{label} certificate did not pass")
    return path, value


def key(entry):
    return (entry["hunter"], entry["part"].lower(), entry.get("lod", 0))


def indexed(rows):
    result = {}
    for row in rows:
        k = key(row)
        require(k not in result, f"Duplicate asset identity: {k}")
        result[k] = row
    return result


def old_audit(entry, build, review_root, lod1_root):
    hunter = entry["hunter"].lower()
    part, lod = entry["part"].lower(), entry.get("lod", 0)
    if part == "biped":
        return (review_root / hunter / "biped/releases/lod0-v3/audit.json" if lod == 0
                else lod1_root / hunter / "audit.json")
    if part == "viewmodel":
        require(lod == 0, "Only LOD0 viewmodels are covered")
        return review_root / hunter / "weapon/audit.json"
    source = build.get("newSources", {}).get(f"{hunter}/{entry['part']}")
    if source is None:
        source = next((value for name, value in build.get("newSources", {}).items()
                       if name.lower() == f"{hunter}/{part}"), None)
    require(source is not None and lod == 0, f"Missing BUILD-LOCK source for {hunter}/{part}/{lod}")
    path = Path(source["auditPath"]).resolve()
    require(sha(path) == source["auditSha256"], f"BUILD-LOCK source audit changed: {path}")
    return path


def original_source_path(audit):
    expected = audit.get("sourceGlbSha256", audit.get("sourceSha256"))
    def descend(value):
        if not isinstance(value, dict):
            return None
        for name, item in value.items():
            if isinstance(name, str) and name.endswith(".glb") and item == expected:
                path = Path(name)
                if path.is_absolute() and path.is_file():
                    require(sha(path) == expected, f"Original Source GLB changed: {path}")
                    return str(path.resolve())
            result = descend(item)
            if result:
                return result
        return None
    for field in ("prebuildProvenance", "pipelineInputs", "inputs"):
        result = descend(audit.get(field))
        if result:
            return result
    return None


def validate_mobile(args, candidate, response_certificate_path, response_certificate_sha):
    if not args.mobile_build:
        require(not any((args.mobile_certificate, args.mobile_certificate_sha256,
                         args.mobile_pack_certificate, args.mobile_pack_certificate_sha256)),
                "Mobile certificates require --mobile-build")
        return None
    require(args.mobile_certificate and args.mobile_certificate_sha256
            and args.mobile_pack_certificate and args.mobile_pack_certificate_sha256,
            "Mobile bridging requires independent quality AND pack-contract certificates with exact SHA-256")
    root = Path(args.mobile_build).resolve()
    build_path, audit_path, lock_path = root / "build-result.json", root / "audit.json", root / "source-lock.json"
    build, audit, lock = read(build_path), read(audit_path), read(lock_path)
    digest = sha(build_path)
    require(build.get("buildCompleted") is True and build.get("sourcePack") == str(candidate),
            "Mobile build source/completion differs")
    require(sha(lock_path) == build["sourceLockSha256"] and lock.get("sourcePack") == str(candidate)
            and lock.get("files") == files(candidate), "Mobile source lock differs from exact material candidate")
    require(audit.get("pass") is True and audit.get("buildResultSha256") == digest,
            "Mobile builder static audit differs from build")
    quality_path, quality = checked_certificate(args.mobile_certificate, args.mobile_certificate_sha256, "Mobile quality")
    contract_path, contract = checked_certificate(args.mobile_pack_certificate, args.mobile_pack_certificate_sha256, "Mobile pack")
    for value, label in ((quality, "quality"), (contract, "pack")):
        require(value.get("buildResultSha256") == digest, f"Independent mobile {label} refers to a different build")
        require(value.get("candidate") == str(root), f"Independent mobile {label} candidate path differs")
    require(quality.get("encoderSha256") == build["encoder"]["encoderSha256"], "Independent mobile encoder hash differs")
    require(quality.get("budget") == BUDGET and quality.get("failingMips") == 0
            and quality.get("failures") == [] and quality.get("images", 0) > 0 and quality.get("mips", 0) > 0,
            "Independent all-mip mobile fidelity budgets/coverage did not pass")
    require(contract.get("responseBridgeCertificateSha256") == response_certificate_sha,
            "Independent mobile pack certificate refers to a different desktop response proof")
    for field, subroot in (("mixedPackFiles", root / "starter"), ("androidPackFiles", root / "android-pack")):
        require(build.get(field) == files(subroot), f"Mobile {field} differs from current files")
    build_rows, audit_rows = indexed(build["models"]), indexed(audit["models"])
    contract_values = contract.get("models")
    require(isinstance(contract_values, list), "Independent mobile pack certificate needs per-model rows")
    contract_rows = indexed(contract_values)
    source_entries = indexed(read(candidate / "characters.json")["models"])
    require(set(build_rows) == set(audit_rows) == set(contract_rows) == set(source_entries),
            "Mobile build/audit/independent certificate does not cover every desktop entry")
    mobile_entries = indexed(read(root / "starter/characters.json")["models"])
    require(set(mobile_entries) == set(source_entries), "Mixed mobile manifest changes roster identities")
    for k, entry in source_entries.items():
        row, static, independent = build_rows[k], audit_rows[k], contract_rows[k]
        require(row["sourceSha256"] == sha(guarded(candidate, entry["model"])), f"Mobile source SHA differs: {k}")
        require(row["mobileSha256"] == static["mobileSha256"], f"Mobile audit model SHA differs: {k}")
        require(sha(guarded(root / "starter", row["mobileModel"])) == row["mobileSha256"], f"Mobile file SHA differs: {k}")
        expected_entry = copy.deepcopy(entry)
        expected_entry["mobileModel"] = row["mobileModel"]
        require(mobile_entries[k] == expected_entry, f"Mixed mobile native contract/entry differs: {k}")
        # Independent report field names are intentionally fixed; raw builder
        # claims cannot substitute for a distinct decoded contract certificate.
        require(independent.get("exactResponseGlbSha256") == row["sourceSha256"]
                and independent.get("mobileGlbSha256") == row["mobileSha256"], f"Independent mobile model hashes differ: {k}")
        if entry["hunter"] != "Samus":
            require(all(static.get(flag) is True for flag in MOBILE_FLAGS), f"Mobile static geometry/material preservation failed: {k}")
            require(all(independent.get(flag) is True for flag in (
                    "allDecodedGeometryUVWeightColorNormalAndBindValuesExact", "nativeNodesSkinsAndAnimationsExact",
                    "imageMeaningsAndFactorIdentityBridgeExact", "samplersAndAlphaCullingMetadataExact",
                    "imagePayloadBindingsMatchExactEncodedCache")),
                    f"Independent mobile native/material contract failed: {k}")
        else:
            require(static.get("preservedDesktopAndMobileBytes") is True, "Samus mobile baseline was changed")
    return {"buildRoot": str(root), "buildResultPath": str(build_path), "buildResultSha256": digest,
            "auditPath": str(audit_path), "auditSha256": sha(audit_path), "sourceLockSha256": sha(lock_path),
            "qualityCertificatePath": str(quality_path), "qualityCertificateSha256": sha(quality_path),
            "packCertificatePath": str(contract_path), "packCertificateSha256": sha(contract_path),
            "correctedAstcAdapterNewImageBytes": contract["correctedAstcAdapterNewImageBytes"],
            "additionalUnalignedBasisRuntimeRgbaImages": contract["additionalUnalignedBasisRuntimeRgbaImages"],
            "memoryScope": contract["scope"],
            "models": build_rows, "physicalAndroidAcceptanceInferred": False, "gpuAcceptanceInferred": False}


def run(args):
    source, candidate = Path(args.source_pack).resolve(), Path(args.candidate_pack).resolve()
    output = Path(args.output).resolve()
    require(not output.exists() and not output.is_relative_to(source) and not output.is_relative_to(candidate),
            "Bridge output must be fresh and outside both packs")
    build_path = Path(args.build_lock).resolve(); build = read(build_path)
    source_snapshot, candidate_snapshot = files(source), files(candidate)
    require(build.get("pass") is True and build.get("candidateFiles") == source_snapshot,
            "Base pack differs from full BUILD-LOCK")
    certificate_path, certificate = checked_certificate(args.certificate, args.certificate_sha256, "Response preservation")
    require(certificate.get("format") == 1 and certificate.get("sourcePack") == str(source)
            and certificate.get("candidatePack") == str(candidate), "Independent response certificate pack paths differ")
    require(certificate.get("sourceManifestSha256") == sha(source / "characters.json")
            and certificate.get("candidateManifestSha256") == sha(candidate / "characters.json"),
            "Independent response certificate manifests differ")
    require(certificate.get("newWholeFileRuntimeAcceptanceInferred") is False,
            "Independent certificate must not imply new-hash runtime acceptance")
    original_manifest, manifest = read(source / "characters.json"), read(candidate / "characters.json")
    originals, entries = indexed(original_manifest["models"]), indexed(manifest["models"])
    require(set(originals) == set(entries), "Response candidate changes roster identities")
    require(len(entries) == build.get("entries") == certificate.get("modelCount"), "Full roster coverage differs")
    require(set(certificate["assets"]) == {entry["model"] for entry in entries.values()}, "Certificate model coverage differs")
    report_path = candidate / "MATERIAL-RESPONSE-CANDIDATE.json"; report = read(report_path)
    require(report.get("pass") is True and report.get("accepted") is False
            and report.get("candidatePack") == str(candidate) and report.get("sourcePack") == str(source),
            "Candidate receipt identity/status differs")
    require(report.get("profileSha256") == certificate.get("profileSha256")
            and report.get("inputLockSha256") == certificate.get("inputLockSha256"), "Independent certificate input/profile differs")
    actual_report_files = dict(candidate_snapshot); actual_report_files.pop(report_path.name)
    require(report.get("candidatePackFiles") == actual_report_files, "Material candidate current files differ from receipt")
    review_root, lod1_root = Path(args.review_root).resolve(), Path(args.lod1_root).resolve()
    prepared, guards, samus = [], {}, {}
    for k, entry in sorted(entries.items()):
        old_entry = originals[k]; relative = entry["model"]
        require(relative == old_entry["model"], f"Response changes model identity: {k}")
        normalized = copy.deepcopy(old_entry)
        if entry["hunter"] != "Samus": normalized.pop("mobileModel", None)
        require(entry == normalized, f"Response changes native manifest contract: {k}")
        row = certificate["assets"][relative]
        original_sha, response_sha = sha(guarded(source, relative)), sha(guarded(candidate, relative))
        require(row.get("pass") is True and row.get("originalGlbSha256") == original_sha
                and row.get("responseGlbSha256") == response_sha and row.get("hunter") == entry["hunter"]
                and row.get("part").lower() == entry["part"].lower(), f"Response proof hashes/identity differ: {relative}")
        if entry["hunter"] == "Samus":
            require(row.get("wholeFileByteExact") is True and original_sha == response_sha, "Samus desktop baseline changed")
            samus[relative] = response_sha; continue
        require(entry["hunter"] in HUNTERS and all(row.get(flag) is True for flag in GEOMETRY_FLAGS),
                f"Independent immutable geometry/native/artwork proof incomplete: {relative}")
        require(row.get("newWholeFileRuntimeAcceptanceInferred") is False and row.get("oldReceiptsRelabeled") is False,
                f"Response certificate relabels historical evidence: {relative}")
        require(len(row.get("unchangedGeometryContractSha256", "")) == 64
                and len(row.get("immutableOriginalDocumentSha256", "")) == 64,
                f"Response certificate lacks immutable contract hashes: {relative}")
        require(all(value.get("independentResponseFormulaPass") is True and value.get("redAndAlphaExact") is True
                    and value.get("sourceSelfIllumAndUntunedRegionsExact") is True for value in row.get("allowedMaterialMaps", [])),
                f"Independent material replay failed: {relative}")
        audit_path = old_audit(entry, build, review_root, lod1_root).resolve()
        audit_sha = sha(audit_path); old = read(audit_path); guards[str(audit_path)] = audit_sha
        require(old.get("pass") is True and old.get("hunter") == entry["hunter"], f"Original static audit identity/pass differs: {audit_path}")
        hash_field = "viewmodelSha256" if entry["part"].lower() == "viewmodel" else "shippingGlbSha256"
        require(old.get(hash_field) == original_sha, f"Original static audit shipping SHA differs from base: {audit_path}")
        require(any(Path(proof["receiptPath"]).resolve() == audit_path and proof["receiptSha256"] == audit_sha
                    and proof["exactOldWholeFileHashField"] == hash_field for proof in row.get("originalExactHashStaticReceipts", [])),
                f"Chosen production audit is not certified against exact old model: {audit_path}")
        prepared.append((entry, row, old, audit_path, audit_sha, hash_field))
    require(len(prepared) == 25 and len(samus) == 4, "Expected 25 non-Samus sources and four preserved Samus entries")
    for relative, digest in source_snapshot.items():
        if relative.startswith(("samus/", "mobile/samus/")):
            require(candidate_snapshot.get(relative) == digest, f"Samus baseline file changed: {relative}")
            samus[relative] = digest
    mobile = validate_mobile(args, candidate, certificate_path, args.certificate_sha256.lower())
    result = {"format": 1, "pass": True, "accepted": False, "sourcePack": str(source), "candidatePack": str(candidate),
              "sourceManifestSha256": sha(source / "characters.json"), "candidateManifestSha256": sha(candidate / "characters.json"),
              "buildLockPath": str(build_path), "buildLockSha256": sha(build_path), "toolSha256": sha(__file__),
              "certificatePath": str(certificate_path), "certificateSha256": sha(certificate_path),
              "materialResponseReceiptPath": str(report_path), "materialResponseReceiptSha256": sha(report_path),
              "samusPreservedFiles": samus, "assets": {}, "newHashRuntimeAcceptanceInferred": False,
              "physicalAndroidAcceptanceInferred": False, "scope": SCOPE}
    output.mkdir(parents=True)
    try:
        for entry, proof, old, audit_path, audit_sha, hash_field in prepared:
            relative = entry["model"]; k = key(entry)
            bridge = copy.deepcopy(old)
            # New proof is additive. Every old evidence field keeps its original
            # value, except the explicitly listed shipping whole-file field.
            bridge[hash_field] = proof["responseGlbSha256"]
            bridge["shippingGlbSha256"] = proof["responseGlbSha256"]
            bridge["scope"] = SCOPE; bridge["accepted"] = False
            bridge["materialResponseBridge"] = {
                "originalAuditPath": str(audit_path), "originalAuditSha256": audit_sha,
                "originalAuditScope": old.get("scope"), "originalShippingHashField": hash_field,
                "originalShippingGlbSha256": proof["originalGlbSha256"],
                "candidateShippingGlbSha256": proof["responseGlbSha256"],
                "certificatePath": str(certificate_path), "certificateSha256": sha(certificate_path),
                "unchangedGeometryContractSha256": proof["unchangedGeometryContractSha256"],
                "immutableOriginalDocumentSha256": proof["immutableOriginalDocumentSha256"],
                "profileSha256": certificate["profileSha256"], "inputLockSha256": certificate["inputLockSha256"],
                "allowedMaterialMaps": proof["allowedMaterialMaps"],
                "oldAcceptanceEvidenceIsHistorical": True, "newHashRuntimeAcceptanceInferred": False,
                "scope": SCOPE}
            if mobile:
                model = mobile["models"][k]
                bridge["shippingMobileGlbSha256"] = model["mobileSha256"]
                bridge["mobileTextureBridge"] = {name: value for name, value in mobile.items() if name != "models"}
                bridge["mobileTextureBridge"].update({"desktopGlbSha256": model["sourceSha256"],
                    "mobileGlbSha256": model["mobileSha256"], "mobileModel": model["mobileModel"]})
            destination = output / entry["hunter"].lower() / entry["part"].lower() / f"lod{entry.get('lod', 0)}" / "audit.json"
            destination.parent.mkdir(parents=True)
            destination.write_text(json.dumps(bridge, indent=2)+"\n")
            identity = f"{entry['hunter'].lower()}/{entry['part']}/{entry.get('lod', 0)}"
            result["assets"][identity] = {"hunter": entry["hunter"], "part": entry["part"], "lod": entry.get("lod", 0),
                "model": relative, "auditPath": str(destination), "auditSha256": sha(destination),
                "originalAuditPath": str(audit_path), "originalAuditSha256": audit_sha,
                "originalGlbSha256": proof["originalGlbSha256"], "shippingGlbSha256": proof["responseGlbSha256"],
                "sourceGlbSha256": old.get("sourceGlbSha256", old.get("sourceSha256")),
                "unchangedGeometryContractSha256": proof["unchangedGeometryContractSha256"]}
            source_glb = original_source_path(old)
            if source_glb:
                result["assets"][identity]["sourceGlb"] = source_glb
                guards[source_glb] = result["assets"][identity]["sourceGlbSha256"]
            if mobile: result["assets"][identity]["shippingMobileGlbSha256"] = mobile["models"][k]["mobileSha256"]
        require(files(source) == source_snapshot and files(candidate) == candidate_snapshot,
                "Source/candidate pack changed during bridge generation")
        require(sha(certificate_path) == args.certificate_sha256.lower()
                and all(sha(path) == digest for path, digest in guards.items()), "Original proof changed during bridge generation")
        if mobile:
            require(all(sha(mobile[path_field]) == mobile[hash_field] for path_field, hash_field in (
                ("buildResultPath", "buildResultSha256"), ("auditPath", "auditSha256"),
                ("qualityCertificatePath", "qualityCertificateSha256"), ("packCertificatePath", "packCertificateSha256"))),
                "Mobile proof changed during bridge generation")
            result["mobileTextureBridge"] = {name: value for name, value in mobile.items() if name != "models"}
        result["bridgeFiles"] = files(output)
        (output / "BRIDGED-AUDITS.json").write_text(json.dumps(result, indent=2)+"\n")
    except Exception:
        (output / "FAILED.json").write_text(json.dumps({"pass": False, "scope": "Incomplete output; no audit bridge may be used."})+"\n")
        raise
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("source-pack", "candidate-pack", "build-lock", "review-root", "lod1-root", "certificate", "certificate-sha256", "output"):
        parser.add_argument("--"+name, required=True)
    for name in ("mobile-build", "mobile-certificate", "mobile-certificate-sha256", "mobile-pack-certificate", "mobile-pack-certificate-sha256"):
        parser.add_argument("--"+name)
    args = parser.parse_args()
    result = run(args)
    print(json.dumps({"pass": True, "accepted": False, "bridgedAssets": len(result["assets"]),
                      "mobileAssets": sum("shippingMobileGlbSha256" in row for row in result["assets"].values()),
                      "output": str(Path(args.output).resolve())}))


if __name__ == "__main__":
    main()
