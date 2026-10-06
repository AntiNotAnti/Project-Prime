# Project Prime Studio architecture and migration

Reviewed baseline: `fcf311ccfc2031a07598860295db03eea330ff6e` (`main`),
October 6, 2026. The checkout was clean before this work. The source tree and
executable checks remain authoritative over this plan and older audits.

## Current migration status

The independent desktop application is implemented and editor/service extraction
is underway. Existing embedded editors remain available until standalone
acceptance gates pass. Source-inspection placeholders identify their incomplete
host and expose `CanSave=false`; real authoring hosts derive save/dirty state from
the canonical document. Opening a path alone is not evidence of replay playback
or full authoring parity.

| Phase | Scope | Acceptance gate | Current status |
| --- | --- | --- | --- |
| 0 | Baseline and architecture contracts | Original map/replay checks, recorded captures/hashes/measurements, source contracts | Source inspection and contracts added; executable evidence recorded below |
| 1 | Independent desktop executable | Start game/Studio in either order; duplicate game rejected; duplicate Studio forwards; independent shutdown | 118 native macOS game/editor integration checks passed; remaining desktop platform acceptance pending |
| 2 | Shared shell | Asset-free home, recent paths, workspace documents, commands, dock layout, jobs, clean shutdown | 682 headless shell/Map/Replay, native Home and full default native Replay 139 checks passed; remaining enhanced workflows and platform acceptance pending |
| 3 | Map extraction and decomposition | Same `MapDocument`, open/save/recovery/history/import/modeling/UV/materials/build/package/Community/four-view parity | Canonical document UI workflows, 114 native Map lifecycle/render and 118 native game/editor integration checks passed; Community and remaining enhanced editor acceptance pending |
| 4 | Cross-process publication | Game holds a map lease; Studio install cannot change files; retry after release installs exact package hash | 53 independent-process fixtures pass, including isolated private Replay readers; native game/editor 118 also proves live-scene publication rejection, unchanged runtime bytes and exact stop/rebuild/restart |
| 5 | Game/Studio IPC | Versioned authenticated current-user endpoint; failure/reconnect/cancellation/duplicate-ID checks | 60 authenticated IPC, 34 canonical broker and 118 native game/editor integration assertions passed; remaining platform acceptance pending |
| 6 | External map playtest | Unsaved map packaged privately; exact identity verified; Studio selection/history/layout survive; stale response rejected | 118 native game/editor integration checks passed, including dirty canonical document retention, exact new-package restart, stale stop rejection and independent crashes |
| 7 | Replay extraction | Private `PassiveReplayPlayer`, deterministic seeks, clips, cameras, diagnostics, export, historical custom-map identity | 94 headless and full default native 139 assertions passed; camera/source binding, v5 persistence and final bounded private-cache retention acceptance pending |
| 8 | Replay presentation host | Explicit update/render/resize/dispose lifecycle; unpublished failure leaves current document/world intact | Native 139 proves HUD, one-clock multiview, three-hash retry and continuing Save As viewport; additional stale-fourth-view cleanup/resource edge pending |
| 9 | Dedicated Studio renderer | Shared modern backend, retained editor resources, DPI contract, resource release and picking parity | 34 retained GPU/M2/picking/device-loss, 114 native Map lifecycle and 97 dense-redraw checks passed; complete native 1440p frame timing acceptance pending |
| 10 | Map enhancements | Reviewed panels/workflows, GPU picking, import/build diagnostics and measured dense-map behavior | Canonical panels, native ID picking and retained metrics implemented; dense-map and native parity acceptance pending |
| 11 | Replay enhancements | Synchronized multiview, camera curve graph, annotations, export queue and audio ownership | Session-owned views, camera/annotation/export facade and tools implemented; feature/export/audio acceptance pending |
| 12 | Shared creator enhancements | Multiple documents, layouts, asset/source browser, command palette, recovery and diagnostics | Source-backed search, configurable commands and measured diagnostic HUD implemented; native modes/HUD 116 passed; final document-scoped diagnostics and editor acceptance pending |
| Release | Packaging/updater/associations | Windows/Linux/macOS packages and compatibility checks; Android excludes Studio | Paired metadata/lifetime guards and 85 updater checks pass locally; earlier Android Release APK passed, final source/APK rerun pending; desktop platform CI/release acceptance pending |
| Cleanup | Remove legacy embedded routes | All standalone map/replay acceptance gates proven first | Deferred |
| Later | Optional selective shared assembly extraction | Small compiler-enforced boundaries without namespace/engine rewrite | Optional follow-up after migration acceptance |

## Product and process boundaries

`ProjectPrime` owns gameplay, network sessions, lobby/Community browsing, Theatre
and quick replay viewing. `ProjectPrimeStudio` owns the Avalonia Desktop creator
window, authoring workspaces, timeline/camera tools, background jobs and recovery.
Both consume one authoritative engine, map compiler/package implementation,
replay format and simulation. The `MphRead` root namespace remains unchanged.

```text
ProjectPrime                          ProjectPrimeStudio
  game shell                           Avalonia Desktop lifetime
  live sessions                        creator documents and jobs
  lightweight Theatre                  map/replay presentation owners
  runtime map publication              private build/cache/staging
          |                                   |
          +--- authenticated local IPC -------+
                        |
             immutable package + exact hashes
```

Studio may consume the canonical `MphRead` project while the process boundary is
established. A game assembly reference is not permission to acquire the game shell
or a live session. The small `ProjectPrime.Studio.Protocol` project remains
framework-only and can be shared without introducing UI, engine ownership or a
gameplay socket. Its one external source link compiles the same internal,
BCL-only `DesktopInstallationIdentity.cs` helper into both assemblies; it does
not introduce an engine assembly dependency. Host/service extraction exposes existing implementations through
reviewed contracts; it must not copy the engine into Studio or add the game
launcher as the creator application shell. Selective assembly extraction is an
optional follow-up after process and host boundaries, feature parity, renderer
separation and reduction of static ownership.

The Studio application starts through `StartWithClassicDesktopLifetime`; its
window belongs to Avalonia. It may later host native GPU viewport surfaces with
explicit owners. It must never call `Shell.Run`, embed `UiSurface`/`UiTopLevel`,
use `RenderWindow` as its application shell or start a live `NetSession`.

The map presentation contract distinguishes native `Present` from explicit
offscreen image rendering. The normal `IStudioNativeMapPresentation` path draws
retained meshes into the Studio-owned native surface and uploads CPU-authoritative
editor overlays; it does not read the GPU world back into Avalonia's bitmap.
Image-returning `Render` remains available for captures/fallback hosts. Runtime
acceptance must verify normal-frame `ReadbackBytes=0`, mesh reuse for camera/
selection changes, pixel/DPI agreement, picking parity and explicit resource
release; source presence alone does not establish those results.

## Existing ownership that must be preserved

### Map authoring and compilation

The authoring source of truth is the existing
[`MapDocument`](../../src/MphRead/Mods/MapEditor/MapDocument.cs). Dirty state uses
saved/current state IDs; common edits retain bounded delta/coalesced history.
Selection/camera/entity invalidation and retained viewport meshes are already
implemented. Extraction must preserve these behaviors instead of reviving older
serialization-based history or whole-map rebuild designs.

[`MapBuildScheduler`](../../src/MphRead/Mods/MapGen/Build/MapBuildScheduler.cs)
already accepts detached `MapBuildSnapshot` values, deduplicates bounded work,
shares private compiled geometry, validates dependency fingerprints and publishes
immutable disk cache entries under a cross-process `FileShare.None` cache lease.
That cache lease is not a game runtime lease.

At the reviewed baseline, the embedded editor called `MapBuildScheduler.Publish`, which entered
[`MapRuntimeUsage.Gate`](../../src/MphRead/Mods/MapGen/Project/MapRuntimeUsage.cs),
requires installation to be allowed, invalidates prewarm and then installs.
`MapRuntimeUsage` then tracked scenes/preparations in process memory and checked the live
network session. A Studio process could not see those readers. Moving the same call
into Studio would therefore create an unsafe publication path even though its
current use inside ProjectPrime is correct.

Studio builds under `<UserData>/studio/{build-cache,staging,playtest}` and produces
portable immutable `.ppmap` archives. ProjectPrime alone validates and publishes
archives into runtime directories used by ProjectPrime. Preserve the exact tuple
`MapId`, `ContentHash`, `PackageHash`; a matching map name is insufficient. An
OS-level publication lock keyed to installation/user complements the game's
existing in-process usage fence. Broker regression checks must verify byte-for-
byte unchanged active runtime files on rejected/deferred publication.

Map presentation currently lives in `MapStudioScreen` partials, `MapViewport`
partials and launcher controls. Their dependencies include overlays, foreground
file-drop events, viewport GPU scheduling, launcher file dialogs and in-process
playtest transitions. Extraction requires coherent workspace/panel owners and
ordinary Avalonia dialogs/storage; copying the large screen unchanged does not
satisfy the gate. Autosave/recovery must survive game, Studio, OS, playtest and
renderer failures.

`IMapStudioHostServices` now separates the authoring control from modal/file-drop,
Community-ticket, build publication, package installation, job and playtest
services. `AvaloniaMapStudioHost` receives that service explicitly and owns the
canonical control/document lifecycle; `MapStudioDocument` exposes it to the
desktop shell. The embedded constructor and game services live in the exact
legacy adapter listed below. This establishes a host boundary; reuse of the
shared screen does not itself complete the requested panel decomposition or
standalone feature-parity acceptance.

### Replay playback and presentation

[`PassiveReplayPlayer`](../../src/MphRead/Mods/Network/PassiveReplayPlayer.cs) is
the canonical private playback owner. Its instance-owned reader/session/transport,
replica scene, player/match/RNG state, mutable resources and checkpoint cache
already exist. Production Studio playback and killcams already use it. There is
no justification for replacing this architecture or adding live session ownership
to creator UI. The standalone `MphRead.Mods.StudioReplay.StudioReplayPlayer`
facade now owns this same player; `ReplayStudioSession` and
`ReplayStudioDocument` own presentation and disposal per document. The facade
does not use the foreground `DemoPlayback` or game shell.

[`ReplayPlaybackSession`](../../src/MphRead/Mods/Network/ReplayPlaybackSession.cs)
belongs to a player/session instance. Foreground `DemoPlayback`, static analytics
caches and the current global export queue are migration seams, not APIs for the
new workspace to depend on. `ReplayStudioSession` owns one private player,
camera/sidecar/annotations, presentation resources and cancellation/disposal path
per document. Bounded reconstruction publishes a new scene only after success.

Export uses the canonical player/sampler/encoder through persisted native worker
processes. Each worker owns its private replay copy, Avalonia lifetime, graphics
device and installation lease through graphics shutdown. The window coordinator
observes at most two workers, exposes central job progress and sends an explicit
persisted cancellation signal. Window shutdown detaches observers without
cancelling export children; restart observes live statuses and resumes queued
tickets without starting duplicate writers. The reduced native worker gate now
proves actual encoder/cancellation and document/window-close behavior; the full
default native Replay gate now also proves HUD pixels, synchronized multiview and
continuing clip presentation. Later camera/source binding and cache retention
acceptance remains separate.
The focused component checker uses FFmpeg testsrc fixture pixels, separately
from native replay viewport captures.

`StudioReplayBundles` materializes the canonical replay and records its exact
SHA-256, historical map/room/content/package identity, camera keys, reel and
annotations. Import checks the frozen identity before rebinding sidecars. A
same-name room is not a replacement for the recorded package identity.

Keep existing supported replay adapters, durable checkpoint fallback, frozen clip
leases, killcam isolation and exact custom-map identity. Gameplay remains fixed at
60 Hz. Export at 120/144 FPS samples fractional presentation time; it must not
increase gameplay stepping frequency or advance authoritative combat.

## Local integration contracts

Studio single-instance coordination is distinct from the game's existing
interactive-client guard. Scope it to installation/user, hold ownership through
shutdown, and forward map/replay/clip/recovery requests to the owning Studio. A
second process must receive an acknowledgment or a useful error; it may not
silently discard the requested document. Game and Studio windows have independent
lifetimes and crashes.

The game updater's installation lease and local IPC endpoint use the same
canonical installation identity helper. It resolves directory aliases and
paired macOS bundle parent directories before platform case normalization.
The Protocol project compiles only that exact BCL path helper; role-specific
game and Studio endpoints remain separate. The current IPC fixture passes 60
checks, including real symlink forwarding, paired bundle identity, delayed
startup, stale endpoints and capability rotation.

The game/Studio IPC version is separate from `NetConfig.ProtocolVersion`. Use a
current-user named pipe or equivalent Unix local endpoint, bounded length-prefixed
messages, a cryptographically random launch secret, explicit version handshake,
request IDs and cancellation. Do not add localhost HTTP/TCP listeners. Large
content moves as immutable files plus identity hashes. Reconnect/disappearance,
duplicate IDs, oversized/malformed messages and stale playtest results must have
tests before the game broker is accepted.

The game-owned `GameStudioBroker` prepares packages on workers and dispatches
commit/launch mutations to `GameStudioIntegration`'s bounded game-owner queue.
It verifies prepared identity and the installed exact identity, reports busy
publication as deferred and validates `ppm1.` before returning a Community
ticket. Game/Studio sources share the framework-only local pipe implementation;
Studio cannot instantiate the privileged game broker in its own process.

`MapPublicationLease` keys durable lock files to canonical runtime root,
runtime namespace and room identity. Preparation/live scene owners hold shared
OS leases; `MapPackageInstaller.Commit` and `MapBuildScheduler.Publish` acquire
an immediate exclusive lease before replacing package/runtime outputs. Unix uses
`flock`, Windows uses `LockFileEx`; owner crash releases kernel leases. Existing
`MapRuntimeUsage.Gate` and installation checks remain. The
`tools/map-publication-check` fixture exercises independent readers/publishers,
unchanged rejected outputs, release/retry, crash release, case/symlink identity
and runtime namespaces. Its 53 passing assertions use deterministic ZIP/runtime
fixture files and the actual fence/file publication implementation. The added
private Replay scope fixture opens and hashes historical payload bytes in a
separate process while same-room game publication succeeds. Ordinary game reader
deferral and exact-hash retry remain covered. The fixture does not claim decoded
game `.ppmap` or live-scene acceptance; its log is
`/tmp/project-prime-map-publication-private.log`.

Community publication continues using the existing narrow, short-lived `ppm1`
ticket. Raw Hunter License/Supabase refresh/access credentials do not cross Studio
IPC or go to the Community map service. Studio IPC changes do not justify a
gameplay network protocol bump.

## Executable source contracts

```sh
dotnet run --project tools/studio-architecture-check -c Release
dotnet run --project tools/studio-architecture-check -c Release -- --self-test
```

The [architecture checker](../../tools/studio-architecture-check/README.md)
checks Studio and protocol sources and project dependencies. It blocks direct
game shell/session owners, foreground replay facades, game runtime publication,
Android targets/code, linked game presentation sources and game-to-Studio
executable dependencies. Studio may reference only the canonical engine and
protocol projects; the protocol cannot reference engine/UI projects or packages.
There are no direct Studio-source exceptions for prohibited owners. Any
engine-side migration adapter exception must record its exact source file,
permitted symbol, purpose and removal gate, and cannot transfer game shell or
live-session ownership into Studio.

| Exact compatibility source | Permitted dependency | Purpose and removal gate |
| --- | --- | --- |
| `src/MphRead/Mods/Launcher/Gui/MapStudioLegacyHost.cs` | `Shell`, `PrimeOverlayHost`, `LegacyMapStudioHostServices`, `CustomRooms`, `MapPackageInstaller`, `MapRuntimeUsage`, `MapBuildScheduler.Publish`, narrow `HunterLicenseClient` ticket methods | Embedded game editor drop events, overlay dialogs, narrow Community ticket acquisition and existing fenced publication. Standalone supplies `IMapStudioHostServices` through the native adapter and cannot instantiate this service. Remove after standalone parity and external creator-route acceptance. |
| `src/MphRead/Mods/Launcher/Gui/MapViewportGpu.cs` | `UiSurface` | Existing embedded viewport invalidation/composition during renderer transition. Standalone owns its native graphics surface. Remove after replacement of embedded creator presentation is accepted; current runtime GPU diagnostics remain independent. |
| `src/MphRead/AvaloniaShared/StudioPrivateMapRuntime.cs` | `MapBuildScheduler.Publish` with explicit private output directories | Publish canonical build outputs only under the Studio runtime root. Canonical/symlink-aware destination guards reject root equality, ancestor/descendant overlap, case aliases and escaped destination links; guards rerun before each publish. Studio UI cannot invoke publication directly. Retain the isolated-output service or extract it into the eventual map-core boundary after path-isolation regression acceptance. |
| `src/MphRead/AvaloniaShared/StudioGameAssets.cs` | `CustomRooms.DeferInitialRegistration` only | Suppress inherited metadata static registration when initializing the authoring process. This does not grant catalog registration, installed-package mutation or publication; exact member access is checked. Replace this switch when metadata initialization becomes explicitly scoped. |

All other shared `MapStudio*` and `MapViewport*` production files are audited,
along with public `MphRead/AvaloniaShared`, `MphRead/Mods/StudioReplay` and
`MphRead/Mods/StudioRendering` facades and exact modern renderer Studio extensions.
The existing `MapViewportCheck.cs` is an in-game diagnostic, not a standalone host,
and is retained as the baseline oracle. A new helper filename does not inherit
an adapter exception. `StudioReplayPlayer` must directly own the canonical private
player; foreground `DemoPlayback`/`ReplayController` cannot enter that facade.

The checker is a lexical/project-source contract, not a C# semantic or IL audit;
it does not evaluate arbitrary imported MSBuild targets. Runtime/editor parity
and security still require their own tests. A passing empty Replay workspace
does not prove private playback. Extend the adapter ownership contracts and
deterministic fixture checks as actual editor hosts replace the placeholders.

## Baseline command inventory

Run from the repository root with .NET 10. The asset-free suites use synthetic
temporary fixtures and must remain available throughout migration:

```sh
dotnet run --project tools/map-editor-check -c Release
dotnet run --project tools/map-editor-check -c Release -- --next-pass-only
dotnet run --project tools/map-editor-check -c Release -- --model-import-only
dotnet run --project tools/map-editor-check -c Release -- --collision-only
dotnet run --project tools/map-editor-check -c Release -- --community-only
dotnet run --project tools/map-editor-check -c Release -- --runtime-only
dotnet run --project tools/map-multiplayer-check -c Release -- /tmp/prime-map-acceptance
dotnet run --project tools/replay-timeline-check -c Release
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -replaycontrolcheck
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -replayformatcheck
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -replayuicheck
```

`map-multiplayer-check` requires a new empty output directory. It creates its own
map packages, isolated runtime/library, loopback Community fixture and child
server/client/replay workers; it preserves logs and packages for inspection.
Do not use a user's installed runtime map library for the fixture.

The existing map GPU path requires a working desktop OpenGL context. These
captures exercise the existing editor before the standalone renderer is extracted:

```sh
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -mapstudioshot /tmp/prime-map-ui
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -mapviewportcheck /tmp/prime-map-gpu
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -renderbackendcheck
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -rendergraphcheck
dotnet run --project tools/map-editor-check -c Release -- --renderer-retention-only
```

`mapstudioshot` provides 1440×900 and 960×600 UI layouts. `mapviewportcheck`
captures synthetic viewports at 960×600, 1440×900 and 2880×1800, GPU previews,
overlay order, full editor, four-view mode and map library. It checks pointer/DPI
agreement, stable mesh uploads and resource cleanup. `-mapproject FILE` adds a
real imported-map draw/raster microbenchmark. Linux CI uses Xvfb/Mesa with a
3840×2160 screen. Keep these tests while adding Studio-hosted equivalents.

Asset-backed replay checks need tester-supplied extracted game data configured
through `paths.txt`. Synthetic world fixtures are generated from those local
assets; the repository does not contain redistributable game data or canonical
asset-backed replay files.

```sh
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -replayworldcheck synthetic -output /tmp/prime-replay-world
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -replayreplicacheck RECORDING.ppdemo -shots /tmp/prime-replay-replica
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -replaytheatrecheck RECORDING.ppdemo -shots /tmp/prime-replay-theatre
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -replaydeterminism RECORDING.ppdemo -replayhashout /tmp/prime-replay-reference.ppdemo
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -replaydurablecheck RECORDING.ppdemo -output /tmp/prime-replay-durable
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -replayexportcheck RECORDING.ppdemo -output /tmp/prime-replay-export
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -replayshot /tmp/prime-replay-ui -demo RECORDING.ppdemo
```

Determinism captures explicit versioned gameplay hashes separately from
animation/trail/particle projections. Archive the source replay SHA-256, engine
SHA, hash schema, package identity and generated reference together. PNG file
hashes are useful artifact identities, but GPU image acceptance should inspect
the image or use the existing pixel checks rather than assume cross-driver byte
identity. See the [replay command inventory](../../.claude/multiplayer/NETWORK-DEMOS.md)
and [map authoring inventory](../../.claude/mapgen/MAP-STUDIO.md).

## Measurement and evidence status

Historical measurements in
[`replay-map-performance.md`](replay-map-performance.md) are prior engineering
evidence, not fresh results at this baseline. The five measured native Home starts
are below the requested 1.5 seconds. Loaded-editor startup and 60+ FPS at 1440p
remain targets until measured.

Existing component measurement commands are:

```sh
dotnet run --project tools/map-editor-check -c Release -- --benchmark
dotnet run --project tools/map-editor-check -c Release -- --map-benchmark
dotnet run --project tools/map-editor-check -c Release -- --large-model-benchmark
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -replaybenchmark RECORDING.ppdemo -output /tmp/prime-replay-benchmark
```

`map-benchmark` reports strict package allocation/GC/RSS, catalog list latency and
disk-cache cold/warm behavior. Replay benchmark writes inspectable JSON and
indexed/unindexed local copies, checks equivalent gameplay hashes and measures
seek work, CPU/allocation, timeline retention and private world memory. These are
component costs, not end-to-end FPS or native/GPU memory totals.

For the new application record cold/warm launch-to-usable-shell time, process RSS,
managed retained memory, document-open/close deltas and GPU resource counters with
OS, SDK/runtime, hardware, display scale, revision, fixture identity and warmup
method. Use multiple runs and report median/tail values. Viewport acceptance
tracks CPU/GPU frame time, draw calls, mesh/texture uploads and picking cost.
Closing documents must release their resources; closing Studio must release all
native owners on the correct thread.

`StudioPerformanceHud` reads immutable renderer reports, canonical replay player
performance/status, the selected map scheduler and the actual Studio job manager.
Its optional owned native window does not resize a viewport. It samples every
500 ms while attached and visible, releases providers on disposal and does not
initialize a graphics device. Map reports belong to the selected document and
return unavailable after resource release or device loss; Replay timings belong
to that document's player. Map counters distinguish cumulative vertex upload bytes from
resident vertex/uniform storage, frame readback from accumulated one-pixel picking
readback, and submitted draw/batch/primitive counts. Map renderer CPU submission
timing covers retained graph preparation, GPU uploads, submission and surface
presentation; it excludes the preceding canonical CPU overlay rasterization and
is labelled as a component measurement. Replay exposes actual nullable
seek/advance/render times and checkpoint cache count/bytes. GPU timestamp timing,
replay GPU counters and checkpoint capture timing remain explicitly unavailable
where their canonical implementations do not measure them.

The new canonical `MapModelingEnhancements` module proposes detached meshes for
proportional falloff, uninterrupted quad-ring cuts, shared-vertex world-plane
knife cuts, segmented boundary bridging, Coons grid caps, constant-width planar
region inset, manifold endpoint-fan bevel and ordered mirror/array evaluation.
The existing topology, validator, world transforms and geometry compiler remain
authoritative. Closed input must remain closed and consistently wound; invalid
input/output never commits a document history command. Restrictions are explicit:
quad loops cannot cross poles, bridge loops need equal corner counts, grid caps
and constant-width inset need convex planar boundaries, and mirror source must
lie on one side of its local plane.

Prefab authoring uses `MapPrefabMetadata`, `MapPrefabInstances` and the same
`MapDocument` history/snapshot path. Source updates retain member/material identity
and explicit overrides; incompatible local type changes require detaching before
update. Worker preparation freezes document state plus file/base/source/bundle
context, stages generated assets privately, and owner-thread adoption rejects
stale state or Save As races before mutation. Runtime packages contain resolved
objects/resources and remove external prefab provenance; compilation does not
require the source prefab file. `MapStructuralDiff` compares typed canonical
objects, materials, gameplay resources, navigation, environment and prefab state.
The focused canonical fixtures pass 46 checks, including Save As context races,
local type-conversion preservation and compiler/package independence after the
prefab source disappears. Native prefab panel integration remains separate.

`MapMesh.ModifierSource` stores resolved runtime geometry together with one
self-contained original source and an ordered, typed modifier list. Repeated
stack edits reevaluate that original and cannot form recursive provenance chains.
The stack is bounded to 32 modifiers and the existing 65,535 vertex/face limits.
Canonical project/build snapshots retain provenance; no parallel package identity
or runtime compiler is introduced. Raw topology edits require an explicit bake.
The fixture tool `tools/studio-modeling-check` tests actual compiler acceptance,
closed winding, area/volume, corner UVs, source byte immutability, editable JSON
provenance and real document undo/redo. Its 115 assertions pass against the actual
canonical engine. They include convex and concave plane cuts, closed quad rings,
bridging and grid caps, nonuniform-transform inset width, trivalent and
four-valence bevel fans, pinched-vertex rejection, ordered/toggled/repeated
modifier JSON, current object transform retention, snapshot provenance, failed
operation history preservation and canonical face-paint rejection before stacked
geometry/history changes. Native modeling UI acceptance remains separate.

Loop, knife and bevel operations split all affected shared edges in one polygon
pass. On this host, a measured 128-cube workload (1,024 source vertices, 1,536
output vertices and 1,280 output faces) took 37.203 ms in the headless run and
118.373 ms in the subsequent desktop-engine run, with 25,691,296 allocated bytes
in both, after replacing repeated whole-mesh edge splits. The same fixture before
that optimization took 302.227 ms and allocated 469,127,848 bytes. The fixture
asserts its closed output volume and a 64 MiB allocation ceiling. This measures
one detached modeling proposal; dense geometry and UI responsiveness require
their own measurements and bounded, cancellable background adoption.

The framework-only `tools/studio-diagnostics-check` compiles the actual collector,
formatter and job manager. Its 18 assertions cover provider release over 1,000
registration cycles, cancellation, immutable samples, bounded job history over
80 real jobs and unknown timing values. On this host, 120 collection samples
averaged 0.030 ms CPU and 7,264 allocated bytes per sample, with measured working
set 48,758,784 bytes (`/tmp/project-prime-studio-diagnostics-final-labels.log`).
This is collector overhead; it is not GPU or viewport FPS.
Native rendering and UI tests must separately prove resource release and the
reported renderer counters.

| Evidence | Current verification status |
| --- | --- |
| Baseline revision and initial clean checkout | Recorded above |
| Architecture scanner | Current source scan passed; 46 scanner/protocol self-tests cover alias/static/method-group publication, nested interpolation, exact map identity, bounded paths/frames, challenge/version-bound authentication and the exact BCL-only Protocol source link; full scan rerun after each new host/broker boundary |
| Original map/replay suites | Map editor 442; next pass 47; model import 65; collision 86; Community 84; runtime pass; renderer retention 12; replay timeline 43; replay format 2,962; replay controls pass |
| Isolated map multiplayer acceptance | Passed after test HTTP-peer retry/cancellation harness repair; original timeout log retained separately |
| Baseline map/replay UI captures | 35 original `-uishot` captures, including map/replay editor layouts; asset-backed replay presentation captures pending |
| Baseline GPU viewport captures | 30 assertions passed and captures produced; original check fails at the toolbar composite assertion on macOS Retina (`MapViewportCheck.cs:181`); baseline gate remains failed |
| Current game GPU viewport regression | 48 assertions passed after repairing the diagnostic's toolbar/overlay oracles against actual production composition; original failed baseline evidence remains retained |
| Asset-backed deterministic reference hashes | Existing local fixture: 1,801 frames, 14 seeks, five rates and EOF/divergence checks passed; enriched reference recording saved |
| End-to-end startup/memory/frame measurements | Five native Home samples: median 490.1517 ms, maximum 1,163.267 ms, median working set 182,403,072 bytes at logical 1280×800/native 2×; 20 distinct dense Map submissions have renderer CPU median 10.2702 ms/p95 11.8727 ms at actual 2560×1019 pixels; full 1440p CPU/GPU frame and loaded-editor startup distributions pending |
| Studio desktop UI/lifecycle acceptance | 682 headless assertions pass, including the 462 shell/Map gates plus Replay marker/key search, Save As tab adoption, camera preferences, read-only world state, late cancellation, visible transport status and Map gameplay analysis/selection; 55 Replay-suite manifest captures and three additional gameplay captures reviewed; full default native Replay139 passed; remaining enhanced workflow/outliner acceptance pending |
| Diagnostic collection ownership | 18 actual collector/job checks passed; native modes/owned HUD suite passed 116, including native ownership, measured counters, unchanged viewport allocation and sampling/provider release; new document-scoped/released metrics acceptance pending |
| Retained Studio renderer | 34 real Metal/Apple M4 Pro checks passed including M2 modes; native Map114 proves one/four-view shared resources, zero normal readback and final-close resource counts zero; dense97 proves 20 distinct native submissions without geometry reuploads; offscreen 2560×1440 submission costs measured separately |
| Current game replay regressions | Immutable current game snapshot passes 2,962 format checks, all replay-control groups, and the exact baseline reference's 1,801 frames/14 cold-cached seeks/five rates/EOF/divergence; full default standalone native Replay139 also passed |
| Standalone Replay simulation | 94 headless assertions pass with two historical recordings, two isolated custom-map versions and portable identity; this does not prove native GPU presentation/export |
| Enhanced canonical mesh modeling | 115 actual canonical-engine fixtures passed; two detached proposal runs measured 37.203/118.373 ms and 25,691,296 allocated bytes each; native modeling UI acceptance pending |
| Replay export components | 1,130 assertions pass with actual FFmpeg 9 H.264/AAC child encoding, canonical sampler/PCM/WAV/portable ZIP, bounded worker cancellation/detach/restart/parent-exit logs, deep ticket validation and cross-process cache-pin/pruning fixtures; full default native Replay139 passes actual PNG/MP4, positive PCM, cancellation, document/window-close, renamed-source and installation-lease gates; final session retention acceptance pending |
| Canonical prefab/diff and game broker components | 46 prefab/diff and 34 game-owner broker assertions passed; live native playtest acceptance remains separate |
| Native game/editor integration | 118 checks passed with actual unsaved canonical Map, real game scene, exact package restart, unchanged active runtime bytes, dirty-close Cancel and independent crash/reconnect ownership |
| Headless server build | M5 plus critical Replay repair Release `MphReadServer=true` build passed, zero errors and 38 existing warnings; Studio presentation remains excluded; final camera/retention source rerun pending |
| Android build | Earlier Release APK passed with zero errors/140 warnings and signature verification; current arm64/x86_64 patched runtimes rebuilt and managed source exclusions repaired; final source/APK rerun pending latest Replay batch; device runtime acceptance remains separate |
| Paired release/updater ownership | 85 updater assertions passed on macOS with net10 target override, including profile-independent coordination, IPC version labels, child Studio lifetime, unchanged blocked install bytes, crash release, exact paired versions and unpaired downgrade rejection |

Native lifecycle suite totals include the shared 91 CLI/persistence/document/job/
process assertions. The additional native gates depend on the run's explicit
arguments; a worker-only result does not include omitted HUD/multiview/clip gates.

The GPU toolbar assertion failed before gameplay/editor source changes in this
work. Its capture shows the toolbar, but visual presence does not make the failed
pixel assertion pass. The test's assumption that every RGB channel of the theme
must exceed 20 was replaced by comparison against the actual Avalonia CPU
raster: opaque flat toolbar pixels must contrast with the underlying geometry
and match after composition. The corrected current-source check now passes all
48 assertions; its log is `/tmp/project-prime-studio-mapviewport-final.log` and
captures are in `/tmp/project-prime-studio-mapviewport-final/`. The original
failure/capture remains a failed baseline gate.

The fresh immutable M5 regression snapshot in
`/tmp/project-prime-original-regressions-m5/` again passes Map editor 442, next
pass 47, model import 65, collision 86, Community 84, portable runtime, complete
map multiplayer acceptance, Replay timeline 43, format 2,962 and all replay
control groups. Its map logs use `/tmp/project-prime-map-*-m5.log`, and Replay
logs use `/tmp/project-prime-replay-*-m5.log`. Original native
OpenGL viewport acceptance also passes 48 in `/tmp/project-prime-mapviewport-m5.log`.
These original-domain gates remain distinct from the full standalone native
Replay139 result and later camera/final cache-retention acceptance.

The earlier integrated Map capture exposed a 694×73 viewport at logical
1280×800 (`/tmp/project-prime-studio-ui-actual/map-studio-default-current.png`).
The repaired current layout/UI suite now passes 462 assertions and native Map
passes 114. Current UI captures are in `/tmp/project-prime-studio-ui-current/`;
the expanded Replay UI suite passes 680 assertions with 55 manifest captures in
`/tmp/project-prime-studio-ui-replay/`, recorded by
`/tmp/project-prime-host-ui-replay.log`. Its replay viewport is intentionally
blank in headless mode; these captures establish panel layout, not GPU pixels.
Native Map output is `/tmp/project-prime-host-native-map.log`. The corrected
dense gate passes 97 checks and samples 20 distinct rendered revisions (10–29),
recorded in `/tmp/project-prime-native-dense-redraw.json` and
`/tmp/project-prime-host-native-dense-redraw.log`. Its actual 2560×1019-pixel
viewport at 2× contains 1,024 resident meshes and 1,025 draws. Renderer CPU
submission has median 10.2702 ms, p95 11.8727 ms and maximum 19.7944 ms;
cumulative geometry uploads remain 15,237,120 bytes and normal readback is zero.
GPU time is unavailable. The window manager limited the requested 2560×1440
viewport, and this component timing excludes preceding CPU overlay rasterization.
It does not establish full-frame 1440p FPS. Earlier repeated samples of one
cached submission remain invalid evidence.

The native Map modes/owned HUD suite passes 116 checks, recorded in
`/tmp/project-prime-host-native-map-modes-hud.log`. The HUD has its own native
window and owner, remains readable with the Inspector/Hierarchy hidden, and
does not alter viewport bounds. Its capture and measured device/CPU/memory
snapshot are in `/tmp/project-prime-native-map-modes-hud/`; hiding it releases
sampling and providers. The HUD capture renders the actual owned Avalonia
window separately from GPU viewport captures; it does not claim an OS composite
screenshot. Document-scoped metrics and released/lost resource cases require
the next coherent Studio snapshot.

Primitive counters describe CPU-visible triangles/lines submitted in the main
pass, including the native fullscreen overlay triangle. They exclude shadow
passes and do not count the final depth-tested visible pixels. Batch counts are
actual draw submissions/material parts.

The current retained renderer's 34-check gate includes a separate 1,024-object,
2560×1440 offscreen target workload on Metal/Apple M4 Pro. Renderer CPU submission
is median 7.829 ms, p95 12.5427 ms and maximum 19.7572 ms, recorded in
`/tmp/project-prime-studio-render-evidence/dense-1440p.json`. Its normal readback
remains zero; GPU time is unmeasured. This offscreen component test does not
include native window presentation or canonical CPU overlay rasterization and
does not establish end-to-end 1440p FPS.

Standalone Replay94 is a headless canonical simulation gate, recorded in
`/tmp/project-prime-host-replay-current.log`. The earlier native Replay snapshot
failed with an invalid uniform location; the M5 default HUD check then failed
its positive health/reticle-region pixel assertion. Those failures remain
recorded in `/tmp/project-prime-host-native-replay.log` and
`/tmp/project-prime-host-native-replay-m5.log`. A reduced native worker diagnostic
passes 113 checks in `/tmp/project-prime-host-native-replay-m5-workers.log`,
including actual MP4 encoding and document/whole-window closure. Its explicit
Game HUD, four-view and clip omissions do not certify those default paths.
The repaired full default native gate now passes 139 checks in
`/tmp/project-prime-host-native-replay-cancel-fixed.log`, with GPU captures and
clock/encoder evidence in `/tmp/project-prime-native-replay-cancel-fixed-evidence/`.
It verifies positive default HUD health/reticle pixels, a second player POV,
four camera views sharing one clock at two rates, read-only timer flags,
nonzero-frame retry preserving all three semantic hashes and a continuing clip
viewport after Save As. Native PNG/MP4 workers, positive PCM, actual FFprobe,
explicit cancellation, document closure, whole-window closure,
source rename and installation ownership also pass. The critical repair binds
the scene shader before camera uniforms, composes the private replica's HUD
without advancing gameplay and coordinates native generations and the single
workspace clock. These results do not certify later v5 camera persistence,
exact source binding, final cache-retention or stale-fourth-view cleanup cases.
The replay owner repaired repeated `.ppclip`
materialization through unique temporary extraction and retained immutable
snapshots; reopen/retry/two-session tests must prove that change. Evidence now
uses the retained source hash, avoiding a full source-file hash on the UI thread,
and validates nested comparison DTOs before adoption. A further review found
that Save As clips still reference the mutable logical source instead of the
prepared immutable recording; source-replacement acceptance remains pending.
The immutable recording snapshots and historical runtime directories require
bounded age/byte retention with active session/export owners excluded. Shared
cache pins and age/byte pruning primitives have been added; session adoption,
queued/detached export handoff and independent-process eviction fixtures remain
pending. Export's actual cache pin/prune and deep-descriptor fixtures now pass
within the 1,130-check component suite in `/tmp/project-prime-export-retention.log`.
Its contention fixture holds the real owner gate while UI job submission returns
within 500 ms and queued cancellation interrupts within two seconds before a
worker launches. Preparation-only cache locks do not establish session lifetime.
These remain pending gates. Native game/editor integration now passes 118 checks
in `/tmp/project-prime-host-native-game-expanded.log`, extending the earlier
empty-Studio lifecycle check with real dirty canonical Map state/history/
selection/layout retention, live-scene publication deferral and exact stop/edit/
rebuild/restart identities. The five Home startup samples are
in `/tmp/project-prime-native-startup.json`.

Local output is retained under `artifacts/studio-baseline/fcf311c/`, with a
`manifest.json` recording artifact SHA-256 and size. It includes suite logs,
`ui/{map-editor,replay-studio}.png`, GPU viewport/preview/composite images and
`reference-hashes.ppdemo`. These local artifacts are ignored by Git; durable
review/CI evidence must archive them explicitly. The fresh measurement host was
macOS 27.0 arm64, .NET SDK 10.0.100/runtime 10.0.0. Missing Block Fort import/texture
inputs were reported during the local UI/replay commands; synthetic authoring
fixtures and the selected replay determinism fixture completed independently.
The tracked [baseline record](project-prime-studio-baseline.json) retains the
revision, actual statuses, command log locations, reference replay hash and
unmeasured items. It marks release acceptance false.

Current additional logs are `/tmp/project-prime-studio-modeling-optimized.log`,
`/tmp/project-prime-studio-modeling-final.log`,
`/tmp/project-prime-studio-server-m5-repaired.log`,
`/tmp/project-prime-studio-server-build.log` and
`/tmp/project-prime-studio-android-build-final.log`. The Android build produced
`src/MphRead.Android/bin/Release/net10.0-android36.0/com.projectprime.game-Signed.apk`;
APK inspection confirms both renderer ABIs and no separate Studio executable,
`Avalonia.Desktop` or `Avalonia.Headless` entries. The evaluated Android source/
project graph also excludes Studio presentation and desktop package references.
The development APK signature passes `apksigner verify`; this is not a signed
production release or an on-device graphics/gameplay acceptance result.

Update this evidence table with actual output locations and failures as checks
finish. Unavailable assets or graphics contexts are recorded limitations, not
passing gates. Phase 0 is incomplete until the required evidence exists.

## Release and deletion gates

Build Studio for Windows, Linux and macOS, never Android. Desktop packaging and
the updater must deliver compatible game/Studio versions and report mismatches
explicitly. Avoid generic `.json` associations; optional `.ppdemo`, `.ppclip` and
`.ppmap` associations belong to the explicit desktop packaging slice.

`DesktopReleasePair` validates owned compatibility metadata and matching game/
Studio versions. `InstallationLifetime` holds a shared kernel lease throughout
either application's lifetime; the updater requires an exclusive lease before
any installed byte changes. A running Studio therefore blocks a paired update,
and process crash releases its lease. The updater refuses a game-only downgrade
that would silently leave an incompatible newer Studio. These local checks do
not substitute for signed/notarized desktop bundle and platform association tests.

Only redirect legacy `ProjectPrime -mapstudio` and creator buttons after the new
host can open the requested document with meaningful feature parity. Remove
embedded routes, `Shell.StudioWindow`, editor sizing hacks and old popout lifecycle
only after the standalone map/replay suites, Studio captures at 1280×800,
1920×1080/HiDPI, IPC tests and game/Studio lifecycle tests pass. Preserve the
game's lightweight Theatre and independent existing killcams.
