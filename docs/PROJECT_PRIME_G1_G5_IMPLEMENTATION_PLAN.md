# Project Prime - G1-G5 Gameplay and Online Systems Implementation Plan

> **HISTORICAL DESIGN DOCUMENT — SUPERSEDED IMPLEMENTATION PLAN.** This plan
> records an earlier G1–G5 baseline and is not a current task queue or
> architecture reference. Use [CURRENT_ARCHITECTURE.md](CURRENT_ARCHITECTURE.md)
> and [CURRENT_PROTOCOL.md](CURRENT_PROTOCOL.md) for the current system; source
> and the release ledger take precedence over this plan.

## Document purpose

This is the implementation plan for the next five major Project Prime gameplay and online systems epics, based on a fresh audit of the latest uploaded codebase.

The plan is intentionally source-grounded. It distinguishes between systems that already exist and should be extended, systems that are incomplete, and systems that do not exist yet.

The implementation priority remains:

- modern
- simple
- clean
- efficient
- optimized
- server-authoritative
- deterministic where gameplay requires it
- presentation-only where a feature is visual
- measured before balance is changed
- compatible with existing multiplayer content unless a ruleset explicitly opts into new behavior

This plan is designed to be handed directly to an AI coding agent.

---

# 1. Current codebase baseline

## 1.1 Major refactor status

The large multiplayer-only refactor is complete through the previously planned R0-R12 work.

The latest source already has:

- .NET 10 projects
- multiplayer-only runtime
- no Adventure launch path
- no offline authoritative match path
- server-owned match lifecycle
- server-owned match rules
- scene-owned `MatchRuntime`
- immutable `MatchResult`
- campaign persistence/progression removed
- campaign enemy runtime removed
- project split into `Game`, `Client`, `Server`, `Android`, `Audio.Ncsf`, `Tools`, and tests
- Android no longer compiling the complete desktop source tree through a wildcard
- dedicated-server publishing boundaries
- multiplayer-only source guards
- Project Prime user-facing branding

Do not redo that work during G1-G5.

## 1.2 Active source shape

The active source tree is approximately:

| Area | C# files | Approx. LOC |
|---|---:|---:|
| `src/Game` | 146 | 69,518 |
| `src/Client` | 175 | 50,937 |
| `src/Server` | 22 | 3,711 |
| `src/Android` | 20 | 5,539 |
| `src/Audio.Ncsf` | 39 | 7,946 |
| `src/Tools` | 34 | 29,449 |
| `tests/Tests` | 46 | 6,975 |

The current wire protocol is version 7.

## 1.3 Existing multiplayer timing

Current authoritative/network presentation behavior includes:

- server simulation: 60 Hz
- client simulation: 60 Hz
- player snapshots: 30 Hz
- complete world state: 5 Hz
- remote presentation delay: fixed 6 ticks / 100 ms
- maximum remote extrapolation: 3 ticks / 50 ms
- render rate can exceed simulation rate
- renderer currently draws the newest simulation state without local simulation-state interpolation

## 1.4 Existing match lifecycle

The server already owns:

```text
WaitingForPlayers
    -> Countdown
    -> Playing
    -> Ending
    -> Intermission
```

Waiting/countdown worlds are frozen and the world is reset before competitive play begins.

G1 must harden this lifecycle, not replace it.

## 1.5 Existing live input path

Live authoritative input already uses one `InputCommand` per 60 Hz client simulation tick, including:

- held button state
- rising-edge `Pressed` state
- aim direction
- desired weapon
- view server tick

Up to eight contiguous input commands are sent redundantly in each `InputBundle`.

`ServerInputStream`:

- applies at most one command per authoritative server tick
- holds continuous inputs during short gaps
- never repeats rising edges during a held fallback
- bounds buffering and skips unrecoverable old gaps

The historical protocol-4 `IntentPacket` path is replay-only.

Do not redesign live input around the old protocol-4 press-history limitation.

## 1.6 Existing HUD foundations

Project Prime already has:

- original HUD
- Pro HUD
- ping column
- eight damage-indicator model nodes
- objective/player locator infrastructure
- server-owned `PlayerRadar` rule
- mode-specific objective locators

The current authoritative damage presentation selects only four cardinal damage indicators even though eight visual nodes exist.

There is no complete modern:

- hit-marker system
- kill feed
- assist presentation
- death recap
- 2D tactical radar/minimap
- network-quality HUD

## 1.7 Existing spectator and replay foundations

Spectating already supports:

- server-owned spectator participation
- free camera
- first-person player cycling
- scoreboard while spectating
- rejoin behavior

A dedicated `UpdateCameraSpectator()` path is still unfinished.

Replay playback already supports authoritative modern recordings, but:

- replay file format is version 2
- records are sequential
- no indexed seeking
- no proper timeline
- no playback-speed system
- rewinding reopens/replays the file
- no keyframe index

These foundations should be extended rather than replaced.

## 1.8 Current persistence status

There is no persistent online identity/backend layer yet.

There is no:

- `Backend` project
- persistent `PlayerId`
- account system
- Hunter License storage
- Ranking Points storage
- leaderboard storage
- central match-history database

However, `MatchResult` is already an excellent source for authoritative post-match reporting.

---

# 2. Cross-epic architecture rules

These rules apply to G1 through G5.

## 2.1 Authority boundary

Gameplay truth must remain:

```text
Client input
    ->
Authoritative server
    ->
Game simulation
    ->
Snapshots / reliable events
    ->
Client presentation
```

Never make HUD, audio, radar, replay UI, or local client effects authoritative.

## 2.2 Render-only changes cannot mutate gameplay

High-refresh rendering must not change:

- collision
- hitboxes
- damage
- weapon timing
- movement
- spawn choice
- server input
- replay simulation
- match results

Render interpolation must use temporary presentation state and restore authoritative simulation state after drawing.

## 2.3 One server simulation path

Do not reintroduce local gameplay authority.

Practice with bots must eventually use a private localhost authoritative server.

## 2.4 Classic behavior remains available

Introduce explicit policy/preset controls before changing original gameplay behavior.

Recommended future presets:

```text
Classic
Competitive
Custom
Practice
```

Classic should remain the preservation-oriented configuration.

Competitive may opt into modern fairness improvements.

## 2.5 Avoid speculative optimization

Optimize:

- allocation hot paths
- render hitches
- packet/presentation jitter
- startup/loading hitches
- server scheduling jitter
- actual measured bottlenecks

Do not spend time turning a sub-millisecond server tick into a smaller number without player-visible benefit.

## 2.6 Do not block simulation on external services

The game server must never synchronously wait for:

- Hunter License backend
- PostgreSQL
- leaderboard service
- telemetry upload
- external matchmaking service

Use bounded asynchronous queues and durable retry/outbox patterns where persistence matters.

## 2.7 Preserve current user work

The audited repository contains unrelated modified map/tool/script files in the working tree.

The implementation agent must:

- never `git reset --hard`
- never discard unrelated modifications
- never mass-format the repository
- never rewrite map source files unless the current task requires it
- keep each G1-G5 change in narrowly scoped commits

## 2.8 Internal naming

Project Prime is now the product name.

The current code still contains legacy namespace/assembly identifiers such as `MphRead` and `ProjectPrime`.

Do not mix a repository-wide namespace/assembly rename into G1-G5. Perform that as a separate housekeeping migration so gameplay diffs remain reviewable.

---

# 3. Protocol evolution policy

G1-G3 require several likely wire changes.

Current protocol:

```text
NetHeader.Version = 7
```

Use the following policy:

1. Design all G1-G3 wire changes before declaring protocol 8 stable.
2. While protocol 8 is unreleased, amend protocol 8 rather than bumping repeatedly.
3. Preserve protocol 7 replay playback through replay-only adapters.
4. Do not accept old gameplay protocols on live sockets.
5. Every wire struct must:
   - validate exact lengths
   - validate enum ranges
   - reject non-finite vectors
   - reject invalid identities
   - remain allocation-bounded
6. Modern replay recording should record authoritative presentation facts, not client claims.

Likely protocol-8 additions:

- burn remaining ticks in player snapshot
- disruption remaining ticks in player snapshot
- assist attribution on death or a dedicated kill event
- reliable world-event payloads for pickups/objectives
- new match-rule policy fields required by G3

Persistent authenticated `PlayerId` can be protocol 9 if G4 ships after protocol 8 has become public.

---

# 4. Epic dependency graph

Recommended execution order:

```text
G1 Feel & Correctness
    |
    v
G2 Combat Readability
    |
    v
G3 Match Quality
    |
    v
G4 Persistent Player Experience
    |
    v
G5 Competitive / Community Platform
```

Some preparation may overlap, but do not implement G4 persistence while G1-G3 are still changing what an authoritative match result means.

---

# G1 - Feel & Correctness

## Goal

Make Project Prime feel smoother and more trustworthy without changing the fundamental Hunter/weapon balance.

G1 is primarily about:

- high-refresh presentation
- timing clarity
- state correctness
- spawn fairness
- deterministic edge cases
- validation of the already-modern live input path

## G1.0 - Establish a new golden baseline

Before changing gameplay:

### Record

- current unit/integration test results
- 20-second and long WAN network matrices
- server tick-time distribution
- render frame timing at 60/120/144/240 Hz
- snapshot interpolation statistics
- prediction correction statistics
- input starvation/skipped-command statistics
- real match lifecycle validation
- replay playback validation
- Android managed build
- dedicated server publish
- Windows/macOS launcher smoke where available

### Add baseline artifact

Create:

```text
docs/G1_BASELINE.md
```

Include:

- commit hash
- protocol version
- test commands
- content/data version
- measured network values
- known failures
- known hardware limitations

### Required baseline commands

At minimum:

```bash
dotnet build Game.sln -c Release

dotnet test tests/Tests/Tests.csproj -c Release
dotnet test tests/Imaging/Imaging.Tests.csproj -c Release

dotnet build tools/nettest/nettest.csproj -c Release
dotnet build src/Android/Android.csproj -c Release
```

Run the existing network baseline, lifecycle, world, combat, replay, lag-compensation, and mixed-combat harnesses using real multiplayer data.

### Acceptance

No G1 behavior work begins until baseline failures are recorded and reproducible.

---

## G1.1 - Normalize simulation timing expressions

### Current problem

Approximately 348 active `// todo: FPS stuff` references remain across 28 files.

The highest concentrations are:

```text
PlayerAi.cs             ~84
PlayerInput.cs          ~64
PlayerProcess.cs        ~53
PlayerEntity.cs         ~25
PlayerCamera.cs         ~24
PresentationPlayerHud   ~17
BeamProjectileEntity    ~12
HalfturretEntity        ~10
PlayerCollision         ~10
PlatformEntity          ~9
```

Many gameplay intervals are written as DS-era conversions such as:

```csharp
90 * 2
60 * 2
2 * 2
```

Do not make simulation variable timestep.

### Implement

Add a small timing utility in `Game`, for example:

```text
src/Game/Simulation/SimulationClock.cs
src/Game/Simulation/SimTicks.cs
```

Suggested API:

```csharp
public static class SimTicks
{
    public const int Hz = 60;

    public static int FromSeconds(int seconds);
    public static int FromMilliseconds(int milliseconds);
    public static int From30HzFrames(int frames);
}
```

For compile-time constants where required:

```csharp
public const int SimulationHz = 60;
public const int RespawnTicks = 3 * SimulationHz;
```

### Migration sequence

1. spawn/invulnerability/respawn constants
2. burn/freeze/disruption timers
3. weapon cooldown/charge timings
4. item and objective timings
5. HUD presentation timers
6. camera timings
7. projectile timings
8. bot/AI timings last

### Rules

- numeric runtime behavior must remain bit-for-bit equivalent wherever practical
- no tuning in this pass
- no float-second timers inside authoritative simulation
- wire values remain tick counts
- replays remain tick-based
- tests must compare old and new values

### Acceptance

- no behavior change in golden match traces
- timing helper names describe intent
- each migrated subsystem removes its corresponding `FPS stuff` comments
- do not force all 348 references into one commit

---

## G1.2 - Add local simulation render interpolation

### Current state

`FrameTiming` already decouples:

- simulation: fixed 60 Hz
- drawing: display/capped rate

But drawing currently uses the newest simulation state without blending.

Remote network players already have a robust render-only interpolation system through:

```text
SnapshotInterpolation
PlayerPresentation.BeginInterpolatedPose(...)
PlayerPresentation.EndInterpolatedPose(...)
```

That temporary apply/restore pattern should guide local/world render interpolation.

### Implement

Add:

```text
Client/Rendering/RenderInterpolation.cs
Client/Rendering/SimulationPoseHistory.cs
```

Expose:

```csharp
FrameTiming.RenderAlpha
```

Conceptually:

```csharp
RenderAlpha = clamp(accumulator / StepSeconds, 0, 1);
```

Capture previous/current simulation presentation poses at simulation boundaries.

### First interpolation targets

Phase 1:

- local third-person/biped body
- local first-person camera origin/orientation where safe
- first-person viewmodel transform
- moving platforms
- doors
- other simple dynamic movers

Phase 2:

- selected projectiles
- bombs
- pickup presentation transforms
- cosmetic effects where interpolation improves motion

### Do not interpolate

- health
- ammo
- score
- hit detection volumes
- collision nodes used by simulation
- authoritative projectile state used by damage
- match clocks
- objective ownership
- spawn/death state across discontinuities

### Discontinuity barriers

Reset pose history on:

- spawn
- death
- teleport
- map transition
- spectator target change
- form transition if geometry cannot be safely blended
- identity/life change
- hard server correction above threshold

### Remote players

Do not double-interpolate remote players.

Remote presentation remains driven by `SnapshotInterpolation`.

### Acceptance

At 144/240 Hz:

- local/world visual motion is visibly smooth
- 60 Hz simulation hashes/traces are unchanged
- no collision or shot outcome changes between 60 and 240 Hz rendering
- no camera snapping on spawn/teleport/morph
- no render-only pose remains written into simulation after draw

---

## G1.3 - Add render-rate local camera late latching

### Problem

Mouse/controller input is sampled through the simulation path, so first-person camera response remains effectively tied to the 60 Hz simulation even when rendering at 144/240 Hz.

### Goal

Reduce perceived look latency without allowing render-rate input to influence gameplay more than once.

### Design

Separate:

```text
authoritative simulation aim
```

from:

```text
temporary visual look delta
```

### Implement

Add a local presentation-only accumulator:

```text
Client/Input/RenderLookAccumulator.cs
```

Flow:

```text
Raw mouse movement
    ->
render-look accumulator
    ->
temporary camera/viewmodel offset for current draw

same accumulated movement
    ->
consumed once by next 60 Hz simulation sample
    ->
InputCommand aim
```

### Requirements

- consume each raw delta exactly once into simulation
- never send render frames as extra network inputs
- never alter server simulation frequency
- clamp extreme/stale deltas
- reset on focus loss, pause/menu capture, spectator transition, map transition
- preserve sensitivity/invert behavior
- mouse first
- controller late latch only after mouse behavior is proven

### Acceptance

- visual response improves at high refresh
- 60 Hz authoritative aim sequence matches equivalent accumulated input
- no double aim
- no divergence caused by display refresh rate
- replay/network behavior remains deterministic at simulation boundaries

---

## G1.4 - Fix authoritative burn/disruption state synchronization

### Current defect

`SnapshotPlayerFlags` includes:

- Frozen
- Burning
- Disrupted

But `SnapshotPlayer` carries only:

```text
FrozenTicks
```

`PlayerEntity.ApplyServerState()` applies frozen state/timer but does not reconcile burn or disruption timers.

`CombatEvent` already carries:

- `FrozenTicks`
- `BurnTicks`
- `DisruptTicks`

### Desired architecture

```text
Reliable Affliction event
    ->
immediate visual/audio feedback

Snapshot
    ->
durable authoritative remaining status
```

### Implement

For protocol 8, extend `SnapshotPlayer` with:

```text
BurnTicks
DisruptTicks
```

Keep the flag bits.

### Important client rule

Do not make snapshot reconciliation run authoritative damage logic on the client.

Create presentation-safe status state, for example:

```text
PlayerPresentationStatus
```

or explicit network setters that cannot invoke gameplay damage.

Suggested fields:

```text
FrozenTicks
BurnTicks
DisruptTicks
```

owned for client presentation.

### Presentation behavior

Implement `CombatEventKind.Affliction`:

- immediate freeze/burn/disrupt cue
- no duplicate gameplay mutation
- snapshot heals lost/reordered presentation events
- reconnect/mid-affliction correctly shows remaining status

### Tests

- join while target is burning
- join while disrupted
- burn expires on correct server tick
- disruption expires on correct server tick
- event received twice does not duplicate SFX
- event omitted/lost but snapshot restores state
- no client-generated burn damage
- replay protocol 7 remains readable

### Acceptance

Burning/disrupted/frozen presentation agrees with server state after every snapshot.

---

## G1.5 - Validate modern input edge fidelity

### Important scope correction

Do not rewrite live input around `IntentPacket.PressHistory`.

That code is protocol-4 passive replay compatibility.

Current live protocol uses per-tick `InputCommand.Pressed`.

### Add stress tests

Create network tests that intentionally generate:

```text
press-release-press
```

for the same action inside short impaired-network windows.

Test:

- Jump
- Morph
- AltAttack
- NextWeapon
- PreviousWeapon
- Boost where edge-sensitive
- spectator transition

Across:

- 0% loss
- 2% loss
- 5% loss
- burst loss
- jitter
- packet reordering
- client stalls

### Required invariant

Each accepted simulation edge must be applied:

```text
exactly once
```

or be explicitly declared unrecoverable after the bounded input-history window.

### Legacy protocol-4 replays

Only fix `NetPlayerBridge.MissedPresses()` if exact reproduction of multiple same-button edges in old protocol-4 replays is a product requirement.

Do not let legacy replay compatibility complicate live authority.

---

## G1.6 - Formalize deterministic simulation ordering

### Current state

The server already has a clear high-level order:

- lifecycle before step
- input selection
- combat context
- network input application
- world simulation
- match evaluation
- snapshot/history capture

Within `Scene.UpdateScene()`, entities still process in entity-list order before queued messages and final time update.

A global rewrite into an ECS-style phase scheduler is not justified.

### Goal

Define outcomes for simultaneous events and lock them with tests.

### Add tests for

- mutual kill on same tick
- lethal damage and health pickup on same tick
- objective score and death on same tick
- score goal reached as time reaches zero
- two players score on same tick
- flag carrier dies on scoring boundary
- Prime Hunter changes on match-ending tick
- Survival last-two-player simultaneous death
- team objective contested at timer expiry
- late disconnect at countdown/play boundary

### Document

Create:

```text
docs/SIMULATION_ORDERING.md
```

Document current intended policy.

### Implementation rule

If a test exposes undesirable accidental entity-order behavior, add the narrowest phase fence/service necessary.

Do not reorder every entity class simply to make the architecture look cleaner.

---

## G1.7 - Spawn Director 2.0

### Current state

`GetRespawnPoint()`:

- examines up to 25 spawn points
- rejects inactive/cooldown points
- respects Capture team-index spawns
- considers a spawn safe if every living player is at least 10 units away
- otherwise picks the point with the best minimum distance
- uses `FrameCount % valid.Count`
- eventually falls back to the first active spawn

This is robust enough to prevent origin-stall failures but is not competitively intelligent.

### New architecture

Add:

```text
src/Game/Gameplay/Spawning/
    SpawnDirector.cs
    SpawnPolicy.cs
    SpawnCandidate.cs
    SpawnDangerHistory.cs
```

`PlayerEntity` should ask the director for a spawn rather than implement scoring itself.

### Candidate hard filters

Preserve:

- active
- team eligibility
- supported map spawn
- hard cooldown where appropriate

### Candidate score

Suggested score components:

```text
nearest enemy distance
direct enemy line-of-sight penalty
enemy facing/aim penalty
local enemy density penalty
recent death near spawn penalty
recent spawn-use penalty
same-spawn repetition penalty
friendly proximity bonus for team modes
objective danger penalty
major pickup proximity policy
```

### Determinism

Use deterministic tie-breaking based on server-owned match RNG.

Do not use wall-clock randomness.

### Policies

```text
Classic
    retain legacy algorithm

Enhanced
    weighted Spawn Director

Duel
    stronger LOS/repetition/resource-control penalties
```

### Spawn protection

Add a competitive option:

```text
CancelSpawnProtectionOnOffensiveAction
```

Offensive actions include:

- firing
- dropping an offensive bomb
- offensive alt attack

Do not change Classic unless explicitly chosen.

### Tests

- all spawns clear
- all spawns crowded
- one safe LOS-hidden spawn
- all points in enemy LOS
- eight active players
- team Capture spawn restrictions
- same spawn recently used
- recent death hot zone
- deterministic tie
- no eligible ideal spawn, fallback still works
- never spawn at origin because selection returned null

### G5 integration

Expose Spawn Director scoring diagnostics to later map telemetry tooling.

---

## G1.8 - Harden existing match lifecycle

Do not rebuild lifecycle.

Add validation around:

- player disconnect during countdown
- quorum drops below requirement during countdown
- all players leave during Ending
- late join during Ending/Intermission
- map rotation while reliable events are pending
- stale inputs from previous phase
- stale world state from previous match
- terminal `MatchResult` captured exactly once
- pristine reset invariant on every new competitive countdown

### Acceptance for G1

G1 is complete when:

- high-refresh local rendering is smoother
- render refresh does not change simulation results
- camera feels lower-latency
- burn/disrupt/freeze presentation is authoritative and recoverable
- live input edge tests pass
- simultaneous-event behavior is documented and tested
- enhanced Spawn Director is available behind policy
- existing lifecycle remains stable
- no weapon/hunter balance values have changed

---

# G2 - Combat Readability

## Goal

Make combat outcomes immediately understandable without changing who wins a valid encounter.

G2 should make the player know:

- did I hit?
- was it a headshot?
- did I kill?
- what killed me?
- where did damage come from?
- what is happening with the objective?
- what can radar legitimately tell me?

---

## G2.1 - Central authoritative combat-feedback layer

### Current problem

`AuthoritativePlay.DrainEvents()` routes a combat event primarily to the event's subject entity.

For Damage/Death, that is normally the target.

This makes victim presentation straightforward but does not provide a clean attacker-facing event pipeline.

### Add

```text
src/Client/Combat/
    CombatFeedback.cs
    CombatFeedbackState.cs
    KillFeedEntry.cs
    DamageHistory.cs
```

Flow:

```text
CombatEvent
    ->
CombatFeedback.Process(event)
    ->
attacker feedback
victim feedback
global feed
death history
    ->
entity-specific presentation
```

### Identity safety

Use:

```text
Slot + ConnectionId + Life
```

when correlating local attacker/victim.

Do not attribute an old projectile to a new occupant of the same slot.

---

## G2.2 - Server-confirmed hit markers

### Behavior

When an authoritative `Damage` event has:

```text
Actor == local player's current combat identity
```

show:

- normal hit marker
- headshot variant
- kill-confirm variant when target died

Local shot firing stays immediate/predicted.

Hit confirmation waits for the authoritative server.

### Settings

```text
Hit markers: Off / Visual / Visual+Audio
Headshot cue: On/Off
Kill confirmation: On/Off
```

### Acceptance

- no hit marker for a server miss
- no duplicate marker on retransmitted event
- correct delayed-projectile attribution
- headshot flag presented correctly
- silent/zero-damage presentation rules defined

---

## G2.3 - Kill feed

Add a compact authoritative feed.

Support:

- attacker
- victim
- weapon
- headshot
- suicide
- environmental death
- team kill
- affinity indicator
- alt-form/bomb distinctions where data supports it

Suggested lifetime:

```text
4-6 seconds
```

Keep a small bounded queue.

Do not allocate unbounded strings each frame.

Resolve names from authoritative roster identity.

---

## G2.4 - Assists and combat attribution

### Server

Add a bounded per-target damage contribution ledger.

Suggested record:

```text
CombatActor attacker
uint tick
ushort damage
byte weapon
bool headshot
```

Maintain only recent meaningful contributors.

### Assist policy

Define a pure function.

Example initial policy:

- contributor is not the killer
- contributor is a valid human/bot combat actor
- contribution occurred within last 5 seconds OR remains part of the target's current unhealed damage window
- minimum contribution threshold configurable
- team damage never awards assist

Do not add score points in Classic.

### Match state

Add:

```text
Assists[slot]
```

to `MatchRuntime` / `PlayerMatchStats`.

Add `Assists` to immutable `PlayerMatchResult`.

### Network presentation

Either:

1. extend Death event to carry one assist actor, or
2. add a dedicated authoritative `KillEvent`

Prefer a dedicated structured kill event if multiple assists may be supported later.

### Persistence

G4 stores assists as a career stat.

---

## G2.5 - Death recap

The client already receives authoritative damage events.

Keep a bounded damage history for the current local life.

On death display:

```text
Eliminated by <player>
<weapon>
<final damage>

Recent damage:
<source> <weapon> <amount>
<source> <weapon> <amount>
...
```

### Rules

- clear on new life
- bind records to life identity
- cap to small N
- do not expose information the player was not legitimately given
- optionally hide while playing Competitive if information would reveal hidden players beyond normal game rules
- always available after match/replay

---

## G2.6 - Upgrade directional damage indication

### Current state

Eight HUD nodes exist:

```text
N, NE, E, SE, S, SW, W, NW
```

Current authoritative combat presentation reduces damage direction to four cardinal indices.

### Change

Quantize the incoming horizontal direction to eight sectors.

Add optional vertical hint for:

- significantly above
- significantly below

Keep the visual subtle.

### Acceptance

Synthetic angle tests cover all sectors and boundary angles.

---

## G2.7 - Radar 2.0

### Current state

Project Prime has player/objective locator infrastructure and a server-owned radar rule, but not a complete modern tactical radar.

### Architecture

Add:

```text
src/Client/HUD/Radar/
    RadarWidget.cs
    RadarFrame.cs
    RadarContact.cs
    RadarSettings.cs
```

HUD reads a prepared `RadarFrame`.

HUD must not scan arbitrary world state and decide what is legal to reveal.

### Radar contact model

```text
type
world position
relative position
team
elevation band
visibility/detection state
objective state
staleness
```

### Visual language

Suggested:

```text
enemy       circle
teammate    diamond
objective   mode-specific icon
above       upward marker
below       downward marker
stale       outlined/faded
```

### Server/rules authority

A contact is only added if current match rules permit it.

Support:

- same-level
- above
- below
- clamped edge indicator
- optional rotating-with-player or north-up display
- objective contacts

### Detection models

Initial modes:

```text
Classic
    preserve existing radar reveal behavior

Enhanced
    clearer presentation of same legal information

Custom
    optional last-known/reveal rules
```

Do not introduce permanent wallhacks as the default.

### Later possibilities

After telemetry/playtesting:

- firing reveals player for short duration
- movement/distance-based detection
- last-known positions
- Hunter-specific radar behavior

Those are balance changes and belong behind rules.

---

## G2.8 - Objective presentation events

G3 will add reliable world events.

G2 HUD should be ready to consume:

- flag picked up
- flag dropped
- flag returned/reset
- capture
- node captured
- node contested
- Prime Hunter changed
- Defender state changed
- major pickup respawn if rules permit

Do not poll 5 Hz world state for time-sensitive audiovisual cues after G3 event infrastructure exists.

---

## G2.9 - Audio readability pass

Add consistent cues for:

- hit confirm
- headshot
- kill
- being critically low
- objective pickup/drop/capture
- Prime Hunter change
- major pickup respawn
- overtime
- match point

World events should use positional audio when appropriate.

HUD/UI confirmation sounds should be non-positional.

Expose independent feedback volume where useful.

Do not overload the player with constant tones.

---

## G2.10 - Post-match statistics presentation

`MatchResult` already carries rich stats.

Build a dedicated post-match results presentation rather than bloating the live scoreboard.

Include mode-appropriate fields.

### General

- placement
- kills
- deaths
- assists
- damage dealt
- headshots
- longest kill streak
- best weapon

### Bounty/Capture

- scores/captures
- drops
- stops

### Nodes

- nodes captured
- nodes lost
- defensive contribution if added

### Prime Hunter

- Prime time
- kills as Prime
- Prime eliminations

### Acceptance for G2

G2 is complete when:

- attacker gets server-confirmed hit/headshot/kill feedback
- kill feed is authoritative
- death recap explains recent damage
- assists are tracked without changing Classic scoring
- all eight damage directions work
- Enhanced Radar cleanly renders only server/rules-approved information
- objective/audio feedback is consistent
- Android/small-screen layouts remain usable

---

# G3 - Match Quality

## Goal

Improve fairness, pacing, joining, team composition, network presentation, and moment-to-moment match flow.

---

## G3.1 - Overtime framework

### Current state

No explicit overtime model exists.

Do not add a new lifecycle phase that accidentally stops simulation.

Prefer:

```text
MatchPhase.Playing
+
MatchPeriod / OvertimeState
```

Example:

```csharp
public enum MatchPeriod
{
    Regulation,
    Overtime,
    SuddenDeath
}
```

Replicate it as match state.

### Mode policies

Define an `OvertimePolicy` in `MatchRules`.

Initial recommended policies:

#### Battle / Team Battle

If tied at regulation expiry:

```text
Sudden death
Next valid score lead wins
```

#### Capture

If tied:

```text
continue
next capture/score lead wins
```

If an objective is actively carried/contested at regulation expiry, never silently discard the live play.

#### Bounty

Next objective score lead wins.

#### Nodes

If tied or objective is actively contested, continue according to explicit control/score policy.

#### Defender

Do not end while the scoring zone is actively contested if policy enables overtime.

#### Prime Hunter

Tie at objective-time boundary continues until a player gains the required lead.

#### Survival

Use lives/standing rules. If a configured time limit creates a tie, use explicit last-survivor/sudden-death policy.

### Tests

Every mode needs regulation-expiry tie and non-tie cases.

---

## G3.2 - Team allocator

### Current state

Server team assignment is still effectively:

```text
slot % 2
```

on admission/rotation.

### Add

```text
src/Server/Matchmaking/TeamAllocator.cs
```

### Initial algorithm

For a joining player:

1. choose team with fewer active participating players
2. if tied, choose team trailing in match score only for casual late-join balancing if allowed
3. otherwise deterministic tie-break

### Countdown rebalance

Before match start:

- ensure team count difference <= 1
- use deterministic movement
- do not move players once regulation begins unless explicitly requested/admin-controlled

### Future hooks

Leave room for:

- parties
- hidden MMR balancing
- tournament roster locks

Do not implement those in this pass.

---

## G3.3 - Mode-aware late joining

Add:

```csharp
public enum LateJoinPolicy
{
    JoinImmediately,
    SpectateUntilNextMatch,
    Disabled
}
```

Add to `MatchRules`.

### Suggested defaults

```text
Casual Battle/Nodes/Capture:
    JoinImmediately

Competitive:
    SpectateUntilNextMatch

Survival:
    SpectateUntilNextMatch

Duel:
    SpectateUntilNextMatch
```

### Reconnect distinction

A reconnecting participant should not necessarily be treated as a brand-new late join.

Track:

```text
known PlayerId/connection session
previous participation
reconnect grace
```

G4 persistent `PlayerId` will make this stronger.

### Server behavior

A late join admitted as spectator:

- receives roster/world/snapshot state
- does not spawn
- does not affect current match scoring
- automatically becomes eligible at next countdown

---

## G3.4 - Reliable pickup/objective events

### Current state

Player snapshots are 30 Hz.

Full world state is 5 Hz.

The 5 Hz state is excellent recovery data but can make important state transitions feel up to ~200 ms stale before network latency.

### Add reliable event type

Example:

```text
ReliableEventType.WorldEvent
```

Payload:

```text
event id
server tick
match id
entity/objective identity
event kind
actor
team
small event-specific payload
```

Event kinds:

```text
PickupConsumed
PickupRespawned
FlagPickedUp
FlagDropped
FlagReset
FlagCaptured
NodeCaptured
NodeContested
PrimeChanged
DefenderStateChanged
```

### Architecture

Game logic publishes semantic state-transition callbacks through scene services.

Server translates them into reliable presentation events.

Game must not reference Server.

### Recovery

Full world snapshots remain authoritative recovery.

Reliable event:

```text
immediacy
```

World snapshot:

```text
eventual reconciliation
```

### Deduplication

Use event IDs and match IDs.

No duplicate SFX or HUD animation on resend.

---

## G3.5 - Weapon-selection UX

### Preserve actual combat timing

Do not shorten weapon-switch timings in Classic.

Improve input ergonomics.

### Add

#### Quick swap

Switch to previously equipped weapon.

#### Queued desired weapon

If a switch is requested during a temporary non-switchable state:

- remember newest valid desired weapon
- equip once legal
- do not require a second input press

#### Controller radial wheel

Current gamepad input exists, but the old HUD wheel assumes pointer-like selection.

Create a real stick-based radial selector.

Requirements:

- no mouse cursor warping
- deadzone-aware
- selection wedge preview
- release to commit
- cancel support
- does not alter keyboard direct binds

### Tests

- weapon queue canceled on death
- unavailable weapon ignored
- duplicate network input does not double switch
- previous weapon tracks successful equips only

---

## G3.6 - Player-facing network health HUD

Current `NetMetrics` already tracks:

- smoothed RTT
- jitter
- snapshot intervals
- duplicate/reordered snapshots
- queue age
- silence/gaps
- packet counters

Do not invent a second metrics system.

Add a compact derived health state:

```text
Good
Unstable
HighLatency
Interrupted
```

### Normal HUD

Show only when degraded.

### Advanced overlay

Optional:

- RTT
- jitter
- snapshot interval
- extrapolation/hold percentage
- prediction correction
- server input starvation
- packet/reorder counts

### Rules

This is diagnostic presentation only.

Never change game authority because the HUD says a connection is bad.

---

## G3.7 - Adaptive remote interpolation

### Current state

All remote players use:

```text
6 ticks / 100 ms
```

presentation delay.

Lag compensation also assumes that fixed delay.

### Goal

Reduce visual latency for stable connections without sacrificing jitter tolerance.

### Architecture

The server must own/approve the delay used for compensation.

Do not trust a client-supplied arbitrary interpolation delay.

### Initial bounded profiles

Example:

```text
3 ticks   excellent/stable LAN
4 ticks   very stable WAN
5 ticks   normal WAN
6 ticks   jittery WAN/default
```

### Inputs

Use existing metrics:

- smoothed RTT
- jitter
- snapshot interval variance
- interpolation underruns
- extrapolation frequency

### Hysteresis

Change profile slowly.

Example:

- minimum several seconds before reduction
- immediate or faster increase when sustained underrun occurs
- never oscillate per packet

### Server lag compensation

`LagCompensationPolicy` must use the exact server-owned presentation delay selected for that peer.

### Classic

May remain fixed 6 ticks.

### Competitive

Use validated adaptive policy if testing proves benefit.

### Acceptance

Run deterministic A/B matrices:

```text
fixed 6
vs
adaptive 3-6
```

Compare:

- presentation age
- extrapolation
- correction magnitude
- shot fairness
- CPU/bandwidth

---

## G3.8 - Server browser quality pass

Current server status already includes more information than the current row displays, including time remaining.

Upgrade browser details:

- mode
- map
- players/max
- ping
- time remaining
- ruleset preset
- ranked/verified flag
- friendly fire
- radar policy
- password/private state if introduced

Add:

- favorites
- recent servers
- filter by mode
- hide full
- hide incompatible
- max-ping filter
- sort by ping/player population

### Quick Join

Score candidates by:

```text
compatibility
not full
ping
population
preferred rules/mode
```

Do not build full matchmaking here. G4/G5 can layer on it.

### Acceptance for G3

G3 is complete when:

- tied matches have explicit mode-aware overtime
- teams are not slot-parity-only
- late joining follows match policy
- critical world transitions arrive immediately and reconcile safely
- weapon switching feels modern without balance changes
- network quality is understandable
- adaptive interpolation is either proven and enabled by policy or rejected with benchmark evidence
- server browser exposes meaningful match information

---

# G4 - Persistent Player Experience

## Goal

Turn each Project Prime player into a persistent identity with a Hunter License, official career statistics, Ranking Points, star rank, match history, and leaderboards.

This is the first epic that introduces a central service/database.

---

## G4.1 - Add stable player identity

Create:

```text
src/Game/Identity/PlayerId.cs
```

Use an immutable 128-bit identifier, typically `Guid`.

Do not use:

- display name
- connection ID
- slot
- IP address
- machine fingerprint

as persistent identity.

### Flow

```text
Account
    ->
PlayerId
    ->
Hunter License
    ->
career stats
```

Display name can change.

PlayerId cannot.

---

## G4.2 - Add Backend project

Add:

```text
src/Backend/
    Backend.csproj
```

Target:

```text
net10.0
```

Use:

- ASP.NET Core
- PostgreSQL
- established .NET authentication components
- simple HTTP/JSON APIs initially

Do not add Redis, Kafka, RabbitMQ, Elasticsearch, or Kubernetes unless measurements later justify them.

### Dependency

Backend may reference `Game` for small shared identifiers/contracts.

Do not reference:

- Client
- Server executable
- Android
- rendering/audio

If Backend referencing Game becomes too broad, extract only then. Do not pre-emptively create many micro-projects.

---

## G4.3 - Accounts and registration

### Identity levels

#### Guest

- no registration required
- private practice
- LAN/community if server allows
- no official Ranking Points
- optional local practice stats

#### Registered Hunter

- persistent PlayerId
- Hunter License
- official verified-server stats
- Ranking Points eligibility
- leaderboard eligibility

### Initial registration

Keep simple:

```text
email
password
display name
```

Use established ASP.NET Identity/IdentityCore password handling.

Never store plaintext/reversible passwords.

Email verification may be optional for early private testing and required before public official ranking.

### One account, one Hunter License initially

Avoid multi-profile complexity in v1.

---

## G4.4 - Short-lived game join tickets

Do not hand account passwords or long-lived account credentials to game servers.

Flow:

```text
Client signs into Backend
    ->
Client requests ticket for Server X
    ->
Backend issues short-lived signed ticket
    ->
Client joins game server with ticket
    ->
Game server validates ticket
    ->
server learns authoritative PlayerId/display identity
```

Ticket should include:

```text
PlayerId
server identity
expiry
nonce/session identifier
signature
```

Add replay protection.

---

## G4.5 - Verified server identity

Official stats require trusted match reporters.

Give verified game servers their own credentials.

Trust classes:

```text
Practice
Private
Community
VerifiedCasual
Ranked
Tournament
```

Suggested persistence eligibility:

| Match class | Career stats | Ranking Points |
|---|---:|---:|
| Practice/bots | separate | No |
| Private | optional/separate | No |
| Community | separate | No |
| Verified casual | Yes | Yes |
| Ranked | Yes | Yes |
| Tournament | Yes | policy |

Bots never contribute to official Ranking Points.

---

## G4.6 - Extend authoritative MatchResult for persistence

Current `MatchResult` already contains excellent immutable match statistics.

Add only what persistence truly needs:

### Match-level

```text
persistent MatchId (UUID or globally unique backend-safe ID)
server identity
room/map key
mode/ruleset/variant
started-at UTC
ended-at UTC
duration
verification class
protocol/build version
```

### Player-level

```text
PlayerId
assists
human/bot flag
disconnect/forfeit result
```

Later weapon telemetry may include:

```text
shots
hits
damage by weapon
```

Do not make the live simulation depend on database DTOs.

---

## G4.7 - Match-report outbox

### Critical requirement

Do not post match results synchronously from the 60 Hz simulation thread.

On `MatchResult` creation:

```text
immutable MatchResult
    ->
bounded background submission queue
    ->
durable local outbox
    ->
Backend
```

### Idempotency

Backend must treat `MatchId` as an idempotency key.

Submitting the same match ten times results in one stored match and one rating transaction.

### Failure behavior

If backend is unavailable:

- match server continues
- result is stored in local durable outbox
- retry with bounded backoff
- operator can inspect failed queue

---

## G4.8 - PostgreSQL schema

Initial schema should remain straightforward.

### Identity/profile

```text
players
player_profiles
hunter_licenses
```

### Match ledger

```text
matches
match_players
match_player_weapons
```

### Aggregates

```text
player_stats
hunter_stats
map_stats
mode_stats
weapon_stats
```

### Rating

```text
rating_transactions
```

### Future

```text
achievements
player_achievements
seasons
player_season_stats
```

Do not make future tables until used.

### Source of truth

Raw authoritative match ledger is the rebuildable source.

Aggregates exist for fast reads.

---

## G4.9 - Hunter License

The Hunter License becomes the persistent player card.

### Core identity

- display name
- selected favorite Hunter
- most-played Hunter
- join date
- equipped badge/title if later added

### Career

- matches
- wins
- losses
- win ratio
- kills
- deaths
- assists
- K/D
- damage
- headshot kills
- biped kills
- alt-form kills
- play time
- longest kill streak
- longest win streak

### Preferences/derived favorites

- favorite map
- best map
- favorite mode
- favorite weapon
- most-played Hunter
- best Hunter with minimum-match threshold

Distinguish:

```text
selected favorite Hunter
```

from:

```text
most played Hunter
```

---

## G4.10 - Restore 1-5 star Ranking Points

The original visible Hunter License star thresholds should be preserved:

```text
0-39        ★      Bounty Hunter
40-139      ★★     Super Hunter
140-389     ★★★    Elite Hunter
390-749     ★★★★   Master Hunter
750+        ★★★★★  Legendary Hunter
```

The original Hunter License also rewarded multiplayer-win milestones, which can be restored where desired:

```text
25 wins     Bronze emblem
100 wins    Silver emblem
200 wins    Gold emblem
```

Adventure-derived license symbols must not return, since Project Prime is multiplayer-only.

Replace those only with multiplayer accomplishments if desired.

### Rating implementation

Create a pure backend service:

```text
RatingService
```

Inputs:

- current Ranking Points/star tier
- placement/result
- opponent star tiers
- match trust class
- human/bot state

Outputs:

```text
points before
delta
points after
rank before
rank after
```

### Original gain/loss table

Before coding the exact point-delta table:

- verify the original multiplayer point table from reliable historical references
- encode it as immutable data
- create snapshot tests for every rank-vs-rank combination

Do not approximate the original table in production code.

### 8-player extension

The original game was designed around smaller player counts.

Project Prime supports eight.

Write and approve an explicit extension specification before implementation.

Recommended model:

1. compare a player's placement pairwise against each human opponent
2. calculate original-style result against that opponent's visible rank
3. normalize/cap the aggregate so an 8-player match does not inflate RP several times faster than smaller matches
4. bots contribute zero
5. disconnected/forfeit behavior is explicit

Do not hide this policy in implementation details.

### Hidden MMR

Do not make visible Ranking Points the only future matchmaking skill estimate.

Later add separate hidden MMR for match quality.

Visible stars preserve MPH identity.

Hidden MMR serves matchmaking.

---

## G4.11 - Hunter License API

Initial endpoints can be small.

Examples:

```text
POST  /v1/auth/register
POST  /v1/auth/login
Historical/retired client route (superseded by Node admission): POST /v1/game-tickets

GET   /v1/players/{id}/license
PATCH /v1/me/profile

GET   /v1/players/{id}/matches
GET   /v1/leaderboards/ranking

POST  /v1/server/matches
```

Server match submission endpoint requires server authentication.

Player endpoints enforce ownership where required.

Use cursor pagination for history/leaderboards.

---

## G4.12 - Client Hunter License UI

Make licenses reachable from:

- main online profile
- lobby
- scoreboard
- post-match results
- server player list
- leaderboard
- match history
- spectator target panel

Initial card should show:

```text
display name
star rank/title
Ranking Points
progress to next star
favorite Hunter
W/L
win ratio
K/D
favorite map
favorite weapon
favorite mode
play time
streaks
win emblem
recent match form
```

Do not download huge history payloads to render the card.

---

## G4.13 - Match history

Store and expose:

```text
date
server
mode
map
hunter
placement
W/L
kills/deaths/assists
RP delta
```

Selecting a match can show its full authoritative scoreboard.

---

## G4.14 - Leaderboards

Initial leaderboards:

- Ranking Points
- wins
- win percentage with minimum games
- kills
- headshots
- objective categories
- per-Hunter categories

Use indexed PostgreSQL queries/materialized aggregate tables first.

No Redis until needed.

---

## G4.15 - Persistence tests

Required:

- duplicate match submission
- transaction rollback
- backend unavailable/outbox recovery
- player rename retains stats
- bot exclusion from RP
- private/community match cannot alter official RP
- expired join ticket rejected
- replayed join ticket rejected
- wrong server ticket rejected
- rating rank-up
- rating rank-down
- rank threshold boundaries
- eight-player normalization
- leaderboards deterministic under ties
- aggregate rebuild from raw match ledger

### Acceptance for G4

G4 is complete when:

- a registered player has an immutable PlayerId
- the Hunter License survives reinstall/device change
- verified authoritative servers submit results
- duplicate submissions are harmless
- original star thresholds are preserved
- bots/private farming cannot alter official RP
- match history and leaderboards are queryable
- gameplay continues normally during backend outages

---

# G5 - Competitive / Community Platform

## Goal

Build the systems that turn Project Prime from a good online match into a durable competitive/community game:

- high-quality spectating
- modern replay
- server bot fill
- map telemetry
- competitive presets
- Duel
- tournament administration

---

## G5.1 - Spectator 2.0

### Current foundation

Already available:

- free camera
- first-person target cycling
- scoreboard
- server participation state
- rejoin

Do not rewrite those pieces.

### Add camera modes

```text
Free
FirstPerson
Chase
Orbit
AutoDirector
```

### Dedicated spectator controller

Implement a client-side camera system rather than continuing to depend only on swapping `MainPlayerIndex`.

Suggested:

```text
src/Client/Spectator/
    SpectatorController.cs
    SpectatorCamera.cs
    SpectatorTarget.cs
    SpectatorHud.cs
    AutoDirector.cs
```

### Controls

- next target
- previous target
- freecam
- chase
- orbit
- first-person
- objective follow
- FOV
- camera speed
- scoreboard
- timeline controls during replay

### HUD

Show:

```text
player
Hunter
star rank when G4 connected
health
weapon
ammo
score
K/D/A
objective status
```

### True observer connections

Current spectator participation still revolves around player slots.

For tournament/community use, add dedicated observer connections that do not consume the eight player slots.

Server config:

```text
maxPlayers = 8
maxSpectators = N
```

Observer:

- receives state
- sends no gameplay InputCommand
- never owns a PlayerEntity slot
- may send spectator camera preference only if necessary

### Anti-ghosting

Add optional:

```text
SpectatorDelaySeconds
```

for competitive public matches.

Replay delayed snapshots/events to non-trusted spectators.

Trusted tournament observers may bypass delay.

---

## G5.2 - Replay 2.0

### Current foundation

Current `.fpreplay` format 2:

- compressed sequential records
- authoritative protocols supported
- no index

### New format

Introduce replay format version 3.

Keep format 2 readable.

### Keyframes

Every 5-10 seconds write a complete replay keyframe sufficient to restart presentation:

- roster
- rules/match state
- player snapshot
- complete world state
- relevant presentation state

### Index

Store or append an index:

```text
tick
file offset
keyframe offset
event references
```

A footer or sidecar index is acceptable if crash recovery remains clean.

### Seeking

To seek:

1. find nearest keyframe <= target
2. restore keyframe
3. fast-forward records without rendering/audio
4. resume presentation at target

### Playback controls

- pause
- frame step
- 0.25x
- 0.5x
- 1x
- 2x
- 4x
- seek timeline
- jump to next/previous event

### Event markers

Index:

- kill
- headshot
- multi-kill if derived
- flag capture
- node capture
- Prime change
- match point
- overtime start
- match end

### Camera

Use Spectator 2.0 cameras during replay.

### Compatibility

- format 2 remains playable
- protocols 4/5/6/7 replay-only adapters remain isolated
- modern format records current authoritative facts

---

## G5.3 - Server-owned bot fill

### Current state

The existing Hunter bot AI is mature and extensive.

Production authoritative server matches do not currently use it as dynamic bot fill.

Do not rewrite `PlayerAi` first.

### Add

```text
src/Server/Bots/
    ServerBotManager.cs
    BotParticipant.cs
    BotFillPolicy.cs
```

### Bot identity

A bot is:

- server-owned
- no `NetConnection`
- normal player slot
- normal Hunter/TeamIndex
- normal score/stat participant
- marked as bot in roster/result

### Fill policy

Example:

```text
minParticipants = 4

1 human -> 3 bots
2 humans -> 2 bots
3 humans -> 1 bot
4 humans -> 0 bots
```

### Human joins

Retire a bot safely:

- prefer Waiting/Countdown
- otherwise next safe death/respawn boundary
- release/drop carried objectives correctly
- never delete a live objective state

### Practice

Private localhost Practice becomes:

```text
authoritative server
+
bot fill
```

No separate offline simulation.

### Bot skill model

After G1 timing normalization:

Tune with human-like parameters:

- reaction delay
- aim error
- aim tracking rate
- projectile lead error
- decision delay
- weapon-selection quality
- objective priority
- retreat thresholds
- movement mistakes

Avoid difficulty through:

- extra health
- extra damage
- impossible aim snaps
- hidden information

unless a custom mode explicitly asks for it.

### Personalities later

Possible:

- aggressive
- defensive
- objective
- sniper
- resource-control
- mobile

Do not block basic bot fill on personality work.

---

## G5.4 - Map telemetry

### Goal

Use authoritative data to improve maps/spawns/balance objectively.

### Collect

Event-based:

- kill/death positions
- damage positions
- spawn positions
- spawn-to-death interval
- weapon pickups
- objective interactions
- flag paths
- node captures
- Prime changes

Sampled:

- player position every 0.5-1.0 seconds for route heatmaps

### Do not

Write every player position every 60 Hz tick to PostgreSQL.

### Architecture

```text
Server TelemetryCollector
    ->
bounded in-memory batch
    ->
compressed per-match telemetry
    ->
asynchronous backend/local file
```

No allocations in the hot loop where avoidable.

### Tools

Add commands such as:

```text
Tools telemetry heatmap
Tools telemetry spawn-safety
Tools telemetry routes
Tools telemetry weapon-control
```

Outputs:

- PNG/SVG heatmaps
- JSON/CSV summaries
- map-space coordinates
- spawn danger table

### Key metrics

#### Spawn

- death within 3/5/10 seconds of spawn
- direct LOS at spawn
- average enemy distance
- repeated spawn frequency

#### Map

- kills by region
- deaths by region
- traversal usage
- dead/unused areas

#### Weapon

- pickup frequency
- possession-to-kill interval
- kills after pickup
- damage share

#### Objectives

- capture paths
- contest duration
- objective ownership
- defender/offender success

### Privacy

Prefer aggregate gameplay telemetry.

Do not expose precise persistent-player location history unnecessarily.

---

## G5.5 - Competitive ruleset

### Extend MatchRules

Add policy concepts, not ad hoc booleans everywhere.

Suggested:

```text
RulesetPreset
SpawnPolicy
LateJoinPolicy
OvertimePolicy
TeamBalancePolicy
SpectatorPolicy
RankingEligibility
RadarPolicy
```

### Classic

Preserve original mechanics/pacing as closely as practical.

### Competitive

Initial Competitive should enable fairness systems without changing weapon damage:

- enhanced Spawn Director
- spawn protection canceled by offense
- proper overtime
- balanced teams
- competitive late-join spectate
- verified-server requirement for RP
- server-controlled radar policy
- spectator delay as configured
- authoritative hit/kill feedback
- clean countdown/lifecycle

### Important

Do not initially change:

- weapon damage
- Hunter health
- Hunter speed
- charge times
- pickup times

Collect telemetry first.

Balance changes, if any, become a later versioned Competitive rules revision.

---

## G5.6 - Duel

Do not force Duel into the legacy content-layer `MatchMode` unless necessary.

The room/entity layer currently maps multiplayer modes to original game modes.

Prefer:

```text
Mode = Battle
Variant = Duel
```

or:

```text
RulesetPreset = Duel
```

### Duel defaults

- exactly 2 active players
- additional connections spectate
- clean 3-second countdown
- no mid-match late join
- enhanced Duel Spawn Director
- sudden-death overtime
- ranked bots disabled
- map pool
- rematch
- replay automatically recorded on ranked/official servers
- item timings initially Classic

### Future

Optional:

- best-of-N rounds
- map veto
- item timing practice overlay
- tournament ready checks

---

## G5.7 - Map voting and rematch

### Public/community server

At intermission offer a server-approved subset of rotation entries.

Vote never bypasses administrator map/rules allowlist.

### Duel/private

Offer:

```text
Rematch
Next map
Return to lobby
```

Tie votes use deterministic server RNG.

Do not let clients select arbitrary file paths/content.

---

## G5.8 - Tournament tooling

### Server controls

Add authenticated admin commands/API for:

- lock/unlock roster
- assign teams
- force spectator
- map selection
- ruleset selection
- ready check
- start countdown
- cancel match before start
- pause between rounds
- resume
- kick
- mute
- force replay recording

Avoid mid-simulation global pause unless a tournament rule explicitly defines it.

### Match identity

Tournament match gets:

```text
external tournament ID
round ID
Project Prime MatchId
```

### Result export

Produce:

- signed backend result
- JSON export
- replay path/id
- final scoreboard

### Observer support

Integrate with true spectator slots and spectator delay.

---

## G5.9 - Competitive telemetry feedback loop

After enough valid matches:

Evaluate:

- Hunter win rates
- Hunter pick rates
- K/D by Hunter
- damage/min
- weapon pickup/control rates
- weapon kills/damage
- map side/team bias
- spawn danger
- match duration
- overtime frequency

Only then consider Competitive balance tuning.

Every balance change must have:

```text
hypothesis
metric
baseline
change
validation window
rollback condition
```

No balance-by-vibes.

---

## G5.10 - G5 acceptance

G5 is complete when:

- spectators can use first-person, free, chase, and orbit modes
- true observer connections no longer consume player slots
- competitive spectator delay exists
- replay files can seek through indexed keyframes
- playback speed/timeline/event jumping works
- authoritative server can dynamically fill with Hunter bots
- bot removal for joining humans is safe
- telemetry produces useful map/spawn heatmaps
- Classic and Competitive rulesets are explicit
- Duel works as a 2-player variant
- public/private rematch/voting paths exist
- tournament operators can control roster/match/replay without editing files mid-event

---

# 5. Recommended implementation sequence

Do not implement each epic as one huge branch.

Recommended passes:

## G1

```text
G1.0 baseline
G1.1 timing normalization foundation
G1.2 render interpolation
G1.3 late-latched camera
G1.4 affliction reconciliation / protocol 8
G1.5 live input edge stress validation
G1.6 simulation-ordering tests
G1.7 Spawn Director 2.0
G1.8 lifecycle hardening
```

## G2

```text
G2.1 combat feedback bus
G2.2 hit/headshot/kill markers
G2.3 kill feed
G2.4 assist ledger
G2.5 death recap
G2.6 8-way damage direction
G2.7 Radar 2.0
G2.8 objective feedback integration
G2.9 audio readability
G2.10 post-match stats UI
```

## G3

```text
G3.1 overtime
G3.2 TeamAllocator
G3.3 late-join policy
G3.4 reliable world events
G3.5 weapon-selection UX
G3.6 network-quality HUD
G3.7 adaptive interpolation experiment
G3.8 server browser quality
```

## G4

```text
G4.1 PlayerId
G4.2 Backend project
G4.3 authentication/registration
G4.4 game join tickets
G4.5 verified server identity
G4.6 persistence-ready MatchResult
G4.7 durable match-report outbox
G4.8 PostgreSQL schema/migrations
G4.9 Hunter License domain
G4.10 Ranking Points/stars
G4.11 backend APIs
G4.12 Hunter License UI
G4.13 match history
G4.14 leaderboards
G4.15 integration/security tests
```

## G5

```text
G5.1 Spectator 2.0
G5.2 Replay 2.0
G5.3 server bot fill
G5.4 map telemetry
G5.5 Competitive ruleset
G5.6 Duel
G5.7 map vote/rematch
G5.8 tournament tooling
G5.9 telemetry-driven balance loop
```

---

# 6. Protocol release strategy

Recommended protocol milestones:

## Protocol 8

Complete before G3 release.

Contains:

- snapshot burn/disrupt remaining ticks
- structured kill/assist attribution if needed
- reliable world events
- G3 match-policy fields

Modern replay writer should record protocol-8 authoritative facts.

Replay reader keeps protocol 7 compatibility.

## Protocol 9

Reserve for G4 authenticated identity if protocol 8 has already shipped.

Contains:

- persistent PlayerId or secure session identity reference
- authenticated join-ticket integration
- bot/observer roster identity additions if not already safely represented

If G4 begins before protocol 8 ships publicly, it is acceptable to keep evolving protocol 8 instead. Avoid meaningless version churn.

---

# 7. Testing strategy

## 7.1 Unit tests

Pure logic should be isolated and tested:

- Spawn Director scoring
- overtime policy
- team allocator
- late-join policy
- damage angle quantization
- radar contact transforms
- network-health classifier
- Ranking Point thresholds
- rating delta table
- leaderboard tie ordering
- telemetry bucket mapping
- replay index search

## 7.2 Simulation tests

Use headless real-content simulation for:

- spawn safety
- simultaneous events
- overtime
- assists
- objective transitions
- bot fill
- Duel
- team balance

## 7.3 Network impairment tests

Use existing nettest/lag tooling.

Required matrices:

- latency
- jitter
- loss
- burst loss
- reorder
- stalls

Validate:

- input edges
- combat events
- world events
- adaptive interpolation
- spectator delay
- reconnect/late join

## 7.4 Render tests

At:

```text
60
120
144
165
240 Hz
```

Verify:

- simulation stays ~60 Hz
- interpolation has no state leak
- local look latency improves
- HUD scales correctly
- kill feed/radar do not overflow

## 7.5 Android

Real-device acceptance is mandatory before declaring HUD/radar/replay UI complete.

The existing emulator/SwiftShader path is insufficient for final visual acceptance.

Test:

- small viewport
- touch controls
- scoreboard
- radar
- kill feed
- spectator controls
- Hunter License
- replay timeline

## 7.6 Long-running server

Run:

- 8 players
- bots
- repeated rotations
- G4 backend temporarily unavailable
- telemetry enabled
- replay recording
- spectators

Verify:

- stable tick time
- bounded memory
- no queue growth
- outbox recovery
- no MatchResult duplicates

---

# 8. Performance budgets

These are design budgets, not promises to optimize without measurement.

## Server simulation

No G1-G5 feature may introduce unbounded per-tick work.

Target:

- no per-tick database calls
- no per-tick HTTP
- bounded combat ledger
- bounded telemetry buffer
- bounded event queues
- no LINQ-heavy hot loops in authoritative simulation where measurable

## Client HUD

- preallocate bounded feed/history structures
- do not build strings every draw when value has not changed
- avoid scanning all world entities multiple times per HUD widget
- prepare one HUD/radar data frame per simulation/presentation update

## Replay

- keyframe interval bounded
- seeking performs no normal rendering during catch-up
- index lookup logarithmic or direct bucketed
- file remains recoverable after imperfect shutdown as far as practical

## Backend

- transaction per authoritative match report
- indexed PlayerId/MatchId/rating queries
- cursor pagination
- idempotent submissions
- no synchronous dependency from gameplay server tick

---

# 9. Explicit non-goals for G1-G5

Do not add during these epics unless separately approved:

- rollback netcode rewrite
- peer-to-peer authority
- client-authoritative movement
- 120/240 Hz authoritative simulation
- ECS rewrite
- microservice explosion
- Redis by default
- message broker by default
- Kubernetes requirement
- weapon/Hunter balance changes before telemetry
- aim-assist system without an input-fairness design
- voice chat
- anti-cheat kernel driver
- broad namespace rename
- unrelated map rebuilds

---

# 10. AI implementation-agent instructions

For every pass:

1. Read the existing implementation before editing.
2. Reuse existing services rather than creating parallel systems.
3. Keep authority in Game/Server and presentation in Client.
4. Preserve protocol validation.
5. Add tests before or with behavior changes.
6. Keep allocations bounded.
7. Do not block the simulation thread.
8. Do not change Classic balance accidentally.
9. Do not discard unrelated working-tree modifications.
10. Keep commits narrow and reviewable.
11. Update relevant documentation in the same pass.
12. Run the complete relevant regression matrix before declaring the pass complete.
13. If a historical MPH behavior is uncertain, isolate the decision behind policy/data and document the uncertainty rather than inventing a fact.
14. If a proposed optimization cannot be measured, do not complicate the architecture to obtain it.
15. Prefer deletion and reuse over parallel abstractions.

---

# 11. Definition of done for the G1-G5 program

The G1-G5 roadmap is complete when Project Prime has:

## Feel

- smooth high-refresh presentation
- lower perceived local look latency
- trustworthy affliction presentation
- deterministic competitive edge cases
- safe modern spawning

## Combat clarity

- authoritative hit/headshot/kill feedback
- kill feed
- assists
- death recap
- improved damage direction
- Enhanced Radar
- objective/audio readability

## Match quality

- overtime
- proper team balancing
- mode-aware late join
- immediate objective/pickup presentation
- better weapon selection
- network-quality feedback
- proven interpolation policy
- improved server discovery

## Persistent identity

- accounts/guests
- immutable PlayerId
- Hunter License
- PostgreSQL-backed authoritative stats
- original 1-5 star progression
- Ranking Points ledger
- match history
- leaderboards
- verified-server trust

## Competitive/community platform

- complete spectator system
- indexed replay system
- server bot fill
- map telemetry
- Classic/Competitive rulesets
- Duel
- map voting/rematches
- tournament controls
- telemetry-driven balance process

At that point Project Prime is no longer simply a multiplayer-capable recreation. It has a coherent modern online FPS platform built around the original Hunters gameplay identity.
