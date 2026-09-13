# Project Prime Network and Lag Compensation Implementation Status

This record covers the P1 and P2 implementation from the NetPlay, Online
Architecture, Responsiveness, Reliability, and Performance Plan at
`634608ec3bbead57a0fdd72766063d38c70a0b62`. P0 is treated as complete at the
user's direction; this record does not independently re-certify P0 platform CI.

## Scope and invariants

- The Worker remains a fixed 60 Hz, server-authoritative, single-writer
  simulation. The client cannot select timing, rewind, result, lobby, or
  reconnect authority.
- Gameplay remains direct UDP to the Worker; lobby/session control remains on
  the Node connection.
- Queues, telemetry, reliable admission, timing values, latency probes, and
  retry behavior are bounded.
- `MaxRewindTicks` remains 15.
- P3 protocol/placement expansion, rollback, delta snapshots, multiple reliable
  streams, and client-authoritative gameplay remain out of scope.

## P1 result

### Snapshot cadence

Workers accept only 30 or 60 Hz authoritative snapshot cadence through
Node-owned configuration. The default remains 30 Hz.

The deterministic codec experiment over 120 simulation ticks produced:

| Players | Rate | Player packets | Player bytes | Observer packets | Observer bytes |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 2 | 30 Hz | 120 | 26,160 | 60 | 13,080 |
| 2 | 60 Hz | 240 | 52,320 | 120 | 26,160 |
| 4 | 30 Hz | 240 | 98,400 | 60 | 24,600 |
| 4 | 60 Hz | 480 | 196,800 | 120 | 49,200 |
| 8 | 30 Hz | 480 | 381,120 | 60 | 47,640 |
| 8 | 60 Hz | 960 | 762,240 | 120 | 95,280 |

This is synthetic packet accounting, not rendered WAN evidence. The 60 Hz
setting therefore remains an experiment rather than the production default:
bandwidth doubles, and no geographic rendered-WAN evidence was available to
show that the presentation improvement clears the plan's release gate.

### Unified timing

Protocol 15 carries versioned `NetworkTimingProfile` updates and bounded client
timing telemetry with a `PresentedFrames` denominator. The Worker is the policy
owner. Profiles move one adjacent step at a time, increase safety quickly,
require an eight-second clean dwell to reduce buffering, and fall back safely if
acknowledgement does not complete. Only an Excellent interval (underrun <0.25%
and extrapolation <0.5%) is clean recovery evidence; Healthy (<1%/<2%) and the
intermediate Degraded bands are explicitly non-clean, while Unstable
(>=2%/>=5%) can add a safety step immediately.
The selected presentation delay is stamped alongside accepted input for rewind
plausibility, while the rewind ceiling stays unchanged.

The client slews delay decreases without moving the presented tick backward. Input
playout supports one to three ticks and preserves sequence/edge semantics. The
adaptive timing, frame telemetry (`AdaptiveTimingV2Enabled`), and adaptive input
behavior have independent Node-owned operational flags and default off until
rendered WAN acceptance is available. Disabling V2 fail-closes telemetry
consumption while retaining the older RTT/input policy; it does not make
Protocol 14 peers are not wire-compatible with the Protocol 15 build.

### Protocol-15 UDP authentication boundary

Protocol 17 is the current gameplay wire contract. It preserves protocol 15's timing
telemetry message and explicit optional radial movement axes in each input
command, protocol 16's authoritative Spire alternate-form attack presentation
flag, and adds remote weapon charge presentation state. The UDP envelope has a direction-bound authentication
primitive: a 32-byte per-handoff key protects a 16-byte tag, and the maximum
authenticated payload is bounded by the existing 1,024-byte datagram cap. The
verification order is deliberately opaque verify, body validation, then
connection-state application; invalid packets cannot acknowledge, rebind, or
advance liveness.
Authenticated sequence and keepalive counters are non-wrapping; connections
retire before exhaustion, and their owned key bytes are erased at terminal
retirement. Unsigned discovery replies are ignored during a protected handoff.

Node handoffs carry a public routing `AdmissionId` and a redacted key-bearing
handoff. Node installs the key in the owning Worker before publishing the
handoff, and the Worker retains it only for the bounded admission lease. The
key is never included in `ToString`, diagnostics, tickets, command-line
arguments, or environment values. `UdpAuthenticationEnabled` is the production
switch and is enabled by default; an explicit disabled value is reserved for
legacy/test seams. Enabled mode has no tag-stripping or keyless fallback:
unknown or unauthenticated joins are dropped without a response, and an old
reconnect key cannot consume a new lease. This handoff/install boundary is
control-plane evidence; it must not be reported as public-Internet UDP proof
until the end-to-end packet path is exercised.

### Worker hot path and reliability

- Worker receive drains use caller-provided spans; active matches use a
  copy-on-write read snapshot.
- Outbound datagrams use fixed preallocated slots. Snapshot traffic is
  latest-state-wins per connection; World traffic remains FIFO and
  revision-atomic.
- Weighted traffic classes prevent a reliable burst from indefinitely starving
  snapshots. The physical send occurs outside the mailbox lock.
- A 32-slot physical critical reserve prevents ordinary traffic from consuming
  the complete match mailbox. Critical reserve use/exhaustion, drops, age, and
  high water are observable; critical traffic may evict only snapshot or best-
  effort traffic.
- The Worker owns a 512-attempt global per-pump budget and services matches by
  round robin. Failed sends and keepalives consume the budget, so a failing or
  saturated match cannot monopolize the I/O lane.
- Simulation-lane commands have count, wall-time, and deadline guards. Waiting
  follows the absolute next-tick deadline. The first safe command cannot be
  starved by sub-millisecond scheduler bookkeeping.
- Reliable admission reserves 8 of 32 entries for critical traffic. Overflow
  is explicit. Retransmission uses a bounded 50-500 ms adaptive RTO with retry
  backoff.

The content-backed performance baseline used explicit warm-up, 4,096 samples,
and 128 operations per sample on .NET 10 arm64. The changed steady-state
scenarios reported 0.0 B/op, including:

| Scenario | Mean ns/op | Allocated B/op |
| --- | ---: | ---: |
| `ServerInputStream.ReceiveTake` | 79.2 | 0.0 |
| `ReliableChannel.TryGetDue.MarkSent` | 104.7 | 0.0 |
| `MatchDatagramTransport.EnqueueFlush` | 622.8 | 0.0 |
| `LagCompensationPolicy.ResolveTick` | 14.6 | 0.0 |
| `ServerCombat.CaptureShot` | 146.6 | 0.0 |
| `DynamicCollisionHistory.Record` | 121.2 | 0.0 |
| `SnapshotState.CaptureServerState` | 127.8 | 0.0 |
| `HistoricalCollisionQueryEngine.TryQuery` | 567.7 | 0.0 |
| `WorldStateCapture.Capture` | 1046.5 | 0.0 |

The exact-zero hot-path contract for shot capture is owned by this controlled
performance lane. Its unit test retains fixed-storage correctness assertions,
because the aggregate test runner demonstrated order-dependent runtime/JIT
initialization on the measuring thread.

### Matchmaking

`quickplay.join` is one atomic Node operation. It cannot bypass waitlists or
offered seats and uses deterministic authority-side scoring. The client uses
the legacy list/join retry path only when a Node explicitly reports the new
command as unsupported.

Automatic Node choice probes at most eight eligible public `/health` endpoints
in parallel, with a 750 ms per-probe timeout. Preferred region remains the
primary choice boundary; measured reachability/RTT then precedes population and
the deterministic Node ID tie-break. The endpoint is rate-limited per source.

## P2 result

- Node and Worker use the shared 45-second reconnect policy.
- Rejoin always requests a fresh Node-authenticated Worker handoff; stale Worker
  tickets are not reused.
- Worker completion is relayed by Node as an immutable `match.completion`
  payload before lifecycle end. The client validates match identity and report
  identity and accepts duplicates idempotently. Node does not recalculate game
  results.
- `ClientOnlineRuntime` owns the existing `ClientSessionCoordinator`, current
  Node connection, one-match context, recovery state, and lifetime
  cancellation. `NodeSessions.Current` and `AuthoritativePlay.Current` are thin
  compatibility facades into that owner.
- Rejoin mutation is queued through `MatchClientContext` and executed by the
  gameplay owner during its poll. Completion is epoch-fenced and is not
  published as Connected until the replacement connection, match/role, fresh
  snapshot, current-life `InputEpoch`, and presentation reset are all valid.
- The player-facing state uses smoothed Excellent, Good, Unstable, Poor, and
  Reconnecting states. Raw engineering metrics remain in diagnostics.
- Node completion data provides exact available core post-match facts when the
  UDP terminal result is missing; unavailable detail is omitted rather than
  fabricated.

## Rollback controls

The implementation provides the planned bounded controls:

- Worker: `SnapshotRateHz`, `AdaptiveTimingEnabled`,
  `AdaptiveTimingV2Enabled` (protocol-15 frame telemetry; disabled fail-closed
  when rollback is required),
  `AdaptiveInputPlayoutEnabled`, `TransportQueueV2Enabled`,
  `ReliableAdaptiveRtoEnabled`, and `UdpAuthenticationEnabled` (production
  default enabled; false only through an explicit legacy/test seam).
- Node: `QuickPlayV2Enabled`.
- Client: `PROJECT_PRIME_ONLINE_RUNTIME_V2` (enabled unless explicitly set to
  `false`, `off`, or `0`).

## Validation boundary

Local validation proves compilation, focused protocol/authority behavior,
content-backed Node/Worker lifecycle behavior, deterministic cadence accounting,
and the content-backed performance measurements above. It does not prove Android/Windows
packaging, real geographic WAN feel, long soak
behavior, or live deployed reconnect. Those remain release/operator gates and
must not be inferred from local tests.

P3 remains intentionally deferred: the measurements collected here do not
justify a pose protocol, World replication redesign, multiple reliable streams,
or richer Worker placement classes.

## NetPlay fidelity alignment (2026-09-10)

The follow-on fidelity pass adds measurement and test infrastructure without
changing combat authority:

- Android and desktop now import one Client-owned shared runtime source list.
  The Android compile manifest includes `ClientOnlineRuntime` and the bounded
  presented-collision measurement type; there is no duplicate runtime
  implementation.
- Predicted contacts retain their local headshot classification. Diagnostics
  distinguish headshot agreement, downgrade, promotion, denial, and
  authoritative-only populations while keeping the pre-existing authoritative
  headshot cue counter separate.
- Clamped rewind requests record bounded, identity-fenced requested-versus-
  served position error in world units, including vertical/horizontal and
  fixed per-weapon samples. Missing requested history, missing served history,
  and future timing hints remain separate facts.
- Authenticated rendered-run reports carry clamp position P95/P99/max and
  vertical P95/max, plus interpolation underruns/extrapolated frames and
  prediction correction P95/max. The diagnostic overlay carries at most seven
  dynamic colliders per packet (with its existing truncation flag) so these
  metrics fit without increasing the 1024-byte transport cap.
- The Client stages value-only remote positions while the sampled presentation
  pose is active and publishes them only when that exact frame is committed.
  This is position/continuity measurement, not a complete historical hunter
  collision proxy and not gameplay collision authority.
- `nettest --rendered-wan-validation --scenario headshot` adds an isolated
  Unit1 RM1 developer-fixture scenario with deterministic target choreography,
  Imperialist/zoom/ammo checks, command-sequence shot correlation, scenario
  validity reasons, unique RunId metadata, and exclusive output reservation.
  Invalid setup is classified as `HARNESS INVALID`, not network failure.
- Headshot scenario reports expose independent `ChoreographyValid`,
  `ShotCorrelationValid`, `CombatCoverageValid`, and `HeadshotEvidenceValid`
  gates. Production muzzle/camera convergence is reused, projectile roots bind
  actor plus command sequence, and head geometry comes from authoritative
  metadata. No agreement rate is accepted without actual combat and headshot
  populations.
- A deterministic real-respawn regression proved that an unseen pre-death
  input can cross a death/respawn inside `Playing` when input has no life
  identity. Protocol 12 therefore adds a required `InputEpoch`; the Worker
  rejects stale epochs and installs a neutral current-life combat command at
  the spawn boundary. Protocol 13 introduced the presented-frame denominator;
  protocol 14 adds the authenticated UDP envelope while retaining that timing
  telemetry, and protocol 15 adds explicit quantized radial movement axes.
  Protocol 11/12/13/14 peers are intentionally incompatible with the current
  wire contract.

The evidence gates remain closed. No presented collision proxy is used for
speculative gameplay (F7), no real geographic WAN matrix has been accepted
(F8), `MaxRewindTicks` remains 15 (F9), and production speculative defaults
remain unchanged (F10). The local headshot fixture is explicitly
`rendered-loopback-process-local-impairment`; it cannot satisfy the real-WAN or
human-review gate.

A clean-link rendered loopback run on 2026-09-10 passed the scenario-validity
gate with six trigger attempts, six local Imperialist roots, six authoritative
roots, and six command-correlated roots. Both vertical/strafe and close/long
target observations were present, and the same-session reconnect retained the
match and seat while rotating the connection identity. The run produced no
predicted or authoritative hit population, so it is harness-readiness evidence
only; it is not headshot-agreement evidence and does not authorize F7.

## N5/N6 acceptance tooling

The N5 snapshot-rate tool is deliberately a planner and fail-closed decision
report, not a synthetic acceptance result. `nettest
--rendered-wan-snapshot-matrix-plan` reserves a fresh output directory and
creates paired 30/60 Hz cells. Until every cell is complete and explicitly
marked `real-wan-independent-path`, the decision is
`INSUFFICIENT_REAL_WAN_EVIDENCE`, with no recommendation to change the 30 Hz
production default.

The N6 two-client tool validates separately authenticated shooter/target
reports, run/match/Worker binding, role and shot identity uniqueness, and the
absence of developer-fixture claims. The merge cannot publish headshot
agreement without actual headshot evidence; it emits no `renderedWanProof`
claim for local fixtures, missing evidence, or mismatched reports. These
checks make local loopback and process-local impairment useful harness evidence
without promoting them to geographic WAN or human-review acceptance.

## N12 NetPlay hardening and optimization (2026-09-11)

N12's locally actionable hardening was implemented on the Protocol 15
baseline. N12 did not add or revise a wire message. References to Protocol 14 in
the N12 plan describe its target baseline; the intervening radial-input work
made Protocol 15 its repository contract, the later Spire presentation fix
advanced the live contract to Protocol 16, and remote weapon charge
presentation advanced it to Protocol 17.

### Transport, ingress, and timing

- Signed datagrams carry an explicit semantic delivery class. One canonical
  reliable-event policy maps critical, normal, state, world, and best-effort
  traffic; malformed `Auto` submissions are dropped instead of being
  reclassified from authenticated bytes. Saturation tests exercise actual
  signed datagrams and the critical reserve.
- Worker ingress has bounded pre-authentication, admission, and established
  route limits. Established connections have a 240 datagram/second rate with a
  360-datagram burst and a 64-datagram queued-work cap. Per-class aggregate
  drops and maximum connection depth are observable without high-cardinality
  heartbeat state.
- Admission leases use monotonic deadlines. Expiry is purged once per pump,
  not scanned per packet, and retired key material is zeroed.
- Accepted client timing telemetry records freshness. A downshift requires at
  least 30 presented samples and telemetry no older than 2.5 seconds. Missing
  or stale telemetry blocks only a safety downshift; it cannot suppress an
  upshift.
- The Worker network lane uses an event signal plus absolute deadlines instead
  of 1 ms polling. Publish-before-signal ordering, bounded per-pump work,
  round-robin match service, delayed-held-datagram wakeups, and immediate
  repumps preserve latency without unbounded work. Wake, idle, immediate
  repump, receive-to-route age, and outbound age are measured.
- `NetClock` retains a bounded 16-sample low-RTT/MAD-filtered window, uses
  bounded slew, preserves monotonic presentation, detects persistent latency
  regime changes, and rejects transient outliers after a new regime is
  established.
- Per-connection quotas are the first-stage inbound fairness mechanism. A
  deterministic abusive-peer-versus-healthy-peer test passes. A second input
  coalescing queue was not added because the measured bounded design does not
  justify the extra ownership surface.

### ACK coalescing

ACK coalescing is implemented behind an opt-in flag and remains disabled by
default. Pending ACK state uses monotonic/tick deadlines, duplicate reliable
events renew acknowledgement pressure without redelivery or liveness/rebind
mutation, eligible outbound traffic piggybacks the ACK, and the deadline falls
back to a standalone ACK. Disconnect and terminal failure perform a final
flush attempt.

Only a non-evictable reliable/world queue acceptance or a successful direct
socket send counts as an accepted ACK carrier. Evictable state, best-effort,
and held-impairment submissions cannot clear pending ACK state. Metrics
separate carrier submission attempts from accepted standalone and piggyback
emissions. The Client opt-in is `PROJECT_PRIME_ACK_COALESCING`; Worker and Node
placement use `--ack-coalescing`.

### Allocation and capacity evidence

The 60-second real-loopback UDP receive measurement is
`/tmp/prime-n12-udp-measurement-60s.json`. All 1/8/16/32-player cells emitted
every scheduled interval and reported zero receive/drain mismatch, queue drop,
transport rejection, send error, or skipped interval. The 32-player cell
received 2,302.97 packets/second, allocated 680,056 managed bytes/second
process-wide, performed four Gen0 and zero Gen1/Gen2 collections, spent 0.881 ms
in GC pause (0.00147% of wall time), used 0.356% process CPU, and reached queue
high-water 96. These are localhost transport measurements, not deployed-host
or WAN capacity claims.

The measured receive pressure did not justify introducing leased pooled packet
ownership. The latest reliable-path self-test reports `TryEnqueue` 1,369.2 ns/32 B,
encode 21.7 ns/0 B, event queue 3,022.2 ns/392 B, and combat event 2,876.1
ns/176 B. Those bounded allocations did not justify a payload slab or a more
complex client event store. N12.7 and the optional parts of N12.8 were therefore
correctly skipped by their evidence gates.

Authenticated content-backed capacity runs passed at 1 match/8 peers, 2/16,
and 4/32. The 4/32 run completed 9,600 playing ticks, processed 70,560 inputs
and 20,173 snapshots, drained all four matches, ingested all four reports, and
reported zero queue drops or packet rejections. Maximum ingress depth was 12,
maximum deadline misses were 1, network-pump P99 was 0.6994 ms, P99.9 was
1.1873 ms, receive-to-route P99 was 1.6060 ms, and outbound-age P99 was 0.778
ms. The soak uses fresh Node-installed admission identity and key material for
every initial connection and reconnect; it has no keyless fallback.

### Fidelity and release boundary

The latest 60-second local headshot run is recorded at
`/tmp/prime-n12-headshot-local-20260911-r5/report.json`. Choreography passed,
including vertical/strafe, close/long, legal Imperialist cadence, stationary
body/head calibration, and same-session/same-match/same-seat reconnect with a
rotated connection identity. It correlated 29 of 29 local and authoritative
root shots, but missed the minimum by one shot and produced only two predicted
contacts and one authoritative headshot. A target-lifecycle repair moved
ordinary respawn processing ahead of the validation `ModInPlay` gate, but this
rerun still produced no post-reconnect hit population. The report is correctly `HARNESS
INVALID`; thresholds were not lowered and no agreement rate was published.
Stationary body/head evidence is now life-fenced and phase-exclusive; a
long-range overlap regression prevents one aim frame from satisfying both
calibration populations. That final accounting guard passed its focused
self-test after r5; no later local run is being promoted as acceptance evidence.
Later diagnostic captures show ordinary scripted target motion entering
occluded room geometry. A bounded choreography revision that preserves genuine
movement without repeated Worker-side repositioning is still required before
the local combat-population gate can pass.

The repository contains fail-closed self-tested tooling for the real UDP
baseline, snapshot matrix, and independent two-client report merge. This local
machine cannot supply independent geographic US, Europe, and APAC paths or a
human-reviewed two-client population. Consequently the N12 Definition of Done
remains externally gated on:

- sufficient local and independent two-client combat/headshot populations;
- real geographic WAN validation;
- paired 30 Hz versus 60 Hz rendered-WAN cells; and
- controlled WAN evidence for each production-default graduation.

The evidence does not authorize gameplay use of presented collision,
prediction enrichment, or a larger rewind window. `MaxRewindTicks` remains 15,
snapshot cadence remains 30 Hz by default, and adaptive timing V1/V2, adaptive
input playout, Queue V2, adaptive RTO, and ACK coalescing remain disabled by
default. UDP authentication, the critical reserve, and the Worker global
network budget remain enabled.

Post-implementation validation on the shared dirty worktree produced a clean
Release solution build, 135/135 focused N12 tests, all four WAN-tool self-tests,
the seven-case headshot scenario self-test, 172/172 Python tool tests, a clean
project-boundary check, and a clean diff whitespace check. The broad game suite
ran 2,625/2,628; its three failures are in concurrent map-platform work
(`MapAcquisitionServiceTests` and two `ServerContentValidationTests`). The
content-backed Node suite ran 207/208; its one failure is the concurrent A24
admission test using an exactly-30-second future issue time while the shared
contract currently permits 30 seconds of skew. These failures are outside N12,
were not hidden or changed here, and mean the entire dirty worktree is not being
reported globally green.
After that clean solution build, later concurrent UI/replay edits introduced
three Client compile errors in `SdlGameHost`/`KillcamController`; the final
Worker and nettest sources were therefore rebuilt independently against their
already-built references and passed with zero warnings or errors.
