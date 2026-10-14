"""Light falsifiers for launcher identity, scope, samples and preservation gates."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).parent))
import run_primitives as runner

HEAD = "1" * 40
CLIENT = "2" * 64
FIXTURE = "3" * 64
MVID = "00000000-0000-0000-0000-000000000001"
FMVID = "00000000-0000-0000-0000-000000000002"


def qualification():
    source = {"commit": HEAD, "tree": "tree", "dirty": ""}
    return {"status": "PASS_FOCUSED", "sourceBefore": source, "sourceAfter": source.copy(),
            "actualLoadedClient": {"sha256": CLIENT, "mvid": MVID}, "sourceUnchanged": True,
            "physicalHashesAfter": {"ProjectPrime.dll": CLIENT, "nettest.dll": FIXTURE}, "nettestMvid": FMVID}


def result():
    names = ["diagnostics_disabled_hook_bundle", "claim_scope_exact_key", "constructed_witness_scope",
             "typed_claim_payment_in_page", "typed_claim_payment_new_page", "full_row_and_byte_budget_refusal"]
    cases = [{"Name": n, "Unit": "constructed pair", "SetupPolicy": "outside timing", "OperationsPerBatch": 32,
              "MeanCallerAllocatedBytesPerUnit": 1 if n.endswith("new_page") else 0,
              "BatchWallMs": {"Median": 1, "P95": 2},
              "Raw": [{"Index": i, "Operations": 32, "CallerAllocatedBytes": 32 if n.endswith("new_page") else 0,
                       "WallMs": 1.0, "ProcessCpuMs": 0.0, "SetupWallMs": .2, "SetupCallerAllocatedBytes": 48}
                      for i in range(30)]} for n in names]
    return {"QualifiedActualClient": {"Sha256": CLIENT, "Mvid": MVID},
            "FullNativeFixtureIdentityOnly": {"Sha256": FIXTURE, "Mvid": FMVID},
            "ThinBenchmarkEntry": {"Sha256": "4" * 64, "Mvid": "00000000-0000-0000-0000-000000000003"},
            "Samples": 30, "WarmupBatches": 8, "Cases": cases, "ProofMode": "Shadow", "Scope": "constructed primitives only",
            **dict.fromkeys(["SemanticAssertionsPassed", "DisabledRecorderStayedAbsent", "ActualClientUnchanged",
                            "EntryUnchanged", "FullFixtureUnchanged", "DependenciesUnchanged"], True)}


class PrimitiveRunnerTests(unittest.TestCase):
    def validate(self, data):
        runner.validate_result(data, CLIENT, MVID, FIXTURE, FMVID)

    def test_valid_fixture_identity_and_complete_raw_samples(self):
        runner.validate_qualification(qualification(), HEAD, CLIENT, MVID, FIXTURE, FMVID)
        self.validate(result())

    def test_qualification_failure_dirty_drift_and_wrong_engine_fail(self):
        for mutate in [lambda q: q.update(status="FAIL"), lambda q: q["sourceBefore"].update(dirty="M source"),
                       lambda q: q["sourceAfter"].update(commit="f" * 40),
                       lambda q: q["actualLoadedClient"].update(sha256=FIXTURE),
                       lambda q: q.update(allOwnedGroupsClosed=False)]:
            q = qualification(); mutate(q)
            with self.assertRaises(ValueError): runner.validate_qualification(q, HEAD, CLIENT, MVID, FIXTURE, FMVID)

    def test_full_fixture_must_be_bound_to_qualification_before_launch(self):
        for mutate in [lambda q: q["physicalHashesAfter"].pop("nettest.dll"),
                       lambda q: q["physicalHashesAfter"].update({"nettest.dll": CLIENT}),
                       lambda q: q.pop("nettestMvid"), lambda q: q.update(nettestMvid=MVID),
                       lambda q: q["physicalHashesAfter"].update({"ProjectPrime.dll": FIXTURE})]:
            q = qualification(); mutate(q)
            with self.assertRaisesRegex(ValueError, "Qualification.*(?:fixture|engine)"):
                runner.validate_qualification(q, HEAD, CLIENT, MVID, FIXTURE, FMVID)

    def test_wrong_qualified_full_fixture_stops_before_any_process(self):
        with tempfile.TemporaryDirectory() as tmp:
            base = Path(tmp); repo = base / "engine-source"; repo.mkdir()
            qualification_path = base / "qualification.json"
            q = qualification(); q["nettestMvid"] = MVID
            qualification_path.write_text(json.dumps(q))
            snapshot = {"head": HEAD, "tree": "tree", "status_porcelain": ""}
            benchmark_head = "f" * 40
            benchmark_snapshot = dict(snapshot, head=benchmark_head)
            arguments = ["--repo", str(repo), "--runtime", str(base / "runtime"), "--donor", str(base / "donor"),
                         "--sdk", str(base / "dotnet"), "--out", str(base / "fresh-evidence"),
                         "--qualification-receipt", str(qualification_path), "--build-receipt", str(base / "build.json"),
                         "--head", HEAD, "--benchmark-head", benchmark_head, "--client-sha", CLIENT, "--client-mvid", MVID,
                         "--fixture-sha", FIXTURE, "--fixture-mvid", FMVID, "--execution-slot-released"]
            with mock.patch.object(runner, "source_snapshot", side_effect=lambda path: snapshot if Path(path).resolve() == repo.resolve() else benchmark_snapshot), mock.patch.object(runner, "run_owned") as owned:
                self.assertEqual(runner.main(arguments), 1)
                owned.assert_not_called()
            failure = json.loads((base / "fresh-evidence" / "receipt.json").read_text())
            self.assertFalse(failure["success"])
            self.assertEqual(failure["processes"], [])
            self.assertIn("Qualification full fixture", failure["error"])
            self.assertNotEqual(failure["source_before"]["head"], failure["benchmark_source_before"]["head"])

    def test_declared_fixture_field_aliases_are_supported(self):
        q = qualification()
        q["physical_hashes_after"] = q.pop("physicalHashesAfter")
        q["nettest_mvid"] = q.pop("nettestMvid").upper()
        validated = runner.validate_qualification({"payload": q}, HEAD, CLIENT, MVID, FIXTURE, FMVID)
        self.assertEqual(validated["full_fixture"]["sha256"], FIXTURE)

    def test_thin_full_fixture_alias_and_wrong_mvid_fail(self):
        for mutate in [lambda r: r.update(ThinBenchmarkEntry=r["FullNativeFixtureIdentityOnly"].copy()),
                       lambda r: r["QualifiedActualClient"].update(Mvid=FMVID)]:
            r = result(); mutate(r)
            with self.assertRaises(ValueError): self.validate(r)

    def test_raw_sample_removal_reorder_and_case_duplicates_fail(self):
        for mutate in [lambda r: r["Cases"][0]["Raw"].pop(), lambda r: r["Cases"][0]["Raw"].reverse(),
                       lambda r: r["Cases"].pop(), lambda r: r["Cases"][0].update(Name=r["Cases"][1]["Name"])]:
            r = result(); mutate(r)
            with self.assertRaises(ValueError): self.validate(r)

    def test_actual_new_page_cost_remains_required(self):
        r = result(); r["Cases"][4]["Raw"][0]["CallerAllocatedBytes"] = 0
        with self.assertRaises(ValueError): self.validate(r)

    def test_every_case_nonzero_observations_are_preserved_without_zero_gate(self):
        r = result()
        for c in r["Cases"]:
            c["Raw"][0]["CallerAllocatedBytes"] = 1360
        self.validate(r)
        self.assertTrue(all(c["Raw"][0]["CallerAllocatedBytes"] == 1360 for c in r["Cases"]))

    def test_negative_or_wrong_operation_counters_fail(self):
        for field, value in [("Operations", 0), ("WallMs", -1), ("ProcessCpuMs", -1), ("CallerAllocatedBytes", -1), ("WallMs", float("nan")),
                             ("SetupWallMs", float("inf")), ("SetupCallerAllocatedBytes", -1)]:
            r = result(); r["Cases"][0]["Raw"][0][field] = value
            with self.assertRaises(ValueError): self.validate(r)

    def test_semantic_failure_or_enforce_cannot_pass(self):
        for field, value in [("SemanticAssertionsPassed", False), ("DisabledRecorderStayedAbsent", False), ("ProofMode", "Enforce")]:
            r = result(); r[field] = value
            with self.assertRaises(ValueError): self.validate(r)

    def test_environment_removes_inherited_campaign_and_promotion(self):
        env = runner.clean_environment({"PATH": "system", "PRIME_SHOT_PROOF": "enforce",
                                       "PRIME_NET2_EVIDENCE": "private", "PRIME_NET2_SOAK": "1"}, Path("/sdk/dotnet"))
        self.assertEqual(env["PRIME_SHOT_PROOF"], "shadow")
        self.assertNotIn("PRIME_NET2_EVIDENCE", env)
        self.assertNotIn("PRIME_NET2_SOAK", env)
        self.assertEqual(env["PATH"], "system")

    def test_existing_output_and_source_output_never_overwritten(self):
        with tempfile.TemporaryDirectory() as tmp:
            base = Path(tmp); repo = base / "repo"; repo.mkdir(); prior = base / "prior"; prior.mkdir()
            for target in [repo, repo / "child", prior]:
                with self.assertRaises(ValueError): runner.require_external_fresh_output(repo, target)
            runner.require_external_fresh_output(repo, base / "fresh")

    def test_review_keeps_all_sample_arrays_and_batch_labels(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "review.md"; runner.write_review(result(), path)
            text = path.read_text()
            self.assertEqual(text.count("Batch wall ms:"), 6)
            self.assertEqual(text.count("Caller bytes/batch:"), 6)
            self.assertIn("No operation/frame tail or FPS claim", text)
            for line in text.splitlines():
                if "Batch wall ms:" in line or "Caller bytes/batch:" in line:
                    self.assertEqual(len(json.loads(line.split("`")[1])), 30)


if __name__ == "__main__":
    unittest.main()
