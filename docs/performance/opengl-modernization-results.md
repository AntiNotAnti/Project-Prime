# OpenGL modernization implementation and measured results

Updated October 12, 2026. The requested room/shader/shadow implementation and resource stress tooling are implemented. Native memory stability and broad hardware acceptance remain unresolved. The user authorized shorter tests and enabling safe work-reducing paths even when FPS is unchanged. Results below distinguish implementation, measured Mac behavior, and outstanding validation.

## Latest: room validity, unlit specialization, shadow preparation and resource testing

Measured client MVID `580b3bd6-0bcf-4609-bc76-0a59862e6d69`. This supersedes the earlier completion candidate `680c26eb-c1eb-411a-aaaa-1d5434de33bd` for final FPS claims. The long memory test is explicitly tied to the earlier binary; a shorter all-failure-stage lifecycle rerun validates the final cache fix.

- Room submission now interns exact material identities in a scene-owned bounded cache, avoids a duplicate material payload in every packet, combines membership and merge-validity scans, and reuses proven adjacent mesh object preparation across material batches. Current transforms, lighting, palette revisions, visibility and ordered barriers remain authoritative. Current vertex color is still restored for every mesh, and atlas fallback avoids preparing its first mesh twice.
- A fourth prewarmed plain/unlit world shader removes vertex lighting only for a proven entirely unlit eligible frame. The full/native/plain programs retain lit, cosmetic, outline and viewmodel behavior. The native unlit diagnostic confirms active specialization without steady-state program switches. Additional per-item program variants were not selected because extra switches would add submission work; normal/UV generation, skinning, alpha and depth behavior stay intact.
- Shadow collection uses weakly owned immutable 16-node bounds groups as conservative broad rejection, then retains original per-node visibility checks and order. Adjacent compatible room casters reuse shared/object preparation. Unknown bounds, changing pose/palette and incompatible material state keep their ordinary path. The small MP3 combat fixture exercises two group tests but no broad rejects; no culling savings are claimed for that room.
- Fixed the concrete shader-prewarm regression: the old single matrix-layout cache was replaced by the fourth shader's layout, causing ordinary combat draws to invalidate palette stamps and upload matrices repeatedly. Layouts now belong to their linked programs on desktop and GLES. Relink/delete invalidates the affected layout; context reset clears all. No per-frame uniform-location queries or layout allocations were added. Eight new regression assertions cover cross-program registration, deletion/relink and context reset.

All five new policies (`ROOM_IDENTITY`, `MESH_OBJECT`, `SHADOW_PREP`, `UNLIT_SHADER`, `SHADOW_GROUPS`) are enabled, bringing enabled policies to 25 of 27. Earlier `DEPTH_VERTEX` and `PROCEDURAL_STREAM` remain opt-in following unfavorable/mixed measurements. Shared preparation and shaders apply to Windows/macOS/Linux and Android; desktop atlas/VAO paths remain desktop-specific. Android compilation is not device performance acceptance.

Final controls use eight-player MP3 PROVING GROUND, 1920×1080, unlimited/VSync-off on Apple M4 Pro. Each runs 12 simulated seconds with about eight measured seconds after warm-up. Profiling, screenshots and resource stress are separate. Values are means of per-run summaries, not pooled percentiles.

| Final comparison | Baseline FPS | Enabled FPS | Baseline p99 ms | Enabled p99 ms |
| --- | ---: | ---: | ---: | ---: |
| Same build, five new flags off → on (2 runs/arm) | 532.36 | 542.06 | 4.6903 | 4.6960 |
| Previous delivery → fixed build (2 runs/arm) | 530.22 | 540.40 | 4.8543 | 4.8899 |
| Profiled combat (2 runs/arm) | 517.11 | 529.51 | 5.3704 | 4.9263 |
| Profiled Low shadows (2 runs/arm) | 418.31 | 432.00 | 5.5883 | 5.0305 |
| Profiled High shadows (2 runs/arm) | 420.15 | 418.84 | 5.3685 | 5.4628 |
| Profiled Ultra shadows (4 runs/arm) | 416.10 | 405.69 | 5.3977 | 5.5514 |

Observed unprofiled throughput improves **1.82% same-build** and **1.92% over the prior delivery**, while p99 is **0.12% / 0.73% higher**. This is a small observed gain, not a proven broad performance win or complete resolution of world submission. The shadow results are mixed: Low +3.27% FPS, High −0.31%, Ultra −2.50%; Ultra was repeated in reverse order and all four runs per arm remain included. No quality reduction was used to improve these numbers. Short tests do not establish significance or performance non-regression. All retained performance controls have zero simulation drops/stalls.

The corrected profile pair and reverse repeat show World CPU p99 **1.2607→1.1996 ms** (about −4.85%), with approximately **63.63 adjacent mesh object reuses** and **42.94 room identity reuses** per frame. The candidate now uploads about **36.98 matrix arrays / 19,716 bytes** per frame instead of the pre-fix candidate’s **63.18 / 42,961**; baseline is 36.81 / 19,539. Cache skips are 26.59 rather than zero; additional redundant calls are avoided before reaching the cache. Both arms retain nine program binds and zero steady-state variant switches. Roughly 450 immediate draws remain per frame, so broader submission cost is still material. The separate final unlit run confirms the fourth program is active without extra steady-state switches.

Fill/texture response profiling did not justify reducing quality: enabled FPS is **538.04 / 528.81 / 533.54** at 100% / 75% / 50% linear render scale, and **525.95 / 522.41** at High / Low texture quality. These are short CPU/frame-response proxies, not direct texture bandwidth or true overdraw counters. Lack of a consistent response to lower pixel or texture demand is consistent with CPU/driver submission limits, but unsupported GPU timers prevent definitive attribution.

Final native validation uses **115 exact image/depth comparisons**, with the bomb control explicitly replaced by independently repeated controls. The original disabled bomb arm differed in four body images; two fresh disabled arms and both enabled arms match all 51 bomb image/depth files exactly. The cause of the original discrepancy is undetermined. Its [failed initial comparison](opengl-completion-postfix-initial-validation.json) and [repeat provenance](opengl-completion-bomb-repeat.json) remain preserved, rather than relabeling the original mismatch as a pass. The final enabled build independently matches **108/108 previous-delivery image/depth references**. See [validated final captures](opengl-completion-postfix-results.json), [aggregates](opengl-completion-postfix-aggregate.json), and [upgrade visuals](opengl-completion-postfix-upgrade-visual.json).

The pre-fix candidate's short controls showed −1.86% same-build FPS and −1.61% versus prior delivery, with zero palette-array skips and increased matrix traffic. Those results and the discovered cause are preserved in [pre-fix capture validation](opengl-completion-results.json) and `artifacts/opengl-modernization/completion-final-aggregate-validated.json`; they are not combined with the final fixed binary. A first cross-build ABBA group with a dropped-step arm was rejected as a whole and preserved under `completion-cross-rejected-20261012/`.

Resource evidence: a 30-cycle run passed 60 map reload arms, 90 Studio viewport cache reloads and 30 actual injected failures. A separate **60-cycle / 368.52-second** diagnostic passed **120 exact color/depth arms, 180 Studio viewport cache reloads and 60 failures** across retained VBO/VAO/combined allocations, depth snapshot FBO and shadow FBO allocation. All tracked renderer-owned resources were zero after each unload. Depth-snapshot allocation uses an explicitly labeled live-target fixture where the idle scene does not naturally require a snapshot. Studio coverage invokes the actual editor renderer/cache invalidation path; it is not a full Studio UI automation test.

**Native/process memory boundedness is not established.** In the 60-cycle test, ten-arm RSS means rose from 759 to 1,183 MiB while GC committed memory stabilized near 278 MiB. All 120 asynchronous audio shutdowns completed after a final 5.5 ms wait, with no faults; this did not lower RSS. Diagnostic full GC/LOH compaction, finalizers and GL completion lowered RSS by about 143 MiB to **1,042 MiB**, leaving substantial unexplained growth. A native process sample observed audio create/destroy lock contention during reloads, but does not prove the cause of RSS growth. Private-memory metrics are unavailable on this Mac. See [memory findings](opengl-completion-memory.md) and [raw-linked memory report](opengl-completion-memory.json). Memory stability, Windows/Linux/Android hardware behavior, sustained thermal performance, input latency and exhaustive map/Studio/replay coverage remain open; the original plan is not fully accepted.

Final cache-fix validation: **535/530 pure PASS records** (default/forced legacy), **366/256 synthetic pixel checks**, desktop/server/Android arm64 builds with **31/23/64 warnings and zero errors**, thirteen analyzer tests, OpenGL-only contract and whitespace checks. The final binary’s five-cycle failure rerun passed all ten map arms, fifteen Studio viewport reloads and all five injection stages; see [final lifecycle rerun](opengl-completion-postfix-memory.json). This short rerun does not replace the longer pre-fix memory diagnostic or establish native-memory boundedness.

The **self-contained osx-arm64 build** is packaged at `dist/opengl-completion-20261012/ProjectPrime-v0.1.50-osx-arm64.tar.gz`, with an extracted app beside it. Version `0.1.50`, informational version `0.1.50.opengl-completion.20261012`; local ad-hoc signing. ARM64/native-library/JIT entitlement checks, shipped-map and no-retail-assets checks, fresh-home startup/GLFW/launcher/thumbnail tests on both original and extracted package passed. The extracted app also passes all 535 pure records and 366 synthetic pixel checks. Packaging uses the same frozen production source as the measured client; RID/version metadata changes the assembly identity. Build details and hashes are in [delivery manifest](opengl-completion-delivery.json).

## Previous delivery: persistent room groups and frame-level shader selection

Client MVID `05d10db6-7342-42ea-ad0d-0407c7bbe2ea`, SHA-256 `f90128c0ca9308767919ee5fc3161bcb3cea0ec1af022175207f60f8ac7d1d83`. Production source is frozen for all final captures below.

- `ROOM_GROUPS` splits opaque material runs into ordered compatible room subsets instead of abandoning the entire run when one object has a different pose or is not room geometry. It preserves visibility, order, dynamic barriers and the 1,024-ID limit. Scene plans retain exact ordered membership and only recopy/hash when membership changes. Desktop draw tokens skip repeated atlas dictionary/hash/ID lookup; context identity, geometry generation and membership revision must match. Recompile, delete, eviction and context reset invalidate tokens. Every execution still renews the exact pose/light/material merge proof after shadow collection. The existing 256-plan and 64 MiB atlas bounds remain. Unsupported/GLES batching keeps its original larger shared-state runs without extra subgroup preparation.
- `PLAIN_SHADER` adds a third prewarmed program with only the inactive cosmetics body removed from the native material shader. It selects after collection, before world setup, and persists across eligible frames. Eligibility covers all three retained lists, the outline replay queue and separate preview items. Active skin/effect/dissolve (including NaN), advanced materials, shadows, pause maps and editor previews retain an appropriate full/native program. Launcher previews explicitly select full/native; editor scopes retain their full-program override. Shader compile/link failure retains the ordinary program. GLSL 1.20 and GLES 300 sources share the same transformation. There are no extra per-frame program bundle allocations or palette-layout queries.
- Corrected an existing atlas edge case: a nonfinal list's trailing normal/UV/color write can feed the next list's inherited attributes. Such groups now use ordered fallback, with exact native pixels and final current attributes covered by a new fixture.

All twenty enabled policies remain enabled under the user's safe-work-reduction preference. `DEPTH_VERTEX` and `PROCEDURAL_STREAM` remain opt-in from earlier slower/mixed observations. Desktop room atlas work applies to Windows/Linux/macOS; shader specialization and shared eligibility apply to Android as well. Android compilation is not real-device performance acceptance.

Final short controls use eight-player MP3 PROVING GROUND, Original settings, shadows off, 1920×1080, unlimited/VSync-off on the same Apple M4 Pro. Each run uses 12 simulated seconds, excluding the first 240 steps (about eight measured seconds). Same-build ABBA toggles only ROOM_GROUPS/PLAIN_SHADER; the previous eighteen enabled features stay on. Delivery ABBA intentionally compares the preceding delivered binary with the current enabled binary. Image readback and profiling remain separate.

| Final comparison, mean of two runs per arm | Baseline FPS | Candidate FPS | Baseline p95 / p99 ms | Candidate p95 / p99 ms |
| --- | ---: | ---: | ---: | ---: |
| Same build, new flags off → on | 535.95 | 537.70 | 3.4846 / 4.2881 | 3.6038 / 4.5364 |
| Previous delivery → current | 533.16 | 528.88 | 3.5107 / 4.3381 | 3.5629 / 4.5043 |

This is **effectively flat throughput, not a demonstrated FPS win**: +0.33% same build and −0.80% versus delivery. Aggregate p99 worsens 5.79% and 3.83%, respectively; the tail observations must not be hidden behind the work-reduction counters. These short samples do not establish statistical significance or prove performance non-regression. No visual/gameplay regression was observed in the fixtures, and all ten final performance runs have zero dropped simulation steps and zero stalls. Sustained FPS, input latency and GPU timing remain unverified.

The separate profiled pair retains 15 complete windows per arm. Eligible frames use the plain shader with **zero steady-state variant switches** and nine program binds per picture, matching baseline. Approximately **13.09 room atlas draws per picture** reuse validated tokens. This avoids repeated preparation, but world CPU p99 is **1.184167→1.255792 ms** and capture CPU p99 **0.084458→0.085167 ms** in this short pair; it does not prove a CPU-time improvement. World submission remains unresolved as an end-to-end performance target. Unsupported Mac GPU timers prevent distinguishing GPU execution from driver/backpressure costs.

The first implementation switched programs twice per frame; it was replaced with persistent selection after its delivery ABBA showed about −4% FPS. Its `room-final-*` and earlier `room-shader-*` observations are superseded, not combined with final results. Their favorable same-build observations are not claims about the shipped version. Final evidence uses only MVID `05d10db6-7342-42ea-ad0d-0407c7bbe2ea` except the explicitly mixed-build delivery group.

Validation: **467/462 pure PASS records** (defaults/forced legacy), **366/256 synthetic pixel checks**, **108 exact same-build native image/depth comparisons**, and **108 exact comparisons against the previous delivery**. Native workloads cover solo/two-player Ice Hive, High shadows, bombs, impacts and two combat snapshots. Desktop/server/Android arm64 builds pass with **31/23/64 warnings and zero errors**. Thirteen analyzer tests, the OpenGL-only contract and diff whitespace check pass. The twelve-second fullscreen/resize/hide/show cycle completes all four stages with zero recorded owned resources after cleanup; its two intentional transition stalls are excluded from performance evidence.

Evidence: [performance](room-persistent-results.json), [native visuals](room-persistent-visual.json), [previous-delivery visuals](room-persistent-upgrade-visual.json), [validated captures](room-persistent-validation.json), and [delivery manifest](room-persistent-delivery.json). Raw logs, invocations, hashes and isolated/superseded experiments are preserved under `artifacts/opengl-modernization/`. Use `short_checks.py --bundle room-shader`; the matching summarizer supports `--cross-abba` for the explicit reverse-order delivery repeat. Windows/Linux/Android hardware, sustained thermal behavior, exhaustive custom maps and full Studio/replay lifecycle acceptance remain open.

## Previous delivery: depth preservation, shader programs and upload preparation

Client MVID `7c2aef09-f76c-40f9-9df9-bfaf31fbe28c`, SHA-256 `67f15fb6bad6b68566aaee601b64861511f03fd30ce0e71cb20a77c4310b19b0`.

The final short combat controls use MP3 PROVING GROUND, eight players, 1920×1080, Original quality, shadows off, unlimited cap and VSync off on Apple M4 Pro / macOS 27.0 / GL 2.1 Metal 91.7. Each workload runs 720 simulation steps (12 simulated seconds); the first 240 steps are warm-up, leaving about **8.03 measured seconds**. Profiling and image readback are separate. No production sources were rebuilt during these controls.

| Comparison | Baseline FPS | Candidate FPS | Baseline aggregate p99 ms | Candidate aggregate p99 ms |
| --- | ---: | ---: | ---: | ---: |
| Same build A | 425.30 | 544.39 | 7.6891 | 4.6283 |
| Same build B, reverse order | 419.55 | 534.88 | 7.5055 | 4.4354 |
| Prior delivery → current, one short pair | 502.67 | 617.75 | 5.4623 | 4.2285 |

The repeated same-build means are **422.42→539.64 FPS (+27.75%)**, p95 **4.7827→3.6175 ms (−24.36%)**, and p99 **7.5973→4.53185 ms (−40.35%)**. The same-build bundle toggles room proof reuse, depth preservation and shader variants together; it does not isolate each feature. The intentional prior-delivery pair includes unconditional upload/preflight changes and reports **+22.89% FPS / −22.59% p99**. Absolute rates varied between groups; these are short descriptive observations, not confidence intervals or guaranteed device-wide gains. They show improvement over the delivered build in this workload, including frame-time tails, but do not prove universal frame-pacing or input-latency acceptance.

The separate profiled pair retains 12/15 complete windows after the harness warm-up marker and two additional excluded windows. Median window world CPU p99 is **1.395354→1.220000 ms (−12.57%)**, and depth-rebuild p99 **0.407979→0.267208 ms (−34.50%)**. Eligible replay savings average **164.25 submissions per picture** with **1.78 depth copies per picture**. Capture CPU p99 is **0.075813→0.078916 ms**; the small increase is preserved rather than claiming every stage improved. ROOM_CACHE remains enabled because it removes repeated metadata/proof work and the delivered bundle improves frame time. GPU timers are unsupported here, so no GPU-time attribution is made. All eight final performance runs have zero dropped simulation steps and zero stalls, with simulation near 60 Hz.

Final validation: **424/419 pure PASS records** and **347/237 synthetic pixel checks** with defaults/forced legacy. All **108 same-build** native image/depth comparisons and **108 against the preceding procedural build** are exact. The six workloads include one/two-player Ice Hive, High shadows, bombs, impacts and two eight-player combat snapshots. Four unmatched files from the prior longer combat capture are explicitly excluded. Capture-only stable HUD timing does not validate kill-feed fading/pruning. Desktop/server/Android arm64 builds have zero errors (31/23/64 warnings). The analyzer’s 13 tests and OpenGL-only contract pass.

A separate 12-second native window cycle completes fullscreen, normal resize, hide and show, with all recorded retained/HUD/shadow/depth-snapshot resources released to zero. Its two intentional transition stalls are not performance evidence. The initial eight-second cycle failed because it ended at simulation frame 480 before the final scheduled transition at frame 660; the unchanged binary passes with sufficient test duration. Existing ignored retired shader-tool build caches initially tripped the source-only contract; they were preserved under `artifacts/opengl-modernization/retired-shader-build-cache`, and the contract rerun passes.

Evidence: [performance](opengl-remainder-results.json), [same-build visuals](opengl-remainder-visual.json), [prior-build visuals](opengl-remainder-upgrade-visual.json), [native validation](opengl-remainder-validation.json), and [build/lifecycle delivery manifest](opengl-remainder-delivery.json). Raw commands, logs, binary hashes and MVIDs are preserved in those manifests. The reusable `tools/opengl-modernization/short_checks.py` supports short performance/profile/visual groups, explicit paths, resume controls and immutable evidence.

- `DEPTH_KEEP` preserves the first opaque pass’s depth when shader/geometry coverage is provably equivalent. When decals/translucent marking can change it, a scene-owned single-sample D24S8 snapshot copies **depth only** before those passes and restores it before translucent composition. Stencil winners and color are preserved. The snapshot is capped at 64 MiB, reused across frames, recreated on size changes, discarded on context generation changes, and disposed with the scene. Unsupported capabilities, allocation/FBO failure, unknown list effects, inherited coverage inputs, alpha overrides or cosmetics retain ordinary replay.
- Display-list metadata records final current color/normal/3D UV and inherited attribute reads. Nested dependencies invalidate on deletion/recompilation; unsupported state commands make the proof unavailable. It is capped at 16,384 entries. The skipped replay’s final item preparation and attribute writes are reproduced so later draws receive the same state. No visibility, simulation, camera, effect lifetime or draw ordering changes are made.
- `SHADER_VARIANTS` prewarms a bounded pair of programs: the canonical full material shader and a native shader with only the advanced companion-map lighting body removed. Native alpha, palette, cosmetics, fog and polygon behavior remain shared. Selection happens at frame boundaries; editor previews use the full program and restore the prior program/location bundle. Launcher previews select consistently. Failed variant compilation retains the full program. GLSL 1.20 and GLES 300 equivalents are preserved.
- Palette uploads now preserve exact independently resolved camera/projection/texture matrix cache entries. Unknown locations/counts, relinks and context changes retain conservative invalidation. Mutable arrays are still not identified by their address alone.
- `ROOM_CACHE` is enabled again under the user’s work-reduction preference. Current-frame visibility, poses and material state remain authoritative. Exact-ID negative batch preflights avoid repeating immutable compatibility failures; the cache holds at most 128 × 1,024 IDs (512 KiB payload), invalidates on geometry/context changes and never caches budget/driver failures. Promoted GPU-buffer bytes, retained CPU-array payload, combined bytes and negative-cache bytes are separately reported; opaque driver allocations remain unknown.

The new defaults are eighteen of twenty policies. `DEPTH_VERTEX` and `PROCEDURAL_STREAM` remain opt-in because prior measurements found mixed/slower behavior; they are not pending required implementations. Explicit per-feature `0`, force-legacy and capability/material fallbacks remain available. Implementation is shared across Windows/Linux/macOS and Android where applicable; retained desktop atlases/VAOs and their failure cache are desktop paths. Compilation does not establish device-specific FPS gains.

### Depth/stencil contract

| Pass | Existing depth/stencil behavior | Preservation rule |
| --- | --- | --- |
| Opaque | Alpha equals one; depth Less and writes enabled; stencil zeroed | Snapshot captures resulting D24S8 depth only after all opaque draws. |
| Decal | Polygon offset; depth Lequal and writes remain enabled | Any decal invalidates direct reuse; snapshot restoration leaves its color/stencil effects intact. |
| Mark translucent | Alpha less than one; color masked; depth writes and stencil Replace | Direct reuse requires every item provably rejected by alpha. Otherwise restore snapshot depth after marking. |
| Rebuild depth | Clear and replay opaque; stencil Keep | Proven equivalent snapshot replaces only the clear/draws. Final replay uniforms/current attributes are reproduced. |
| Translucent behind/front | Depth Lequal with writes disabled; stencil NotEqual/Equal | Original ordered passes and stencil winners are unchanged. |

### Plan disposition

| Original work | Disposition |
| --- | --- |
| 01–05 measurement/capabilities/VAOs/GLES streaming/uniform preparation | Implemented, including targeted array invalidation. Unsupported GPU timing and total driver residency remain explicitly unknown. |
| 06 room packets/spatial groups | Existing room-node visibility clusters, immutable templates, exact descriptors, sort reuse and execution proofs are joined by bounded compatible subsets, persistent ordered membership and validated atlas draw tokens. Broader spatial coalescing remains omitted: capture cost is small, no cheap complete material/pose revision exists, and larger groups can increase overdraw. |
| 07 atlas lifetime | Bounded LRU, context/list invalidation, negative preflight reuse and owned CPU/GPU payload telemetry implemented. Compact indices are optional and omitted. Full map/Studio stress acceptance remains open. |
| 08 depth replay | Guarded direct preservation and depth-only snapshot implemented with ordinary replay fallback. |
| 09 shadows | Depth-only target/fallback and guarded depth preparation implemented. Immutable room caster templates already exist; animated casters continue to redraw. Reduced PCF/filter quality and speculative shadow-map reuse are omitted to preserve visuals; performance presets have shadows off. |
| 10 shaders | Three prewarmed world programs implemented, including persistent neutral-cosmetics selection. Guarded depth shader retained. Additional lit/unlit/per-material variants and bandwidth changes remain unselected without a safe measured benefit. |
| 11 optional modern GL | UBO, persistent mapping, instancing, indirect and GPU occlusion are deliberately not selected. They are optional, require additional capability/architecture proof, and are not required to complete this implementation scope. |
| 12 rollout/QA | Short Mac A/B, synthetic/native parity and desktop/server/Android builds recorded below. Windows/Linux/Android real hardware, sustained thermal tests, input latency, exhaustive custom maps and full Studio/replay lifecycle remain unverified. The original plan is **not fully accepted** while these gaps remain. |

## Historical procedural world-submission changes

The current client is MVID `94b6074e-d0cb-457b-94af-0d14545b45b3`, SHA-256 `56b7de1721d56eeb4aa778f0e41d7174a571eaf4cdd7327ccb997c8dca0c3982`. Pure checks pass 348 default / 343 forced-legacy assertions and native synthetic checks pass 316 / 206 respectively. All 112 same-build game image/depth comparisons are exact. Desktop, server and Android arm64 builds pass with zero errors (31, 20 and 64 warnings). An additional 112 comparisons against the previous delivered build are exact. All fourteen final performance runs completed with zero simulation drops/stalls and recorded renderer resources released; performance limits are described below.

Eighteen policies are implemented; fifteen default enabled: `VAO`, `DYNAMIC_STREAM`, `UNIFORM_REVISIONS`, `STATIC_CACHE`, `SHADOW_FAST`, `HUD_SAMPLERS`, `ATLAS_EVICTION`, `HUD_DISCARD`, `DEPTH_SHADER`, `GLES_UNIFORMS`, `DEPTH_BATCH`, `HUD_GLYPHS`, `HUD_SPRITES`, `DEPTH_PREP`, and `PROCEDURAL_BATCH`. `ROOM_CACHE`, `DEPTH_VERTEX`, and `PROCEDURAL_STREAM` are opt-in after mixed or slower measurements. Explicit `0`, forced legacy, capability checks and material fallback remain available.

With `DEPTH_VERTEX` off, desktop and GLES compile stable legacy vertex sources with the exact original body, rather than retaining the specialization branch behind a false runtime uniform. Canonical fast sources remain available for opt-in and synthetic coverage, with both legacy and fast source synchronization checks.

`PROCEDURAL_BATCH` reuses preparation only across adjacent eligible packets with exact material/raster equality, including emissive intensity. Object-state reuse additionally requires zero palettes, unweighted geometry, no billboard/viewmodel, and identical pose, diffuse and both lights. Each item's stencil operation, geometry and submission order remain unchanged. TrailStack emits its ordered independent quads within one Begin/End. Unlit procedural draws omit unused light/material RGB uniform preparation while retaining vertex color, actual lighting enable, alpha and emissive-map intensity. Guarded alpha-only depth preparation now includes these four procedural types. Desktop direct immediate submission emits the same native primitives and UV/vertex sequence without per-vertex display-list capture checks, falling back during list compilation and for unsupported counts. Shared preparation and batching apply on desktop and Android; the VBO procedural stream is desktop-only and requires supported enabled VAOs.

`PROCEDURAL_STREAM` remains experimental and off by default. The pilot measured approximately 422→407 FPS without profiling and world CPU p99 1.465→1.548 ms with profiling; see `opengl-procedural-stream-pilot.json`. This is an unfavorable Mac observation, not evidence for enabling the stream. All hardware evidence remains Mac-only; Windows/Linux/Android runtime gains and full original-plan completion remain unverified.

Four profiling-disabled same-build combat runs (immediate/batched/batched/immediate, eight players, MP3 PROVING GROUND, 1920×1080, unlimited/VSync off, 40 seconds) average **431.21→433.64 FPS (+0.57%)**. Aggregate p95 averages **4.4534→4.4109 ms (−0.95%)**, but aggregate p99 averages **6.1570→6.2489 ms (+1.49%)**. This is a small observed throughput improvement with mixed tail behavior, not a robust general FPS or frame-pacing claim. See `opengl-procedural-same-results.json`.

The separate profiled ABBA pair averages **422.80→428.44 FPS (+1.33%)**, with aggregate p99 **6.3848→6.33365 ms (−0.80%)**. Median completed-window world CPU p99 decreases in both paired repeats: **1.4900→1.45146 ms** and **1.50485→1.44665 ms**, approximately **3.23%** across the repeated values. Rebuild-depth p99 falls **0.45608→0.43079 ms** and **0.44271→0.41692 ms**; mark-translucent falls **0.20179→0.18508 ms** and **0.19792→0.18704 ms**. Counters show roughly 233–240 guarded depth preparations and 208–221 unlit lighting preparations omitted per frame. These inclusive CPU scopes include driver time and are not additive GPU timing. Every same-build/profile run reports zero simulation drops/stalls. See `opengl-procedural-profile-results.json`; the intentional older-build delivery comparison follows.

Six profiling-disabled delivery comparisons run previous/regressed/current/current/regressed/previous. Repeated averages are:

| Delivered build | Average FPS | Aggregate p95 ms | Aggregate p99 ms |
| --- | ---: | ---: | ---: |
| Previous world (`55ee892c…`) | 423.04 | 4.39985 | 6.25975 |
| Last room/depth slice (`0d952d3c…`) | 425.28 | 4.43570 | 6.34250 |
| Current procedural (`94b6074e…`) | 429.53 | 4.47590 | 6.41760 |

Current throughput is **1.00% higher than the last delivered slice** and **1.53% above the preceding world build** in these repeats. Aggregate p99 is **1.18% / 2.52% higher**, respectively; p95 is also slightly higher. The earlier measured average-FPS loss is absent in this comparison, but a complete frame-pacing regression fix is **not established**. The old two-build throughput ordering also reversed, illustrating run variability. These are descriptive workload observations, not statistical confidence or isolated shader attribution. See `opengl-procedural-cross-results.json` for both repeats, actual flags, immutable binary hashes and MVIDs.

The enabled procedural bundle is retained for its repeated world CPU reduction and modest throughput improvement under the user's optimization priorities, with the unfavorable tail measurements explicitly preserved. Capture/metadata preparation was already much smaller than world submission; broad room caching did not address that cost. Per-item stencil, alpha, pose and material requirements still limit safe draw merging in combat. Unsupported GPU timers leave the GPU contribution unmeasured. Windows/Android share the applicable preparation changes (desktop direct immediate submission does not apply to GLES); hardware FPS, wider lifecycle checks and full plan completion remain unverified. `opengl-procedural-validation.json` links the final builds, checks, native scenarios and reports. Exploratory state-only and unlit-only pairs that failed to establish gains are preserved in `opengl-procedural-preparation-pilots.json`.

## Historical combat slice: room metadata/proof reuse and coherent depth vertex branch

That recorded source added independently controllable, default-enabled `ROOM_CACHE` and `DEPTH_VERTEX`, bringing the policy count to sixteen. Explicit environment `0`, forced legacy and existing capability/material fallback remain effective. The new client is MVID `0d952d3c-b569-4816-862c-86808bbfb828`, SHA-256 `2215b9b666743adc3258249aaaf1fabbb7ee84b724d029378bf9f39ce8f85c7a`.

`ROOM_CACHE` reuses scene-owned retained mesh descriptors by exact owner/key identity. An unused per-frame lookup map is also removed unconditionally; same-build flag comparisons therefore do not measure that removal. Current-frame packet materials, poses and visibility remain fresh. On desktop, a frame-scoped execution-batch proof plus exact IDs/prepared sequence hash avoids repeating compatible-batch preparation across applicable passes. Storage is bounded to 256 plans × 1,024 IDs, about 1 MiB of IDs; overflow follows ordinary preparation. GPU atlas reuse still validates exact list IDs and context ownership independently, so a hash never authorizes unrelated geometry. GLES shares descriptor metadata reuse but does not have the desktop combined atlas/proof execution path.

`DEPTH_VERTEX` is coherent uniform specialization inside the existing GLSL 1.20 / GLES 300 program, **not a separate-program variant**. Only the exact existing fragment depth/override/outline/cosmetic guard skips vertex lighting, transformed-normal and surface-position RGB work, setting unused varyings deterministically and alpha to one. Skinning, model/position transforms and UV generation remain unchanged; fallback runs the full original path. All six desktop/ES source synchronization hashes match. Native fixtures explicitly toggle the uniform and compare depth/alpha and fallback color, including UV modes, weighted skinning and billboards.

Current checks pass: synthetic 201 default / 199 legacy, pure 264 default / 259 legacy; desktop/server/Android compilation has zero errors with 31/20/52 warnings. All 112 same-binary image/depth comparisons are exact, and another 112 comparisons against the previous delivered build are exact. The six scenarios cover solo/two-player Ice Hive, shadows, bombs, impacts and eight-player combat. The native two-player outline-occlusion probe and bomb/impact probes pass; recorded renderer resources release to zero. Evidence is preserved in `opengl-combat-next-validation.json`, `opengl-combat-next-visual.json` and `opengl-combat-upgrade-visual.json`. Capture-only kill-feed stabilization does not validate wall-clock fade/prune timing. Twelve same-binary performance runs are complete in `opengl-combat-next-results.json`; earlier world/HUD tables below belong to their recorded MVIDs.

Combat controls use eight players in `MP3 PROVING GROUND`, 1920×1080, 40 seconds, unlimited cap and VSync off, alternating baseline/optimized/optimized/baseline. The vertex-only and room-only groups retain all other policies; their frame/world p99 values are medians of completed profile windows after the measurement marker and five excluded warm-up windows. Average FPS always comes from the full harness measurement interval.

| Isolated pair | Baseline FPS | Optimized FPS | Baseline window frame p99 ms | Optimized window frame p99 ms | Baseline world CPU p99 ms | Optimized world CPU p99 ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Depth vertex A | 439.31 | 431.05 | 4.42125 | 4.28550 | 1.48629 | 1.47329 |
| Depth vertex B | 424.64 | 427.06 | 4.32846 | 4.47658 | 1.49846 | 1.46454 |
| Room cache A | 429.97 | 423.70 | 4.35933 | 4.37500 | 1.48233 | 1.49654 |
| Room cache B | 427.46 | 426.85 | 4.24025 | 4.64067 | 1.46446 | 1.48804 |

Neither isolated switch establishes an FPS improvement. The depth vertex path slightly lowers measured world CPU p99, with mixed throughput/tails. Room caching removes about 45 dictionary lookups and 13 repeated compatibility/ID/hash preparations per presentation, but its capture CPU p99 (0.071–0.075 ms) does not improve on the off arm (0.070–0.073 ms). Eliminating work is not itself evidence of better FPS.

Separate profiling-disabled controls toggle both new policies together. They are effectively throughput-neutral: baseline A/B **425.59 / 427.81 FPS**, optimized **429.60 / 425.19 FPS**, or about **+0.16%** averaged across repeats. Aggregate frame p99 is **5.8642 / 5.8388 ms** baseline versus **6.0835 / 5.9433 ms** optimized (about **2.77% higher** on average). These short repeated workloads do not establish a meaningful throughput gain or universal non-regression; the unfavorable tail observation is preserved. Those policies were enabled in that historical source under the user's instruction, with independent off switches. These controls include the unconditional unused-map removal in both arms.

All twelve runs report zero dropped simulation steps/stalls, 59.84–60.45 Hz simulation and complete recorded renderer-resource cleanup. GPU timing and input latency remain unverified. The principal remaining measured cost is world submission (roughly 1.46–1.50 ms CPU p99), not metadata capture (roughly 0.07 ms): combat records about 475–498 executed immediate Begin/End submissions versus 56 retained indexed draws per presentation. Counts are not additive GPU timing and do not establish per-primitive cost. Procedural submission batching is a stronger next profiling target than broad spatial packet caching. The unchanged-state quad loop in `RenderTrailStack` is a specific candidate; it has not been changed by this slice.


A separate intentional cross-build, profiling-disabled ABBA control compares the previous delivered client (`55ee892c…`) against the current client (`0d952d3c…`) with every available policy enabled. This includes unconditional changes and shader-source/compiler effects that same-build switches cannot isolate. Previous A/B measures **425.10 / 430.99 FPS** and **5.9000 / 5.6306 ms** aggregate frame p99; current A/B measures **423.78 / 426.73 FPS** and **6.1811 / 5.8854 ms**. Repeated averages are **428.04→425.26 FPS (−0.65%)** and **5.7653→6.0333 ms p99 (+4.65%)**. Thus this slice does **not** establish a combat FPS gain, and the observed tail increase must not be presented as performance non-regression. Zero simulation drops/stalls and exact captured rendering parity are separate findings. `opengl-combat-cross-build-results.json` preserves both MVIDs, unchanged binary hashes, invocation manifests and aggregate measurements. No GPU/input-latency or other-device acceptance is inferred.

| Platform | Applicable paths | Acceptance limits |
| --- | --- | --- |
| Windows desktop | Verified core/ARB retained VAO, combined atlas/LRU and desktop batch proof, shared metadata/uniform/depth/HUD paths | Actual capabilities select fallback; hardware gains/parity for this slice remain unverified. |
| macOS GL2.1 | Advertised/entry-point-validated APPLE VAO, desktop atlas/proof and shared paths | Current native image/depth parity passes; performance results are recorded separately. |
| Linux desktop | Verified core/ARB retained VAO, desktop atlas/proof and shared paths | Actual driver/context support is required; runtime hardware acceptance remains unverified. |
| Android GLES3 | Shared room metadata, palette/depth/HUD policies; bounded adjacent dynamic streaming and GLES scalar/single-matrix caching | No desktop combined atlas/proof. Compilation is verified; native hardware gains/lifecycle parity remain unverified. |

## Preceding world slice: APPLE VAOs and guarded depth preparation

Mac GL2.1 now has an actual capability-selected retained VAO path. The context must advertise `GL_APPLE_vertex_array_object` and resolve all four APPLE creation/bind/delete/query functions; verified core/ARB support takes precedence. Compiled geometry records its dispatch API. One-time validation checks the fixed-function layout and buffer/pointer associations before retaining a VAO, with ordinary retained arrays as fallback. Caller VAO, buffers and client texture state are restored; inherited attributes, list invalidation and combined-atlas ownership remain intact.

The initial isolated VAO evidence is `docs/performance/opengl-world-results.json`, eight initial runs on MVID `94ca95d6-7ad3-40c1-a52c-a6d59d08b131`, with existing HUD caches and other policies matched. Average FPS is the harness post-startup aggregate. Frame/world p99 values are medians across completed 240-presentation windows emitted after the measurement marker and five discarded windows:

| Pair | Arrays average FPS | APPLE VAO average FPS | Arrays frame p99 ms | VAO frame p99 ms | Arrays world CPU p99 ms | VAO world CPU p99 ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Solo A | 769.57 | 999.24 | 3.34871 | 3.03975 | 0.79383 | 0.53731 |
| Solo B | 759.76 | 987.84 | 3.36025 | 3.04704 | 0.79533 | 0.54863 |
| Combat A | 402.32 | 412.74 | 4.77917 | 4.41125 | 1.53288 | 1.47996 |
| Combat B | 400.87 | 417.79 | 4.54804 | 4.51881 | 1.57825 | 1.47956 |

Solo executes 182 VAO draws and avoids 546 pointer calls per presentation; combat about 56 / 168. These are measured Mac workload gains, strongest in solo; unavailable GPU timers prevent GPU-execution claims. CPU scopes include driver call time and nested scopes must not be summed.

The newer `PROJECT_PRIME_GL_DEPTH_PREP` defaults enabled as the fourteenth independently controllable policy. It avoids RGB material/light-vector and companion-map preparation only when the guarded alpha-only depth path is eligible. It retains geometry transforms, skinning/billboard inputs, UV generation, material mode/alpha, base texture, raster state and original pass/draw order; unsupported/cosmetic/outline/procedural cases retain full preparation. Explicit `0` and forced legacy disable it independently.

The client binary used for the final world checks is MVID `55ee892c-1932-4822-8b50-8dadccaf8e3a`, SHA-256 `9fe0a5bc704422c32c1dd63a2ed88a4e114fbdb2d5331e2cf8b106e6bdab3795`. `docs/performance/opengl-world-visual.json` records all 112 PNG/depth comparisons identical across six native Mac scenarios. Synthetic checks pass 130 with defaults, 128 with VAO disabled and 128 with forced legacy; pure runs emit 225/220 PASS records for default/legacy. Desktop/server/Android builds have zero errors and 32/21/53 warnings respectively.

Four final-binary profiled ABBA combat runs isolate `DEPTH_PREP` with VAOs enabled in both arms. Baseline/optimized averages are 420.10→421.74 FPS and 411.76→418.68 FPS. Median window world CPU p99 falls 1.46108→1.45669 ms and 1.48504→1.46377 ms; depth rebuild falls 0.43860→0.43498 ms and 0.44325→0.43529 ms. About 88 item preparations per presentation use the coverage path. This is a modest CPU saving, not a robust standalone frame-tail win: median window frame p99 increases 4.47546→4.54387 ms and 4.48621→4.71162 ms, while aggregate harness frame p99 decreases 6.3675→6.0132 ms and 6.7537→6.4648 ms. Both statistics are retained rather than selecting the favorable definition. All four runs report zero dropped simulation steps/stalls and about 60 Hz simulation.

The world profile still places combat submission at about 1.46–1.49 ms p99, compared with roughly 0.08 ms for packet capture. The renderer still performs many animated/procedural submissions. A broad packet-cache rewrite is therefore not the strongest measured next target. Inclusive CPU scopes and independently aggregated p99 values must not be added; swap wall duration does not establish GPU execution time.

Eight final-binary profiling-disabled ABBA runs compare both world changes together against their independent off switches, with both HUD caches and all previous optimizations enabled in both arms. These runs use 1920×1080, unlimited cap, VSync off; solo is idle `MP9 CRYOCHASM` for 24 seconds and combat is eight players in `MP3 PROVING GROUND` for 40 seconds. The following are aggregate harness measurements, not medians of profile windows:

| Pair | Baseline FPS | Optimized FPS | Baseline frame p99 ms | Optimized frame p99 ms |
| --- | ---: | ---: | ---: | ---: |
| Solo A | 762.30 | 1014.02 | 3.3703 | 2.9629 |
| Solo B | 766.69 | 1011.39 | 3.3515 | 2.9535 |
| Combat A | 403.83 | 424.11 | 6.4177 | 6.1786 |
| Combat B | 400.94 | 420.13 | 6.9372 | 6.5815 |

Averaging the repeated per-run throughput values gives about **32.5% higher solo FPS** and **4.9% higher combat FPS**. These are descriptive repeated workload results, not a confidence interval or a claim for every map/device. All twenty world performance runs have zero harness dropped steps/stalls, roughly 60 Hz simulation (59.92–60.44 Hz), and successful retained/HUD/shadow cleanup. Initial profiled solo runs retain one to three zero-rate diagnostic windows before the two-second simulation-rate accumulator fills; the independent full harness interval is near 60 Hz. GPU execution timing and input latency remain unverified.

`docs/performance/opengl-world-results.json` preserves both build identities, complete timing definitions, raw paths/hashes, flags and resource observations. The initial isolated VAO group uses MVID `94ca95d6…`; the four depth and eight combined controls use final MVID `55ee892c…`. `docs/performance/opengl-world-validation.json` preserves build/test summaries and all twelve native fixture results. Combat visual captures use the existing capture-only kill-feed lifetime stabilizer; it is never active in performance runs.

A separate cap-144 pair is effectively pacing-bound: baseline/optimized averages are 139.25→140.51 FPS and aggregate frame p99 9.1926→9.2411 ms, with zero stalls/dropped steps. This single pair does not establish a cap-bound pacing or latency improvement. The separate 16-second fullscreen/windowed/hide/show check restores a visible, focused 1920×1080 window and releases all recorded retained/HUD/shadow resources. Its two stalls occur in the run containing intentional native transitions, as in the prior HUD lifecycle check; it is excluded from performance results and does not test full context recreation. Both checks are retained in the world validation JSON.

The original plan remains incomplete, including broader spatial packet caching and Windows/Linux/Android hardware plus wider lifecycle/scenario acceptance.

## HUD image-cache measured result (preceding world slice)

The preceding HUD slice evidence is `docs/performance/opengl-hud-image-results.json`: twelve runs share MVID `5ee6782d-32d2-45ef-ad46-446f0e80178d`. Existing optimizations stay enabled. Four profiled combat runs isolate `HUD_SPRITES` with glyph caching on; separate profiling-disabled solo/combat ABBA controls toggle both HUD image caches. All use the same Apple M4 Pro / macOS 27 / GL2.1 driver, actual 1920×1080 framebuffer and render target, unlimited cap and VSync off.

| Comparison | Mutable average FPS | Cached average FPS | Mutable frame p99 ms | Cached frame p99 ms |
| --- | ---: | ---: | ---: | ---: |
| Sprite-only combat A, profiled | 233.59 | 415.12 | 7.55742 | 4.22842 |
| Sprite-only combat B, profiled | 238.50 | 410.67 | 7.10038 | 4.48940 |
| Both caches combat A, profiling off | 93.34 | 421.57 | 18.45370 | 6.20780 |
| Both caches combat B, profiling off | 88.58 | 418.81 | 22.32810 | 5.96620 |
| Both caches solo A, profiling off | 234.34 | 784.45 | 8.10600 | 3.13570 |
| Both caches solo B, profiling off | 235.90 | 767.84 | 7.81420 | 3.10470 |

Average FPS always uses the harness aggregate post-startup interval. Profiled rows use median window p99 after the harness measurement marker plus five discarded completed windows; profiling-off rows use aggregate interval p99. These definitions are intentionally distinguished.

The sprite-only combat slice reduces HUD CPU wall p99 from 3.71917–3.75673 ms to 0.17825–0.18777 ms, and median texture uploads from about 1.95–2.83 per presentation to zero. World CPU p99 stays about 1.55–1.56 ms and now exceeds HUD cost. The principal measured blocker was synchronous CPU wall time in repeated tiny HUD image uploads; the driver's exact internal GPU-wait mechanism remains unproven. Independent no-profile controls confirm substantial actual Mac workload throughput gains, rather than profiler-only savings.

Both cache pools are bounded separately to 1,024 entries / 4 MiB texel-format bytes, with no eviction and ordinary mutable fallback when disabled/full. Combat owns 135 glyph images / 34,560 bytes plus 45 sprite images / 11,520 bytes; all twelve runs release glyph and sprite count/bytes to zero. Harness dropped steps/stalls are zero, simulation stays near 60 Hz, and observed window transitions occur only at startup. GPU timing remains unavailable, and these results do not establish other-device acceptance. Final same-client-binary visuals, one cap-144 pair and the separate window-cycle check completed as recorded next.

## Final native visual, capped and lifecycle evidence

`docs/performance/opengl-hud-image-visual.json` records **112 identical comparisons** on the same client MVID `5ee6782d-32d2-45ef-ad46-446f0e80178d`: 111 decoded PNG pairs and one shadow-depth capture, spanning solo, two-player, shadows, bomb visibility, impact presentation and 20-second combat with four snapshots. Meter coverage includes partial health/ammo observations (87 health / 7 ammo at frame 1, 99 / 8 at frame 3), rather than only full bars.

The original unstabilized manifest, `artifacts/opengl-modernization/hud-image-visual-comparison.json`, remains preserved: 109/112 comparisons were exact; three combat HUD differences were confined to kill-feed wall-clock fade/expiry around x=920…1265. World and meter images matched. The capture harness advances simulation faster than wall time, so the repeat used explicit capture-only `-stablehud` (requires `-shots`) to reset existing kill-feed timestamps into their opaque lifetime before each frame. Row content/order and the ten-row cap remain intact. This fixture tests rendering parity, **not kill-feed fade/prune lifetime timing**; no performance run uses it.

Only the harness was rebuilt for that fixture with `--no-dependencies --no-restore` (zero warnings, approximately 1.37 seconds). Client `ProjectPrime.dll` SHA-256 stayed `8da00397c0142c20f8fe3aca53e771c9b463a9bbe8cd4d4a3d43d93c89d3bf0b`, preserving measured client provenance.

`docs/performance/opengl-hud-image-lifecycle.json` records the separate cap-144 pair: mutable/cached aggregate average FPS 134.42→139.25 and frame p99 9.4819→8.9227 ms, with zero dropped steps/stalls. This is one descriptive pair, not repeated cap-bound acceptance or a latency measurement.

The separate 16-second four-stage fullscreen/windowed/hide/show cycle recovered a visible 1920×1080 framebuffer and released all retained, glyph and sprite resources to zero. It reported two stalls during the run containing intentional native transitions; this lifecycle run is not performance evidence. These completed checks cover the Mac workloads only; the original plan's other-device and broader lifecycle matrix remains incomplete.

## Implemented scope

| Area | Current implementation | Limits |
| --- | --- | --- |
| Measurement | Opt-in inclusive CPU pass p50/p95/p99, allocation statistics, real submission/upload/state counters, matched machine/build/workload metadata; extended GPU pass/status telemetry alongside the unchanged legacy world schema | GPU queries require actual capability and entry points; unsupported timing remains null/unverified. CPU scopes overlap and must not be summed. |
| Capability policy | Live-context version, exact extension-token and entry-point checks; independent feature selection; forced legacy precedence; reset on context lifecycle | macOS GL2.1 remains supported. Desktop ARB extensions do not imply GLES support. Eighteen optimization policies default enabled by user authorization; unsupported capabilities retain their fallback. |
| Desktop VAO experiment | Retained compatible geometry captures fixed-function layout once using verified core/ARB or APPLE dispatch, validates pointer/buffer associations at compilation, restores caller bindings and retains fallback | Requires verified VAO capability and enabled existing retained VBO path. Mixed/unsupported geometry remains on existing fallback. Mac GL2.1 now selects advertised APPLE VAO functions after one-time pointer/layout validation; unsupported contexts retain ordinary arrays. |
| GLES dynamic streaming | Adjacent same-state Begin/End submissions defer until a state/resource/uniform/pass barrier, with three rotated orphaned buffer stores and bounded reusable index scratch | 16,384 queued-vertex threshold; oversized primitive submits safely and releases oversized retained storage. Triangle/line barriers preserve order; inherited colors are resolved at each primitive End. Owned dynamic bytes/high-water are reported, not inferred driver residency. |
| GLES uniforms | Program-scoped scalar/vector/single-matrix elision before deferred barriers; typed array invalidation | Emulation-owned color/alpha uniforms always submit; program relink/delete/context reset invalidate caches. |
| Palette preparation/upload | Revision stamps at model palette writes, immutable render-item stamps, program/location/count/source/revision/scale upload identity, conservative ordinary-write/relink/context invalidation | Unknown revisions use ordinary uploads. Guarded DEPTH_PREP also skips RGB/companion maps/light-vector preparation only in eligible alpha-only passes; geometry/UV/alpha/raster ordering is preserved. No UBO is introduced. |
| Static material keys | Retained material/state-key reuse plus reusable opaque sort permutations proved by exact input count/StateKey/reorderability equality | Cached permutations reorder current-frame packet references, preserving current poses and visibility; key/barrier/count changes sort normally. Scene-owned descriptors and execution proofs additionally use ROOM_CACHE. Broader spatial merging is deliberately omitted as described above. |
| Shadow target experiment | Depth-only shadow FBO with a completeness check and existing color-backed fallback | Caster coverage/filtering remain unchanged; shaders now include a guarded depth-alpha branch with material fallback. Actual driver residency is not inferred from attachment format. |
| HUD sampler experiment | Exact four-scalar sampler-state suppression within a HUD scope after explicit texture/unit observations | Scope begins empty, unknown state submits normally, and ending/reset invalidates the cache. Native Mac HUD A/B is recorded below. |
| Combined atlas lifetime | Bounded 64 MiB LRU eviction of combined copies, with current-world-scope groups pinned and affected groups invalidated on underlying-list changes | Original promoted lists are never evicted by this policy; oversized or pinned-budget requests fall back, without forced GPU waits. Negative preflight proofs and separate owned GPU/CPU byte counters are included as described above. |
| Depth replay | Adjacent compatible retained indexed batches in the existing depth-rebuild pass; guarded alpha-only depth branch | Existing pass/stencil/alpha ordering remains intact; bounded material selection uses full-shader fallback for unsupported modes. The guarded snapshot/direct-preservation path described above replaces eligible replay; unsupported cases retain replay. |
| Shader programs | Two prewarmed world programs, full material and native lighting, selected at frame boundaries | Full fallback, per-program uniform/cosmetic locations, editor/preview restoration and GLES translation retained. |
| HUD zero alpha | Exact zero-alpha discard in HUD-only shader scopes | Fractional-alpha samples retain ordinary blending; this is not an approximate alpha threshold. |
| HUD glyph textures | Text-only immutable glyph texture cache keyed by exact decoded RGBA pixels and dimensions, capped at 1,024 entries / 4 MiB texel-format bytes | No eviction avoids dangling cached bindings; off/budget exhaustion uses the separate mutable texture fallback. Scene unload owns release. Warm hits still decode/hash; decoding CPU work is not eliminated. Isolated native/performance observations are recorded below. |
| HUD sprite textures | Separate exact-image cache for meter `BarInst`/`TankInst`, using the glyph strategy and its own 1,024-entry / 4 MiB format-byte pool | Default enabled under `HUD_SPRITES`; budget/off preserves the mutable fallback. Latest isolated and no-profile performance observations are recorded above; final Mac visual/cap/window-cycle checks completed as recorded above. |
| Analysis | Two-arm fail-closed evidence review with explicit warmup, four completed windows, same-build/machine/settings checks, numeric validation, independent visual evidence and GPU gates | Recommendations only; no automatic enablement. Bundled flag changes must be measured individually for isolated attribution. |

The existing VBO, binding cache and opaque batching remain their established features. These eighteen optimization policies are enabled by default as explicitly requested by the user. Set an individual environment variable to `0` to disable it, or `1` to enable it subject to capability and compatibility checks:

- `PROJECT_PRIME_GL_VAO=1`
- `PROJECT_PRIME_GL_DYNAMIC_STREAM=1`
- `PROJECT_PRIME_GL_UNIFORM_REVISIONS=1`
- `PROJECT_PRIME_GL_STATIC_CACHE=1`
- `PROJECT_PRIME_GL_SHADOW_FAST=1`
- `PROJECT_PRIME_GL_HUD_SAMPLERS=1`
- `PROJECT_PRIME_GL_ATLAS_EVICTION=1`
- `PROJECT_PRIME_GL_HUD_DISCARD=1`
- `PROJECT_PRIME_GL_DEPTH_SHADER=1`
- `PROJECT_PRIME_GL_GLES_UNIFORMS=1`
- `PROJECT_PRIME_GL_DEPTH_BATCH=1`
- `PROJECT_PRIME_GL_HUD_GLYPHS=1`
- `PROJECT_PRIME_GL_HUD_SPRITES=1`
- `PROJECT_PRIME_GL_DEPTH_PREP=1`
- `PROJECT_PRIME_GL_ROOM_CACHE=1`
- `PROJECT_PRIME_GL_PROCEDURAL_BATCH=1`
- `PROJECT_PRIME_GL_DEPTH_KEEP=1`
- `PROJECT_PRIME_GL_SHADER_VARIANTS=1`

`PROJECT_PRIME_GL_FORCE_LEGACY=1` or `-gllegacy` overrides the new experiments. Profiling uses `-glprofile`; GPU timing additionally uses `-glgpu` where supported. Existing legacy list/binding switches remain available for their respective features.

The user-authorized defaults supersede the plan's initial default-off rollout guidance. The analyzer remains deliberately strict for performance/acceptance claims, but its recommendations do not block activation or implementation. Capability and material fallbacks remain mandatory correctness behavior. Optional persistent mapping, UBOs, broader spatial merging and hardware occlusion are deliberately omitted for this implementation; the complete native device acceptance matrix remains open. See the plan disposition above.

## Earlier verification before the world and combat slices

The earlier Release desktop client, Android Debug, and server Release builds have zero errors, with 32, 53, and 21 warnings respectively. Default `-rendergraphcheck` produces 192 PASS lines; forced legacy produces 187, including 21 HUD cache assertions. Coverage includes capabilities, bounded statistics, palette/scalar caches, dynamic primitive/inherited-color checks, opaque permutation reuse and atlas LRU/pinning. `-graphicscheck` passes. Python suites pass: modernization analyzer 13 tests, Phase 4 analyzer 7, and native visual tooling 6.

Ordinary `RenderWindow` context lifecycle now resets the shared GL caches/policies, matching diagnostic context handling. On the earlier HUD binary, synthetic `-glvisualcheck` passed 19 pixel checks and produced five captures with VAO requested; the unsupported GL2.1 VAO path correctly fell back. This exercises fallback correctness and does not validate an accelerated VAO path on Windows/Linux.

The OpenGL-only source snapshot check passed. The local workspace script reported a false positive from an ignored empty `tools/modern-shaders` directory; this is recorded as a workspace check limitation rather than hidden as a clean local invocation.

Compilation is not real-device acceptance. macOS native rendering and comparisons were exercised as described below. Windows, Linux and Android runtime/rendering/performance acceptance remains unverified.

## Historical independent native visual evidence

Completed pairs are stored in `artifacts/opengl-modernization/solo-reference`, `solo-optimized`, `two-reference`, `two-optimized`, `shadow-reference`, and `shadow-optimized` artifacts, with associated logs, result JSON and captures. `artifacts/opengl-modernization/pixel-comparison.json` records exact decoded pixel equality for nine PNG pairs: world, repeat-world and HUD captures for solo, two-player and shadows workloads. The shadow pair also has identical 2048×2048 float32 depth captures: 16,777,216 bytes compared exactly. All ten PNG/depth comparisons were repeated successfully on final MVID `865494f7-d22e-4b89-b742-853b0b990e15`.

Both two-player logs report `NATIVERENDER outline occlusion PASS hidden=0 visible=2619`. These are native Mac workload observations, not acceptance for other GPUs or the full visual matrix. Performance captures remain separate from screenshot/depth readback runs.

The `SHADOW_FAST` depth-only target removes an unused 2048×2048 RGBA8 attachment when framebuffer completeness succeeds and falls back to the existing color-backed target otherwise. The native cleanup telemetry now reports `shadowColorBytesBefore:16777216` for the reference and `0` for the optimized shadow target. `retainedBytesAfter:0` is reported in all four two-player/shadow reference/optimized runs. These are allocated attachment-format bytes and renderer-owned retained counters, not measured driver residency. These historical captures used the then-current shader behavior. Current source preserves caster/culling coverage while adding the guarded depth-alpha branch.

## Enabled-default native visual evidence

The `enabled-{solo,two,shadow,bomb,impact}-{legacy,default}` artifact pairs completed on the Mac. `enabled-pixel-comparison.json` records all sixteen identical comparisons: fifteen decoded PNG pairs (world/repeat-world/HUD for each workload) plus the shadow float32 depth capture. These compare the implemented enabled defaults against forced-legacy behavior on the same captured binary. This native parity evidence is limited to these Mac workloads and capability fallback; it is not Windows/Linux/Android runtime acceptance.

First-wave enabled-default performance observations are recorded next; these predate the subsequent HUD glyph-cache slice. The subsequent historical isolated tables retain their original MVIDs and earlier policies; they are not current all-enabled benchmarks.

## Historical first-wave enabled-default observations (before glyph caching)

The authoritative source is `docs/performance/opengl-modernization-enabled-summary.json`, which includes exact run contexts, raw-log paths and SHA-256 hashes, per-pass CPU statistics, counters, and cleanup evidence. Eight runs share final binary MVID `4d880054-8e8a-4c9b-ad24-8b4dd68a54d1`. They compare the then-current eleven user-authorized default policies together against forced legacy, retaining existing VBO/binding/batching in both arms. On that historical binary macOS GL2.1 kept VAO unavailable, and desktop execution does not exercise the GLES streaming/uniform paths despite their policy flags. This is bundled comparison, not feature attribution or hardware acceptance.

All runs use Apple M4 Pro on macOS 27.0.0, GL `2.1 Metal - 91.7`, 1920×1080, scale 100, unlimited cap and VSync off. Solo/shadow use `MP9 CRYOCHASM` with one player; shadow uses High quality and solo shadows Off. Combat uses `MP3 PROVING GROUND`, eight players, shadows Off. Screenshot readback is absent. The first five completed windows are excluded explicitly; values are medians of retained 240-presentation-window statistics:

| Run | Windows | Frame p95 ms | Frame p99 ms | Render CPU p99 ms | Derived 1% low FPS |
| --- | ---: | ---: | ---: | ---: | ---: |
| solo-legacy-a | 25 | 4.85771 | 5.49929 | 4.10500 | 181.84 |
| solo-default-a | 25 | 4.97371 | 5.74329 | 3.99925 | 174.12 |
| solo-default-b | 24 | 4.93408 | 5.15115 | 3.96060 | 194.14 |
| solo-legacy-b | 15 | 8.99975 | 9.68913 | 7.10021 | 103.21 |
| shadow-legacy-a | 22 | 6.85621 | 7.70635 | 5.92529 | 129.78 |
| shadow-default-a | 23 | 6.74325 | 7.24212 | 5.49563 | 138.08 |
| combat-legacy-a | 9 | 13.95333 | 15.08038 | 14.30504 | 66.31 |
| combat-default-a | 10 | 13.58875 | 14.76813 | 13.81242 | 67.72 |

Solo pair A frame p99 worsens 4.44%, while B improves 46.84%. The legacy baselines themselves differ markedly (5.49929 versus 9.68913 ms), so there is no stable general FPS claim. World CPU p99 falls 14.55% and 17.80% in A/B, and depth-rebuild CPU p99 falls 28.09% and 29.38%. Solo indexed draws fall 213→182, matrix-array uploads 113→36, matrix-array bytes 81,472→9,792, and sampler writes 160→32, with 77 palette skips and 128 sampler skips. Median allocated bytes per presentation fall 12,560→12,304. These measurements demonstrate reduced submission work and particular CPU pass costs on the tested Mac workload.

The single High-shadow pair has 6.02% lower frame p99; the single combat pair has 2.07% lower frame p99. Each needs repeated alternating pairs before a stable workload gain can be claimed. Combat activity and dynamic submission counts vary. CPU HUD wall scopes include driver backpressure and do not establish a HUD GPU bottleneck.

Every retained window reports zero dropped simulation steps and zero stalls, with simulation near 60 Hz. GPU timing is unavailable on this Mac and remains `UNVERIFIED` in every run. Cleanup reports zero retained/combined bytes after unload; shadow color attachment bytes are 16,777,216 in legacy and zero in the default shadow arm, representing owned format allocation rather than driver residency. The strict single-flag analyzer cannot promote a multi-flag bundle or treat missing GPU evidence as a pass. Activation remains user-authorized, independent of these advisory claim gates.

The final-binary `rollout-visual-*` native rerun completed on the same MVID as these benchmarks. All 103 comparisons are identical: 102 decoded PNG pairs, including the detailed bomb/impact probe captures, plus the 2048×2048 float32 shadow depth capture. The five paired workloads are solo, two-player Spire/red outlines, High shadows, bomb visibility, and impact presentation. All ten native runs passed their checks and released retained allocations after unload. `docs/performance/opengl-modernization-enabled-visual.json` records the comparison manifest; raw captures and result JSON remain under `artifacts/opengl-modernization/rollout-visual-*`. Screenshot/depth readbacks were separate from every performance run. These are Mac observations, not acceptance of untested devices.

## Isolated HUD glyph-cache observations

`docs/performance/opengl-glyph-results.json` is the authoritative twelve-run source, including raw hashes, context, window/target diagnostics, cleanup, profiled window statistics and independent harness aggregate intervals. Only `HUD_GLYPHS` changes; all eleven preceding flags remain identical. The profiled runs share MVID `291e4eb8-facc-412c-93e3-f18a2e4b6d9d`. Runs use the same Mac/driver and uncapped/VSync-off 1920×1080 settings; actual framebuffer and render target sizes agree. Window state is Normal, with visibility/focus acquired at startup. Solo and combat repeat in mutable/cached/cached/mutable order. Profiling-disabled solo controls repeat that order separately.

Average FPS below is the harness's completed-frame aggregate over its post-startup interval. Profiled frame/HUD p99 values use records emitted after `NATIVERENDER measurement-start`, then exclude the first five post-marker windows (including the first potentially straddling window); CPU passes align by retained window IDs. They are medians of retained 240-presentation windows; these differ from pooled harness percentiles and must not be mixed:

| Run | Profiling | Aggregate average FPS | Window frame p99 ms | Window HUD wall p99 ms |
| --- | --- | ---: | ---: | ---: |
| glyph-solo-mutable-a | on | 200.25 | 5.48525 | 3.09240 |
| glyph-solo-cached-a | on | 772.37 | 3.36858 | 0.08600 |
| glyph-solo-cached-b | on | 761.50 | 3.40804 | 0.08652 |
| glyph-solo-mutable-b | on | 148.52 | 9.60558 | 5.79660 |
| glyph-combat-mutable-a | on | 90.35 | 15.25771 | 11.53404 |
| glyph-combat-cached-a | on | 219.42 | 8.18544 | 3.18677 |
| glyph-combat-cached-b | on | 210.00 | 8.27569 | 3.05379 |
| glyph-combat-mutable-b | on | 80.90 | 18.35844 | 14.00285 |
| glyph-solo-no-profile-mutable-a | off | 136.68 | — | — |
| glyph-solo-no-profile-cached-a | off | 756.76 | — | — |
| glyph-solo-no-profile-cached-b | off | 769.59 | — | — |
| glyph-solo-no-profile-mutable-b | off | 207.19 | — | — |

With profiling disabled, cached solo averages 756.76–769.59 FPS versus mutable 136.68–207.19 FPS. Profiled solo repeats show 761.50–772.37 versus 148.52–200.25 FPS; profiled combat shows 210.00–219.42 versus 80.90–90.35 FPS. These are actual isolated workload observations, not predictions or guarantees for other hardware/settings. The no-profile controls establish that the improvement is not merely avoiding profiler overhead.

The principal measured blocker was CPU wall time inside repeated small HUD texture uploads: mutable solo performs 16 uploads / 4,096 bytes per presentation, with TextureUpload p99 2.92356–5.59267 ms. Cached warmed solo performs zero texture uploads, 16 glyph hits per presentation, and HUD p99 falls from 3.09240–5.79660 ms to about 0.0860–0.0865 ms; world CPU remains about 0.8 ms. The evidence localizes expensive submission/API wall time; it does not prove the driver's precise internal GPU wait mechanism. Combat retains other mutable HUD image uploads, motivating the separate sprite-cache slice.

Cached solo owns 23 glyph textures / 5,888 format bytes; cached combat owns 135 / 34,560. Every reported glyph cleanup ends at zero count/bytes. Warmed hits still decode/hash the pixels; no decoding-elimination claim is made. GPU timing is still unavailable/unverified on this Mac.

All harness aggregates and marker-filtered retained windows report zero dropped steps/stalls. The initial cumulative seven drops in cached solo logs occur before the measurement marker (the marker follows frame window eight), and are not steady-state regression evidence. After marker-plus-five exclusion, cached A/B retain 59/58 windows starting at ID 14; mutable A/B retain 12/8 starting at ID 7. Combat retains 9/28/26/8 windows. The cached solo retained sets still contain two/one windows with `simulationHz:0` because the diagnostics reset needs a new two-second tally; the remaining simulation samples and harness aggregate are near 60 Hz. These zero diagnostic values are unavailable reporting intervals, not measured simulation stoppage, and still require explicit exclusion/annotation before strict analyzer acceptance. Aggregate throughput is unchanged by this corrected profiler filtering.

`HUD_SPRITES` extends immutable exact-image storage to meter bars/tanks in a separate 1,024-entry / 4 MiB pool. Its isolated final-binary performance evidence is recorded at the top of this report; the older glyph-only results remain attributed to glyph caching.

## Original plan completion status

Implementation and device acceptance are separate. The user's later instructions replace the original default-off/performance-threshold gates: safe reductions in work can remain enabled with flat FPS. The optional modern-GL rewrite is not a required deliverable. This table supersedes the older partial-status descriptions in historical results.

| Plan PR | Implementation disposition | Remaining evidence or deliberate boundary |
| --- | --- | --- |
| 01 full-frame telemetry | Implemented: CPU passes, submission/upload counters, allocation and asynchronous GPU capability/status reporting | This Mac cannot supply GPU timer results; direct GPU bandwidth/overdraw and other-device attribution remain unavailable. |
| 02 capability policy | Implemented: explicit overrides, supported entry points, context isolation and fallback | Hardware coverage is separate from compilation. |
| 03 desktop VAO | Implemented: core/ARB and APPLE paths, retained arrays fallback | Mac native validation exists; Windows/Linux hardware validation remains. |
| 04 GLES dynamic stream | Implemented: bounded adjacent batching and rotated orphaned stores | Android ARM64 compiles; sustained device and lifecycle performance remains unverified. |
| 05 uniform preparation | Implemented: palette revisions, scalar caches, depth/procedural preparation and adjacent mesh uniform reuse | Exact current pose, lighting, palette and coverage guards remain; mesh current color is always restored. |
| 06 room packet cache | Implemented: retained templates, current visibility clusters, material ordering, bounded compatible room groups, exact material identities, membership/draw tokens and compact packets | Incompatible poses, ordering barriers and unknown geometry keep ordinary submission. Cross-pose geometry baking/indirect drawing is a separate architecture change, not forced to enlarge batches. |
| 07 atlas lifetime | Implemented: bounded atlas/LRU/invalidation, resource counters and explicit allocation-failure fixtures | Process RSS is not driver residency; real device context-loss coverage remains. |
| 08 depth replay | Implemented: exact coverage/attribute authority, depth-only D24S8 preservation, ordinary replay fallback | Unknown coverage and unsupported targets retain replay; no stencil/color restoration shortcut. |
| 09 shadow cost | Implemented: depth target fallback, independent light-space casters, static preparation reuse and conservative spatial groups | Low/High/Ultra comparisons are in the latest completion report; no quality reduction or stale shadow-map reuse. |
| 10 shader variants | Implemented: prewarmed full/native/plain/plain-unlit programs with whole-frame selection and fallback | Additional per-item lit/outline/viewmodel programs are deliberately omitted: they add switches to paths already covered by the selected program. Other-driver validation remains. |
| 11 optional newer GL | Deliberately skipped | Persistent mapping, UBOs, indirect drawing and a core-context rewrite lack demonstrated need on this compatibility renderer. |
| 12 rollout and testing | Implemented local test tooling and default policy; Mac acceptance recorded per build | Cross-platform hardware, exhaustive custom maps, full Studio UI interaction and long thermal/input-latency acceptance remain outside the available local evidence. |

The new HUD glyph-cache slice addresses a specific candidate bottleneck observed in code: overwriting mutable text textures can force synchronous driver/GPU backpressure before HUD submission completes. Long HUD wall scopes are compatible with that hypothesis but do not prove it. The cache is text-instance-only; nontext and budget/off fallback paths preserve mutable behavior. Its exact pixel/dimension identity prevents content aliasing, bounds format storage, and releases scene-owned textures on unload. Warmed hits still perform decoding/hash work. The isolated glyph observations below show a measured FPS improvement and successful release counters on this Mac; warmed decoding remains. The newer sprite performance observations are recorded above; final Mac visual, capped and window-cycle checks completed above; broader device acceptance remains outstanding.

## Historical preliminary same-build macOS run

Source artifacts: `artifacts/opengl-modernization/baseline-solo.log`, `baseline-solo.json`, `candidate-solo.log`, and `candidate-solo.json`. These local benchmark artifacts may be ignored by Git and must be preserved separately when sharing this report.

Both approximately 24-second captures used binary MVID `f821f65d-0971-40b9-8c3d-0001d1ed6814`, machine `Jarretts-MacBook-Pro`, macOS 27.0.0, Apple M4 Pro, OpenGL `2.1 Metal - 91.7`, `MP9 CRYOCHASM`, one idle player, 1920×1080, shadows off, render scale 100, unlimited cap (`-1`), VSync off. Existing VBO, binding cache and batching were enabled in both. The candidate enabled `uniformRevisions` and `staticCache`; VAO stayed disabled by capability policy.

The following are medians across the final 14 completed 240-presentation windows in each run, after excluding the first five windows explicitly for startup/warmup. They are descriptive preliminary observations, not a promotion report:

| Metric | Baseline | Candidate |
| --- | ---: | ---: |
| Frame interval p95 | 4.9519 ms | 5.0654 ms |
| Frame interval p99 | 5.5100 ms | 5.8262 ms |
| Render CPU p99 | 4.0577 ms | 4.0741 ms |
| Interval-derived 1% low estimate | 181.4933 FPS | 171.7138 FPS |
| Measured simulation rate | 59.8906 Hz | 59.9501 Hz |

The preliminary candidate does not show a frame p99 win; tails are noisy and isolated alternating runs are required before attributing any effect. Both new flags changed together, so this pair is not an isolated single-experiment analyzer input. Mac GPU timer entry points were unavailable: GPU timings are `UNVERIFIED`. Independent solo/two-player/shadow visual pairs have since completed as recorded above; repeat performance measurements have completed and are recorded below. No screenshot readback is included in the performance comparison.

Final delivered desktop binary `6801f915-cbf8-403c-8f70-403a556b2b49` was rebuilt after the counter instrumentation repair. Its two-player Spire/red-outline run requested all desktop experiments (GLES scratch is platform-specific; VAO fell back on this Mac). A same-binary forced-legacy run overrode every requested new switch; all three world/repeat/HUD PNG pairs were pixel-identical, and both outline occlusion checks passed. `delivery-same-build-parity.json` records this comparison. Retained allocations returned to zero after cleanup. The final active-path sample reports nonzero immediate submissions and 15 merged draws, verifying the repaired counters. These capture runs are visual/lifecycle checks, not additional performance evidence.

## Historical isolated performance measurements

Seven final nominal 24-second runs completed under `artifacts/opengl-modernization/final-*`: five solo runs in order base A, uniform A, static, uniform B, base B, followed by combat baseline/uniform. Harness measurement durations are approximately 20.03 seconds after its separate startup period; the log profiler records completed presentation windows independently. All use MVID `865494f7-d22e-4b89-b742-853b0b990e15`, the same Mac/driver/resolution/scale/unlimited cap/VSync-off/shadows-off settings described above. Solo is `MP9 CRYOCHASM` one idle player; combat is `MP3 PROVING GROUND` eight players. Existing VBO/binding/batching are on; VAO and shadowFast are off. Only `uniformRevisions` or `staticCache` changes in each selected pair.

The first five completed windows are explicitly excluded in every run. Each row below is the median of retained 240-presentation window statistics, not a pooled percentile:

| Run | Retained windows | Frame p95 ms | Frame p99 ms | Render CPU p99 ms | Derived 1% low FPS |
| --- | ---: | ---: | ---: | ---: | ---: |
| Solo base A | 9 | 9.05346 | 9.61121 | 6.62533 | 104.04519 |
| Solo uniform A | 14 | 4.94550 | 5.25823 | 3.95621 | 190.17907 |
| Solo static | 10 | 8.54842 | 9.06550 | 6.47610 | 110.35613 |
| Solo uniform B | 12 | 7.17269 | 8.22500 | 6.27890 | 122.29730 |
| Solo base B | 14 | 4.93369 | 5.27052 | 3.94129 | 189.73459 |
| Combat base | 3 | 15.05833 | 16.92925 | 14.99163 | 59.06936 |
| Combat uniform | 3 | 14.89696 | 16.45775 | 14.30896 | 60.76165 |

The solo uniform experiment removes redundant upload work consistently: median uploads per presentation fall from 113 to 36, matrix-array bytes from 81,472 to 9,792, and 77 palette uploads are skipped. Indexed draws (213), display-list calls (112) and median allocation p50 (12,560 bytes/presentation) are unchanged. World CPU p99 is 0.96867→0.82554 ms for A and 0.88379→0.86938 ms for B. These are measured submission reductions, with inconsistent end-to-end frame tails.

Combat median uploads fall from 139.78 to 45.40 per presentation, array bytes from 120,795.73 to 24,795.73, with 93.55 skips. Allocation p50 remains 34,816 bytes/presentation; world CPU p99 is 3.06550→3.05492 ms. Combat dynamic draws and scene activity vary, and only three windows remain after warmup, so this workload is insufficient for the four-window acceptance gate. Its descriptive metrics must not be treated as a passing benchmark.

Static-cache solo counters remain 113 uploads, 81,472 array bytes, 213 indexed draws and 112 list calls per presentation; median allocation p50 remains 12,560 bytes. CaptureWorld CPU p99 is 0.04992 ms versus 0.05021 ms for the nearby base B and 0.05479 ms for base A. Base A and base B themselves differ substantially, demonstrating order/thermal/pacing variation; static frame tails do not demonstrate a stable win.

The strict analyzer was invoked with `--warmup-windows 5` and the appropriate `--experiment-flag uniformRevisions` / `staticCache`. Reports are `comparison-final-solo-uniform-a.json`, `comparison-final-solo-uniform-b.json`, `comparison-final-solo-static.json`, and `comparison-final-combat-uniform.json` in the same artifact directory. A's frame-p99 reduction is 45.29%, while B regresses by 56.06%; static against nearby base B regresses by 72.00%. These conflicting observations do not support a broad FPS improvement claim. All solo reports recommend **keep experimental** with GPU `UNVERIFIED`; combat is **invalid evidence** for insufficient retained windows. The analyzer invocations omit a reviewer-attested visual manifest, so their visual gate is also false; the separately recorded native pixel comparisons above remain distinct evidence.

The query-window parser now counts unnumbered legacy/extended GPU records independently per pass, ignoring zero-sample support messages, and requires every reported pass to cover all retained frame windows. An added interleaving/warmup/partial-pass regression test passes. Unsupported Mac GPU timing continues to prevent any successful GPU gate.

## Historical HUD sampler isolated measurements

Four nominal 36-second runs completed in A/B/B/A order, stored as `hud-base-a`, `hud-candidate-a`, `hud-candidate-b`, and `hud-base-b` JSON/log pairs. They use same-build MVID `4169ac7f-6d05-4bf7-a945-c86e5a7d6f3f` with the same Mac, room, one idle player, 1920×1080, shadows-off, render-scale-100, unlimited/VSync-off settings. Only `hudSamplers` differs. These captures belong to this binary; later counter instrumentation edits/rebuilds have a different MVID and do not retroactively change the captured observations.

Again, the first five completed 240-frame windows are excluded explicitly, with medians computed across retained window statistics:

| Run | Retained windows | Frame p95 ms | Frame p99 ms | Render CPU p99 ms | Derived 1% low FPS |
| --- | ---: | ---: | ---: | ---: | ---: |
| HUD base A | 17 | 9.00467 | 9.61112 | 6.59971 | 104.04609 |
| HUD candidate A | 15 | 9.03629 | 9.64354 | 6.63479 | 103.69634 |
| HUD candidate B | 19 | 6.67192 | 7.42792 | 6.41746 | 134.62726 |
| HUD base B | 23 | 4.92537 | 5.59171 | 4.06458 | 178.83623 |

Median sampler writes fall from 160 to 32 per presentation, with 128 exact redundant parameter submissions skipped. State-skip counts increase from 181 to 309. Indexed draws (213), display-list calls (112), matrix-array uploads (113), and matrix-array bytes (81,472) remain unchanged. This proves avoided sampler submissions on the tested workload; it does not establish a frame-time gain.

The A pair frame p99 regresses by 0.34%; B regresses by 32.84%, with substantial baseline variation. `comparison-hud-a.json` and `comparison-hud-b.json`, generated with `--warmup-windows 5 --experiment-flag hudSamplers`, both recommend **keep experimental**. GPU remains `UNVERIFIED`; no reviewer-attested visual manifest was supplied to those analyzer invocations. Independently, `hud-pixel-comparison.json` verifies three pixel-identical native world/repeat-world/HUD PNG pairs between `hud-reference` and `hud-optimized` on the Mac.

A long CPU HUD scope measures wall time inside submission, including synchronous driver backpressure and possible queued GPU work. It does not identify a pure CPU preparation cost or establish a HUD GPU bottleneck without valid GPU timers. The optional `Ui` scope is absent in these harness windows and correctly reports zero samples/null timings.

Counter semantics for the final instrumentation are explicit: `ImmediateDraws` counts executed facade Begin/End submissions, not primitives stored inside native display lists; `DisplayListCalls` counts list executions. `MergedDraws` is a subset of `IndexedDraws`, so these counters must not be added. GLES compiled-list indexed execution is now instrumented at the actual draw call. The older captured MVIDs' zero ImmediateDraws/MergedDraws fields cannot be retrospectively interpreted as proof that no such work occurred; final source instrumentation changes are distinct from measured binaries.

## Reproduction

Use legally extracted game assets and the configured `PROJECT_PRIME_USER_DATA`/`paths.txt` as documented by `tools/native-render-check/README.md`. Build once, then use the same binary for both arms. These commands isolate palette revisions against the other current defaults held identical; explicitly set the other twelve flags to `0` in both environments to reproduce the historical isolated baseline. The following isolates palette revisions without screenshot readback:

```sh
PROJECT_PRIME_GL_UNIFORM_REVISIONS=0 dotnet tools/native-render-check/bin/Release/net10.0/native-render-check.dll \
  -room 'MP9 CRYOCHASM' -players 1 -idle -seconds 24 -hz -1 -size 1920x1080 \
  -glprofile -glgpu -output /tmp/prime-baseline.json > /tmp/prime-baseline.log
PROJECT_PRIME_GL_UNIFORM_REVISIONS=1 dotnet tools/native-render-check/bin/Release/net10.0/native-render-check.dll \
  -room 'MP9 CRYOCHASM' -players 1 -idle -seconds 24 -hz -1 -size 1920x1080 \
  -glprofile -glgpu -output /tmp/prime-candidate.json > /tmp/prime-candidate.log
python3 tools/opengl-modernization/analyze.py \
  --baseline /tmp/prime-baseline.log --candidate /tmp/prime-candidate.log \
  --warmup-windows 5 --experiment-flag uniformRevisions --output /tmp/prime-comparison.json
```

Keep the other optimization flags explicitly disabled or identically configured in both commands. Repeat in alternating order; extend combat duration until at least four windows remain after the explicit warmup count. Missing GPU timing keeps the result unverified. Separately collect native visuals; for example:

```sh
PROJECT_PRIME_GL_UNIFORM_REVISIONS=1 dotnet tools/native-render-check/bin/Release/net10.0/native-render-check.dll \
  -room 'MP9 CRYOCHASM' -players 2 -hunter Spire -idle -seconds 8 -hz -1 \
  -outlines Red -outlinecheck -size 1920x1080 \
  -shots /tmp/prime-two-shots -output /tmp/prime-two-visual.json
```

Collect the corresponding flag-disabled visual arm and review exact captures before providing the analyzer's independent visual manifest. HUD sampler A/B has completed as recorded below; this experiment has no demonstrated frame-time improvement.

## Rollout decision and outstanding acceptance

The current fifteen enabled defaults and three opt-in policies are listed above, with explicit `0`, force-legacy and capability/material escape paths. Evidence gates remain advisory for activation and strict for measured performance or platform-acceptance claims. The first-wave bundle reduced submission work without a stable general frame-time gain; the subsequent isolated glyph-cache slice demonstrates a large workload-specific Mac FPS improvement. Sprite measurements also demonstrate a tested Mac gain; Mac HUD visual/capped/window-cycle checks completed; current world depth/combined performance and other-device/broader lifecycle acceptance remain outstanding. Preserve matched native profiling, independent visuals and device-specific validation while continuing implementation.

Prior Phase 4 acceptance gaps remain: inconsistent Mac FPS benefit, unresolved native lit-surface differences and incomplete Windows hardware acceptance. This implementation does not silently resolve them. In particular, validate actual multiplayer `MP9 CRYOCHASM` one- and two-player wall/lighting/outlines rather than substituting the adventure room.
