"""Run: python3 -m unittest discover -s tools -p test_compare_perf.py -v."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


SCRIPT = Path(os.environ.get("COMPARE_PERF_SCRIPT", Path(__file__).with_name("compare-perf.py")))
IDENTITY = ("Room", "Players", "Width", "Height", "RenderScale", "PresentationHz")
METRICS = (
    "DrawAverageMs", "DrawP95Ms", "DrawP99Ms", "DrawP999Ms",
    "SimulationP99Ms", "AllocatedBytesPerDraw",
    "DrawP99EquivalentThroughputFps", "DrawP999EquivalentThroughputFps",
)


class ComparePerformanceTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.report = {
            "Version": 2,
            "Room": "MP3 PROVING GROUND", "Players": 8, "Width": 1920, "Height": 1080,
            "RenderScale": 1.0, "PresentationHz": 120,
            "DrawAverageMs": 2.0, "DrawP95Ms": 3.0, "DrawP99Ms": 4.0,
            "DrawP999Ms": 5.0, "SimulationP99Ms": 1.0, "AllocatedBytesPerDraw": 64.0,
            "DrawP99EquivalentThroughputFps": 250.0,
            "DrawP999EquivalentThroughputFps": 200.0,
        }

    def compare(self, baseline=None, current=None, *args):
        before = self.root / "baseline.json"
        after = self.root / "current.json"
        before.write_text(json.dumps(self.report if baseline is None else baseline), encoding="utf-8")
        after.write_text(json.dumps(self.report if current is None else current), encoding="utf-8")
        return self.compare_paths(before, after, *args)

    def compare_paths(self, before, after, *args):
        return subprocess.run([sys.executable, str(SCRIPT), str(before), str(after), *args],
                              capture_output=True, text=True)

    def assert_refused(self, result):
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertIn("PERF COMPARE REFUSED", result.stdout)
        self.assertNotIn("\nPASS", result.stdout)

    def test_identical_valid_report_passes(self):
        result = self.compare()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(result.stdout.rstrip().endswith("PASS"))

    def test_each_metric_regression_fails(self):
        for key in METRICS:
            with self.subTest(key=key):
                current = dict(self.report)
                current[key] *= 0.5 if key.endswith("Fps") else 2.0
                result = self.compare(current=current)
                self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
                self.assertIn("REGRESSION", result.stdout)

    def test_no_fail_retains_valid_regression_warning(self):
        current = dict(self.report, DrawP99Ms=8.0)
        result = self.compare(self.report, current, "--no-fail")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(result.stdout.rstrip().endswith("WARN"))

    def test_missing_metrics_on_either_side_are_refused(self):
        for key in METRICS:
            for side in ("baseline", "current"):
                with self.subTest(key=key, side=side):
                    invalid = dict(self.report)
                    del invalid[key]
                    self.assert_refused(self.compare(**{side: invalid}))

    def test_matching_missing_metrics_are_refused_even_with_no_fail(self):
        for key in METRICS:
            with self.subTest(key=key):
                invalid = dict(self.report)
                del invalid[key]
                self.assert_refused(self.compare(invalid, invalid, "--no-fail"))

    def test_missing_workload_fields_are_refused(self):
        for key in IDENTITY:
            with self.subTest(key=key):
                invalid = dict(self.report)
                del invalid[key]
                self.assert_refused(self.compare(invalid, invalid))
                self.assert_refused(self.compare(current=invalid))

    def test_malformed_metrics_are_refused_on_both_sides(self):
        for key in METRICS:
            for value in (None, False, "2.0", -1.0, float("nan"), float("inf"), -float("inf")):
                for side in ("baseline", "current"):
                    with self.subTest(key=key, value=value, side=side):
                        invalid = dict(self.report)
                        invalid[key] = value
                        self.assert_refused(self.compare(**{side: invalid}))

    def test_malformed_workload_fields_are_refused(self):
        bad_values = {
            "Room": (None, "", "   ", 3),
            "Players": (None, True, 0, -1, 8.5, "8"),
            "Width": (None, True, 0, -1, 1920.5, "1920"),
            "Height": (None, True, 0, -1, 1080.5, "1080"),
            "RenderScale": (None, True, 0, -1, "1", float("nan"), float("inf")),
            "PresentationHz": (None, True, 0, -1, 120.5, "120"),
        }
        for key, values in bad_values.items():
            for value in values:
                with self.subTest(key=key, value=value):
                    invalid = dict(self.report)
                    invalid[key] = value
                    self.assert_refused(self.compare(invalid, invalid, "--no-fail"))

    def test_workload_mismatch_is_refused(self):
        for key in IDENTITY:
            with self.subTest(key=key):
                current = dict(self.report)
                current[key] = "OTHER ROOM" if key == "Room" else current[key] + 1
                self.assert_refused(self.compare(self.report, current, "--no-fail"))

    def test_zero_metrics_are_valid_and_equal(self):
        zero = dict(self.report)
        zero.update({key: 0 for key in METRICS})
        result = self.compare(zero, zero)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(result.stdout.rstrip().endswith("PASS"))

    def test_new_allocations_from_zero_baseline_are_a_regression(self):
        before = dict(self.report, AllocatedBytesPerDraw=0)
        after = dict(self.report, AllocatedBytesPerDraw=1)
        result = self.compare(before, after)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("AllocatedBytesPerDraw", result.stdout)
        self.assertIn("REGRESSION", result.stdout)

    def test_equivalent_throughput_improvement_from_zero_passes(self):
        before = dict(self.report, DrawP99EquivalentThroughputFps=0,
                      DrawP999EquivalentThroughputFps=0)
        self.assertEqual(self.compare(before, self.report).returncode, 0)

    def test_optional_gc_counts_remain_optional(self):
        current = dict(self.report, Gen0Collections=2, Gen1Collections=0, Gen2Collections=0)
        result = self.compare(self.report, current)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("GC collections", result.stdout)

    def test_present_invalid_gc_counts_are_refused(self):
        for key in ("Gen0Collections", "Gen1Collections", "Gen2Collections"):
            for value in (None, True, -1, 1.5, "1", float("nan")):
                with self.subTest(key=key, value=value):
                    invalid = dict(self.report)
                    invalid[key] = value
                    self.assert_refused(self.compare(current=invalid))

    def test_build_metadata_can_differ(self):
        before = dict(self.report, Version=2, Build="old", Seconds=20, DrawSamples=2400)
        after = dict(self.report, Version=2, Build="new", Seconds=20, DrawSamples=2401)
        self.assertEqual(self.compare(before, after).returncode, 0)

    def test_non_object_and_malformed_json_are_refused(self):
        before = self.root / "baseline.json"
        after = self.root / "current.json"
        before.write_text(json.dumps(self.report), encoding="utf-8")
        for text in ("[]", "null", "{", "NaN"):
            with self.subTest(text=text):
                after.write_text(text, encoding="utf-8")
                self.assert_refused(self.compare_paths(before, after, "--no-fail"))

    def test_exponent_overflow_metric_is_refused(self):
        before = self.root / "baseline.json"
        after = self.root / "current.json"
        before.write_text(json.dumps(self.report), encoding="utf-8")
        after.write_text(json.dumps(self.report).replace('"DrawAverageMs": 2.0',
                                                         '"DrawAverageMs": 1e999'), encoding="utf-8")
        self.assert_refused(self.compare_paths(before, after))

    def test_missing_file_is_refused_without_traceback(self):
        before = self.root / "baseline.json"
        before.write_text(json.dumps(self.report), encoding="utf-8")
        result = self.compare_paths(before, self.root / "missing.json")
        self.assert_refused(result)
        self.assertNotIn("Traceback", result.stderr)


if __name__ == "__main__":
    unittest.main()
