# OpenGL modernization evidence review

This two-arm analyzer complements the fixed four-arm Phase 4 analyzer. It produces a review recommendation and never enables renderer features.

```sh
python3 tools/opengl-modernization/analyze.py \
  --baseline baseline.json --candidate candidate.log \
  --warmup-windows 1 --experiment-flag vao \
  --visual-evidence visual.json --output comparison.json
python3 -m unittest discover -s tools/opengl-modernization -v
```

Inputs are raw logs or native harness JSON containing `stdout` as a string or array of lines. The analyzer reads schema 1 `[glframe-json]`, `[glpasses-json]`, and `[glgpu-json]` and `[glgpupass-json]` lines. It excludes the first explicitly requested number of completed frame windows and any `warmup: true` windows. Four complete 240-presentation frame windows must remain. New records should share integer `window` identifiers. Legacy records without identifiers align by occurrence within their category (GPU occurrences are counted independently per pass, ignoring zero-sample status lines); the legacy world GPU schema remains supported.

Every retained frame must have a context containing `assemblyMvid`, `machine` (or `machineIdentity`), `renderer`, `glVersion`, `room`, `playerCount`, `width`, `height`, `shadows`, `renderScale`, `cap`, `vsync`, and an explicit `flags` dictionary. Top-level `flags` is also accepted. These must match throughout and between runs. `--experiment-flag NAME` declares the single dictionary key allowed to differ; all other flags remain matched. Old captures lacking this identity metadata are invalid evidence and must be recaptured.

CPU pass records contain `samples:240`, a `cpu` dictionary mapping pass names to `{samples,p50Ms,p95Ms,p99Ms}`, optional allocation statistics and numeric counters. Zero-sample optional scopes (for example disabled shadow collection or absent UI) are unavailable and may have null timings; they do not fail eligibility.  Sampled pass timings must be finite and nonnegative. Reports preserve the pass statistics for inspection. Complete CPU pass windows are required for promotion.

GPU records preserve legacy `[glgpu-json]` `worldGpuP99Ms` and prefer extended `[glgpupass-json]` `{pass,p50Ms,p95Ms,p99Ms,supported,droppedQueries}`. Pass names match without case sensitivity. Unsupported status records require no sample fields and mark the capture unverified. Each reported GPU pass must cover every retained frame window and needs at least four windows, positive finite p99 values, support, and zero dropped queries. Missing, unsupported, partial or mismatched GPU evidence is `UNVERIFIED`, never a successful GPU gate. Sequential individual pass durations must not be summed as an inferred full-frame duration.

The promotion gates follow the modernization plan: at least 3% reduction in median window frame p99, frame p95 no worse than 5%, interval-derived 1% low no worse than 2%, and every matched GPU pass p99 no worse than 3%. Only uncapped (`cap:-1`; `cap:0` selects the display refresh ceiling) and VSync-off runs qualify for automatic promotion recommendations. Cap-bound headroom remains an experiment requiring separate review. Simulation must remain within 1.5 Hz of 60 with no reported dropped steps or stalls. Nonfinite, negative, boolean or missing required metrics and mismatched contexts invalidate the comparison. Four windows provide eligibility guidance, not a statistical confidence interval; repeated alternating runs and platform-specific hardware acceptance are still required.

Visual evidence is collected in separate runs so screenshot readback cannot contaminate performance. The manifest is an explicit reviewer attestation, not automated pixel proof:

```json
{
  "source": "native-game-on-device",
  "passed": true,
  "separateCapture": true,
  "captures": ["ice-hive-solo/manifest.json", "ice-hive-two-player/manifest.json"]
}
```

Reviewers must inspect the referenced native captures for all affected paths, including MP9 CRYOCHASM one- and two-player wall/lighting/outline behavior, glass/stencil, HUD, and Studio as relevant. Synthetic fixtures alone cannot satisfy this manifest. Missing visual evidence keeps the recommendation experimental. Existing Phase 4 native visual and Windows acceptance gaps remain unresolved by this tool.

Exit 1 indicates invalid evidence; exit 0 indicates a valid report, which may still recommend `keep experimental`. Consumers must inspect `decision`, `gpuStatus`, and each gate. Output always sets `automaticActivation:false`.

Counter semantics: `MergedDraws` is a subset of `IndexedDraws`; `ImmediateDraws` counts executed facade Begin/End, not native display-list contents. GLES compiled-list actual indexed draws are instrumented in final source. Captured binary MVIDs identify the instrumentation that produced each log. CPU HUD wall scopes include driver backpressure and do not establish a GPU bottleneck without valid timers.

The GPU pass selector is the telemetry `pass` field (for example `World`, `Shadow`, `Outlines`, `SceneOverlays`, `PostProcess`, `Composite`); names are case-insensitive in comparisons. CPU scope keys are preserved verbatim and their inclusive timings must not be added together. The analyzer compares every reported GPU pass rather than accepting a cherry-picked subset.

## Implemented optimizations and authorized defaults

Twenty-two policies are implemented; twenty default enabled. `DEPTH_VERTEX` and `PROCEDURAL_STREAM` remain opt-in after mixed or slower measurements. Enabled additions include `ROOM_CACHE`, `DEPTH_KEEP`, `SHADER_VARIANTS`, `ROOM_GROUPS` and `PLAIN_SHADER`. Each uses the `PROJECT_PRIME_GL_` prefix. Set a flag to `0` for independent fallback; `PROJECT_PRIME_GL_FORCE_LEGACY=1` / `-gllegacy` overrides them. Room atlas groups require the desktop retained-batching backend; GLES keeps its existing shared-state runs. Shader specialization has desktop GLSL and GLES sources, with the ordinary program used for active cosmetics, advanced materials, shadows and paused scenes. Eligible frames retain the specialized program across frames, with selection after item collection and before world setup. Current evidence is in `docs/performance/opengl-modernization-results.md`; historical observations below retain their original feature sets.

The GLES path defers adjacent same-state primitives through comprehensive state/resource/uniform/pass barriers, preserves triangle/line ordering and end-of-primitive inherited color, uses three orphaned buffer regions, and bounds queued vertices at 16,384 with oversized fallback. Actual renderer-owned bytes/high-water are reported. Program-scoped scalar/single-matrix cache skips exact redundant uniform writes before introducing barriers; emulation-owned uniforms and arrays retain their explicit fallback/invalidation behavior.

Static caching reuses material keys and exact-proved opaque permutations while using current-frame packets, poses and visibility. The 64 MiB combined-atlas LRU pins current-scope groups and never evicts underlying lists. Depth-rebuild batching preserves existing passes; the alpha-only depth branch has bounded material fallback. Shadow depth-only FBO completeness retains the color-backed fallback. HUD sampler suppression and exact-zero-alpha discard remain scoped to their applicable paths.

Native Mac enabled/legacy solo, two-player, shadow, bomb and impact captures produced 103 identical PNG/depth comparisons on the final benchmark binary, recorded in `docs/performance/opengl-modernization-enabled-visual.json`. Eight same-build performance runs are summarized in `docs/performance/opengl-modernization-enabled-summary.json`: submission work and world/depth CPU costs fell, while variable solo frame tails prevent a broad FPS claim. Other hardware remains unverified. Historical tables identify their captured binary MVIDs. See `docs/performance/opengl-modernization-results.md` for details and practical commands. Isolate one declared flag at a time for this analyzer's evidence review; candidate bundles remain descriptive exploration.

`HUD_GLYPHS` caches only text-instance glyph textures by exact decoded RGBA pixels and dimensions, up to 1,024 entries / 4 MiB texel-format bytes. It does not evict bindings; budget/off fallback uses separate mutable storage, and scene unload owns release. Warm hits still decode/hash. Isolated twelve-run glyph evidence shows large Mac FPS gains, including profiling-disabled controls. Repeated HUD upload CPU wall time is the measured blocker; the exact internal GPU wait remains unproven.

`HUD_SPRITES` defaults enabled and applies the same exact-image strategy to meter `BarInst`/`TankInst` in a separate 1,024-entry / 4 MiB pool; final builds and isolated runtime performance measurements passed on the tested Mac; final Mac visual/capped/window-cycle checks completed. See `docs/performance/opengl-glyph-results.json` and the results document for actual glyph observations and diagnostic limitations.

Latest evidence: `docs/performance/opengl-hud-image-results.json` contains twelve same-MVID runs. Profiling-off both-cache combat averages 418.81–421.57 FPS versus 88.58–93.34; solo averages 767.84–784.45 versus 234.34–235.90. Sprite-only ABBA reduces HUD CPU wall p99 to 0.178–0.188 ms with zero median texture uploads. GPU execution timing remains unverified; these are measured Mac workload improvements, not cross-device guarantees.

Final visual manifest `docs/performance/opengl-hud-image-visual.json` records 112 identical PNG/depth comparisons. The original 109/112 manifest remains preserved; three combat kill-feed wall-clock differences were resolved for pixel capture using harness-only `-stablehud` with `-shots`, which preserves existing row content/order but does not validate fade/prune lifetime timing. Performance never uses that fixture. A separate cap-144 pair and four-stage native window cycle completed; the cycle has two intentional transition stalls and is not performance evidence. See `docs/performance/opengl-hud-image-lifecycle.json`.

The desktop VAO selector now uses verified core/ARB or advertised APPLE GL2.1 functions, with one-time layout/buffer-pointer validation when geometry is created and ordinary array fallback. Initial isolated Mac VAO results are in `docs/performance/opengl-world-results.json`; final native world parity is 112 identical PNG/depth comparisons on client MVID `55ee892c-1932-4822-8b50-8dadccaf8e3a`. `DEPTH_PREP` skips RGB/companion/light-vector preparation only within eligible guarded alpha-only depth passes, preserving geometry/UV/alpha/raster/order and full fallback. Final profiling-disabled combined repeats improve solo throughput about 32.5% (762–767→1011–1014 FPS) and combat about 4.9% (401–404→420–424 FPS) on the tested Mac, with lower aggregate frame p99 and zero stalls/dropped steps. Isolated depth preparation yields only a modest CPU saving; its window and aggregate frame-tail measurements are mixed. The twenty-run world JSON preserves both initial and final MVIDs and metric definitions. Other-device acceptance and the full original plan remain incomplete.

Current procedural recovery candidate is MVID `94b6074e-d0cb-457b-94af-0d14545b45b3`, SHA-256 `56b7de1721d56eeb4aa778f0e41d7174a571eaf4cdd7327ccb997c8dca0c3982`: 348 / 343 default/legacy pure assertions and 316 / 206 native synthetic checks pass; all 112 same-build game image/depth comparisons are exact. Desktop/server/Android arm64 builds pass. All fourteen final combat runs completed with zero simulation drops/stalls. With `DEPTH_VERTEX` off, desktop/GLES compile the exact original vertex body, with stable translated legacy sources and synchronization checks. `PROCEDURAL_BATCH` reuses exact adjacent material state including emissive intensity, and skips object setup only for zero palettes and identical unweighted pose/lights/diffuse without billboard/viewmodel. Per-item stencil, geometry and order remain intact; TrailStack uses one Begin/End for ordered quads. Unlit procedural lighting setup and guarded depth preparation avoid unused work; desktop direct immediate submission bypasses list-capture hooks without changing GL primitives or UV order. Android shares preparation/batching; experimental VBO streaming is desktop-only with enabled supported VAOs. The off-default stream pilot was slower: about 422→407 FPS without profiling and world CPU p99 1.465→1.548 ms profiled (`docs/performance/opengl-procedural-stream-pilot.json`). Final same-build unprofiled repeats show 431.21→433.64 FPS (+0.57%) with aggregate p99 6.1570→6.2489 ms (+1.49%); profiled world CPU p99 improves about 3.23% in repeated comparisons. Intentional delivery comparisons show 425.28→429.53 FPS (+1.00%) versus the last slice, but aggregate p99 6.3425→6.4176 ms (+1.18%). Frame-pacing recovery is not established. Another 112 prior-build image/depth comparisons are exact. See `docs/performance/opengl-procedural-{same,profile,cross}-results.json`, `opengl-procedural-visual.json`, `opengl-procedural-upgrade-visual.json` and `opengl-procedural-validation.json`. Hardware observations remain Mac-only.

Historical combat slice (`ROOM_CACHE` / `DEPTH_VERTEX`) reuses exact scene-owned descriptor metadata and bounded desktop frame execution proofs (256 × 1,024 IDs); current materials/poses/visibility and exact GPU atlas/context validation remain authoritative. GLES shares metadata but has no desktop combined atlas proof. The depth vertex optimization is a guarded coherent uniform branch in the existing GLSL120/GLES300 program, with synchronized hashes, not separate program variants. New MVID `0d952d3c-b569-4816-862c-86808bbfb828` passes 112 same-build native comparisons plus 112 against the previous delivered build, 201/199 default/legacy synthetic checks and 264/259 pure assertions; desktop/server/Android builds succeed. Twelve same-build combat runs show no reliable standalone FPS improvement from either new switch. Profiling-disabled combined averages are 426.70→427.39 FPS (+0.16%), with aggregate frame p99 5.8515→6.0134 ms (+2.77%); the tail observation is preserved, and no general FPS/non-regression claim is made. World submission remains the larger measured CPU cost. See `opengl-combat-next-results.json` and `opengl-combat-next-validation.json`; prior recorded Mac gains remain specific to earlier binaries. Windows/Linux capability-selected desktop paths and Android shared/GLES paths are mapped in the results document; other-device gains remain unverified.

Intentional previous/current cross-build ABBA controls are in `docs/performance/opengl-combat-cross-build-results.json`: averages 428.04→425.26 FPS (−0.65%) and aggregate frame p99 5.7653→6.0333 ms (+4.65%). This slice establishes no combat throughput win; preserved exact image/depth parity and zero simulation drops/stalls do not erase the unfavorable performance-tail observation.


## Short checks for the completed rendering slices

`short_checks.py` runs the final client with explicit feature settings, preserves raw logs and immutable binary identities, and refuses to overwrite evidence. `same` runs four 12-simulated-second combat workloads in ABBA order (about eight measured seconds each after 240 warm-up steps); `profile` runs a separate 12-second AB pair. `visual` runs six baseline/candidate workloads (8 seconds idle, 10 seconds combat with two deterministic simulation captures). Screenshot readback and capture-only HUD stabilization never run in performance groups. These short observations do not establish sustained thermal or input-latency acceptance.

```sh
python3 tools/opengl-modernization/short_checks.py --group same --prefix my-check
python3 tools/opengl-modernization/short_checks.py --group profile --prefix my-check
python3 tools/opengl-modernization/short_checks.py --group visual --prefix my-check
```

Use `--dotnet`, `--harness`, `--output`, and `--user-data` for local paths; otherwise the tool uses `dotnet`, the Release harness, an artifacts directory and inherited `PROJECT_PRIME_USER_DATA`. `--limit` and `--start` select/resume a scheduled subset. A `cross` comparison additionally requires `--previous-harness` with a materialized old client beside it; it intentionally allows different MVIDs. Use a fresh prefix after rebuilding.

The comparison bundle toggles `ROOM_CACHE`, `DEPTH_KEEP` and `SHADER_VARIANTS`. Both arms enable the prior preparation/HUD/VAO paths and leave the previously slower `DEPTH_VERTEX` and `PROCEDURAL_STREAM` experiments disabled. Exact-layout uniform cache invalidation and negative batch-preflight changes exist in both same-build arms; only a cross-build comparison includes their delivery effect. Current results and plan disposition are in `docs/performance/opengl-modernization-results.md`.


For the latest room/shader slice, pass `--bundle room-shader`. This holds `ROOM_CACHE`, `DEPTH_KEEP` and `SHADER_VARIANTS` enabled and toggles only `ROOM_GROUPS` / `PLAIN_SHADER`. Optional `--feature room` or `--feature shader` isolates that feature while holding the other enabled. Use a different prefix per feature and build.

```sh
python3 tools/opengl-modernization/short_checks.py --bundle room-shader --group same --prefix room-check
python3 tools/opengl-modernization/short_checks.py --bundle room-shader --group profile --prefix room-check
python3 tools/opengl-modernization/short_checks.py --bundle room-shader --group visual --prefix room-check
python3 tools/opengl-modernization/summarize_room_shader.py --prefix room-check
```

The summarizer validates recorded identities, requested flags, workload, cleanup and raw hashes, and compares decoded image pixels and exact depth bytes. Missing groups remain pending. Add matching `--feature` for isolated experiments and `--cross` only when that optional group was captured.

## Completion and resource checks

`short_checks.py --bundle completion` holds the previous room/cache/shader features enabled, leaves `DEPTH_VERTEX` and `PROCEDURAL_STREAM` off, and toggles `ROOM_IDENTITY`, `MESH_OBJECT`, `SHADOW_PREP`, `SHADOW_GROUPS` and `UNLIT_SHADER` together. Its visual group adds an explicitly unlit scene and Ultra shadows to the existing High-shadow and six gameplay cases. These bundled comparisons establish delivery behavior; they do not isolate which flag caused a timing change.

```sh
python3 tools/opengl-modernization/short_checks.py --bundle completion --group visual --prefix completion-final
python3 tools/opengl-modernization/resource_checks.py --group same --prefix completion-final
python3 tools/opengl-modernization/resource_checks.py --group cross --prefix completion-final \
  --previous-harness /absolute/path/pre-completion-runtime/native-render-check.dll
python3 tools/opengl-modernization/resource_checks.py --group shadow --prefix completion-final
python3 tools/opengl-modernization/resource_checks.py --group fill --prefix completion-final
python3 tools/opengl-modernization/resource_checks.py --group texture --prefix completion-final
python3 tools/opengl-modernization/summarize_completion.py --prefix completion-final \
  --output docs/performance/opengl-completion-results.json
```

The resource runner's `same` and `cross` groups are unprofiled eight-second ABBA combat controls by default. The cross group deliberately compares old/current builds and records each identity. `shadow` runs separate profiled ABBA controls at Low, High and Ultra; its FPS includes instrumentation and must not be presented as unprofiled FPS. `fill` compares 100/75/50 percent render scales with fixed High texture quality; `texture` compares High/Low textures at fixed output/render scale. Both use short profiled baseline/candidate pairs. Resolution sensitivity combines shader, fill and memory costs; texture sensitivity is a footprint/cache proxy. Neither measures GPU bandwidth, rejected fragments or true overdraw, and tested Mac GPU timers remain unavailable.

Both runners accept explicit `--dotnet`, `--harness`, `--output` and optional `--user-data`; `--limit`/`--start` bound or resume a schedule. Use a fresh prefix after a build. They refuse overwrites and verify immutable client/harness hashes plus MVID after each run. The summarizer takes `--artifacts` for that input directory, validates raw hashes, workload, flags, simulation drops/stalls and cleanup, and excludes two complete profile windows after the measurement-start marker before aligning CPU scopes by window ID. It reports all counter keys, compares decoded RGBA pixels and exact float depth bytes, and leaves missing groups pending. Cross-build metadata absent from the old harness remains explicitly unavailable; only shared available configuration fields are compared.

```sh
python3 tools/opengl-modernization/resource_checks.py --group stress --prefix resource-final \
  --cycles 30 --frames 120
```

Resource stress uses one native window/context and actual `EndScene`/`BeginScene`/room-load paths. Each cycle compares an ordinary reference with an injected fallback at the same exact simulation frame, rotates rooms, checks every scene-owned resource after unload, and requires retained-generation invalidation. The five diagnostic-only allocation stages are retained promotion after its first VBO, combined atlas after its first VBO, retained VAO after creation, depth snapshot after its first FBO, and shadow target after its first FBO. The callback defaults null and is confined to the harness GL thread; a requested stage that never fires fails the fixture. Shadow-allocation failure compares against the ordinary unshadowed reference. Depth snapshot allocation uses an explicit live D24S8 fixture when the room is conservatively ineligible for cached replay, then checks ordinary world rendering; this does not establish room replay eligibility.

Each cycle also creates the actual Studio editor renderer, invalidates its geometry/material cache three times, requires visible covered geometry and exact repeated pixels, and verifies meshes/textures/FBO/retained bytes release. This tests renderer/cache reload, not the full Studio UI. Thirty cycles are the longer memory-growth schedule; a four-cycle harness default is only a pilot. Exact resource accounting must return to zero after every unload. Process RSS and managed-memory trends are informational and include initialization, GC and driver behavior. On the tested Mac, private-memory accounting returns unavailable and is represented by null with `privateMemoryAvailable=false`; zero is not evidence of an empty process. Screenshot/readback, GC and reload transitions are excluded from FPS evidence.

For an unresolved RSS trend, `memory_checks.py` runs a separate sixty-cycle schedule with final `-resource-drain`, records immutable invocation/client/harness/raw hashes and wall time, and writes a memory report with ten-arm block means.

```sh
python3 tools/opengl-modernization/memory_checks.py --dotnet /absolute/path/dotnet \
  --prefix completion-memory --cycles 60 --frames 120 \
  --report docs/performance/opengl-completion-memory.json
```

The diagnostic records live managed bytes, GC committed/heap/fragmented bytes, RSS and thread count after each arm, retaining every observed audio shutdown task. Completion times are observations from cleanup start and include GC/scheduling; they are not exact native device-close durations. The final drain samples before waiting, after waiting up to fifteen seconds for all recorded audio tasks, and after one blocking full GC/LOH compaction, finalizers and `GL.Finish`. Remaining/faulted tasks are explicit. A reduction after draining suggests deferred release; an unchanged RSS value alone does not prove a leak. Owned-resource zero is recorded separately from still-unverified native memory boundedness. `--summarize-only` reads an existing diagnostic; neither raw evidence nor report files are overwritten.
