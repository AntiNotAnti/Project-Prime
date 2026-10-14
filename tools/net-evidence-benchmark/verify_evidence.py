#!/usr/bin/env python3
"""Verify portable derivatives, raw measured rows and identity/scope separation."""
import argparse
import hashlib
import json
from pathlib import Path
import re


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    args = parser.parse_args(); base = args.evidence.resolve()
    manifest = json.loads((base / "manifest.json").read_text())
    assert manifest["kind"] == "sanitized_derived_evidence_package"
    assert manifest["rawOriginalsIncluded"] is False
    for artifact in manifest["derivedFiles"]:
        path = (base / artifact["relativePath"]).resolve()
        assert path.is_relative_to(base) and path.is_file()
        data = path.read_bytes()
        assert len(data) == artifact["bytes"]
        assert hashlib.sha256(data).hexdigest() == artifact["sha256"]
        derivative = json.loads(data)
        assert derivative["kind"] == "sanitized_derivative"
        assert derivative["original"]["sha256"] == artifact["originalSha256"]
    batches = 0
    for benchmark in manifest["benchmarks"]:
        label = benchmark["label"]
        result = json.loads((base / label / "results.sanitized.json").read_text())["payload"]
        receipt = json.loads((base / label / "receipt.sanitized.json").read_text())["payload"]
        lines = json.loads((base / label / "batch-lines.sanitized.json").read_text())["payload"]["lines"]
        recorded = {}
        for line in lines:
            prefix, name, raw = line.split(" ", 2)
            assert prefix == "RAW"
            recorded.setdefault(name, []).append(json.loads(raw))
        assert len(result["Cases"]) == benchmark["cases"]
        assert receipt["success"] and receipt["source_clean_stable"]
        assert receipt.get("qualified_binaries_unchanged", receipt.get("physical_inputs_unchanged", False))
        assert receipt.get("benchmark_source_clean_stable", True)
        assert all(process["owned_group_closed"] for process in receipt["processes"])
        assert result["QualifiedActualClient"]["Sha256"] != result["ThinBenchmarkEntry"]["Sha256"]
        assert result["FullNativeFixtureIdentityOnly"]["Mvid"] != result["ThinBenchmarkEntry"]["Mvid"]
        for case in result["Cases"]:
            assert recorded[case["Name"]] == case["Raw"]
            assert [row["Index"] for row in case["Raw"]] == list(range(30))
            assert all(row["Operations"] == case["OperationsPerBatch"] for row in case["Raw"])
            assert all(row["WallMs"] >= 0 and row["ProcessCpuMs"] >= 0 and row["CallerAllocatedBytes"] >= 0 for row in case["Raw"])
        rows = sum(len(case["Raw"]) for case in result["Cases"])
        assert rows == benchmark["rawBatches"] == len(lines)
        batches += rows
    failed_batches = 0
    for benchmark in manifest.get("failedBenchmarks", []):
        label = benchmark["label"]
        receipt = json.loads((base / label / "receipt.sanitized.json").read_text())["payload"]
        lines = json.loads((base / label / "batch-lines.sanitized.json").read_text())["payload"]["lines"]
        assert receipt["success"] is False and receipt.get("error")
        assert all(process["owned_group_closed"] for process in receipt["processes"])
        assert receipt["source_before"] == receipt["source_after"]
        assert receipt["physical_inputs_before"] == receipt["physical_inputs_after"]
        assert receipt["benchmark_source_before"] == receipt["benchmark_source_after"]
        recorded = {}
        for line in lines:
            prefix, name, raw = line.split(" ", 2)
            assert prefix == "RAW"
            recorded.setdefault(name, []).append(json.loads(raw))
        assert len(recorded) == benchmark["cases"]
        for rows in recorded.values():
            assert [row["Index"] for row in rows] == list(range(30))
            assert all(row["Operations"] == rows[0]["Operations"] and row["Operations"] > 0 for row in rows)
            assert all(row["WallMs"] >= 0 and row["ProcessCpuMs"] >= 0 and row["CallerAllocatedBytes"] >= 0 for row in rows)
        assert len(lines) == benchmark["rawBatches"]
        failed_batches += len(lines)
    for path in base.rglob("*"):
        if not path.is_file(): continue
        assert path.suffix not in {".dll", ".pdb", ".dylib", ".so", ".ppdemo", ".zip", ".png", ".wav"}
        assert not re.search(r"/Users/|/home/|/private/var/|sk-proj-[A-Za-z0-9_-]{15,}", path.read_text())
    print(json.dumps({"success": True, "derivedHashes": len(manifest["derivedFiles"]),
                      "rawBatches": batches, "failedAttemptRawBatches": failed_batches, "negativeScopes": len(manifest["negativeEvidence"])}))


if __name__ == "__main__": main()
