#!/usr/bin/env python3
"""Pure parser regression for active Linux capacity evidence (no game assets)."""
import importlib.util
from pathlib import Path


def main():
    source = Path(__file__).with_name("server-capacity-benchmark.py")
    spec = importlib.util.spec_from_file_location("server_capacity_benchmark", source)
    # importlib does not infer a loader for a hyphenated Python filename.
    from importlib.machinery import SourceFileLoader
    spec = importlib.util.spec_from_loader("server_capacity_benchmark",
                                          SourceFileLoader("server_capacity_benchmark", str(source)))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    line = ("[server] sim: MP1 SANCTORUS (Battle), 1200 step(s), "
            "0.52 ms mean, 1.04 ms p95, 3.22 ms p99, "
            "18.7 ms worst, 3072000 allocated bytes, "
            "2 overrun, 0 dropped, 0 stall(s)")
    match = module.STEP_RE.search(line)
    assert match is not None, "actual ServerSim.Describe() output must be recognized"
    assert match.groups() == ("1200", "0.52", "1.04", "3.22", "18.7",
                              "3072000", "2", "0", "0", None)
    failed = module.STEP_RE.search(line + ", 1 FAILED")
    assert failed is not None and failed.group(10) == "1"
    assert module.STEP_RE.search("100 step(s), 0.0 ms mean, 0.0 ms worst") is None
    print("PASS: capacity parser consumes actual simulation p95/p99/GC evidence")


if __name__ == "__main__":
    main()
