# Current Project Prime architecture

Status: **CURRENT** working-tree reference, last reviewed 2026-09-12. Source,
evaluated project files, and focused tests are authoritative when this summary
conflicts with a historical plan. This document does not claim deployment,
physical-device acceptance, or WAN acceptance.

## CURRENT: ownership

```text
Backend
  |
Persistent Server Node
  |-- sessions, lobbies, chat, ready state, handoff, administration
  |
Worker pool
  `-- one MatchInstance per match
      `-- authoritative fixed-60-Hz simulation and direct gameplay UDP

Desktop Client head                    Android head
  |-- SDL window/input/GPU host          |-- Android lifecycle/input/EGL host
  |-- desktop secure/update adapters     |-- Android secure/update adapters
  +------------------+-------------------+
                     v
             Client.Presentation
              |-- launcher/Avalonia UI
              |-- scene/HUD/audio/replay
              |-- client networking
              +--> Renderer
                     |
                 Client.Core
              |-- account/session state
              |-- portable input/settings
              `-- Node control/runtime state
                     |
                    Game
```

The exact direct project-reference graph is recorded in `PROJECT_LAYOUT.md`.
The Backend and Server Node own control-plane state. Workers own mutable match
state and run one authoritative writer at a fixed 60 Hz. Reliable control stays
on the Node connection; gameplay datagrams go directly to the selected Worker.
Each `MatchInstance` owns its state, RNG, queues, and resources. No match may
read or mutate another match's state.

Game owns simulation, protocol, and content contracts. Client.Core owns
portable client policy and runtime state. Client.Presentation owns the shared
picture and user-facing state. Renderer owns backend-neutral render resources
and the desktop SDL GPU backend. Client and Android are platform heads.

## CURRENT: presentation, timing, and input

The live scene remains authoritative for local gameplay. Replay and killcam
presentations consume copied or frozen state and never rewind or replace the
live scene. They share the existing native host rather than creating another
SDL device or window.

On desktop, `SdlGameHost` owns SDL lifetime, window state, native event polling,
GPU surface ownership, and presentation transitions. It translates input into
portable snapshots before `GameWindowFrameLoop` advances `FrameTiming`, fixed
simulation steps, presentation construction, backend submission, successful
presentation acknowledgement, and frame cleanup. The extracted SDL window,
input, pointer, and gamepad helpers remain owned by the host thread.

Android retains its Android-owned lifecycle and EGL surface loop. It consumes
the same Client.Core and Client.Presentation assemblies but supplies Android
touch, controller, storage, update, and surface adapters. Its presentation
acknowledgement advances only after a successful EGL swap.

Render interpolation owns presentation histories only. It samples completed
simulation ticks and changes copied submissions or frame-local presentation
state, never authoritative positions, aim, collision volumes, node references,
or protocol state. Current simulation orientation remains the consumed gameplay
orientation; camera translation/FOV and allow-listed presentation poses may be
resolved using `FrameTiming.RenderAlpha`. `SmoothCamSeqHandoff` remains
default-off without live sequence evidence.

Prime-owned key, mouse, controller, pointer, stylus, and look values are the
portable input vocabulary. SDL and Android adapters translate platform events
at the edge. During presentation handoff, live buttons, sticks, triggers,
pointer movement, and text are quarantined; held physical input remains neutral
until released.

## CURRENT: identity, maps, and persistence

`CombatActor` (slot, connection, life) is the exact identity for client-side
observation history, broadcast scoring, awards, feeds, and director focus. Slot
numbers are only storage/indexing hints. Recent actor history is bounded and
safe across slot reuse; an absent or stale identity is not recovered from a
process-global current session.

Map acquisition retains resumable partial files after cancellation or transient
failure and never installs an unverified partial. Exact artifact requirements
are serialized by a process-wide keyed, reference-counted lock; ownership is
rechecked after acquisition. That registry shares neither payloads nor match
state across scenes.

PostgreSQL is a durable control-plane dependency. Database or network I/O must
not block the authoritative 60 Hz loop, and report delivery remains idempotent.
Disposable database evidence is distinct from a configured or deployed
database.

## TARGET: invariants

- Keep simulation and portable client state independent of native hosts.
- Keep one authoritative representation and one writer per match.
- Keep client presentation replaceable without changing protocol or gameplay.
- Keep native resources owned and disposed by their platform head.
- Replace compatibility seams only when their consumers can move together;
  never create a second permanent architecture during migration.

## TEMPORARY EXCEPTIONS

Client.Presentation's Android build is an explicit opt-in
`net10.0-android36.0` evaluation. It exists because the current GLES/audio
presentation branches are substantial; ordinary and desktop evaluation remains
single-target `net10.0`. Android restores that opt-in graph non-recursively in
a dedicated `Client.Presentation/obj/android` intermediate directory so neither
platform can overwrite the other's NuGet assets. Two guarded Renderer GLES implementation files are
still source-linked into that Android evaluation. These are recorded migration
exceptions, not permission for Android or native APIs in Client.Core.

Some presentation boundaries retain OpenTK-compatible key/mouse value types
after SDL/Android translation. They do not own a native window or graphics
context and remain bounded by the project guards.

## Evidence boundary

On 2026-09-12, the content-free desktop runtime smoke ran locally on Apple
Silicon macOS, resolved its application path and secure-session provider,
bootstrapped Avalonia, then created the hidden SDL GPU/Metal surface, submitted
one frame, and disposed successfully (`driver=metal`, `surface=32x32`). This proves
the bounded local native lifetime/submission path only. Windows and Linux smoke
are scheduled in CI but are not claimed here without their job results.
Android emulator history is useful lifecycle evidence, not physical Android
rendering, input, performance, or driver acceptance.

`CURRENT_RELEASE_GATES.md` records current validation. Historical plans and
baselines remain evidence for their stated snapshots, not current architecture
instructions. `.claude/KNOWN-GAPS.md` is the structured ledger for every open,
partial, fixed, superseded, unreproduced, or device-dependent claim.
