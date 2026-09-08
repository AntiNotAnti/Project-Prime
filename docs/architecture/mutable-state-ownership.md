# Mutable state ownership audit

Status: bounded MI0/A2-A5 source audit. This document records current behavior,
proposed owners, and the tests required to prove isolation. It does not claim that
two scenes or two matches are isolated.

Audit scope:

- the `MI1` through `MI10` and `A2` through `A5` sections of
  `/Users/jarrett/Downloads/PRIME_HUNTERS_MULTI_INSTANCE_SERVER_ARCHITECTURE_MASTER_PLAN.md`;
- player and roster state, RNG, camera and special entities, bot AI, collision
  query workspaces, content caches, and gameplay feature state;
- the current source and the dirty worktree snapshot available during this
  audit.

The checkout already contained dirty changes outside this audit, and the A2
player/roster migration was actively in progress. This audit added only this
file. The line references below are evidence from that snapshot and may move as
the migration continues. No source implementation was changed and no isolation
claim is inferred from a passing single-match test.

## Ownership vocabulary

The plan's MI0 classification is used throughout:

| Class | Meaning | Required treatment |
| --- | --- | --- |
| A | Immutable process-wide data | May remain process-wide after construction is complete. |
| B | Mutable client-only presentation | May remain client-process state only when it cannot be reached by server or match simulation. |
| C | Mutable worker infrastructure | Make worker-owned and synchronized; it must not carry match semantics. |
| D | Mutable match gameplay state | Own it by `Scene`/`MatchRuntime`/`MatchInstance`; no process-global mutable copy. |
| E | Unknown | Resolve by call-path tracing and a focused test before MI5. |

The practical boundary is the one in the plan: a second scene must not reset,
overwrite, or read semantic state from the first scene. A thread-local pointer is
not an ownership boundary when the underlying object is still global.

## Snapshot findings

The initial implementation was single-scene by construction. At `HEAD`,
`PlayerEntity` held the slot array and counters, `GameState` held nicknames and
called global resets, `Rng` held both mutable streams, and camera/AI/collision
helpers held static current state or scratch collections. The current dirty tree
has progressed through much of A2: `Scene` owns `MatchPlayers`, `MatchRoster`
exists, `GameState.cs` is deleted, and production `src/` references to the old
player globals have largely been migrated. The initial audit snapshot still had
old `tools/nettest` callers, while the constructor still reset camera globals and
content caches. The test tree's direct `GameState` reference is an intentional
deletion guard described below.

Follow-up implementation status: the scoped A2 player/roster gate is accepted.
Release `976/976` passed, including four ownership tests; all legacy
`tools/nettest` references were migrated and its Release build passed. This
evidence covers player/roster ownership only. A3/A4/A5 remain unresolved, so it
does not establish full multi-match isolation.

The remaining A3/A4 blockers are independently visible in current source:

1. `Rng.Rng1`/`Rng.Rng2` and all `GetRandomInt*` methods mutate process-global
   state. Server replay, content validation, AI, input, projectiles, camera
   shake, and room load still call these methods.
2. `CameraSequence.Current`/`Intro`, `CamSeqEntity.Current`, and
   `PointModuleEntity.Current` are process-global semantic state. `Scene` and
   room setup still clear them while other scene paths read them.
3. `PlayerAiData` is per-player, but its global destination array, visibility
   matrix, and rotating indices are static. The current update methods accept a
   `Scene` while writing those shared arrays, so passing a scene does not isolate
   the state.
4. `CollisionDetection` returns a process-global candidate list and uses global
   active/inactive/temp pools and a shared `_seenData` set. `Init()` is called per
   scene, which adds to one global pool; concurrent queries can clear and refill
   the same collections.
5. `Scene` construction calls `Read.ClearCache()` and `Text.Strings.ClearCache()`;
   `Read.ServerMode`, `ContentEnvironment.Open`, and `ContentEnvironment`'s
   context switch also clear caches and mutate global content paths. This is
   incompatible with concurrent scenes or multiple content identities in one
   worker.
6. `Features.AllowInvalidTeams` is read in match flow and
   `Features.MaxPlayerDetail` is read while player simulation chooses effects.
   They are gameplay-reachable mutable statics and need match rules or a frozen
   match feature set. HUD-only values can remain client-owned after reachability
   is proved.

## Player and roster state — MI1/MI2/A2

### Confirmed current behavior

The pre-migration `PlayerEntity` implementation (the `HEAD` version of
`src/Game/Gameplay/Hunters/PlayerEntity.cs`) defined mutable static
`MainPlayerIndex`, `PlayerCount`, `MaxPlayers`, `PlayersCreated`, `_players`,
`Players`, and `Main` together. Its static `Construct`, `Reset`, and `Create`
methods allocated and cleared the one process-wide slot array. `GameState` held a
static `Nicknames` array and its `Reset` called `PlayerEntity.Reset()` and cleared
camera current state (`HEAD:src/Game/Runtime/GameState.cs`). Those are class D
match state, not process-wide metadata.

The dirty tree now contains a per-scene registry in
`src/Game/Gameplay/Hunters/MatchPlayers.cs:8-45`. It owns an eight-entry array,
`ActiveCount`, `CreatedCount`, `MaxPlayers`, reset, and slot creation. `Scene`
exposes it at `src/Game/World/Scene/Scene.cs:21-25,35-49`, and
`PlayerEntity` has scene-relative `IsMainPlayer` and no longer exposes the old
static roster fields (`src/Game/Gameplay/Hunters/PlayerEntity.cs:198-200`).
`src/Game/Match/MatchRoster.cs:5-13` provides per-scene default nicknames.

The A2 player/roster migration is accepted by the Release evidence above.
The initial audit snapshot recorded old `tools/nettest` calls (for example
`tools/nettest/LagCompScriptCheck.cs:66-73` and
`tools/nettest/MatchPhaseCheck.cs:178-184`); those callers have since been
migrated. `src/Game/World/Scene/Scene.Headless.cs:13-18` still documents the
older one-scene process assumption, and `src/Game/World/Scene/Scene.cs:39-49`
still clears global content and camera state in every constructor. Those are
tracked as A3/A4 blockers. The reflection assertion in
`tests/Tests/Match/PlayerIsolationTests.cs:84` intentionally verifies that the
removed `GameState` type stays absent; it is a regression guard, not a stale
consumer. That guard covers the removed roster/type surface only; it is not a
complete mutable-static inventory.

`MatchRuntime` already owns per-slot match statistics and counters in
`src/Game/Match/MatchRuntime.cs`; those arrays are distinct from the entity
registry and identity state. The current `MatchRoster` only stores `Nicknames`.
That nickname-only shape is the deliberate minimal MI2 equivalent in this
migration. Existing participant identity is carried by the player entity and
server ledger paths; any later consolidation of `PlayerId`, participant kind,
team, hunter, or connection identity must keep those owners explicit, but their
absence from this small roster is not an A2 failure.

### Proposed owners

- `Scene.Players` owns `PlayerEntity` instances, slot capacity, active count,
  creation/reset, and the scene-local presentation slot. Headless scenes use
  `LocalPlayerSlot = -1`; a server has no conceptual main player.
- `Scene.Roster` owns the per-scene display names in the current A2 shape.
  Existing participant identity remains in the player entity/server ledger paths;
  if a later change moves stable IDs, participant kind, team, hunter, or
  connection identity into the roster, it must preserve those owners explicitly.
  `MatchRuntime.Players` remains statistics, not identity.
- `PlayerEntity` owns only its own mutable simulation and AI state. It should
  reach sibling players through its scene/roster owner, never through a static
  array.
- Client-only morph-ball trail buffers currently stored as static arrays at
  `src/Game/Gameplay/Hunters/PlayerEntity.cs:261-264` and mutated at
  `:976-1009` should be per-player or in a scene presentation registry. They are
  class B only after proving that headless simulation cannot touch them.
- `PlayerVolumes` and `KandenAltNodeDistances` at
  `src/Game/Gameplay/Hunters/PlayerEntity.cs:2069-2107` are derived from content.
  Make them immutable `WorkerContent`/player collision profiles after one
  version-bound build; they are not match roster state.

### Required checks

1. Run a source/reflection guard over all server-reachable assemblies. No mutable
   static player roster, nickname, local-player, or reset field may remain.
2. Construct scenes A and B, populate the same slots, then assert distinct
   object identities, independent active counts, names, hunters, teams, and
   server/entity connection identities. Construct/reset B after mutating A and
   compare A's complete snapshot before and after.
3. Verify a headless scene has no local player and that client-only main-player
   input cannot be selected by server code.
4. The scoped A2 validation is recorded as Release `976/976` plus four
   ownership tests, with all legacy `tools/nettest` references migrated and its
   Release build passing. A source-only grep or the roster/type reflection guard
   is not enough for the full MI0 inventory; use a separate reflection/Roslyn
   scan for all server-reachable mutable statics.

## RNG — MI3/A3

### Confirmed current behavior

`src/Game/Runtime/Rng.cs:5-18` contains the exact linear state transition in
`CallRng(ref uint,uint)`, but `Rng1` and `Rng2` are mutable static properties at
`:10-11`. `GetRandomInt1` and `GetRandomInt2` mutate them at `:30-42`, and
`SetRng1`/`SetRng2` mutate them at `:44-52`. `DoCameraShake` consumes three
`Rng2` calls per frame at `:64-84`.

The use is broader than server combat. Current call sites include player input
(`src/Game/Gameplay/Hunters/PlayerInput.cs:968-970`), camera shake
(`src/Game/Gameplay/Hunters/PlayerCamera.cs:873-894`), AI (many calls in
`src/Game/Gameplay/Hunters/PlayerAi.cs`, including `:822`, `:2155-2221`, and
`:10625-10646`), room load (`src/Game/World/Entities/RoomEntity.cs:237-249,327-340`),
projectiles (`src/Game/World/Entities/BeamProjectileEntity.cs:797,1313,1416`),
and replay/snapshot serialization (`src/Server/Replay/ServerReplaySession.cs:52-53`).
`ServerSimulation` snapshots and restores global values at
`src/Server/Simulation/ServerSimulation.cs:61-63,210-229`; this protects one
match only and causes interference when another scene advances the same streams.

`ServerCombat` has a per-instance `_spreadSeed` and a pure `Rng.CallRng` step
(`src/Server/Simulation/ServerCombat.cs:37-69`), but its default seed is still
read from global `Rng.Rng2` at `:59`. Its `[ThreadStatic]` `Current` pointer at
`:37-39` is ambient service state, not a match owner, and is explicitly barred
as a permanent design by the plan.

### Proposed owners

- Keep `Rng.CallRng`'s exact arithmetic as a pure algorithm with a focused
  sequence test.
- Add one `MatchRandom` to `Scene`/`MatchRuntime`, with both stream states and
  explicit `Next1`/`Next2` operations. All gameplay call sites, including AI,
  projectiles, room setup, camera shake, replay, and server validation, must
  receive the owning scene or random service.
- Seed `ServerCombat` from the match random state or an explicit MatchSpec seed;
  never read a process-global stream when constructing a combat authority.
- Store snapshot/replay RNG fields from the match owner. A replay serializer may
  remain process-wide code, but it must serialize the supplied instance state.

### Required checks

1. Compare a long sequence from `MatchRandom` against the current exact
   `Rng.CallRng` arithmetic, including zero and maximum bounds.
2. Advance B one thousand times and assert A's next values and authoritative
   event trace are unchanged. Run A alone and interleaved with busy B and compare
   traces byte-for-byte for the same inputs and seeds.
3. Reset one match to its initial seed and assert event, spread, AI, and replay
   output reproducibility. Search and reflection-scan for mutable static RNG
   fields before A3 acceptance.
4. Prove server content validation and replay snapshot code does not reset or
   read a different scene's RNG.

## Camera and special entities — MI4/A3

`src/Game/World/Entities/CameraSequence.cs:26-50` has mutable sequence progress,
camera references, and static `Current`/`Intro`. `SetUp` writes `Current` at
`:178-220`; `End` conditionally clears it at `:248-277`. `Process` reads the
scene-local player, but input, player processing, camera processing, and match
flow still read the static current pointer (for example
`src/Game/Match/MatchFlow.cs:33-49,238-246` and
`src/Game/Gameplay/Hunters/PlayerInput.cs:471,779,854,1130,1539`).
`CameraSequence.GetKeyframeRef` is already scene-relative at `:528-544`, which
is useful evidence that the static current pointer can be removed without
changing sequence data ownership.

`src/Game/World/Entities/CamSeqEntity.cs` retains a static current entity and a
static sequence data cache; `PointModuleEntity` retains a static current chain
(`src/Game/World/Entities/PointModuleEntity.cs:3-10,37-53`). Room transition
cleanup writes these globals at `src/Game/World/Entities/RoomEntity.cs:310-323`.
`SceneSetup.LoadGame` clears camera caches/current/intro at
`src/Game/World/Scene/SceneSetup.cs:48-60`, and the current `Scene` constructor
does the same at `src/Game/World/Scene/Scene.cs:45-49`. Creating B can therefore
end or clear A's sequence even if both scenes have different players.

The owner should be a `Scene.CameraSequences` manager containing the current
sequence, intro sequence, and per-scene sequence instances, plus a scene-owned
special-entity registry for point-module/camera state. Immutable sequence bytes
or parsed keyframes may be `WorkerContent` only after they no longer contain
scene or mutable camera references. Callers should use the owning scene or
player camera rather than a static ambient current.

Required tests: start distinct sequences in A and B; process/end/reset one while
the other is active; assert each scene's camera info, input blocking, intro
sequence, node references, and special-entity current pointer remain unchanged.
Include a headless scene because “rarely used” is not an isolation proof.

## Bot AI — MI5/A3

`PlayerAiData` is constructed per player (`src/Game/Gameplay/Hunters/PlayerEntity.cs:427-434`)
and contains substantial per-bot timers, targets, paths, and flags. That part is
the right ownership boundary. `PlayerAi` also has static mutable state at
`src/Game/Gameplay/Hunters/PlayerAi.cs:178-211`: `_globalField0`, `_globalField2`,
the `_globalObjs` destination array, `_playerVisibility`, and rotating
visibility indices.

`InitializeGlobals` clears those arrays globally at `:213-234`.
`UpdateVisibilityAndGlobals(Scene)` accepts a scene but writes the shared matrix
and indices at `:236-284`; `UpdateGlobals` reads and mutates the shared object
list at `:286-320`, and `RemovePlayerFromGlobals` compacts it at `:10772-10800`.
`Scene.AddRoom` calls global initialization and `Scene.UpdateScene` calls the
global update methods (`src/Game/World/Scene/Scene.World.cs:80-102,211-235`).
The current `ServerBotManager` has an instance participant list, but its player
activation and team/health mutations still depend on scene player state; an
instance bot list does not isolate these static AI workspaces.

`AiPersonality` combines per-player loading with mutable global parse caches:
`src/Game/Content/Formats/AiPersonality.cs:51-79,81-205` stores a version,
byte buffer, and several dictionaries, and clears them globally. Parsed
personality data can be worker content; active personality/timers/targets belong
to the bot and scene.

The proposed owner is one `BotRuntimeState` per `Scene`/`MatchInstance` holding
the destination object list, visibility matrix, and scan cursors. Keep
`PlayerAiData` per bot. Load immutable personality data through a
version-bound `WorkerContent` handle, with no global version switch.

Required tests use two scenes with bots in the same slots but different room
geometry and targets. Interleave AI frames and collision visibility checks;
assert visibility matrices, chosen targets, destination queues, and bot traces
match the isolated runs. Include a bot reset in A while B is active. A static scan
must reject the old global object list, matrix, and indices.

## Collision and query workspaces — MI6/A3

`src/Game/Content/Formats/CollisionDetection.cs:55-70` defines static
`_activeItems`, `_inactiveItems`, and `_tempItems` pools. `Init()` enqueues 2,048
objects into the one process-wide queue. `CheckBetweenPoints` and sphere checks
clear the static `_seenData` set at `:83-105` and `:372-379`, then use it while
iterating candidates. `ClearCandidates` drains the global active list at
`:350-358`.

`GetCandidatesForLimits` and `GetCandidatesForPoints` clear, fill, and return the
same `_activeItems` list (`:758-783,870-890`). The pool is drained and repopulated
through `_inactiveItems` and `_tempItems` at `:832-867`. `Scene.InitializeWorld`
calls `CollisionDetection.Init()` for each scene at
`src/Game/World/Scene/Scene.World.cs:242-263`. These are semantic query results
associated with a scene, not harmless process-wide metadata; concurrent calls
can overwrite the list while another caller is enumerating it.

The collision data cache is a separate concern. `src/Game/Content/Formats/Collision.cs:34-43`
has static normal/First Hunt dictionaries and `ClearCache`; parsed
`CollisionData.Counter` is marked “only set at runtime” at `:205`, but no write
was found in the targeted source search. Keep that field class E until its
runtime meaning is traced.

Move active/inactive/temp candidate pools, `_seenData`, and any semantic query
cursor to `Scene.CollisionWorkspace`. A query should return a workspace-owned
immutable snapshot or a caller-owned span/list whose lifetime is explicit. Pure
temporary data can be stack/local or a non-semantic pool leased to one simulation
lane. Parsed collision geometry can be immutable `WorkerContent` once its cache
is version-bound. Do not share one static scratch list across lanes.

Required tests run identical queries on A and B with different rooms in parallel,
retain both result collections until each query completes, and compare results
with serial execution. Add pool-exhaustion and repeated `Init` tests. Run the
same collision/AI visibility workload with A alone and interleaved with a busy B.

## Content and cache lifetime — MI8/MI9/A4

The current `Scene` constructor clears `Read` and string caches on every scene
creation (`src/Game/World/Scene/Scene.cs:35-49`). `Read.ServerMode` also clears
all model/effect/particle caches on change (`src/Game/Content/Read.cs:12-37`),
and `Read` stores mutable model/effect/particle dictionaries at `:28-36,851-852`.
`Text.Strings` stores a mutable language-keyed cache and clears it globally
(`src/Game/Content/Strings.cs:11-38`). `Collision` and `AiPersonality` have
similar global cache clear paths.

`ContentEnvironment.Open` and its `ContentContext` mutate `Paths.MphKey`,
`Paths.SetPath`, a process-global manifest, and then clear all caches
(`src/Game/Runtime/ContentEnvironment.cs:13-47,67-96`). The context saves and
restores one global version; it cannot make two versions concurrently visible.
`Scene.CreateHeadless` toggles `Read.ServerMode` and `CloseHeadless` toggles it
back (`src/Game/World/Scene/Scene.Headless.cs:19-30,96-110`). `Scene.Language` is
also a static mutable setting (`src/Game/World/Scene/Scene.World.cs:26-41`) and
string lookup keys its global cache from it.

The A4 owner should be one immutable `WorkerContent` loaded and validated before
scenes are admitted. It should contain the data version, content hash, supported
maps, model/collision metadata, and read-only lookup caches. Cache builders may
use private mutable construction state, but publish immutable/frozen data before
the first scene starts. A scene receives a content handle and must never clear or
switch the worker's content. If multiple content versions are required, use
separate worker pools as the plan requires.

`Weapons.Current` is another mutable process-wide selector
(`src/Game/Content/Metadata/Weapons.cs:373-374`), set during room setup at
`src/Game/World/Scene/SceneSetup.cs:32`. It should become part of immutable
content or match rules, with a per-scene reference to the selected table. The
same review applies to any global `Paths` or language selector reachable from
server code.

Required checks:

1. Create/load two scenes concurrently and assert scene creation does not clear
   already-loaded model, string, AI, or collision data.
2. Assert one worker has exactly one immutable content version/hash and that no
   scene can switch it after bootstrap. Read caches concurrently under a stress
   test and run a race detector or equivalent failure check for mutable
   dictionaries.
3. Attempt to open a second version while a scene runs; it must be rejected or
   routed to another worker pool, never globally switched in place.
4. Repeat A4 with headless scene creation/destruction because the current
   `Read.ServerMode` setter is itself a cache-reset edge.

## Features and other static state — MI7/A3

`src/Game/Runtime/Features.cs:5-147` contains mutable static bugfix, HUD,
gameplay, and presentation settings. `Features.AllowInvalidTeams` is read by
match flow at `src/Game/Match/MatchFlow.cs:65-88`; `Features.MaxPlayerDetail`
changes player effect behavior in `src/Game/Gameplay/Hunters/PlayerEntity.cs:788`.
Those are class D because they can change match validity or simulation results.
`ProHud`, opacity, crosshair, weapon-list, and similar values are class B only
when all reads are client presentation reads. Cheats and campaign toggles need a
call-path classification before being treated as retired or client-only.

Move gameplay-affecting values into frozen `MatchRules`/`MatchFeatureSet` before
match start. Keep strictly client-only presentation preferences in the client
process. Debug and developer switches should be explicit worker or test options,
not silently inherited by every match. Add a two-scene test that changes a
feature in A and proves B's rules and authoritative trace are unchanged.

Other static state found during this targeted scan needs explicit classification:

- `ServerCombat.Current` is `[ThreadStatic]` ambient authority state
  (`src/Server/Simulation/ServerCombat.cs:37-39`); pass authority explicitly or
  bind it to the single-writer scene lifecycle.
- `ContentFiles` read tracing uses thread-static recorder fields. It is tooling
  infrastructure, not match state, but concurrent worker content loads need a
  scoped recorder test (`src/Game/Content/ContentFiles.cs`).
- Frozen tables such as metadata lists and authored countdown tables are class A
  only if they are never mutated after startup; the static guard should allow
  immutable/frozen values by type and reject mutable containers.

## Required implementation and acceptance sequence

This sequence follows the plan's A2-A5 gates:

### A2 / MI1-MI2

The scoped A2 gate is accepted: Release `976/976` passed with four ownership
tests, all legacy `tools/nettest` references were migrated, and the Release build
passed. Keep the existing player/entity and server-ledger identity ownership
explicit in reconnect, replication, and result paths. The intentional
type-absence regression guard remains useful, but it does not inventory all
mutable statics. Camera and cache reset edges remain MI4/MI8 work and prevent a
full multi-match isolation claim until A3/A4/A5 pass.

### A3 / MI3-MI7

First preserve the exact RNG algorithm in a pure helper, then move both streams
to one match owner. Extract camera current/intro and special entity current state;
move AI workspaces and collision query workspaces to scene owners; classify every
feature flag by reachability and move gameplay flags to immutable match rules.
Delete compatibility statics before MI5. Do not use `AsyncLocal` or `ThreadStatic`
to hide a process-global gameplay owner.

### A4 / MI8-MI9

Build and publish immutable `WorkerContent` once per worker. Remove cache clears
from `Scene` construction and normal scene teardown. Reject global content
switching while a worker is active. A `Scene` constructor should allocate only
that scene's state; constructing B must be harmless to A.

### A5 / MI10 hard gate

Add `tests/Tests/MultiInstance/` with these cases before increasing worker
density:

- B construction/reset does not change A's player, roster, camera, AI, collision,
  content, or match snapshot;
- same slot in A and B refers to different objects and names, teams, hunters,
  and active counts remain independent;
- B's 1,000 RNG calls do not alter A's next value;
- camera and special entity current state stays per scene;
- bot visibility, destination queues, targets, and AI traces stay per scene;
- parallel collision queries do not share returned lists or semantic scratch;
- changing a gameplay feature in A does not change B's frozen rules;
- A alone and A interleaved with a busy B produce identical authoritative traces;
- dozens of headless scenes can be created, stepped, reset, and disposed without
  cross-match contamination.

Add a reflection/Roslyn/static guard for mutable static fields in
server-reachable gameplay namespaces. Keep a small allowlist for immutable
metadata and scoped infrastructure, and require a focused test for each class E
entry. Do not accept a regex-only guard.

The stop condition is the plan's MI10 condition: two matches cannot influence
each other under creation, stepping, reset, collision, AI, replay, or teardown.
Until that suite passes, the supported runtime remains one match per worker and
no multi-match worker should be built.

## Evidence and open uncertainty index

Confirmed from source in this audit:

- process-global RNG state and broad gameplay call sites;
- camera and special-entity current pointers plus constructor/room cleanup;
- static AI object/visibility workspaces;
- static collision pools, candidate return list, and seen set;
- scene/cache clears and global content-path switching;
- gameplay-reachable feature flags;
- A2 player/roster gate accepted by the supplied Release `976/976`, four
  ownership tests, and migrated `tools/nettest` Release build evidence;
  this does not cover the full mutable-static inventory.

Requires follow-up before ownership is final:

- whether player morph-ball trail arrays are ever touched by headless simulation;
- all writes and intended lifetime of `CollisionData.Counter`;
- complete classification of `Features`, `Bugfixes`, and `Cheats` by call path;
- whether camera sequence parsed data can be made immutable without retaining a
  `Scene` reference;
- all static content tables not covered by the targeted scan;
- whether replay/snapshot wire compatibility requires both RNG streams at each
  existing boundary.

The next implementation pass should resolve those items with source tracing and
focused tests, then update this document with the actual owner and evidence of
the passing gate. A source grep or the existing single-match baseline is not
evidence of multi-instance isolation.
