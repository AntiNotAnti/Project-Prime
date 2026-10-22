import json
from pathlib import Path
import tempfile
import unittest
from net13_performance import audit, SCOPE, HISTOGRAM_UNIT

class PerformanceEvidenceTests(unittest.TestCase):
    def test_aggregate_validation_and_hash(self):
        report = dict(stopwatchFrequency=1000, rows=[dict(operation="Step", result=None, count=2, elapsedTicks=4, maximumTicks=3)],
                      histograms=[[2]+[0]*31]+[[0]*32 for _ in range(5)], histogramUnit=HISTOGRAM_UNIT, processCpuMilliseconds=1, gcPauseMilliseconds=0, scope=SCOPE)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/"perf.json"
            path.write_text(json.dumps(report))
            first = audit(path)
            self.assertEqual(first["operationCounts"]["Step"], 2)
            self.assertEqual(first["sha256"], audit(path)["sha256"])
            report["histograms"][0][0] = 1
            path.write_text(json.dumps(report))
            with self.assertRaises(ValueError): audit(path)
            report["histograms"][0][0] = 2
            report["playerName"] = "forbidden"
            path.write_text(json.dumps(report))
            with self.assertRaises(ValueError): audit(path)

    def test_adversarial_numeric_types_and_bounds(self):
        import copy
        good = dict(stopwatchFrequency=1000, rows=[dict(operation="Step", result=None, count=2, elapsedTicks=4, maximumTicks=3)],
                    histograms=[[2]+[0]*31]+[[0]*32 for _ in range(5)], histogramUnit=HISTOGRAM_UNIT,
                    processCpuMilliseconds=1, gcPauseMilliseconds=0, scope=SCOPE)
        mutations = [
            lambda r: r.update(stopwatchFrequency=True), lambda r: r.update(stopwatchFrequency=1.5),
            lambda r: r.update(stopwatchFrequency=0), lambda r: r.update(rows={}),
            lambda r: r.update(histograms={}), lambda r: r.update(scope="arbitrary private data"),
            lambda r: r.update(histogramUnit="arbitrary private data"),
            lambda r: r["rows"].__setitem__(0, []),
            lambda r: r["rows"][0].update(operation=[]),
        ]
        for field in ("count", "elapsedTicks", "maximumTicks", "result"):
            for value in (True, 1.5, "1", [], {}):
                mutations.append(lambda r, f=field, v=value: r["rows"][0].update({f:v}))
        for field, value in (("count",0), ("count",-1), ("elapsedTicks",-1), ("elapsedTicks",2),
                             ("elapsedTicks",7), ("maximumTicks",-1), ("maximumTicks",0), ("result",-1), ("result",256)):
            mutations.append(lambda r, f=field, v=value: r["rows"][0].update({f:v}))
        for field in ("processCpuMilliseconds", "gcPauseMilliseconds"):
            for value in (True, "1", [], -1, float("nan"), float("inf")):
                mutations.append(lambda r, f=field, v=value: r.update({f:v}))
        for value in (True, 2.0, "2", -1):
            mutations.append(lambda r, v=value: r["histograms"][0].__setitem__(0,v))
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/"perf.json"
            for index, mutation in enumerate(mutations):
                with self.subTest(index=index):
                    bad=copy.deepcopy(good); mutation(bad); path.write_text(json.dumps(bad))
                    with self.assertRaises(ValueError): audit(path)
