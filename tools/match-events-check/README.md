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
No consumer migration or wire extension is included here.
