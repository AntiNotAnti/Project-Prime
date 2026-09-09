# QZ3 match semantic events, awards, and presentation

This document records the Prime-owned invariants implemented for QZ3-A through
QZ3-D. The implementation is intentionally content-free at the semantic
boundary: authoritative simulation supplies numeric facts, and presentation
consumers choose local text or audio.

## Authoritative ownership

Each `MatchRuntime` owns one synchronous `MatchEventDispatcher`. Combat kills
and spawns enter it from `ServerCombat`, objective and prime transitions enter
it from `ServerWorldEvents`, and lifecycle transitions enter it from
`MatchLifecycle`. Producers always pass `Id = 0`; the dispatcher assigns the
monotonic match-owned semantic ID and returns the normalized fact. A round reset
clears consumer state but does not reuse semantic or award IDs. Consequently,
independent matches cannot deduplicate each other's facts, and delayed facts
from an earlier round cannot collide with a later fact.

`MatchEvent` carries full `(slot, connection, life)` actor identity, match ID,
phase revision, server tick, team, and bounded flags. It is not a snapshot and
does not mutate gameplay state.

## Award definitions

`AwardEngine` consumes only authoritative semantic facts and emits exactly the
eight initial Prime awards: `FirstHunt`, `DoubleKill`, `TripleKill`,
`Interceptor`, `Defender`, `PrimeSlayer`, `Capture`, and `Assist`.

- First/double/triple kills use simulation ticks only (180 ticks at 60 Hz),
  with wrap-safe age checks. Spawn, death, round, suicide, team-kill, and
  environment-kill boundaries reset the relevant streak state.
- Interceptor is emitted only when the source kill was recorded against the
  pre-mutation objective carrier. PrimeSlayer uses the pre-mutation prime flag.
- Defender is emitted only from the authoritative Defender/TeamDefender source
  condition: competitive opposing killer and victim are both inside the same
  `NodeDefenseEntity.Volume` before victim mutation.
- Assist uses the assisting actor as the subject. A bot flag describes that
  assisting actor, not the killer. No award changes score, Ranking Points, or
  rating.

## Wire, clients, replay, and telemetry

Protocol 9 `MatchSemanticEventPacket` and `MatchAwardPacket` are generated from
bounded numeric fields. They have no strings and are carried as distinct
reliable event types; combined semantic flag masks are encoded as a bounded
`ushort` rather than validated as a single enum member. `MatchInstance` owns
one fixed 256-entry semantic sink and drains each normalized fact to the
existing reliable event, observer/replay, and telemetry paths. Queue overflow
is counted and never blocks the authoritative simulation. Low-level combat and
world events remain intact.

`AnnouncerService` and `AwardHudQueue` each maintain bounded deduplication and priority queues;
TripleKill supersedes a queued DoubleKill for the same subject. Announcer
cooldown uses wrap-safe tick age, built-in assets are always available, and a
validated QZ6 `OptionalPresentationManifest` may replace individual local
asset mappings without affecting gameplay identity. The HUD has an actual
bounded draw path in `PlayerPresentation`, not just a queue. The renderer
drains at most one announcer cue per presentation pass. Optional files are
resolved through the integrity-checked QZ6 resolver and played by one
replaceable, streaming SoundFlow player attached to the existing audio device;
resolution, decode, or device failure falls back to a built-in cue. No audio
result feeds gameplay state.

Replay records the raw normalized semantic and award packets. General match
facts generate their corresponding objective/lifecycle markers, while
`DoubleKill` and `TripleKill` generate `MultiKill` only from authoritative
`MatchAward` facts. Consecutive kill timing never creates a replay multi-kill
marker, and a capture award does not repeat its objective marker. Protocol 8
retains low-level `WorldEvent` and terminal-world match-end inference; Protocol
9 uses the normalized semantic fact instead. The
`SemanticAwardJournal` provides a bounded newest-retaining history, revision
delivery, checkpoint serialization, and deduplication; the client and server
replay consumers reset/replay raw facts for seek boundaries. Playback never
derives a new award from an old kill or snapshot under current rules.
Telemetry records bounded numeric event/award kind, semantic identity, entity,
actor, team, and flag facts without adding spatial samples at a fabricated
origin.

## Deterministic regression evidence

The focused QZ3 corpus covers:

- `tests/Tests/Game/MatchAwardsTests.cs` covers dispatcher normalization and
  global IDs, first/double/triple boundaries, tick wrap, spawn/death/round and
  slot/life reset, suicide/team/environment policy, carrier/defender/prime
  facts, bot and assist identity, capture, generated semantic and award wire
  validation (including combined flags),
  replay markers/raw-fact dedup/checkpoints including newest-retaining order,
  dropped-fact metrics, and two independent `MatchRuntime` instances.
- `tests/Tests/Client/MatchAwardPresentationTests.cs` covers reliable duplicate
  suppression, HUD supersession and capacity, wrap-safe announcer cooldown,
  deterministic priority, QZ6 manifest mapping/fallback including the maximum
  stable ID, production cue dequeue, optional decoder fallback,
  newest-retaining replay presentation, recorded general-semantic playback,
  and winner-aware victory/defeat selection.
- `tests/Tests/Telemetry/Qz3AwardTelemetryTests.cs` covers all eight bounded
  award counters, full-buffer drops, normalized semantic identity, and raw
  award/semantic command reading without contaminating spatial aggregates.
- `tests/Tests/MultiInstance/MatchInstanceTests.cs` covers production draining
  from the match-owned semantic sink into telemetry.

Each test uses fixed ticks, identities, IDs, and packet bytes; no foreign
binary, live client, network, or external content dependency is required.
