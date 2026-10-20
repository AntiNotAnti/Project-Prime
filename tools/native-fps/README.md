# Offline native FPS diagnostics

Run from the repository root:

```sh
python3 tools/native-fps/analyze.py capture.json
python3 tools/native-fps/analyze.py capture.json -output report.json
python3 -m unittest discover -s tools/native-fps -p 'test_*.py' -v
```

Accepts schema-2 `glframe-capture` receipts with raw `frames`, or native harness `result.frameIntervalsMs` (also a direct result object). Uses only Python's standard library. Input is retained unchanged; the report repeats chronological intervals and carries capture context or harness observed workload and binary identity fields.

Calculates nearest-rank interval p50/p95/p99, p99.9 only at 1,000 or more samples, mean completed-frame FPS, the true one-percent low from the mean of the slowest `ceil(samples * .01)` intervals, and the legacy reciprocal-p99 proxy. Late rates use only explicit positive numeric caps; Display (0) and Unlimited (-1) have no invented monitor budget. Budget comparisons are strictly greater than 1.5x and 2x.

Malformed, nonfinite, nonpositive, empty, count-mismatched, dropped-record, and nonchronological captures fail with a nonzero exit code. Schema-2 captures containing mixed segments, caps, or vsync modes fail; produce separate raw captures for separate conditions. No rows are silently filtered. Legacy arrays preserve supplied order, but lack timestamps with which to independently prove chronology.

Reports always have qualification `NOT_RUN`: offline arithmetic cannot establish device qualification. Loaded identity is carried as recorded, not independently authenticated. Baseline source is explicitly incomplete; controlled external device evidence and a complete baseline are required for performance conclusions. There is no automatic A/B winner or rollout decision. Application Present-return intervals do not measure GPU execution, physical scanout, or photon latency. This command does not run native benchmarks.
