# Scene lifetime ownership baseline (LIFE-01)

This slice adds observation, not a leak fix or a foreground binding change. The
baseline is `16b1d89bfb6ed7776b801b3e39d513e740ee5e58`. In particular, a foreground
scene remains legitimately reachable after visual teardown while post-match
saving still reads `GameState.Current`. Actual retained .NET root paths must be
captured after all eligible owners release before diagnosing a leak.

## Observation contract

[`SceneLifetimeSnapshot`](../../../src/MphRead/Mods/Diagnostics/SceneLifetimeDiagnostics.cs)
is a value-only record with a monotonically increasing local scene generation,
current policy role, lifecycle stage, exact foreground facade identities, player
and entity counts, owned texture names, the four main program names, model
leases, pending streaming decodes and decoder cancellation state. Program counts
cover the four named fields, not every backend resource. These values do not
query GL or audio. External lease/callback counts are null because they are not
tracked; null must not be interpreted as zero. Facade flags are known source
references, not a runtime root dump. A role is sampled from policy at that stage:
callers that set SideScene after construction may first report a live role.

Observation is disabled by default and only enabled by an internal,
thread-scoped `SceneLifetimeDiagnostics.Begin`. It stores at most the requested
1–4096 records (default 256), drops overflow and reports DroppedRecords. Nested
observations restore the previous owner; disposal must be in order on the
owning scene/context thread. No observer callback or world object is retained.
Scene creation, load, successful cleanup and visual release publish records;
failed cleanup does not publish a successful completion. The headless visual
release stage confirms the headless early-return boundary, not native teardown.

The existing native-render-check `-transition-gc keep|skip-load|skip-end|skip-both`
control enables these records in its existing output. `-transition-rebind`
continues to use the existing weak-only teardown helper. No new GC policy switch
or collection was added. Production OnLoad and EndScene collections are unchanged.

## Source owner map

| Owner / role | Create, bind and publish | Shutdown / cancellation / disposal |
| --- | --- | --- |
| Foreground desktop/offline/client | [`Scene` constructor](../../../src/MphRead/Renderer.cs) owns players, state, RNG and bridge; live constructor assigns the three compatibility facades. `RenderWindow.BeginScene` publishes `_scene`, `LoadScene` activates it. | `RenderWindow.EndScene`: scene cleanup, GL unload, clear window ownership, existing collection on context thread. Facades remain save owners. |
| Standalone window | [`MatchStart.Launch`](../../../src/MphRead/Mods/Launcher/Portable/MatchStart.cs) owns the window and names the match. | `Run` returns after OnUnload cleanup; AfterMatch then saves, using-owned window disposes. |
| Dedicated match child | [`ServerSim.Start`](../../../src/MphRead/Mods/Network/ServerSim.cs) constructs headless live scene, publishes `_scene` after OnLoad. | `Stop` releases map usage, clears `_scene`, stops/resets network runtime and Read cache, existing memory-pressure collection on simulation owner thread. Initialization before publication needs separate failure-path validation. |
| Replay Theatre/Studio, killcam and canonical capture replica | [`PassiveReplayScene`](../../../src/MphRead/Mods/Network/PassiveReplayScene.cs) owns private scene/session/reader with ReplaySceneServices. Replica constructor leaves foreground facades alone. [`ReplayLiveWorld`](../../../src/MphRead/Mods/Network/ReplayLiveWorld.cs) queues bounded value records from callbacks and constructs/steps worlds on scene thread. | Passive Dispose releases poses, owned scene cleanup/GL, session. ReplayLiveWorld unsubscribes recorder events and releases its private world. |
| Replay lab | [`DemoPlayback.TakeControl`](../../../src/MphRead/Mods/Network/DemoPlayback.cs) detaches scene ownership from passive wrapper into `_lab`; [`BeginReplayLab`](../../../src/MphRead/Mods/Replay/SceneReplica.cs) explicitly rebinds foreground facades. | DemoPlayback.Stop releases replay audio, player/session and lab resources, clears shell/lab owners on scene thread. Foreground rollback is not yet an explicit lease; do not clear facades early. |
| Launcher hunter preview | [`LauncherHunter`](../../../src/MphRead/Mods/Render/LauncherHunter.cs) retains one process-lifetime `_scene`; NewSideScene calls live NewScene then sets SideScene, so construction changes facades. | DropSideScene clears owner then UnloadGl on context thread. Bounded preview retention is intentional; changing its ownership is outside this slice. |
| Thumbnail/cosmetic preview | Existing [`ThumbnailWindowCheck`](../../../src/MphRead/Mods/Diagnostics/ThumbnailWindowCheck.cs), [`CosmeticPreviewCheck`](../../../src/MphRead/Mods/Cosmetics/CosmeticPreviewCheck.cs) and render-window thumbnail paths own their windows/resources. | Window/context teardown; rendered preview acceptance remains required. These are native rendered roles, unlike the device-free test. |
| Map editor viewport | [`MapViewportGpu`](../../../src/MphRead/Mods/Launcher/Gui/MapViewportGpu.cs) owns renderer; [`CreateEditorRenderer`](../../../src/MphRead/Mods/Render/EditorViewportPass.cs) uses replica EditorSceneServices and rolls back initialization failure. | ReleaseRenderer unloads then clears renderer on UI/context owner thread; shell final cleanup releases map renderer. |
| Map editor playtest | Shell's plan launch goes through [`MatchStart.Begin`](../../../src/MphRead/Mods/Launcher/Portable/MatchStart.cs), ordinary foreground match lifecycle. | Shell EndMatch then returns to front/studio. Native editor open/playtest/return coverage remains required. |
| Async room prewarm | [`RoomPrewarm`](../../../src/MphRead/Mods/RoomPrewarm.cs) creates no Scene/GL. One-room files/models, generation, task and preparation lease are distinct owners. | Clear/Release invalidates generation; worker checks generation before publication and releases preparation lease. Other async texture/map/UI/audio completions are not certified by this observation slice. |
| Capture/export | Scene owns decoder cancellation source and output state; [`ReplayVideoExporter`](../../../src/MphRead/Mods/Replay/ReplayVideoExporter.cs) and replay capture own export/private worlds. | Scene cleanup OutputStop/cancel; UnloadGl releases replay output. Current live owner cleanup releases ReplayCapture world/KillCam. Native resource deletion stays on context owner thread. |
| Audio | [`Sfx`](../../../src/MphRead/Sound/Sfx.cs) implementation holds listener Scene; replay audio has explicit [`ReplayAudioOwner`](../../../src/MphRead/Mods/Replay/ReplayAudioOwner.cs) token. | Scene service policy determines Sfx shutdown; shutdown/close clears listener. [`AudioLifetime`](../../../src/MphRead/Sound/AudioLifetime.cs) owns process close coordination. Native audio failure tests remain separate. |

Root categories to inspect after eligibility include GameState.Current ->
SceneGameState.Owner -> Scene, PlayerEntity.LegacyRegistry -> players ->
OwningScene, optional NetAiClient._scene, audio listener, DemoPlayback
shell/lab/player, replay callbacks and bounded launcher preview/cache owners.
Rng.Current holds value RNG state; its identity flag does not establish a Scene
backreference. Read/RoomPrewarm caches are distinct from a retained scene graph.

## Device-free validation and limits

Run `dotnet run --project tools/nettest -c Release -- --scene-lifetime`. The test
uses real scene construction and cleanup with `initializeRuntime:false`, no
OnLoad, game assets, window, GL/audio device or network session. No-inlined
helpers leave only weak references and bounded scalar snapshots across the
collection boundary. The test first proves expected retention by foreground
facades, then restores the previous bindings solely in the diagnostic fixture
and proves eligible collection; a private replica is collectible independently.
It also checks snapshot reference-freedom, stage ordering/idempotent cleanup,
monotonic generations, bounds, thread isolation and disabled publication.
Diagnostic full collections/finalizer drain exist only in this test, not product
code or the existing native transition harness.

A PASS is evidence of the diagnostic contract and content-free owner release.
It does not establish native GL/audio lifecycle parity, production facade release,
post-match save parity, callback fencing, 10/100/500 rendered transitions or an
actual GC-root audit. Reuse the existing native transition controls for those
eligible teardown observations after the save handoff and remaining owners are
validated. Do not infer a leak from an early weak-alive target.

Measured on macOS with the task's .NET 10 SDK: Release nettest build passed
(one existing duplicate-using warning in PlayerNameChatTests); native-render-check
compiled with zero warnings/errors and was not run. `--scene-lifetime` passed
16 checks plus the existing transition-GC selfchecks; `--scene-setup-ownership`
passed with zero failures. No native graphics/audio process or network campaign
was started.
