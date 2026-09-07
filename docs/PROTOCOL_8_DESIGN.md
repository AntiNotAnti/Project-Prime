# Protocol 8 proposal (read-only G1–G3 audit)

Status: UNRELEASED, EVOLVING. G1.4 implements NetHeader8 and player96 (burn/disruption/assists=0); coordinated G1.7 implements rules84 with SpawnPolicy@68 and cancel-on-offensive-action bit0@72. Other rule extension bytes are reserved zero. Kill/world events,26-record prefix and G4/G5 identity extensions below remain FUTURE PROPOSALS, not current wire behavior. Read the G1–G5 plan's sections 3, G1.4, G1.7, G2.1–G2.5, G3.1–G3.4, G4.4 and G5.3. Live accepts version 8; historical versions are confined to demo adapters. Keep protocol 8 unpublished until its G1–G3 schema and golden tests are complete; do not increment repeatedly during development. No claim of WAN or GUI verification.

## Confirmed wire baseline

All integers little endian; vectors are three finite IEEE float32 values. Existing NetHeader is 24 bytes; NetConfig caps datagrams at 1024 bytes. ReliableChannel holds 32 events with maximum 512-byte payload, so enlarging a batched payload must respect that tighter cap.

| Contract/source under src/Game/Protocol | Current bytes and important offsets |
|---|---|
| NetHeader.cs | 24; Version=7; message IDs Join1, Accepted2, Input3, Snapshot4, Event5, KeepAlive6, Ping7, Pong8, Refused9, Ack10, World12 |
| SnapshotPacket.cs | player88; packet header26; eight-player payload730, datagram754 |
| SnapshotPlayer | slot0, hunter1, team2, weapon3; flags u16@4; health6/ammo8/10; life u32@12; connection u64@16; position24/speed36/aim48/facing60; points i32@72/kills76/deaths80; weapons u16@84; frozen u16@86 |
| CombatEvent.cs | CombatActor13 = slot u8, connection u64, life u32; event82: id0/tick4/command8/kind12/weapon13/flags14/actor16/target29/health42/amount44/position46/direction58/frozen70/burn72/disrupt74/charge76/spread78. Batch count1 + six events =493, below512 |
| MatchRulesWire.cs | 68: mode0/capacity1/flags2/damage3; score i32@4/lives8; TimeSpan ticks i64@12/20, -1 unlimited; ASCII room40@28 |
| JoinPacket.cs | Join34: version0/nonce u64@1/hunter9/name16@10/prior connection u64@26; accepted86 = fixed18 + rules68 |
| MatchTransitionPacket.cs | 76 = match u32@0/tick u32@4 + rules68 |
| RosterChatPackets.cs | header5 + up to8 entries29; entry slot0/connection u64@1/hunter9/team10/name16@11/ping u16@27 |
| InputCommand.cs | command33; bundle header9 (count plus match and phase revision), eight commands273; keep live per-tick input semantics |
| WorldPacket.cs | header20 + up to24 records40 =980 payload,1004 datagram; capacity256. Record kind0/slot1/flags u16@2/id u32@4/position8/A20/B24/C28/D32/E36 |

Current canonical world prefix is Match + eight Score/Time pairs + Lifecycle (18 records). Score A–E are already points,kills,deaths,teamPoints,teamKills; Time A–E already teamDeaths,playerTime,teamTime,nodes,octolithScores. Do not overwrite a supposedly unused score field. Match.E and Lifecycle.D/E are currently zero and can acquire deliberate v8 meanings.

Current team assignment is confirmed slot parity at ServerNetwork admission and rotation (TeamIndex setters), matching the plan; do not assume the earlier refactor already implemented G3 balancing.

## Proposed coherent G1–G3 layout

### Durable player state: player96 (append-only)

Keep bytes0–87, append BurnTicks u16@88, DisruptTicks u16@90, Assists nonnegative i32@92. Eight-player packet becomes794 bytes,818 with NetHeader: safely below1024. Keep current status flag IDs. Writer emits consistent flag/timer pairs; decoder rejects contradictory v8 status pairs, invalid identities/enum/masks/nonfinite vectors before exposing output. Timer setters must only reconcile presentation, never run damage.

A snapshot's server tick anchors remaining durations; late reliable Affliction events must not rewind a newer snapshot's status. Client feedback deduplicates event IDs, while durable snapshot state recovers dropped cues. Do not manufacture missing historical burn duration for old demos.

### Combat facts and assists

Keep existing CombatEvent82 unchanged, including six-event batches. Adding assist identities to this record would overflow the current 512-byte six-event batch and burden every Shot/Damage event.

Add ReliableEventType.Kill=11 with dedicated fixed137-byte KillEvent:

| Offset | Field |
|---|---|
| 0,4,8,12 | eventId, serverTick, matchId, phaseRevision (u32 each) |
| 16,29 | killer CombatActor13, victim CombatActor13 |
| 42,43 | weapon u8, kill flags u8 (explicit mask) |
| 44,45 | assistCount u8 (0..7), reserved u8=0 |
| 46 | seven CombatActor13 entries (91 bytes); unused entries canonical None |

Seven storage entries cover every non-victim participant; a valid distinct killer leaves at most six eligible assists. Whether environmental deaths award assists is an explicit gameplay policy. Actor uniqueness and killer/victim exclusion are mandatory. Killer may be None for environment; victim must be valid; assists must match eligible identities from the server ledger, never client claims. Nonnegative assists also flow into immutable result/player stats and snapshots. Keep CombatEvent.Death for existing victim state/cues; only KillEvent drives global kill feed/assist notifications to avoid two death announcements. Use one shared server presentation event sequence if practical; otherwise dedup key includes event family.

### Full rule block: rules84 (append16)

Preserve rules0–67 and append:

| Offset | Field |
|---|---|
| 68 | SpawnPolicy u8: Classic/Enhanced/Duel (only implemented values accepted) |
| 69 | OvertimePolicy u8: explicit validated enum, Classic default preserves regulation end behavior |
| 70 | LateJoinPolicy u8: JoinImmediately/SpectateUntilNextMatch/Disabled |
| 71 | TeamBalancePolicy u8: deterministic count balance / optional casual trailing-score tie-break |
| 72 | policy flags u16: bit0 CancelSpawnProtectionOnOffensiveAction; remaining bits zero |
| 74 | AssistMinimumDamage u16 |
| 76 | AssistWindowTicks u16 (default300 if agreed gameplay policy) |
| 78 | reconnectGraceTicks u16 (0 disables pre-G4 session grace) |
| 80 | reserved u32=0, no semantics accepted until explicitly implemented |

Accepted becomes102; transition92. Policies must round-trip immutable MatchRules and default to Classic-compatible values in demo adapters. Concrete overtime enum values/assist thresholds need gameplay-owner agreement; this proposal does not silently pick balance numbers. Saturation is forbidden: reject values outside representable policy ranges before constructing rules.

### Match period and world recovery

Keep MatchPhase.Playing for overtime. Encode Match.E as MatchPeriod (0 Regulation,1 Overtime,2 SuddenDeath). Use Lifecycle.D/E for periodStartTick/periodEndTick; Lifecycle flags bit1 means period deadline exists (bit0 remains phase deadline). A period change must not reset gameplay input PhaseRevision as though countdown restarted. World revision/tick ordering makes period recovery atomic.

Append new WorldRecordKind.PlayerStats=9, one per slot, with A=nonnegative assists and B–E/Position/Id/flags zero. Canonical prefix becomes26 records, so even an entity-empty world spans two batches. This is a deliberate bandwidth tradeoff to retain exact integer stats and preserve current score semantics; snapshot assists remain fast recovery. Validate the complete26-record prefix before mutation and preserve the existing 256-record total budget. If avoiding eight records matters, choose a separate packed stats packet after measurement rather than encoding integers in floats.

### Semantic world-event immediacy

Add ReliableEventType.WorldEvent=12, distinct from existing World=7 recovery records. Fixed64-byte event:

- id u32@0, tick@4, matchId@8, phaseRevision@12;
- entity kind u8@16, semantic kind u8@17, team u8@18 (255 None), reserved0@19;
- entity/objective id u32@20;
- actor CombatActor13@24;
- reserved bytes37–39 zero;
- position Vector3@40;
- event-specific uint A@52/B@56/C@60.

Kinds: PickupConsumed/Respawned, FlagPickedUp/Dropped/Reset/Captured, NodeCaptured/Contested, PrimeChanged, DefenderStateChanged. Validate kind-specific actor/id/team/payload invariants, not merely global numeric bounds. Stable IDs come from existing authoritative spawner/item/objective identity; no pointer or client-created ID. Dynamic IDs must not be reused within one match/reset epoch. PhaseRevision fences pre-countdown-reset events. Full world revision remains recovery; these events drive immediate presentation only, never award another pickup/score on clients.

Game publishes semantic callbacks through ISceneServices/ICombatAuthority-owned Game values; Server translates callbacks to wire records. All event admission is bounded and explicit under ReliableChannel backpressure; do not silently lose a terminal kill/capture. Batch4 world events=257 if count-prefixed; kill events can use one per reliable payload initially (137), avoiding needless variable framing.

## Demo boundary (must precede live version flip)

Demo file format2 already records protocol byte; no file-container bump is necessary for these changes. DemoFile.IsAuthoritativeProtocol currently enumerates5/6/7: extend to8. LegacyDemoState currently sends every snapshot through live SnapshotPacket.TryRead, every roster through live SessionRosterPacket, and protocol>=7 transitions through live MatchTransitionPacket: all three are migration hazards.

Create an explicit client-only Protocol7DemoCodec with frozen player88/rules68/transition76/accepted86/roster29 and world18-prefix layouts. Versions5/6 already have legacy49-byte transitions and17-prefix world paths; retain those separately. For protocol7 snapshot adaptation use burn/disrupt unknown/zero duration without claiming precision; recorded Affliction events still provide immediate cues. Classic rule defaults, assists0, MatchPeriod.Regulation. Do not weaken v8 live validators to accept old sizes or version7 headers. Audit DemoPlayback/LegacyDemoState/NetSession/ClientWorldState and DemoInfo/recording paths so every version-dependent decoder is explicitly selected by recorded protocol. New Kill/WorldEvent demo records must be recorded after server validation and replayed through the same deduplicating feedback paths.

## G4/G5 capacity without prematurely shipping fake identity

Do not equate an actor's stable occupancy identity with an authenticated user. Keep the existing 13-byte session/life combat key through G1–G3; later clarify the u64 as participant-session identity when bots have no NetConnection. A server-generated bot identity must be nonzero/unique and must not grant a socket admission. Persistent PlayerId belongs in separately authenticated roster metadata, not in every projectile/event.

Before v8 release, if G4/G5 implementation lands: append a validated roster flags byte (Bot/Observer), optional authenticated PlayerId16 in a bounded new identity roster event; separate spectator connection capacity from the fixed8 competitive slots. Observer slot255 needs explicit session identity and must never index player arrays; do not simply loosen slot<8 validators. A bounded ticket-bearing Join extension should have length u16 and at most384 ticket bytes (old34+2+384=420 payload), verified before admission; never log tickets or accept client-asserted PlayerId. Add server identity/expiry/nonce/signature/replay validation at the server auth boundary. These are reserved design directions, not enabled v8 features or arbitrary zero-filled permanent fields. If G4 follows releasedv8, use protocol9 as the plan permits.

## Ownership and tests

Game codecs/contracts: SnapshotPacket, MatchRulesWire, JoinPacket, MatchTransitionPacket, RosterChatPackets if G4/G5 lands, WorldPacket, ReliableChannel enum; new KillEvent and WorldEvent. Game match/result and services gain data/policy only. Server: ServerNetwork admission/event queues, ServerCombat ledger/events, ServerSimulation state capture/reset, WorldStateCapture, TeamAllocator/latejoin. Client: NetClient reliable dispatch, ClientWorldState atomic recovery, AuthoritativePlay feedback pipeline, LegacyDemoState/Protocol7DemoCodec, DemoFile/recording.

Required focused tests: byte-offset golden vectors for old7/new8; exact/truncated/extra lengths; every invalid enum/mask/NaN/Infinity/identity; no partially mutated batch; max8 snapshot818 datagram; max reliable payload<=512; old7 live rejection with demo acceptance; omitted/duplicate/reordered affliction vs newer snapshot; expiry and join-midstatus without client damage; maximum-assist uniqueness/reconnect/life replacement; Kill vs Death dedup; WorldEvent replay/reset epoch and queue-full policy; overtime remains Playing/input uninterrupted;26-prefix multi-batch world assembly and capacity boundaries; old4/5/6/7 demo fixtures unchanged; two-client authoritative8 loopback and existing WAN suite separately. Do not infer GUI readability or authenticated-backend correctness from codec tests.
