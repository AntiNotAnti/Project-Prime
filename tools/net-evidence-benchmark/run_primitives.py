#!/usr/bin/env python3
"""Fresh friend entry against an explicitly qualified engine; no engine source rebuild."""
import argparse
import datetime
import json
import math
import os
from pathlib import Path
import re
import shutil
import signal
import sys
import uuid
import xml.etree.ElementTree as ET

# Same portable log/digest and owned process-group policy as the archival harness.
from run import portable, run_owned, sanitize_log, sha, source_snapshot


def clean_environment(original, sdk):
    env = {k: v for k, v in original.items() if not k.startswith("PRIME_")}
    env.update({"DOTNET_ROOT": str(sdk.parent), "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
                "DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER": "1", "MSBUILDDISABLENODEREUSE": "1",
                "PRIME_SHOT_PROOF": "shadow"})
    return env


def validate_qualification(document, head, client_sha, client_mvid):
    q = document.get("payload", document)
    before = q.get("sourceBefore", q.get("source_before", {}))
    after = q.get("sourceAfter", q.get("source_after", {}))
    actual = q.get("actualLoadedClient", q.get("actualCompiledClient", {}))
    if not q.get("status", "").startswith("PASS"):
        raise ValueError("Require passing full qualification")
    if before.get("commit", before.get("head")) != head or after != before:
        raise ValueError("Qualification must identify exact stable full source HEAD")
    if before.get("dirty", before.get("status_porcelain", "")):
        raise ValueError("Qualification source is dirty")
    if actual.get("sha256") != client_sha or actual.get("mvid", "").lower() != client_mvid.lower():
        raise ValueError("Qualification client identity mismatch")
    for field in ("sourceUnchanged", "runtimeUnchanged", "allOwnedGroupsClosed"):
        if q.get(field) is False:
            raise ValueError("Qualification explicitly failed " + field)
    return {"status": q["status"], "source": before, "actual_client": actual}


def require_external_fresh_output(repo, out):
    if out == repo or repo in out.parents:
        raise ValueError("Output must be outside source checkout")
    if out.exists():
        raise ValueError("Output exists; preserve failures/repeats with a fresh directory")


def validate_result(result, client_sha, client_mvid, fixture_sha, fixture_mvid):
    for field, digest, mvid in (("QualifiedActualClient", client_sha, client_mvid),
                               ("FullNativeFixtureIdentityOnly", fixture_sha, fixture_mvid)):
        identity = result[field]
        if identity["Sha256"] != digest or identity["Mvid"].lower() != mvid.lower():
            raise ValueError("Result identity mismatch " + field)
    entry = result["ThinBenchmarkEntry"]
    if entry["Sha256"] == fixture_sha or entry["Mvid"].lower() == fixture_mvid.lower():
        raise ValueError("Thin entry aliases full fixture")
    expected = {"diagnostics_disabled_hook_bundle", "claim_scope_exact_key", "constructed_witness_scope",
                "typed_claim_payment_in_page", "typed_claim_payment_new_page", "full_row_and_byte_budget_refusal"}
    if len(result["Cases"]) != 6 or {c["Name"] for c in result["Cases"]} != expected:
        raise ValueError("Require exact six cases")
    if result["Samples"] != 30 or result["WarmupBatches"] != 8:
        raise ValueError("Require 30 raw batches and eight warmups")
    for c in result["Cases"]:
        if len(c["Raw"]) != 30 or [s["Index"] for s in c["Raw"]] != list(range(30)):
            raise ValueError("Missing/reordered raw batches")
        if any(s["Operations"] != c["OperationsPerBatch"] or s["Operations"] <= 0
               or s["CallerAllocatedBytes"] < 0 or s["WallMs"] < 0 or s["ProcessCpuMs"] < 0
               or not math.isfinite(s["WallMs"]) or not math.isfinite(s["ProcessCpuMs"])
               or s["SetupCallerAllocatedBytes"] < 0 or s["SetupWallMs"] < 0 or not math.isfinite(s["SetupWallMs"]) for s in c["Raw"]):
            raise ValueError("Invalid measured counters")
        if c["Name"] == "typed_claim_payment_new_page":
            if any(s["CallerAllocatedBytes"] <= 0 for s in c["Raw"]):
                raise ValueError("Actual new-page allocation missing")
        elif any(s["CallerAllocatedBytes"] != 0 for s in c["Raw"]):
            raise ValueError("Warmed primitive unexpectedly allocated")
    for field in ("SemanticAssertionsPassed", "DisabledRecorderStayedAbsent", "ActualClientUnchanged",
                  "EntryUnchanged", "FullFixtureUnchanged", "DependenciesUnchanged"):
        if result.get(field) is not True:
            raise ValueError("Failed primitive assertion " + field)
    if result.get("ProofMode") != "Shadow":
        raise ValueError("Proof mode changed")


def write_review(result, path):
    lines = ["# Constructed primitive overhead", "", result["Scope"], "",
             "Batch percentiles describe 30 batch totals after eight warmups. No operation/frame tail or FPS claim.", "",
             "| Case | Unit | Units/batch | Batch median ms | Batch p95 ms | Caller bytes/unit (mean) |", "|---|---|---:|---:|---:|---:|"]
    for c in result["Cases"]:
        p = c["BatchWallMs"]
        lines.append(f'| {c["Name"]} | {c["Unit"]} | {c["OperationsPerBatch"]} | {p["Median"]:.6f} | {p["P95"]:.6f} | {c["MeanCallerAllocatedBytesPerUnit"]:.6f} |')
    for c in result["Cases"]:
        lines += ["", c["Name"] + ": " + c["SetupPolicy"],
                  "Batch wall ms: `" + json.dumps([s["WallMs"] for s in c["Raw"]], separators=(",", ":")) + "`",
                  "Caller bytes/batch: `" + json.dumps([s["CallerAllocatedBytes"] for s in c["Raw"]], separators=(",", ":")) + "`"]
    path.write_text("\n".join(lines) + "\n")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("repo", "runtime", "donor", "sdk", "out", "qualification-receipt", "build-receipt"):
        parser.add_argument("--" + name, type=Path, required=True)
    for name in ("head", "client-sha", "client-mvid", "fixture-sha", "fixture-mvid"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--execution-slot-released", action="store_true", required=True)
    args = parser.parse_args(argv)
    for name in ("repo", "runtime", "donor", "sdk", "out", "qualification_receipt", "build_receipt"):
        setattr(args, name, getattr(args, name).resolve())
    for name in ("client_sha", "fixture_sha"):
        value = getattr(args, name).lower()
        if not re.fullmatch(r"[0-9a-f]{64}", value):
            raise ValueError("Require exact SHA256 for " + name)
        setattr(args, name, value)
    if not re.fullmatch(r"[0-9a-f]{40}", args.head):
        raise ValueError("Require full commit SHA")
    for name in ("client_mvid", "fixture_mvid"):
        setattr(args, name, str(uuid.UUID(getattr(args, name))))
    require_external_fresh_output(args.repo, args.out)
    args.out.mkdir(parents=True)
    prep = Path(__file__).resolve().parent
    labels = {str(args.repo): "$SOURCE", str(args.runtime): "$QUALIFIED_RUNTIME", str(args.donor): "$DEPENDENCIES",
              str(args.sdk): "$DOTNET", str(args.out): "$EVIDENCE", str(prep): "$BENCHMARK_SOURCE",
              str(args.qualification_receipt): "$QUALIFICATION_RECEIPT", str(args.build_receipt): "$BUILD_RECEIPT"}
    receipt = {"schema": 1, "success": False, "scope": "constructed primitive overhead only; no native/admission/payment/FPS coverage",
               "utc_started": datetime.datetime.now(datetime.timezone.utc).isoformat(),
               "args": {k: str(v) if isinstance(v, Path) else v for k, v in vars(args).items()}, "processes": [], "logs": []}
    binaries = {}
    def interrupted(signum, frame):
        raise KeyboardInterrupt("Received signal " + str(signum))
    prior_handler = signal.signal(signal.SIGTERM, interrupted)
    try:
        before = source_snapshot(args.repo); receipt["source_before"] = before
        if before["head"] != args.head or before["status_porcelain"]:
            raise ValueError("Require exact qualified clean source HEAD")
        receipt["qualification"] = validate_qualification(json.loads(args.qualification_receipt.read_text()),
            args.head, args.client_sha, args.client_mvid)
        receipt["qualification_receipt_sha256"] = sha(args.qualification_receipt)
        # This is a hashed provenance document, not a claimed re-execution of its build.
        json.loads(args.build_receipt.read_text())
        receipt["build_receipt_sha256"] = sha(args.build_receipt)
        client, fixture = args.runtime / "ProjectPrime.dll", args.runtime / "nettest.dll"
        dependencies = [p for p in sorted(args.donor.glob("*.dll")) if p.name not in {"ProjectPrime.dll", "nettest.dll"}]
        if not any(p.name == "OpenTK.Mathematics.dll" for p in dependencies):
            raise ValueError("Dependency donor lacks OpenTK.Mathematics.dll")
        binaries = {str(p): sha(p) for p in [client, fixture] + dependencies}
        receipt["physical_inputs_before"] = binaries
        if binaries[str(client)] != args.client_sha or binaries[str(fixture)] != args.fixture_sha:
            raise ValueError("Physical qualified input SHA mismatch")
        project_dir = args.out / "external-entry-source"; project_dir.mkdir()
        shutil.copy2(prep / "primitives" / "Program.cs", project_dir / "Program.cs")
        receipt["thin_entry_source_sha256"] = sha(project_dir / "Program.cs")
        receipt["launcher_sources"] = [{"file": name, "sha256": sha(prep / name)} for name in ("run_primitives.py", "run.py")]
        project = ET.Element("Project", Sdk="Microsoft.NET.Sdk"); props = ET.SubElement(project, "PropertyGroup")
        for key, value in {"OutputType": "Exe", "TargetFramework": "net10.0", "Nullable": "enable", "AssemblyName": "nettest",
                           "EnableDefaultCompileItems": "false", "UseAppHost": "false", "UseSharedCompilation": "false",
                           "EnableNETAnalyzers": "false", "Deterministic": "true"}.items():
            ET.SubElement(props, key).text = value
        items = ET.SubElement(project, "ItemGroup"); ET.SubElement(items, "Compile", Include="Program.cs")
        for p in [client] + dependencies:
            ref = ET.SubElement(items, "Reference", Include=p.stem)
            ET.SubElement(ref, "HintPath").text = str(p); ET.SubElement(ref, "Private").text = "true"
        ET.indent(project); proj = project_dir / "benchmark.csproj"
        ET.ElementTree(project).write(proj, encoding="utf-8", xml_declaration=True)
        runtime = args.out / "runtime"; env = clean_environment(os.environ, args.sdk)
        def owned(command, cwd, log_name):
            log = args.out / log_name
            try:
                process = run_owned(command, cwd, log, 300, env); receipt["processes"].append(process)
            finally:
                if log.exists():
                    receipt["logs"].append(sanitize_log(log, labels))
            if process["returncode"] or process["timeout"] or process["interrupted"] or not process["owned_group_closed"] or process["leftover_group_after_parent_exit"]:
                raise RuntimeError("Owned process failed or did not close normally: " + log_name)
        owned([str(args.sdk), "build", str(proj), "-c", "Release", "-o", str(runtime), "--nologo",
               "-p:UseSharedCompilation=false", "-nodeReuse:false"], project_dir, "external-entry-build.log")
        if sha(runtime / "ProjectPrime.dll") != args.client_sha:
            raise ValueError("Entry copied wrong engine")
        for dependency in dependencies:
            if sha(runtime / dependency.name) != binaries[str(dependency)]:
                raise ValueError("Entry copied wrong dependency " + dependency.name)
        result_path = args.out / "results.json"
        owned([str(args.sdk), str(runtime / "nettest.dll"), str(result_path), args.client_sha, args.client_mvid,
               str(fixture), args.fixture_sha, args.fixture_mvid], runtime, "primitive-raw.log")
        result = json.loads(result_path.read_text())
        validate_result(result, args.client_sha, args.client_mvid, args.fixture_sha, args.fixture_mvid)
        receipt["identities"] = {k: result[k] for k in ("QualifiedActualClient", "ThinBenchmarkEntry", "FullNativeFixtureIdentityOnly", "PhysicalRuntimeDependencies")}
        if result["ThinBenchmarkEntry"]["Sha256"] != sha(runtime / "nettest.dll"):
            raise ValueError("Reported thin entry differs from executed physical file")
        after = source_snapshot(args.repo); receipt["source_after"] = after
        receipt["physical_inputs_after"] = {p: sha(Path(p)) for p in binaries}
        if after != before or receipt["physical_inputs_after"] != binaries:
            raise ValueError("Qualified source/physical inputs changed during run")
        receipt["source_clean_stable"] = True; receipt["physical_inputs_unchanged"] = True
        write_review(result, args.out / "review.md")
        receipt["raw_files"] = [{"file": name, "sha256": sha(args.out / name)} for name in ("results.json", "review.md", "external-entry-source/Program.cs")]
        receipt["all_owned_groups_closed"] = all(p["owned_group_closed"] for p in receipt["processes"])
        receipt["success"] = True
        print(json.dumps({"success": True, "receipt": str(args.out / "receipt.json"), "raw_batches": 180}))
        return 0
    except (Exception, KeyboardInterrupt) as error:
        receipt["error"] = type(error).__name__ + ": " + str(error)
        try:
            receipt["source_after"] = source_snapshot(args.repo)
            receipt["physical_inputs_after"] = {p: sha(Path(p)) for p in binaries}
        except Exception as post_error:
            receipt["postcheck_error"] = str(post_error)
        receipt["all_owned_groups_closed"] = all(p["owned_group_closed"] for p in receipt["processes"])
        print(receipt["error"], file=sys.stderr)
        return 1
    finally:
        receipt["utc_ended"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        (args.out / "receipt.json").write_text(json.dumps(portable(receipt, labels), indent=2) + "\n")
        signal.signal(signal.SIGTERM, prior_handler)


if __name__ == "__main__":
    sys.exit(main())
