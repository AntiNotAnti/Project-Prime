# Offline native FPS diagnostics

Run from the repository root:

```sh
python3 tools/native-fps/analyze.py capture.json
python3 tools/native-fps/analyze.py capture.json -output report.json
python3 tools/native-fps/analyze.py docs/performance/native-fps/evidence/provenance-controls/pair-2-B/frames.json.gz -o report.json
python3 -m unittest discover -s tools/native-fps -p 'test_*.py' -v
```

Accepts schema-2 and schema-3 `glframe-capture` receipts with raw `frames`, or native harness `result.frameIntervalsMs` (also a direct result object). Accepts plain UTF-8 JSON (optional BOM) or gzip-compressed `.json.gz`, including the committed raw receipts. Uses only Python's standard library. The output cannot overwrite the input, including hard-link aliases. Input is retained unchanged; the report repeats chronological intervals and carries capture context or harness observed workload and binary identity fields.

Calculates nearest-rank interval p50/p95/p99, p99.9 only at 1,000 or more samples, mean completed-frame FPS, the true one-percent low from the mean of the slowest `ceil(samples * .01)` intervals, and the legacy reciprocal-p99 proxy. Late rates use only explicit positive numeric caps; Display (0) and Unlimited (-1) have no invented monitor budget. Budget comparisons are strictly greater than 1.5x and 2x.

Malformed, nonfinite, nonpositive, empty, count-mismatched, dropped-record, and nonchronological captures fail with a nonzero exit code. Schema-2/3 captures containing mixed segments, caps, or vsync modes fail; produce separate raw captures for separate conditions. When a raw timestamp frequency is present, adjacent timestamp gaps must also agree with the reported intervals (within clock precision); this catches an unmarked reset that omits a long interval. Schema 3 additionally requires the explicit interval start, before/after presentation generations, stall/drop deltas and consistent reason; timing fences retain both the trigger and following intervals in the same segment. Bounded lifecycle events label first Present, cancellation/focus/cap/scene transitions, failed Present and capture stop. Boundary-buffer overflow is rejected. Its first stored interval can be cross-checked against its explicit start. Schema 2 has no preceding stored boundary for its first interval. No rows are silently filtered. Legacy arrays preserve supplied order, but lack timestamps with which to independently prove chronology.

The report also shows fixed/reported simulation Hz, active cap/VSync, profiling state and recorded graphics overrides; missing identity/policy fields remain UNVERIFIED.

Reports always have qualification `NOT_RUN`: offline arithmetic cannot establish device qualification. Loaded identity is carried as recorded, not independently authenticated. Baseline source is explicitly incomplete; controlled external device evidence and a complete baseline are required for performance conclusions. There is no automatic A/B winner or rollout decision. Application Present-return intervals do not measure GPU execution, physical scanout, or photon latency. This command does not run native benchmarks.
