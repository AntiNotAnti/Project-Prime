# Project Prime: Next Engineering Implementation Plan

**Scope:** P0 persistent AGX graphics allocations; P1 targeted OpenGL CPU submission optimization; P1 Netcode 2.1 timed-pickup authority completion; P2 scene lifecycle and ownership refinement.\
**Repository:** [`AntiNotAnti/Project-Prime`](https://github.com/AntiNotAnti/Project-Prime)\
**Reference source reviewed:** `main` at `64af9a78e7b9484b450c42e1479144a7663bd47a`, October 13, 2026.\
**Audience:** Autonomous coding agent, reviewer, and release maintainer.\
**Status:** Implementation specification, **not** a claim that the following changes have already been implemented or passed.\

## 0. Mission, assumptions, and execution rules

### Objective

Deliver a simpler, more stable engine with measured long-session memory behavior, lower OpenGL CPU/driver submission costs, authoritative timed-power-up damage semantics, and correct scene ownership. Preserve user-facing fidelity and the successful consolidation work. Do **not** perform a wholesale renderer, `Scene`, `PlayerEntity`, or `NetSession` rewrite.

### Assumptions

1. Treat unrelated consolidation PRs, integration work, package fixes, and release-validation work as successfully completed, as directed. Do not spend the program reopening those items.
2. Treat timed-pickup A1 (authority grant ledger), A2 (claims/contact proof), and A3 (prediction/grant reconciliation) as **existing prerequisite functionality** in the target integration branch. Verify they are actually present before writing A4. In the source snapshot reviewed, A1/A2/A3 remain stacked PRs #472/#473/#474, and A3's published document still says **NOT ACCEPTED**. If the target tree already contains their final accepted implementations, skip reimplementation. If not, clearly mark the A4 dependency as blocked rather than silently replacing that system.
3. Retain current OpenGL on desktop and OpenGL ES on Android. No Vulkan/WebGPU/DX12/Metal renderer reintroduction.
4. Preserve the `NetConfig.ProtocolVersion` of the checked-out target **unless and until an actual incompatible wire change requires a coordinated increment**. `main` at the review snapshot is protocol 53; A3's staged proposal declares protocol 54. Never assume that 54 or 55 is the correct new number without checking the actual target source.
5. Keep all previously enabled, validated render optimizations. `DEPTH_VERTEX` and `PROCEDURAL_STREAM` remain opt-in pending positive evidence. Never repeat the failed individual-model VBO promotion unchanged.
6. Keep compatibility with historical replay formats and source data. No fabricated grant evidence for archived files. Keep retired Enhanced Hunters live behavior disabled while historical readers remain valid.

### Invariants: mandatory for every PR

- **60 Hz simulation**, independent from 60/120/144/240+ Hz rendering; no new per-frame game logic tied to render cadence.
- Existing dedicated-server authority for combat, health, score, match-end and semantic events; existing client-reported movement validation and lag compensation remain unchanged unless separately accepted.
- Exact `ShotKey` / source-life / generation / match / authority-epoch payment identities; once-only damage, combat ACKs and reliable terminal receipts.
- Existing normal, Balanced Mode, hunter affinity, alt-form/morph-ball, turret, splash/child projectile and weapon behavior.
- Replay world/checkpoint/seek/determinism contracts; compatibility adapters remain intact.
- Ordered GL opaque/decal/translucent, stencil, depth reconstruction, particle and viewmodel behavior; no pickup/player-outline visibility through walls.
- Valid owner, thread, context-generation and resource lifetime for GL, audio, UI, scene, map, and replay objects.
- Windows, Linux, macOS, Android and dedicated-server builds; no shipping of game assets or logs containing secrets.
- No blind compiler-warning suppressions, unexplained feature defaults, silent failure swallowing, or broad mechanical renames.

### Agent implementation protocol

1. Fetch `origin/main`, record exact SHA, active PR bases and current protocol, and compare against this specification. Work from fresh feature branches; never force-reset or overwrite the user's changes.
2. For each PR write a **hypothesis**, **one change boundary**, **baseline evidence**, **positive and negative tests**, **measured results**, and **rollback instructions**. Prefer small, reviewable PRs and feature flags for runtime experiments.
3. Split diagnostic-only instrumentation from production behavior changes. Do not call a diagnostic PR an optimization.
4. Treat every untested platform, missing fixture, skipped check, inconclusive GPU timer, or confounded benchmark as **NOT_RUN / UNVERIFIED**, not PASS.
5. Do not claim improvement from FPS, RSS, GL-name deletion, or a single microbenchmark alone. Preserve raw runs, failed attempts and immutable binary/source hashes.
6. Stop or revert any candidate that breaks exact parity, lifecycle closure, reproducibility, or a required CI gate; do not compensate by weakening the gate.
7. Update source-backed documentation in the same PR. Every merged implementation PR must have successful **exact-head** CI, then a final merged-main validation.

## 1. Proposed workstream and PR dependency graph

| PR slice | Priority | Work | Dependency | Gate |
|---|---|---|---|---|
| `AGX-01` | P0 | Reproduce AGX allocation growth with immutable baseline and per-phase attribution | None | Existing growth quantified reproducibly |
| `AGX-02` | P0 | Independent shader-compiler and lifecycle control matrix | AGX-01 | Identify app-vs-driver mechanism or preserve explicit unknown |
| `AGX-03` | P0, conditional | Narrow proven owner or compile-work fix, optionally context-local shader lease | AGX-02 | Exact parity, context/lease closure, allocation improvement |
| `AGX-04` | P0 | Long-load boundedness confirmation and diagnostics gate | AGX-02/03 | No unsupported plateau claim; release disposition documented |
| `GL-01` | P1 | Clean-host CPU/GPU/render-source attribution | AGX instrumentation stable | Ranked bottleneck supported by repeated captures |
| `GL-02` | P1 | Palette/uniform setup work elimination | GL-01 | Same images/depth, fewer operations, measured frametime |
| `GL-03` | P1 | Particle/procedural work consolidation, order-preserving | GL-01 | Same order/state and image/depth, measured benefit |
| `GL-04` | P1 | Device A/B rollout and policy selection | GL-02/03 | Windows/Linux/macOS/GLES positive gates or defaults stay off |
| `NET-01` | P1 | Shot-scoped trusted timed-grant certificate in Shadow | Accepted A1–A3 | Trusted source-frame classification and immutable history |
| `NET-02` | P1 | Apply trusted modifier to authority emissions and rescue payouts | NET-01 | No illegitimate multiplier; existing exact-once payment survives |
| `NET-03` | P1 | Continuous/secondary/turret/dynamic-source completeness | NET-02 | Explicit supported/unavailable family matrix |
| `NET-04` | P1 | Impaired live acceptance, replay/checkpoint, rollout | NET-03 | Complete positive workload + no accidental live enforcement |
| `LIFE-01` | P2 | Retention-root and owner graph audit | AGX-04 or independent diagnostic scope | Actual roots and legitimate save leases distinguished |
| `LIFE-02` | P2 | Explicit post-match save handoff and commit ownership | LIFE-01 | Save parity during return/lobby/update/replay |
| `LIFE-03` | P2 | Scoped foreground facade binding and async owner fencing | LIFE-02 | Old world cannot mutate a new one or remain unintentionally retained |
| `LIFE-04` | P2 | Lifecycle soak and measured cleanup | LIFE-03 | Scene/root closure, no GL/audio/renderer regression |

`GL-*` and `NET-*` can proceed in parallel after baselines; do not concurrently edit overlapping renderer/scene ownership code without branch reconciliation and integration testing. `LIFE-*` should not change shader/resource ownership while `AGX-*` is validating its lifetime hypotheses.

---

# 2. P0: Persistent AGX graphics allocations and native memory stability

## 2.1 Observed evidence and problem statement

The existing macOS AGX diagnostics found **167.783630 MiB** of outstanding direct malloc-prefix fragment-program groups after a first 120-cycle context is disposed; after 180 additional cycles in a fresh context, the population reached **423.113419 MiB** and remained **422.972794 MiB** after disposal. A separate final GLFW poll/terminate control left the identified AGX fragment group unchanged. Recognized GL names, shaders, buffers, framebuffers, textures, and display-list names were correctly retired in the tested cases. Therefore, the problem is not explained by known live GL names, but **it is not proven to be a driver leak**.

Relevant baseline reports: [`native-context-controls.md`](https://github.com/AntiNotAnti/Project-Prime/blob/64af9a78/docs/audits/engineering-consolidation/native-context-controls.md), [`native-attribution-results.md`](https://github.com/AntiNotAnti/Project-Prime/blob/64af9a78/docs/audits/engineering-consolidation/native-attribution-results.md), and [`transition-gc-controls.md`](https://github.com/AntiNotAnti/Project-Prime/blob/64af9a78/docs/audits/engineering-consolidation/transition-gc-controls.md).

### Primary code and tool targets

- `src/MphRead/Mods/Render/SceneShaderOwnership.cs`
- `src/MphRead/Mods/Render/WorldShaderVariants.cs`
- `src/MphRead/Mods/Render/GraphicsPipeline.cs`
- `src/MphRead/Mods/Render/PlayerOutlines.cs`
- `src/MphRead/Mods/Render/GraphicsApi.cs`
- `src/MphRead/Mods/Render/GlEs.cs`
- `src/MphRead/Mods/Render/SharedModelResources.cs`
- `src/MphRead/Mods/Render/DesktopRetainedGeometry.cs`
- `src/MphRead/Renderer.cs`, `Scene.OnLoad`, `Scene.UnloadGl`, `RenderWindow.EndScene`
- `tools/native-render-check/`, `tools/native-render-check/README.md`
- `docs/audits/engineering-consolidation/`

## 2.2 AGX-01: Freeze reproduction and establish attributable memory accounting

**Implementation**

1. Enhance the existing `native-render-check` resource-context harness only as needed. Give every capture an immutable manifest: Git commit, assembly MVID and SHA-256, macOS version/build, GPU/driver, OpenTK/.NET versions, scene/map key, window context/share-group identity, framebuffer size, graphics preset, flags, run seed and exact replay/input fixture.
2. Use **isolated user data** and identical extracted-asset identity across arms. Do not silently inherit a different graphics preset, OpenGL environment variable or operator-running game state.
3. Run serial warm/cold repeated-load campaigns with stepped targets such as 0, 30, 120, 300, 600, 900 loads, stopping early if memory/resource guardrails become unsafe. Test same-context reload, context destroy/recreate, optional final GLFW termination, no-shader/control workload, and one minimal GL-only program workload.
4. Capture **separate** metrics at each phase: outstanding AGX stack-origin groups and call counts, VM category/physical footprint, resident RSS, GC heap/committed/LOH, retained asset pools, GL names still recognized, context/share-group lease counts, shader compilation/link attempts and shader-source/stage hashes. Never infer native heap as `RSS - GC`.
5. Preserve the existing capture tool's `vmmap -summary` / `malloc_history -allBySize -fullStacks` semantics on supported macOS. Record unsupported data as null. Log instrumentation overhead and use different runs for memory diagnostics and FPS.
6. Add a data-quality validator rejecting missing capture phases, changed binary hashes, truncated stack logs, source-key collisions, unexpected workload gaps, silent failures and mixed contexts.

**Acceptance**: reproducibly separate per-load application shader/program creation from the native groups that remain, with failure and incomplete-arm receipts preserved. This slice can pass without claiming a fix.

## 2.3 AGX-02: Minimal reproduction and compiler-variant attribution

Build **small diagnostic-only** reproductions, not new production backends:

- No shader compilation and no scene loads, but identical context create/destroy.
- Identical canonical vertex/fragment source compiled and linked repeatedly in one context, with complete delete/unbind.
- The same program sources compiled across fresh unshared contexts and, separately, a tested shared-context configuration.
- One program compiled once and rebound many times while all materials/uniforms/poses change.
- Canonical, native, plain and unlit variants individually, then the existing four-program prewarm pattern.
- GL shaders with the same source text but different attribute bindings, define strings, link inputs, sampler/texture state and pipeline state, to isolate the *actual* driver pipeline key rather than assuming GLSL hash alone is sufficient.
- Empty map, static room, animated hunters, shadows Off/High/Ultra, editor preview and replay/side scene as separate arms.

Record `GL.CreateShader`, source hash, `CompileShader`, link/status, `GL.UseProgram`, detach/delete, context identity, program generation and error path. **Do not log full proprietary shader/material data** when hashes suffice. Instrument off the hot path except for opt-in diagnostics. Cross-check all creation paths, not just `WorldShaderVariants` (`GraphicsPipeline`, outline, launcher, previews, diagnostics).

**Decision tree:**

- **Engine-owned resource survives** or a program stays bound after supposed teardown: fix that exact owner, including partial initialization and failure rollback; rerun native recognition plus real allocation groups.
- **Redundant program compilation with proven equivalent context-local contract**: prototype minimal same-context reuse in AGX-03. Do not share across destroyed or unshared contexts.
- **Growth reproduced in a tiny correct GL program with complete owner release**: classify as reproducible *driver-origin behavior*, not an engine leak. Seek a safe, measured reduction in program churn; preserve a minimal driver-reproduction artifact for vendor/OS comparisons.
- **No reproducible growth**: test original environmental confounders and preserve `UNVERIFIED`; do not invent a successful fix.

## 2.4 AGX-03: Conditional production correction

**Do not automatically implement a global shader cache.** Only if AGX-02 demonstrates it is safe and likely to reduce native growth, implement a small **context-local program lease/cache** in a separately gated PR:

1. The lease owner is the **live GL context/share group**, not a static global and not an arbitrary scene. Key by context generation/share-group, exact fully expanded vertex/fragment sources, shader stage, attribute bindings, link options and all proven pipeline-identity inputs. Distinguish program objects from mutable scene uniforms.
2. Program object has explicit acquire/release; creation and rollback are exception-safe; native deletion occurs only after its last legitimate lease, on the owning GL thread while the context remains valid. Context destruction invalidates all handles/tokens and forces fresh programs on a new unshared context.
3. A scene must **never inherit another scene's mutable uniform state**. On reuse, restore all scene-owned matrices, palette layouts, view/projection, texture units/samplers, lighting, fog, skins/cosmetics, alpha/depth, shadow/outline state and program-specific cache stamps. Add a complete per-program/per-scene generation invalidation contract.
4. Prove safe simultaneous foreground, Replay Studio, map/editor, launcher preview and side-scene use. Preserve the existing `_fullWorldProgram`/`_nativeWorldProgram`/`_plainWorldProgram`/`_unlitWorldProgram` fallback behavior. Reject and fall back to independent compile if the eligibility proof is incomplete.
5. Keep an opt-in policy, e.g. **proposed** `PROJECT_PRIME_GL_PROGRAM_LEASE=1`; no change to default until accepted. Bound cache entries/bytes and expose lease counts and surviving program generations.
6. Inject failure at shader allocation, compile, link, initialization, scene disposal and context recreation. No deleted program may be sampled by a later owner.

Alternative narrow fixes (wrong unbind order, extra compile in a secondary path, retained temporary strings, a real leaked native handle) are preferable to a cache if proven.

## 2.5 AGX-04: Long-session validation and release decision

Repeat the same immutable campaigns with baseline and candidate in reversed order. Primary indicator is **outstanding named AGX allocation groups by origin over increasing workloads**, with a stable warm-up period and confounder report; RSS is secondary and can decrease due paging while native allocations rise. Define a noise envelope from repeated control arms **before** interpreting a candidate. A boundedness PASS requires stabilization across multiple successive increasing-load windows and no continuing material growth beyond that observed control variation. A falling RSS trace, one end-of-run cleanup, or GL `IsProgram=false` does not pass.

Cross-check on a second macOS/GPU/OS combination if available; collect Windows/NVIDIA/AMD and Linux/Mesa GL lifecycle regression separately. Do not imply the Apple allocator result generalizes to other drivers.

**Exit states**:

- `FIXED_AND_BOUNDED`: cause shown, corrective patch accepted, sustained groups plateau, exact visuals/tests pass.
- `DRIVER_BEHAVIOR_WITH_SAFE_MITIGATION`: minimal repro proves app-level release while safe churn reduction is measured; document remaining driver uncertainty.
- `UNRESOLVED_P0`: growth persists or classification fails. Do not declare full memory acceptance or turn on speculative sharing/GC knobs. Provide quantified mitigation/operational risk, hardware scope and an explicit release decision.

---

# 3. P1: Targeted OpenGL CPU submission optimization

## 3.1 Evidence and excluded false starts

Existing instrumentation partitions executed immediate submissions and inclusive `RenderItem` CPU scopes. In the measured High-shadow fixture, palette geometry had median window p95 **0.941 ms**, particles **0.664 ms**, room geometry **0.371 ms**. These are **inclusive CPU wall times with possible driver waits**, not isolated GPU execution or proof that batching is eligible. The previous single-model retained VBO experiment changed **7,380 world pixels**, **7,369 HUD pixels** and **16,366 depth values** and was reverted. Repeating it unchanged is forbidden.

Review [`render-draw-attribution.md`](https://github.com/AntiNotAnti/Project-Prime/blob/64af9a78/docs/audits/engineering-consolidation/render-draw-attribution.md) and [`opengl-modernization-results.md`](https://github.com/AntiNotAnti/Project-Prime/blob/64af9a78/docs/performance/opengl-modernization-results.md).

### Primary source targets

- `src/MphRead/Mods/Render/OpenGlPassProfiler.cs`
- `src/MphRead/Mods/Render/OpenGlSubmissionProfiler.cs`
- `src/MphRead/Mods/Render/OpenGlFrameProfiler.cs`
- `src/MphRead/Mods/Render/MatrixPaletteRevision.cs`
- `src/MphRead/Mods/Render/MeshObjectStateReuse.cs`
- `src/MphRead/Mods/Render/ShadowCasterPreparationCache.cs`
- `src/MphRead/Mods/Render/ProceduralFrameGeometry.cs`
- `src/MphRead/Mods/Render/DynamicGeometryStream.cs`
- `src/MphRead/Mods/Render/DesktopProceduralStream.cs`
- `src/MphRead/Mods/Render/DesktopRetainedGeometry.cs`
- `src/MphRead/Formats/Types.cs` (`RenderItem`)
- `src/MphRead/Renderer.cs` (draw paths, effect/particle submission)
- `src/MphRead/Mods/Render/GraphicsApi.cs`, `GlEs.cs`
- `tools/native-render-check/`, `tools/opengl-modernization/`, `tools/run-perf-suite.sh`

## 3.2 GL-01: Select a proven work-reducing target

Use current `[glpasses-json]` owner attribution plus frame CPU, swap/present, display interval, GL counters and GPU timings **only where actually supported**. Collect at least five matched unprofiled runs per arm in interleaved/reverse order, identical fixture/caps/settings, idle machine recorded. Save all runs, including outliers and negative controls.

Mandatory rendering matrix: solo, one bot, eight players; original preset; shadows Off/Low/High/Ultra; 1920×1080 plus representative low-end size; 60/120/144/240 Hz presentation where hardware supports; normal first-person, morph ball, high particle density, moving/shadowed models, transparent/alpha/stencil, HUD, replay and editor views. Include one Windows GL driver, one Linux GL driver and physical Android GLES when available.

Rank by **actual executed owner operation count + per-owner setup/geometry CPU time + total end-to-end p95/p99**. Separate world CPU, GPU, swap wait and frame-pacing. Do not prioritize a category solely because its immediate draw count is high.

## 3.3 GL-02: Animation palette and uniform preparation

1. Determine repeated palette/transform state operations that still occur despite current `MatrixPaletteRevision`, `MeshObjectStateReuse` and room/shadow caches. Record exact per-program upload count/bytes, redundant requests, normalized uniform state and origin for one frame.
2. Eliminate only repeat work with **provable identity**: same active shader program/generation, scene, matrix source object and revision, palette length/count/scale, transform, weighted skinning, billboard mode, viewmodel/pass, shader uniform location and context. State snapshots must not hold scene lifetime past owner release.
3. Prefer reusing scalar layouts/derived transforms and avoiding repeated uniform writes to reshaping vertex formats. Keep matrix values calculated at the same gameplay/render time. Do not use array-address equality or model name as a proxy for a verified revision.
4. Explicitly invalidate when animation advances, replay seeks/restores, model/skin/LOD swaps, camera/viewmodel transforms change, program relinks, textures/materials/cosmetics change, scene unloads, an editor overrides state, or GL context is recreated.
5. Preserve native fallback for unknown, unversioned, modified or partially supported geometry. Keep existing program-specific matrix layout registration; the previous wrong-layout optimization produced additional uploads and was fixed.
6. Add a source-level test with two equal and two near-equal batches, plus an intentionally mutated array without revision; unknown mutation must force fallback rather than reuse.

**Evidence required:** unchanged pixel/depth/stencil outcomes; lower real uniform/matrix operation counts; improved matched CPU/render p95/p99 or a documented negative result. No default-on if the improvement is only a synthetic no-op counter.

## 3.4 GL-03: Order-safe particle and procedural submission

1. Characterize by particle/trail type, pass, shader, texture, depth blend and stencil. Identify the work truly repeated across passes; existing `ProceduralFrameGeometry` already packs immutable points once per frame, and `PROCEDURAL_BATCH` is default-enabled. Build on those features rather than implementing a second conflicting queue.
2. Prototype **same-pass contiguous work only**: bounded reusable CPU scratch/stream ranges, avoid repeated point/UV preparation and redundant vertex pointer setup, and merge only *adjacent state-compatible* primitives whose original per-item state and draw order are identical. A material pass barrier or order-sensitive alpha/stencil/depth transition closes a run.
3. Do not reorder particles by texture/material across transparency or replay-depth passes. Do not expand the legacy display-list model promotion that previously broke depth/HUD parity. Keep exact native fallback for incompatible fixed-function state, attribute inheritance, unsupported primitive topology, unknown revisions and stream-buffer exhaustion.
4. If experimenting with `PROCEDURAL_STREAM`, use a separate explicit opt-in with original and candidate in the **same binary** where feasible. Compare buffer orphaning, uploads, batches, driver waits, GPU time and actual frame pacing. An unfavorable result leaves the setting off.
5. Ensure persistent or pooled buffers release after context/scene change; no new AGX resource-growth regression. Never allocate or resize large arrays each presentation frame.

**Evidence required:** deterministic particle sort/order witnesses, exact image/depth comparison including near/far particles, muzzle effects, explosions, bombs, jump pads, translucent walls, shadows, HUD and Studio, plus repeated end-to-end CPU/GPU improvements.

## 3.5 GL-04: Controlled rollout

Only ship candidates meeting existing project regression thresholds and demonstrating a repeatable net benefit at target workloads. **Proposed selection target**: at least approximately 10% lower relevant owner CPU work or a defensible 3–5% improved total p95/p99 without another metric materially degrading; treat these as desired gates, not guaranteed gains. A candidate that is flat or unstable should be reverted or remain default-off.

Require:

- No sim dropped ticks, altered tick cadence, input-latency regression, frame-cap overshoot, shader relink storm, or GPU/driver error.
- Exact unchanged world/HUD PNG, float32 world depth and available shadow depth, including solo and one-bot occlusion.
- Explicit macOS, Windows, Linux and GLES results; unsupported native GPU queries marked UNVERIFIED.
- Lifecycle/context/Studio/replay resource stress from AGX-04 repeated against the optimized renderer.
- A policy rollback without changing the wire protocol, saves or shader cache contracts.

---

# 4. P1: Complete Netcode 2.1 authoritative timed pickups

## 4.1 Prerequisites and scope

Verify the target tree has accepted A1, A2 and A3 implementations and their true protocol/checkpoint constants. The reviewed staged documents are [`netcode21-a1-grants.md`](https://github.com/AntiNotAnti/Project-Prime/blob/8563b672/docs/netcode21-a1-grants.md), [`netcode21-a2-pickups.md`](https://github.com/AntiNotAnti/Project-Prime/blob/a4f4371a/docs/netcode21-a2-pickups.md), and [`netcode21-a3-reconciliation.md`](https://github.com/AntiNotAnti/Project-Prime/blob/31cdd16d/docs/netcode21-a3-reconciliation.md). Do not recode their ledgers, wire queues or replay projection.

A1 establishes three-state `GrantedAt`: **Granted**, **NotGranted** and **Unavailable**, keyed by authority epoch/match/room, actual spawn incarnation, full player life and source input frame; grant begins the step **after** authority contact (`C+1`), uses native 1,800-tick duration, and expires exclusively at `C+1800`. A2 verifies contact/claim, retaining immutable terminal outcomes. A3 separates predicted visual timers from authority-confirmed effects and carries grant facts to clients/replay. **None alone proves a delayed shot was authorized for Double Damage at fire time.** A4 must bind independently accepted shot identity to trusted ledger evidence and enforce the same modifier on both native and rescued damage.

### Primary code targets (after A1–A3 integration)

- `src/MphRead/Mods/Network/NetTimedGrants.cs`, `NetPowerUpLedger.cs`, `NetShotSourceJournal.cs`
- `NetShotProof.cs`, `NetShotInputProof.cs`, `NetFireEvents.cs`, `AcceptedShotLedger.cs`
- `NetHitClaims.cs`, `NetDamage.cs`, `NetCombatPayments.cs`, `NetContinuousPayments.cs`
- `NetFireEvents.cs`, `NetContinuousAdmission.cs`, `NetBombs.cs`, `NetSelfSplash.cs`
- `PlayerEntityAcceptedShot.cs`, `PlayerEntityNetAim.cs`, `PlayerEntityTimedGrants.cs`
- `NetPickupClaims.cs`, `NetPickupNetwork.cs`, `NetGrantNetwork.cs`, `NetTimedGrantFact.cs`
- `ReplayGrantFacts.cs`, `ReplayPickupFacts.cs`, `ReplayReplicaCheckpoint.cs`, `ReplaySceneServices.cs`
- `src/MphRead/Entities/Players/PlayerEntity.cs`, `PlayerProcess.cs`, `PlayerCollision.cs`
- `tools/nettest/` and the existing 2/4/8-client impaired campaign + evidence pipelines.

## 4.2 NET-01: Immutable authority shot-time grant certificate

1. At an actually admitted, lifecycle-fenced `FireEvent` / native owner emission, construct an **authority-only read-only certificate** keyed by complete `ShotKey` (`epoch, match, shooter slot, slot generation, life ID, ShotId`) and exact source frame. A claim ID or packet arrival clock is not a shot identity.
2. Query `NetTimedGrants.GrantedAt` only for the corresponding retained source input/life. Cross-check the native grant's `GrantSequence`, timed type, genuine item incarnation/contact and source-clock plausibility. Preserve the three distinct outcomes: `Granted`, `NotGranted`, `Unavailable` (with reason). Never turn eviction or missing history into known absence or proof.
3. Record *immutable* modifier facts once per shot: whether Double Damage was authorized at fire; which authority grant funded it if present; source frame and authority decision clock; source validation state; exact fire source. Do not recompute authorization from a later owner's timer after a renewal, death, expiration, scene reload or verdict arrival.
4. Protect storage with finite slot/life/epoch capacities and a retention horizon covering existing delivery, rewind, shot history, claim grace and component-flight windows. Derive bounds from existing constants and document overflow/expiry; reject ID aliasing. No unbounded dictionaries or perpetual per-shot heap objects.
5. Add **read-only Shadow** classification first, behind its own opt-in or default-Shadow policy (`TimedGrantProofMode: Off|Shadow|Enforce`, actual name chosen in code). Preserve existing `PRIME_SHOT_PROOF` behavior; modifier verification must not silently activate stronger all-weapon geometry enforcement.
6. A `DoubleAtFire` flag, owner timer, predicted HUD, or client's declared damage are **hints** only. A spoofed flag may be logged, never promoted into a trusted grant. Other buffs (Prime Hunter/affinity/Balanced Mode) retain their separate existing authority contracts.
7. For a shot at the precise grant boundary test `C`, `C+1`, `C+1799`, `C+1800`, renewal, death, old generation, stale room, rollover and a shot delayed until after the grant expired. Only the source-frame truth may authorize the multiplier.

**Acceptance:** immutable certificate equivalence against A1 ledger and accepted-shot journal; no certificate for a fabricated/nonadmitted fire event; bounded storage; zero shadow impact on production damage, duration or packet bytes.

## 4.3 NET-02: Single trusted modifier path for native and rescued damage

**Critical sequencing**: validate modifier ownership **before** an authority-spawned accepted projectile uses its `BeamSpawnFlags.DoubleDamage`; checking only at `NetHitClaims.JudgeCore` is insufficient because the authoritative projectile may already have applied inflated damage. Currently `PlayerEntityAcceptedShot.cs` uses `fire.DoubleAtFire`/`ContinuousDouble` during emission; `NetHitClaims.JudgeCore` uses an overall `MaxDamageFor` ceiling. Reconcile both to a common trusted shot/source policy.

1. Add a narrow modifier evaluator that accepts accepted shot certificate + weapon family + source rules, and returns a **trusted emission modifier and exact bounded damage policy**, not the client's reported multiplier.
2. On authority accepted-shot spawning, pass the sanitized modifier into the existing spawn/equipment path. Preserve recorded original owner-authored pose/flags as presentation evidence if necessary, but never let that unsanitized state authorize health damage. Do not alter normal client prediction or archived replay reproduction accidentally.
3. At claim adjudication, use the same certificate and weapon mechanics to determine the legal damage/effects ceiling, in addition to existing geometry/lifecycle checks. Validate body vs headshot, direct vs splash, charge, affinity, Prime/Double, Balanced tuning and native weapon-specific behavior; **do not trust the claimed damage** just because it is below an all-buffs theoretical maximum.
4. Preserve native-hit vs rescued-claim equivalence. `NetCombatPayments`, `NetContinuousPayments`, accepted component IDs, lag-compensation history and `CombatAck` keep exact-once payment semantics. A duplicate or delayed claim must return its original terminal outcome, not recompute under current power-up state.
5. `Unavailable` source proof must have an explicit policy: in Shadow, telemetry only; in Enforce, never grant an unverified *extra multiplier*, but do not silently discard otherwise valid ordinary base damage. Decide the exact compatible fallback per weapon **from validated native fixtures before enabling Enforce**. Expose this status in telemetry so legitimate-history gaps cannot be hidden.
6. Handle pre-existing true owner/native authority damage separately from client-authored forwarded fire where necessary. Native bots and authority-only contact must use the same ledger with truthful authority frames; no privileged fake grant or replay input is added.

**Acceptance:** forged double flag and oversized damage cannot increase paid authority damage; valid fire-time double grants produce exact native expected damage despite delayed arrival or later expiry; every approved claim pays no more than once; no false effect/sound/replay duplication.

## 4.4 NET-03: Cover all source families, dynamic items and special weapons

Inventory and explicitly classify these paths before attempting general Enforce:

- Hitscan/no-scope/zoom/headshot, charged and ordinary projectile.
- Explosion/splash/direct, ricochet, secondary/cluster child; children inherit a valid originating source key and approved modifier, **not** another independently forged grant.
- Continuous Shock Coil ticks, per-tick source frame and healing/drain payment; each pulse gets its correct source-truth rule.
- Weavel turret fired before owner death / old-life flight, turret outcomes, native self-knockback and Balanced Battlehammer burst/concussion.
- Bombs, alt-form contact, self-damage, traps/DoT and non-shot events. Use an existing trusted launch/contact identity where one exists; otherwise mark as unsupported/unavailable and retain the original safe baseline until a distinct authority proof is designed.
- Samus, Kanden, Spire, Noxus, Trace, Sylux, Weavel; normal/Balanced/Hunter affinity and powerup-disabled options.

Use a family capability matrix with exactly `SUPPORTED`, `SHADOW_ONLY`, `UNAVAILABLE`, `NOT_APPLICABLE`. **No path may silently consume owner Double flags under an Enforce label**. Keep special affinity/balance effects independent of the retired Enhanced Hunters implementation.

For dynamic timed drops: A2 lacks a shared *owner-side claim identity* for arbitrary dynamic items, although the authority ledger and A3 visualization can refer to full spawn incarnations. If final A1–A3 do not already close this gap, implement reliable discovery/claim by **authority-issued dynamic incarnation identity**, never approximate position matching. Preserve authenticated endpoint, native reach, first-contact source, capacity and finite delivery. Cover removal, same-position twin drops, reorder, respawn/room transition, late join, duplicate discovery and consumed tombstone.

## 4.5 NET-04: Replay, impaired UDP, enforcement readiness

1. Add deterministic 1/2/4/8-player and all seven-Hunter grant-to-shot fixtures. Test real native items for **Double Damage, Cloak and Death Alt**; ensure Cloak/Death Alt presentation transitions remain correct even though A4's damage multiplier is Double-specific.
2. Run source chronology cases: same source frame, contact+1, renewal replacing old grant, expiry, player death/rebirth, disconnect/late join, slot reuse, map rotation, epoch rollover, grant fact before verdict and verdict before fact, duplicate claims and malformed/truncated packets.
3. Run full application-level network campaigns, not only codec/transport loops. Representative matrix: RTT 0/100/250/400 ms, jitter 0/20/40/80 ms, loss 0/1/2/5%, reorder 0/1/3/5%, duplication 0/1/2%, plus a 400 ms pump stall and controlled rate/capacity exhaustion. Start with smoke, then repeat representative two- and eight-player campaigns for 20–30 minutes with **positive grant/shot/claim/payment denominators**.
4. Validate exact A1 grant transitions, A2 terminal claims, A3 prediction handover, A4 source certificates, accepted components and paid damage as independent joined evidence. No assertion may PASS when no timed pickups or no affected shots actually occurred.
5. Preserve immutable replay v1–v5 adapters and current checkpoint rules. New recordings must preserve grant/certificate facts without borrowing live `NetSession` or a live world. Seek/clip bootstrap, private replica and fast export must reproduce authoritative damage and presentation with the same source semantics. Old recordings without capability must not invent new grants.
6. Run Off/Shadow side-by-side and compare gameplay hashes/damage/health results. Enforce starts **opt-in on a canary server** only after valid covered families have high proof availability and zero unexplained false rejects in controlled positive campaigns. A suggested evaluation gate is at least 99.9% verified source coverage *among declared supported positive cases*, measured from meaningful samples; insufficient sample counts remain inconclusive. No blanket rollout from synthetic passes alone.
7. Roll back via same-protocol enforcement policy to Shadow if safe; if wire or replay formats change, coordinate server/client version rollout while retaining old replay readers. Never ship a mixed-version handshake by accident.

**Netcode definition of done:** live grants cannot authorize damage retroactively; malicious/delayed reports cannot obtain the Double multiplier without independent shot-time authority evidence; native and rescued hits agree; all supported families, replay and impaired-client gates pass; unsupported families and unknown evidence remain explicit.

---

# 5. P2: Scene lifecycle and ownership refinement

## 5.1 Real source boundary and constraints

A `Scene` already owns `ScenePlayerRegistry`, `SceneGameState`, `MatchRandom`, scene service policy and a `PlayerReplicationBridge`. `CollisionQueryWorkspace` has already removed a significant shared scratch lifetime problem. **Do not rewrite these working structures.** The remaining issue is that compatibility facades such as `PlayerEntity.LegacyRegistry`, `GameState.Current` and `Rng.Current` can refer to the foreground scene. Existing exit paths call `window.EndScene()` **before** `MatchStart.AfterMatch()` commits post-match save state. Clearing those facades at `EndScene` would risk losing valid saves. The previous weak-alive observation is **not proof of a leak**.

Sources: `src/MphRead/Scene.cs`, `SceneServices.cs`, `ScenePlayerRegistry.cs`, `GameState.cs`, `Mods/Replay/SceneReplica.cs`, `Mods/Launcher/Gui/Shell.cs`, `Renderer.cs`, `Mods/Network/ServerSim.cs`, and [`transition-gc-controls.md`](https://github.com/AntiNotAnti/Project-Prime/blob/64af9a78/docs/audits/engineering-consolidation/transition-gc-controls.md).

## 5.2 LIFE-01: Actual retained-root and lifecycle ownership map

1. Instrument diagnostic weak/strong liveness probes after **eligible** teardown, not while a local variable/tool observer intentionally keeps the world alive. Capture actual .NET root paths if a prior scene survives after required save commitments and enough normal generational collections.
2. Build an explicit owner map for foreground scene, dedicated match child, offline scene, Replay Theatre/Studio replica, replay lab, thumbnail/preview, map editor playtest, async room prewarm, capture/export and audio. Record creation, binding, publication, shutdown, cancellation and final dispose thread.
3. Differentiate legitimate bounded current-owner facades, queued callbacks, native handles awaiting GL/audio owner teardown, global retained caches and unintended leaks. A weak target alive before post-match saving **is allowed**; a world retained indefinitely after final owner release is a regression hypothesis requiring its own root proof.
4. Use existing `-transition-gc` and `-transition-rebind` diagnostic controls. **Keep existing production Gen2 OnLoad and EndScene GC behavior**: the previous controlled study showed no clear per-frame benefit from removing them, and the end collection reduced managed memory at a transition. No GC policy experiment may be silently enabled.
5. Introduce value-only `SceneLifetimeSnapshot` diagnostics (generation, role, stage, active leases/callbacks, root categories and resource counters), bounded and disabled by default.

## 5.3 LIFE-02: Post-match commit owner before unbinding

1. Audit exact data read after `EndScene`, especially `MatchStart.AfterMatch`, `GameState.CommitSave`, `StorySave`, player results, profile/progression, replay/killcam result and lobby return. Write source-linked tests that prove the existing order and saved bytes.
2. Introduce a narrow **post-match commit handoff** (`PostMatchCommitContext` or equivalent) that captures exactly the data that legitimately needs to survive visual scene teardown. Avoid cloning entire `Scene` or introducing a giant mutable `MatchContext` merely for one save call.
3. Give the handoff an explicit state machine, e.g. `Active -> Ending -> CommitPending -> Committed/Failed -> Released`; ensure retries/failure do not double-award progression or lose a pending save. Owned GL teardown and save commit can be ordered independently as long as the original save behavior is identical.
4. Migrate one supported exit path at a time: offline rematch, return to lobby, persistent lobby match end, server disconnect, match rotation, replay lab return, Studio playtest exit and full application shutdown.
5. Only **after** save ownership no longer depends on the dead scene may compatibility facade references be conditionally unbound. If a legitimate callback still requires the old world, keep that explicit owner alive until completion; do not null a global early and assume success.

**Acceptance:** identical before/after save data, progress, result screens, match sequence, player preferences and replay metadata, with power interruption/error cases and no duplicate save/award.

## 5.4 LIFE-03: Scope compatibility bindings and asynchronous callbacks

1. Create a small, explicit foreground scene binding/lease with a monotonically increasing **local scene generation** (not a network protocol/replay field). Bind `GameState.Current`, `PlayerEntity.LegacyRegistry`, and `Rng.Current` only at documented activation/foreground-switch boundaries. Use a generation check before unbinding so an old scene cannot clear a newer scene's facade.
2. Route new gameplay-side code to `scene.GameState`, `scene.Players`, `scene.Random` and `scene.Services` rather than global facades where possible. Keep old facades as isolated adapters for legacy source paths. Avoid making each simulation step allocate a wrapper or run reflection.
3. Replica Studio scenes should never overwrite the foreground facade as a side effect of merely opening or seeking. Any intentional foreground transition, such as `BeginReplayLab`, must capture/release its binding explicitly and have a rollback path.
4. Fence async completion by owner-generation and cancellation for map/prewarm, thumbnails, background asset loads, audio, export and UI callbacks. Older completions cannot publish a texture/map/menu state into a replacement scene. Native GL deletion stays on the owning context thread; no finalizer performs GL calls.
5. Isolate context-independent scene data from context-bound render and audio resources with a small lifecycle contract rather than adding unnecessary project/assembly boundaries. Prefer existing `SceneServices`, `ScenePlayerRegistry`, and `SceneGameState` when they already express ownership.
6. If diagnostic evidence shows a true retained root, remove that **specific** reference after its owner has released it. Otherwise preserve the bounded foreground behavior and report no leak fix.

## 5.5 LIFE-04: Regression campaign and scope-limited cleanup

Include:

- Two separate live scenes and a detached replica existing at once; old callbacks/queries cannot mutate new state. Use genuine nested collision workspace leases and proper disposal.
- Ten/100/500 sequential scene loads with varying maps and all seven hunters; repeated paused/unpaused, respawn, match end, menu, lobby and network reconnect.
- Rendered Replay Studio seek/clip and map/editor open/close; audio load and cleanup; HUD/killcam/file export; custom model/texture and map rotation.
- Inject failure during player/room initialization, shader compile, network session spawn, audio close and save commit. Ensure ownership is correctly rolled back and no success is published early.
- After final **eligible** owner release, make one diagnostic-only liveness/GC-root observation. No persistent growth in old scene/player/renderer objects. Distinguish normal bounded cache retention from leaked scene instances.
- End-to-end frame, movement, combat/health, replay hashes, save/setting bytes, GL color/depth and native resource lifecycle parity.

Do not turn this into `NetSession -> MatchContext` in the same PR. A later network-session instance refactor requires an independent need, profiling, authority/replay equivalence and separate design review.

---

# 6. Shared validation, performance and quality gates

## 6.1 Existing commands to reuse

These are existing interfaces in the reviewed tree; exact arguments and asset prerequisites must be checked on the agent's target checkout:

```bash
# Source/build checks
dotnet build src/MphRead/MphRead.csproj -c Release
dotnet build tools/native-render-check -c Release
dotnet build tools/nettest -c Release
bash tools/check-opengl-only.sh
git diff --check

# Existing rendered performance suite (pass the built ProjectPrime executable)
bash tools/run-perf-suite.sh ./ProjectPrime perf-results
python3 tools/compare-perf.py baseline.json candidate.json

# Existing native renderer, graphics profiling and resource tests
# Requires a prepared game-file installation and appropriate graphical host.
dotnet tools/native-render-check/bin/Release/net10.0/native-render-check.dll \
  -room 'MP3 PROVING GROUND' -players 8 -seconds 25 -hz -1 \
  -size 1920x1080 -shadows High -glprofile -glgpu \
  -output /tmp/prime-render-qa.json

# Existing network/proof, integrity and transport checks
dotnet run --project tools/nettest -c Release -- --architecture
dotnet run --project tools/nettest -c Release -- --shot-source-journal
dotnet run --project tools/nettest -c Release -- --netcode-performance
dotnet run --project tools/nettest -c Release -- --transport-stress
dotnet run --project tools/nettest -c Release -- --transport-profile-json /tmp/transport-profile.json
```

> **Note:** For P0 allocation/GL context campaigns, follow the existing `tools/native-render-check/README.md` runner flags and controlled user-data setup. Proposed new flags/switches in this plan are *design suggestions*, not claims that those flags already exist. The example native renderer program currently checks for macOS or Windows; validate Linux/GLES with the appropriate platform harnesses rather than claiming this command supports them.

## 6.2 Regression matrix

| Area | Minimum acceptance |
|---|---|
| Build/platform | Desktop Win/Linux/macOS, dedicated server, Android Release/AOT; maintain existing signing/package checks |
| Protocol | Exact actual protocol, handshake, packet-size, malformed input, 2/4/8-client impairment, once-only award/payment |
| Simulation | Fixed 60 Hz, zero unexplained dropped steps or stalls, correct hunter/morphball/turret/weapon semantics |
| Replay | Old replay adapters, new grant capabilities, deterministic gameplay hashes, seek/clip/checkpoint compatibility |
| GL rendering | Same owner-order, exact world/HUD RGBA and float-depth, shadow/pickup/outline/solo-vs-one-bot parity |
| GPU lifecycle | GL program, shader, list, VBO/VAO, texture, framebuffer, context owner retirement and diagnostic native groups |
| Audio/Studio | Music/SFX reader safety, Replay Studio/Map Studio resource close/reopen, actual file/video export/cancel |
| Performance | Repeated p50/p95/p99/p99.9 render intervals, owner CPU time, GL submissions, bytes/upload, queue/step times, GC and allocation counters |
| Security/robustness | Forged grant flags, stale room/life, duplicate claims, invalid frames/coords, queue caps, malformed replay, no token disclosure |

Do not compare unrelated hardware runs as if they were A/B. Profiled runs are for attribution; **unprofiled matched repeated runs** are required for FPS claims. GPU timer unsupported = `UNVERIFIED`. Use the project's existing performance comparator and retain stricter optional gates for new runtime defaults.

## 6.3 Per-PR reporting requirements

Every PR description must include:

```markdown
## Hypothesis and scope
## Parent/base and exact source/binary identity
## Changed files and component ownership
## Baseline/reproduction (including negative controls)
## Implementation and fallback/rollback
## Compatibility impact: wire, replay, saves, gameplay, settings, assets
## Tests: local + exact-head hosted CI
## A/B: full samples, median and p95/p99/p99.9; GPU/managed/native where available
## Fidelity: world/HUD/depth/stencil, replay hashes, server authority
## Missing or inconclusive gates (NOT_RUN / UNVERIFIED / FAIL)
## Remaining risk and next dependency
```

## 6.4 Program completion criteria

- **P0 AGX:** either demonstrate a reproducible bounded correction with actual allocation-group evidence, or provide a precise driver-origin/remaining-risk conclusion. Never mark boundedness PASS without increasing-load evidence.
- **P1 GL:** ship only proven CPU/render work reductions with full parity and measured frametime improvement; discard or leave gated unsuccessful candidates.
- **P1 NET:** authority-verified original-source grant controls the valid Double multiplier for all declared supported damage paths, with exact-once payment, historical replay compatibility and meaningful live impairment coverage.
- **P2 LIFE:** correct post-match save handoff and scene-owner release, explicit foreground/replica boundaries and no demonstrated old-scene retention after eligible cleanup.
- Final combined `main` CI and cross-feature end-to-end acceptance pass without changed settings, protocol or renderer semantics unless specifically authorized by that phase.

## 7. Final instruction for the implementing AI agent

**Implement this plan, rather than performing another generic code audit.** Begin at AGX-01; establish immutable evidence, then progress through the described dependency graph. Implement experimental changes only when the preceding evidence supports them; retain negative findings and do not force a performance win. Parallelize only independent scopes. Preserve all completed consolidation work and deliver small, testable PRs with exact-head CI, metrics, source contracts and rollback instructions. If a P0 driver-memory hypothesis remains unprovable or uncontrollable in the application, report it with a minimal reproduction and safe mitigations rather than claiming a nonexistent fix.
