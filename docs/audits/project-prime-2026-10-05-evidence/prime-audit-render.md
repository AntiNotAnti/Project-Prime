# Project Prime rendering, platform, frame-loop and HD asset audit

Source: clean main a68627d0, inspected in `/Users/jarrett/.codex/worktrees/engineering-audit/Prime Hunters Online`. All source anchors below are relative to that checkout. Product source was not edited. Read ARCHITECTURE-INVARIANTS.md before tracing paths. This is a scoped evidence notebook for consolidation into the full audit; parent owns the known main build failures and other subsystems.

Scores use the requested 1–10 scales: impact = player/developer benefit, risk = implementation regression risk, effort = implementation/test cost, confidence = strength of diagnosis. Native driver behavior, GPU milliseconds, physical Android stability and actual room memory totals were not measured here. Static defects, deterministic source-model counterexamples and hypotheses are distinguished explicitly.

## Ranked findings

| ID | Finding | Severity | Impact | Risk | Effort | Confidence | Evidence classification |
|---|---|---|---:|---:|---:|---:|---|
| R1 | Android match-end skips scene resource release; static shared model leases accumulate | High | 8 | 3 | 3 | 10 | Confirmed static ownership defect |
| R2 | Surface destruction acknowledges release before native detach, and can time out while rendering still owns surface | High | 8 | 5 | 4 | 9 | Confirmed API contract violation; physical crash impact requires reproduction |
| R3 | Retained multi-draw atlas removes entries without reclaiming ranges/pages | High | 8 | 4 | 4 | 10 | Confirmed allocator/lifecycle defect |
| R4 | Hi-Z reduction drops final rows/columns at odd mip dimensions | High | 7 | 3 | 3 | 10 | Confirmed shader arithmetic defect; source-model reproduction |
| R5 | Desktop mouse capture never enables native raw motion | Medium | 7 | 2 | 2 | 10 | Confirmed repo + pinned dependency + primary API docs |
| R6 | RGBA KTX fallback discards authored color/data mip chain | Medium | 7 | 4 | 4 | 10 | Confirmed decode/upload call chain |
| R7 | GPU resource telemetry/lifetime tests omit new indirect/Hi-Z/atlas/PBR resources | Medium | 6 | 2 | 3 | 10 | Confirmed coverage/measurement gap |
| R8 | Android render/build exceptions omit scene cleanup | Medium | 6 | 3 | 3 | 9 | Confirmed missing cleanup; residual side effects depend failure point |
| R9 | Temporal occlusion does not account for moving occluders | Medium | 7 | 5 | 5 | 9 | Highly likely visible disocclusion bug from confirmed algorithm limitation |
| R10 | Failed HD geometry compilation keeps successful partial texture uploads pinned | Medium | 5 | 4 | 4 | 9 | Confirmed error-path residency defect; trigger dependent |

### R1. Android normal match-end never relinquishes shared model leases

**Locations/call chain:** `src/MphRead.Android/GameView.cs:599` calls End after Loop exits; `:1317–1321` End calls `scene.DoCleanup()` and drops Scene. No `UnloadGl` call exists in Android sources. Run finally `:514–518` releases surface then destroys graphics context; `:850` modern Shutdown or EGL destruction `:861` releases driver resources. Compare desktop `src/MphRead/Renderer.cs:8646–8653`, which does both DoCleanup and UnloadGl. Scene InitTextures `Renderer.cs:1439` retains each Model through `_modelLeases`; `src/MphRead/Mods/Render/SharedModelResources.cs:10–11` stores strong Model keys in a process static Owners dictionary. Its Release `:12–23` is the only removal path. Normal scene-wide release is `Renderer.cs:5522–5525` inside UnloadGl, never reached on Android.

**Trigger/current behavior:** Enter and leave matches repeatedly on Android, OpenGL ES or Vulkan. Room/entity-specific releases `Renderer.cs:3698` can remove some models, but all remaining scene model leases are abandoned at End. Every new non-replica scene clears Read's cache `Renderer.cs:340–343`, so fresh Model objects arrive in the next match while old model objects remain in static Owners. This is a managed lifetime leak independent of native context destruction. Do not claim old display-list IDs necessarily break the next match: constructor cache reset prevents that simpler scenario.

**Impact:** Increasing managed Model geometry/recolor/animation retention across matches; GC cannot collect static dictionary keys. Native driver objects do die with DestroyContext, so the proven issue is CPU data/ownership accumulation. Size per match and Android low-memory failure frequency require measurement.

**Fix:** End/finally should use one idempotent scene disposal routine on the render owner while graphics context/device is alive. Call DoCleanup then UnloadGl before discarding Scene and before ReleaseSurface/DestroyContext. Audit private replay/editor scene ownership so shared leases are decremented, not globally cleared. Don't simply clear Owners, which could destroy resources another scene owns.

**Regression/benchmark:** Headless ownership test or injectable graphics API: 50 normal scene create/load/end cycles returns static lease count and reachable model count to baseline after GC. Physical Android lobby→match→lobby loops under GLES and Vulkan with managed heap snapshots and native resource tracking, compare resident bytes after identical room cycles. No speedup number asserted.

**History:** GameView End shape predates recent lease integration (blame starts 6d69b09c); interaction became hazardous with shared resource ownership and deterministic scene cleanup. Explain as independently sound context destruction + new managed ownership incorrectly integrated.

### R2. Android surface callback can return before render thread stops touching the surface

**Locations:** `GameView.cs:291–303` forwards SurfaceDestroyed to SurfaceGone. `:469–491` waits on `_holdingSurface`, but times out after constant SurfaceReleaseMs=2000 (`:334`) and returns even while graphics thread is mid-load. ReleaseSurface `:810–824` sets `_holdingSurface=false` and pulses waiters; actual `ModernGraphicsCompat.DetachAndroidWindow`, ANativeWindow_release and EGL MakeCurrent/DestroySurface occur later at `:826–840`. `ModernGraphicsCompat.cs:362–370` Detach performs texture release/flush and device window replacement, so the early acknowledgement covers real native work. Device window replacement `ModernGraphicsDevice.cs:158–168` unconfigures/releases surface. BuildScene occurs after a copied holder is bound `GameView.cs:562–591` and can run synchronously through OnLoad `:909–920` before Loop observes holder loss.

**Why confirmed:** Android's SurfaceHolder.Callback contract requires the render thread to stop touching Surface before surfaceDestroyed returns. The code signals completion before that work and explicitly lets timeout return while ownership remains. [Official Android callback contract](https://developer.android.com/reference/android/view/SurfaceHolder.Callback). ANativeWindow reference ownership protects the pointer, not the validity of the destroyed surface's buffer queue.

**Likely behavior:** Rotation/background/surface replacement during load or slow native detach can cause a stale swap/acquire, render error/fallback, or driver-dependent crash. A specific crash was not reproduced. The comment asserting swap fails rather than crashing is not proof of safety.

**Fix:** Signal detached only after native release completes in finally. Use surface generation/cancellation fencing before any bind/load/draw. Keep lengthy scene decoding/loading independent of the window surface (offscreen/persistent context work or cancellable load slices), so UI callback completion is bounded without breaking Android's ownership rule. Removing the timeout without fixing blocking load can create UI stalls/ANRs; do not ship that one-line change alone.

**Test:** Mock detach deliberately blocking after lock release; surfaceDestroyed must remain pending until no native surface operation can execute. Race SurfaceGone against BindSurface/BuildScene with generations. Physical rotate/background/foreground during heavy load and device recreation, GLES and Vulkan, verify no native call follows callback completion.

### R3. Retained multi-draw atlas grows for the lifetime of the desktop renderer

**Locations:** `ModernGraphicsCompat.MultiDraw.cs:14–22` owns page capacity and monotonically increasing vertex/index cursors. Minimum allocations at `:36–37` are 16 MiB vertices +4 MiB indices. EnsureRetainedMultiDrawEntry `:91–190` searches only remaining cursor capacity (`:120–130`), allocates new pages `:134–164`, uploads and increments cursors `:169–183`. ReleaseRetainedMultiDrawGeometry `:808–813` removes dictionary entries and explicit-normal cache entries only. DisposeRetainedMultiDraw `:815–830` frees every page only on renderer Dispose (`ModernGraphicsCompat.cs:940`). `ModernGraphicsCompat.Geometry.cs:272–284` correctly frees ordinary geometry buffers while calling this non-reclaiming atlas path.

**Trigger:** DX12/Vulkan with supported multi-draw indirect feature. GPU visibility preparation promotes eligible room meshes (`MultiDraw.cs:193–205`). Desktop shell keeps renderer across room/rematch scene disposal (`Renderer.cs:8646–8653`). Removing old scene lists invalidates atlas entries, then subsequent maps allocate fresh regions with no reuse.

**Impact:** GPU atlas capacity tracks cumulative promoted geometry over all rooms/rematches, not live geometry; can eventually cause GPU memory pressure. Minimum 20 MiB per newly allocated page is a source fact, not a measured room cost. Also duplicates ordinary retained geometry buffers, so the atlas deserves explicit budget/accounting even when healthy.

**Fix:** Reference-count/free page ranges or use room-owned arenas reclaimable at owner boundary. Free empty pages at safe queue boundary; preserve shared scene references and dense argument offsets. Avoid global reset while private replay/editor scenes still draw those pages.

**Test/benchmark:** Repeated allocate→release→allocate same/switching rooms with native resource capacity metrics; live atlas bytes plateau after warmup. Compare CPU/direct, indirect and multi-draw captured frames after recycling. Run DX12, Linux Vulkan and MoltenVK where feature is supported. Source-model `/tmp/prime-render-repro.py` demonstrates monotonic allocator behavior with explicitly assumed sizes (not real room measurement).

**History:** Introduced by atlas commit167793f42 on2026-10-03; dense buckets added in subsequent df4de4aa/f349894c series. This is a fresh integration regression candidate.

### R4. Odd-sized Hi-Z mips drop depth samples and are not conservative

**Locations:** `ModernGraphicsCompat.GpuVisibility.cs:466–492` creates native mip chain at actual viewport dimensions; native mip sizes halve with floor. BuildGpuHiZ `:590–600` CPU dispatch bounds use ceil-halving but shader uses actual destination dimensions. WGSL reduce `:883–907` reads exactly a 2×2 footprint based on destination id (`:894–903`). With source width5 and destination width2, source column4 is never included. The analogous last row disappears for odd heights. hiz_at `:975–980` maps screen pixels by 2^mip and clamps them onto final destination texel. Occlusion `:1083–1097` uses max-depth comparisons that require every covered texel's far depth to survive.

**Trigger:** Normal viewport sizes eventually become odd, e.g1920×1080→240×135→120×67. Depth1 clear space in trailing footprint disappears from higher mip maxima, so a region can be classified occluded despite containing visible depth.

**Evidence:** `/tmp/prime-render-repro.py` models exact shader reads with native mip dimensions:5×3 clear last column (depth1) reduces to2×1 max0.1; candidate depth0.5 passes occlusion against0.1+bias0.0025. This is arithmetic reproduction, not a native capture. At mip1 a fully in-screen projected rectangle x3..4.9,y0.1..1.9 at5×3 maps to the clamped destination texel and includes ignored column4. Full-in-view gate `:1056–1063` therefore does not repair it.

**Fix:** Conservative footprint mapping that includes odd trailing texels (e.g proportionally partition source extent or explicitly expand final reduction cell); adjust sample coordinate mapping to same partition. Alternative padded power-of-two depth with clear depth1, correct viewport mapping. Max reduction must cover all fine-level texels any query maps to.

**Test:** Shader/native tiny5×3,135×67 depth patterns with clear final strips and interior openings. Read all mips and compare conservative CPU reference. Differential culling rendered image vs CPU-only at1920×1080 and odd window/resolution scales, cameras near screen edges. No performance gain claimed; measure overhead of conservative correction.

**History:** Hi-Z reduce introduced873bf77da on2026-10-03.

### R5. Mouse capture does not enable raw relative motion

**Locations:** `Renderer.cs:9039–9042` sets CursorState.Grabbed for relative gameplay. `:9458–9500` takes MouseMoveEventArgs delta into late presentation; `Entities/Players/PlayerInput.cs:3298–3301` derives simulation deltas from cached virtual cursor position. Repository search finds no RawMouseInput, RawMouseMotion or equivalent GLFW raw mode setter. OpenTK4.9.4 is pinned by `src/MphRead/MphRead.csproj:233`. Its [NativeWindow implementation](https://raw.githubusercontent.com/opentk/opentk/4.9.4/src/OpenTK.Windowing.Desktop/NativeWindow.cs) sets cursor-disabled only in CursorState; separate RawMouseInput property performs the GLFW raw-motion toggle. [Official GLFW input guide](https://www.glfw.org/docs/3.4/input_guide.html#raw_mouse_motion) documents raw relative motion as disabled by default and distinct from disabling/capturing the cursor.

**Trigger/impact:** Desktop relative mouse aiming on OS configurations with pointer acceleration or scaling; cursor-derived motion can depend on OS settings and physical movement speed. Native raw sampling is not currently implemented, although late aim code comments use raw terminology. This is a precision/consistency gap, not a demonstrated millisecond latency penalty.

**Fix:** Enable supported RawMouseInput for captured relative gameplay (settings policy if preserving legacy preference needed); keep absolute stylus/pen/UI input path. Log effective mode/support so testing covers actual capability.

**Test:** Native window property check at gameplay capture; UI→match→menu→match and focus-loss transitions; unsupported raw devices fall back safely. Physical fixed-distance slow/fast sweeps across Windows, Linux and macOS OS pointer acceleration settings; compare resulting aim angles, not invented latency numbers.

### R6. KTX2 RGBA fallback loses authored mip correctness

**Locations:** Offline `sourceio-hd/common/mobile_images.py:105–159` authors premultiplied linear-light albedo/emissive mips (`:120–124,:139–143`) and normal renormalization (`:125–128,:144–147`). `Ktx2TextureAsset.cs:69–83` chooses RGBA when quality cap forces fit or preferred compression None; `:94–98` also direct RGBA. CopyCompressed keeps levels; CopyRgba `:230–244` copies only selected level (default0) into ModernTextureAsset. `TextureAssetManager.cs:158–168` uploads RGBA base and sampling requests GenerateMipmap. `ModernGraphicsCompat.cs:1426–1493` regenerates mip chain through generic linear blits (`:1478–1480`); Rgba8 maps to Rgba8Unorm `:1511–1515`. Blit uses UI shader `ModernGraphicsCompat.World.cs:1381–1383`, simple textureSample×color (`ModernGraphicsCompat.cs:114–117`) without color-space-aware/premultiplied/normal treatment. `ModernGraphicsCompat.CompressedTextures.cs:14–28` rejects ETC2 for character runtime, guaranteeing RGBA fallback on ETC2-only hardware; quality fitting causes same issue on all backends.

**Impact:** Lower mip albedo/emissive darkening, alpha fringes and invalid averaged normal length can differ by adapter/quality despite offline mip receipt. Source arithmetic: half black/white encoded averaging128 vs linear-light correct encoded188. Those values are math, not measured GPU pixels. KTX offline audit is sound but cannot prove fallback runtime parity.

**Fix:** Preserve decoded RGBA mip chain in PreparedTextureAsset and upload authored mips. For quality caps, select suitable existing mip and preserve remainder, or channel-aware resample all levels. Keep color-space agreement between compressed unorm and shader decode. Existing compressed path already retains authored levels.

**Test/benchmark:** Force None vs supported BC/ASTC on same KTX; read every mip with albedo black/white+transparent edge, emissive factors, unit normal patterns and material packing. Quality cap and ETC2-only Android cases. Measure decode peak allocation/map load and resident bytes before/after; no speed gain assumed.

### R7. Lifetime counters and benchmark memory figures miss the newest GPU resources

**Locations:** EndPerformanceSample `ModernGraphicsCompat.Performance.cs:67–107` totals `_nativeTextures`, `_geometryCache`, ordinary geometry/uniform arena, retained regular uniform arena and uploads. Omits `_retainedMultiDrawPages`, `_retainedIndirectArena`, `_retainedPbrUniformArena`, GPU visibility storage/indirect buffers and `_gpuHiZTexture`. LiveResources `ModernGraphicsCompat.Diagnostics.cs:87–129` similarly omits new texture/views/buffers and compute shader/pipelines. Allocation source names: `ModernGraphicsCompat.Commands.cs:89,:264–297` PBR arena; `ModernGraphicsCompat.Indirect.cs:41,:81–90`; `GpuVisibility.cs:29–63`; `MultiDraw.cs:36–40`. `ModernGraphicsWindowCheck.cs:483–503` repeatedly performs resize+mipmap, then compares these incomplete counts and reports120-cycle resource lifetime PASS.

**Impact:** Atlas leak R3 and growth in GPU visibility/PBR can remain invisible to the claimed lifetime gate and reported buffer/texture byte measurements. Counters describe a subset without explicit subset contract. Do not use existing TrackedBufferStorage figure as actual total GPU storage.

**Fix:** One resource accounting registry or complete per-owner counts+capacity. Include atlas allocated vs live bytes, Hi-Z pyramid, GPU buffers, indirect and PBR arenas, transient pending uploads. Extend lifetime suite with actual room load/unload and enabled GPU visibility/dense buckets; baseline high-water retained pools separately from unreclaimed growth.

**Test:** Allocation unit tests count each resource type; a test deliberately allocating each newer resource must move corresponding counters, then release brings baseline back. Report driver/device budget alongside source-tracked memory when practical.

### R8. Android exceptions skip functional scene shutdown

**Locations:** `GameView.cs:494–518` Run catches render exception and invokes error callback, finally only releasing surface/context. BuildScene stores Scene `:909–920`; catch `:928–942` sets Scene=null, reports error and stops without DoCleanup/UnloadGl. `Renderer.cs:5447–5469` DoCleanup cancels room transition, replay/demo ownership, sound, output/decoder tasks, selection and capture world. Normal Loop exit reaches End, exceptional exit bypasses it.

**Trigger:** OnLoad/first hidden draw/render/device recovery/scene work throwing after some state has initialized. Potential audio, decoder/transition/replay state persists across return to launcher, and static resource leak R1 is aggravated. Precise surviving tasks depend where exception occurs; no hardware failure injected here.

**Fix:** Preserve local scene reference through build failure. Execute idempotent DoCleanup and resource release while graphics owner is usable in one finally. If partial context/device already failed, CPU cleanup must run regardless while native deletion is best effort. Ensure _onError/_onEnd fired once.

**Test:** Fault-inject before/after scene allocation, after OnLoad and during draw. Assert cleanup cancellation and exactly one UI completion; next match works with no stale audio/replay session. Include explicit backend failure vs Auto fallback policies.

### R9. Temporal Hi-Z accepts stale moving-occluder depth

**Locations:** Frame graph `FrameRenderGraph.cs:196–207` builds history from depth after complete World graph. `RetainedRenderGraph.cs:523–544` maps Opaque/RebuildDepth to all opaque packets, including dynamic entities, not only immutable room occluders. Shader receives only room candidate bounds; candidates gates `GpuVisibility.cs:172–186`. History guard `:311–319` checks old depth target, resolution and matrices-valid. `:1065–1097` only bounds-checks target projected center/extent motion; no previous depth occluder motion, room revision or temporal disocclusion fence. `_retainedDepthHistoryValid` is invalidated for attachment/target changes (`Renderer.cs:2826,:3103`), not dynamic opaque movement.

**Trigger/likely visual behavior:** Fixed camera and static room candidate behind a moving foreground actor/door. Previous image depth was close, occluder moves away, candidate's own bounds do not move, so target passes motion gate and is falsely suppressed for the first disocclusion picture. No native capture yet, so classify highly likely visible bug; algorithm deficiency is established. Source-model counterexample in `/tmp/prime-render-repro.py` prints depth0.5 against stale0.1+bias.

**Fix:** Build occlusion history from static trusted occluders, or current-depth prepass/two-stage occlusion with conservative reprojection/disocclusion handling. At minimum invalidate when dynamic occluders change in affected regions/room transitions. A blanket disable temporal occlusion is a safe diagnosis switch, not the long-term performance conclusion. Candidate motion2px and extent4px thresholds are heuristics, not a proof of conservativeness under camera movement.

**Test:** Moving actor/door in front of textured room panel with fixed camera; exact target visibility parity CPU→GPU every picture, not just averaged screenshot. Teleport/portal changes, thin openings and slow camera rotations. Benchmark static-only occluder history vs complete depth vs GPU stage disabled with CPU and GPU durations and1% lows.

### R10. Failed HD compilation retains unused partial texture residents

**Locations:** CharacterModelRuntime `:300–362` Compile preuploads all base albedos at `:321–328`, then native material/node validation and lists; catch `:358–362` deletes compiled lists only. CompileWeighted `:372–443` incrementally uploads albedo/maps `:415–427`; catch `:439–443` again only releases lists. `Characters/CharacterModelTextures.cs:25–62` stores scene image bindings and pins base textures; Dispose/Clear is only scene-wide `:88–95`. `TextureAssetManager.cs:102–127` admits upload if budget permits and retains successful uploads. TryGetRigid/Weighted catch caches Failed at `CharacterModelRuntime.cs:213,:258`, preventing retries until quality/sampling reset or scene ends.

**Trigger:** Later material/GLB validation, GPU compile failure, or subsequent albedo exceeds budget after earlier uploads succeeded. Rejected replacement falls back to native geometry but prior uploaded textures remain pinned for scene lifetime, stealing budget from other hunters/parts. Budget is bounded, so this is waste/ordering starvation rather than unbounded GPU leak.

**Fix:** Transactional texture leases per compile: validate mappings before upload, track newly acquired references and roll back on failure; shared images must keep other users' refs. Failed transient resource-admission should be distinguishable from permanent invalid pack content so future retry is controlled.

**Test:** Two-material fake geometry, second material intentionally invalid or budget-exhausted; after failure resident bytes/bindings return to precompile baseline and another valid model can load. Shared texture rollback must preserve already compiled model's binding. Benchmark failed/valid mixture under Android budget; no expected speed number.

## Lower-confidence investigation and performance hypotheses (not promoted as established bugs)

- Startup adapter/device callback requests use global static result slots (`ModernGraphicsDevice.cs:22–25`), serialize through CreateLock (`:171+`), and pump at most256 yield iterations with no time deadline (`:452–462`). Comment says normally inline; if native ABI actually defers longer, startup can report unsupported or late callback can write after slots cleared (`:348–352`). Pinned backend request behavior must be confirmed/fault-injected before calling this a real platform regression. Per-request rooted userdata state and explicit deadline/cancel would be stronger. Suggested Low severity scores impact4/risk4/effort4/confidence6. No measured startup latency.
- Normal transform uses mat3(model)*normal rather than inverse transpose in world/GLSL/PBR shader families. Nonuniform scales exist (e.g beam projectile scale), but legacy cartridge parity and native model semantics must be checked before proposing an inverse-transpose rewrite. Need capture and lighting-only synthetic shape test; no confirmed issue promoted.
- Renderer GetDrawItems/CaptureRetainedRenderWorld still reconstructs frame-local packets/transforms each draw, and GPU candidate/storage uploads remain CPU-fed each frame. Retained static geometry avoids per-frame geometry expansion, but CPU portal walk is authoritative. Profile real gameplay at120/144/240Hz before incremental candidate update or shader/pipeline changes.
- Generic GPU mip regeneration is synchronous render work and material decode/upload can occur on load/draw; quantify cold/warm map-to-first-picture with phase timing and peak managed/native memory. Do not assume broader asynchronous loading will improve the critical path without timing data and ownership-safe handoff.

## Platform capability matrix from actual implementation

| Platform | Auto backend; exposed alternatives | Actual retained/GPU path | Limitations/validation needed |
|---|---|---|---|
| Windows | DX12; Vulkan; OpenGL | CPU room portal/frustum builds packets. DX12/Vulkan indexed indirect; compute current-frustum+temporal Hi-Z; native multi-draw and dense count compaction only if adapter feature enabled. | Atlas/Hi-Z issues apply. Need physical DX12/Vulkan parity, timestamp timing and long session memory. |
| Linux | Vulkan; OpenGL | Same Vulkan indirect/compute capability gates. | Desktop legacy ignored-VSync fallback pacing only; real WSI/different driver tests needed. |
| macOS | Metal; Vulkan(MoltenVK); OpenGL | Metal retained direct draws, no GPU visibility/indirect/multi-draw. MoltenVK follows Vulkan gates if native adapter supports them. | Do not label default Metal GPU-driven culling. Native Vulkan enumeration fallback exists for portability surface rejection (`ModernGraphicsDevice.cs:242–257,:402–449`). |
| Android | Vulkan; OpenGL ES | Vulkan modern retained/direct draw; CPU portal authority; indirect, GPU visibility and multi-draw explicitly disabled at compile branch. ADPF hints exist; display pacing separate from fixed simulation. | Surface/scene lifetime findings apply both GLES/Vulkan. ETC2-only character fallback uses RGBA. Physical lifecycle/memory/thermal tests remain required. |

Anchors: backend policy `GraphicsBackendPolicy.cs:31–34,:217–265,:315`; native requested API mapping `ModernGraphicsDevice.cs:492–511`; adapter verifies selected API `:319–327`; multi-draw feature negotiation `:278–302`; indirect `ModernGraphicsCompat.Indirect.cs:53–68`; GPU visibility `GpuVisibility.cs:94–110`; multi-draw `MultiDraw.cs:72–89`. CPU-generated ordinary indirect argument arena (`Indirect.cs:71–136`) is distinct from compute-produced visibility/compaction buffers (`GpuVisibility.cs:247–350`). Compute culls only packets already accepted by CPU room walk; it is not a GPU-owned scene traversal or automatic whole-engine scheduler.

## Input → simulation → camera/viewmodel → HUD trace

- Desktop OpenTK window maintains live KeyboardState/MouseState references passed into Scene constructor (`Renderer.cs:315–337`). Fixed loop `:9090–9103` calls OnSimulationFrame once per fixed60 step. Scene polls desktop pad, resolves UI ownership and BeginFrame at `:2558–2566`, processes keyboard/mouse `:2594` and applies gamepad `:2597`; then ModCommitLateAim `:2602` clears deltas now accepted by simulation. PlayerInput UpdatePointer (`PlayerInput.cs:3293–3301`) derives current virtual cursor difference or consumes accumulated pen deltas. Its SynchronizeSuppressed `:3275–3291` advances baseline and drops pending pointer/alt state when gameplay does not own input.
- Desktop OnMouseMove (`Renderer.cs:9463–9499`) routes UI first, zeros stale aim under menus/chat/results, and only accumulates relative first-person pending delta; no weapon intent mutation there. On high refresh, render frame polls aim-only stick and CapturePresentationSample (`:9157–9165`) after fixed steps. Ownership revision (`:9139–9153`) prevents menu open+close between steps reusing old aim. R5 flags native mouse acquisition as the missing raw-device layer, not fixed-step consumption.
- Android event/touch state accumulates in controls; DrawFrame `GameView.cs:1011–1022` consumes ApplyInput per fixed step, while high refresh render reads PeekAimDelta (`:1090–1096`) without consuming it. Controller preview captures axes (`:1067–1080`); ownership revisions release pending controls (`:1043–1057`) and suppress late touch/stick aim. Resume resets accumulator/pacer and aim (`:572–583`); MainActivity pause releases touch/pad, pauses performance hints and renderer (`MainActivity.cs:356–398`). No physics rate change from screen refresh.
- Draw calls TransformCamera before GetDrawItems (`Renderer.cs:2864–2880`); TransformCamera prepares first-person pose once (`:3740–3757`). `PlayerEntityNetAim.cs:659–754` uses the same late-latched orientation basis for view and local gun pose, stores `_fpRenderPose`; PlayerDraw consumes cached gun/effect transforms (`PlayerDraw.cs:309–335`) without another device poll. World FOV and viewmodel authored FOV separated (`Renderer.cs:3770+`). Very large late pointer aim bound8degrees plus subframe remainder (`PlayerEntityNetAim.cs:497–523`) is explicit presentation behavior; shooting/collision fields stay simulation-owned.
- HUD layout is applied at draw sites (`Renderer.cs:6335,:6385–6390,:6602–6615`) with presentation-space scaling. Pro HUD responsive aim selects presentation camera path (`PlayerEntityNetAim.cs:675–718`) while leaving gameplay aim/collision state intact. No confirmed HUD-size→simulation latency coupling found along this chain. Crosshair/first-person render acceptance and FrameTiming checks cover pose math; physical raw input capture is additional R5 coverage.

## Audited sound paths and checks

- Fixed60 FrameTiming; catch-up bounded5, >0.25s stall drops debt, presentation alpha separate. DesktopFramePacing policy chooses native-rate VSync vs software cap without stacking; AndroidFramePacing preserves true Unlimited and avoids second display pacing wait. Independent checks run by this subagent: `/Users/jarrett/.dotnet/dotnet run --project tools/desktop-pacing-check/desktop-pacing-check.csproj -c Release` PASS23 cases; Android pacing counterpart PASS28. Render parity comparator tests:8 PASS with bundled Python (system Python lacks Pillow). Actual `mobile_images.prepare` was called with an in-memory2×2 half black/white albedo; authored1×1 mip `[188,188,188,255]` PASS, confirming offline linear-light behavior R6 references. Parent ran supplemental engine pure rendergraph/backend/frametiming checks (all pass) and owns input check results. Supplemental DLL engine sources are unchanged; only acceptance compile errors patched externally, so these do not establish clean main build success.
- Device recovery invalidates native geometry and retained draw calls repromote if native buffer pointers missing (`ModernGraphicsCompat.Retained.cs:290–304`, RetainedPbr `:223–238`, Compat `:1599–1629`). An initially suspected stale cached NativeGeometry problem was disproved. Multi-draw argument offsets also bind atlas geometry in single indirect fallback (`Retained.cs:356–375`, RetainedPbr `:285–307`), so don't report index/baseVertex mismatch.
- GPU visibility outputs indirect commands and dense bucket counts directly, without per-frame CPU readback. Candidate eligibility excludes unsafe non-room, weighted/billboard/translucent/override packets. Exact state comparisons, including matrices/lights/uniforms, guard multi-draw compatibility; hashes do not alone establish equivalence.
- Regular persistent geometry deletion releases buffers; arena state/queue ordering and upload staging avoid overwriting draw-visible data before submit. Renderer Dispose explicitly releases all new resources, so R3 is scene/room reclamation, not missing process-exit cleanup.
- FrameRenderGraph describes fixed ordered passes and executes existing six-pass MPH world semantics. It is orchestration with validated order/resource declarations, not arbitrary render-pass optimization/lifetime scheduler. Replacing it with a generic scheduler is not supported by observed bottleneck evidence.
- HD GLB loaders validate embedded single-buffer images, geometry, native node/material contracts and source provenance. Embedded image cache identity includes quality/sampling/channel/factors; ConditionalWeakTable prevents image payload cache ownership by itself, weak image cache bounded1024. Mobile pack snapshots exact source bytes, rejects path traversal/symlinks, builds fresh outputs and preserves desktop/preserved hunters. Audit independently recomputes channel/mip prep and checks exact accessor bytes/material/UV/skin contract (`mobile_audit.py`). Audit says offline/visual/Android acceptance distinctions explicitly; no claim that receipt alone proves runtime device acceptance.
- Device error callback catches exceptions at unmanaged boundary and tracks recovery state; backend selection verifies requested native API and fallback policy distinguishes explicit request vs Auto. MoltenVK enumerated-adapter fallback checks surface support and releases unselected adapters.

## Cleanup candidates and dependency evidence

- `LowLatencyController.InstallProvider` (`:168`) has no repo caller. NullLowLatencyProvider is current provider (`:152`); wait/marker calls are live at desktop/Android frame loops. This is an intentional vendor seam and diagnostic framework; do not delete active controller or claim current vendor Reflex/Anti-Lag boost. Candidate: clearly label provider unavailable in diagnostics and avoid feature marketing until provider implementation/measurement. No measurable clock/latency improvement promised.
- `docs/rendering/samus-hd-kit/retarget-samus-source.buggy-backup.py` is named an archived buggy backup and README references it (`README.md:88`). Repo dependency search finds that README reference, no runtime/build import. Safe from runtime/build standpoint to relocate to history/archive and update README; keep provenance if documentation explicitly compares it. Not a dead compatibility product path.
- Several modern renderer comments still describe a prototype/subset, but renderer implementation now covers extensive generated/core/retained paths. Documentation correction may reduce contributor confusion; don't remove OpenGL/GLES compatibility path, which remains explicit user-selectable and Auto recovery fallback.

## Suggested implementation/test slices for this scope

1. Fix R1/R8 centralized owner-thread Android scene cleanup and R2 callback ownership; deterministic lifecycle tests plus physical rotate/pause/load smoke.
2. Fix R4 odd Hi-Z mapping and add conservative GPU parity fixtures; decide R9 temporal occluder policy before declaring GPU visibility stable.
3. Fix R3 room atlas reclamation and R7 complete resource accounting; repeated rematch tests prevent recurrence.
4. Enable/verify native raw relative mouse (R5), preserve authored RGBA mips (R6), roll back failed HD texture admission (R10).
5. Profile actual gameplay/configurations and cold/warm map load before optimizing scene rebuild, pipeline warmup, upload staging or adding more GPU-driven features. Frozen benchmark runs180 sim steps then repeated draw at fixed poses (`ModernRenderBenchmark.cs:41–43`) and explicit Finish; good for render-path comparison, insufficient for gameplay motion/disocclusion/input latency or physical GPU timing claims. Existing log `ModernGraphicsCompat.cs:951–967` says GPU timing disabled even if adapter supports timestamp capability. Record frame distributions, CPU phase timers, GPU timestamps, actual presentation mode, resource capacity and device/platform with every proposed performance change.
