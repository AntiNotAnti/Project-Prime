import unittest
from analyze_resources import analyze, trend


class ResourceAnalysisTests(unittest.TestCase):
    def test_linear_cycle_growth_and_warmup(self):
        rows = [{"cycle": i, "candidate": True, "memory": {"workingSetBytes": 1000 + 20*i}} for i in range(8)]
        rows[0]["memory"]["workingSetBytes"] = 9000
        result = analyze({"complete": True, "records": rows}, warmup_cycles=1, block_cycles=3)
        metric = result["series"]["postCandidateUnload"]["postWarmup"]["workingSetBytes"]
        self.assertEqual(metric["slope"], 20)
        self.assertEqual(metric["rSquared"], 1)
        self.assertEqual(metric["samples"], 7)
        self.assertEqual(len(result["series"]["postCandidateUnload"]["blocks"]), 3)

    def test_reference_candidate_studio_never_pooled(self):
        data = {"records": [{"cycle": i, "candidate": candidate, "memory": {"workingSetBytes": base+i*slope}}
                            for i in range(6) for candidate, base, slope in [(False, 100, 10), (True, 200, 20)]],
                "studioReloads": [{"cycle": i, "memory": {"workingSetBytes": 300+i*30}} for i in range(6)]}
        series = analyze(data, 0)["series"]
        self.assertEqual([series[k]["postWarmup"]["workingSetBytes"]["slope"] for k in series], [10, 20, 30])

    def test_quiescence_uses_time_and_ignores_drain_phases(self):
        data = {"drainSamples": [{"phase": "before-drain", "memory": {"workingSetBytes": 99999}},
                                *[{"phase": "quiescent", "elapsedSeconds": t, "memory": {"workingSetBytes": 100-t*2}} for t in [1, 3, 6]]]}
        result = analyze(data)["quiescent"]
        self.assertEqual(result["sampleCount"], 3)
        self.assertAlmostEqual(result["metrics"]["workingSetBytes"]["slope"], -2)

    def test_unavailable_and_nonfinite_are_not_zero(self):
        rows = [{"cycle": i, "memory": {"privateBytes": v}} for i, v in enumerate([None, float("nan"), float("inf"), False])]
        self.assertEqual(trend(rows, "cycle", "privateBytes"), {"samples": 0, "available": False, "slope": None})
        self.assertFalse(analyze({"complete": False})["complete"])

    def test_short_or_constant_x_cannot_establish_slope(self):
        metric = trend([{"cycle": 0, "memory": {"workingSetBytes": 10}}], "cycle", "workingSetBytes")
        self.assertIsNone(metric["slope"])
        self.assertEqual(analyze({"records": [{"cycle": 0, "candidate": True}]})["series"]["postCandidateUnload"]["postWarmupSamples"], 0)

    def test_invalid_analysis_options(self):
        for warmup, block in [(-1, 10), (0, 0)]:
            with self.assertRaises(ValueError):
                analyze({}, warmup, block)


if __name__ == "__main__":
    unittest.main()
