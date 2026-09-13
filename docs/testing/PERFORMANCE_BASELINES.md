# Performance baseline observations

`tools/perf/compare-baseline.py` packages measurements already produced by the
Project Prime harnesses. It does not create a synthetic workload. Supported
inputs are:

- `tools/nettest/nettest.dll --performance-baseline output.json`
- `tools/worker-soak/analyze-capacity.py --output output.json`
- client/render baseline or telemetry JSON containing measured frame/device,
  geometry, or explicit `metrics` sections

Package one or more observations and retain the environment metadata beside
the values:

```bash
python3 tools/perf/compare-baseline.py collect \
  --source nettest=artifacts/nettest/performance.json \
  --source worker-soak=artifacts/worker-soak/capacity.json \
  --output artifacts/perf/current.json
```

Compare two packaged observations:

```bash
python3 tools/perf/compare-baseline.py compare \
  --baseline tools/perf/baseline.json \
  --current artifacts/perf/current.json \
  --output artifacts/perf/comparison.json
```

The policy is advisory: a metric more than 10% worse is reported as an
`advisory`, and one more than 20% worse is a `failure-candidate`. The default
command remains report-only; `--fail-on-candidate` is available only after a
baseline is stabilized on a comparable OS, architecture, runtime, workload,
and content environment. Environment metadata is retained in every report so
cross-machine comparisons are not mistaken for controlled regressions.

The committed `tools/perf/baseline.json` contains a real content-free nettest
observation with source/runtime metadata. The harness emits three intentional
content-backed `gap` scenarios in content-free mode; the packager excludes those
unmeasured zero defaults. Three same-environment runs completed all 12 measured
scenarios, but one queue timing outlier exceeded 10% spread, so the baseline
remains report-only and is not a release claim.

The scheduled workflow accepts the harness's non-zero content-free exit only
when those same three named content-backed gaps are present; missing output or
any other failed scenario still fails observation collection. The comparison
itself remains report-first (`--fail-on-candidate` is not used in CI).

Performance results are evidence, not a release claim. GPU timing, real-device
Android behavior, and content-backed server/client soak results still require
the corresponding native device or content fixture.
