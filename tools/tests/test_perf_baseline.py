"""Focused tests for the report-only performance comparison layer."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "perf" / "compare-baseline.py"
SPEC = importlib.util.spec_from_file_location("compare_baseline", SCRIPT)
PERF = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(PERF)


class PerformanceBaselineTests(unittest.TestCase):
    def test_existing_nettest_and_worker_reports_are_projected_without_synthetic_values(self):
        nettest = PERF.extract_metrics("nettest", {
            "Kind": "ProjectPrime.ContentFreePerformanceBaseline",
            "Scenarios": [{
                "Name": "SnapshotPacket.Write",
                "NanosecondsPerOperation": 12.5,
                "AllocatedBytesPerOperation": 0,
                "P95Nanoseconds": 14.0,
            }],
        })
        worker = PERF.extract_metrics("worker-soak", {
            "shapes": [{"observed": {
                "peakCpuPercent": 31.5,
                "worstP95Milliseconds": 4.0,
            }}],
        })

        self.assertEqual(12.5, nettest["nettest.SnapshotPacket.Write.NanosecondsPerOperation"])
        self.assertEqual(0, nettest["nettest.SnapshotPacket.Write.AllocatedBytesPerOperation"])
        self.assertEqual(31.5, worker["worker-soak.shape[0].observed.peakCpuPercent"])
        self.assertNotIn("nettest.synthetic", nettest)

    def test_compare_classifies_ten_and_twenty_percent_policy(self):
        baseline = {
            "schemaVersion": 1,
            "environment": {"os": "Linux", "architecture": "x64", "dotnet": "10.0"},
            "metrics": {
                "slow": {"value": 100, "direction": "lower-is-better"},
                "candidate": {"value": 100, "direction": "lower-is-better"},
                "throughput": {"value": 100, "direction": "higher-is-better"},
            },
        }
        current = {
            "schemaVersion": 1,
            "environment": {"os": "Linux", "architecture": "x64", "dotnet": "10.0"},
            "metrics": {
                "slow": {"value": 112, "direction": "lower-is-better"},
                "candidate": {"value": 125, "direction": "lower-is-better"},
                "throughput": {"value": 88, "direction": "higher-is-better"},
            },
        }

        result = PERF.compare(baseline, current)
        statuses = {row["name"]: row["status"] for row in result["metrics"]}

        self.assertEqual("advisory", statuses["slow"])
        self.assertEqual("failure-candidate", statuses["candidate"])
        self.assertEqual("advisory", statuses["throughput"])
        self.assertEqual("failure-candidate", result["status"])
        self.assertTrue(result["environmentMatch"])

    def test_zero_baseline_and_missing_metric_are_explicit(self):
        baseline = {"schemaVersion": 1, "metrics": {"alloc": {"value": 0, "direction": "lower-is-better"},
                                                        "removed": {"value": 1, "direction": "lower-is-better"}}}
        current = {"schemaVersion": 1, "metrics": {"alloc": {"value": 1, "direction": "lower-is-better"}}}

        result = PERF.compare(baseline, current)

        self.assertEqual("failure-candidate", result["metrics"][0]["status"])
        self.assertEqual(["removed"], result["missingMetrics"])
        self.assertEqual("incomplete", result["status"])

    def test_collect_records_environment_and_source_metadata(self):
        with tempfile.TemporaryDirectory(prefix="prime-perf-baseline-") as temporary:
            root = Path(temporary)
            source = root / "nettest.json"
            output = root / "current.json"
            source.write_text(json.dumps({
                "kind": "ProjectPrime.ContentFreePerformanceBaseline",
                "schemaVersion": 1,
                "ContentMode": "content-free",
                "Runtime": {"Framework": ".NET 10.0.11"},
                "Scenarios": [
                    {"Name": "Queue", "Status": "ok", "NanosecondsPerOperation": 3.0},
                    {"Name": "ContentOnly", "Status": "gap", "NanosecondsPerOperation": 0.0},
                ],
            }))

            status = PERF.main([
                "collect", "--root", str(root), "--source", f"nettest={source}",
                "--output", str(output),
            ])

            self.assertEqual(0, status)
            report = json.loads(output.read_text())
            self.assertEqual("ProjectPrime.PerformanceBaseline", report["kind"])
            self.assertIn("os", report["environment"])
            self.assertEqual("nettest.json", report["sources"]["nettest"]["path"])
            self.assertEqual("content-free", report["sources"]["nettest"]["contentMode"])
            self.assertEqual({"Framework": ".NET 10.0.11"}, report["sources"]["nettest"]["runtime"])
            self.assertEqual(3.0, report["metrics"]["nettest.Queue.NanosecondsPerOperation"]["value"])
            self.assertNotIn("nettest.ContentOnly.NanosecondsPerOperation", report["metrics"])

    def test_report_only_does_not_fail_candidates_but_opt_in_does(self):
        with tempfile.TemporaryDirectory(prefix="prime-perf-compare-") as temporary:
            root = Path(temporary)
            baseline = root / "baseline.json"
            current = root / "current.json"
            baseline.write_text(json.dumps({"schemaVersion": 1, "metrics": {"tick": 100}}))
            current.write_text(json.dumps({"schemaVersion": 1, "metrics": {"tick": 125}}))

            report_only = PERF.main(["compare", "--baseline", str(baseline), "--current", str(current)])
            enforced = PERF.main([
                "compare", "--baseline", str(baseline), "--current", str(current),
                "--fail-on-candidate",
            ])

            self.assertEqual(0, report_only)
            self.assertEqual(1, enforced)


if __name__ == "__main__":
    unittest.main()
