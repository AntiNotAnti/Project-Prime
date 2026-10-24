# Project Prime OpenGL / Renderer Audit and Implementation Plan

**Prepared:** 2026-10-21<br>
**Repository:** AntiNotAnti/Project-Prime<br>
**Audited main snapshot:** `07f23c014a408d134d1cc67e2f1d7074fc7f7942`<br>
**Platforms:** Windows, macOS, Linux, Android<br>
**Purpose:** An actionable engineering handoff for an AI implementation agent.

**Navigation:** [Executive decision](#1-executive-decision) · [Agent instructions](#2-instructions-for-the-implementing-ai-agent) · [Current architecture](#3-current-architecture-and-work-to-preserve) · [Findings](#4-prioritized-audit-findings) · [Platforms](#5-cross-platform-strategy) · [Implementation packages](#6-implementation-work-packages) · [Measurement](#7-measurement-plan-and-decision-gates) · [Commands](#8-existing-validation-commands-to-reuse) · [Quality policy](#9-proposed-quality-policy) · [Handoff prompt](#10-definition-of-done-and-ai-handoff-prompt)

## 1. Executive decision

Improve this renderer in three successive layers:

1. **Make performance measurements, presentation, lifecycle handling, and resource use trustworthy.** Resolve the frame-capture discontinuity, reproduce the failed desktop cap cases, repair Android cadence and surface handling, and continue the unresolved macOS native-memory investigation.
2. **Remove specific preparation, duplication, upload, and state-management costs already visible in the source.** Portal traversal, geometry compilation identity, draw-time combined geometry construction, target allocation, and texture residency are more credible starting points than another generic cache.
3. **Improve image quality through a small number of independently budgeted features.** Prioritize shadow correctness, texture/material quality, a coherent modern color pipeline, and spatial anti-aliasing. Add lighting, bloom, AO, and adaptive resolution only after the preceding performance and correctness gates.

The renderer already contains substantial modernization: retained room geometry, VBO/IBO and VAO paths, material grouping, bounded batching, depth-reuse proofs, state and uniform caches, display-pose interpolation, texture streaming, directional shadows, and profiling. Preserve and improve those systems.

**Do not start by replacing OpenGL, rewriting the entire renderer, enabling retired post-effect settings, forcing every desktop onto a core profile, or promising a percentage FPS improvement.** The evidence supports specific defects and experiments; it does not establish the current bottleneck on every supported device.

### Recommended first implementation campaign

Complete R00–R13 in the work-package section, with independent fixes proceeding in parallel where ownership allows. Start R13, the macOS memory investigation, immediately alongside the other work. Treat R14, production program reuse, as conditional on that investigation's ownership and eligibility evidence.

The most promising initial performance work is **R11 portal preparation**, **R12 geometry sharing**, and then **R15 submission/batch construction** and **R16 upload scheduling**, guided by current traces. The most promising initial visual work is **R20–R23: shadow correctness, shadow quality, materials/color, and spatial AA**.

### Evidence boundaries

This deliverable is a **static implementation audit plus review of repository-owned historical measurements and official platform documentation**. No fresh game build, retail-asset run, native GPU benchmark, image comparison, thermal soak, or physical input-to-photon measurement was performed for this audit. No repository implementation code was changed. A narrow synthetic check of the AGX acceptance calculation confirmed the validator edge case described below; it was not a native GPU or full campaign-validation run.

Use these labels throughout implementation:

| Label | Meaning |
|---|---|
| Source-confirmed | The inspected control/data flow establishes the issue or missing contract. Its device-specific manifestation may still need reproduction. |
| Historical measurement | A repository report records a result on its own source, binary, device, workload, and date. It is not a new result for this audit snapshot. |
| Structural risk | The architecture permits an incorrect or expensive outcome; reproduce it before asserting an observed bug. |
| Experiment | A proposed improvement requiring controlled measurement and a reversible rollout. |

The pinned links in this document are the audit evidence. The implementation agent must reconcile them with its actual working revision before editing.

## 2. Instructions for the implementing AI agent

### Non-negotiable behavior contracts

- Keep the rendering backend OpenGL / OpenGL ES.
- Preserve the fixed **60 Hz simulation** and existing gameplay, physics, collision, input-edge, networking, replay, RNG, and canonical-state contracts. Higher display rates must remain presentation work.
- Preserve current high-refresh camera, body, viewmodel, beam, and particle presentation ownership. Do not move camera-dependent visibility back to simulation frequency.
- Preserve the six-pass world ordering, alpha/stencil behavior, cutouts, decals, portal/forcefield behavior, inward outlines, native HUD/crosshair clarity, and all depth-reuse eligibility proofs.
- Preserve Windows, macOS, Linux, and Android support, including the current compatibility path. A new capability tier must have a usable fallback.
- Preserve the current Android performance enrollment policy. Do not disable enrolled production optimizations merely because hardware acceptance is incomplete. Keep explicit A/B opt-outs for diagnostics, and retain capability and correctness guards for individual paths.
- Preserve selected frame caps and render scale. Any new automatic resolution feature must be an explicit user-selected mode; it must not silently introduce an Android cap/quality governor.
- Preserve the deliberate Original/native appearance. Modern lighting/color changes must have an explicit path and settings migration.
- Keep GL calls on the owning current-context thread. Decode, hashing, statistics, and preparation may move off-thread only with immutable inputs, bounded work, correct cancellation, and no hidden GL calls.
- Keep caches bounded and keyed by context/resource generation and complete relevant identity. A deleted or unshared context cannot lend its GL names to another context.

### Execution rules

1. Read applicable repository instructions and the current renderer/performance decision documents. Diff the audited snapshot against the actual working head; record resolved, changed, and still-applicable findings.
2. Create a baseline manifest and a short implementation status ledger before changing behavior.
3. Make small reviewable changes, normally one root cause or experiment per PR. Avoid combining pacing, shader math, texture policy, and batching in one comparison.
4. Add focused tests for the actual failure or invariant. Reuse existing harnesses; do not manufacture passing tests that merely duplicate the implementation.
5. Report **PASS, FAIL, NOT_RUN, UNVERIFIED, or NO_ROLLOUT** with scope. A successful build is not a GPU, visual, lifecycle, or performance pass.
6. Preserve failing receipts and negative controls. Never silently filter hitches, failed presents, context loss, missing device data, or unsuccessful runs.
7. Do not promote a performance experiment without a current, same-workload baseline/candidate comparison. If it loses or cannot be attributed, retain the evidence and leave it disabled or remove it.
8. Do not merge, publish, or change release policy beyond the authority supplied to the implementing agent. Existing maintainer decisions remain in force; this document does not revoke or replace them.
9. At each handoff, list exact commits, changed files, commands actually run, evidence paths, open risks, and the next concrete action.

## 3. Current architecture and work to preserve

| Area | Current implementation | Implication for this plan |
|---|---|---|
| Desktop contexts | macOS requests OpenGL 2.1; Windows/Linux request OpenGL 3.2 compatibility. | Core-only facilities are not the common baseline. [Context source][S-CONTEXT] |
| Android | EGL OpenGL ES 3 context, translated shader sources, GLES compatibility facade and streaming paths. | Desktop shader identity, fixed-function assumptions, and extension availability cannot be copied blindly. [Android context][S-GAME-CONTEXT], [translation][S-TRANSLATE] |
| World submission | Retained room templates/clusters, safe material grouping, VBO/IBO promotion, VAOs and a bounded combined atlas; non-room paths still include display lists. | Optimize measured remaining work; do not propose “add VBOs/batching” as a new foundation. [Retained architecture][S-RETAINED-DOC], [geometry][S-GEOMETRY], [submission][S-BATCH] |
| Depth/stencil | Explicit world pass ordering, guarded opaque-depth reuse and restoration. | A blanket depth prepass removal, global sort, or early discard can break correctness. [Render graph][S-GRAPH], [retained pass semantics][S-RETAINED-PASSES] |
| Presentation | Fixed simulation, display-pose history, cap/VSync policy, low-latency markers. | Preserve interpolation and use one pacing owner. Markers are not proof of hardware latency control. [Timing][S-TIMING], [desktop pacing][S-PACING], [latency provider][S-LATENCY] |
| Textures | Native placeholders, asynchronous modern decode, progressive promotion, mip/aniso policy, advanced companion maps, Android ETC2/ASTC KTX2. | Fix residency and scheduling contracts before adding another streaming system. [Streaming][S-STREAM], [texture assets][S-TEXMAN], [KTX2][S-KTX] |
| Visual features | Directional shadows, texel snapping, PCF, light-space room casters, cel shading, inward outlines, modern normal/specular/emissive inputs and supersampling. | Improve existing quality and correctness first. [Shadows][S-SHADOW], [compositor][S-PIPELINE], [materials][S-MATERIAL] |
| Retired features | AA, HDR, bloom, AO, reflections and several old grading controls are explicitly reset/retired. | An enum or settings field is not a working feature. Add narrow new passes instead of re-enabling abandoned controls. [Retirement policy][S-OPTIONS] |
| Resource ownership | Canonical shader rollback and current-program teardown fixes, resource checks, optional-path failure injection, bounded final-composite capture. | Extend established ownership rules; do not claim the old fixes are absent. GL-name cleanup does not prove native memory stabilization. [Shader ownership][S-SHADER-OWNERSHIP], [AGX disposition][S-AGX] |

## 4. Prioritized audit findings

### Finding register

Priorities below express implementation order and risk, not a claim that every issue has caused a user-visible failure. The macOS P0 label is the repository's existing unresolved disposition.

| ID | Priority / evidence | Finding | Principal consequence |
|---|---|---|---|
| F01 | P0 measurement prerequisite / source-confirmed | Raw capture can omit the interval that changes presentation generation through a stall or dropped steps. | Tail analysis can lose the precise event being investigated. |
| F02 | P0 reproduction / historical | October 19 numeric 120/144 cap cases and a 30-minute Windows stability gate failed. | Current pacing acceptance remains unproved. |
| F03 | Existing unresolved P0 / historical plus validator gap | Persistent AGX-origin allocation growth remains unaccepted after application GL-name cleanup; the acceptance calculation does not require the declared warmup anchor. | Long-lived macOS session stability needs its own investigation. |
| F04 | P1 / source-confirmed | Android pacing chooses ownership from supported refresh rates and desired swap policy, with request-state defects. | A selected cap may not be enforced under ignored/failed display or swap requests. |
| F05 | P1 / source-confirmed | EGL surface retries can spin; failed swaps are not cleanly distinguished from successful presentation. | Lifecycle CPU burn, log churn, misleading timing, fragile recovery. |
| F06 | P1 / source-confirmed | Android prepared compressed-world texture paths discard actual mip completeness. | Valid assets can be sampled with an invalid mip policy. |
| F07 | P1 / source-confirmed | Android binary-cache identity hashes desktop inputs rather than the actual translated GLES sources. | GLES-only fixes can continue using stale binaries. |
| F08 | P1 / source-confirmed | GLES UI initialization can clobber a nonzero texture unit and has partial-initialization ownership gaps. | Cross-pass state corruption and failure-path leaks. |
| F09 | P1 / source-confirmed | Readback dimensions can disagree with the selected shadow-composite framebuffer; RGB readback is not a portable GLES assumption. | Incorrect screenshots, thumbnails, or exports at supersampled scale. |
| F10 | P1 / source-confirmed | Optional cel/depth storage and first-frame reallocation waste target memory; limits are not a total byte budget. | Memory pressure, resize stalls, allocation failures. |
| F11 | P1 / source-confirmed and structural risk | Portal clipping allocates per clip; traversal repeats work and relies on debug-only fixed-capacity checks. | Avoidable render-thread work and unsafe dense-graph behavior. |
| F12 | P1 / source-confirmed | Geometry reuse dictionary is queried but never populated. | Duplicate compilation/storage for compatible source geometry. |
| F13 | P2 / source-confirmed work; impact unmeasured | Combined geometry is built/uploaded on draw-time visibility-sequence misses; VAO state is restored per draw. | Potential first-traversal hitches and submission overhead. |
| F14 | P1/P2 / source-confirmed scheduling limit | One texture promotion per frame does not bound the cost of a single upload or first-use mip generation. | Frame-time spikes remain possible despite streaming. |
| F15 | P2 / source-confirmed work, impact unmeasured | Android telemetry polls services, sorts statistics and formats output on the render thread. | Periodic overhead can contaminate the metric it reports. |
| F16 | P1 visual risk / structural | Shadow composition lacks an explicit world/viewmodel/emissive/overlay receiver contract; retry policy is inconsistent. | Risk of incorrect darkening and optional-effect failure persistence. |
| F17 | P2 visual opportunity / source-confirmed | Modern materials lack a complete color/alpha contract and use a fixed specular view vector. | View-dependent lighting inconsistency and color/filtering risk. |
| F18 | P2 feature / source-confirmed | No active modern AA/bloom/AO pipeline exists despite retained settings fields. | Supersampling is an expensive route to cleaner edges. |
| F19 | P2 coverage / source-confirmed | Match traces omit parts of launcher/UI work; the low-latency provider is currently null. | Menu/Studio performance and physical latency need separate evidence. |
| F20 | P2 opportunity / source-confirmed | Desktop Basis KTX2 transcodes to RGBA; fixed compressed formats can be rejected; authored mip preservation is limited. | Avoidable expansion, upload cost, and lost mip quality. |

### F01–F02: repair evidence, then diagnose pacing

`OpenGlFrameProfiler.BeginFrame` records the current presentation generation. `FrameTiming.Advance` increments that generation for a >250 ms stall or catch-up overflow. `EndPresent` only adds a raw sample when the generation still matches. Consequently, the triggering interval is omitted from that capture path and the next frame starts another segment. This does **not** mean every legacy aggregate or native-harness interval has the same omission. [Profiler][S-PROFILER], [timing advance][S-TIMING]

Retain the offender with explicit event metadata, then segment subsequent state. Keep intentional focus/pause/cap transitions identifiable. A lifecycle pause can be excluded from a clearly scoped gameplay statistic, but it must remain in the raw event record.

The repository's October 19 Windows report records:

| Historical condition | Recorded result |
|---|---|
| 120 numeric cap | 98.399 completed callback FPS; p99 55.3877 ms; 22 simulation drops |
| 144 numeric cap | 124.524 completed callback FPS; p99 27.7579 ms |
| 30-minute 240-cap run | 238.297 average FPS; p99 4.7792 ms; true 1% low 135.906 FPS; 26 dropped simulation steps |

External build activity overlapped the long run, so it is not uncontended acceptance and does not prove a renderer root cause. The same program explicitly withheld FPS-04/05 rollout because it did not isolate a safe renderer/upload win. Reproduce on the current build before replacing the scheduler. [Windows receipts][S-WINDOWS], [decision record][S-FPS-DECISION]

Separate timer oversleep, event processing, simulation catch-up, scene preparation, submission, swap blocking, GPU duration, and OS presentation. Existing policy already distinguishes numeric, Display, and Unlimited modes; preserve that distinction.

### F03: continue the AGX investigation without inventing a cache fix

The newer AGX-04 record is more specific than the older RSS observations. In its matched M4 Pro campaign, outstanding direct malloc-prefix AGX fragment-origin groups rose from **44.830734 MiB at 60 loads to 224.644974 MiB at 300 loads**, while an independent 60-load repetition ended at 44.717972 MiB. Observed owned GL names were released. This supports reproducible driver-origin allocation behavior associated with newly linked-and-drawn programs; it does not establish a retaining root, a definitive driver leak, or a safe production mitigation. [AGX-04][S-AGX]

The record keeps production shader leases/cache **NOT_ELIGIBLE**. Complete source/link/pipeline identity, uniform and layout reset, live-context ownership, partial initialization, fallback and scene/UI/replay/Studio interactions remain prerequisites. Identical GLSL hashes alone cannot authorize a global cache.

Preserve its increasing-load/native-attribution acceptance method. A stable GC heap, zero owned GL names, a single idle period, or process restart does not close this finding. Restart is an observed operational mitigation, not the requested renderer improvement. Existing release decisions remain separate from this audit.

The audit also found an acceptance-validator gap: `assess_agx_boundedness.py` filters windows at or beyond the declared warmup load but does not require a candidate observation exactly at that anchor. Later-only near-flat observations can therefore omit growth from the warmup point. In a narrow synthetic calculation, controls at 60 loads of 1000/1010 bytes and candidate windows `(120,9000), (300,9005), (600,9003)` returned PASS; adding the actual candidate anchor `(60,1000)` correctly produced FAIL. This demonstrates a validator condition, not native memory behavior or a full CLI/campaign pass. Require the same-process warmup anchor before accepting the gate. The existing historical AGX failure remains unresolved. [Assessment implementation][S-AGX-ASSESSOR]

### F04–F05: Android needs effective presentation state and bounded lifecycle handling

Current Android native-refresh detection considers supported or maximum rates rather than proving the active mode. The wait path derives presentation ownership from desired policy, not successful swap-interval application and observed cadence. For example, selecting 60 on a 60/120 Hz device does not prove the OS selected 60 or that a swap request succeeded. [Android performance policy][S-ANDROID-PERF], [pacer][S-ANDROID-PACER], [surface rate and swap policy][S-GAME-PACING]

Two request-state defects compound this:

- `_requestedFrameRate = -1` is both an initial/reset sentinel and the Unlimited cap value, so first/resumed Unlimited can skip its intended surface-rate request.
- The requested cap is cached before `SetFrameRate` succeeds, so exceptions can suppress retries for an unchanged request.

Represent desired cap, requested surface rate, successfully applied swap interval, active display mode, observed cadence, surface generation and pacing owner separately. A request is not an observation.

The render loop immediately retries certain failed surface binds while a non-null holder can bypass its idle wait. Failed swaps release the surface but continue through presentation markers/statistics. Classify EGL failures, record failed presents, and wait for a meaningful surface event or bounded retry deadline. Context loss currently has an explicit fatal boundary; do not relabel that as full resource recovery. Rebuild all owners before attempting in-process context recovery. [Lifecycle loop and surface binding][S-GAME-LIFECYCLE], [swap handling][S-GAME-SWAP]

### F06: preserve actual texture residency through sampling

`CompressedTextureAsset.MipmapsAvailable` provides an explicit completeness check for multilevel compressed chains. Some upload paths use it. `TextureReplacementPack.TryUploadPreparedBound` instead derives mip use from dimensions, while compressed upload only submits supplied levels. Progressive registration then marks mip state from policy, and direct/retained sampling can reapply a mip filter without the actual resident-chain facts. [Compressed descriptor][S-COMPRESSED], [replacement upload][S-REPLACEMENT], [upload implementation][S-TEXMAN], [registration][S-TEX-REGISTER], [sampling][S-SAMPLING], [retained sampling][S-RETAINED-SAMPLING]

This is a source-confirmed completeness mismatch for Android GPU-compressed prepared world assets when compression is eligible, either dimension exceeds one, the supplied chain is base-only/incomplete, and the chosen modern policy enables mips. Synchronous albedo and companion-map uploads share the prepared helper; the risk is not limited to progressive promotion. The exact pixel symptom still requires native GLES reproduction.

Use one binding descriptor for dimensions, actual format, transfer function, mip range/completeness, estimated bytes, context generation, and effective sampling. A contiguous partial chain can use a valid supported maximum level; otherwise select safe non-mip sampling or fallback. Do not generate unsupported compressed mip storage. Test actual pixels, not only successful upload return codes.

### F07–F08: make GLES cache and UI ownership exact

The Android binary cache hashes the desktop shader arguments plus driver identity. Actual compilation passes through `EsShaders.Translate`, which can substitute independently maintained GLES shader text. A GLES-only fix can therefore leave the key unchanged. Hash the actual expanded sources compiled, plus translator/schema version, variant defines, attribute/link ABI and relevant driver/context identity. Preserve source fallback and link validation. [Binary cache][S-BINARY-CACHE], [key][S-BINARY-KEY], [translation][S-TRANSLATE], [shader call sites][S-SHADER-OWNERSHIP]

The GLES UI state scope records unit-zero texture state and the previously active unit, then returns to that active unit. Initialization/resizing performs texture binds before explicitly selecting unit zero. If unit three was active, its binding can be changed while restoration only repairs unit zero. Also, fragment compilation can fail after vertex creation but before the cleanup scope, and a nonzero UI program acts as an “initialized” marker before the remaining bundle is complete. Use explicit unit ownership and transactional publication/cleanup of the entire bundle. [UI state][S-UI-STATE], [UI compositor][S-UI-COMPOSITOR]

### F09–F10: capture and target ownership need descriptors and byte limits

`ReadSceneTarget` sizes its allocation/read using `_targetSize`. With supersampling and an active shadow compositor, `GraphicsReadFramebuffer` can select a smaller output-sized target. Return the framebuffer and its actual dimensions together; distinguish scene-resolution and final-output capture. Restore read/draw framebuffer, read buffer and pack/PBO state as applicable. [Readback][S-READBACK], [output sizing][S-PIPELINE]

The shared readback code also requests `RGB / UNSIGNED_BYTE`, which the GLES facade forwards. For normalized fixed-point surfaces, GLES 3 guarantees `RGBA / UNSIGNED_BYTE` and supports an implementation-selected format/type pair; RGB is not universally guaranteed. Use portable RGBA readback and CPU repacking when RGB output is needed, or validate the queried pair for the current attachment. This is a portability risk, not a claim that every device fails. [GLES facade][S-GLES-READBACK], [ES 3 specification, §4.3.2][E-ES3]

Target allocation also has concrete waste:

- The full-size cel texture is allocated and resized even when cel is off.
- A D24S8 renderbuffer remains resident when a separate readable depth texture is attached.
- Initial allocation sets target size but not `_sceneColorFormat`, so first draw can trigger another resize.
- The transient free pool limits object count, not retained bytes.
- Render scale permits 25–800%, and dimension clamps are not an aggregate graphics-memory budget.

These are separate from the unresolved AGX-origin growth; fixing them must not be presented as proof that the AGX finding is resolved. [Target allocation][S-TARGETS], [depth target][S-DEPTH], [pool][S-POOL], [scale policy][S-SCALE]

For scale intuition, one nominal four-byte-per-pixel attachment at 3840×2160 is **31.64 MiB**. At 200% scale in each dimension it is **126.56 MiB**; at 800% it is **2025 MiB before dimension clamps**. This is arithmetic for an illustrative RGBA8/D24S8-sized attachment, not a measurement of the driver's actual RGB storage, tiling, compression or total VRAM use.

Allocate optional attachments lazily, initialize descriptor state atomically, retain only necessary depth storage, trim pools by bytes/age/context, and preserve the last working configuration if a replacement target cannot be created.

### F11–F13: concrete preparation and geometry work

Portal visibility must continue to refresh using the current render pose. Within that correct high-refresh path, clipping allocates two `Vector3[14]` arrays per call; portal projection recomputes view×projection per vertex; recursion scans the full portal list. Frustum/clip storage uses fixed capacities and debug-only assertions. Recursive visits can consume slots independently of distinct room-part count. [Portal implementation][S-PORTAL], [render-frequency refresh][S-PORTAL-REFRESH]

First add checked capacities and conservative visibility fallback. Then use bounded recursion-safe scratch, a once-per-view projection product and ordered node adjacency. Avoid static scratch shared across views or reentrant scene renders. Preserve traversal order and conservative visibility.

`GenerateLists` creates and queries `tempListIds` but never populates it. The proposed repair is **not** simply adding `DlistId` to the dictionary: compilation also depends on texture dimensions, texgen, room mode and relevant attribute semantics. Define a complete key and shared lifetime first. [Geometry generation][S-GEOMETRY]

The combined retained path builds merged CPU arrays and GPU buffers on exact visibility-sequence cache misses. It already has a 64 MiB combined-copy budget/LRU. That budget does not cover all standalone geometry or retained CPU data. Measure first-traversal build/eviction stalls before changing batching; consider stable spatial/material groups and bounded preparation with ordinary draws as fallback. Per-draw VAO restoration is another profiling candidate, with explicit external-boundary state restoration. [Batch construction and draw][S-BATCH]

### F14–F15: schedule indivisible work and keep measurement cheap

Modern texture decode is already asynchronous, with bounded workers and one promotion per frame. Android's post-upload cooldown reacts to an upload's cost; it cannot shorten that upload. Native mip generation can occur on a first qualifying draw, and texture quality changes can reupload tracked sources. Keep the existing scheduler but add actual bytes/time/in-flight accounting, authored mips, priority and safe incremental work. [Streaming and reupload][S-STREAM], [sampling/mip generation][S-SAMPLING]

Android's approximately five-second telemetry update runs from frame recording and performs service polling, multiple sorts, runtime-memory reads and formatting. Its impact has not been measured. Snapshot bounded counters and offload suitable aggregation; use correct thread affinity for platform callbacks. Missing thermal/display observations must remain unknown rather than imply a healthy value. Existing reports are rolling window summaries; their median window p99 is not a global raw-frame p99. [Android telemetry][S-ANDROID-PERF], [comparison contract][S-ANDROID-ACCEPT]

### F16–F20: improve the actual image pipeline

Shadow composition currently occurs after outlines and scene overlays, multiplying existing scene color using world-depth reconstruction. Viewmodels use a different projection, and there is no explicit receiver mask for emissive/translucent/overlay contributions. This is a structural visual risk, not a reproduced screenshot defect. Introduce explicit receiver semantics and targeted image witnesses before raising quality. [Pass order][S-GRAPH], [viewmodel projection][S-VIEWMODEL], [compositor][S-PIPELINE]

Shadow texel snapping, 1024/2048/4096 tiers, nine-tap PCF, normal-dependent bias and light-space room casters already exist. Remaining dynamic casters derive from camera-submitted packets, so verify offscreen actors/platforms. The shadow-map refusal flag resets when switched Off, but compositor refusal survives until disposal. Make optional-effect retry bounded and intentional. The compositor publishes its program handle before link/uniform initialization finishes, so safe retry must first clean up/reset partial state; merely clearing the refusal flag is insufficient. [Shadow map][S-SHADOW], [compositor lifecycle][S-PIPELINE]

Modern material upgrades should preserve original art while introducing explicit sRGB/linear and alpha metadata. Current KTX2 sRGB/unorm decoded assets lose that distinction, byte resampling lacks a complete transfer contract, and the modern specular view vector is fixed despite model/world-space surface data. Normal resize renormalization already exists and should be retained. [KTX2][S-KTX], [modern asset filtering][S-MODERN-ASSET], [shader material math][S-MATERIAL]

Desktop GPU-native compression and authored mip preservation are useful memory/quality opportunities; Android compression already exists and needs its correctness repair and device qualification. Spatial AA should be a newly scoped pass preserving HUD clarity. Defer temporal AA/upscaling/frame generation until motion, history, disocclusion and camera-cut contracts exist. [Texture formats][S-KTX], [retired features][S-OPTIONS]

Finally, launcher/UI work before `BeginFrame` and the no-scene branch need outer-frame attribution. The installed low-latency provider is null; hardware queue control would be a separate experiment. [Desktop callback][S-CALLBACK], [latency implementation][S-LATENCY]

## 5. Cross-platform strategy

### 5.1 Capability tiers

Use a small immutable **capability snapshot per context generation**, including actual GL/GLES version/profile, extensions, loaded entry points, formats, limits, query support and driver identity. Keep requested settings, capability eligibility, active path, fallback reason and measured device qualification separate. This is an extension of existing capability/policy code, not a second graphics abstraction.

| Platform/path | Baseline to preserve | Optional improvements | Required qualification |
|---|---|---|---|
| macOS compatibility | Current GL 2.1 context, compatibility shaders/vertex paths, working APPLE/core-extension distinctions. | Better preparation, ownership, compression where supported, reduced program churn after eligibility, bounded targets and compatible spatial effects. | Apple Silicon plus an Intel Mac if Intel Macs remain supported; repeated scene/Studio cycles and native allocation attribution. Do not assume GPU timers on the tested M4 Pro path. |
| Windows | Current GL 3.2 compatibility request; actual driver capabilities may be higher. | Explicit vertex-input ownership, modern buffers, format-specific compression, asynchronous readback or queue experiments when supported. | NVIDIA, AMD and Intel; fixed refresh and VRR; windowed/fullscreen; numeric/Display/Unlimited transitions. |
| Linux | Same compatibility contract, with driver/window-system differences. | Capability-qualified desktop paths rather than OS-name assumptions. | Mesa AMD/Intel and proprietary NVIDIA where supported; X11 and Wayland/XWayland routes actually used by the application. Record compositor/session type. |
| Android | GLES 3 context and existing enrolled performance policy. | Correct pacing, compressed residency, shader cache, tile-aware pass lifetimes, bounded texture/target work. | At least Adreno and Mali, one memory-limited device, one high-refresh device, actual OS refresh changes and warm thermal sessions. |

Core-only modernization is a separate opt-in experiment. Apple's migration guide explicitly identifies removal of fixed-function and client-side compatibility features in core contexts. The desktop native RmlUi backend also uses compatibility state and matrix/client-array behavior; migrating only the world shader would be incomplete. [Apple core migration][E-APPLE], [native UI backend][S-UI-NATIVE]

Do not make compute shaders, multi-draw indirect, persistent mapping, or shader-storage buffers requirements for the common path. Persistent mapping needs explicit synchronization and does not become portable merely because a desktop exposes it. Core/APPLE VAO behavior and context sharing also require correct ownership. [Buffer storage specification][E-BUFFER], [VAO specification][E-VAO]

### 5.2 Graphics-memory budget

Start with an auditable resource ledger, not a broad rewrite:

- Separate requested storage estimates from observed process/native/GPU memory.
- Count world/cosmetic/native textures, actual compressed formats and mip levels, geometry and combined copies, render targets, shadow maps, UI/preview targets, upload staging, decoded CPU assets and in-flight jobs.
- Attribute ownership to the main scene, preview/Studio views, launcher and context generation. Do not charge shared resources twice.
- Bound caches by bytes as well as counts; use age and pressure-based trimming at safe boundaries.
- Preflight target size with checked arithmetic. Preserve requested quality and expose effective size/fallback when an allocation cannot be honored.
- Keep default memory budgets conservative and configurable by platform/device tier, with observed memory pressure informing safe admission rather than fabricated “available VRAM.”

Existing texture-manager budgets are per owner, not a total-memory guarantee. Unified-memory Macs and Android especially need CPU decode and GPU-facing storage considered together. The accounting work must not be described as an AGX leak fix.

### 5.3 Pacing and latency ownership

At any moment, record one selected pacing owner: software deadline, presentation/VSync, or an optional qualified platform pacing library. Record input sampling, simulation, render-pose preparation, submission and swap as distinct stages.

Android Swappy is a reasonable **conditional** experiment after current policy defects are fixed. It supports OpenGL ES integration and addresses presentation cadence/queueing; it is not a renderer speedup by itself. Integrate it in place of conflicting pacing ownership, not on top of independent software, Choreographer and swap clocks. Keep a qualified fallback. [Android frame pacing][E-ANDROID-PACING], [OpenGL integration][E-SWAPPY]

Do not use unconditional `glFinish` to obtain apparently smooth timing. Any future fence/queue-depth mode must be bounded, capability-gated and measured for both latency and throughput. Missing presentation timestamps remain unavailable; swap return is not panel scanout.

## 6. Implementation work packages

**Dependency notation:** an arrow is expressed as “after Rxx.” “Parallel” means separate files/ownership can proceed concurrently, not that shared renderer changes should be merged without integration review.

**Default completion rule for every package:** focused regression passes; declared baseline/changed evidence; documented fallback; no gameplay/replay drift; status per relevant platform. If physical hardware is unavailable, complete feasible implementation and checks, record native acceptance as NOT_RUN, and do not invent a device pass.

### Phase A — Evidence and concrete correctness repairs

#### R00 — Reconcile the audit and freeze the baseline

**Priority:** First. **Dependencies:** None.

Read the current architecture, FPS and AGX decisions. Diff current head against the audit SHA. Produce `docs/performance/renderer-next/status.md` and a machine-readable manifest binding source commit, loaded client/harness hashes and MVIDs, assets, settings, flags, device/driver, display mode and profiling state.

Inventory effective Android enrolled flags rather than assuming desktop experimental defaults apply there. The desktop PALETTE_PREP, PROCEDURAL_STREAM and DEPTH_VERTEX experiments have historical negative or limited evidence; do not automatically promote them unchanged. Broad Android opt-out is a diagnostic profile comparison, not a single-feature A/B.

**Acceptance:** Every finding is mapped to current source and labeled applicable, resolved, changed or needing reproduction. No fresh benchmark claim is made from old receipts. Reuse existing identity/fixture machinery. [FPS decision][S-FPS-DECISION], [modernization record][S-MODERNIZATION], [Android enrollment][S-ANDROID-ENROLL]

#### R01 — Make capture complete and attribute the full frame

**Priority:** P0 prerequisite. **Dependencies:** R00.

**Files:** `OpenGlFrameProfiler.cs`, `OpenGlFrameCapture.cs`, `FrameTiming.cs`, desktop callback, Android frame reporting and native-FPS analyzer.

Retain the interval that triggers a stall/drop, with timestamps, reason flags and before/after generation. Define boundary-event representation and schema version so the strict analyzer can report a coherent run without silently discarding boundary intervals. Preserve its rejection of malformed, dropped-record, nonchronological or mixed-condition evidence.

Add outer callback, launcher/UI, simulation, preparation, submission, swap-success/failure and wait/wakeup scopes. Keep nested inclusive scope times from being added as if independent. Reuse bounded buffers and async GPU queries; no synchronous query wait or per-frame log allocation.

**Acceptance:** Drive the real timing path with a 100–250 ms catch-up overflow and a >250 ms stall; confirm both events remain observable. Verify that segment initialization also preserves or explicitly labels the following interval instead of silently losing it. Test focus/pause, cap/scene transitions, recording overflow and first/last boundaries. Existing analyzer fixtures still pass with explicit schema migration. [F01 evidence][S-PROFILER], [analyzer contract][S-FPS-TOOLS]

#### R02 — Reproduce and fix desktop numeric-cap failures

**Priority:** P0 investigation. **Dependencies:** R01 for accepted new evidence.

**Files:** `DesktopFramePacing.cs`, `RenderWindow` in `Renderer.cs`, native-render-check and native-fps tooling.

Reproduce 60/120/144/240 plus Display/Unlimited on the current immutable build. The current native harness restricts ordinary runs to Windows/macOS and rejects Display (`-hz 0`); add a focused harness-support change or qualify those cases through the separately instrumented production client before claiming Linux/Display coverage. [Harness limits][S-NATIVE-LIMITS] Instrument desired deadline, actual wakeup, event polling, work start/end and swap. Test CPU-light, representative combat and GPU-saturated scenes to distinguish scheduler error from insufficient rendering capacity.

Change the responsible scheduling/presentation path only after attribution. Preserve one clock owner, stable deadline progression, transition/reset semantics and focus/minimize behavior. Do not change simulation rate to satisfy an FPS target.

**Acceptance:** Previously failing numeric cases meet the predeclared cadence gate on a workload with demonstrated headroom; sustained uncontended run has no unexplained simulation drops. Repeat Windows qualification and run the applicable macOS/Linux cases. Report unresolved platform behavior honestly. [Historical failures][S-WINDOWS], [existing policy][S-PACING]

#### R03 — Correct Android requested versus effective cadence

**Priority:** P1. **Dependencies:** R00; integrate R01 telemetry.

**Files:** `AndroidFramePacer.cs`, `AndroidPerformance.cs`, `GameView.cs`.

Replace the shared Unlimited/unset sentinel with an explicit unset state. Separate attempted and successful surface-rate requests; invalidate by surface/display generation and retry failures with a bounded policy. Distinguish desired swap interval from successfully applied interval and actual active refresh/cadence.

Only surrender software cap enforcement when the presentation owner can enforce the chosen cadence. Handle ignored rate requests, mode changes at a constant selected cap, and unsupported divisors without two competing clocks. Swappy evaluation is a later alternative, not necessary for this repair.

**Acceptance:** On API 30+ with a valid surface, first/resumed Unlimited sends its intended request; failed `SetFrameRate` does not suppress all future retries; failed swap-interval application does not masquerade as successful presentation pacing. Exercise device-supported 60/90/120 modes, cap changes, pause/resume and denied/ignored requests on physical devices. On supported API 24–29 devices, verify observed-refresh/software fallback without calling unavailable `SetFrameRate`. [Supported Android range][S-ANDROID-PROJECT] [Policy and request code][S-GAME-PACING], [pacer][S-ANDROID-PACER]

#### R04 — Bound EGL retry and classify failed presentation

**Priority:** P1. **Dependencies:** R00; coordinate with R03.

**Files:** `GameView.cs`, context/resource generation owners, Android lifecycle tests.

Use surface generation and cancellation-aware waiting/backoff for transient bind/create failures. Wake promptly on new surface, stop or pause. Rate-limit repeated diagnostics. Classify swap errors and keep failed presents separate from successful swap-return intervals.

For context loss, first provide an orderly recoverable shell/restart path with a clear failure reason. Attempt full in-process reconstruction only after scene textures, geometry, shaders, streaming jobs, UI and previews all participate in generation invalidation/rebuild. Resetting facade counters is not resource recovery.

**Acceptance:** Repeated invalid-surface failures do not spin; stop/pause remains responsive; failure injection preserves ownership; stale GL names cannot enter the new generation. Test rapid rotation/background/foreground/surface replacement and real context loss where reproducible. [Lifecycle source][S-GAME-LIFECYCLE], [swap source][S-GAME-SWAP]

#### R05 — Make mip residency authoritative

**Priority:** P1 correctness. **Dependencies:** R00.

**Files:** `PreparedTextureAsset.cs`, `CompressedTextureAsset.cs`, `TextureReplacementPack.cs`, `TextureAssetManager.cs`, texture registration/sampling in `Renderer.cs`, `RetainedRenderGraph.cs`.

Carry actual resident levels, completeness and effective sampling through promotion and binding. Resolve requested filtering against those facts for direct, retained and companion-map paths. Preserve safe native fallback and make replacement transactional. Maintain stable logical material references, or atomically update dependent handles and invalidate retained/sampler state when physical texture names change; current world promotion relies on stable binding identity. Use maximum-level restriction only with a contiguous valid chain and supported API semantics.

**Acceptance:** Base-only, partial and complete ETC2/ASTC KTX2; 1×1/NPOT assets; Auto/Legacy/Custom modes; mip toggles; retained batches; late promotion; failed upload; context recreation. Compare native GLES pixels and GL diagnostics. No test may equate upload success with sampling completeness. [Full path evidence][S-REPLACEMENT], [registration][S-TEX-REGISTER], [retained sampling][S-RETAINED-SAMPLING]

#### R06 — Key shader binaries by what is actually compiled

**Priority:** P1 correctness. **Dependencies:** R00.

**Files:** `AndroidShaderProgramBinaryCache.cs`, `ShaderProgramCacheKey.cs`, `EsShaders.cs`, `GlEs.cs`, shader ownership/variant call sites.

Centralize source preparation so the binary key and compiler consume the same expanded translated GLES text. Include schema/translator version, variant defines and attribute/link ABI, plus driver/context identity. Invalidate old-key entries. Retain atomic writes, bounded entry reads, link checks and source fallback; add bounded total disk-cache pruning.

**Acceptance:** A GLES-only source change changes the key; unchanged inputs remain stable; different variants/link ABI do not collide. Corrupt/truncated/oversize/old-driver binaries recover through source compilation. Cache-hit images match source-compiled images. Keep binary GL operations on the context thread. [Cache][S-BINARY-CACHE], [key][S-BINARY-KEY], [translation][S-TRANSLATE]

#### R07 — Repair GLES UI state and transactional initialization

**Priority:** P1 correctness. **Dependencies:** R00.

**Files:** `RmlUiAndroidGlesState.cs`, `RmlUiAndroidGlesCompositor.cs`.

Select the owned texture unit before any initialization or resize bind/upload. Restore every state item changed by the compositor using its documented boundary contract, including affected binding/pack-unpack state as applicable. Compile/link into temporary owners, clean every failure stage, and publish the ready bundle only after all required programs, buffers, textures and uniforms exist.

**Acceptance:** Enter from a nonzero active unit with an unrelated texture, sampler and nondefault framebuffer state; render/resize UI and verify exact restoration. Inject vertex/fragment/link/buffer/texture failures. No leaked shader and no partially ready bundle can survive. [UI state][S-UI-STATE], [compositor][S-UI-COMPOSITOR]

#### R08 — Couple capture framebuffer and dimensions

**Priority:** P1 correctness. **Dependencies:** R00.

**Files:** `ReadSceneTarget`, `GraphicsPipeline.cs`, `FinalCompositeCapture.cs`, `ScreenCapture.cs`, replay export and preview consumers.

Introduce a small capture-source descriptor: FBO, dimensions, format/encoding, scope and generation. Select scene-resolution or final-output capture explicitly. Use checked buffer-size arithmetic and exact output metadata. On GLES, use portable RGBA8 readback plus explicit RGB repacking, or validate the current attachment’s implementation-selected read format/type. Neutralize pack-buffer, row-length and row/column skip state before reading into managed memory; establish the required alignment, then restore prior state on success or failure. [ReadPixels contract][E-ES3]

**Acceptance:** 50/100/150/200% plus a safely clamped extreme scale, shadows Off/Low/High, cel, window resize, screenshot, thumbnail, replay/Studio and export. Check edge pixels and dimensions, not merely nonempty files. Retain separate visual and performance runs. [Mismatch evidence][S-READBACK], [output target][S-PIPELINE]

#### R09 — Make target allocation lazy, transactional and bounded

**Priority:** P1 memory/stability. **Dependencies:** R08 descriptor contract; independent simple initialization fix can land earlier.

**Files:** target initialization/resize in `Renderer.cs`, `FrameTransientTextures.cs`, outlines, shadow/composite targets.

Initialize target size and format together. Allocate cel color only when needed; release/trim when appropriate. Choose necessary primary depth storage without retaining redundant full-size allocations, while preserving depth sampling, stencil and shared outline semantics. Budget free targets by bytes/age/context; preflight aggregate requests, including the temporary peak while old and replacement targets coexist.

Allocate replacement bundles before committing ownership. On failure retain a valid prior target or a documented lower effective size/disabled optional effect; do not retry expensive failures every frame.

**Acceptance:** One initial allocation set; cel-off target savings; stable target counts and estimated bytes across resize/fullscreen/scale/side-scene cycles; failure injection leaves a drawable scene. Record native/process memory separately. [Allocation][S-TARGETS], [depth][S-DEPTH], [pool][S-POOL]

#### R10 — Make portal capacities safe before optimizing

**Priority:** P1 robustness. **Dependencies:** R00.

**Files:** `Entities/RoomEntity.cs`, visibility fixtures.

Check frustum, clip-point, plane and recursion capacities in release builds. Handle overflow with conservative visibility or another proven safe fallback that does not hide world geometry. Emit one bounded diagnostic with map/graph context. Avoid unbounded growth or recursion from malformed/custom content.

**Acceptance:** Many-path/cyclic graphs, clipping expansion, connector/forcefield cases and maximal supported maps do not index past bounds or drop walls. Native fixtures retain existing visible results. No current failing map is assumed; construct meaningful stress fixtures. [Capacity and clipping source][S-PORTAL]

### Phase B — Measured CPU, memory and streaming improvement

#### R11 — Remove portal preparation allocation and repeated work

**Priority:** High performance candidate. **Dependencies:** R10 and R01.

**Files:** `RoomEntity.cs`, retained-room submission consumers.

Use per-instance/per-view bounded scratch with correct recursion lifetime; compute the render-pose view-projection product once; build stable ordered per-node portal adjacency when geometry changes. Preserve render-frequency refresh, traversal order, conservative clipping and generation invalidation.

**Acceptance:** Zero steady managed allocation from the repaired clipping path after warmup; exact visibility parity on defined legacy fixtures; no stale room pieces during fast 120/144/240 Hz turns, third person, portals, replay or multiview. Demonstrate reduced preparation cost in a portal-heavy current-build comparison. Do not claim total-process zero allocation. [Hot path][S-PORTAL], [refresh contract][S-PORTAL-REFRESH]

#### R12 — Share only compile-identical geometry

**Priority:** High load/memory candidate. **Dependencies:** R00; use R01 attribution.

**Files:** `GenerateLists/DoDlist`, `LegacyGeometryBatch.cs`, `DesktopRetainedGeometry.cs`, resource ownership checks.

Define the complete compilation key, including instruction/source identity, texture dimensions, texgen, room/layout mode and any inherited attributes affecting compilation. Populate the reuse map only after successful compilation. Give shared geometry one clear owner or reference-counted lease; preserve fallback and partial-allocation cleanup.

**Acceptance:** Same display-list ID with different effective inputs remains distinct; exact-compatible uses compile once; unload cannot double-delete. Exercise cold/warm load, replay/Studio/side scenes and injected failures. Compare compiled objects, resident estimates, load times and images. [Ineffective map][S-GEOMETRY]

#### R13 — Complete macOS allocation attribution and reuse eligibility

**Priority:** Existing unresolved P0. **Dependencies:** R00; run in parallel with other fixes.

**Files/tools:** Existing AGX campaign and boundedness tools, shader ownership ledger, all scene/UI/replay/Studio program owners.

First make the assessor reject a missing candidate observation exactly at the declared warmup anchor, with a negative fixture that otherwise shows a late plateau. Reproduce on the current immutable build with at least two independent matched warmup controls and at least three increasing cumulative load observations strictly after the anchor in one process. Require the actual same-process anchor; successive increments and the cumulative span including it must fit the predeclared control envelope. [Validator gap][S-AGX-ASSESSOR] Attribute native allocation origins and lifecycle phases; distinguish compile, link, first draw and disposal. Extend corrected non-double-counting ledger coverage to all relevant owners.

Prove complete identities, uniform/layout reset, ownership and failure behavior before changing runtime reuse. Preserve strict raw-receipt validation and the predeclared control envelope. Add second macOS/GPU/OS coverage and cross-platform lifecycle regressions where hardware is available.

**Acceptance:** A truthful evidence disposition. Closure requires the corrected native increasing-load gate and relevant correctness coverage; its PASS establishes scoped stabilization evidence and does not by itself clear the repository P0/program disposition; a diagnostics-only PR can pass while the problem remains UNRESOLVED_P0. Do not declare memory stabilized from RSS/GC/GL counts alone. [AGX-04 and its exact gate][S-AGX], [corrected ledger][S-AGX-CORRECTION]

#### R14 — Conditional bounded program reuse / prewarming experiment

**Priority:** Conditional. **Dependencies:** R13 establishes eligibility; not authorized by identical GLSL alone.

Implement live-context leases only if R13 proves safe ownership and full identity. Bound retained programs and prewarm work; reset uniform/layout caches correctly when borrowing; invalidate destroyed/unshared contexts. Keep scene unload and auxiliary shader failures correct.

**Acceptance:** Same-scene and cross-owner image/state parity, failure injection, complete retirement, and reversed-order native allocation/performance comparisons pass. A tiny synthetic shader reuse result cannot substitute for full-scene evidence. If benefits or safety are unproved, record NO_ROLLOUT and retain the prior runtime behavior. [Eligibility restrictions][S-AGX]

#### R15 — Reduce measured batch-construction and submission cost

**Priority:** Performance experiment. **Dependencies:** R01; coordinate with R12.

**Files:** `DesktopRetainedGeometry.cs`, `RetainedRoomBatchPlan.cs`, `RetainedRenderGraph.cs`, `RenderPassState.cs`.

First instrument combined-cache misses, CPU concatenation, upload cost, eviction and fallback draws. If significant, prefer stable spatial/material groups, immutable shared buffers/subranges or budgeted preparation; keep ordinary draws when preparation is not ready. Preserve the existing combined-copy budget instead of simply increasing it.

Then test pass-owned VAO/buffer state scopes with restoration at world/UI/preview/external boundaries and at every interleaved legacy/client-array/procedural fallback within a world pass. Restrict new grouping/reordering to the existing `ReorderableOpaque` / `CanMerge` eligibility proofs; spatial similarity alone is insufficient. Convert remaining model/procedural lanes only when traces justify them. Preserve APPLE versus core VAO behavior, current attributes, matrix palette, alpha, stencil and outlines.

**Acceptance:** First traversal and repeat traversal both improve or remain within declared noise/tail gates; no hidden memory growth; no order-dependent image differences. Report hit rate and total frame cost, not only fewer binds or draws. [Construction/draw source][S-BATCH], [pass boundaries][S-PASS-STATE]

#### R16 — Bound texture promotion and settings-change work

**Priority:** Tail-latency improvement. **Dependencies:** R05, R01; coordinate with R09 budgets.

**Files:** modern texture scheduling in `Renderer.cs`, `TextureAssetManager.cs`, prepared assets and promotion pacer.

Extend the existing queue with upload-byte, in-flight decoded-byte and per-job timing accounting. Prioritize visible/near-future assets. Generate missing mips off-thread only under an explicit channel/color/alpha filtering contract; otherwise retain established generation until that contract exists. Use lower-mip-first or safe incremental mip/region uploads where supported, and prewarm native first-use mip work. Schedule quality changes as desired-versus-applied jobs rather than an unbounded same-frame reupload burst.

Use PBO staging/fences only if an isolated experiment shows benefit; never block rendering waiting for a free queue slot. Cancellation and map transitions must retire prepared data without touching the wrong context.

**Acceptance:** Report worst individual upload, first-use hitch rates, p99/p99.9 and decode-memory peaks. Stress a large HD pack, rapid map changes and quality toggles. Placeholders remain correct; completed promotions never regress sampler completeness. [Current queue][S-STREAM], [upload][S-TEXMAN]

#### R17 — Retain authored mips and add qualified desktop compression

**Priority:** Memory/quality improvement. **Dependencies:** R05 and asset descriptor work.

**Files:** `Ktx2TextureAsset.cs`, prepared/modern asset types, compression capabilities and texture upload.

Preserve authored mip chains on RGBA fallback, not only Android compressed paths. Add desktop GPU-native formats only when actual format/channel/driver support is established. Keep compatible macOS and uncompressed fallbacks. Select lower authored levels for quality limits when valid instead of always expanding the largest image then resizing.

**Acceptance:** Native format checks and equivalent visual quality for albedo/normal/masks; actual resident-format bytes, upload time and transcode peaks improve on supported devices. On GLES, validate sampling into a legal framebuffer and reading that result; do not require desktop `glGetTexImage`. Unsupported/failed formats cleanly fall back. Coordinate format selection with R22 transfer-function metadata. Android ETC2/ASTC remains supported and unchanged except deliberate fixes. [KTX2 implementation][S-KTX]

#### R18 — Move Android telemetry cost off the critical path

**Priority:** Targeted performance/measurement improvement. **Dependencies:** R01.

**Files:** `AndroidPerformance.cs`, `AndroidPerformanceHints.cs`, `GameView.cs`.

Keep frame recording allocation-light and bounded. Copy snapshots for CPU aggregation and formatting; obtain platform state through correct callbacks/threads with bounded refresh. Align interval and stage samples to the same frame identity. Represent missing observations explicitly.

Preserve existing ADPF ownership and CPU-work accounting; the existing implementation already excludes swap from the CPU-work report. Do not “fix” it by including presentation sleep.

**Acceptance:** Profile telemetry on/off and verify reduced render-thread periodic work without dropped samples. Identical aligned inputs must retain their aggregation results; version and explain expected changes caused by corrected frame alignment. Raw capture and rolling-window statistics remain correctly labeled. Validate pause/resume/thermal callback teardown. [Telemetry source][S-ANDROID-PERF], [ADPF hints][S-ANDROID-HINTS]

#### R19 — Optimize GLES attachment lifetimes only after proving consumers

**Priority:** GPU bandwidth experiment. **Dependencies:** R09, R20 pass semantics and GPU timing where supported.

Map every color/depth/stencil attachment's producer, last reader, copy/readback/export use and next load. Reduce avoidable FBO switches or attachment churn when a measured native-device trace supports it. Invalidate/discard only after the true last consumer; depth used by shadows, outlines, replay or capture must remain valid.

Compare persistent outline attachment versus transient pooling rather than assuming either is universally faster. Preserve the fallback path and memory budget.

**Acceptance:** Adreno/Mali native timing, images and lifecycle tests; no depth/stencil/capture regression. Separate native-driver timing from translated frame-trace tooling. Arm's tile-renderer guidance supports avoiding unnecessary stores/reloads, not indiscriminate invalidation. [Outline attachments][S-OUTLINES], [Arm framebuffer guidance][E-ARM]

### Phase C — Image quality with explicit cost budgets

All shader-changing packages integrate R06 compiled-source cache identity, maintain desktop/GLES source and variant parity, and follow existing shader ownership/failure conventions. Specify each new pass’s input/output color domain, attachment reads/writes and render-graph position. Work-package dependencies do not prescribe runtime pass order: lighting/bloom/AO, tone/output conversion, AA and native HUD/UI boundaries must remain explicit.

#### R20 — Correct shadow receivers and optional-pass recovery

**Priority:** P1 visual correctness. **Dependencies:** R05, R08–R09.

**Files:** `FrameRenderGraph.cs`, `GraphicsPipeline.cs`, `ShadowMap.cs`, viewmodel/overlay submission.

Define receivers for opaque world, cutouts, dynamic actors, viewmodels, transparency, emissive effects, cel/player outlines and HUD. Choose a receiver mask or a safe composition boundary with correct projection/depth semantics. Do not borrow stencil bits without proving compatibility with the native polygon-ID protocol, which compares the full `0xFF` mask; preserve depth-replay and outline contracts. Do not multiply unrelated late color by opaque world shadow visibility.

Unify bounded retry for shadow-map and compositor failure on explicit setting/context/size transitions. Build the compositor program and uniform bundle transactionally, publish only a ready bundle, and clean up/reset failed optional owners before retry. Do not merely clear the refusal flag while a half-initialized program remains. Preserve the shadow map’s existing target cleanup and native rendering fallback.

**Acceptance:** Dedicated bright-emissive, muzzle/arm-cannon, outline, transparent effect, cutout and moving-player image fixtures; camera/FOV changes and rapid toggles; no native stencil or projection regression. Inject compositor failure, switch Off and lower quality, and verify deliberate recovery. [Graph][S-GRAPH], [shadow/compositor][S-SHADOW], [projection][S-VIEWMODEL]

#### R21 — Improve shadow coverage, stability and cost

**Priority:** Visual/performance improvement. **Dependencies:** R20 and R01 timing.

Preserve existing texel snapping, room light-space collection and PCF. Add or correct dynamic light-space caster collection so visible receivers can be shadowed by offscreen actors/platforms. Improve range fade and texel-footprint/slope-aware bias with fixtures for acne, detachment and grazing surfaces.

Measure static-caster reuse only when light matrix and geometry generation remain valid; compose dynamic contribution correctly. Tune sample count, range and update strategy separately from map resolution. Do not begin with cascades or a universal 4096 default.

**Acceptance:** Moving camera/light-space boundary, offscreen caster, thin geometry and dynamic platform tests; lower shimmering without missing shadows; measured CPU/GPU/memory cost on desktop and Android. [Existing shadow implementation][S-SHADOW], [PCF/bias][S-PIPELINE]

#### R22 — Establish modern color, alpha and material contracts

**Priority:** High-value fidelity. **Dependencies:** R05, R20 and the shared asset/format metadata contract; coordinate with R17 without waiting for all desktop compression work.

**Files:** asset metadata/preparation, KTX2, texture upload/sampling, desktop/GLES material shaders, composite and UI boundaries.

Separate sRGB color/emissive assets from linear normal/roughness/mask data; carry alpha mode. Decode/filter/blend in the intended space and encode once. Desktop and GLES framebuffer-sRGB behavior must be implemented according to their actual API semantics. Preserve the Original/native path and UI contract.

Correct the optional modern specular view vector and normal space; make tangent reconstruction robust. Add authored normal/roughness mip filtering, alpha-coverage preservation and edge/gutter handling. Preserve existing normal renormalization. Propagate alpha/material changes into retained batch identity, shader variants, uniform caches and depth-reuse/coverage eligibility; every merged draw must still share all effective state. Retain ordinary draws when full equivalence is unproved.

**Acceptance:** Reference color ramps, opaque/translucent blends, emissive levels, camera rotation, grazing highlights, mirrored UVs and nonuniform scale. Verify mixed native/HD placeholders and promotions, vertex color, fog, cel and transparency; verify desktop/GLES visual intent and saved-setting migration. [GLES color rules][E-ES3] Do not silently brighten original artwork or treat encoded normal maps as sRGB. [Material code][S-MATERIAL], [asset filtering][S-MODERN-ASSET], [sRGB texture specification][E-SRGB-TEX] and [framebuffer specification][E-SRGB-FBO]

#### R23 — Add spatial AA and restrained reconstruction

**Priority:** First new full-screen quality feature. **Dependencies:** R08–R09, R20; coordinate color domain with R22.

Implement one narrow spatial-AA candidate, such as FXAA or SMAA 1x, with explicit input/output color domain, edge treatment and performance budget. Compare sharp detail, thin geometry, shimmer and cost before choosing a default. Keep native HUD/crosshair and small UI text outside destructive filtering. Offer restrained sharpening for subnative scale only where it improves reference images.

MSAA is a separate experiment: multisample the actual offscreen world target and prove color/depth/stencil resolves, outlines, shadow reconstruction and snapshots. Changing only window samples is insufficient.

**Acceptance:** Same-view native/AA/supersampled comparisons, camera-motion clips, bright thin edges, text and alpha-cutouts; no new halos or excessive blur; platform-specific pass cost and fallback. Do not enable the retired AA field and claim implementation. [Retired settings][S-OPTIONS], [active pipeline][S-PIPELINE]

#### R24 — Add bounded local lighting and improve effect efficiency

**Priority:** Optional fidelity experiment. **Dependencies:** R22 and current CPU/GPU attribution.

Try a small forward-light list for weapon fire, impacts and important emissive sources, with bounded per-object light selection and a documented shader limit. Include effective per-object light identity in batch/merge keys and uniform ownership, or split incompatible draws. Keep gameplay visibility/readability and original lighting available. Avoid a deferred G-buffer rewrite as the first implementation.

Profile translucent particles, trails and decals separately. Use correct frustum/size/lifetime culling, batching and visual-only detail scaling where it reduces overdraw without deleting gameplay cues. Do not alter collision, damage, network events or simulation lifetimes.

**Acceptance:** Two previously batch-compatible objects selecting different lights or coverage must match ordinary draws. Verify combat readability, attachment/presentation parity and no excessive flashing/washed-out surfaces; sustained multi-player/effect-heavy tail times within the chosen quality budget. If CPU preparation dominates, additional fragment lighting is not a remedy for FPS.

#### R25 — Add selective bloom only with a defined color pipeline

**Priority:** Optional. **Dependencies:** R22–R23 and target budgets.

Use an explicit emissive/brightness source, a small downsample chain and bounded half/quarter-resolution filtering. Keep bloom subtle and independently toggleable; preserve HUD, crosshair and competitive readability. Introduce linear floating-point internal color only if required and supported, with tested LDR fallback. If floating-point scene color is introduced, define stable exposure/tone mapping and output encoding explicitly. Internal HDR rendering is separate from HDR monitor output.

**Acceptance:** Bright effects retain shape and nearby dark detail; no full-frame haze, uncontrolled halos, banding regression or unbounded target growth. Capture incremental GPU/frame-tail/memory cost on each enabled tier. Leave disabled where the visual value does not justify cost.

#### R26 — Evaluate small-scale AO/contact shading

**Priority:** Optional and lower than material/shadow work. **Dependencies:** R20–R22, reliable depth/normal contracts.

Prototype a bounded half-resolution, edge-aware AO/contact pass only if reference scenes demonstrate missing depth cues. Exclude/handle viewmodels, sky, outlines, transparency and UI deliberately. Avoid temporally noisy dither in a fast competitive camera unless a valid stabilization contract exists.

**Acceptance:** No dark halos at depth discontinuities, thin geometry or weapon edges; modest scene improvement demonstrated in stills and motion; cost within a predeclared tier budget. Otherwise record NO_ROLLOUT.

#### R27 — Add user-selected adaptive resolution for GPU-bound workloads

**Priority:** Optional performance mode. **Dependencies:** R01 reliable GPU timing, R09 budgets, R23 reconstruction.

Use sustained valid GPU cost, headroom and hysteresis with slow bounded scale changes. Do not react to a single hitch or reduce resolution to mask CPU/driver/scheduler problems. Define min/max/effective scale, allocation strategy and recovery; keep UI/HUD at intended output resolution.

Retain fixed cap/scale defaults and existing Android no-governor policy. A newly selected adaptive mode may adjust only the settings its user-facing contract explicitly covers.

**Acceptance:** GPU-heavy workloads improve deadline compliance with stable image quality; CPU-bound controls do not reduce scale pointlessly; no oscillation, resize churn or undocumented cap changes. Unsupported/disjoint GPU timing falls back to fixed scale.

### Phase D — Complete user flows, optional architecture and qualification

#### R28 — Budget launcher, previews, Studio and capture/export

**Priority:** P1/P2 according to traces. **Dependencies:** R01, R08–R09 and resource ledger.

Include no-scene launcher and pre-render UI work in attribution. Profile chamber, eight-slot lobby, Community entry, hunter previews, replay and four-view Studio. Give side views independent camera/visibility state and explicit resource ownership. Use dirty/event-driven redraw and lower preview update rates only for views that do not require full motion/input responsiveness.

Unify capture descriptors before considering async PBO export. If readback limits export, add a bounded fenced ring with explicit backpressure, ordering and failure cleanup; preserve synchronous screenshot correctness. Require actual PBO/synchronization capabilities; when the ring fills, defer or throttle export production at a safe boundary without dropping/reordering requested replay frames or waiting indefinitely on the interactive render thread. Offline export throughput is a separate metric from interactive FPS.

**Acceptance:** Main view remains responsive with side views/export active; no shared-camera/GL-state contamination or unbounded target accumulation. Preserve replay frame ordering, dimensions, color and metadata. [Callback scope][S-CALLBACK], [multiview qualification][S-MULTIVIEW]

#### R29 — Keep higher-capability/core modernization as a separate program

**Priority:** Deferred experiment. **Dependencies:** Accepted compatibility baseline and measured remaining bottleneck.

Audit all fixed-function dependencies: world/models, procedural effects, matrix palettes, immediate/client arrays, debug drawing, native UI, previews and every shader variant. Convert one lane at a time under compatibility before testing a core context.

Any UBO, persistent-mapped ring, indirect draw or compute path must be capability-qualified and outperform the simpler path on a real target. Do not require desktop-only APIs on GLES 3 or macOS's current context. Do not add Vulkan/Metal as part of this OpenGL plan.

**Acceptance:** Full flow/image/lifecycle parity and a justified performance or maintenance benefit. A working world triangle or main-map screenshot is insufficient. [Context][S-CONTEXT], [native UI][S-UI-NATIVE], [Apple migration][E-APPLE]

#### R30 — Qualify settings, integrate and publish an evidence-based result

**Priority:** Mandatory completion. **Dependencies:** All selected earlier packages.

Keep existing saved presets compatible. Define optional new feature settings independently, then map validated combinations into presets. Record requested and effective feature values and fallback reasons.

Run the platform/workload matrix below against the final integrated immutable build, including original settings, enhanced visuals, sustained combat, lifecycle, memory and multiview. Recheck integration after shared target/state/shader changes, rather than relying solely on isolated PR results.

**Acceptance:** A final report identifies accepted wins, visual tradeoffs, device-specific paths, failures, NOT_RUN cells and remaining unresolved P0 findings. No hardware/default promotion is inferred from compilation alone. Every shipped optional path has a usable fallback and bounded resource behavior.

### Suggested parallel work lanes

| Lane | Suitable work | Integration boundary |
|---|---|---|
| Measurement/pacing | R00–R04 and R18 | Timing schema and presentation ownership |
| Resource correctness | R05–R09, R16–R17 | Texture/target descriptors and context generation |
| CPU preparation | R10–R12, then R15 | Visibility, geometry keys and pass state |
| macOS memory | R13, conditional R14 | Program ownership and lifecycle evidence |
| Visuals after foundations | R20–R27 | Render graph, color/depth semantics and target budgets |

The large `Renderer.cs` is a merge hotspot. Agree descriptor/contracts first and integrate changes sequentially where they touch the same state. Extract small cohesive helpers when necessary; avoid a broad file-splitting refactor mixed into performance comparisons.

## 7. Measurement plan and decision gates

### 7.1 Baseline manifest

Every run must record:

- Source commit and dirty-state status; loaded client/harness hashes and MVIDs; runtime/build configuration.
- OS/build, CPU/GPU, driver and GL/GLES version/profile/extensions; context generation and actual chosen capabilities.
- Device/display identity, active refresh, window/fullscreen/compositor mode, VRR and swap policy where observable.
- Cap mode/value, render/output sizes, quality features, texture pack/asset identity, effective optimization flags and profiling configuration.
- Map/route, player/bot/hunter counts, RNG/workload identity, camera/input protocol, warmup and measurement boundaries.
- Power/battery/thermal state, background workload, fresh/warm cache and launch state.
- Raw timing/event files, images/clips where relevant, diagnostic logs, failure and overflow counts.

Reuse the existing provenance machinery. “Same map name” is not a complete same-workload claim.

### 7.2 Metrics to report

| Goal | Required metrics | Interpretation |
|---|---|---|
| FPS/cap compliance | Mean completed-frame FPS; explicit interval boundary; requested and effective cap/mode | Application callback or swap-return timing is not scanout. |
| Smoothness | p50/p95/p99; p99.9 when sample count supports it; worst intervals; >1.5× and >2× numeric budget rates; >33/50/100 ms counts | Preserve raw chronological intervals and all in-game hitches. |
| True 1% low | `1000 / mean(slowest ceil(0.01 × N) intervals in ms)` | Do not substitute `1000 / p99`; label that separately as a proxy. |
| CPU cost | Wait/event/simulation/preparation/submission/UI/swap scopes; managed allocation and GC events | Inclusive nested timings are not additive. Process allocations are not automatically renderer allocations. |
| GPU cost | Async timer-query pass/frame duration where supported; query availability/disjoint/drop status | Never fabricate GPU cost from CPU timing or force synchronization for each result. |
| Stability | Failed presents, EGL/GL errors, simulation drops/stalls, recovery outcomes, crash/hang counts | Distinguish lifecycle transitions from in-game misses without erasing either. |
| Memory | Owned resources/estimated bytes, native/process/managed observations, peak decode/staging, load-count trajectory | GL deletion, resident estimates and RSS answer different questions. |
| Visual quality | Controlled stills plus motion clips; exact invariants and intended-difference review | Pixel equality is appropriate for correctness optimizations, not every intentional visual upgrade. |
| Latency/power | Input timestamps to defined boundaries; physical latency when available; sustained thermals/energy | FPS alone does not establish latency or sustained mobile performance. |

Use the existing bounded asynchronous query rings. GLES disjoint-query results must be rejected/flagged when invalid; CPU elapsed time is not a GPU measurement. [Timer-query specification][E-TIMER]

### 7.3 Frame budgets

| Target | Budget per frame |
|---:|---:|
| 60 FPS | 16.667 ms |
| 90 FPS | 11.111 ms |
| 120 FPS | 8.333 ms |
| 144 FPS | 6.944 ms |
| 165 FPS | 6.061 ms |
| 240 FPS | 4.167 ms |

These are arithmetic budgets, not promised performance. In a pipelined renderer CPU and GPU work overlap; do not add unrelated full CPU/GPU durations to predict throughput. Track each critical path and reserve headroom for input, simulation, UI and OS variability.

### 7.4 Controlled comparison protocol

1. Select one declared hypothesis and one change. Fix binary, content, route, cap, graphics, power and driver conditions.
2. Separate startup/shader/first-use tests from warmed steady-state tests; both matter.
3. Use unprofiled control runs for user-visible timing and separate attribution runs. Visual readback and failure-injection runs are not performance samples.
4. For exploratory changes, use at least five trials per arm with balanced/reversed ordering and approximately two minutes of useful steady data per trial where practical. Increase only to resolve observed noise; these are planning defaults, not substitutes for the existing harness contracts.
5. For accepted candidates, use representative 10-minute workloads plus a 30-minute uncontended stability session. Android additionally needs thermal steady-state comparison and cold-launch/pause-resume repetitions.
6. Retain every valid trial and every excluded trial with its exclusion reason. Do not choose the best run or hide concurrent activity after the fact.
7. Use same-build isolated switches where available. Cross-build comparisons require full identity and a clear diff. A broad Android enrolled-versus-opt-out comparison cannot attribute a gain to one renderer feature.
8. Predeclare the meaningful improvement and allowable regression based on control variation. Report medians/ranges and raw evidence; add confidence intervals only with an appropriate method and enough independent trials.

**Proposed engineering gates, to freeze before the campaign:**

- Correctness: no unintended gameplay/replay drift, visibility loss, state corruption or image change on preservation fixtures.
- Cap correctness: on an adequately provisioned fixed numeric-cap workload, investigate sustained overshoot above 1% or under-delivery beyond 2%; establish a justified platform tolerance before acceptance. GPU saturation is a separate condition, not automatic scheduler failure.
- Tail performance: no regression exceeding both the measured control envelope and a predeclared material tolerance; 3–5% is a useful starting range for investigation, not a universal statistical law.
- Stability: no unexplained simulation drops, crashes, hangs or failed presents in a qualified uncontended workload; preserve separate records for deliberate lifecycle faults.
- Memory: bounded owned caches/queues and stable repeated-load behavior under the relevant native gate. AGX keeps its stricter existing attribution/increasing-window requirements.
- Visual additions: incremental CPU/GPU/memory cost fits that preset/device's measured headroom. A feature may ship opt-in even if it is inappropriate for a performance preset.

Do not erase a regression by lowering the comparison workload, render scale or effects only in the candidate.

### 7.5 Required workload and hardware matrix

| Workload | Questions it must answer |
|---|---|
| CPU-light numeric-cap scene | Does pacing hit 60/120/144/240 with headroom? |
| Representative eight-player/bot combat | Where are preparation, draw setup, effects, GPU and frame tails spent? |
| Portal-heavy route with rapid turns | Do bounds, current-camera visibility and allocation repairs hold? |
| First map traversal / first effect / first HD asset | Are batch construction, shader materialization and uploads causing hitches? |
| Dense transparency/decals/cutouts | Are ordering, overdraw and shader costs acceptable? |
| Shadows: offscreen casters, viewmodel, emissive, thin geometry | Are receivers, coverage, bias and range correct? |
| Resize/fullscreen/render-scale/preset changes | Are allocations transactional, bounded and correctly resized? |
| Launcher/lobby/preview/replay/four-view Studio | Does whole-application performance remain responsive? |
| Screenshots/thumbnails/video export | Are source dimensions, color, order and GL state correct? |
| 30-minute combat plus repeated map loads | Do stability, memory and thermals hold? |
| Focus/minimize/pause/resume/surface/context changes | Are time, pacing and resources restored safely? |

Minimum device coverage should include Windows discrete NVIDIA and AMD plus Intel graphics; macOS Apple Silicon and Intel if still supported; Linux Mesa and proprietary NVIDIA on supported sessions; Android Adreno and Mali including memory-limited and high-refresh devices. These are qualification targets, not claims that the audit ran them.

Windows PresentMon can provide external presentation evidence, but its own documentation warns that OpenGL “Other” runtime instrumentation is less complete and some CPU/pacing/latency fields are limited. Correlate it with application traces rather than treating every field as ground truth. [PresentMon limitations][E-PRESENTMON]

Android AGI OpenGL ES frame tracing uses ANGLE translation to Vulkan. Use it to inspect rendering structure, but separately validate final timings and behavior on the native GLES driver. Prefer native system traces and supported GPU tools for deployment-path timing. [AGI tracing model][E-AGI]

### 7.6 Choose the next optimization from the measured bottleneck

| Observed pattern | Next investigation | Candidate work |
|---|---|---|
| Missed cap with low CPU/GPU work but late wake/swap | Effective pacing owner, deadline drift, display mode and queue behavior | R02–R04; optional qualified latency/pacing integration |
| Preparation dominates; reducing resolution changes little in an uncapped/headroom-controlled test | Portal traversal, item/material setup, allocations and visibility work | R11–R12; profile before further packet/state caching |
| Driver/submission dominates | Draw-time batch builds, interleaved state ownership, model/procedural lanes | R15; keep current correctness fallback |
| GPU duration falls materially at lower resolution | Shadows, full-screen passes, transparency/overdraw and texture/material cost | R19–R23; R27 only as an explicit selected mode |
| Mostly first-traversal/first-asset hitches | Shader first draw, combined geometry creation, uploads and mip generation | R13–R17 with startup and steady runs separated |
| Periodic hitch matches telemetry/GC or growing memory | CPU diagnostic work, allocation sources, resource and native-memory trajectories | R09, R11, R13, R18; do not infer causality from timing coincidence alone |

These are diagnostic hypotheses. A capped workload can hide throughput changes, and resolution sensitivity alone does not measure GPU bandwidth or overdraw. Change one variable and confirm the attribution before expanding the optimization.

## 8. Existing validation commands to reuse

Run from the repository root. Verify current project/tool requirements before execution; paths below are examples and evidence outputs must use fresh directories. These commands were identified from source/docs, **not executed as part of this audit**. The shown shell syntax is POSIX; use the equivalent environment-variable syntax on Windows.

### Builds and offline checks

```sh
dotnet build src/MphRead/MphRead.csproj -c Release
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true
dotnet build tools/native-render-check -c Release
dotnet build tools/render-resource-check -c Release
bash tools/check-opengl-only.sh
python3 -m unittest discover -s tools/native-fps -p 'test_*.py' -v
python3 -m unittest discover -s tools/native-render-check -p 'test_*.py'
dotnet run --project tools/android-pacing-check -c Release
```

Builds and deterministic/offline tests are useful gates; they do not require or establish physical graphics qualification. Keep server/headless build compatibility when adding renderer-only helpers.

### Representative native timing and analysis

```sh
PROJECT_PRIME_GL_FRAME_CAPTURE=/absolute/fresh/frames.json \
dotnet tools/native-render-check/bin/Release/net10.0/native-render-check.dll \
  -room 'MP3 PROVING GROUND' -players 8 -size 1920x1080 \
  -hz 144 -seconds 604 -shadows Off \
  -output /absolute/fresh/native-result.json

python3 tools/native-fps/analyze.py \
  /absolute/fresh/frames.json -o /absolute/fresh/frame-report.json
```

This current native-harness recipe is for Windows/macOS numeric caps. Linux and Display mode require the R02 harness extension or a production-client capture; do not silently substitute a different mode. `-seconds 604` requests simulation duration including 240 warmup ticks; report observed wall duration too.

The harness needs operator-provided extracted assets and its documented user-data/paths configuration. The default raw capacity is 262,144 rows, which covers this 604-second/144 Hz example. A 30-minute 240-cap run needs roughly 432,000 measured rows: set `PROJECT_PRIME_GL_FRAME_CAPACITY=524288` for that recipe, or use complete harness intervals. The maximum is 1,048,576; preflight every cap/duration and allow measured headroom for Unlimited. Never accept overflow. [Capture limits][S-CAPTURE-LIMITS] Its ordinary workload is frozen-device input with bot AI, not a replay merely because a replay artifact is attached. Use its separate shot/pose fixtures for images and presentation invariants. [Native harness README][S-NATIVE-TOOLS], [FPS analyzer][S-FPS-TOOLS]

### Resource, shader and AGX checks

```sh
dotnet tools/render-resource-check/bin/Release/net10.0/render-resource-check.dll \
  -shadercycles 120

python3 tools/native-render-check/assess_agx_boundedness.py \
  --campaign /absolute/campaign \
  --noise-campaign /absolute/control-a \
  --noise-campaign /absolute/control-b \
  --control-loads 60 --warmup-loads 60 --minimum-windows 3 \
  --output /absolute/fresh/agx-assessment.json
```

Retain the native resource-stress/failure-injection modes documented by the renderer tooling, and add targeted cases for new owners. The AGX tool validates actual campaign receipts; an example command cannot manufacture sufficient increasing-load evidence. [Resource tooling][S-MODERN-TOOLS], [AGX gate][S-AGX]

### Android build and comparison

```sh
dotnet publish src/MphRead.Android/MphRead.Android.csproj -c Release

# Diagnostic broad-profile baseline only; retain enrolled production policy.
dotnet publish src/MphRead.Android/MphRead.Android.csproj \
  -c Release -p:AndroidPerfOptIn=false

python3 tools/android-perf-compare.py --self-test
python3 tools/android-perf-compare.py \
  --baseline baseline.log --candidate candidate.log \
  --minimum-windows 60 --max-p99-regression-pct 5 \
  --json-out android-result.json
```

Install/run and collect the documented `androidperf` logs on real devices. The comparison's p99 gate is **median window p99**, not a reconstructed global raw-frame p99. For feature-specific claims, keep enrollment constant and isolate that feature. An APK build does not prove refresh behavior, image parity, lifecycle recovery or sustained thermals. [Enrollment][S-ANDROID-ENROLL], [hardware comparison][S-ANDROID-ACCEPT]

## 9. Proposed quality policy

Do not silently rename or remap existing saved presets. The table below is a design direction for new or explicitly migrated options after measurements, not current implemented behavior.

| Goal | Recommended direction | Avoid |
|---|---|---|
| Original/native | Preserve current art, lighting, HUD and user scale/cap. Correctness/stability repairs still apply. | Global color/lighting changes disguised as a bug fix. |
| Competitive/performance | Sharp native presentation where affordable, stable mip/aniso, optional low-cost spatial AA, restrained shadows/effects, best frame tails. | Automatic resolution cuts in CPU-bound scenes or added blur. |
| Balanced visual | Modern texture/material contract, validated spatial AA, stable medium-cost shadows and selective local lighting. | Stacking every feature without pass budgets. |
| High fidelity desktop | Optional higher shadow quality, selective bloom/AO, supersampling or other AA according to measured tradeoffs. | Treating 150/200% scale or 4096 shadows as free. |
| Android sustained | Existing enrolled optimizations, native compression, bounded targets/decodes, deliberate cap, qualified shadow/AA options. | Unrequested cap/scale governors or cold-run-only claims. |

Expose the effective setting and reason when a capability or allocation limit forces fallback. Avoid a confusing settings panel containing active controls for retired/nonfunctional effects.

## 10. Definition of done and AI handoff prompt

### Minimum successful outcome

Track **implementation complete / native qualification pending** separately from **fully accepted**. The requirements below define full acceptance for the selected scope. Missing hardware can leave implementation complete with NOT_RUN cells, but cannot be called full performance or platform acceptance.

- All source-confirmed correctness repairs selected from R01–R12 have focused regressions and applicable native validation.
- Current desktop numeric-cap and sustained-stability results are explicit, including any remaining failures.
- Android cap, surface, shader-cache, texture and UI cases are validated on the declared physical devices.
- At least one substantive CPU, memory/upload or GPU improvement has current controlled evidence; improvements are reported by workload/device rather than as a universal percentage. If no experiment meets its gate, report that result and the program remains incomplete; never force a rollout to satisfy this goal.
- Image improvements have still/motion evidence, measured incremental cost and an unchanged Original option.
- Resource bounds cover main scene, launcher, replay/Studio and transitions; native AGX status is reported independently and truthfully.
- All four platform cells have evidence or a clear NOT_RUN limitation; build success is never substituted.
- The final integrated build, not only intermediate PRs, has the selected acceptance matrix.
- Rejected experiments and fallback reasons remain documented.

### Suggested receiving-agent prompt

```text
Implement the attached Project Prime OpenGL / Renderer Audit and Implementation
Plan against the current repository. Begin with R00 reconciliation and the
measurement/correctness foundation. Keep OpenGL/OpenGL ES, fixed 60 Hz simulation,
gameplay/network/replay invariants, high-refresh presentation, compatibility
fallbacks and current Android enrollment/no-governor policy.

Treat the pinned SHA as audit evidence, not permission to overwrite newer work.
Use small reviewable changes. Prioritize concrete defects, then measured CPU,
memory/upload and GPU improvements, then independently budgeted visual features.
Do not enable retired controls or introduce a global shader cache as a shortcut.

For each package, provide the implementation, focused tests, exact commands
actually run, evidence paths, platform scope, fallback and PASS/FAIL/NOT_RUN/
NO_ROLLOUT status. Distinguish source reasoning from hardware reproduction.
Do not invent FPS gains, device tests, visual parity or AGX closure.

Proceed autonomously with authorized reversible work. If hardware is unavailable,
finish the feasible implementation and offline checks, prepare exact native
qualification steps, and report the remaining hardware gate rather than
claiming completion. Do not change release defaults or publish beyond the
authority given for the implementation task.
```

## 11. Source ledger

All repository links below are pinned to the audited SHA. Line ranges identify the reviewed implementation; the agent must reconcile them with its working head. External references are official platform/specification or tool-owner documentation consulted for this plan.

### Repository evidence

[S-CONTEXT]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/DesktopGlContext.cs#L88-L96
[S-GAME-CONTEXT]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead.Android/GameView.cs#L756-L788
[S-TRANSLATE]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/EsShaders.cs#L699-L733
[S-RETAINED-DOC]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/docs/architecture/retained-renderer.md
[S-GEOMETRY]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Renderer.cs#L1041-L1085
[S-BATCH]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/DesktopRetainedGeometry.cs#L347-L547
[S-GRAPH]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/FrameRenderGraph.cs#L80-L204
[S-RETAINED-PASSES]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/RetainedRenderGraph.cs#L934-L1091
[S-TIMING]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/FrameTiming.cs#L183-L226
[S-PACING]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/DesktopFramePacing.cs
[S-LATENCY]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/LowLatencyController.cs#L32-L170
[S-STREAM]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Renderer.cs#L1670-L2135
[S-TEXMAN]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/TextureAssetManager.cs#L109-L221
[S-KTX]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/Ktx2TextureAsset.cs#L41-L213
[S-SHADOW]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/ShadowMap.cs#L38-L247
[S-PIPELINE]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/GraphicsPipeline.cs#L1-L376
[S-MATERIAL]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Shaders.cs#L221-L256
[S-OPTIONS]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/RenderOptions.cs#L324-L434
[S-SHADER-OWNERSHIP]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/SceneShaderOwnership.cs
[S-AGX]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/docs/engineering-next/agx04.md
[S-PROFILER]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/OpenGlFrameProfiler.cs#L40-L212
[S-WINDOWS]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/docs/performance/native-fps/windows.md#L103-L139
[S-FPS-DECISION]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/docs/performance/native-fps/decision.md
[S-ANDROID-PERF]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead.Android/AndroidPerformance.cs
[S-ANDROID-PACER]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead.Android/AndroidFramePacer.cs
[S-GAME-PACING]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead.Android/GameView.cs#L1338-L1477
[S-GAME-LIFECYCLE]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead.Android/GameView.cs#L609-L903
[S-GAME-SWAP]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead.Android/GameView.cs#L1206-L1235
[S-COMPRESSED]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/CompressedTextureAsset.cs#L1-L46
[S-REPLACEMENT]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/TextureReplacementPack.cs#L119-L131
[S-TEX-REGISTER]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Renderer.cs#L1840-L1935
[S-SAMPLING]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Renderer.cs#L7172-L7214
[S-RETAINED-SAMPLING]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/RetainedRenderGraph.cs#L884-L916
[S-BINARY-CACHE]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead.Android/AndroidShaderProgramBinaryCache.cs
[S-BINARY-KEY]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/ShaderProgramCacheKey.cs
[S-UI-STATE]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead.Android/RmlUiAndroidGlesState.cs#L60-L140
[S-UI-COMPOSITOR]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead.Android/RmlUiAndroidGlesCompositor.cs
[S-READBACK]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Renderer.cs#L3054-L3100
[S-TARGETS]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Renderer.cs#L710-L910
[S-DEPTH]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Renderer.cs#L3130-L3177
[S-POOL]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/FrameTransientTextures.cs#L99-L164
[S-SCALE]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/RenderOptions.cs#L106-L122
[S-PORTAL]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Entities/RoomEntity.cs#L893-L1369
[S-PORTAL-REFRESH]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Entities/RoomEntity.cs#L1685-L1702
[S-ANDROID-ACCEPT]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/docs/performance/android-hardware-acceptance.md
[S-VIEWMODEL]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Renderer.cs#L5907-L5916
[S-MODERN-ASSET]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/ModernTextureAsset.cs#L368-L407
[S-CALLBACK]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Renderer.cs#L9249-L9360
[S-UI-NATIVE]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/native/rmlui-poc/projectprime_rmlui_gl2.cpp#L96-L148
[S-MODERNIZATION]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/docs/performance/opengl-modernization-results.md
[S-ANDROID-ENROLL]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/docs/performance/android-release-performance-opt-in.md
[S-FPS-TOOLS]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/tools/native-fps/README.md
[S-AGX-CORRECTION]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/docs/engineering-next/agx02-counter-correction.md
[S-PASS-STATE]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/RenderPassState.cs
[S-ANDROID-HINTS]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead.Android/AndroidPerformanceHints.cs
[S-OUTLINES]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/src/MphRead/Mods/Render/PlayerOutlines.cs#L142-L225
[S-MULTIVIEW]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/docs/architecture/replay-studio-multiview-qualification.md
[S-NATIVE-TOOLS]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/tools/native-render-check/README.md
[S-MODERN-TOOLS]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/tools/opengl-modernization/README.md

[S-AGX-ASSESSOR]: https://github.com/AntiNotAnti/Project-Prime/blob/07f23c014a408d134d1cc67e2f1d7074fc7f7942/tools/nati