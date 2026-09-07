# G3.7 interpolation experiment: retain fixed six ticks

The server-RTT adaptive candidate is **rejected**. Fixed six-tick interpolation remains the runtime default; no wire format, runtime policy, or lag-compensation behavior changes are part of this experiment. Across 35 deterministic comparisons, 25 met all predeclared gates. Downlink-heavy paths exposed the candidate's symmetry assumption: extrapolated frames increased from 0.17% to 49.54% despite identical total RTT to the successful uplink-heavy case.

## Reproduction

Build `tools/nettest/nettest.csproj` with .NET 10, then run its output:

```sh
dotnet nettest.dll --interpolation-ab /tmp/interpolation-ab.json
```

The command returns success when the experiment executes; each JSON row independently reports candidate quality gates. Two executions produced byte-identical 35-row JSON (SHA-256 `428dac86f6354a042259b2e3651982f753b3526ed492089c8c5fb8e27ed4d33d`). The repository harness results also match the original standalone experiment semantically.

## Method and candidate

Each case runs 120 simulated seconds at 60 presentation ticks/second and 30 snapshots/second; measurements exclude the first five seconds. Seeds are 17, 43, 91, 173, and 811. The harness uses the actual `SnapshotInterpolation` history, sample, extrapolation, and view-tick capture implementation. Position follows two smooth sine components, with analytically derived velocity in engine units. This supplies reproducible correction and error comparisons; it does not represent every combat motion.

The candidate starts at six ticks. Server-observed echo RTT uses an EWMA with weight 1/8 and the last 32 RTT samples. Every 15 ticks it computes `clamp(ceil(RTT / 2 + jitter95 + 2), 3, 6)` in tick units; jitter95 is the 95th percentile of absolute RTT deviations from the EWMA. Increases apply at the next evaluation. Reductions require five seconds of a stable desired value and proceed one tick per five seconds.

The same interpolation history survives every profile change. Selecting a different delay is represented algebraically by shifting the time argument of the existing fixed-six history. When increasing delay would rewind presented time, the harness holds the previous presented tick. No history reset is used.

The modeled lag-compensation budget uses the exact selected delay: `min(15, ceil(RTT_ms * 0.03) + delay + 2)`. The view tick already includes delay and is not adjusted twice. The model compares requested rewind against this budget and checks that no adaptive allowance exceeds fixed-six allowance. It uses nominal path RTT/uplink transit for this fairness calculation, not a full packet-level input/clock simulation.

## Schedules and gates

Delays and jitter below are milliseconds. Jitter is uniform independently on each direction; packet and echo-ACK loss are independently sampled. Lost snapshots never enter history; delivered snapshots may arrive out of order.

| Schedule | Down/up | Jitter | Loss |
|---|---:|---:|---:|
| LAN | 5/5 | 1 | 0% |
| Stable WAN | 30/30 | 3 | 0.2% |
| Normal | 50/50 | 20 | 2% |
| Poor | 80/80 | 35 | 5% |
| Downlink-heavy | 70/10 | 3 | 0.2% |
| Uplink-heavy | 10/70 | 3 | 0.2% |
| Burst transition | 5/5, then 70/30 during seconds 40–50 | 1, then 35 | 0.2%, then 10% |

Predeclared gates: LAN/stable-WAN mean age improves by at least one tick (16.67 ms); extrapolation increases by at most one percentage point; extrapolation-hold increases by at most 0.5 percentage point; correction p95 is at most `fixed * 1.1 + 0.001` world units; rewind-clamp fraction increases by at most 0.5 percentage point; no excess rewind allowance and no backward presented ticks.

## Results

Values are means across five seeds. An extrapolation hold means the history reached its three-tick extrapolation limit; transition time holding is independently covered by monotonicity and age.

| Schedule | Mean age ms fixed → adaptive | Extrapolated % fixed → adaptive | Hold % fixed → adaptive | Gate passes |
|---|---:|---:|---:|---:|
| LAN | 100 → 52.72 | 0 → 0 | 0 → 0 | 5/5 |
| Stable WAN | 100 → 83.83 | 0 → 0 | 0 → 0 | 0/5 |
| Normal | 100 → 100 | 0.57 → 0.57 | 0 → 0 | 5/5 |
| Poor | 100.10 → 100.10 | 31.23 → 31.23 | 0.50 → 0.50 | 5/5 |
| Downlink-heavy | 100 → 83.51 | 0.17 → 49.54 | 0 → 0 | 0/5 |
| Uplink-heavy | 100 → 83.51 | 0 → 0 | 0 → 0 | 5/5 |
| Burst transition | 100.01 → 61.80 | 1.73 → 1.87 | 0.05 → 0.09 | 5/5 |

Stable WAN narrowly misses the age gate because measured transition/hysteresis time makes the gain 16.17 ms. The decisive quality failure is downlink-heavy extrapolation, not this threshold edge. Its mean maximum position error increases from 0.02518 to 0.05658 world units, and mean maximum correction from 0.04390 to 0.05658. Correction p95 remains approximately 0.0062, showing why tail/error and extrapolation metrics matter together. Every case had zero backward presented ticks, zero excess allowance, and no added modeled clamp fraction.

## Cost and limits

Policy insertion-sort comparisons average about 18.2 per presentation frame. This is an operation-count proxy, **not measured CPU time**. Profile changes per 120 seconds are LAN 3, stable WAN 1, normal/poor 0, each asymmetric path 1, and burst 7. An illustrative eight-byte profile message plus headers/envelope/ACK costs 65 bytes per change, hence 0–455 bytes per case before retransmissions. This is a proposed lower-bound protocol cost, **not measured bandwidth**; no message was implemented.

The experiment assumes a perfect server clock, instantaneous profile synchronization, and retention of the exact profile for in-flight input epochs. These optimistic assumptions omit clock estimation error, profile delivery/retry timing, and real frame scheduling. There is no rendered-client, WAN performance, or fairness certification here. A future candidate needs a server-owned direction-aware signal, authenticated profile revision/effective-tick semantics, historical profile lookup for lag compensation, legacy fixed-six compatibility, and the same quality gates before live enablement. This rejection applies to this RTT-symmetry candidate, not to every adaptive policy.
