# Momentum PR-00: audit and design lock

Audit date: 2026-10-19. Main was fetched from `AntiNotAnti/Project-Prime` and
resolved to `dc7f097067d755ec76d145fc7cc333068566863d`. The supplied plan's pinned
commit is still current. Work is isolated from the user's dirty
`feature/rmlui-primary-ui` checkout. No game movement, collision, wire semantics
or replay schema changes belong in PR-00.

## Protected gameplay contract

- Classic/Native remains the default, including absent saved or historical values.
- Momentum initially supplies only pure horizontal math, unreachable from gameplay.
  PR-02 may add a scene-owned offline developer opt-in. Public exposure waits for
  multiplayer/replay/device acceptance through PR-09. No local switch may override
  an online match. There is no new preference or production rollout flag in PR-00/01.
- Exactly one solver owns ordinary biped horizontal input and friction per tick.
  Native jumps, gravity, collision, special abilities, impulses, carrier movement,
  lifecycle fences and position ownership remain canonical.
- Simulation stays at 60 Hz (`FrameTiming.SimulationHz`, `StepSeconds`). Native
  `Speed` uses legacy per-30-Hz-step units: position advances by `Speed / 2` each
  60-Hz tick. A future adapter must explicitly convert units; feeding `Speed`
  directly to a units/second solver would be wrong by a factor of 30.
- No movement ACK stream, rollback, reconciliation, second collision engine,
  Unity dependency, unfinished Q3 solid mover support or anti-cheat enforcement.
- Bhop eligibility/landing state, total-speed impulse exemptions, Hunter-specific
  tuning and immutable online profile version are PR-03 work, not guessed here.
- Attribution/provenance policy is in [reference-attribution.md](reference-attribution.md).

This is an implementation design lock for review, not a claim of maintainer
approval, full original-cartridge parity or release acceptance.

## Native tick order and future integration seams

`PlayerEntity.Process` → `ModMovementShadowBeginFrame` → `ProcessInput` →
`ProcessBiped` or `ProcessAlt` → `ModMovementShadowObserve`.

Within `ProcessBiped` (`PlayerInput.cs:711` at audit HEAD):

1. Check frozen/health/script state; update aim. Collect directional controls into
   local `speedDelta` and animation flags. `AnalogScaleX/Y` preserves signed partial
   axes; digital-axis precedence is resolved earlier by input binding code.
2. Biped lock/stuck and jump-pad lock gate legal jump input. `UsedJump`, Space Jump,
   Prime and Hunter `Values.JumpSpeed` own the vertical jump impulse.
3. **Before adding this tick's `speedDelta`**, call `ProcessMovement` once.
4. Restore remote owner position through `NetHooks.AfterRemoteMovement`, refresh
   replicated aim, camera and shot input, process weapon and morph actions.
5. Add `speedDelta`, then apply horizontal magnitude cap only if the impulse
   increased speed. Preserve above-cap external speed rather than clipping it down.

Within `ProcessMovement` (`PlayerInput.cs:2634`): Q3 triggers → external acceleration
timer → speed/cap/facing basis bookkeeping → subtract jump-pad horizontal component
→ choose native alt/ground/air/slippery damping → apply **half** the native damping
delta (except Q3 launch gravity path) → restore jump-pad contribution → native
gravity half-step → position half-step → player collision → standing carrier
transform → reset contact flags → `ResolveMovementCollision` → `CheckCollision` →
Spire crest and landing bookkeeping. Surface, launch and form state affect this
order. `PlayerCollision` hulls and recovery remain authoritative.

PR-02 must preserve this Native order and prove its alternate branch does not run
both solvers. Simply inserting new acceleration before the existing damping or
after collision without an explicit ordering decision would change behavior.
`PlayerMovementShadow` currently compares only Samus/Spire stable windows at paired
60-Hz boundaries against a collision-free 30-Hz reference. It explicitly skips
contacts/transitions and is not seven-Hunter exact physics proof. Momentum must
not be labeled as Native reference drift.

## Conflicts and re-audit requirement

The live protocol is **57** (`NetProtocol.cs:2537`). PR-00 corrects current numeric
claims of 53 in the invariants and 41/38 in `CLAUDE.md`; historical descriptions
remain historical. Actual replay support is demo formats 1–6 (`DemoFile.cs:226`),
world checkpoint version 10 (`ReplayWorldCheckpoint.cs:25`), decoder checkpoint
default/current 12/13 (`ReplayReplicaCheckpointLayout.cs:18`), and gameplay hash
schema 4 (`ReplayStateHash.cs:14`). These versions remain unchanged here.

`MatchModifierFlags : uint` uses bits 0–13; bit 14 is unassigned at audit HEAD.
`SessionStatePacket.TryRead` rejects bits outside 16383 (`SessionProtocol.cs:79`).
`MatchStatePacket.RuleBits` is `ushort`: the candidate bit fits, but both masks and
every projection/copy/validation path need updating together in PR-05. No new
intent bits or streams are needed. The byte `PlayerState.Flags` is fully occupied.

Authoritative rules flow through `MatchDefinition.ApplyModifiers`, `SceneSetup`,
`GameSettings`, `NetRoomChange`, `NetSessionLobby`, `MatchStart` and
`ReplaySceneServices`. Extend the existing Balanced Mode propagation/copy paths
and lobby validation rather than inventing a client-local physics preference.
`PlayerReplicationBridge` preserves the local same-life body (`:760`), restores
remote accepted owner poses (`:1257`) and derives remote velocity from accepted
position differences (`:1278`). New math cannot change those authority boundaries.
Replay rules must apply before room construction; any persistent player state
requires deliberate world schema/accessor/hash compatibility, not just a rule bit.

Open PRs were enumerated before edits; full file inventories use paginated
`gh api repos/AntiNotAnti/Project-Prime/pulls/<number>/files --paginate` because
`gh pr view --json files` truncates large PRs.

| PR / head observed | Overlap and action |
| --- | --- |
| [#702](https://github.com/AntiNotAnti/Project-Prime/pull/702), `f4f14cd0671f64136f48b80d6db9adac646612ac` | Primary RmlUi/Studio split plus networking/presentation work. 1,228 files: PlayerInput, PlayerEntity, PlayerCollision, PlayerCamera, PlayerWallCling, MatchDefinition, NetProtocol, replay checkpoints, nettest runner and build workflow. Re-audit before PR-02 and PR-05/06/08; do not copy dirty working-tree changes. |
| [#561](https://github.com/AntiNotAnti/Project-Prime/pull/561), `65e4a1ef433036fed2c497954d500361e4c9fa94` | Radiant/map/Studio work; 218 files, including PlayerQ3Traversal, DedicatedServer, LobbyCommands, NetRoomChange, nettest runner and build workflow. Re-audit traversal and authoritative rule launch seams before integration. |

A pre-commit recheck found #702 had advanced to `b0518eed83347d38747696ac77a234e6941ab4b3`;
its initial overlap inventory remains a coordination warning. Main remained at the
audited SHA. Re-audit the current PR diff before integration.

PR-00/01 add isolated tests/docs/math and minimal runner/CI entries. They do not
reserve a live rule bit, change packet lengths or bump protocol early. Recheck
current constants and open PRs at each affected integration phase.

## Telemetry and rollout contract

Future bounded, opt-in telemetry: schema version, model/profile version, source
simulation tick, planar speed, wish speed, acceleration contribution, grounded and
legal-jump state, cap interventions, native contact correction/overflow and aggregate
step timing. No per-frame unbounded production logs or identifying player history.
Native-reference comparisons and Momentum observations are separate domains.
Movement admission remains advisory with `HardRejections = 0`.

Record source SHA, platform/CPU, build, room, Hunter/player count, sample count,
render cadence and network impairment with each experiment. Solver-only throughput
does not establish the integrated p95 simulation budget (provisional +5%).

## Native golden fixture and acceptance evidence

The test-only native baseline harness and its checked-in value fixture are described
in [pr00-results.md](pr00-results.md). Positions and velocities are measurements
from the existing native simulation using the user's local extracted assets; no
models, maps, textures or cartridge data are committed. The tests do not assert
all maps, multiplayer, controller/touch or historical cartridge fidelity.

Remaining phases: PR-01 pure math; PR-02 offline biped adapter; PR-03 jump/tuning;
PR-04 collision/specials; PR-05 authoritative rules/protocol; PR-06 replays;
PR-07 inputs/bots; PR-08 lobby/offline UI; PR-09 release acceptance. Optional
surf/crouch work remains outside v1.
