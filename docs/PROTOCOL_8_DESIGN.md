# Protocol 8 proposal (read-only G1–G3 audit)

> **HISTORICAL DESIGN DOCUMENT — SUPERSEDED.** This proposal describes an
> earlier unreleased protocol-8 baseline and is not the live wire contract.
> Use [CURRENT_ARCHITECTURE.md](CURRENT_ARCHITECTURE.md) and
> [CURRENT_PROTOCOL.md](CURRENT_PROTOCOL.md) for current ownership and protocol
> behavior; source and the release ledger take precedence over this document.

Status: HISTORICAL, SUPERSEDED. The protocol-8 layout and the statements below that it accepted live version8 are retained as design history only. The current live authoritative protocol is 15; historical versions remain confined to frozen replay adapters. Runtime/GUI/WAN evidence is separate from codec and focused-test evidence.

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
| InputCommand.cs | protocol8 baseline command33; live protocol11 command36 appends boost activation/signed X/signed Y, bundle header9 and eight commands297; keep live per-tick input semantics |
| WorldPacket.cs | header20 + up to24 records40 =980 payload,1004 datagram; capacity256. Record kind0/slot1/flags u16@2/id u32@4/position8/A20/B24/C28/D32/E36 |

Current canonical world prefix is Match + eight Score/Time pairs + Lifecycle (18 records). Score A–E are already points,kills,deaths,teamPoints,teamKills; Time A–E already teamDeaths,playerTime,teamTime,nodes,octolithScores. Do not overwrite a supposedly unused score field. Match.E and Lifecycle.D/E are currently zero and can acquire deliberate v8 meanings.

Current team assignment is confirmed slot parity at ServerNetwork admission and rotation (TeamIndex setters), matching the plan; do not assume the earlier refactor already implemented G3 balancing.

## Proposed coherent G1–G3 layout

### Durable player state: player96 (append-only)

Keep bytes0–87, append BurnTicks u16@88, DisruptTicks u16@90, Assists nonnegative i32@92. RadarReveal/Previous flags are2048/4096; WaitingForMatch is8192. Eight-player packet becomes794 bytes,818 with NetHeader: safely below1024. Keep current status flag IDs. Writer emits consistent flag/timer pairs; decoder rejects contradictory v8 status pairs, invalid identities/enum/masks/nonfinite vectors before exposing output. Timer setters must only reconcile presentation, never run damage.

A snapshot's server tick anchors remaining durations; late reliable Affliction events must not rewind a newer snapshot's status. Client feedback deduplicates event IDs, while durable snapshot state recovers dropped cues. Do not manufacture missing historical burn duration for old replays.

### Combat facts and assists

Keep existing CombatEvent82 unchanged, including six-event batches. Adding assist identities to this record would overflow the current 512-byte six-event batch and burden every Shot/Damage event.

Add ReliableEventType.Kill=11 with dedicated fixed137-byte KillEvent:

| Offset | Field |
|---|---|
| 0,4,8,12 | eventId, serverTick, matchId, phaseRevision (u32 each) |
| 16,29 | killer CombatActor13, victim CombatActor13 |
| 42,43 | weapon u8, kill flags u8 (explicit mask) |
| 44,45 | assistCount u8 (0..7), KillSourceKind u8 (Beam0/Bomb1/Alt2/Environment3) |
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

Accepted becomes102; transition92. Policies must round-trip immutable MatchRules and default to Classic-compatible values in replay adapters. Concrete overtime enum values/assist thresholds need gameplay-owner agreement; this proposal does not silently pick balance numbers. Saturation is forbidden: reject values outside representable policy ranges before constructing rules.

### Match period and world recovery

Keep MatchPhase.Playing for overtime. Lifecycle.D carries MatchPeriod (Regulation0/Overtime1/SuddenDeath2), E carries periodStartTick. Overtime is unlimited and clears the phase deadline; no new phase or PhaseRevision is created. Match.E carries terminal endReason+1 (zero before result capture), and Match.Position.Z carries completion simulation time. World revision/tick ordering makes period recovery atomic.

The canonical prefix is58 records: original18, then five records per slot. CombatStats9 carries assists/actualDamageDealt/headshots/longestStreak/killsAsPrime. ObjectiveStats10 carries scores/drops/stops/nodesCaptured/nodesLost. WeaponStats0(11) carries beam kills0–4; WeaponStats1(12) carries beam kills5–8 and Prime eliminations. Each statistic is a nonnegative32-bit integer. PlayerIdentity13 has a dedicated codec: bytes4–19 hold canonicalASCII16 nickname (not a vector); A=Hunter, B=team orUInt32.MaxValue, C=active, D=standing/teamStanding/resultSlot packed into three bytes, E=0. Identity records expose typed PlayerName and do not reinterpret names as floating point.

All58 records and active result-slot permutation are validated before mutation. Terminal capture reads the server's immutable MatchResult even after live player slots change. The client freezes its result once from a complete terminal baseline and authoritative identity metadata. Other unused legacy efficiency/count fields are not advertised as replicated statistics. Capacity stays256; content validation budgets58 plus two records per static item spawner and one per flag/node. Dynamic-drop overflow remains explicit. Frozen protocol7 world18 and protocol5/6 world17 paths remain replay-only.

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

## Replay boundary (must precede live version flip)

Replay file format2 already records protocol byte; no file-container bump is necessary for these changes. ReplayFile.IsAuthoritativeProtocol currently enumerates5/6/7: extend to8. LegacyReplayState currently sends every snapshot through live SnapshotPacket.TryRead, every roster through live SessionRosterPacket, and protocol>=7 transitions through live MatchTransitionPacket: all three are migration hazards.

Create an explicit client-only Protocol7ReplayCodec with frozen player88/rules68/transition76/accepted86/roster29 and world18-prefix layouts. Versions5/6 already have legacy49-byte transitions and17-prefix world paths; retain those separately. For protocol7 snapshot adaptation use burn/disrupt unknown/zero duration without claiming precision; recorded Affliction events still provide immediate cues. Classic rule defaults, assists0, MatchPeriod.Regulation. Do not weaken v8 live validators to accept old sizes or version7 headers. Audit ReplayPlayback/LegacyReplayState/NetSession/ClientWorldState and ReplayInfo/recording paths so every version-dependent decoder is explicitly selected by recorded protocol. New Kill/WorldEvent replay records must be recorded after server validation and replayed through the same deduplicating feedback paths.

## G4/G5 capacity without prematurely shipping fake identity

Do not equate an actor's stable occupancy identity with an authenticated user. Keep the existing 13-byte session/life combat key through G1–G3; later clarify the u64 as participant-session identity when bots have no NetConnection. A server-generated bot identity must be nonzero/unique and must not grant a socket admission. Persistent PlayerId belongs in separately authenticated roster metadata, not in every projectile/event.

Superseded early G4/G5 design sketch (see implemented updates below): append a validated roster flags byte (Bot/Observer), optional authenticated PlayerId16 in a bounded new identity roster event; separate spectator connection capacity from the fixed8 competitive slots. Observer slot255 needs explicit session identity and must never index player arrays; do not simply loosen slot<8 validators. A bounded ticket-bearing Join extension should have length u16 and at most384 ticket bytes (old34+2+384=420 payload), verified before admission; never log tickets or accept client-asserted PlayerId. Add server identity/expiry/nonce/signature/replay validation at the server auth boundary. These are reserved design directions, not enabled v8 features or arbitrary zero-filled permanent fields. If G4 follows releasedv8, use protocol9 as the plan permits.

## Ownership and tests

Game codecs/contracts: SnapshotPacket, MatchRulesWire, JoinPacket, MatchTransitionPacket, RosterChatPackets if G4/G5 lands, WorldPacket, ReliableChannel enum; new KillEvent and WorldEvent. Game match/result and services gain data/policy only. Server: ServerNetwork admission/event queues, ServerCombat ledger/events, ServerSimulation state capture/reset, WorldStateCapture, TeamAllocator/latejoin. Client: NetClient reliable dispatch, ClientWorldState atomic recovery, AuthoritativePlay feedback pipeline, LegacyReplayState/Protocol7ReplayCodec, ReplayFile/recording.

Required focused tests: byte-offset golden vectors for old7/new8; exact/truncated/extra lengths; every invalid enum/mask/NaN/Infinity/identity; no partially mutated batch; max8 snapshot818 datagram; max reliable payload<=512; old7 live rejection with replay acceptance; omitted/duplicate/reordered affliction vs newer snapshot; expiry and join-midstatus without client damage; maximum-assist uniqueness/reconnect/life replacement; Kill vs Death dedup; WorldEvent replay/reset epoch and queue-full policy; overtime remains Playing/input uninterrupted;26-prefix multi-batch world assembly and capacity boundaries; old4/5/6/7 replay fixtures unchanged; two-client authoritative8 loopback and existing WAN suite separately. Do not infer GUI readability or authenticated-backend correctness from codec tests.

Rules flags u16@72 now use bit0 for cancellation of spawn protection on offensive action and bit1 for PickupRespawnAnnouncements (default false); all other bits are rejected. World semantic kinds11/12 are OvertimeStarted and MatchPoint, emitted at authoritative transitions.

## G4 optional account admission extension (unreleased8)

Guest Join remains34 bytes. The G5 extension appends role flags@34, ticket length u16LE@35 and compact ASCII JWT@37, maximum963 bytes. The largest Join including24-byte header is exactly1024, preserving the existing datagram ceiling. Extension lengths are exact, characters restricted to base64url segments separated by two dots; there is no credential-bearing fragmentation. Historical replay payloads remain frozen; live roster and observer role changes are specified below.

Discovery retains its base and rules-v1 reader. The optional rules-v2 tail keeps the original8-byte rules prefix, sets extension version2, uses prefix byte5 bit0 for RequiresTicket, and appends16 Guid.ToByteArray-order ServerId bytes. EmptyServerId means no advertised account service. Discovery is informational: signed ticket validation still binds the configured server ID and incarnation. Client NetClient accepts an optional explicit nonce/ticket pair; authenticated reconnect requires a fresh pair.

Server validation pins issuer, ES256 P-256 configured public keys, audience/serverId, sid/startupId, sub/account UUID, jti, exact name/nonce and bounded numeric lifetime. Duplicate JSON names, unsupported algorithms, key URLs/embedded keys and critical headers are rejected. Replay ownership is scoped to exact endpoint+nonce+ticket; only a fresh credential may restore a disconnected authenticated participant. All HTTP/signature work runs on the bounded background authority; only completed, still-unexpired results reach owner-thread admission.

### Unreleased G5 observer role

The ordinary guest Join remains 34 bytes. An extended Join adds flags at byte34
(bit0 requests observer), unsigned ticket length at35, and the optional compact
JWT at37. The shared maximum ticket size is963 bytes, retaining the1024-byte UDP
limit. A zero-length extension is canonical only for an observer request.
Welcome slot255 denotes an observer and never indexes the eight competitive
slots. A server may downgrade a requested player to observer (Duel overflow);
an explicit observer request cannot be upgraded to a competitive slot.
Reliable event13 carries the existing MatchTransition payload for an in-place
observer transfer. The client fences older snapshot/world header sequences and
reliable event IDs before accepting its historical baseline.

Observers use a separate bounded connection array (0..16). Their immutable shared
history retains at most3601 ticks/64MiB, with configured delay0..30 seconds. No
live baseline fallback exists: warm-up admission fails explicitly, and an evicted
cursor closes. Rules, roster, snapshots, world records and reliable gameplay cues
come from the same historical match. Signed operator-authorized observerTrusted
is the only delay bypass. Observers cannot submit gameplay input or occupy bot/
player/report participant slots.

The client camera is presentation-owned: F1 cycles Free/FirstPerson/Chase/Orbit/
AutoDirector, F2/F3 cycle active targets, F4 cycles replicated objectives, +/-
adjust FOV, and brackets adjust free-camera speed. AutoDirector initially uses
bounded round-robin selection. The HUD reports replicated health, weapon, ammo,
K/D/A and score; objective view reports replicated ownership/carrier state.
Camera-only movement continues while replay simulation is paused. This source
implementation does not constitute rendered GUI or WAN validation.

## G5.7 intermission ballot extension

Live reliable kinds 14 (server ballot) and 15 (human vote request) extend the live allowlist; 13 remains observer transition. Ballots use a bounded 28-byte header and up to eight 36-byte options. Requests are exactly 16 bytes and carry only match/phase/option-set revisions and a server-offered byte ID. Independent publication revision prevents reordered vote-count updates from erasing confirmation. The complete field table, authority rules, and frozen-codec boundary are in [G5_VOTING.md](G5_VOTING.md).

### G5 bot handoff: bounded pending admission

Unreleased protocol 8 adds **NetMessageType.JoinPending = 13** (a separate namespace from ReliableEventType 13). The 24-byte envelope requires `Unsequenced`, connection ID/sequence/ACK fields zero. Its exact eight-byte body is the original client nonce (u64 little endian). The client accepts it only from the already pinned server endpoint while unconnected and with the matching nonce; malformed lengths or a different nonce do not refresh admission liveness.

A full bot-filled server reserves at most one bot slot per pending human, with eight pending admissions maximum. Repeated Join packets act as heartbeats for that same reservation and receive JoinPending; they never consume an authenticated ticket again. A cached validated identity must remain unexpired at final admission. The pending lifetime is at most 30 seconds and also subject to the normal network heartbeat timeout. Match rotation, expiry or cancellation releases the retirement request. Bot fill cannot reuse a reserved slot. Only the existing safe retirement boundary (waiting/countdown or dead during play) releases authoritative objectives and allows ordinary Accepted/ClientReady admission. This does not despawn live bots or alter combat balance.

Discovery v3 uses its final byte, `IdentityV2Size + 5`, as bot count, constrained to the advertised total player count. Old discovery versions imply zero bots. Browser full/QuickJoin filtering compares human count (`PlayerCount - Bots`) with capacity, allowing discovery of bot-filled servers.

### Reliable event receipt window

Packet headers retain their 32-bit ACK history and each connection retains at
most32 pending reliable payloads. Application event deduplication is a separate
16,384-bit ring (2KiB per connection), covering the30-second peer timeout at the
production send budget of8 events per60Hz tick (14,400 attempts). The sender
refuses admission before an outstanding event would leave that receipt window.
Thus steady completions cannot evict a delayed old event after only32 newer
IDs. Retries still need an ACK for an actually received packet; no receipt is
inferred. Sequence half-range ambiguity is rejected and uint wrap is supported.
Diagnostics identify the oldest pending event's type/ID, attempts and age without
logging its payload. This changes no wire bytes or pending payload capacity.
