# Semantic event foundation (Stage A/B)

Run `dotnet run --project tools/match-events-check -c Release`. No game content,
network socket, GPU, external package or credentials are required. CI runs this
check on game-source changes. These are **F0 domain/adapter checks**. The scene
adapter tests use explicit minimal substitutes in `SceneStubs.cs`; they are not
F1 gameplay simulation or F4 real-process network evidence.

## Production behavior

Each scene owns a passive bounded event bus, award engine, contribution tracker,
and independent legacy-marker parity diagnostics. Replica scenes and online
clients return before identity lookup or event generation. Online identity uses
the existing `ushort` match, `ulong` epoch and occupant/life identifiers. Offline
identity belongs to the scene. All timestamps use the existing 60 Hz simulation
clock, not presentation time. No health, score, durable statistics, license
eligibility, replay format, protocol, HUD, kill feed or announcer behavior changes.

The following real simulation transitions now produce events:

- Authority countdown/start and game-over; point-goal modes emit match-point
  transitions for every team, not just the local HUD's team.
- Spawn, confirmed death/suicide, and headshot at accepted damage resolution.
- Successful player weapon firing, once after Spawn succeeds (not per projectile).
- Octolith/relic pickup and drop, octolith capture and defense/stop.
- Node contest transitions and credited node captures.
- Prime transfer on death and Prime loss on death/disconnection.
- Assists: any distinct non-killer who dealt positive enemy damage to that victim
  life within the last **300 simulation ticks, inclusive**, receives one assist
  at death. Repeated hits update one contribution; self/friendly/zero damage is
  excluded. An earlier-life contribution may qualify, but a different occupant
  reusing that slot cannot receive it. Suicides/environment deaths can still
  award qualifying non-killer contributions. License exclusions remain intact.

`OvertimeStarted` is declared but has no producer: the current engine has no
actual overtime transition (the replay enum alone is not a gameplay rule).
WeaponFired also covers successful autonomous halfturret shots and alt-form bomb
spawns. These carry Turret/AltForm flags and entity IDs; bombs carry their BombType
in Value and use Weapon=None. Child projectiles and bomb detonations do not emit
additional firing events. NodeCaptured follows existing per-player capture
credits, so multiple credited players can share an entity transition.

Awards include first blood, two-through-ten chain kills (inclusive 240 ticks),
five-through-thirty killing sprees, assists, capture, defense, carrier interception
and Prime slaying. Enemy kills only advance chains/sprees; death resets the exact
victim life. Slot/life reuse does not inherit awards. Histories and awards retain
at most 1024 entries; assist state is at most eight by eight contributors.

## Identity and migration boundaries

The bus is an **ordered local Stage A stream**. Its high-water deduplication must
not be used unmodified for a future reordered network stream. Future reliable
transport needs a bounded reorder/deduplication window. IDs never wrap silently;
exhaustion refuses emission until the owner explicitly starts a new match/epoch.
Offline identity/clock exhaustion also fails closed without changing gameplay.
Repeated or older deaths for a known victim generation/life are refused even if
presented with a new event ID.

Independent parity observes existing exact replay kill markers and accepted
headshot markers at snapshot publication. It compares occupant/life, weapon,
classification and an explicit 120-tick maximum observation delay to pending
semantic facts. `Matched`, `MissingSemantic`, `MissingLegacy` and `Pending` remain
inspectable on `scene.MatchEvents.ParityDiagnostics`; development builds also
write unmatched observations through Debug. Snapshot coalescing, unavailable
exact identities and capture gaps can produce real mismatches. A passing domain
check does **not** establish production parity. Asset-backed all-weapon/all-mode
sessions must inspect these diagnostics before any Stage C–G consumer migration.
Player-facing consumer migration remains gated on the broader parity evidence below.

## Protocol 35 staged recording and receiving

Production packet IDs 55 (semantic fact) and 56 (award) use the analyzer-only
packet generator. `ValidateSchema(ref bool)` extends generated validation for
cross-field identities and full-width nonzero `ulong` authority epochs. Generated
TryRead rejects impossible identity combinations, all truncations, trailing bytes,
unknown enum values and out-of-range fields. Packets carry no display names.

The authority records generated facts and awards into the existing passive replay
fact stream. Replica decoders retain their own bounded semantic receiver and do
not recompute awards. Decoder checkpoint version 5 restores facts, authoritative
awards and reordering/deduplication history; existing checkpoint versions 1–4 and
protocol 34 recordings remain readable. The recorder seeds semantic history when
private world capture starts and includes it in ordinary network restore baselines.
Histories retain 1024 events and 1024 awards; the detached decoder checkpoint limit
is now 256 KiB to accommodate those bounded histories. This is recent history,
not an unbounded full-match event archive; complete recordings retain the full
sequential fact stream separately.

Live network fanout is mandatory on the dedicated server. Both packet types use
ordinary reliable delivery and background processing. `TrySendSemantic` refuses additional facts while
16 reliable entries are outstanding. The server retains 4096 source facts and
tracks each ready peer's cursor, retries eight facts per pump within the ordinary
reliable budget, and disconnects a peer whose cursor falls behind retained history.
Late join starts at the current tail. The mandatory passive stream never consumes
critical reserve. Inbox admission checks actual priority atomically before ACK.
Scene.SemanticPublisher.HistoryGaps remains an explicit producer diagnostic.

`ProjectPrime -replayformatcheck` includes generated packet golden bytes,
malformed-wire boundaries, reordered delivery, bounded history eviction, semantic
checkpoint restoration, atomic failure and historical protocol34 compatibility.
The existing protocol-generator tool exercises the optional validation hook.

With user-provided game files, run:

```
dotnet run --project tools/nettest -c Release -- --semantic-scene /path/to/userData
```

This F2 check starts the real ServerSim, uses real players and the production
accepted-damage path, and independently compares exact replay kill/headshot markers
against semantic facts. It covers health/score invariance, assists, all nine beam
types and respawn lives. It does not claim projectile trajectory/collision,
F4 reconnect, or F5 WAN evidence. The game-mode scene check now uses real
spawned lives and compares accepted pickups/captures, node contest/capture and
Prime changes to independent production counters across 2/4/8 players (891
checks). The reliable test exercises real UDP inbox saturation and retries the
same semantic event after capacity becomes available.

Remaining Stage C–G migration gates: source-claim corrections and delayed/coalesced snapshot parity; measure reliable
bandwidth under load; migrate kill feed, announcer, telemetry and post-match
consumers only once those checks pass. Existing player-facing consumers and their
legacy replay translation continue unchanged. Overtime still needs an actual
authorized gameplay rule before an authoritative producer can exist.
