# Project Prime Studio architecture and migration

Reviewed baseline: `fcf311ccfc2031a07598860295db03eea330ff6e` (`main`),
October 6, 2026. The checkout was clean before migration. Current source and
executable checks take precedence over historical architecture plans.

## Current status and acceptance

`ProjectPrimeStudio` is an independent Avalonia Desktop executable using the same
canonical engine, map compiler/package implementation and private replay player
as `ProjectPrime`. Normal game creator routes launch Studio. The game retains
Theatre and a lightweight quick replay viewer. The original embedded Map host
remains only for the explicitly retained viewport/UI diagnostic oracles.

Local macOS acceptance proves native Map rendering, real external playtests,
private Replay playback and synchronized presentation, native modal ownership,
loaded creator controls and detached native video export. Local evidence records the exact scopes and immutable graphs below. Required final
Windows/Linux/macOS and Android CI outcomes are authoritative in
[PR #367’s checks](https://github.com/AntiNotAnti/Project-Prime/pull/367/checks)
for its current head. Completion requires those checks to succeed; a queued,
failed or superseded run is not passing evidence. Central jobs, cancellation,
curve selection, Android APK exclusion/signature and paired macOS
publish/sign/extract have passed locally on the recorded historical graphs.
The latest shared process-identity/cache fixes pass a fresh desktop build, 8,153
kernel-cache source contracts, 26 real-process hosted-cache checks and 1,160 macOS export
checks. Fresh native Replay 158, Android APK inspection and paired macOS package
plus extracted Studio Home also pass locally with their exact source/binary/version
identities recorded below. The subsequent atomic status-sharing/validation batch
passes macOS component checks, exact atomic-graph native Replay 158 and a fresh
Android APK. The later diagnostic build also passes the full physical M4 Metal/GL
check with production defaults. Each graph and current-head CI is recorded separately. Historical results are not attributed to newer binaries.
`ProjectPrime.Studio.Protocol` already extracts the proven framework-only shared
protocol boundary. Broader engine/UI assembly splitting and file associations
remain optional follow-ups after the proven process separation.

Except where a newer component result is identified, the phase totals below
refer to the immutable historical acceptance graphs recorded later in this
document. Current-head release acceptance still requires the live CI gates.

| Phase | Implemented boundary | Recorded acceptance and remaining gate |
| --- | --- | --- |
| 0 | Frozen baseline, captures/hashes, measurements and executable contracts | Original map/replay suites, five exact-baseline native Game startup/memory samples and five matching final-jobs Game samples recorded. Original Retina viewport assertion failure retained; corrected production oracle passes 48 on current source. |
| 1 | Independent executable, installation/user single instance, request forwarding and lifetime | Native game/Studio integration 118, IPC 60 and real client-guard process checks 11 pass on macOS. Paired macOS publish/sign/extract passes; current-head platform outcomes are recorded in PR #367’s required checks. |
| 2 | Asset-free Home, documents, commands, docking/floating panels, persistence and jobs | Loaded headless UI 824 and native owned dialogs 122 pass, including central expensive-action jobs, true cancellation/drain, source-wait ownership and adoption guards. |
| 3 | Canonical Map document and decomposed shared panels behind native host services | Map lifecycle/render 114, loaded UI 824, native modals 122 and real game/editor 118 pass. Actual failed native surface admission releases the unregistered handle and keeps canonical CPU editing available. |
| 4 | Private Studio builds and game-owned exact package publication; all game runtime generation fenced | Canonical independent-process real-scene generation 114, publication/path 83 (86 on a case-sensitive filesystem), physical alias 17 and native game/editor 118 pass. |
| 5 | Framework-only authenticated local protocol and bounded owner-thread game broker | IPC 60 and canonical broker 34 pass, including aliases, reconnect, rotated capabilities, cancellation, exact identity and narrow tickets. Required current-head platform outcomes are recorded in PR #367’s checks. |
| 6 | Unsaved private package → exact external playtest → stop/edit/rebuild/restart | Native game/editor 118 passes with live-byte preservation, selection/history/layout retention, independent crashes and stale stop rejection. |
| 7 | Per-document canonical private player, frozen sources, exact historical map resources, sidecars and lifetime pins | Standalone canonical Replay worker 72,624 plus parent lifecycle 94 pass; full native Replay 158 passes, including central seek cancellation and exact checkpoint/scene preservation. |
| 8 | Explicit native presentation host, one session clock, view/device generation lifetime and retry | Native Replay 158 proves default HUD, two POVs, four cameras, 1×/2× timing, retry hashes, continuing clip Save As and stale-fourth-view recovery/cleanup. Full-graph hashes compare the same explicitly presented phase. |
| 9 | Direct retained creator render world/graph, native surfaces, integer ID/depth picking and shared backend | Metal renderer 42 with evidence output, native Map 114 and dense redraw 97 pass. Exact 1440p offscreen renderer submission measured; end-to-end 1440p frame/GPU timing remains unmeasured. |
| 10 | Map workspace, viewport modes, modeling, prefabs, assets, analysis, structural diff and restart authoring loop | Canonical modeling 121, prefab/diff 70, renderer 42 and loaded UI 824 pass. Cooperative topology/modifier cancellation preserves source, history and failed adoption. |
| 11 | Replay tracks/curves, multiview, combat/analytics/comparison, offline audio, exports and portable/diagnostic bundles | Canonical Replay 72,624 and native Replay 158 pass on their historical graph; current macOS export components pass 1,160. Loaded UI 824 proves true two-dimensional key box selection, Speed-channel gestures and central action jobs. |
| 12 | Studio Home/session recovery, search, configurable hotkeys and measured diagnostic HUD | Loaded UI 824, collector 18 and native modes/owned HUD 119 pass, including second/released/lost document attribution. GPU time is explicitly unavailable. |
| Cleanup | External game creator routes; full in-game Replay editor and editor-only UiSurface sizing removed | Source contracts and original player UI regression pass. Retained diagnostic Map adapter is intentional; normal routes launch Studio. |
| Release | Paired desktop metadata, installation leases and updater compatibility; Android runtime only | Updater 85, final server Release and actual paired macOS publish/sign/extract pass. Final signed Android development APK passes both ABI/native payload/extracted assembly exclusion checks. Required current-head platform outcomes are recorded in PR #367’s checks. |
| Later | Broader engine/UI assembly separation and optional file associations | The framework-only shared Protocol boundary is already extracted. Selective additional assemblies are optional after the proven process boundary; no giant namespace/engine rewrite is required. |

## Product and process ownership

```text
ProjectPrime                          ProjectPrimeStudio
  player shell                          Avalonia Desktop lifetime
  gameplay/network sessions             creator documents and jobs
  Theatre / quick replay viewer          Map / Replay native viewports
  runtime map publication                private build/cache/staging
          |                                    |
          +---- authenticated local IPC -------+
                  immutable files + exact hashes
```

The `MphRead` root namespace and one authoritative implementation remain intact.
Studio references the canonical `MphRead` project and the small
`ProjectPrime.Studio.Protocol` project. This follows the requested process-first
sequence; it does not grant Studio game-window or live-session ownership.

Studio starts through `StartWithClassicDesktopLifetime`. Avalonia owns its
application window and child graphics surfaces. Studio cannot use `Shell.Run`,
`UiSurface`, `UiTopLevel`, `PrimeShell`, `StartScreen`, `RenderWindow` as an
application shell, `DemoPlayback`, `ReplayController`, `NetSession` or
`NetHostSession`. The game cannot reference the Studio executable/presentation.

The protocol remains framework-only, with no engine/UI assembly or package
references. Its one external source link is the exact internal BCL-only
`MphRead/Mods/Update/DesktopInstallationIdentity.cs`. Both assemblies compile the
same canonical directory-alias and paired macOS installation rules from that
file. Roles remain distinct, so game and Studio endpoints cannot collide.
Independent fixture tools may link exact production sources without adding those
links to either application; fixture substitutes establish only their explicitly
stated contract.

Gameplay remains fixed at 60 Hz and independent of rendering. Studio IPC version
1 is separate from the existing gameplay protocol 42. Studio migration does not
change network protocol, combat authority, replay format compatibility, killcam
ownership or immutable package identity.

## Map authoring, jobs and publication

[`MapDocument`](../../src/MphRead/Mods/MapEditor/MapDocument.cs) remains the
source of truth. Dirty state uses saved/current state IDs; common edits preserve
bounded delta/coalesced history, fine-grained invalidation and retained viewport
caches. All preparation receives detached canonical snapshots. Failed operations
leave document state, history, selection and the published world intact.

The shared `MapStudio*` and `MapViewport*` controls are decomposed into hierarchy,
inspector, geometry/modeling, UV, material, asset/source, navigation/gameplay,
problems/build, prefab/diff and Community partials. `IMapStudioHostServices`
injects native dialogs, immediate local file-drop paths, Community tickets,
private output ownership, jobs, installation and playtests.
`AvaloniaMapStudioHost` owns the canonical control/document lifecycle;
`MapStudioDocument` exposes it to the desktop document host. Native dialogs are
owned by the Avalonia application and preserve their scope through cancellation
and shutdown. Source workers capture owner/document/revision/path context and
reject stale or physically escaped adoption before history mutation.

Studio builds under `<UserData>/studio/{build-cache,staging,playtest}`.
`MapBuildScheduler` still deduplicates bounded detached builds, validates
fingerprints and publishes immutable caches. `StudioPrivateMapRuntime` can
publish canonical outputs only inside its physically resolved private root;
constructor and immediate pre-publication checks reject game-root overlap,
symlink escape and case-sensitive sibling escape. The renderer/compiler is not
forked. Runtime packages preserve the exact `MapId`, `ContentHash`, `PackageHash`
tuple; same-name replacement is forbidden.

ProjectPrime's `GameStudioBroker` prepares immutable packages off-thread and
queues commit/launch on the bounded game-owner dispatcher. It verifies the
prepared and installed exact identity. Studio does not publish game runtime
files. Busy map ownership returns a deferred result. Playtest supports default,
selected and team spawns and the current camera, without requiring Save or
closing Studio. Restart builds a new package and scene; it does not patch a live
collision/world simulation.

`MapPublicationLease` complements the existing process-local `MapRuntimeUsage`
gate. Keys include physical runtime root, runtime namespace and room. Scene and
preparation owners acquire shared kernel leases; package commit and runtime
publish require an immediate exclusive lease before any output changes. Unix
uses `flock`, Windows uses `LockFileEx`; crash releases ownership. Acquisition
precedes the first resource read. A failed runtime root/namespace switch retains
the existing shared owner rather than exposing an already-live room.

Every generation entry point obeys this rule, including public `Install`,
`Publish`, recipe/CLI generation, `GenerateMissing`/`GenerateIfNeeded` and raw
`MapPacker` commits. `MapRuntimePublication` recognizes complete canonical game
room/namespace output sets, rejects partial/mixed/cross-namespace/file-alias
outputs and permits genuinely private compile destinations. Fenced public
scheduler methods alone call private `InstallOwned`. Prewarm generates before
acquiring its own shared lease, then revalidates the fingerprint and all output
hashes under that lease before reading. This avoids attempting a writer upgrade
under its own reader.

The 114-check canonical generation fixture creates a real `SceneSetup.SetUpRoom`
with generated fixture assets and decodes `RoomEntity` collision, models, entities
and navigation. A separate writer exercises every generation route while the
scene is alive: all six runtime files remain byte-identical. Release/retry matches
privately compiled outputs; kill releases ownership; a poisoned collision barrier
proves admission precedes reading. Ten thousand reader tracking operations against
ten thousand exclusive attempts admit zero writers. Its before-fix run proves
the actual bypass, rather than only a synthetic lock failure.

The separate publication fixture passes 83 checks on the ordinary filesystem and
86 when repeated on an owned case-sensitive APFS volume. It covers exact-hash
retry, crash release, private Replay room independence, canonical directory
aliases and exact private runtime ownership. These are two runs of the same
fixture, not additive totals. Its earlier ZIP/runtime fixture is not itself a
decoded game scene. Seventeen additional physical alias helper assertions cover
real case-variant sibling escape and alias cycles.

Community publication retains the existing short-lived narrow `ppm1` ticket.
Only a validated ticket crosses IPC. Studio cannot read stored Hunter License /
Supabase access or refresh tokens. Without an authenticated game, Studio directs
the user to sign in through ProjectPrime. Existing canonical Community service
fixtures and injected dashboard/modal routes establish the local workflow; no
live production credential or publication is required by these tests.

## Replay ownership, camera state and caches

[`PassiveReplayPlayer`](../../src/MphRead/Mods/Network/PassiveReplayPlayer.cs)
remains the canonical private player. Reader/session/transport, replica scene,
players/match/RNG, mutable resources and checkpoint caches belong to the player.
`StudioReplayPlayer` owns this implementation; `ReplayStudioSession` and
`ReplayStudioDocument` own presentation, authoring and disposal per document.
They do not use the game foreground facade or open a live gameplay socket.

Seeking uses the existing bounded canonical reconstruction and publishes a new
candidate scene only after successful preparation. Owner updates remain bounded
to 120 fixed steps and a time budget. All four native views render the same
simulation/presentation frame from one session clock; individual viewport timers
do not advance simulation. The presentation host receives camera/output/HUD
policy explicitly, binds the correct native generation and retains document,
camera/annotation state and hashes across recoverable presentation failure.

Source preparation captures immutable exact bytes and a retained SHA-256 off the
UI thread. Evidence/report UI uses that frozen hash. Nested evidence DTOs are
validated for nulls, counts, identities/enums and finite data before adoption.
Portable bundles carry exact replay and historical custom-map package identity;
import verifies it before rebinding sidecars. Same room/name/latest package is
never substituted. Private historical resources are scoped and do not register
a game-global reader lease for the same room.

Camera v5 persists the canonical window evaluator and bounded opaque state,
preserving exact fractional crop/nested-window paths and subsequent key edits.
Clip/project descriptors retain durable original source references with optional
exact `SourceContentHash`; worker preparation rejects replaced bytes. Save As
does not introduce an ephemeral snapshot dependency. Sidecar writes are queued,
immutable and cancellation-aware, with guarded owner adoption, explicit Discard
and awaited Save/Close. The replay recording remains immutable.

Source and exact-package shared cache pins are acquired before writer exclusivity
ends, transferred to session ownership and released only on final disposal.
Device deinitialization retains them. Preparation/disposal races release local
pins/candidates instead of adopting a dead document. Managed retained owners let
queued jobs survive document close without UI filesystem waits. Production
`MapDiskCache.Pin/Prune` excludes active/queued owners, and process crash releases
kernel pins. Pruning is detached and cancellation-aware: source budget 512 MiB /
7 days, package/runtime 2 GiB / 30 days, descriptors 64 MiB / 7 days. Active owners
may exceed nominal budgets; they cannot be evicted to satisfy a byte target.

Hosted-package library owners and export workers share the BCL-only canonical
`MphRead.Platform.ProcessLifetimeIdentity`. Linux and Android use exact boot ID,
PID and kernel start time from procfs; Windows and macOS use exact process
creation time. Unavailable metadata and legacy Linux/Android UTC records retain
ownership as `UnverifiedAlive`. Hosted-cache reap removes an aged library only
after confirmed exit or an exact different process incarnation, then rechecks
the unchanged owner marker before deletion. An unreadable identity cannot drop
the library’s protection of its exact hash archive. The 26-check gate exercises
actual separate owner/reaper processes and cache pressure, including exit, PID
reuse, legacy and malformed markers. Studio delegates to this engine helper;
the game has no dependency on Studio.

## Creator rendering and diagnostics

The Map path uses `StudioRenderDevice`, `StudioRenderSurface`, retained
`EditorRenderWorld` resources, `ViewportRenderGraph` and integer ID/depth picking,
reusing the existing modern backend/material/PBR/device recovery. Geometry
changes upload the affected retained resources. Camera/selection changes reuse
those resources, including across four viewports. CPU triangle picking remains
a fallback and parity oracle. GPU click picking reads one pixel.

Normal `IStudioNativeMapPresentation.Present` renders into an Avalonia-owned
native child surface and uploads canonical CPU overlay pixels. It performs no
world GPU readback (`ReadbackBytes=0`). Image-returning capture APIs are explicit
separate operations. Source contracts prohibit readback in the normal native
Map graph, and actual native tests assert counters and final-close release.

M2 implements PBR, lighting/shadows/fog, collision modes/heat/terrain, navigation,
spawn/pickup/jump, kill-plane/partition, overdraw, wireframe, texel-density and
material-ID views. The native modal proof includes a GPU preview beside a short,
scrolled native dialog; headless blank viewports are not native pixel evidence.

The performance HUD reads document-scoped immutable renderer reports, canonical
Replay timing/checkpoint state, the selected map scheduler and actual Studio jobs.
It samples only while visible, never allocates a device just to display metrics,
and releases providers/timers. Its owned native window does not shrink a viewport.
Map metrics become unavailable after world release, generation loss or before
that document renders. Replay timing belongs to that document's player.

“Renderer CPU submission” measures retained graph preparation, uploads, queue
submission and native surface presentation. It excludes preceding canonical CPU
overlay rasterization and GPU completion. “Submitted primitives” counts main-pass
triangles/lines and the native overlay triangle; it excludes shadow passes and
does not count final visible pixels. Draw/batch counts are submitted material
parts. Geometry upload bytes are cumulative actual retained vertex writes;
resident geometry includes uniform storage. Pick readback and normal frame
readback are distinct. GPU timestamp timing, Replay GPU counts and checkpoint
capture timing remain explicitly unavailable where not measured.

## Map and Replay enhancements

The existing CSG/topology/UV/material compiler remains authoritative.
`MapModelingEnhancements` creates detached validated proposals for proportional
falloff, quad ring cuts, shared-vertex plane knife cuts, segmented boundary bridges,
Coons grid caps, constant-width planar inset, manifold endpoint-fan bevel and
ordered mirror/array evaluation. Closed inputs remain closed and consistently
wound. Explicit geometry limits remain: loops cannot cross poles; bridges need
equal corner counts; grid fill/inset require convex planar boundaries; mirror
source lies on one side of its plane. Unsupported topology rejects before mutation.

`MapMesh.ModifierSource` retains one self-contained original and at most 32 typed
modifiers, bounded by the canonical 65,535 mesh limits. Repeated stack edits reuse
the original, preserve the current object's ID/transform and cannot recurse.
Project/build snapshots retain provenance; runtime geometry is the evaluated
canonical mesh. Raw topology/UV/face painting requires explicit Bake.

Canonical prefabs have stable source/member/material identity, revision, transform,
overrides, update and detach. Prepared adoption captures state and Save As context,
stages assets privately and rejects stale/type-incompatible edits before mutation.
Runtime packages resolve objects/resources without requiring external prefab
sources. Structural diff compares typed objects, materials, gameplay resources,
navigation, environment and prefab state. Asset/source panels provide previews,
search/tags/usage, physical source context, drag/import/reimport/reference changes.
Gameplay diagnostics and navigation/spawn actions leave authored state/history
unchanged. The live authoring loop packages changed content and restarts externally.

Replay has tracks/ranges/markers/annotations, camera curves/tangents/FOV/roll/path
visualization, synchronized POV/free/target/overview views, recorded combat facts
and shooter/authority comparison, world/projection heatmaps with filters, and
build/replay evidence comparison. Diagnostic output redacts secrets. Review found that key box selection filtered time/X only. The owning UI batch
now adds vertical bounds and must pass actual gesture/cancellation tests before
its integration is accepted.

All expensive creator operations must be observable and cancellable through
`StudioJobManager`. Builds, opens/preparation, exports and existing canonical jobs
already use it. Review identified private thumbnail/Q3 preparation and direct
Replay analysis/extract/portable/diagnostic/report actions, plus bounded seek
observation, outside that central presentation. Their central-job integration is implemented and awaits final workflow acceptance;
canonical simulation remains on its bounded owner update. Modeling proposals accept
the central job cancellation token and check it throughout topology/modifier loops,
in addition to guarded adoption. The 121-assertion canonical gate includes deterministic in-progress cancellation
after the first complete modifier, matching token, byte-exact source preservation
and unchanged live document/history; cancelled work cannot publish a late proposal. Completed/cancelled jobs have bounded history and shutdown
drains owners.

## Export process and update ownership

The window owns `ReplayExportWorkerCoordinator`; canonical player/sampler/audio/
encoder jobs run in child Studio worker processes. Each child owns its private
replay source/resources, Avalonia/native device and installation lifetime through
graphics shutdown. At most two actual live workers are admitted using the shared
exact process-incarnation helper. An unverified live record retains capacity
until its ownership can be resolved; Linux UTC boot estimates are not compared
across processes. Persisted queued tickets and statuses survive restart;
live statuses reserve capacity before new queued work launches, even after 256
completed history entries. Deep malformed/null/oversized tickets fail closed.

Cancel uses a persisted explicit signal. Closing a document or the entire editor
detaches observers and leaves admitted exports running; a new Studio observes
status/resumes queued tickets without duplicate writers. Workers hold exact
source/package/runtime pins and their installation lease until terminal cleanup.
Encoder cleanup is idempotent. Bounded durable worker diagnostics survive parent
pipe closure/exit. Final scratch directories are removed after ownership ends.

`StudioReplayStatusFile` provides one canonical reader for the coordinator,
export facade and retained cache-reference scanner. It opens with
`FileShare.Read | FileShare.Delete`, denies write sharing and bounds the opened
handle snapshot to 64 KiB before parsing. A worker may atomically replace the
path while a reader retains the complete previous version. Production writers
already close staging before rename. Cross-process fixture PID/readiness/
completion signals now follow the same closed-staging publication rule.
Real child witnesses retain an unpublished staging file, then verify published
PID and complete old/new status versions across replacement. Oversized and
malformed status snapshots are rejected. The macOS source-linked suite passes
1,161 base checks or 1,166 with actual FFmpeg/FFprobe; the Windows old-reader
share-denial negative remains pending actual next CI.

Offline audio uses replay events and deterministic PCM/WAV mixing, including
combat/game audio and optional music/volumes, without desktop audio capture.
24/30/48/60/90/120/144 FPS and 720p/1080p/1440p/4K remain. Fractional sampling over
60 Hz gameplay is canonical; extra exported frames do not advance simulation.

`DesktopReleasePair` verifies paired compatible versions and owned release
metadata. `InstallationLifetime` uses the same physical installation identity as
IPC and holds a shared kernel lease for Game, Studio and export children. The
updater needs exclusivity before changing any installed byte, reports both
versions and rejects unpaired downgrades. Windows/Linux/macOS release jobs package
both applications; Android retains compatible runtime/map/replay code and excludes
Studio UI. Its shared-source glob explicitly removes desktop `MapStudio*`,
`MapViewport*` and the exact native Map host services/facade. General Android
capture helpers remain; only their embedded Map diagnostic is guarded out.
Untrimmed assembly metadata, trimmed ABI assemblies and the actual APK are checked
separately. Pure canonical MapEditor/MapGen and passive Replay code remain shared.
Platform build/publish/sign/extract checks remain release gates.

## Executable architecture contracts

```sh
dotnet run --project tools/studio-architecture-check -c Release
dotnet run --project tools/studio-architecture-check -c Release -- --self-test
```

The [checker](../../tools/studio-architecture-check/README.md) scans Studio,
Protocol and public engine facade/shared authoring sources. It rejects game shell,
live session, foreground replay and direct game publication owners; Android code;
external source links other than the exact BCL Protocol helper; and game-to-Studio
presentation dependencies. The current atomic-status source scan passes 8,179
contracts. The preceding kernel-cache scan passes 8,153 and its freshly compiled
self-test run passes 66 lexical/protocol/Android-exclusion contracts; those negative
self-tests have not been rerun for the status batch, whose checker is unchanged. The tests include aliases, escaped names, comments/interpolation,
constructor route negatives, exact identities, bounded frames and challenge/
version/role-bound authentication. Android project negatives reject missing, commented
or later reintroduced creator exclusions; capture negatives require the desktop
Map diagnostic to be omitted while preserving general Android captures. Counts
increase with source additions.

Game source contracts reject normal embedded Map construction, the removed full
`ReplayControlsView` and `StudioWindow`, and editor-only `UiSurface` factors.
Legacy Studio dispatch precedes ordinary game-file setup. Publication contracts
require writer acquisition before install/raw commits and generation-before-reader
followed by validated prewarm. Normal Map present cannot read GPU world pixels.

| Exact adapter/service | Permitted ownership | Reason and retention |
| --- | --- | --- |
| `MphRead/Mods/Launcher/Gui/MapStudioLegacyHost.cs` | Game overlays/drop, narrow ticket, existing fenced game publication | Diagnostic-only compatibility for `MapViewportCheck`, `UiCapture` and `-mapstudioshot`. Normal game routes cannot construct it; standalone native facade injects its own services. Retain while these original diagnostic oracles run. |
| `MphRead/Mods/Launcher/Gui/MapViewportGpu.cs` | `UiSurface` | Original embedded viewport/composition diagnostic path. Standalone uses native retained presentation; this exception does not allow game-surface ownership in Studio. |
| `MphRead/AvaloniaShared/StudioPrivateMapRuntime.cs` | Canonical `MapBuildScheduler.Publish` with private destinations | Exact physical-root and immediate destination guards; no game runtime output or low-level install permission. |
| `MphRead/AvaloniaShared/StudioGameAssets.cs` | `CustomRooms.DeferInitialRegistration` only | Avoid inherited global catalog initialization in authoring process; no installed catalog mutation/publication permission. |

No Studio-source exceptions permit prohibited owners. New filenames do not
inherit adapter permission. The checker is a lexical/project-source contract,
not a semantic/IL audit or arbitrary MSBuild target evaluator. Runtime, security,
UI, deterministic and platform tests remain independent gates.

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

## Recorded verification and measurements

Native lifecycle totals include the shared 91 base assertions; optional native,
Replay, Game and measurement branches run only when explicitly requested.
A reduced worker-only run never certifies omitted HUD/camera/clip branches.

| Evidence | Accepted scope and location |
| --- | --- |
| Original canonical regressions | Map 442, next 47, model 65, collision 86, Community 84, runtime pass, renderer retention 12; complete multiplayer retry/cancellation/rotation; Replay timeline 43, format 2,962, control groups pass. Fresh immutable M5 logs `/tmp/project-prime-{map,replay}-*-m5.log`. |
| Original native viewport oracle | Corrected current source 48 passes `/tmp/project-prime-mapviewport-m5.log`; original baseline toolbar/overlay failure and captures retained. |
| Deterministic baseline recording | Exact local reference 1,801 frames, 14 cold/cached seeks, five rates and frozen EOF/divergence passes. Reference SHA/hash schema and exact historical identity retained. |
| Current atomic-status architecture | 8,179 source contracts `/tmp/project-prime-studio-architecture-atomic-status-final.log`. The unchanged checker retains its preceding 66 negative self-test result; no new negative-suite rerun is claimed. |
| Recorded kernel-cache architecture | 8,153 source contracts `/tmp/project-prime-studio-architecture-kernel-cache-final.log`; 66 freshly compiled self-tests `/tmp/project-prime-studio-architecture-kernel-cache-self-final.log`. Historical Android-expanded 66 self-tests / 8,121 source contracts remain in `/tmp/project-prime-studio-architecture-android-{self,scan,build}.log`. |
| Latest desktop build | Fresh shared-identity/cache graph passes zero errors / 133 existing warnings in 52.64 s, `/tmp/project-prime-studio-kernel-cache-final-build.log`; exact binaries are recorded below. Native Replay passes on these exact binaries; fresh APK and paired-package acceptance retain their own source/binary/version identities below. |
| IPC / broker / updater | 60 IPC, 34 canonical broker, 85 paired update/lifetime assertions. Real endpoint aliases, capability rotation and owner cancellation covered. |
| Real game/editor | Native 118 `/tmp/project-prime-host-native-game-final-lifetime.log`: dirty canonical map, real scene, both launch orders, exact restart, deferred publication, crash isolation and retained history/selection/layout. |
| Canonical map generation | 114 `/tmp/project-prime-map-generation-live.log`; real decoded active scene, all generation routes, reader-before-read barrier, concurrent tracking and crash/retry. Failed original admission retained `/tmp/project-prime-map-generation-before-fix.log`. |
| Publication / paths | 83 `/tmp/project-prime-private-runtime-ownership.log`; the same suite passes 86 on case-sensitive APFS `/tmp/project-prime-private-runtime-case-sensitive.log`. Separate physical alias checks 17 `/tmp/project-prime-raw-alias-case-sensitive.log`. Private history and active game ownership remain independent. |
| Loaded creator UI, historical graph | 824 `/tmp/project-prime-host-ui-final-lifetime.log`, captures `/tmp/project-prime-studio-ui-final-lifetime/`: all prior loaded texture/WAV/model/prefab/source/build/recovery/Replay assertions, independent animation owners, central action jobs, held-thumbnail cancel/drain/renewal, source waits, 2D curve selection and Speed gesture preservation. Headless viewports intentionally do not prove GPU pixels. |
| Content-free creator UI | 471 `/tmp/project-prime-ci-content-free-ui.log`, or 581 with self-contained generated asset fixtures `/tmp/project-prime-ci-content-free-ui-assets.log`. Both begin without an extracted game root; these shell/Map CPU gates do not include the historical loaded Replay suite or prove native GPU pixels. |
| Hosted cache ownership | 26 `/tmp/project-prime-hosted-cache-identity.log`: actual separate owner/reaper processes retain active libraries and exact hash archives under eviction pressure; malformed/legacy/unavailable identity remains protected, exact PID reuse and confirmed exit permit collection. |
| Native owned dialogs | 122 `/tmp/project-prime-host-native-dialog-v5-scrolled-gpu.log`, `/tmp/project-prime-native-dialog-v5-scrolled-gpu-evidence/`: parent/child ownership, active viewport, owned cancellation, 300-DIP scrolled actions and explicit GPU preview. |
| Canonical modeling / prefab | 121 `/tmp/project-prime-studio-modeling-cancellation.log`; prefab/diff 70 `/tmp/project-prime-map-prefab-v6.log`. Actual compiler/topology/provenance/history/Save As races are covered; broad native gesture claims require their own workflows. |
| Canonical Replay | Worker 72,624 plus parent lifecycle 94 `/tmp/project-prime-final-seek-replay.log`. Two historical recordings and two isolated custom-map versions; cancellation/supersession retains exact scene, checkpoint payload hashes/count/bytes and original transport, and disposes abandoned allocated candidates. |
| Atomic-status native Replay | 158 `artifacts/studio-acceptance/final-release/native-replay-atomic-status/manifest.json`: full default HUD/four-view/seek/retry/clip/worker scope on exact 4d2ebe/4fa9f binaries and native 8d. Later Mac GL diagnostic source is not included. |
| Atomic-status Android APK | 118,118,652 bytes, SHA-256 `4200f0a54b43f2d09ecdf407f776b37074b57975ca631e93ab2b6673c7d4d4b2`, `artifacts/studio-acceptance/final-platform/android-atomic-status/manifest.json`: exact unchanged 1,177 inputs, package/signatures/creator exclusion and native payloads pass. Pre-Mac diagnostic ModernGraphicsWindowCheck source is inactive on Android; no later-head or runtime relabeling. |
| Physical production-default Metal/GL | Exact f51 graph passes full pixels/mips/GPU timestamps, fresh OpenGL recovery and Metal restart, `artifacts/studio-acceptance/final-release/physical-metal-gl-default/manifest.json`; both diagnostic opt-ins unset, actual process exit 0. |
| Fresh full native Replay | 158 `artifacts/studio-acceptance/final-release/native-replay-kernel-cache/manifest.json`: default HUD/two POVs, four-view clock/rates, exact retry/seek/clip/camera, stale final-view cleanup and actual detached PNG/FFmpeg/PCM/close/cancel/lease/scratch checks. Engine c06e5651 / Studio 13f45952 / native 8d; one explicitly presented phase within the recorded source, without claiming old-fixture equivalence. |
| Full native Replay, historical graph | 158 `/tmp/project-prime-host-native-replay-final-presented.log`, captures/JSON `/tmp/project-prime-native-replay-final-presented-evidence/`: default HUD/two POVs, one-clock four views at 1×/2×, seek/retry hashes, clip continuation, stale fourth generation, exact frozen v5 crop, pixel-equal worker frame, native PNG/MP4/PCM exports and central jobs. |
| Shared-identity export components | macOS 1,160 `/tmp/project-prime-export-shared-identity-final.log`: canonical sampling/PCM/encoder, actual FFmpeg/FFprobe, shared exact process identity, small-thread-pool ownership and cache contracts. Historical f070 Linux real FFmpeg 1,162 also passes in job 112577552084; post-sharing current-head CI is separate. Historical 1,144 `/tmp/project-prime-export-acknowledged-evidence.log` retains delayed-child startup/queue/concurrency evidence; fixture pixels are separate from native Replay pixels. |
| Atomic publication components | macOS base 1,161 / actual FFmpeg and FFprobe 1,166, `artifacts/studio-acceptance/final-release/export-atomic-publication/summary.json`: real child staging/PID/status-version witnesses and one bounded canonical Read/Delete-sharing status reader used by all three consumers. Windows old-share-denial negative is pending actual next CI. |
| CI fixture synchronization and preparation | Fresh production-linked Gamepad 1,022 and Prime UI 201 pass `/tmp/project-prime-gamepad-ci-repairs.log` after committing a headless compositor frame before pointer input. Replay preparation 32 passes `/tmp/project-prime-ci-map-preparation-final.log`: exact private staging ownership/identity, existing archive/runtime bytes and cancellation cleanup, with unowned-file and mutation rejection. Production input, export coordination and map publication are unchanged by these fixture repairs. |
| Map native renderer | Retained Metal 42 `/tmp/project-prime-studio-gpu-picking-final-evidence.log` (41 without output), native Map 114, dense redraw 97. Real one/four-view shared uploads, ID/depth CPU parity, loss/recovery, final resource zero counts and bounded winning-triangle CPU refinement. |
| Diagnostics | Collector/job 18 `/tmp/project-prime-studio-diagnostics-final-labels.log`; owned native modes/HUD 119 `/tmp/project-prime-host-native-map-final-scoped.log`, captures `/tmp/project-prime-native-map-final-scoped-evidence/`; second unrendered/released map and lost-generation metrics are unavailable, then current-generation presentation restores them. |
| Native failed admission | Generic native lifecycle 118 `/tmp/project-prime-host-native-admission-final-lifetime.log`: eight actual C API rejection checks, created/released handle counts, zero live surfaces/worlds/targets, retained error and CPU edit/Undo/Discard. |
| Native RmlUi overlay | 27 `/tmp/project-prime-rmlui-durable-native.log`: three physical layouts, native DOM keyboard/mouse exactly once, visible pixels and shutdown. This macOS GL overlay gate is distinct from the whole game menu. |
| Server / Android, historical graphs | Recorded server Release zero errors/38 existing warnings `/tmp/project-prime-studio-server-final.log`. Recorded Android APK zero errors/137 warnings `/tmp/project-prime-studio-android-final-creator-excluded.log`; actual signed APK assembly-store extraction, creator exclusion, both pinned native ABIs and v2/v3 development signatures pass. |
| Fresh Android APK | Zero errors / 137 warnings, 1,176 raw compile sources match f070; actual packaged assembly extraction, creator exclusion, both native 8d ABIs and v2/v3 development signatures pass `artifacts/studio-acceptance/final-platform/android-kernel-native-f070/manifest.json`. Actual generated informational version remains local+39ff; no emulator/on-device pass is implied. |
| Fresh paired macOS package | Publish/sign/extract/version/native smoke and real extracted Studio Home pass `artifacts/studio-acceptance/final-release/osx-arm64-kernel-identity/release-evidence.json`. Local test version 1.2.3 / IPC 1, ad hoc signatures, archive 162,431,716 bytes, SHA-256 `da1a787b9f76c46e956979cb69b2d5863edfc17234db82f15e57f9f8bf500f04`. Packaged GUI acceptance is Home; full Replay is the separate 158-check harness. |
| Paired macOS package, historical graph | Actual SDK self-contained osx-arm64 publish, strict ad hoc signatures, extraction, both executable versions 1.2.3/IPC 1 and extracted native smoke pass `/tmp/project-prime-paired-final-package.log`; archive 162,415,713 bytes, SHA-256 `33a28da60f4f16a829d9db74b688200aa5531d5470855f62776fcd2e9eeb0d72`. |

The recorded historical native Replay 158 gate includes seven PNG frames, immediate prelaunch cancellation, an
actual 25-frame FFmpeg video with measured stereo PCM and FFprobe, and document/
whole-app closure while a child completes 121 frames after source rename. The
child retains the installation lease until terminal state; scratch cleanup and
durable bounded diagnostics pass. Stale fourth-view loss/recovery at frame 17
preserves gameplay/presentation/full-graph hashes and releases surfaces 4→0,
renderbuffers 1→0 and textures 94→2, where two textures are host baseline.
Canonical/headless acceptance covers the exact fractional/nested camera matrix,
source replacement refusal, session adoption/disposal pins and cancellation.
The final return-to-frame-17 full-graph oracle explicitly draws both reference and
returned scenes at 640×360 before comparing. The earlier unpresented/presented
mismatch is retained and explained by named model matrices, animation and room
render mode; simulation frame, RNG, decoder and cosmetics were unchanged. The
matched presented phase has equal full-graph hashes and an empty field/component
diff, without filtering fields or changing production code.

Earlier native invalid-uniform/default-HUD failures remain in
`/tmp/project-prime-host-native-replay.log` and
`/tmp/project-prime-host-native-replay-m5.log`. Their later full repaired gates
replace those results; reduced native worker 113 is not substituted for them.
Likewise the first 694×73 Map layout capture and repeated stale dense metric
samples are retained failures, superseded by repaired current UI and twenty
actual distinct rendered revisions.

Five native asset-free Studio Home starts have usable-shell median 490.1517 ms,
maximum 1,163.267 ms and median working set 182,403,072 bytes at logical
1280×800/native 2× (`/tmp/project-prime-native-startup.json`). The measured
warm-cache process distribution is below the 1.5-second target. Loaded-editor
startup and controlled cold-cache distributions remain unmeasured.

The corrected dense native Map sample contains 20 distinct revisions (10–29),
1,024 resident meshes and 1,025 draws. Actual target is 2560×1019 pixels at 2×;
the window manager clamped the requested 1440-pixel height. Renderer CPU submission
median is 10.2702 ms, p95 11.8727 ms, maximum 19.7944 ms, with stable 15,237,120
geometry upload bytes and zero normal readback (`/tmp/project-prime-native-dense-redraw.json`).

The separate 1,024-object exact 2560×1440 offscreen Metal test measures renderer
CPU submission median 7.4885 ms, p95 9.3272 ms, maximum 13.0159 ms, 1,024 draws /
12,288 triangles, resident geometry 18,792,448 bytes and cumulative vertex uploads
16,760,832 bytes (`/tmp/project-prime-studio-final-picking-evidence/dense-1440p.json`).
Eager retained wire/ID geometry explains upload size; it remains stable through
camera changes. Neither submission distribution includes CPU overlay raster/GPU
completion or establishes end-to-end 60+ FPS at 1440p. GPU timing is unavailable.

A real detached modeling proposal on 128 cubes transforms 1,024 source vertices
into 1,536 vertices / 1,280 faces with closed volume 1,024. Replacing repeated
whole-mesh edge splitting reduced allocation from 469,127,848 to 25,691,296 bytes;
measured proposal CPU was 37.203 ms headless and 118.373 ms desktop, compared with
302.227 ms before optimization. The fixture asserts compiler/topology/volume and
a 64 MiB allocation ceiling. This does not claim dense interactive modeling.
Collector-only overhead is 0.030 ms / 7,264 allocated bytes per sample over 120
samples; it is not a viewport/GPU measurement.

The original Game startup distribution was measured later by rebuilding an
isolated exact Git archive of `fcf311ccfc2031a07598860295db03eea330ff6e`.
All 2,005 tracked source blobs match the baseline; its resulting assembly has
SHA-256 `f6c19ec15a71093497dceea30add566f45118939b850b0ab58794c76b5c9e9e6`.
The unmodified production executable uses an external `DOTNET_STARTUP_HOOKS`
observer. Five initially empty, independently owned profiles read existing AMHE1
assets and close through normal Game Quit. At the first native presented frame,
the observer submits Enter through production input; usable means a later
post-presentation callback with the startup gate removed, a visible/enabled shell
and closed overlays. Its stopwatch median is 1,244.7097 ms (range
1,228.3426–1,279.3976 ms); first presented frame median is 838.53 ms. Parent
process-launch observation, using 2 ms polling, has median 1,280.2915 ms (range
1,269.4802–1,326.0576 ms). Working set, sampled before explicit pixel capture,
has median 310,935,552 bytes (range 309,805,056–311,951,360 bytes). This measures
OpenGL on Apple M4 Pro at logical 1280×768/native 2560×1536. It includes the
24-frame reveal animation, excludes human reaction delay, disables the updater
and does not flush filesystem/GPU caches. These are fresh processes rather than
a controlled cold-cache distribution. Five actual GPU shell PNGs, observer
source, source verification and build/process logs are retained under
`artifacts/studio-baseline/fcf311c/startup/`; the baseline JSON records individual
sample/capture hashes. The Studio Home and dense-render measurements use
different workloads and are not a comparative game GPU-performance result.

Five matching Game samples now pass against the immutable final-jobs working tree
graph, engine SHA-256
`3823d127d2490a049383faf682b802e7e13a60bcc1f71372adddc82612d4aabf`.
All children exit normally and reach the same interactivity/metadata gates at
frame 25, with the same Enter input, assets, 1280×768/2560×1536 OpenGL window and pre-capture
memory sampling. Builds and other native/headless fixtures were paused. A temporary
owned display-awake assertion avoids the macOS idle-monitor GLFW failure without
changing OS preferences. Filesystem/GPU caches remain warm and uncontrolled.

| Same Game workload | Exact baseline median | Final-jobs median |
| --- | ---: | ---: |
| Observer start → usable presented shell | 1,244.7097 ms | 940.0855 ms |
| Process spawn → observed measurement file (2 ms polling) | 1,280.2915 ms | 970.9797 ms |
| First presented frame | 838.5300 ms | 749.1938 ms |
| Pre-capture process working set | 310,935,552 bytes | 305,496,064 bytes |

The first accepted frame can retain the last cached startup-reveal raster; these
measurements do not claim completed visual fade. A separate post-measurement
calibration proves fully faded native Home at the next presented frame 26 and
retained clean Home at frame 30. Production `UiSurface.KeyDown/KeyUp` Q then changes
the actual route from News to Settings; frame 35 captures its selected native
Settings page. The calibration exits normally and is excluded from timing samples.

The final-jobs usable-shell range is 937.9679–960.2527 ms and working-set range is
305,250,304–305,807,360 bytes. These five-run distributions show the measured
startup/working-set result; they are not a controlled cold-cache, gameplay or GPU
FPS experiment. Subsequent thumbnail cancellation and failed native-surface
admission cleanup change the complete engine binary hash without changing this
Game Home path. The measurements remain attributed to the exact final-jobs graph,
not a later assembly. Captures, per-process logs, external observer source/binary,
native staging and SHA-256 manifest are retained under
`artifacts/studio-acceptance/final-jobs/game-startup/`. The tracked baseline JSON
records individual samples, capture hashes and comparison definitions. Excluded
setup/idle-display calibrations are separate from the five accepted samples.
Studio Home is a different workload and cannot replace this comparison.

Baseline evidence is retained under `artifacts/studio-baseline/fcf311c/` with
SHA-256/size manifest; the tracked [baseline record](project-prime-studio-baseline.json)
records exact revision, fixture/build/capture identities, statuses and definitions.
These large local logs/screenshots/reference recordings are ignored by Git and
must be archived explicitly for durable review. Hardware: macOS 27.0 arm64,
Apple M4 Pro, .NET SDK 10.0.100/runtime 10.0.0. Missing local import textures are
recorded fixture limitations, not passing acceptance. GPU images use pixel/
semantic/visual oracles rather than cross-driver PNG byte identity.

## Completion contract and evidence scopes

The working-tree causal batch after `8acb52aabbab616bc66620ac7d7348e48669a9fa`
repairs status publication and a positively diagnosed native DX12 binding-order
mismatch. Its exact precommit build/local renderer receipts are recorded separately;
earlier package/runtime passes are not inherited by these new sources.

In actual 8acb Windows Studio job `112598737320`, the old-reader denied-replacement
negative passes, then the held Read|Delete positive child Move fails. That failure
omitted ErrorType/HResult; no specific native code is inferred. The new canonical
`StudioReplayStatusFile.Write` closes unique bounded staging and uses Windows
`File.Replace` for an existing destination, whose ReplaceFileW target access is
compatible with immutable read/delete sharing. Initial Windows publication uses
non-overwriting Move; Unix keeps overwrite rename. Both actual worker publishers
and the direct child fixture delegate to this same helper. `Read | Delete` is
unchanged, Write remains denied, errors propagate and there is no deletion gap
or in-place update. Local macOS pure 1,189 and actual FFmpeg/FFprobe 1,194 pass,
recorded in `artifacts/studio-acceptance/final-release/status-replace-pure/manifest.json`.
Actual next-head Windows replacement/runtime proof remains pending.

Actual DX12 job `112598737289` now identifies GPU-based-validation error 939 on
the first VERTEX draw: root parameter 0 / range 0 holds an SRV while the shader
expects a CBV. The failed Map and actual `GetDeviceRemovedReason` both report
`0x887A0005`; the d26 failure log is retained at
`/tmp/project-prime-ci-8acb-windows-dx12-112598737289.log`. Independent renderer and
shell source reviews find nativecore EntryMap stage insertion order `[2,0,1]`
passed to HAL while bindgroup entries are ordered `[0,1,2]`, violating HAL's
sorted-binding contract. The approved repair sorts at that native boundary and
adds an intentionally unsorted shader-binding regression. The frozen native
fingerprint is `467581d1f838134de71fafc1a2bb259bd9aea3974c67c9ed21e9729ba00c8725`.
Actual Naga/check_stage derivation and explicit-layout invariants pass 2/2;
classifier 2, WSI 2, fault subprocess 35 and packaging 10 also pass locally. The
source receipt and all four macOS production/fault and Android arm64/x64 library
hashes are retained under
`artifacts/studio-acceptance/final-release/native-layout-order-467581d1/`.
This establishes local compiled ABI/regression scope. Actual Windows runtime
proof remains pending: both full validation-enabled and subsequent
production-default DX12 checks remain required.

The new coherent Main build passes with zero errors / 131 warnings in 50.29 s;
all 1,413 evaluated source inputs are identical before/after, SHA-256
`9c8fa69c98332087fbfa1b8310da343bfb71e1c5841f495606bafbf69db581bc`.
Its exact engine is
`0f6487e771d2c61197caab83f406e93ce361f6c3ea877ec01bb855f361c4a683`, Studio
`d806217982d79a20e79b125678fd894a989ca47b840426603b16ace3c53b4652`, and macOS
native `2c8f690c54705e6119d33db3d1515351061dd7e4c0ffc332398cff2760851c93` /
467581d1. Actual generated version retains base8acb precommit attribution.
Architecture 8,239/8,239 and negative contracts 66/66 pass on these sources.
The build/source/asset-guard receipt is
`artifacts/studio-acceptance/final-release/layout-status-main-build/manifest.json`.

The physical Apple M4 Pro check on this exact engine/native graph exits zero in
2.595112708 s at production defaults, with GPU validation and hosted capability
allowance unset. Full Metal pixel/mip/window phases, fresh actual OpenGL with
93 mip assertions, and modern restart pass; owned application and awake lease
exit. Its separate receipt is
`artifacts/studio-acceptance/final-release/physical-metal-gl-layout-final/manifest.json`.
Fresh actual cue 110 and full native Replay 158 now pass on this exact
0f6487/d806217/467581 graph. The actual cue controls produce frozen worker
tickets; each independent worker renders three PNGs and 2,400 stereo 48 kHz
PCM frames, with audible RMS 0.1767625848345871 / 4,792 nonzero samples and
muted 0 / 0. Logs are
`/tmp/project-prime-host-native-replay-layout-status-{cue,full}.log`; evidence is
`/tmp/project-prime-native-replay-layout-status-{cue,full}-evidence`. All owned
applications, workers and awake leases exit. The earlier loaded UI 858 remains
its actual161/30/d26 source-compatible receipt, without relabeling. Windows
DX12 and final-head CI acceptance are not inferred from these local results.

The historical exact 8acb receipts remain separate: precommit Main 560cef8e/
c392973f and its physical Metal/GL proof; paired macOS SDK engine
`01129e4fd7af6c7f8aeba3be0960fc47338813bd439139f51e7de4aa81c94b6b` / Studio
`dbb7ffeff200f3ba2f1f68ba8c4e4ebd1217da09882385f347698f2778f2d713` / native d26,
archive 162,431,465 bytes SHA-256
`e9ca39d23e7142365657ceee6b6213f5564f07cfd6e4764ad49b461e91a04c81`
(`artifacts/studio-acceptance/final-release/osx-arm64-final-d26/release-evidence.json`).
This actual SDK test package is 1.2.3 / IPC 1 with local strict ad hoc/extract/Home
scope, not a production release or notarized package. Source-compatible cue110,
full Replay158 and UI858 are still attributed to their actual engine161ae5be/
Studio30a573/d26 binaries, without relabeling to either Main or SDK assemblies.

The exact committed 8acb Android receipt is
`artifacts/studio-acceptance/final-platform/android-8acb/manifest.json`:
118,126,844-byte APK SHA-256
`0d9132a80a0c4477022af28ed19d63ef189ff9a2adcdf9a39662026d0714cf07`,
zero errors / 137 warnings in 105.58 s. All 1,178 committed source inputs match
before/after, SHA-256
`6c724b7b4e479178645e2cc99bd0d9cc07f8d8d08c450d2c050c9d06ddf016ff`.
Actual informational version is `local+8acb52aabbab616bc66620ac7d7348e48669a9fa`;
both ABI/linked/untrimmed metadata retain 247 Map types and passive Replay with
zero creator/Studio types or references. Both d26 native payloads and AndroidDebug
v2/v3 development signatures verify. This exact build/package proof does not
establish Android runtime or accept newer status/binding repair sources. New
final-head Android APK, paired SDK and all required final-head CI results remain
pending for the causal batch; overall acceptance remains false.

The working-tree batch after `b692ef6db2eea87f09d7545442d42b863310329c`
completes the required R7 event-to-WAV authoring route. The Export inspector can
add, update and remove at most 64 recorded-event bindings, choose an audio bus,
set finite cue gain and an optional recorded integer value, and validate mono/
stereo PCM16 or float32 WAV through a cancellable central document job. Source-bound
presentation preferences retain bus volumes, audio switches, music and bindings;
clip Save As transfers them only after the replacement source is prepared.
Queued canonical exports independently copy the binding collection. Recorded
event types/values are cached after the matching immutable recording is Ready,
rather than rebuilding the full event list every graphics callback. These source
changes are implemented. The separate source-linked suite passes 1,186 checks,
including 26 cue-authoring preference/validation/freeze cases
(`/tmp/project-prime-r7-audio-pure-check.log`). The final loaded UI passes 858
checks with 87 actual retained captures. Focused native cue acceptance passes 110
(91 base plus 19 cue checks) on engine `161ae5be6f96a721bba045d203f4fa5debc63aeee3383b70f36f6050dd73fa92`,
Studio `30a5730d45b98d03714e924491bab49083aa906439abc3166e4a4d57641b47f7`
and native d26. Actual Add Cue / Export Selection controls produce immutable
worker tickets despite subsequent editor changes. Each independent terminal
worker renders three PNGs and 2,400 stereo 48 kHz PCM frames: the audible Replay
bus has RMS 0.1767625848345871 / 4,792 nonzero samples, while the muted bus is
exactly 0 / 0. Logs/captures are `/tmp/project-prime-host-audio-ui-final-dispatch.log`,
`/tmp/project-prime-studio-audio-ui-final-dispatch-evidence` and
`/tmp/project-prime-native-replay-audio-final-evidence`. The owned event fixture
has SHA-256 `a758177e90ba455a90e1c9dd20fdd6b08611cbaa88a4880671a3b6aa6ec03b10`;
the earlier native Replay 158 recording has no metadata events and remains
unchanged. The separate current full native Replay 158 also passes on the exact
161ae5be/Studio30a573/d26 graph (`/tmp/project-prime-host-native-replay-audio-full.log`).
Neither result is relabeled to the later 560cef8e/c392973f final build: production
Replay/Studio source bodies are unchanged, while its additional CLI capability
helper has a separate build/physical-renderer scope.

The accepted b692 paired macOS SDK receipt is
`artifacts/studio-acceptance/final-release/osx-arm64-atomic-status/release-evidence.json`:
162,442,580 bytes, SHA-256
`c58dd6ea2f899ab000af0453ce95c6d05d69259a490ff117901e663c4f6d3f2a`.
Its exact tracked raw source identity is
`17ea1d3588d6b5aa859fed195160dc788d0f4d7934d066906a6ec314297c092f`,
matching b692 tree `82c37c850a3c54f4d864ac1db1093bb423252e0b`. This is a local
self-contained `osx-arm64` acceptance package at test version 1.2.3 / IPC 1,
with strict ad hoc signatures and actual extracted Game launcher/thumbnail and
Studio Home/clean exit evidence. It is not a production release, Developer ID
signature or notarized package. Published IL verifies the canonical immutable
status reader and all three consumers. Packaged Home does not establish Map/
Replay parity or accept newer cue/diagnostic sources; the full native Replay 158
scope remains its separate 4d2ebe/4fa9f2 graph.

The initial actual b692 workflow `37556696151` observation records 26 successful
scoped jobs. Its later 38-row snapshot has 27 successes, five failures and six
not done; overall acceptance remains false. Its tested merge
`cb5b4490851e60f615db7231430e8e6fe9de2ad7` has the exact b692 source tree.
Windows Studio job `112586714897` fails a negative fixture because denied rename
returns `UnauthorizedAccessException` / HRESULT `0x80070005`, rather than the
fixture's expected `IOException`. The repair accepts only the two denial exception
types with native codes 5, 32 or 33; the canonical Read|Delete positive replacement
and complete old/new snapshot oracles remain mandatory. Actual next-head Windows
execution is pending. DX12 job `112586714751` still fails its validation-enabled
full run; the causal trace is incomplete and the required production-default
second full run is skipped, so neither is accepted. Linux package job
`112586714885` fails the unchanged baseline `NetTransport.Send` allocation path,
whose formatted result was `0.01 B/op`. The new fixture reports exact allocated
bytes, successful sends, socket errors and connection-lock counters plus runtime/
OS/architecture and GC diagnostics. The strict zero-allocation failure is
intermittent and unclassified; the production transport and exact zero gate are
unchanged. Actual Intel GL constructor capability and ARM restart test sequencing
are the other recorded failures. The narrow helper repair remains a next-CI gate.

The new native diagnostic source fingerprint is
`d26e910ce2f23e5c90e885c927d47c22e6d3227ac4a3ce14e6380ed5e5835d84`.
Managed diagnostics retain and forward bounded Warn/Error records ahead of noisy
lower-priority logs. With `PRIME_WGPU_VALIDATION=1` and the additional exact
`PRIME_WGPU_GPU_VALIDATION=1` opt-in, DX12 calls the actual GPU-based/synchronized
command-queue validation setters before device creation. A failed opted-in Map
queries `GetDeviceRemovedReason` and reports the actual operation/reason HRESULTs.
Default rendering does not enable these diagnostics. The workflow must prove the
actual setters and pass the validation-enabled full check, then run a second full
check with both diagnostic variables unset. The actual macOS production/fault
and both Android native builds now match d26 and bridge ABI 1, with 35 real fault
subprocess cases and 10 packaging checks; their exact native hashes are retained
in `artifacts/studio-acceptance/final-release/native-diagnostics-d26/current/all-native-receipts.json`.
These component proofs do not establish Windows DX12 or hardware recovery.
Historical f070/b692/8d binary results remain attributed to their exact
inputs. Required final-head checks remain the authority; scoped greens do not
accept the whole migration.

The fresh coalesced Main build passes zero errors / 132 warnings in 52.92 s;
all 1,412 tracked source inputs are unchanged before/after. The durable receipt is
`artifacts/studio-acceptance/final-release/audio-diagnostics-main-build/manifest.json`:
engine `161ae5be6f96a721bba045d203f4fa5debc63aeee3383b70f36f6050dd73fa92`,
Studio `9d52dbb8b0b57fcea77692bf35404f6ac9a80c08bd9ffbc5b63212ce9aa3f6b2`,
native `6b6a9f2b74ca3ea935c3b08be503dd84cecaac6d2a53b261ebbf24723ac83f44` / d26.
The fresh architecture scan passes 8,235 source contracts and 66 negative cases;
tracked asset guard and whitespace checks pass. These working inputs retain their
actual precommit b692 version attribution; they are not relabeled to a future
committed head. The later Studio-only cue graph and actual PCM proof above are
separate from this original `9d52dbb8` build receipt.

The Android SDK build also passes zero errors / 137 warnings in 91.29 s.
All 1,177 evaluated raw source inputs match before/after, SHA-256
`6f61ab9086a0b33019d27266aa7d09f18c9807cbd934e2ba6793dfb8c887eb90`.
They differ from committed b692; the actual generated version remains
`1.0.0.0` / `local+b692ef6db2eea87f09d7545442d42b863310329c`.
Actual APK inspection now passes on those exact inputs:
118,459,468 bytes, SHA-256
`446a0ac0d1d2d7abb7b5d9e7fb37a0142fd3e758c2937ba130a3e2e580ccce8d`,
recorded in `artifacts/studio-acceptance/final-platform/android-d26/manifest.json`
and `acceptance.json`. Both packaged ABI canonical DLLs are identical, retaining
247 Map types, the passive player and nine allowed pure Replay helpers, with
zero creator GUI/Studio types or references. Both d26 native payloads match their
verified builds; development APK v2/v3 signatures verify. The build includes
b692 plus four frozen working deltas, rather than a future committed head.
No local Android runtime or later helper/final-head CI result is inferred.

The new shared `HostedLegacyGlCapabilityCheck` passes 54 exact-source diagnostic
contracts (`/tmp/project-prime-hosted-legacy-contract.log`). Actual physical CGL
returns two formats for all four requests, so the allowance is rejected and all
handles are released. Explicit Actions/Mac ARM64 or X64 opt-in, exact typed NSGL
constructor/callback, an actually measured exact Apple Paravirtual adapter and
four independently known-absent CGL results are all required. Standalone GL probes
a disposable offscreen Metal device only after that exact failure. `UNAVAILABLE`
does not claim GL pixel success. The proven-unavailable restart branch initializes
a real modern owner before the unchanged overlay release, double shutdown and
fresh restart pixel/reupload oracle; capable GL checks remain unchanged. Actual
hosted capability/Metal/MoltenVK and required final-head CI remain pending.

The final Main build including this helper passes zero errors / 131 warnings in
44.94 s. All 1,413 source inputs are unchanged before/after, SHA-256
`782fc06f8178cd41cf628a4cf07435639f5f1d368346eb7aa39804e1d509ae26`.
Its exact receipt is `artifacts/studio-acceptance/final-release/r7-platform-main-build/build-identity.json`,
engine `560cef8e9dbf9197422e44d5719487846b9851a61a2a228db83e6f8ca80c8110`
and Studio `c392973f43dd50ba25b6b56cb235024cf47d99e26b4766fbd4c1ac742cf8de94`,
with the same verified 6b6a9f2b/d26 native payload. The source scan passes 8,239
and negative self-tests 66, as retained in
`/tmp/project-prime-studio-r7-platform-architecture.log` and
`/tmp/project-prime-studio-r7-platform-architecture-self.log`. Generated precommit
b692 version attribution is preserved; no future committed head is claimed.

Actual physical M4 full Metal/fresh GL/modern restart also passes on this exact
560cef8e/d26 graph with production defaults, exit 0 in 2.766493417 s. The durable
receipt `artifacts/studio-acceptance/final-release/physical-metal-gl-r7-final/manifest.json`
records clean exit of both the owned application and awake lease. This actual
capable-device proof does not establish hosted CGL absence, Windows device-loss
causality or final cross-platform acceptance. Overall migration acceptance remains
false until required final-head checks succeed.

The final platform-diagnostic managed build passes zero errors / 133 existing
warnings in 53.76 s (`/tmp/project-prime-studio-platform-diagnostics-final-build.log`).
Its immutable snapshot is `/tmp/project-prime-studio-platform-diagnostics-final-snapshot`,
engine `f51b04109769927f75cce7c095f7898fb3a983cd5c1968970328f9d9b5feefcd` and Studio
`5d0011e64b4207bb1fdaed706571a0ee87a9e606c8fd5f8325132107337f7406`, retaining the
verified native 8d payload. Current source scan passes 8,179
(`/tmp/project-prime-studio-architecture-platform-diagnostics-final.log`).

Its full physical Apple M4 Pro Metal check passes with native validation and
hosted NSGL allowance both unset: full draw pixels, mip sampling, real GPU
timestamps, forced fresh OpenGL green pixels / 93 mip levels, idempotent shutdown
and Metal restart, with actual exit 0. Exact binary/source/log identity is retained
in `artifacts/studio-acceptance/final-release/physical-metal-gl-default/manifest.json`.
This production-default run does not infer Windows or hosted Mac capabilities.

The hosted Mac exception is confined to the acceptance diagnostic: explicit opt-in,
GitHub Actions, macOS ARM64 and exact `Apple Paravirtual device` must all match.
Four independent CGL requests must prove known absence with zero format handle
and count; the typed GLFW `FormatUnavailable`, callback description and exact
OpenTK constructor message must also match the suitable-pixel-format failure.
It reports legacy OpenGL `UNAVAILABLE` and still requires modern restart pixels.
Unexpected errors, ambiguous/present formats and stereo’s different error remain
failures; normal renderer fallback is unchanged. The unchanged extracted helper
body passes 21 contracts (`/tmp/project-prime-nsgl-contract.log`), including actual
M4 count 2 on all four requests, which blocks the exception. Actual constructor
and stereo-negative evidence is retained in
`/tmp/project-prime-nsgl-constructor-contract.log`. Restricted hosted runtime
acceptance remains a next-CI gate. Windows DX12 must pass validated full checks
and then production-default full checks; its first-window loss cause remains
unresolved. No new SDK package is attributed to this diagnostic source; the
accepted f070 package retains its exact historical raw inputs and test version.

The subsequent atomic-status/validation desktop graph passes zero errors / 133
existing warnings in 49.96 s, `/tmp/project-prime-studio-atomic-status-final-build.log`.
Its immutable snapshot is `/tmp/project-prime-studio-atomic-status-final-snapshot`,
engine `4d2ebe1bd955dd9d78d6d86144b659754b5cf39f00d28121607133ef0c0c1e04` and Studio
`4fa9f2cc80868f5145a6d80d3e402ad31fb990b4de44013d0ade41a3a04fed57`, with the same
verified 8d native payload. The rejected enum-to-uint flag-assignment build is
preserved at `/tmp/project-prime-studio-atomic-status-rejected-flag-build.log`;
the repaired build uses an explicit uint cast. The 1,161/1,166 macOS component
checks cover this status-source batch. Full native Replay 158 now passes on the
exact 4d2ebe/4fa9f graph, recorded in
`artifacts/studio-acceptance/final-release/native-replay-atomic-status/manifest.json`.
The 2,433,255-byte source retains SHA-256
`cf2d6ce25e9679e2973652253077e4b313fb5d675c3b187958923a96709254e8`; all world hashes
compare the same explicitly presented phase within the run. Later diagnostic
source and older fixture equivalence are not inferred.

The atomic-status Android APK also passes build/package/signature/metadata checks:
118,118,652 bytes, SHA-256
`4200f0a54b43f2d09ecdf407f776b37074b57975ca631e93ab2b6673c7d4d4b2`, recorded in
`artifacts/studio-acceptance/final-platform/android-atomic-status/manifest.json`.
All 1,177 evaluated inputs remain byte-identical before/after compilation.
Its actual informational version remains `local+f070c9324847ae1f9987fea957450e86b1af6cd6`;
the working tree differs from committed f070 and is not relabeled to a future head.
The retained pre-Mac diagnostic `ModernGraphicsWindowCheck.cs` input is inactive
on Android. Both native payloads, packaged/linked assembly identity, 247 canonical
Map types/passive Replay and creator exclusion pass; v2/v3 signing uses the Android
Debug development certificate. On-device/runtime acceptance is not claimed by
this local package check. The later diagnostic graph and prior f070 SDK package
remain separate records.

The accepted kernel-cache desktop Release build passes zero errors / 133 existing warnings in
52.64 s (`/tmp/project-prime-studio-kernel-cache-final-build.log`). Its immutable
snapshot is `/tmp/project-prime-studio-kernel-cache-final-snapshot`, engine
`c06e565170ce09582307fae355b1b7bbc00eb6fce3ce04ca14bcd0b6288bac11` and Studio
`13f45952fee0ec3bc3ca50f53c95790098506c23605d1f20d344c07c707b5943`.
This graph includes the canonical shared identity and hosted-cache ownership
fixes. Full native Replay 158 now passes on those exact binaries and the 8d
native payload. The durable manifest is
`artifacts/studio-acceptance/final-release/native-replay-kernel-cache/manifest.json`.
It records the 2,433,255-byte replay source SHA-256
`cf2d6ce25e9679e2973652253077e4b313fb5d675c3b187958923a96709254e8`, full coverage,
owned-child cleanup and raw source identities matching
`f070c9324847ae1f9987fea957450e86b1af6cd6`. Generated binary versions remain as
built. All world hashes use the same explicit presentation phase within this
recording; equivalence with an older run’s fixture is not inferred.

Fresh Android acceptance is retained in
`artifacts/studio-acceptance/final-platform/android-kernel-native-f070/manifest.json`.
The signed APK is 118,447,180 bytes, SHA-256
`1cebdf0ed1fedcd9d64d8aa981fda80ca64d3c2d9b2517aca7bd791429a52c81`;
both extracted canonical ABI assemblies match
`10b300bf08513d2af3c387d2977fc90b47b8b9bd9cc88bd3623819d57b79dcb0`.
All 1,176 evaluated raw compile sources match the f070 tree. Actual generated
version 1.0.0.0 / informational `local+39ff92d44b3182048c20106bf8b65e15bb8fc497`
is preserved because compilation started before that commit. Creator exclusion,
247 canonical Map types/passive Replay, both packaged native 8d payloads and
v2/v3 development signatures pass. This does not establish emulator/on-device
acceptance.

Fresh paired macOS publish/sign/extract/version/native smoke and an actual
extracted Studio Home window pass at local test version 1.2.3 / IPC 1.
`artifacts/studio-acceptance/final-release/osx-arm64-kernel-identity/release-evidence.json`
retains f070 source tree/raw-file identity, revalidation at archive completion,
strict ad hoc signatures, native payload and clean Home shutdown/capture.
The archive is 162,431,716 bytes, SHA-256
`da1a787b9f76c46e956979cb69b2d5863edfc17234db82f15e57f9f8bf500f04`.
Published engine/Studio assembly hashes are respectively
`c9abb713c92772f9f006fd8120ef5f088f98a1e03ef96619881febb0a04e3154` and
`856ea225ac4a65efb08ec73bd19d893df2dbf46df0a23e1f2cda2d6726b1f289`.
The packaged GUI scope is Home without documents; full Replay 158 belongs to
the separate c06e/13f native harness. This local package is not a production
release and does not claim Developer ID signing or notarization. Earlier
lifetime/native/APK/package results remain historical records below.

The native patch fingerprint is
`8d0246408e9d8df823557ae2b613d784cd4bbc69f1979fcd791f9743a0316ebf`.
The actual macOS payload SHA-256 is
`66cea18944cc9ea1e662828f23697b4a27f54528e31892ab26d73dd19569ee30`, recorded in
`artifacts/wgpu-native-macos/osx-arm64/PRIME-WGPU.json`. Both Android native-only
ABIs were built and fingerprint/bridge verified; their hashes and logs are in
`artifacts/studio-acceptance/final-platform/android-native-8d024640/manifest.json`.
The separate native-only build records do not establish managed APK acceptance;
the fresh packaged APK verification is recorded above. On-device acceptance
remains outside these local gates.
The Windows FXC repair in `tools/wgpu/patches/dx12-fxc-source-name.patch` supplies
an owned NUL-terminated `CString` to `D3DCompile`, including an unlabeled module.
Actual f070 Windows FXC named/unlabeled/invalid shader checks pass (three
assertions, job 112576154062). Its DX12 job 112577552102 also passes 25 offscreen
checks on Microsoft Basic Render Driver, then loses the device at first window
UI readback (`0x887A0005`). The whole job failed; the cause remains unresolved.
The subsequent diagnostic opt-in `PRIME_WGPU_VALIDATION=1` sets
`(uint)InstanceFlag.Validation` (native value 2), with DEBUG unset, and enables
native info logging without changing shader optimization. Its contract log is
`/tmp/project-prime-native-validation-contract.log`. On the exact 4d2ebe graph,
actual physical Metal passes all 25 offscreen/red-pixel checks with Validation
flags and exit 0 (`/tmp/project-prime-native-validation-metal-probe.log`). This
proves the conditional uint interop on Metal, rather than full Replay/window or
Windows DX12 acceptance. Actual next-CI window diagnostics remain required; the
option does not certify a device-loss repair.

The Android Map/renderer boot scripts now require a finite positive deadline,
bound adb waits and reap their owned child on every exit. Their fake-SDK harness
passes 182 assertions across 22 success/failure cases, using real child processes
and a default 600-second boot deadline. Manifest, case timings, source hashes and
log are retained in
`artifacts/studio-acceptance/final-release/android-bootstrap/manifest.json`.
This admission/cleanup fixture does not claim a real emulator or Android runtime
pass. Separate actual f070 emulator evidence records Vulkan cold launch and
surface recreation with two device/surface/acquire/draw/readback/present/UI
frames (job 112576154085) and positive ppmap package verification/download/
registration/runtime decode (job 112576310722). The renderer uses KVM and
software SwiftShader on an x64 emulator; no physical-device or hardware-GPU
acceptance is claimed. Durable records are
`artifacts/studio-acceptance/final-ci/current-f070/android-{renderer,map}/manifest.json`.
The Map runtime step alone does not certify its entire Android workflow.

Historical f070 Linux Studio job 112577552084 passes pure export 1,157, actual
FFmpeg 1,162, lifecycle 91, native Xvfb 118 and content-free headless UI 471,
with paired publish 1.0.0 / IPC 1. Its native admission pixel metrics are retained
log evidence; the workflow did not upload that CPU-fallback PNG. Exact log and
artifact scopes are in `artifacts/studio-acceptance/final-ci/acceptance.json` and
`current-f070/ubuntu-studio-112577552084.log`. These accepted job scopes belong
to f070 / merge `e13716ea3459a3e31f861aaf3b7f816e686b21a5`, matching tree
`a3df1b9129caa39ab05183d7be7c2c3b5ad19e53`, run 37553962101. They do not convert
its failed or unfinished jobs into overall acceptance. The f070 macOS renderer
job 112576153965 produced positive pixels through all Metal draw phases, then
failed forced OpenGL restore with `NSGLFormatUnavailable` on AppleParavirtual.
Its retained log is `/tmp/project-prime-ci-f070-modern-mac-arm64-112576153965.log`;
that investigation remains open and the whole job is not accepted. Required
checks must still pass for the final current head.

Historical local desktop acceptance passes against the immutable lifetime graph:
engine `5789c339739fa809c0d6dec09aec52a025efc5720a01f2837df832e0cbbf834e`
and Studio `9167e7a4133d97cef40a4b3822b1433a6ad2e2265f4c94b9fa700a82ef362aa2`.
The later paired publish adds only the authorized generated GLFW diagnostic
fixture repair; its separate source/binary identities are retained in
`artifacts/studio-acceptance/final-release/osx-arm64/release-evidence.json`.
Actual paired macOS publish/sign/extract/version and native smoke pass with ad
hoc signatures; this does not claim Developer ID signing or notarization.

The recorded historical server compilation and Android development APK pass. The APK is
118,102,268 bytes, SHA-256
`ea91e76407ac6bdacd95356e4a8976fa12d839b34107447b5935194461d6ecfd`.
Both actual APK ELF assembly stores were extracted and their XALZ/LZ4 payloads
decoded; each canonical assembly exactly matches the inspected linked DLL
(`875abfa5e73812c1ed33abada9462cdadbf42a6ad4f81b9917c50918b64493a7`).
Independent metadata confirms absent desktop creator/native host/full Replay
editor types and Studio/Avalonia Desktop/Headless references, while preserving
247 canonical Map types and the passive Replay player. Both packaged renderer
ABIs match their pinned fingerprint-verified builds. APK v2/v3 signatures verify
with the Android Debug development certificate. The exact 1,175-source compile
manifest, logs, probes, metadata and APK are retained in
`artifacts/studio-acceptance/final-platform/android/`.
Android compilation/package verification does not establish on-device graphics
or gameplay acceptance. Required final Windows/Linux/macOS and Android checks must succeed for the current
PR head. [PR #367’s checks](https://github.com/AntiNotAnti/Project-Prime/pull/367/checks)
are the authoritative live status; they are not replaced by this static local
record. The ignored consolidated acceptance manifest
`artifacts/studio-acceptance/final-ci/acceptance.json` retains the actual tested
head, generated PR merge and source tree, per-job outcomes, logs and artifact
identities once the final results settle. A generated merge can certify the head
only when its source tree matches exactly, as verified for the recorded b2f79132
run: merge `0819eefa266c7558609966945572acb7012bd61e`, matching tree
`82cfbba268daa0f75c17aa90df9bb983bb684cec`. That identity verification does not
convert a failed or incomplete workflow into acceptance. No required authoring capability is being deferred to
optional broader engine/UI assembly splitting or file associations; do not associate generic
`.json` files.

Preserve the diagnostic-only legacy Map adapter while original viewport/UI checks
need it. Normal creator routes and the full in-game Replay authoring UI are
removed; this exception must not reopen embedded application ownership. Keep
Theatre quick watch and independent existing killcams. The framework-only Protocol
assembly is already shared and extracted. Optional additional engine/UI assemblies follow the proven boundaries and must not fork
the engine.
