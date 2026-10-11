"""Strict field/type validation of local aggregate evidence."""
import hashlib
import json
import math
from pathlib import Path

SCOPE = "dedicated server run after BeginRun; operation scopes overlap; eligibility is Judge result, settlement is terminal Answer result; no latency measurement"
HISTOGRAM_UNIT = "microseconds; upper bounds 2^bucket; bucket31 includes overflow"

def audit(path):
    data = Path(path).read_bytes()
    report = json.loads(data)
    expected = {"stopwatchFrequency", "rows", "histograms", "histogramUnit", "processCpuMilliseconds", "gcPauseMilliseconds", "scope"}
    if type(report) is not dict or set(report) != expected:
        raise ValueError("unexpected aggregate fields")
    if type(report["stopwatchFrequency"]) is not int or report["stopwatchFrequency"] <= 0:
        raise ValueError("invalid timing frequency")
    if report["scope"] != SCOPE or report["histogramUnit"] != HISTOGRAM_UNIT:
        raise ValueError("unexpected evidence description")
    if type(report["rows"]) is not list or type(report["histograms"]) is not list:
        raise ValueError("invalid aggregate containers")
    names = ["Step", "PumpBefore", "PumpAfter", "Replication", "Eligibility", "Settlement"]
    totals = dict.fromkeys(names, 0)
    keys = set()
    for row in report["rows"]:
        if type(row) is not dict or set(row) != {"operation", "result", "count", "elapsedTicks", "maximumTicks"}:
            raise ValueError("unexpected row fields")
        name, result = row["operation"], row["result"]
        if type(name) is not str or name not in names or (result is not None and (type(result) is not int or not 0 <= result <= 255)):
            raise ValueError("invalid result bucket")
        key = name, result
        if key in keys:
            raise ValueError("duplicate result bucket")
        keys.add(key)
        count, elapsed, maximum = row["count"], row["elapsedTicks"], row["maximumTicks"]
        if any(type(n) is not int for n in (count, elapsed, maximum)) or count <= 0 or maximum < 0 or not maximum <= elapsed <= count * maximum:
            raise ValueError("invalid duration aggregate")
        totals[name] += count
    if len(report["histograms"]) != 6:
        raise ValueError("wrong histogram count")
    for name, histogram in zip(names, report["histograms"]):
        if type(histogram) is not list or len(histogram) != 32 or any(type(n) is not int or n < 0 for n in histogram) or sum(histogram) != totals[name]:
            raise ValueError("histogram/sample mismatch")
    for key in ("processCpuMilliseconds", "gcPauseMilliseconds"):
        value = report[key]
        if type(value) not in (int, float) or (type(value) is float and not math.isfinite(value)) or value < 0:
            raise ValueError("invalid process timing")
    return {"sha256": hashlib.sha256(data).hexdigest(), "operationCounts": totals,
            "scope": report["scope"], "status": "PASS"}
