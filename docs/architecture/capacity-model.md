# Worker capacity model

Status: A23 bounded host-specific measurement accepted on 2026-09-08. A25 is
marked complete for administrative tracking by user confirmation, but no local
eight-hour soak artifact was independently verified in this run. This is an
evidence record for the current Release binaries and one macOS host. It is not
a capacity ceiling, a production density default, or a substitute for
cross-host validation.

## Result boundary

The highest tested topology that met the active-window scheduler-drop criterion
for all three roster variants was `8/1/4`: eight matches per Worker, one
Worker, and four simulation lanes. The topology notation in this document is
`matches-per-Worker / Workers / lanes`. The criterion is bounded to the
observed workload: every roster row must have an active-window
`droppedTicks` delta of zero. The `16/1/4` topology was also exercised, but it
had nonzero active-window scheduler drops in one roster row, so it does not
meet that criterion.

The A25 candidate is four matches per Worker with two lanes and two matches per
lane:

```text
--max-matches 4 --lanes 2 --max-matches-per-lane 2
```

The runtime fallback remains one match with one lane because the local
eight-hour soak artifact was not independently verified in this run. The
administrative A25 status does not freeze this candidate or make it
production-ready; cross-host validation remains required.

## Method

The run used the repository's sequential matrix runner,
`tools/worker-soak/run-capacity-matrix.sh`, against caller-supplied frozen
Release assemblies. Each row was an independent `worker-soak` child process;
no density was inferred from a prior row or from a rolling p99. The fixed
topology rows were:

```text
2/1/1, 4/1/1, 4/1/2, 8/1/2, 8/1/4, 16/1/4, 4/2/2
```

Every topology was run with each of these roster variants (`players / bots /
observers`):

```text
1/2/1, 2/1/2, 4/0/0
```

That produces 21 unique rows. Every row requested 180 seconds of workload,
30-second rounds, and a required drain. Crash, outage, reconnect, and rematch
injection were disabled for this controlled density matrix. The runner required
zero match failures and a clean drain for every child. The harness exercised
the real Node coordinator, Worker processes, signed UDP actors, and local
report ingestion used by the Worker/Node path.

The captured matrix manifest is `/tmp/codex-re/worker-capacity-20260908/matrix.jsonl`.
It records all 21 unique run records and `exitStatus: 0` for each child. The
parent matrix session was reaped before the final poll, so this evidence must
not claim that a captured parent exit was observed. The aggregate
`/tmp/codex-re/worker-capacity-20260908/analysis.json` is therefore the source
for the post-run row and topology summaries, while the child status claim is
limited to the 21 manifest records.

Each row also wrote `summary.json`, `metrics-00.jsonl`, `trend.jsonl`,
`critical-events.jsonl`, reports, replays, telemetry, and the local backend
database below its own run directory. The row summary requires active traffic,
logical occupancy of at least 0.9, durable report reconciliation, and zero
payload-hash mismatches before it reports success.

### Metric interpretation

`SimulationLane` stores up to 600 recent tick-duration samples per lane and
publishes p50, p95, p99, and maximum from that latest sample window. The
percentiles in the tables are rolling-window values; topology rows select the
worst lane and roster result. They are not averaged across lanes, roster rows,
Workers, or sequential runs.

`FixedTickScheduler.CatchUpTicks` and `DroppedTicks` are cumulative counters
from lane startup. The analysis retains each lane's first and last observed
counter and reports `catchup_delta` and `dropped_delta` by subtraction. The
delta is the active-window signal and excludes work already accumulated during
startup. A topology meets the scheduler-drop criterion only when its maximum
`dropped_delta` across all three roster rows is zero.

CPU is the peak Worker process value reported by `WorkerHealth`; it is
normalized to this host's 12 logical CPUs and is shown per Worker. Memory is
reported per Worker. `Rx/Tx` in the row table is the row's network byte delta
divided by its 180-second workload interval. UDP queue drops are transport
queue drops, separate from scheduler `DroppedTicks`; the two observed nonzero
cases were 2 in the `4/1/1` mixed `2/1/2` row and 4 in the two-Worker
`4/2/2` four-player row. Packet rejects are retained as the Worker's
`PacketsRejected` counter and do not imply a match failure by themselves.

## Host and frozen inputs

The matrix provenance record identifies the measurement host and input trees:

| Item | Value |
|---|---|
| Host | `Jarretts-MacBook-Pro` |
| OS | macOS `26.6.1`, build `25G76` |
| Architecture | `arm64` |
| CPU | Apple M4 Pro |
| Logical CPUs | `12` |
| Memory | `25,769,803,776` bytes (24 GiB) |
| .NET SDK | `10.0.400` |
| Content tree | `AMHE1`, 3,550 files, manifest SHA-256 `3313abb40ce64fa40fd169d737b252e5b712a73332acf156180b68b7f7ab23f4` |
| Worker assembly | `src/Server.Worker/bin/Release/net10.0/ProjectPrime.Server.Worker.dll` |
| Worker assembly SHA-256 | `0aaa4205e6090ab9057937e5999e65e980f959cf6c74f4ca78d60c8f70101c79` |
| Worker runtime | 209 files, manifest SHA-256 `639dd740fd5f7623e296c44779a228420b5effe80eb1f37a0cc52b9e2ca7a00e` |
| Soak assembly | `tools/worker-soak/bin/Release/net10.0/worker-soak.dll` |
| Soak assembly SHA-256 | `8c78b5e4fb04bfe2758cac93cebdfa80fad8763e6fb2517c8ead3c0b893588ed` |
| Soak runtime | 487 files, manifest SHA-256 `c5cf2ff1d5bee169cd29c56c2b3d6860c6237bf65d10fbfbe8e68483890e6bff` |
| Run content identity | `AMHE1`, content SHA-256 `1f1d8a705c614802719e8fc7a872789aaa7421d027539acf176e7557156a1805` |
| Run build identity | `1.0.0+9ef8cb5d572837c7d1efcc821ea4e3ae1bf7c615`, protocol `9` |

The assembly and runtime manifest hashes are frozen provenance for this result;
they do not identify a future build or another host.

## Worst result by topology

Each row below is the maximum of the three roster rows for that topology. A
`yes` criterion value means that all three roster rows had zero active-window
scheduler drops. Memory values use binary MiB. `Drain` is the slowest row
drain in seconds.

| Topology (M/W/L) | Rows | Worst p95 ms | Worst p99 ms | Worst max ms | Catch-up delta max | Scheduler drop delta max | Criterion | CPU peak %/Worker | Managed heap MiB/Worker | Working set MiB/Worker | UDP queue drops max | Packet rejects max | Drain max s |
|---:|---:|---:|---:|---:|---:|---:|:---:|---:|---:|---:|---:|---:|---:|
| 2/1/1 | 3 | 2.929 | 10.98 | 52.71 | 2 | 0 | yes | 3.232 | 233.0 | 294.9 | 0 | 186 | 46.759 |
| 4/1/1 | 3 | 3.415 | 10.64 | 44.79 | 1 | 0 | yes | 5.701 | 326.6 | 326.1 | 2 | 461 | 41.020 |
| 4/1/2 | 3 | 3.263 | 8.476 | 42.21 | 2 | 0 | yes | 4.749 | 328.8 | 339.4 | 0 | 473 | 41.014 |
| 8/1/2 | 3 | 2.923 | 12.11 | 47.83 | 31 | 17 | no | 6.333 | 390.1 | 404.2 | 0 | 1,143 | 38.828 |
| 8/1/4 | 3 | 3.366 | 10.75 | 43.69 | 4 | 0 | yes | 6.557 | 389.2 | 354.9 | 0 | 1,255 | 39.654 |
| 16/1/4 | 3 | 5.154 | 13.05 | 47.90 | 15 | 1 | no | 9.279 | 651.2 | 501.0 | 0 | 2,404 | 83.227 |
| 4/2/2 | 3 | 2.872 | 11.71 | 51.54 | 6 | 0 | yes | 4.057 | 326.8 | 316.3 | 4 | 1,107 | 50.133 |

The selected two-Worker maximum lane duration was `51.5433 ms`, in the
`4/2/2` `2/1/2` row. It is a measured tail value for that run, not a target
budget or a cross-host guarantee.

## All 21 rows

The row table preserves the roster-specific result rather than hiding the
mixed and four-player cases inside an average. `P/B/O` means
`players / bots / observers`; `Rx/Tx` is MiB/s over the 180-second workload.

| Run | Topology (M/W/L) | P/B/O | Completed | p95/p99/max ms | Catch-up delta | Scheduler drop delta | CPU %/Worker | Working set MiB/Worker | Rx/Tx MiB/s | UDP queue drops | Packet rejects | Drain s |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 000-matrix-1-2-1 | 2/1/1 | 1/2/1 | 10 | 2.442/7.795/42.32 | 1 | 0 | 3.232 | 294.9 | 0.013/0.109 | 0 | 53 | 34.538 |
| 001-matrix-2-1-2 | 2/1/1 | 2/1/2 | 10 | 2.929/7.568/52.71 | 2 | 0 | 2.672 | 267.2 | 0.026/0.208 | 0 | 154 | 43.837 |
| 002-matrix-4-0-0 | 2/1/1 | 4/0/0 | 10 | 2.385/10.98/47.94 | 1 | 0 | 2.548 | 271.8 | 0.051/0.228 | 0 | 186 | 46.759 |
| 003-matrix-1-2-1 | 4/1/1 | 1/2/1 | 20 | 3.015/7.213/37.17 | 1 | 0 | 4.801 | 326.1 | 0.027/0.210 | 0 | 118 | 41.020 |
| 004-matrix-2-1-2 | 4/1/1 | 2/1/2 | 16 | 3.415/8.821/44.79 | 1 | 0 | 5.338 | 271.9 | 0.050/0.398 | 2 | 331 | 12.529 |
| 005-matrix-4-0-0 | 4/1/1 | 4/0/0 | 16 | 2.710/10.64/38.84 | 1 | 0 | 5.701 | 319.6 | 0.097/0.441 | 0 | 461 | 12.451 |
| 006-matrix-1-2-1 | 4/1/2 | 1/2/1 | 20 | 2.211/8.476/42.21 | 1 | 0 | 4.749 | 339.4 | 0.027/0.210 | 0 | 119 | 41.014 |
| 007-matrix-2-1-2 | 4/1/2 | 2/1/2 | 16 | 3.263/7.380/39.05 | 2 | 0 | 3.455 | 322.1 | 0.050/0.401 | 0 | 332 | 12.034 |
| 008-matrix-4-0-0 | 4/1/2 | 4/0/0 | 16 | 2.031/7.626/35.83 | 2 | 0 | 3.361 | 316.2 | 0.097/0.439 | 0 | 473 | 12.479 |
| 009-matrix-1-2-1 | 8/1/2 | 1/2/1 | 32 | 2.062/7.165/47.83 | 31 | 17 | 5.058 | 404.2 | 0.052/0.412 | 0 | 220 | 5.713 |
| 010-matrix-2-1-2 | 8/1/2 | 2/1/2 | 32 | 2.923/6.040/41.15 | 2 | 0 | 5.051 | 363.7 | 0.084/0.688 | 0 | 1,025 | 38.828 |
| 011-matrix-4-0-0 | 8/1/2 | 4/0/0 | 32 | 2.583/12.11/39.98 | 2 | 0 | 6.333 | 348.7 | 0.165/0.778 | 0 | 1,143 | 36.343 |
| 012-matrix-1-2-1 | 8/1/4 | 1/2/1 | 32 | 2.370/7.411/43.69 | 1 | 0 | 5.264 | 294.0 | 0.051/0.410 | 0 | 238 | 6.599 |
| 013-matrix-2-1-2 | 8/1/4 | 2/1/2 | 32 | 2.758/6.133/25.75 | 0 | 0 | 5.447 | 268.1 | 0.086/0.696 | 0 | 1,018 | 37.311 |
| 014-matrix-4-0-0 | 8/1/4 | 4/0/0 | 32 | 3.366/10.75/41.22 | 4 | 0 | 6.557 | 354.9 | 0.163/0.761 | 0 | 1,255 | 39.654 |
| 015-matrix-1-2-1 | 16/1/4 | 1/2/1 | 64 | 3.096/9.357/39.42 | 15 | 1 | 9.279 | 413.0 | 0.090/0.734 | 0 | 694 | 27.075 |
| 016-matrix-2-1-2 | 16/1/4 | 2/1/2 | 64 | 5.154/13.05/47.90 | 4 | 0 | 8.857 | 404.2 | 0.149/1.236 | 0 | 2,130 | 83.227 |
| 017-matrix-4-0-0 | 16/1/4 | 4/0/0 | 64 | 1.867/11.69/46.84 | 7 | 0 | 9.236 | 501.0 | 0.282/1.368 | 0 | 2,404 | 80.618 |
| 018-two-worker-scaling-1-2-1 | 4/2/2 | 1/2/1 | 40 | 2.799/7.441/40.60 | 2 | 0 | 3.280 | 316.3 | 0.053/0.418 | 0 | 202 | 50.133 |
| 019-two-worker-scaling-2-1-2 | 4/2/2 | 2/1/2 | 32 | 2.872/11.71/51.54 | 6 | 0 | 3.738 | 306.5 | 0.088/0.714 | 0 | 992 | 36.029 |
| 020-two-worker-scaling-4-0-0 | 4/2/2 | 4/0/0 | 32 | 2.522/11.51/38.91 | 2 | 0 | 4.057 | 291.5 | 0.170/0.788 | 4 | 1,107 | 34.487 |

## Maximum-player supplemental rows

The 21-row matrix above intentionally used its three fixed roster variants.
Two direct supplemental runs extend the bounded evidence to the maximum-player
roster (`8/0/0`, eight players with no bots or observers) at the already tested
`4/1/2` and `4/2/2` topologies. These rows are supplemental evidence; they do
not change the matrix's `8/1/4` active-window criterion result or turn A23 into
a capacity ceiling.

Both direct children exited 0, had no failures or interrupted matches, had no
payload-hash mismatches, and drained their outboxes. The one-Worker run
completed and persisted `16/16` matches/reports with logical occupancy
`0.9998445` and `QueueDrops: 0`. The two-Worker run completed and persisted
`32/32` matches/reports with logical occupancy `0.9998393` and
`QueueDrops: 0` on both Workers. The raw evidence is under
`/tmp/codex-re/worker-capacity-supplement-20260908/`; both runs used the frozen
assembly hashes recorded above.

The lane values below are the maximum observed rolling p99 and maximum values
for each lane across the samples. Startup counters are shown separately from
the active-window deltas; both supplemental runs had active catch-up/drop
deltas of `0/0` on every lane.

| Run | Topology (M/W/L) | Roster (P/B/O) | Lane | p99/max ms | Startup catch-up/drop | Active catch-up/drop |
|---|---:|---:|---:|---:|---:|---:|
| One Worker | 4/1/2 | 8/0/0 | 0 | 10.6571/30.0619 | 3/5 | 0/0 |
| One Worker | 4/1/2 | 8/0/0 | 1 | 4.8126/28.054 | 0/0 | 0/0 |
| Worker A | 4/2/2 | 8/0/0 | 0 | 11.1869/25.7998 | 3/5 | 0/0 |
| Worker A | 4/2/2 | 8/0/0 | 1 | 5.9538/29.1108 | 0/0 | 0/0 |
| Worker B | 4/2/2 | 8/0/0 | 0 | 10.7717/26.4504 | 3/5 | 0/0 |
| Worker B | 4/2/2 | 8/0/0 | 1 | 4.1206/24.8268 | 0/0 | 0/0 |

The two-Worker supplemental health samples retained normalized CPU peaks of
`4.0824%` for Worker A and `3.7391%` for Worker B, managed heaps of `286.8`
and `300.0 MiB`, working sets of `318.3` and `291.3 MiB`, and packet rejects
of `1,162` and `1,383`, respectively. Packet rejects remain a retained
diagnostic counter and did not produce a match failure in these direct runs.

## Aggregate reconciliation and throughput denominators

Across all 21 rows, the analysis records:

| Measure | Result |
|---|---:|
| Matches created/completed | 622/622 |
| Reports ingested/persisted | 622/622 |
| Replays | 622 |
| Telemetry artifacts | 622 |
| Failures | 0 |
| Interrupted matches | 0 |
| Payload-hash mismatches | 0 |
| UDP packets received/sent | 5,507,135 / 3,973,228 |
| UDP bytes received/sent | 352,854,978 / 2,197,984,680 |
| UDP queue drops | 6 |
| Packet rejects | 14,655 |
| Replay bytes | 426,086,138 |
| Telemetry bytes/events | 15,370,816 / 71,178 |

The 21 rows ran sequentially with 180 seconds of requested workload each, so
the aggregate time-throughput denominator is `21 * 180 = 3,780` workload
seconds. On that denominator, replay payload throughput was 112,721 B/s and
telemetry payload throughput was 4,066 B/s. Per-match artifact size uses the
622 completed matches as its denominator: 685,026 replay bytes/match and
24,712 telemetry bytes/match (114.4 telemetry events/match). Startup and
drain time are excluded from both denominators; neither denominator is an
average of row percentiles.

## A25 administrative status and A26 evidence limits

A25 is complete for administrative tracking by user confirmation. The plan's
minimum eight-hour soak artifact was not independently verified locally in this
run. This record therefore makes no claim about lifetime memory stability,
reconnect retention, a frozen capacity setting, or cross-host operational use.
The existing `CriticalEventLog` bound remains relevant: it caps evidence at
8,192 records and 4 MiB, which cannot hold the proposed eight-hour reconnect
evidence without scenario-derived bounded capacity or complete archival chunks.

A26 is accepted on the native `osx-arm64` fresh-extracted package smoke: WSS
authentication, public lobby create/configure/start, real Worker handoff, UDP
admission, match end, replay/artifact writes, graceful drain and no-orphan
checks passed; the empty-content negative exited 1. The package boundary is
the persistent `Server.Node` apphost at the root and the Node-managed
`Server.Worker` apphost below `worker/`; there is no Worker `--standalone` mode.
Private or unlisted local hosting is retired from the client cutover, which
creates and joins public lobbies through the Node.

This document records source, local process, loopback, and artifact evidence
only. It makes no claim about live Windows or Android behavior, deployed
services, WAN connectivity, rendered client behavior, or a database service
outside the captured local run.
