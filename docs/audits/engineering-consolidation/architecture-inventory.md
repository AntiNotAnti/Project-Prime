# Engineering consolidation: Phase 0 architecture inventory

Snapshot: `91b1c0c6c44e8c90effd250729709f791a84765e` (`origin/main`, October 12, 2026). This is a source inventory and dependency baseline, not a runtime acceptance report. The primary checkout was clean at inspection; documentation is authored in the separate engineering-baseline worktree. No production code was changed by this inventory.

The full supplied `Project_Prime_Engineering_Consolidation_Plan.md` governs the queue below. Current source outranks historical prose. Keep fixed 60 Hz simulation, server-authoritative combat/results, owner-reported validated movement, OpenGL/GLES-only rendering, exact packet identities/layouts, historical replay readers, Balanced Mode, settings migrations and updater rollback. Resource and performance changes require evidence before implementation.

## Snapshot and quality gates

| Item | Verified baseline / evidence |
| --- | --- |
| Source SHA | `91b1c0c6c44e8c90effd250729709f791a84765e`; both HEAD and origin/main matched during read-only inventory |
| SDK contract | `global.json`: .NET SDK `10.0.401`, rollForward disabled, no prerelease; no global workload-set pin |
| Local runtime | Coordinator-reported `/Users/jarrett/.dotnet/dotnet`: SDK 10.0.401, runtime 10.0.12, macOS 27 arm64; local command output belongs in the execution manifest |
| Wire protocol | **53**, `src/MphRead/Mods/Network/NetProtocol.cs:2529`; `ARCHITECTURE-INVARIANTS.md` still stated 41 at this SHA |
| Replay versions | Readers support file formats 1–5; default metadata format is 3 and checkpointed archives/clips use 4/5 (`ReplayMetadata.cs:47`, `ReplayArchive.cs:219`, `ReplayFormatV3.cs:82`, `DemoFile.cs`). Historical protocol range 4–53 is best-effort at playback boundaries only (`ReplayIdentityCompatibility.cs:17`) |
| Replay checkpoint contracts | Replica envelope version **10**, `Mods/Network/ReplayReplicaCheckpoint.cs:24`; inner world capsule version **9**, `Mods/Replay/ReplayWorldCheckpoint.cs:25`; generated accessor contract `A4512223EC0CC49516DB00DD3733168DA6969DEA059E9DDE84B31062ADEA675A` (`ReplayCheckpointAccessors.cs:9`). These are different versions, not one universal replay schema number |
| Settings | `SettingsMigration.CurrentSchema = 10`; archive manifest format 1; settings archive limits 128 files, 16 MiB expanded total, 20 MiB archive (`Mods/SettingsMigration.cs:18`, `Mods/Settings/SettingsArchive.cs:13`) |
| Baseline CI | Coordinator verified all-success build [38343443470](https://github.com/AntiNotAnti/Project-Prime/actions/runs/38343443470) and release [38343469903](https://github.com/AntiNotAnti/Project-Prime/actions/runs/38343469903). CI coverage must not be represented as physical-device or exhaustive gameplay acceptance |
| Recent merged work | Bot AI #481–#485; #486 OpenGL pass telemetry; Android Mono startup layout/Release AOT commit `576a0709`; #487 workload-set CI scope. Confirmed by `git log`; preserve these changes |
| Open upstream dependencies | Coordinator checked #424 RmlUi (unvalidated draft), #423 impact acceptance train, and #472–#474 stacked authority/pickup work. Recheck before each affected branch; do not duplicate, retarget or infer merge readiness |
| Protection recommendation | Coordinator's fresh read-only query confirms main unprotected. Require the essential checks below and current PR approval against the latest head. No repository-settings mutation is authorized by this report |

Recommended required checks retain assets/OpenGL-only guard, server compile and startup smoke, fidelity oracle, renderer policy/final composite, pacing/input policies, settings/protocol/replay/network regression, maps/editor, Windows/Linux/macOS packaging and Android compile/native/startup acceptance. Resolve exact GitHub check names from the current workflow/check suite before configuring protection. Long asset-dependent renderer/reload/impairment tests remain explicit release/local gates with durable evidence.

## Assembly, package and build matrix

`src/MphRead.sln` contains MphRead, NcsfPlay and MphRead.Protocol.Generator. Android is deliberately outside the solution; tool harnesses are separate projects.

| Build node | References / packages | Build and platform contract |
| --- | --- | --- |
| Desktop game `MphRead.csproj` | Runtime → `NcsfPlay`; analyzer-only → `MphRead.Protocol.Generator`; OpenTK 4.9.4, ReFuel.StbImage 2.1.1; client-only Ktx2.NET 1.0.5 and OpenAL Soft native 1.23.1; Avalonia, Desktop, Headless, Skia, Fluent and Inter all 12.1.2 | net10.0, unsafe, nullable, embedded debug, single-file. Client defines MPHREAD_AVALONIA and MPHREAD_SHELL. Windows client RID is WinExe; other targets Exe. CI clients win-x64, linux-x64, osx-arm64, osx-x64 |
| Dedicated server, same project | Same source compilation with MPHREAD_SERVER; launcher GUI compilation and Avalonia/KTX/OpenAL-native/client assets excluded; OpenTK and NcsfPlay remain referenced | `-p:MphReadServer=true`; ReadyToRun on publish. CI linux-x64, linux-arm64 and win-x64 server packages. Windows binary ProjectPrimeServer.exe; elsewhere ProjectPrime. Headless startup is an execution contract, not proof that all graphics/audio assemblies disappear |
| Android `MphRead.Android.csproj` | Compiles `../MphRead/**/*.cs` a second time, excluding bin/obj, desktop Shell/UiSurface/UiBench/UiOverlay and launcher GL backgrounds; runtime → NcsfPlay, analyzer → protocol generator; Avalonia/Android/Fluent/Inter 12.1.2, OpenTK 4.9.4, STB/KTX with native assets excluded | net10.0-android36.0, minimum API 24, APK. Own activity/GL/input head; MPHREAD_AVALONIA without MPHREAD_SHELL; GL alias GraphicsApi, AL/ALC aliases AlEs/AlcEs. KTX natives supplied by artifacts for arm64-v8a/x86_64. TrimmerRoots.xml preserves reflected input types |
| `NcsfPlay.csproj` | CommunityToolkit.HighPerformance 8.4.2, SoundFlow 1.4.1, System.IO.Hashing 10.0.9 | net10.0, nullable; shared music decoder/playback dependency. SoundFlow has a native runtime, so include it in server artifact audits |
| `MphRead.Protocol.Generator.csproj` | Microsoft.CodeAnalysis.CSharp 4.14.0, PrivateAssets all | netstandard2.0 Roslyn component; referenced as Analyzer with ReferenceOutputAssembly=false. Keep it out of runtime dependency counts |
| Tool projects | Most reference the desktop game; standalone/linked-source policy checks have their own csproj contracts | Explicit CI invocations, not automatically covered by `dotnet build src/MphRead.sln` |

Source: `src/MphRead/MphRead.csproj`, `src/MphRead.Android/MphRead.Android.csproj`, `src/NcsfPlay/NcsfPlay.csproj`, `src/MphRead.Protocol.Generator/MphRead.Protocol.Generator.csproj`; `.github/workflows/build.yml:430,684,773,945`; `.github/workflows/release.yml:328,685`.

The exact CI Android invocation is `dotnet publish src/MphRead.Android/MphRead.Android.csproj -c Release -p:AndroidPackageFormat=apk -p:AndroidSdkDirectory=... -p:JavaSdkDirectory=... -o publish/android`, preceded by workload installation at 10.0.401, SDK dependency installation, native KTX builds and avares verification. Release additionally supplies signing/version properties. Do not invent AOT flags that override the restored default Release behavior.

The important compilation switches are ANDROID, MPHREAD_SERVER, MPHREAD_AVALONIA, MPHREAD_SHELL and DEBUG. Source gates distinguish desktop window APIs, server presentation, Android aliases and shell hooks; assembly extraction must preserve those differences. Broad shared-source compilation is debt to evaluate, not authorization to reorganize all source now.

## Owners, threads and lifecycle seams

| Domain | Current ownership / execution boundary | Existing safeguards and remaining audit question |
| --- | --- | --- |
| Simulation world | Scene owns `GameState`, `ScenePlayerRegistry` and `ISceneServices` (`Scene.cs:21–37`, `ScenePlayerRegistry.cs`, `SceneServices.cs`). LiveSceneServices/LivePlayerReplicationHost are singletons; replica services own independent world policy | Scene/player/services seams already exist. Replica WeaponPhase differs from static live NetSession phase. Extend narrow seams only after collision characterization |
| Network match | Static partial `NetSession` owns transport, peer list, scratch, frame/slot state and replay sinks (`NetSession.cs:60–199`). `DedicatedServer`/`ServerSim` resolve authoritative combat; LocalServer isolates child match processes | Static session is intentional per isolated process. Do not convert to multi-match runtime. Characterize socket shutdown and cross-world adapters first |
| UDP transport | Instance `NetTransport` owns socket, receive worker, optional impairment worker, pooled packet rentals, reliability connections and queues (`NetTransport.cs:136–242,328,401,1053`) | Live queue capacity 2048 and 320 max connections; priority budgets/reservations and coalescing already present. Playback inbox is a separate ordered path. Dispose permits 2 s graceful Bye drain then up to 1 s joins per worker: inspect actual callers before changing UI exits |
| Render resources | Scene-associated texture manager/transients/retained world and context caches; model owners reference-counted in static `SharedModelResources`. DesktopRetainedGeometry owns VBO/IBO/VAO/atlas lists; GraphicsApi owns uniform/program-layout cache; ShadowMap owns its FBO attachments | Texture budget and decoding limits, frame-transient reclamation, room-plan limits (1024 IDs/256 plans), 64 MiB combined-atlas budget, failure fallbacks and generation invalidation already exist. Tracked zero-owned counts do not prove native/process memory boundedness |
| Desktop UI/render thread | RenderWindow owns GL context and one loop; Shell drives UiSurface/UiOverlay and Avalonia dispatcher in that window (`Mods/Launcher/Gui/Shell.cs`, `UiSurface.cs`, `Mods/Render/UiOverlay.cs`) | One-window architecture already implemented. UI, Studio preview and live scene share native lifetime boundaries needing route/lifecycle tests |
| Android | MainActivity is AvaloniaMainActivity; GameView starts background `ProjectPrime GL` thread (`GameView.cs:408`), owns EGL/window/context/pacing; AndroidUiSurface renders in-game result/training panels | Preserve UI/GL thread boundaries, pause/resume generation and aliases. Current pacing policy tests are not battery/thermal/input-latency measurements |
| SFX | Static Sfx facade/active instance; immutable bank prewarm Task.Run; instance OpenAL context/device created on match thread, shutdown Task.Run destroys old native lifetime (`Sound/Sfx.cs:193,1600,1656`) | AudioLifetime registers only initialized music, signals cancellation, then waits up to 2 s at final shutdown. Historical stack sample proves overlapping create/destroy stalls, not an audio memory leak; ensure all lifetimes are tracked, not only latest task |
| Music/audio callback | Static MusicPlayer load task and SoundFlow audio engine, NcsfPlay decoder and raw provider (`Sound/Music.cs:872`, `NcsfPlay.csproj`) | Shutdown request/stop flags exist; measure native device/context and worker count in isolation, preserve callback nonblocking behavior |
| Replay | Private readers/playback session/replica scene; bounded immutable ReplayPayload/timeline and explicit schemas; ReplayStorageJobs limits disk operations to two workers; ReplayEncoderJob owns cancellation and external process | `ReplayStorageJobs.cs`, `ReplayWorldCheckpoint.cs`, `ReplayPlaybackSession.cs`, `ReplayEncoderJob.cs`. Semaphore concurrency limit alone is not proof of bounded callers waiting for slots; audit producers/export backlog separately |
| Map editor/build | MapAutosaveService owns one writer plus one replaceable pending detached snapshot; MapSourceWatchService owns timer/watchers; MapBuildScheduler owns detached, single-flight bounded builds | Existing two-worker/32-job build scheduler and compiler/content caches should be reused. Watch callbacks report notification; UI owns review/reload. Inspect disposal/cancellation and undo memory using existing checks |
| Background map preparation | HostedMapRequests and LobbyMapPreparation own cancellation/timeouts/tasks for staged downloads; publication happens at session owner after room lease release | Preserve immutable package identity, staging and atomic publication; do not replace with arbitrary worker publication |
| Telemetry and settings | NetTelemetryWriter owns background writer and bounded queue; static input/menu preferences, SettingsPersistence.Gate and versioned migration/atomic replacement own persisted stores | Existing rotating diagnostic logs/settings/archive limits are baseline. Audit rejected ingress and log privacy without inventing a vulnerability |

Static process owners also include room/model/resource caches, input/preferences, DebugLog and native GL policy state. They require a boundedness/reset/reference audit rather than blanket removal. The collision scratch is the unusually direct reentrancy hazard: `CollisionDetection.cs:60–65,390` has mutable static active/inactive lists, queue/stack and shared face HashSet, reused by multiple query paths. No observed race is asserted.

## Feature reachability and compatibility classification

| Classification | Feature / proof | Consolidation treatment |
| --- | --- | --- |
| Active | OpenGL/GLES renderer selected by GraphicsBackendPolicy; desktop GraphicsApi and Android GLES adapter. Only OpenGL is supported (`GraphicsBackendPolicy.cs:94–141`) | Preserve as sole game backend; run `tools/check-opengl-only.sh` |
| Active | Avalonia desktop shell and Android activity; Shell Run is reached by Studio CLI (`ModEntry.cs:625–631`), Android MainActivity inherits AvaloniaMainActivity (`MainActivity.cs:59`); both csproj files package toolkit | Do not delete dependencies until full replacement route parity is demonstrated. No RmlUi implementation/package was found in src/tools/workflows beyond historical release comments/baseline metadata; #424 is an upstream dependency |
| Active but intended retirement | Enhanced Hunters settings toggle (`Gui/Workspaces/OfflineWorkspace.cs:405`), lobby state/export (`LobbyScreen.cs:777,1246`), CLI `-enhancedhunters` (`ModEntry.cs:1117`), process frame hook (`PlayerProcess.cs:67`), spawn hook (`PlayerEntity.cs:729`), fire/hit hooks | Separate retirement family; normal Balanced Mode is distinct and disables EnhancedHunters.Enabled (`EnhancedHunters.cs:16–17`). Removing just UI leaves gameplay hooks reachable |
| Active compatibility data | Enhanced flag bit **1024**, Balanced Mode bit **8192** (`MatchDefinition.cs:51`); enhanced state in canonical packet suffix (`NetProtocol.cs:1795–1798`) and replay schema field (`ReplayWorldSchemas.cs:46`) | Keep wire widths/IDs and historical decoding. Any layout removal must be explicitly versioned; characterize settings migration and rejected unsupported online modifiers |
| Active defaults | 25 independently controlled modernizations: VAO, DYNAMIC_STREAM, UNIFORM_REVISIONS, STATIC_CACHE, SHADOW_FAST, HUD_SAMPLERS, DEPTH_BATCH, ATLAS_EVICTION, HUD_DISCARD, DEPTH_SHADER, GLES_UNIFORMS, HUD_GLYPHS, HUD_SPRITES, DEPTH_PREP, PROCEDURAL_BATCH, ROOM_CACHE, DEPTH_KEEP, SHADER_VARIANTS, ROOM_GROUPS, PLAIN_SHADER, SHADOW_PREP, ROOM_IDENTITY, MESH_OBJECT, UNLIT_SHADER, SHADOW_GROUPS (`OpenGlCapabilities.cs:63`) | Preserve capability/material fallbacks and independent explicit 0 overrides; FORCE_LEGACY/-gllegacy overrides requested enablement. Enabled policy is not device capability/performance acceptance |
| Experimental | DEPTH_VERTEX and PROCEDURAL_STREAM not default enabled; live callers in `RetainedRenderGraph.cs:747,909` and checks in `OpenGlModernizationCheck.cs:173` | Keep opt-in pending matched new performance/visual evidence |
| Retired but compatible | GraphicsBackend Auto/DirectX12/Vulkan/Metal enum/spellings, startup guard shims and historical post-FX settings | Runtime rejects modern backend requests, normalizes persisted renderer to OpenGL and post-FX off (`SettingsMigration.cs:27,101–105`); distinguish stale comments from live alternate backend code |
| Completely dead candidates | Unreferenced compatibility shim methods/comments, artifacts or aliases after exhaustive reference/settings/package check | No whole feature is declared completely dead from string search alone. Check callers and persisted data before removing anything |

## Maintainability inventory and warnings

Line counts were taken from tracked source-tree `.cs` files excluding bin/obj. Priority below considers responsibilities and compatibility cost; size alone does not justify a split.

| Hand-maintained module | Lines | Responsibility / safe future seam |
| --- | ---: | --- |
| `Entities/Players/PlayerAi.cs` | 12,136 | Legacy navigation/behavior plus recent tactical integration; characterize 1/4/8 bots before isolating decision/motor/perception adapters |
| `Renderer.cs` | 9,886 | Scene initialization/submission/presentation, resource teardown and RenderWindow lifecycle; begin with native owner/transition boundaries |
| `Entities/Players/PlayerHud.cs` | 4,665 | Native HUD, maps/dialogs and new notifications; only allocation changes supported by profiling |
| `Menu.cs` | 4,139 | Settings/console/native menu; persisted-schema boundary before UI cleanup |
| `Entities/Players/PlayerInput.cs` / `PlayerEntity.cs` | 3,622 / 3,608 | Input/player lifecycle, Balanced/Enhanced hooks and collision/damage; separate adapters after invariants and characterization |
| `Mods/Launcher/Gui/SettingsView.cs` / `MapStudioScreen.cs` | 3,066 / 2,830 | UI routes/settings and preview/editor orchestration; migration must prove full route parity |
| `Entities/BeamProjectileEntity.cs` | 2,985 | Projectile physics/combat/render coupling; collision/workspace gate comes first |
| `Mods/Network/NetHitClaims.cs` / `NetProtocol.cs` | 2,840 / 2,806 | Authority proof/validation and fixed packet codecs; preserve byte fixtures and upstream #472–#474 work |
| `Mods/ModEntry.cs` / `Network/DedicatedServer.cs` | 2,770 / 2,650 | Product/test CLI and server lifecycle; do not mix ownership refactor with option retirement |
| `Entities/Players/PlayerProcess.cs` | 2,612 | Simulation transitions/alt forms/spawn/combat; respawn temporary list starts at method line 2388 |
| `Formats/CollisionDetection.cs` / `Entities/RoomEntity.cs` / `Network/NetSession.cs` | 2,157 / 2,156 / 2,064 | Shared query scratch, world visibility and static match owner. Smaller but high risk due to deterministic/live-world coupling |

`MemoryClasses.cs` (7,848 lines), Metadata/Weapons (6,706), Rooms (4,115), Metadata (3,969) and Enemies (3,087) are largely data/layout catalogs. Audit provenance/change patterns before ranking as hand-maintained architectural monoliths. `Mods/Replay/ReplayCheckpointAccessors.cs` (3,981) explicitly says generated by `tools/replay-schema-generator`; exclude it from split/cleanup targets. Change schemas/generator and regenerate with contract validation.

| Suppression category | Scope / rationale found | Actionable audit question |
| --- | --- | --- |
| Avalonia private API AVA3001 | Client csproj and Android csproj; exact 12.1.2 pins plus unstable ITopLevelImpl/IRenderTimer access | Expected notice; retain until UI dependency/parity decision, verify packages when upgrading |
| Android platform CA1416 | Entire Android csproj; comments cite unreachable console extractor/menu paths, with redirected/caught setup exception | Broad suppression can hide newly reachable platform APIs. Compile with suppression removed in isolated diagnostic invocation and classify before narrowing |
| Android native XA0141 | Entire Android csproj; comment says expected obsolete after Skia 3.119 alignment | Candidate to remove only after actual Android build proves no recurrence; do not infer from comment |
| Obsolete/platform CS0618, CA1422 | Scoped pragmas in AndroidPerformance, ApkInstaller, MainActivity, AndroidGamepadHaptics and GlEs | Mostly legacy Android compatibility APIs; inspect target/API guards rather than blanket deletion |
| Single-file IL3000 | Scoped in ThumbnailBatch; framework-dependent launch branch rationale | Verify package launch/thumbnail smoke; keep narrow documented suppression if still correct |
| Style IDE0060 | Two Effects.cs attributes | Non-runtime style; low priority |
| Stream correctness CA2022 | Nettest Protocol19Tests fixture at line 151 | Test intentionally malformed read fixture; preserve focused rationale |
| Disabled code quality | `src/.editorconfig`: CA1806 (ignored returns), CA1836, CA2208 and IDE0130 none; CA1822/CA2211/CA1862/CA1859 silent, style unused-value preferences none | CA1806/CA2208 and static mutable-field CA2211 merit targeted ownership/argument validation review. Silent style/performance suggestions are not compiler warning counts |

**Coordinator's baseline solution Release build: PASS, 30 warnings, 0 errors.** Warning counts by project/category still require classification from the execution log. Attach build logs with SDK, configuration, project and suppression state. A suppressed green build does not demonstrate unsuppressed diagnostics are empty. Do not repeat old csproj comments that the build has no other warnings as current evidence.

## Task-to-existing-code map and dependency gates

| Plan family | Existing implementation to preserve/reuse | Eligible next work and gate |
| --- | --- | --- |
| 0 baseline/doc drift | CI workflows, protocol/replay checks, performance/native/fidelity tooling and historical evidence manifests | Correct current 41→53 prose in isolated doc PR; record outputs, workflow SHA and full environment. Hardware matrix remains explicit NOT_RUN until exercised |
| 1 memory attribution | `tools/native-render-check`, render-resource-check, allocation-failure/fallback arms, retained/native counters, `opengl-completion-memory.*` | Current-binary ≥60 cycles plus no-audio, audio-only, GL-only, no-Studio maps, Studio-only and replay-only controls; VM/native attribution and quiescent plateau before choosing fix |
| 1 lifecycle / GC | `Sfx` async teardown, AudioLifetime final drain, scene/transient/model/context ownership, forced init GC (`Renderer.cs:675`) and exit GC (`Renderer.cs:8750`) | Fix only attributable ownership/concurrency failure; preserve all task identities and fallback pixels. GC policy requires transition/residency/frame-tail A/B after lifetime fix |
| 2 obsolete systems/UI | Live Enhanced Hunters gates/wire/replay fields; active Avalonia shell/activity; OpenGL policy/settings normalization | Separate compatibility-sensitive retirement PR; UI route inventory/#424 validation before any dependency removal. Do not remove Balanced Mode or historical layouts |
| 3 renderer | Retained geometry/world, descriptor and sort caches, room groups/atlas, shader variants/per-program palette cache, HUD caches, shadows/depth policies, pass/submission profilers | Attribute remaining immediate submissions on identical fixture after memory owner gate. Preserve translucent/stencil/order/depth; repeated ABBA + image/depth parity, zero owned-resource leftovers |
| 4 collision/scene | SceneServices, ScenePlayerRegistry, replica world separation already exist; collision static scratch remains | Nested/independent-scene/render-vs-gameplay query characterization before scoped reusable workspace; preserve candidate/tie/floating evaluation order, gameplay and replay hashes |
| 5 network/server | Bounded/coalesced priority queues, reliability and generation/life fences, authoritative ServerSim and process isolation; transport lock telemetry already exists | Profile and characterize owner seams after collision/scene gate; exact wire fixtures, impairment/load/reconnect acceptance; recheck stacked upstream PRs; no broad NetSession rewrite |
| 6 replay/editor | Generated checkpoint accessors, explicit world schemas, immutable bounded timeline, private replica sessions, storage slots, encoder cancellation, editor dirty caches/history, MapBuildScheduler | Measure cold/warm seek/export/viewports and producer backlog after native/scene gates. Preserve exact custom/HD identity, historical readers, cancellation cleanup; no premature separate Studio runtime |
| 7 AI/HUD/input | Bot AI #481–#485, PlayerAiTactical/perception/motor/hunter/adaptation code, frame pacing and input policy checks | Profile fixed 1/4/8 bots; navigation `float[10]` at PlayerAi.cs:10164 and notification sort/list at PlayerHud.cs:4127 are candidates, not proven bottlenecks. Physical Android battery/latency gate before pacing changes |
| 8 build/CI/warnings | SDK/workload/AOT startup fixes, platform publishes, avares/native/asset guards, server smoke/updater acceptance | Narrow warning fixes and package-content audit; platform-neutral extraction only at established seams, cross-platform compile/package smoke. No size claim without bytes |
| 9 ingress/robustness | Packet-size rejection/pooling, map package/hash/staging, replay bounds, settings archive limits, updater rollback/hash/atomic publication | Failure injection for malformed/truncated/oversized payloads, traversal/symlink/expansion/cancel behavior; establish concrete defect before security claim/fix |
| 10 integration | Existing CI plus local asset-dependent test commands | Full reviewed-SHA matrix and remaining risks. Do not claim full acceptance while native boundedness or required hardware gates remain open |

Historical evidence is already substantial but not current-binary acceptance. `docs/performance/opengl-modernization-results.md:14,27–29` records 25/27 defaults, ~450 immediate draws and small/mixed measured gains on its identified fixture, with shader-layout fix already applied. `docs/performance/opengl-completion-memory.md` identifies an earlier binary, resource release/fallback passes and unresolved rising RSS; it predates that shader fix and lacks quiescent plateau/native category attribution. `docs/architecture/replay-map-performance.md` records previous checkpoint allocation/seek improvements; do not redo them as novel work. `docs/architecture/opengl-only-migration.md` contains staged/historical status text that needs explicit supersession, not resurrection of removed backends.

The coordinator's fresh pinned-baseline execution passed solution Release (30 warnings, 0 errors), OpenGL-only guard, render-resource-check (0 failures) and replay format (3,034 checks). Current native MVID `bb5922d1-051d-4a89-940b-d14096c550fe` passed 60 cycles/120 scene arms, all five injected allocation-failure stages, 60 Studio sessions with three cache rebuilds each, exact fallback pixels and zero tracked owned resources after unload. First/last ten-arm RSS means were 704.87/529.55 MiB, GC committed 675.72/289.19 MiB and managed live estimate 580.58/217.56 MiB. Final RSS 525.03 MiB became 526.77 after audio wait, then 400.47 after GC/drain (GC committed 132.36 MiB). All 120 audio shutdown tasks completed without faults. **This run did not reproduce the historical rising RSS, but different binary/runtime and no controlled historical A/B prevent a causal fix claim. Native/VM attribution, isolation arms and long quiescent plateau remain open.** Exact commands, hashes and raw evidence belong in the coordinator's execution manifest. Protocol documentation correction is independently open as [PR #488](https://github.com/AntiNotAnti/Project-Prime/pull/488).

## Ranked risk backlog

Triage score = impact (1–5) × exposure/urgency (1–5). This is engineering priority, not bug probability. Evidence distinguishes observed behavior from a source hazard or an untested hypothesis.

| Rank | Score / priority | Concern and evidence | Required closure / dependency |
| ---: | --- | --- | --- |
| 1 | 25 / P0 | Native/process boundedness unresolved on historical 60-cycle workload; owned counters returned to zero while RSS rose and residual remained. No proven leak/source attribution | Reproduce current MVID/hash, controlled isolation and native/VM/handle/audio/worker counters, slope and long quiescence; retain P0 if evidence unavailable |
| 2 | 20 / P0 | Historical stack captured OpenAL create/destroy overlap stall; Sfx still asynchronously destroys old context. Not proof audio causes RSS growth | Characterize all outstanding lifetimes/partial init/cancel and native overlap; separate lifecycle fix only after current evidence |
| 3 | 20 / P1 | Collision queries share static mutable scratch across world/query paths | Characterize nested and separate-scene queries and deterministic ordering, then allocation-bounded scoped workspace; source hazard, no observed race |
| 4 | 20 / P1 | Enhanced Hunters still publicly selectable/live despite retirement intent, and embedded in wire/replay schema | Retirement/compatibility/Balanced fixtures; preserve fixed bits/layout/readers, migrate settings and reject unsupported rules explicitly |
| 5 | 16 / P1 | UI migration discrepancy: active Avalonia desktop+Android and upstream unvalidated #424 | Complete route/dependency matrix and replacement parity on every target before package/resource removal |
| 6 | 16 / P1 | Renderer forces compacting Gen-2 init collection plus exit collection | Measure transition pause/residency/frame tails after lifetime attribution; do not remove based on intuition |
| 7 | 15 / P1 | Remaining world immediate submission and mixed Ultra-shadow results in historical fixture | Current CPU/GPU/present attribution, exact solo/bot/color/depth witness and repeated matched A/B; no draw-count-only gain claim |
| 8 | 15 / P2 | NetTransport Dispose permits blocking graceful drain and worker joins; static live/replica adapters remain | Trace caller/thread ownership, loss/reorder/disconnect stress, exact wire/replay timing; no disruptive rewrite |
| 9 | 12 / P2 | Broad Android CA1416/XA0141 plus ignored-return/argument/static-field diagnostics hide signals | Isolated unsuppressed build classification, reachable API check and native package validation; narrow justified fixes |
| 10 | 12 / P2 | Replay/Studio/editor long-session/export cancellation and waiting producer counts need current evidence despite existing bounded caches/workers | Repeated seek/export/encoder failure/editor/playtest/cancel fixtures and native/context owners after phases 1/4 |
| 11 | 12 / P2 | Ingress/archive/updater malformed input/expansion/privacy are high-impact boundaries but no vulnerability is established | Focused hostile input and cancellation fixtures backed by concrete findings; preserve valid historical content |
| 12 | 9 / P2 | AI/HUD scratch/materialization and Android pacing may cost CPU/allocations, not yet profiled at this SHA | Fixed fixture/allocation timing and AI behavior; physical-device thermal/input evidence for pacing |
| 13 | 8 / P2 | Desktop/Android shared-source wildcard, server residual managed dependencies and large hand-maintained files | Package bytes/native inventory and cross-platform builds; narrow seams before shared-assembly extraction |
| 14 | 6 / P0 quick slice | Current protocol doc 41 versus source 53, stale staged render statuses and historical benchmark prose | Small source-backed documentation correction, history labels without modifying measured reports |

## Reproducible execution/evidence manifest contract

For each execution record: SHA and worktree, SDK/runtime path/version, OS/device/GPU/driver, assembly hash/MVID, build flags, command, fixture/map/hash, bot/player count, quality/shadows, 1920×1080 or stated exception, VSync/cap, warmup/duration/sample count, input/RNG, profiling/readback effects, run order and artifact hashes. Store summaries and bounded outputs here; avoid committing bulky raw machine logs or cartridge data.

Use repository commands, not imagined equivalent flags:

```sh
dotnet build src/MphRead.sln -c Release
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true
bash tools/check-opengl-only.sh
bash tools/run-perf-suite.sh ./ProjectPrime perf-results
python3 tools/compare-perf.py baseline.json candidate.json
```

Select actual SDK/binary paths and the exact Android CI publish command above. Assets/context are required by real renderer/replay/scene tests. Native-render-check covers 60/120/144/240/uncapped, 240-step warmup, fixed seeds/input, separate submission/present distributions and explicit capture-only readbacks; follow `tools/native-render-check/README.md`. Standard perf-suite 60/120/144 coverage is not automatic 240/uncapped acceptance. Source guards and synthetic/no-assets tests cannot establish native platform performance.

| Phase 0 workload requirement | Inventory status | Evidence slot for execution owner |
| --- | --- | --- |
| Local SDK/runtime and desktop/server/generator builds | Coordinator solution Release PASS (30 warnings, 0 errors); separate local server build NOT_RUN in this report | Environment/build log path, warning categories per project |
| Android Release/AOT/package/native/startup | Baseline CI reported PASS; new local physical/device execution NOT_RUN | CI/job evidence plus actual ABI/device and publish flags |
| Renderer solo/two/eight, transparency/projectiles, shadow Off/Low/High/Ultra, custom maps, replay/Studio | NOT_RUN by inventory agent | Same binaries/maps/settings, CPU/GPU/present/state/upload/draw telemetry, color/depth hashes |
| 60/120/144/240/unlimited, p50/p95/p99/p99.9 and lows/drop steps | NOT_RUN by inventory agent | Separate profiles and unprofiled controls; matched warmup and sample count |
| Server 2/4/8 players, 8 bots, jitter/loss/reorder, reconnect/JIP/rotation | NOT_RUN by inventory agent | Step/alloc/message/retry/queue stats and authority/gameplay tests |
| Lifecycle ≥60 cycles, longer confirmation, long quiescence and isolation arms | Coordinator 60-cycle/120-arm native resource/fallback PASS; longer confirmation/quiescence/isolation NOT_RUN in this report | Current SHA/MVID, RSS/VM/GC/LOH/native/audio/worker/handle/cache counters and failure fallback witnesses |
| UI/editor/audio/thumbnail/context loss/Android pause-resume transitions | NOT_RUN by inventory agent | Observed native events and route logs, distinction between simulated and physical hardware |

Runtime validation and hardware measurements are delegated to the consolidation execution owner. Replace slots only with actual outputs. This source report satisfies inventory/mapping/backlog work; it does not claim Phase 0 workloads or Phase 1 memory acceptance complete.
