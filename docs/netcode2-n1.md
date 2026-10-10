# Netcode 2.0 — N1 wire budgets and transport correctness

N1 starts at merged main `0e45a0d635ba47e781fdfd1346919f707679494c`
(N0, PR #450). Branch: `codex/net2-01-wire-and-queues`. Production source
tested for final acceptance: `981e52ac799dcc670aab93ae3d1b2b4686433e17`.
Protocol remains **41**. Packet/replay layouts, reliable-channel behavior,
queue capacities, combat authority and renderer are unchanged. N2 is not included.

## Changes

`NetTransport.CaptureBudget()` reports fixed counters for Intent, SlotIntent,
SnapshotFast, slow/world state, HitClaim, CombatAck, reliable control, map transfer,
cosmetics, replay, match results, discovery, other traffic, ACK-only and malformed
datagrams. Reports include packets, UDP datagram bytes, maximum datagram size,
lifetime average byte rates and dequeue age histograms. Each admitted connection
has its own counters, keyed by endpoint and connection incarnation.

Counts are taken at socket receive and successful socket send. They include the
24-byte transport header and 4-byte reliable event ID where present, but exclude
UDP/IP/link headers. Inbound artificial loss/duplication is applied after counting;
outbound artificial loss is applied before sending/counting. Oversized receive
counts reflect the receive buffer's observed/truncated length. The aggregate
includes unauthenticated/discovery traffic; endpoint counters start at admission,
so the first Welcome and unbound packets can appear only in the aggregate.
Retirement removes an endpoint from subsequent snapshots; saved time windows
retain its earlier counters. Delayed sends cannot charge a replacement incarnation.

Queue age is socket arrival to application dequeue, including injected receive
holds, with inclusive millisecond histogram edges 1, 2, 4, 8, 16, 32, 64, 128,
256, 512, 1024, unbounded. It is **not** one-way network latency. Coalesced packets
count as wire traffic; only the surviving packet contributes a dequeue sample.
The diagnostic arrival timestamp is separate from the existing application
timestamp used by countdown logic. Playback admission/timing remains unchanged.

Input age measures the local frames since the accepted intent arrived, sampled
when remote controls are applied. Inclusive edges are 0, 1, 2, 4, 8, 16, 32, 64,
128, 256, unbounded frames; unavailable ages have a separate counter. It does not
estimate a remote wall clock or change stale-input policy. Existing connection
attempts/ACKs, estimated loss, reliable retries/pending/high water and connection
lock wait/hold counters are exported alongside these observations.

With `PRIME_NET2_EVIDENCE` enabled, the existing campaign report additionally saves
one transport snapshot per second (maximum 2,048 snapshots) and existing
WorldReady admission/load/bootstrap durations with rejoin/late-join flags and
match/slot identity. Snapshot allocation happens at reporting cadence; counter
recording allocates no per-packet objects or strings and performs no disk I/O.
Counters use fixed arrays per existing bounded connection (maximum 320); no
dictionary keyed by arbitrary inbound endpoints is added. Evidence overflow still
fails the existing campaign gate. New observations are local campaign evidence,
not additions to the production telemetry upload schema.

Snapshots are approximate concurrent reads. Adjacent cumulative-byte deltas divided
by elapsed monotonic seconds give interval rates; do not subtract different
connection incarnations. Lifetime average rates include startup and idle time.

## Existing budgets and result recovery

| Receive lane | Packets/s | Burst |
| --- | ---: | ---: |
| Intent | 180 | 64 |
| Realtime | 2,000 | 512 |
| Critical | 60 | 128 |
| Background | 100 | 32 |
| Facts from an admitted authority | 1,920 | 128 |
| Discovery, shared across transport | 300 | 128 |

These are shared lane admission limits, not per-packet-type allowances or send
targets. The evidence exports each lane's conservative byte ceiling using the
1,472-byte transport maximum. Current Intent/SlotIntent/SnapshotFast datagrams
remain within 1,200 bytes. Inbox capacity is 2,048 including coalescing cells;
the ordinary inbox retains 128 critical slots. Per-frame pump budgets remain
128 critical / 256 realtime / 32 background. Reliable pending capacity remains
40 with 32 ordinary slots and a 256-event dedup span. Queue current/high-water
counters retain their existing FIFO scope; the nine bounded coalescing cells
are not included in that high-water measurement.

The production behavior change is `PostMatchReport` moving to the critical inbox
and critical admission bucket. It is an existing full authoritative result
summary, published at match end and repeated every second during postmatch.
Those repeats can recover a lost report while cosmetic/replay queues are full,
without consuming the semantic reliable queue or enlarging any queue.
The regression fills the background inbox, loses the initial report, then applies
the repeated report through the actual client result handler and checks statistics,
objectives and wrong-match rejection.

This recovers final statistics/objectives/names, not every intermediate award or
announcement. Points/standings also use the existing snapshot lane. Recovery
requires a surviving connection and receipt before the postmatch interval ends;
the existing bounded reliable expiry and semantic-history-overrun disconnects
remain in effect. Existing report generation-validation limitations are not
changed by this slice.

## Validation and provenance

The Mac is the validation host. Windows was not needed for these platform-neutral
changes; no Linux/Android test or CI run is requested. The owner waived another
long soak. Short 2/4/8-player controls, packet-pressure tests and a short rotation
exercise the relevant N1 gate. The N0 combat limitations remain recorded in
[N0 completion](netcode2-n0-completion.md); N1 does not waive them.

Baseline game binaries come from N0 source
`99072453425b37c9d343e8e101ea19a5a6a906e4`, whose `src/` tree is identical to
the merged N1 base. Engine SHA-256:
`57d2860d74fb9e374e36acc485cb82d55fd0a0db1611ee6ce07c48edfae0aa63`.
All baseline and candidate attempts retain their original logs, manifests,
asset hashes and adjudications under `artifacts/net2-n1/`.

The final engine SHA-256 is
`84b9ce3ecb499935b8473607e3af7fba7f07c3a5484385d6357f8def90a750d6`;
the final test harness SHA-256 is
`819bd318338b3ff8536a561c372a51b29982cc83f937a7a66d4cfac9337b1b5a`.
The test result files' `assembly_sha256` field names the harness DLL; campaign
manifests' `assemblySha256` names the engine DLL. Both are retained separately.

**N1 transport gate: PASS. Full combat acceptance: not established.**

| Final Mac run | N1 transport / bounds | Whole campaign | Missing view commits | Peak FIFO queue |
| --- | --- | --- | ---: | ---: |
| 2 players, 25 s | PASS / PASS | FAIL: N0-pattern combat gap | 1 | 14 |
| 4 players, 25 s | PASS / PASS | PASS | 0 | 35 |
| 8 players, 25 s | PASS / PASS | FAIL: N0-pattern combat gaps | 3 | 54 |
| 2 players, 60 s, two rooms | PASS / PASS | PASS, rotation PASS | 0 | 16 |

All final runs had zero connection loss, semantic disconnect/overrun, queue drop,
socket error, simulation step failure, health mismatch or semantic gap. The failed
2-player key is `(639272642031772890, 2, 0, 1, 1, 23)`: Missile committed by
authority at frame 1021, delivered to peer 1, first sampled shooter death at 1026.
The failed 8-player key is `(639272642739418980, 2, 4, 1, 2, 23)`: Missile committed
at 1331, delivered to peers 1/3/7, first sampled death at 1332. These match the N0
delivered-event/missing-view-commit phenotype assigned to N3; the sampled death
proximity does not prove exact ordering. The combat FAIL verdicts are retained,
not converted to passes by the narrower N1 gate.

Release build: 0 errors, 41 existing warnings. Final-source queue/boundary/wire
accounting/result recovery, reliable delivery, allocation and actual UDP stress
suites passed. Stress delivered 128 controls exactly once across eight peers
with 0–400 ms configured RTT, loss/jitter and a 400 ms pump stall. Its maximum
FIFO occupancy was 23; lock contention was 0.076% with a 0.063 ms maximum wait.
Warmed counter recording and connected UDP sending allocated **0 B/op**.
Protocol/architecture checks and all 44 Python evidence tests also passed on
the earlier N1 commit, before the isolated timestamp correction; their code is
unchanged. The final lag regression proves measurement includes receive hold
without moving the application arrival timestamp.

The original `fa85d4f8` 2/4-player attempts passed and are preserved as superseded
evidence. Review found its metric timestamp could also affect injected-latency
countdown timing; `981e52ac` separates that timestamp and is the final tested
production source. The final runs were not repeated to erase combat failures.

| Players | Baseline / candidate server TX, B/s* | Baseline / candidate step mean, ms | Baseline / candidate step p95, ms |
| --- | ---: | ---: | ---: |
| 2 | 204,729 / 204,628 | 0.541 / 0.685 | 0.83 / 0.76 |
| 4 | 1,052,693 / 1,057,570 | 0.501 / 0.594 | 0.73 / 0.92 |
| 8 | 4,578,458 / 4,653,033 | 0.557 / 0.899 | 0.99 / 1.37 |

*Comparable aggregate bytes divided by requested 25 seconds, including startup
and tail; these are not steady-state rates. Each entry is one short run, includes
startup/JIT and evidence capture, and is not an isolated overhead benchmark.
The eight-player candidate timings were higher; this slice claims no performance
improvement or broad performance acceptance. It preserves the transport gate.
Actual sampled eight-player server TX was about 4.035 MB/s, including 3.447 MB/s
SlotIntent and 0.500 MB/s SnapshotFast. SlotIntent accounts for about 85.4% of that
sampled outbound traffic, establishing a concrete baseline for N2.

Rotation recorded two initial admissions and two new-room readiness observations;
new-room load-to-ready was 201.743 ms, bootstrap-to-ready 4.114 ms. Input-age windows
are populated on server and clients; slot removal resets its counters, so final
post-disconnect zero values are not interpreted as missing samples. Actual
connection-rejoin latency, rendered projectile agreement, specialized combat
acceptance, Windows and another long soak are **NOT_RUN** in this N1 validation.

The [N1 ledger](../tools/nettest/baselines/net2-n1-20261010.json) records exact
sources, commands, artifact hashes, all attempts and gate scopes. Complete raw
reports and primary binaries are retained in `artifacts/net2-n1-evidence.tar.gz`
with a per-file SHA-256 index; staged dependency directories remain local.

## Source mapping and rollback

| Existing source | N1 addition |
| --- | --- |
| NetTransport / NetDatagram | Socket byte accounting, incarnation counters, dequeue age, budget snapshots |
| NetTelemetry / NetHooks | Consumed input age, preserving existing acceptance/staleness decisions |
| WorldBootstrap / DedicatedServer / NetCampaignEvidence | Readiness durations and bounded local report windows |
| NetPacketQueue | Critical admission for the repeated full match-results report |
| tools/nettest | Boundary, real UDP ingress, wire allocation/accounting, lag-timestamp and result recovery regressions |

Leave `PRIME_NET2_EVIDENCE` unset to disable campaign report retention. Revert the
N1 PR to remove the production counters and restore the previous report priority;
there is no wire or saved-data migration. N0 delivery/loading repairs remain on main.
