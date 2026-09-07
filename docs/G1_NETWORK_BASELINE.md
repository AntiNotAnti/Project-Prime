# G1.0 network baseline

Source: `b31bc5764b01da0d8dac8b1f261b11e2d791e312`; protocol 7; real AMHE1 extracted content. Source and asset files are not modified by these checks. Pre-existing LICENSE deletion and maps changes remain excluded.

Status: completed with failures and explicit measurement gaps. Commands, exit codes and durations are continuously recorded in `/tmp/codex-re-prime-g1/network/commands.json`; exact built assembly hashes in `network/binaries.json`. Binaries come from the completed G1 baseline solution Release build, not frozen R12 results.

Ordered workload: lifecycle, match phases/scoring, history/bomb/catch-up/homing/weapon/lock checks; twelve world modes; combat/spectator checks; 30-second Imperialist duel; full deterministic lag-compensation ON/OFF/trace-only matrix; full 16-case WAN matrices at 20 and 60 seconds/case; 300-second mixed-combat ON and OFF runs. The timed network matrices and mixed-combat runs execute sequentially to avoid contaminating each other's host load.

Evidence limits: these socket and simulation harnesses do not render. They expose snapshot counts, movement/input acknowledgements, rejected-packet counts and authoritative tick metrics; mixed clients also report final smoothed RTT. The mixed-combat workload exposes tick p50/p95/p99/max, CPU and allocation metrics. The current dedicated-server log does not expose the existing ServerInputStream StarvedTicks/SkippedCommands counters. Render timing, rendered interpolation and prediction correction measurements require a separate rendered-client run. Demo unit coverage and actual rendered demo playback are tracked separately; no live demo claim yet.

## Reproduced baseline failures

Each failed twice with the same error on the unchanged baseline binaries; original and `-repeat.log` evidence and `failure-repeats.json` are retained.

- `--match-lifecycle`: `Server did not bind. Run this command with FruityPrimeServer; dedicated servers use their own executable.` Source `tools/nettest/MatchLifecycleCheck.cs:264` launches the retired Client `-server` route.
- `--catch-up`: `Pending beam received an extra scene step.` Source assertion `CatchUpCheck.cs:86`.
- `--homing`: `Delayed homing acquisition/catch-up was not enabled.` Source assertion `HomingCheck.cs:89`.

Parent source trace identifies the latter two fixtures as constructing a separate ServerCombat while Scene.Services still points to the simulation's combat service; their old static-context assumption was not migrated. This diagnosis is separate from the reproduced runtime failures. Any fixture repair is a distinct post-baseline result and does not overwrite these binaries.

## Focused results completed

Match scoring (12 modes), match phases, history boundary, bomb pool, weapon policy, all nine shared-lock variants, all twelve authoritative world modes, direct combat (nine weapons and three afflictions), and spectator checks pass. The 30-second Imperialist real-socket duel passes: resolved damage=4, deaths=2, occluded shots=2, miss damage=0; both clients observe all four damage/two death events; 14 shots rewound and 20 history queries. The full deterministic lag-compensation matrix passes 30 paired comparisons (five scenarios x three delays x OFF/trace-only baselines), at the existing default 1200 ticks.

## Mixed-combat baseline failures

Both initial 300-second policy runs failed and are not acceptance passes. ON: one reliable-admission refusal disconnected slot 2; minimum peers=7, reliableOverflow=1. All clients misleadingly passed the cumulative health threshold, despite the dropped peer's stale snapshot/input-ack stream. OFF: all eight peers remained connected, no reliable overflow, but server scheduler dropped 3 ticks (maxDue=4); client aggregate also failed. These failure causes must not be conflated.

ReliableChannel has both a 32-pending-message cap and a 32-event-ID span guard. The current failure log does not reveal which condition refused admission. Retry interval is 150 ms; per-poll send budget is eight due reliable messages. Healthy peers received approximately 15,200 application events in 300 seconds (50.7/s); a 32-event window therefore covers roughly 0.63 seconds at this workload. The clients send an ACK for every validated reliable Event and drain the application event queue each tick. Exact pending occupancy, oldest pending ID, per-event attempt history and ACK history were not recorded; a capacity-vs-window root-cause claim is not yet supported. The enabled-policy run was repeated unchanged; see the repeat results below.

## WAN results

Measured on macOS 26.6.1 (25G76), Apple M4 Pro, 12 logical CPUs, 24 GiB RAM. Other development work may run on this host. These are observations, not isolated capacity limits.

| Seconds | Case | Players | Result | Mean tick ms | Worst tick ms | Dropped ticks | Catch-up ticks | Queue drops |
|---:|---|---:|---|---:|---:|---:|---:|---:|
| 20 | asymmetric | 4 | PASS | startup only | startup only | startup only | startup only | startup only |
| 20 | extreme | 2 | PASS | startup only | startup only | startup only | startup only | startup only |
| 20 | extreme | 4 | PASS | startup only | startup only | startup only | startup only | startup only |
| 20 | extreme | 8 | PASS | startup only | startup only | startup only | startup only | startup only |
| 20 | good | 2 | PASS | startup only | startup only | startup only | startup only | startup only |
| 20 | good | 4 | PASS | startup only | startup only | startup only | startup only | startup only |
| 20 | good | 8 | PASS | startup only | startup only | startup only | startup only | startup only |
| 20 | lan | 2 | PASS | startup only | startup only | startup only | startup only | startup only |
| 20 | lan | 4 | PASS | startup only | startup only | startup only | startup only | startup only |
| 20 | lan | 8 | PASS | startup only | startup only | startup only | startup only | startup only |
| 20 | normal | 2 | PASS | startup only | startup only | startup only | startup only | startup only |
| 20 | normal | 4 | PASS | startup only | startup only | startup only | startup only | startup only |
| 20 | normal | 8 | PASS | startup only | startup only | startup only | startup only | startup only |
| 20 | poor | 2 | PASS | startup only | startup only | startup only | startup only | startup only |
| 20 | poor | 4 | PASS | startup only | startup only | startup only | startup only | startup only |
| 20 | poor | 8 | PASS | startup only | startup only | startup only | startup only | startup only |
| 60 | asymmetric | 4 | PASS | 0.279 | 35.698 | 0 | 1 | 0 |
| 60 | extreme | 2 | PASS | 0.147 | 39.821 | 0 | 1 | 0 |
| 60 | extreme | 4 | PASS | 0.279 | 21.814 | 0 | 0 | 0 |
| 60 | extreme | 8 | PASS | 0.430 | 29.433 | 0 | 0 | 0 |
| 60 | good | 2 | PASS | 0.138 | 22.108 | 0 | 0 | 0 |
| 60 | good | 4 | PASS | 0.191 | 28.456 | 0 | 0 | 0 |
| 60 | good | 8 | PASS | 0.236 | 39.574 | 0 | 1 | 0 |
| 60 | lan | 2 | PASS | 0.166 | 35.251 | 0 | 1 | 0 |
| 60 | lan | 4 | PASS | 0.182 | 26.910 | 0 | 0 | 0 |
| 60 | lan | 8 | PASS | 0.216 | 20.436 | 0 | 1 | 0 |
| 60 | normal | 2 | PASS | 0.182 | 28.806 | 0 | 0 | 0 |
| 60 | normal | 4 | PASS | 0.199 | 24.407 | 0 | 0 | 0 |
| 60 | normal | 8 | PASS | 0.401 | 36.621 | 0 | 1 | 0 |
| 60 | poor | 2 | PASS | 0.180 | 27.741 | 0 | 0 | 0 |
| 60 | poor | 4 | PASS | 0.205 | 17.535 | 0 | 0 | 0 |
| 60 | poor | 8 | PASS | 0.319 | 38.910 | 0 | 1 | 0 |

Simulation client rows retain snapshot counts, movement distance, input acknowledgement and rejected-packet counts. RTT/jitter are impairment inputs here, not measured client latency. Input starvation/skipped-command counters and rendered prediction/interpolation metrics are not emitted by this harness.

## Mixed combat results

| Mode | Result | Ticks | p50 ms | p95 ms | p99 ms | Max ms | Bytes/tick | CPU seconds |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| on | False | 18000 | 0.3727 | 1.5047 | 2.4889 | 12.278 | 62950.069777777775 | 13.191905 |
| off | False | 17997 | 0.2925 | 1.1352 | 2.2355 | 30.7658 | 63047.593710062785 | 11.823385 |

## Unchanged enabled-policy repeat

`network/mixed-on-repeat-command.json` records the exact command and exit 1. This second 300-second ON run did **not** reproduce the reliable-admission disconnect: the server passed, retained eight peers, and reported zero reliable overflow, zero dropped server ticks and zero transport/combat/catch-up queue drops. Server tick p50=0.2934 ms, p95=0.7586 ms, p99=1.3351 ms, maximum=15.4558 ms. The overall run still failed because the client scheduler dropped three ticks; every individual client met the current cumulative health checks.

Conclusion: one observed reliable overflow remains intermittent, while scheduling overrun failures recur across processes/runs. No long mixed-combat soak is a passing acceptance result. Do not weaken queue bounds or silently drop terminal events. The next diagnostic should record the exact admission refusal reason, oldest pending event ID/age, pending count, retransmission attempts, ACK progression and reliable event rate in a separate instrumented run; existing logs cannot establish which admission guard failed.

## Demo and presentation limits

No actual recorded-demo playback pass was produced. The planned hidden `-netcheck -recorddemo` / `-democheck` helper was deferred before creating clients to keep network measurements isolated and because the independent platform probe demonstrates the required native context is unavailable: `NSGL: The compatibility profile is not available on macOS`. See `/tmp/codex-re-prime-g1/maptest-render-probe.log` and `baseline-platform.md`. Unit/demo-reader coverage belongs to the independent `baseline-tests.md` result (434 tests pass); it is not a rendered demo playback claim.

No measured physical 60/120/144/240-Hz render timing, rendered prediction corrections or interpolation statistics is available here. The existing mixed client does exercise SnapshotInterpolation's delayed timeline, but does not serialize its metrics. Input starvation/skipped-command counts exist in ServerInputStream but are not emitted. Future instrumentation is required; zero must not be substituted for missing measurements.

## Final evidence summary

- Exact b31bc57 assembly hashes were rechecked unchanged after the timed matrices.
- 20-second WAN: 16/16 pass. 60-second WAN: 16/16 pass.
- Deterministic lag compensation: 30 paired comparisons pass.
- Focused lifecycle/catch-up/homing fixtures each fail twice identically; other executed focused probes pass.
- Mixed ON/OFF and repeated ON aggregate results all fail, with the distinct reasons above.
- No production files, queue policy, packet format or game assets changed by this baseline task. Scratch output only.
