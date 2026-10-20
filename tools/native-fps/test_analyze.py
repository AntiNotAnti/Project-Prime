import json
import gzip
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from analyze import analyze


def capture(values, cap=100):
    return {"schema": 2, "droppedRecords": 0, "summary": {"samples": len(values)},
            "context": {"assemblyMvid": "test-loaded-binary", "assemblySha256": "test-sha"},
            "frames": [{"IntervalMs": v, "PresentTimestamp": i + 1, "Segment": 0,
                        "Cap": cap, "Vsync": "Off"} for i, v in enumerate(values)]}


class AnalyzeTests(unittest.TestCase):
    def test_rank_and_true_low(self):
        report = analyze(capture(list(range(1, 1001))))
        self.assertEqual(report["frameIntervalP50Ms"], 500)
        self.assertEqual(report["frameIntervalP95Ms"], 950)
        self.assertEqual(report["frameIntervalP99Ms"], 990)
        self.assertEqual(report["frameIntervalP999Ms"], 999)
        self.assertAlmostEqual(report["trueOnePercentLowFps"], 1000 / 995.5)
        self.assertAlmostEqual(report["onePercentLowEstimateFps"], 1000 / 990)
        self.assertEqual(report["tailSamples"], 10)
        self.assertEqual(report["fixedSimulationHz"], 60)
        self.assertEqual(report["activeCap"], 100)
        self.assertEqual(report["activeVsync"], "Off")
        self.assertIs(report["profilingActive"], True)
        self.assertEqual(report["qualification"], "NOT_RUN")
        self.assertEqual(report["loadedBinaryIdentity"]["assemblyMvid"], "test-loaded-binary")

    def test_partial_preserves_order_and_ceil_tail(self):
        values = [12, 10, 11] + [10] * 98
        report = analyze(capture(values))
        self.assertEqual(report["chronologicalIntervalsMs"], values)
        self.assertEqual(report["tailSamples"], 2)
        self.assertAlmostEqual(report["trueOnePercentLowFps"], 1000 / 11.5)
        self.assertIsNone(report["frameIntervalP999Ms"])
        self.assertIsNone(analyze(capture([10] * 999))["frameIntervalP999Ms"])

    def test_late_boundaries_and_unknown_caps(self):
        report = analyze(capture([15, 20, 21]))
        self.assertEqual(report["lateOver1Point5Budget"], 2)
        self.assertEqual(report["lateOver2Budget"], 1)
        self.assertEqual(report["lateOver2Fraction"], 1 / 3)
        for cap in (0, -1):
            report = analyze(capture([1000], cap))
            self.assertEqual(report["numericBudgetSamples"], 0)
            self.assertIsNone(report["lateOver2Fraction"])

    def test_legacy_nested_and_direct(self):
        result = {"samples": 2, "frameIntervalsMs": [20, 10], "effectiveCap": 0,
                  "observedWorkload": {"binary": "identity"}, "assemblyMvid": "mvid"}
        for document in (result, {"result": result}):
            report = analyze(document)
            self.assertEqual(report["samples"], 2)
            self.assertEqual(report["loadedBinaryIdentity"]["observedWorkload"], {"binary": "identity"})
            self.assertEqual(report["qualification"], "NOT_RUN")

    def test_reject_bad_samples_without_filtering(self):
        for value in (0, -1, float("nan"), float("inf"), -float("inf"), None, "10", True):
            with self.subTest(value=value), self.assertRaises(ValueError):
                analyze(capture([10, value]))
            with self.subTest(legacy=value), self.assertRaises(ValueError):
                analyze({"result": {"samples": 2, "frameIntervalsMs": [10, value]}})

    def test_reject_incomplete_mixed_and_bad_structure(self):
        bad = [capture([]), {"schema": 2, "frames": "wrong"}, {}]
        for field, value in (("Segment", 1), ("Cap", 60), ("Vsync", "On"),
                             ("PresentTimestamp", 1), ("PresentTimestamp", -1)):
            document = capture([10, 20])
            document["frames"][1][field] = value
            bad.append(document)
        for value in (1, -1, 0.5, None, True):
            document = capture([10])
            document["droppedRecords"] = value
            bad.append(document)
        document = capture([10]); document["summary"]["samples"] = 2; bad.append(document)
        document = capture([10]); document["frames"][0] = None; bad.append(document)
        bad.append({"result": {"samples": 3, "frameIntervalsMs": [10]}})
        for document in bad:
            with self.subTest(document=document), self.assertRaises(ValueError):
                analyze(document)

    def test_reject_failed_or_overflowed_harness(self):
        for extra in ({"complete": False}, {"timingEvidenceComplete": False},
                      {"droppedIntervalRecords": 1}, {"droppedIntervalRecords": -1},
                      {"droppedIntervalRecords": True}):
            with self.subTest(extra=extra), self.assertRaises(ValueError):
                analyze({"samples": 2, "frameIntervalsMs": [10, 20], **extra})
        self.assertEqual(analyze({"samples": 2, "frameIntervalsMs": [10, 20],
                                 "timingEvidenceComplete": True, "droppedIntervalRecords": 0})["samples"], 2)

    def test_timestamp_gap_consistency(self):
        document = capture([10, 20])
        document["timestampFrequency"] = 1000
        document["frames"][1]["PresentTimestamp"] = 21
        self.assertEqual(analyze(document)["timestampIntervalConsistency"], "CHECKED: adjacent stored frames")
        document["frames"][1]["PresentTimestamp"] = 800
        with self.assertRaisesRegex(ValueError, "timestamp gap"):
            analyze(document)
        document["frames"][1]["Segment"] = 1
        with self.assertRaisesRegex(ValueError, "mixed segments"):
            analyze(document)
        for value in (0, -1, True, 0.5):
            document["timestampFrequency"] = value
            with self.subTest(value=value), self.assertRaises(ValueError):
                analyze(document)

    def test_cli_compressed_receipt_and_truncation(self):
        script = str(Path(__file__).with_name("analyze.py"))
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "raw.json.gz"
            with gzip.open(source, "wt", encoding="utf-8-sig") as stream:
                json.dump(capture([10, 20]), stream)
            result = subprocess.run([sys.executable, script, str(source)], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(json.loads(result.stdout)["samples"], 2)
            source.write_bytes(source.read_bytes()[:-5])
            result = subprocess.run([sys.executable, script, str(source)], capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(result.stdout, "")
            self.assertIn("frame analysis failed:", result.stderr)

    def test_cli_preserves_raw_input(self):
        script = str(Path(__file__).with_name("analyze.py"))
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "raw.json"
            original = json.dumps(capture([10, 20])).encode()
            source.write_bytes(original)
            result = subprocess.run([sys.executable, script, str(source), "-o", str(source)], capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(source.read_bytes(), original)
            alias = Path(directory) / "alias.json"
            os.link(source, alias)
            result = subprocess.run([sys.executable, script, str(source), "-o", str(alias)], capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(source.read_bytes(), original)

    def test_cli_stdout_file_and_failure(self):
        script = str(Path(__file__).with_name("analyze.py"))
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "raw.json"
            output = Path(directory) / "report.json"
            source.write_text(json.dumps(capture([10, 20])))
            result = subprocess.run([sys.executable, script, str(source)], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(json.loads(result.stdout)["samples"], 2)
            result = subprocess.run([sys.executable, script, str(source), "-output", str(output)], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(json.loads(output.read_text())["qualification"], "NOT_RUN")
            source.write_text(json.dumps(capture([])))
            result = subprocess.run([sys.executable, script, str(source)], capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(result.stdout, "")


if __name__ == "__main__":
    unittest.main()
