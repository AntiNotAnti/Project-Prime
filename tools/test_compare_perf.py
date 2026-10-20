#!/usr/bin/env python3
"""CLI acceptance for legacy cross-build and strict paired CPU draw-work reports."""
import copy
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest

SCRIPT = pathlib.Path(__file__).with_name("compare-perf.py")
METRICS = ("DrawAverageMs", "DrawP95Ms", "DrawP99Ms", "DrawP999Ms", "SimulationP99Ms",
           "AllocatedBytesPerDraw", "OnePercentLowFps", "PointOnePercentLowFps")


def report():
    return {
        "MeasurementKind": "batched-draw-cpu", "ApplicationImageSha256": "a" * 64,
        "ApplicationImageKind": "assembly", "Build": "test",
        "Hardware": {"MachineSha256": "b" * 64, "OperatingSystem": "fixture-os",
                     "ProcessArchitecture": "Arm64", "LogicalProcessors": 8,
                     "GlVendor": "fixture", "GlRenderer": "fixture-gpu", "GlVersion": "4.5"},
        "Workload": {"Schema": 1, "Route": "map-audit-v1", "Bots": True,
                     "MainHunter": "Samus", "GameRules": {"Mode": "Battle"}, "Settings": {"TextureReplacements": False,
                     "CharacterModelReplacements": False, "FieldOfView": 78},
                     "SourceIdentity": {"Scope": "room-files-v1", "GameKey": "fixture",
                                        "Files": {"Model": "c" * 64, "Collision": "d" * 64}}},
        "Room": "FIXTURE", "Players": 8, "Width": 1920, "Height": 1080,
        "RenderScale": 100, "PresentationHz": 240, "Seconds": 20, "DrawSamples": 4800,
        "DrawAverageMs": 2.0, "DrawP95Ms": 3.0, "DrawP99Ms": 4.0, "DrawP999Ms": 5.0,
        "SimulationP99Ms": 1.0, "AllocatedBytesPerDraw": 0,
        "OnePercentLowFps": 250.0, "PointOnePercentLowFps": 200.0,
        "Gen0Collections": 0, "Gen1Collections": 0, "Gen2Collections": 0,
    }


def tolerance(base):
    return {"Schema": 1, "MeasurementKind": "batched-draw-cpu",
            "ApplicationImageSha256": base["ApplicationImageSha256"],
            "Hardware": copy.deepcopy(base["Hardware"]), "Workload": copy.deepcopy(base["Workload"]),
            "ReportIdentity": {key: base[key] for key in ("Room", "Players", "Width", "Height",
                               "RenderScale", "PresentationHz", "Seconds")},
            "Evidence": "fixture: repeated same-machine baseline captures 1-5",
            "MaxRegressionFraction": {metric: 0.05 for metric in METRICS}}


class ComparePerfTests(unittest.TestCase):
    def run_compare(self, base, current, limits="default", *, paired=True, extra=()):
        with tempfile.TemporaryDirectory(prefix="prime-perf-compare-") as directory:
            root = pathlib.Path(directory)
            for name, value in (("base.json", base), ("current.json", current)):
                (root / name).write_text(json.dumps(value), encoding="utf-8")
            command = [sys.executable, str(SCRIPT), str(root / "base.json"), str(root / "current.json")]
            if paired:
                command.append("--paired-ab")
            if limits == "default":
                limits = tolerance(base) if paired else None
            if limits is not None:
                (root / "limits.json").write_text(json.dumps(limits), encoding="utf-8")
                command.extend(("--tolerances", str(root / "limits.json")))
            result = subprocess.run([*command, *extra], text=True, capture_output=True, check=False)
            return result.returncode, result.stdout + result.stderr

    def test_measured_pair_passes_within_tolerance(self):
        base = report()
        current = copy.deepcopy(base)
        current["DrawP99Ms"] *= 1.04
        code, output = self.run_compare(base, current)
        self.assertEqual(code, 0, output)
        self.assertIn("no frame pacing or input-latency claim", output)

    def test_missing_tolerance_refused_even_with_no_fail(self):
        code, output = self.run_compare(report(), report(), None, extra=("--no-fail",))
        self.assertEqual(code, 2, output)
        self.assertIn("measured baseline variance", output)

    def test_binary_hardware_workload_and_cap_mismatches_refused(self):
        for key in ("ApplicationImageSha256", "Hardware", "Workload", "PresentationHz", "Seconds"):
            with self.subTest(key=key):
                base = report()
                current = copy.deepcopy(base)
                if key == "Hardware":
                    current[key]["GlRenderer"] = "different-gpu"
                elif key == "Workload":
                    current[key]["SourceIdentity"]["Files"]["Model"] = "e" * 64
                elif key == "ApplicationImageSha256":
                    current[key] = "f" * 64
                else:
                    current[key] += 1
                code, output = self.run_compare(base, current)
                self.assertEqual(code, 2, output)
                self.assertIn("identity differs", output)

    def test_missing_identity_refused(self):
        for key in ("MeasurementKind", "ApplicationImageSha256", "Hardware", "Workload"):
            with self.subTest(key=key):
                current = report()
                del current[key]
                self.assertEqual(self.run_compare(report(), current)[0], 2)

    def test_missing_and_invalid_metrics_refused(self):
        for metric in METRICS:
            with self.subTest(metric=metric):
                current = report()
                del current[metric]
                self.assertEqual(self.run_compare(report(), current)[0], 2)
        for value in (None, True, "4.0", -1, float("nan"), float("inf")):
            with self.subTest(value=value):
                current = report()
                current["DrawP99Ms"] = value
                self.assertEqual(self.run_compare(report(), current)[0], 2)

    def test_zero_positive_metric_or_unordered_percentiles_refused(self):
        current = report()
        current["DrawP99Ms"] = 0
        self.assertEqual(self.run_compare(report(), current)[0], 2)
        current["DrawP99Ms"] = current["DrawP999Ms"] + 1
        self.assertEqual(self.run_compare(report(), current)[0], 2)

    def test_incomplete_hardware_source_and_game_rule_identity_refused(self):
        for mutate in (
            lambda value: value["Hardware"].update(GlRenderer=""),
            lambda value: value["Hardware"].update(MachineSha256="host-name"),
            lambda value: value["Workload"]["SourceIdentity"]["Files"].pop("Model"),
            lambda value: value["Workload"]["SourceIdentity"]["Files"].update(Model="missing"),
            lambda value: value["Workload"].pop("GameRules"),
            lambda value: value["Workload"].update(Route="unknown"),
        ):
            current = report()
            mutate(current)
            self.assertEqual(self.run_compare(report(), current)[0], 2)

    def test_invalid_workload_counts_refused(self):
        for key in ("Players", "Width", "Height", "RenderScale", "PresentationHz", "DrawSamples"):
            current = report()
            current[key] += .5
            self.assertEqual(self.run_compare(report(), current)[0], 2)

    def test_cpu_regression_fails(self):
        current = report()
        current["DrawP99Ms"] *= 1.10
        code, output = self.run_compare(report(), current)
        self.assertEqual(code, 1, output)
        self.assertIn("REGRESSION", output)

    def test_low_fps_regression_fails_and_no_fail_only_suppresses_regression(self):
        current = report()
        current["OnePercentLowFps"] *= .90
        self.assertEqual(self.run_compare(report(), current)[0], 1)
        self.assertEqual(self.run_compare(report(), current, extra=("--no-fail",))[0], 0)
        del current["DrawAverageMs"]
        self.assertEqual(self.run_compare(report(), current, extra=("--no-fail",))[0], 2)

    def test_zero_allocation_growth_fails(self):
        current = report()
        current["AllocatedBytesPerDraw"] = 1
        self.assertEqual(self.run_compare(report(), current)[0], 1)
        self.assertEqual(self.run_compare(report(), report())[0], 0)

    def test_missing_invalid_and_unrelated_tolerance_refused(self):
        for mutate in (
            lambda value: value.pop("Evidence"),
            lambda value: value["MaxRegressionFraction"].pop("DrawP99Ms"),
            lambda value: value["MaxRegressionFraction"].update(DrawP99Ms=float("nan")),
            lambda value: value["MaxRegressionFraction"].update(DrawP99Ms=-.1),
            lambda value: value["MaxRegressionFraction"].update(DrawP99Ms=1),
            lambda value: value.update(ApplicationImageSha256="e" * 64),
            lambda value: value["Hardware"].update(GlRenderer="other"),
            lambda value: value["ReportIdentity"].update(PresentationHz=144),
        ):
            limits = tolerance(report())
            mutate(limits)
            self.assertEqual(self.run_compare(report(), report(), limits)[0], 2)

    def test_unverified_replacement_content_refused(self):
        for key in ("TextureReplacements", "CharacterModelReplacements"):
            base = report()
            base["Workload"]["Settings"][key] = True
            self.assertEqual(self.run_compare(base, base)[0], 2)

    def test_legacy_cross_build_behavior_retained(self):
        base = report()
        current = copy.deepcopy(base)
        # Old reports have neither identities nor the paired schema.
        for value in (base, current):
            for key in ("MeasurementKind", "Hardware", "Workload", "ApplicationImageKind"):
                del value[key]
        current["ApplicationImageSha256"] = "f" * 64
        current["DrawP99Ms"] *= 1.10  # Within historical 15% threshold.
        self.assertEqual(self.run_compare(base, current, paired=False)[0], 0)
        current["DrawP99Ms"] = base["DrawP99Ms"] * 1.16
        self.assertEqual(self.run_compare(base, current, paired=False)[0], 1)
        current["PresentationHz"] = 144
        self.assertEqual(self.run_compare(base, current, paired=False)[0], 2)

    def test_tolerance_cannot_silently_change_cross_build_mode(self):
        self.assertEqual(self.run_compare(report(), report(), tolerance(report()), paired=False)[0], 2)

    def test_non_object_and_duplicate_json_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            first, second = root / "a.json", root / "b.json"
            second.write_text(json.dumps(report()), encoding="utf-8")
            for content in ('[]', '{"Room":"A","Room":"B"}'):
                first.write_text(content, encoding="utf-8")
                result = subprocess.run([sys.executable, str(SCRIPT), str(first), str(second)],
                                        capture_output=True, text=True, check=False)
                self.assertEqual(result.returncode, 2, result.stderr)


if __name__ == "__main__":
    unittest.main()
