# Current Project Prime architecture

Status: authoritative working-tree reference, 2026-09-11. Source and focused
tests are the authority when this summary conflicts with a historical plan.
This document does not claim a deployed service, physical-device run, or WAN
acceptance.

## Ownership boundaries

```text
Backend
  |
Persistent Server Node
  |-- sessions, lobbies, chat, ready state, handoff, administration
  |
Worker pool
  |-- one MatchInstance per match
      |-- authoritative simulation and gameplay UDP

Client
  |-- launcher/control session
  |-- live ScenePresentation
  |-- replay/theatre and killcam presentations
```

The Backend and Server Node own control-plane state. Workers own mutable match
state and run one authoritative writer at a fixed 60 Hz. Gameplay datagrams go
directly to the Worker; reliable control traffic stays on the Node connection.
Each `MatchInstance` owns its state, RNG, queues, and resources. No match may
read or mutate another match's state.

`Game` contains simulation, protocol, and content contracts. `Client` owns
presentation and platform integration. Shared map/replay contracts are
immutable at handoff where practical; renderer and SDL ownership do not cross
into the authoritative simulation.

## Presentation and input

The live scene remains authoritative for local gameplay. Replay and killcam
scenes consume copied/frozen state and never rewind or replace the live scene;
they do not create a second SDL device or window. `SdlGameHost` supplies current
drawable/framebuffer size and propagates resize to the active auxiliary
presentation. Killcam skip is a logical one-shot command at the input boundary.
On handoff, live keyboard, mouse, controller buttons, sticks, and triggers are
quarantined; held physical input stays neutral until release.

The current renderer uses the SDL GPU backend through the existing
`IRenderToolHost` utility boundary. Render interpolation owns presentation
histories only. Consumed gameplay orientation stays at the newest simulation
orientation; captured camera position and FOV can be presented between ticks.
`SmoothCamSeqHandoff` remains default-off without live sequence evidence.

## Identity and observation

`CombatActor` (slot, connection, life) is the exact identity for client-side
observation history, broadcast scoring, awards, feeds, and director focus.
Slot numbers are only storage/indexing hints. Recent actor history is bounded
and safe across slot reuse; an absent or stale identity is not recovered from
the current authoritative singleton.

## Maps and persistence

Map acquisition retains resumable partial files after cancellation/transient
failure and never installs an unverified partial. Exact artifact requirements
are serialized by a process-wide keyed, reference-counted lock shared by all
service instances; ownership is rechecked after acquisition. The lock registry
does not share map payloads or match state across scenes.

PostgreSQL is a durable control-plane dependency. Gameplay must not block its
60 Hz loop on database I/O, and report delivery remains idempotent. Disposable
database evidence is distinct from a configured or deployed database.

## Evidence boundary

The release-gate ledger in `CURRENT_RELEASE_GATES.md` records what was actually
run. Historical plans and baseline reports are context only; they must not be
used as current architecture instructions without checking this document.
