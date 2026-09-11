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

Protocol 10 carries versioned `NetworkTimingProfile` updates and bounded client
timing telemetry. The Worker is the policy owner. Profiles move one adjacent
step at a time, increase safety quickly, require an eight-second clean dwell to
reduce buffering, and fall back safely if acknowledgement does not complete.
The selected presentation delay is stamped alongside accepted input for rewind
plausibility, while the rewind ceiling stays unchanged.

The client slews delay decreases without moving the presented tick backward. Input
playout supports one to three ticks and preserves sequence/edge semantics. The
adaptive timing and adaptive input behavior have independent Node-owned
operational flags and default off until rendered WAN acceptance is available.

### Worker hot path and reliability

- Worker receive drains use caller-provided spans; active matches use a
  copy-on-write read snapshot.
- Outbound datagrams use fixed preallocated slots. Snapshot traffic is
  latest-state-wins per connection; World traffic remains FIFO and
  revision-atomic.
- Weighted traffic classes prevent a reliable burst from indefinitely starving
  snapshots. The physical send occurs outside the mailbox lock.
- Simulation-lane commands have count, wall-time, and deadline guards. Waiting
  follows the absolute next-tick deadline. The first safe command cannot be
  starved by sub-millisecond scheduler bookkeeping.
- Reliable admission reserves 8 of 32 entries for critical traffic. Overflow
  is explicit. Retransmission uses a bounded 50-500 ms adaptive RTO with retry
  backoff.

The content-free performance baseline used explicit warm-up, 4,096 samples,
and 128 operations per sample on .NET 10 arm64. The changed steady-state
scenarios reported 0.0 B/op, including:

| Scenario | Mean ns/op | Allocated B/op |
| --- | ---: | ---: |
| `ServerInputStream.ReceiveTake` | 74.7 | 0.0 |
| `ReliableChannel.TryGetDue.MarkSent` | 109.4 | 0.0 |
| `MatchDatagramTransport.EnqueueFlush` | 774.8 | 0.0 |
| `LagCompensationPolicy.ResolveTick` | 16.8 | 0.0 |

Content-backed capture scenarios remain unmeasured because extracted AMHE1 was
not available in this environment.

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
- The player-facing state uses smoothed Excellent, Good, Unstable, Poor, and
  Reconnecting states. Raw engineering metrics remain in diagnostics.
- Node completion data provides exact available core post-match facts when the
  UDP terminal result is missing; unavailable detail is omitted rather than
  fabricated.

## Rollback controls

The implementation provides the planned bounded controls:

- Worker: `SnapshotRateHz`, `AdaptiveTimingEnabled`,
  `AdaptiveInputPlayoutEnabled`, `TransportQueueV2Enabled`, and
  `ReliableAdaptiveRtoEnabled`.
- Node: `QuickPlayV2Enabled`.
- Client: `PROJECT_PRIME_ONLINE_RUNTIME_V2` (enabled unless explicitly set to
  `false`, `off`, or `0`).

## Validation boundary

Local validation proves compilation, focused protocol/authority behavior,
content-free Node/Shared behavior, deterministic cadence accounting, and the
content-free performance measurements above. It does not prove Android/Windows
packaging, extracted-content scenarios, real geographic WAN feel, long soak
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
- A deterministic real-respawn regression proved that an unseen pre-death
  input can cross a death/respawn inside `Playing` when input has no life
  identity. Protocol 12 therefore adds a required `InputEpoch`; the Worker
  rejects stale epochs and installs a neutral current-life combat command at
  the spawn boundary. Protocol 11 peers are intentionally incompatible.

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
