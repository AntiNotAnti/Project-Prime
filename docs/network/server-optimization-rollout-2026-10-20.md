# SRV-01..07: Dedicated server optimization and Linux acceptance

Implementation started 2026-10-20. These slices do **not** change the fixed
60 Hz simulation, authority ownership, shot settlement, client-reported
movement, native bot controls, or packet/replay layout.

## Slices and attribution

- **SRV-01** makes the Linux capacity harness launch 2+ real independent
  headless UDP clients for every isolated server. It refuses to use an empty
  continuous server as evidence of active match capacity.
- **SRV-02** replaces repeated 1 ms active-loop sleeps with a UDP-activity
  event wait (max 8 ms), preserving deadline margin/yield/spin behavior.
  CPU reduction is a **hypothesis** until real A/B measurements.
- **SRV-03** batches social membership at 10 Hz, waitlist/party maintenance
  at 20 Hz, and reuses snapshot iteration buffers. Admission and simulation
  remain unchanged.
- **SRV-04** gates *new* hosted-match admissions with sampled parent/child
  CPU headroom and operator-configured caps; never evicts running games.
- **SRV-05** removes a redundant completed-prewarm Stopwatch allocation
  and logs prewarm wait/world creation/total join work. Further cold-join
  speedups require measured bottleneck attribution before changing
  Scene/NetSession lifetime and owner thread.
- **SRV-06** asynchronously requests Linux/macOS SIGTERM for hosted children,
  then falls back to kill after a bounded grace window. Master shutdown
  joins owned termination tasks; routine reaping never blocks UDP.
- **SRV-07** adds binary hashes, per-step p95/p99/overrun/drop/failure
  evidence, a pure acceptance gate, and a matched-baseline comparator.

## Reproducible, asset-backed Linux matrix

The test host needs a dedicated server and netcheck-capable client publish,
with locally configured `paths.txt`. Do not copy game assets into the repo.

```sh
python3 tools/test-server-capacity-benchmark.py
python3 tools/test-server-capacity-qualify.py

python3 tools/server-capacity-benchmark.py \
  --binary /opt/prime/server/ProjectPrime \
  --workdir /opt/prime/server \
  --client-binary /opt/prime/client/ProjectPrime \
  --client-workdir /opt/prime/client \
  --mode active --clients-per-match 2 \
  --instances 1,2,4,8 --seconds 600 \
  --require-healthy --max-p99-ms 16.67 \
  --output artifacts/srv07/active-baseline
```

Run the same published build and fixed workload at least twice for a control
repeat, then candidate/control in a predeclared ABBA order with unambiguous
fresh output directories. The baseline/candidate must use the **same Linux
machine**, configuration, number of instances, runtime and match/map. The
harness includes the client binaries' CPU pressure, so the result describes
*co-located* client/server competition and is **not** a substitute for
remote load-generating machines or physical WAN qualification.

```sh
python3 tools/server-capacity-qualify.py \
  --baseline artifacts/srv07/active-baseline/capacity-summary.json \
  --candidate artifacts/srv07/active-candidate/capacity-summary.json \
  --max-p99-ms 16.67 --max-overruns 0 \
  --output artifacts/srv07/qualification.json
```

The acceptance gate requires a real, admitted 60 Hz authority, nonzero
simulation step evidence, p99 within the stated ceiling, no dropped
simulation deadlines, no simulated-step failure, and bounded overruns.
It reports CPU/RSS/p99 deltas, but one A/B pair does **not** establish a
statistically supported improvement. Raw server/client logs, machine
characteristics, binary SHA-256 values and failed runs must be retained.

The original capacity script reported periodic server steps (not a full
wall-clock trace of every tick). `ServerSim.Describe()` currently reports
cumulative p95/p99 and resource counters at a periodic log boundary.
This is a coarse last-observed check. Physical deployment qualification
requires complete-duration telemetry, live-UDP fault/load campaigns, replay
finalization, reconnect/timeout testing and at least one representative
Linux host, not just a passing localhost benchmark.

## Required build/regression gates

Run exact-head Release server build and linked network/AI tests, including:
`--architecture`, `--netcode-performance`, `--transport-stress`,
`--server-performance`, queue/party/social membership checks, native
bot lifecycle, join/rejoin, match end/rotation, replay stop, and clean
SIGTERM vs forced-child-exit tests. Enable performance evidence via
`PRIME_SERVER_PERF_OUTPUT` and operator aggregates via
`PRIME_SERVER_OPERATOR_OUTPUT` only in isolated diagnostic runs.
Never consider a queued/unexecuted check a pass.

## Rollback

Revert individual SRV pull requests in reverse order. SRV-06 depends on
SRV-04's process metrics; SRV-07 depends on SRV-01's corrected benchmark.
SRV-04's CPU budget can be tuned with
`PROJECT_PRIME_HOST_MAX_CPU_CORES`; optional RAM/match ceilings are
`PROJECT_PRIME_HOST_MAX_TOTAL_RSS_MB` and
`PROJECT_PRIME_HOST_MAX_MATCHES`. Disable experiments with their
respective PR rollback rather than relaxing correctness invariants.

**Acceptance status:** source-only implementation. No Linux hardware
capacity, real WAN, or full .NET CI claims are made until those gates
finish successfully.
