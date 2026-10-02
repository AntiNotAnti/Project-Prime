# Cosmetics implementation

Implemented September 27, 2026. Entry point: **Hunter License → Customization**.
Choose a hunter, select Skin / Armor Effect / Death Presentation, and press Equip.
The real hunter preview rotates, zooms, and can play/reset the selected death.

## Delivered

- Stable string catalog and per-hunter local loadouts. Default, Obsidian, and Alimbic for all seven hunters; 16 armor effects plus None; eight death choices including Classic. Built-in skins are procedural material treatments, with optional authored texture channels supported separately.
- Local-first atomic persistence, pending synchronization, stale-response protection, and authenticated Hunter License writes. The `hunter-cosmetics` Supabase function is deployed; its server-owned catalog rejects unknown and wrong-hunter selections. All initial items are unlocked.
- Shared forward, GLES, and deferred shader source, separate skin/material overrides, first-person reduction, alt forms, Weavel turret, live model previews, and normal player paths used by spectator/replay/killcam.
- Gameplay status and team visibility take priority. Team games preserve saturated native team panels while allowing the cosmetic treatment on neutral armor. Show Custom Cosmetics hides presentation without changing replicated selections; quality Off retains skins and uses native deaths.
- Scene-owned texture cache and embedded-resource loader. Optional missing maps fall back safely. Existing scene GPU ownership handles shutdown/context teardown.
- Pooled analytic particles, cached animated attachment points, distance LOD, match/slot/generation seeds, and bounded nearest lights. Desktop light cap 4 on High / 2 on Medium; Android cap 2. No first-person particle emitters.
- Authority-triggered procedural death presentation, stable effect selection during a death, respawn cancellation, custom alt-form death rendering, and bounded particles/lights. No collision, health, respawn, or camera rules change.
- Optional protocol-24 CosmeticState packet, server sanitization, occupant/match/authority/revision fencing, reconnect and roster resends. Existing Identify/Roster layouts are unchanged.
- Replay packet bootstrap, timeline recording, decoder checkpoints, and world-checkpoint cosmetic death appendix. The appendix uses world capsule version 3 without adding fields/types to the gameplay graph; previous capsule versions 1/2 remain readable against their existing gameplay contract. As before, unrelated gameplay-schema changes can still invalidate old world capsules.
- Catalog cards consume real rendered thumbnail caches. Normal preview captures selected entries lazily; the visual-check command below bakes the full 196-image set locally from installed game assets.
- Developer override and deterministic-time commands.

## Validation

- Desktop build: passed.
- Dedicated-server build: passed (isolated artifact directory).
- Cosmetics checks: 68 passed, including persistence, stale synchronization, compact IDs, occupant fencing, late-join state, replay decoder restoration, status priority, quality fallback, death appendix round-trip, and respawn cancellation.
- Replay format checks: 2,710 passed.
- Launcher UI suite: 17,959 passed; general launcher checks do not substitute for every customization interaction on a device.
- Network architecture suite: passed, including existing byte fixtures.
- Desktop OpenGL shader compilation/link check: passed for world/postprocess/HDR/deferred.
- Actual GL model capture: 196 previews generated (7 hunters × 3 skins, 17 armor choices, 8 deaths). Representative Obsidian, Inferno, and Quantum images visually inspected.
- Backend catalog tests and unauthenticated/invalid-session rejection: passed. Database schema and parameterized upsert verified with read-only SQL/EXPLAIN. A valid authenticated user's save was not exercised end-to-end.

## Remaining acceptance work

These are not claimed as verified by the implementation tests:

- Android compilation/device rendering and context-loss exercise: this machine lacks the Android .NET workload. Desktop GLES-compatible source and bundled resource wiring are implemented, but actual GLES/device behavior needs validation.
- Live two-client/late-join/reconnect and recorded killcam acceptance: cache/protocol tests pass, but a real multi-client session was not run.
- Full world replay/seek simulation: the existing coverage runner stalls generating TEST ARENA during startup, before reaching replay assertions. It was stopped; this is not a passing world simulation test. Its synthetic fixtures now include cosmetics.
- Sustained performance measurement on target desktop/Android hardware: budgets are enforced in code, not yet benchmarked on devices.
- Authored replacement PNGs, new audio recordings, and skeletal death animation assets are not bundled. Initial skins/effects use procedural rendering and existing particle primitives. Audio metadata is reserved; initial definitions have no extra sound cues. Restricted unlocks and authored animation sets remain the plan's future scope.

## Commands

From the repository root, after building `src/MphRead/MphRead.csproj`:

```sh
dotnet src/MphRead/bin/Debug/net10.0/ProjectPrime.dll -cosmeticscheck
dotnet src/MphRead/bin/Debug/net10.0/ProjectPrime.dll -replayformatcheck
dotnet src/MphRead/bin/Debug/net10.0/ProjectPrime.dll -cosmeticpreviewcheck /tmp/prime-cosmetics-previews
dotnet src/MphRead/bin/Debug/net10.0/ProjectPrime.dll -thumbnailwindowcheck
dotnet run --project tools/nettest -- --architecture
node supabase/functions/hunter-cosmetics/catalog.test.ts
```

Visual diagnostic overrides: `-cosmetic skin skin.samus.obsidian`,
`-cosmetic armor armor.inferno`, `-cosmetic death death.quantum`,
`-cosmetic clear`, and `-cosmetictime 0.5`. These do not change saved loadouts.

Texture layout and fallback rules: `src/MphRead/Assets/Cosmetics/README.md`.


## Visibility audit (September 27)

Fixed preview particles with zero-sized/uninitialized vertices; preview-only render
items and their rented particle buffers are now recycled separately from world
items. Cosmetic sprites explicitly tint the native sprite alpha shape instead of
inheriting its blue RGB. The preview sets vertex colors and its own billboard
orientation, and its camera leaves room for tall hunters and bursts.

Local appearance now follows slot ownership rather than camera mode, and uses
the equipped local loadout immediately while remote
players and replay replicas keep their network/recorded state. Identify precedes
the cosmetic announcement when switching hunters. Bright Skins only suppresses
cosmetics where the actual competitive override applies, so it no longer hides
the local arm cannon. Passive cloak/fades suppress cosmetic emission and optional
maps. Native ice/smoke/auxiliary player meshes are excluded from cosmetic shading.
None remains None at far LOD.

Custom death bodies can render through first-person/native HideModel paths while
an accepted death presentation is active. Body and particles use the captured
death position. Offline deaths are observed during simulation, independent of
whether a player was drawn. Dissolve reaches completion by HideBodyAt; preview
and world share surface parameters. Death durations and motion differ by effect;
respawn timing remains unchanged. Classic now has a visible preview burst.

Effects have stronger colored surface blending (avoiding additive clipping on
saturated armor), larger sprites without increasing particle-count budgets, and
distinct animation speeds. Rim shading uses the actual view direction rather than
a fixed world axis. Team-colored panels survive procedural skins. The
customization controls appear before the catalog, death choices auto-preview,
and Compare Native / Reset Loadout controls are available. Settings that hide
effects are explained on the page. Thumbnail cache v2 replaces stale visuals.

The real-model visual diagnostic asserts nondegenerate particle geometry and no
world-queue growth, in addition to capturing every catalog choice. It also
exercises the actual player material/death path with Bright Skins, cloak,
first-person/native-hidden death bodies, and respawn lifecycle checks.

## Hunter visibility and preview follow-up (2026-09-28)

Armor motes now use per-hunter biped and alternate-form presentation envelopes,
with larger sprites on Spire and smaller envelopes on Trace and compact forms.
These are visual-only values; collision and gameplay dimensions are unchanged.
Preview particles share the model's zoom and rotation. Camera framing gives
Trace's raised limbs and Spire's shoulders more room and backs away for narrow
panels. Changing hunters re-resolves the preview appearance even when the
loadout keys are unchanged.

Customization now distinguishes an unequipped preview from equipped/pending-sync
items and offers a looping death preview with a short living-pose interval.
Reset Preview stops the loop until another preview is requested. Armor and death
particle submission both enforce the master visibility and quality switches;
death particles use budgets of 0/6/12/18 for Off/Low/Medium/High. Armor retains its
existing maximum of 16 rather than increasing particle counts for visibility.

The diagnostics cover all seven actual player material/death paths, all hunters'
replicated loadouts across team/alternate-form/quality combinations, GPU particle
budgets and visibility toggles, and 600 looping death frames with restart/reset
and render-queue ownership assertions. This is automated path coverage, not a
claim of a completed two-client match or a GPU frame-time benchmark. Authored
skin assets, weapon/alternate-form preview modes, Android captures, and live
multiplayer visual acceptance remain follow-up work.

Validation for this follow-up: desktop build passed (20 warnings, zero errors),
299 cosmetics checks passed, 18,015 UI checks passed, and the OpenGL diagnostic
captured 196 catalog previews and passed the repeated-loop/toggle checks. Trace
and Spire captures were visually inspected after the camera adjustment.

## Authored artwork and model previews (2026-09-28)

Obsidian and Alimbic now ship 14 original transparent decal sheets (one of each
style per hunter), with technical panel markings, circuit inlays, and distinct
hunter insignia. These upgrade the existing skin IDs, so saved and replicated
loadouts remain compatible. They are authored UV decal treatments, not fully
hand-painted per-material atlases. The standard-library source generator and
asset workflow are documented in `tools/cosmetic-art/README.md`.

When no material-specific albedo exists, the renderer composites the bundled
sheet over locally extracted native pixels and preserves native alpha. It
caches the resulting texture and releases it through normal cosmetic texture
cleanup. The optional-art path falls back safely on loading failures. Existing
team-palette and critical-status suppression policies remain in force.

Hunter License now offers Hunter, Weapon, and Alternate Form preview modes.
They load the corresponding gameplay models and use their material contexts;
weapon effects use first-person intensity and omit armor particles. Non-biped
models are centered and sized from model bounds, with an idle-size adjustment
for Spire's expanded attack bounds. Drag and zoom remain available. Death preview
returns to Hunter mode, model switches initialize their own GPU resources, and
non-biped views cannot overwrite the hunter catalog thumbnails. Thumbnail cache
v3 invalidates images from before the authored artwork.

Validation: desktop build passed (20 warnings, zero errors), 299 cosmetics checks
and 18,025 UI checks passed. The OpenGL diagnostic captured 196 catalog previews
plus 63 native/authored model-mode comparisons, checked all 42 authored bindings,
and passed visibility/quality toggles and 600 death-preview frames. UI checks
exercise repeated model selection and License detach/re-entry. Weapon and
alternate-form captures were visually inspected; Spire's shell gap is also
present in the native model comparison. Android and live two-client visual
acceptance remain unverified.

## Expanded customization (2026-09-29)

Added Ceramic (panel seams), Circuit (traces and nodes), Tiger (warped stripes),
and Nebula (clouds and stars) for all seven hunters. Existing wire IDs stay intact;
new skin IDs are 15–42. Added Orbital, Double Helix, Warp Drive and Starfall armor
IDs 17–20. The catalog now offers seven skins per hunter and 20 armor effects plus None.

All armor effects have separate analytic trajectories. Pooled translucent ribbons
form arcs, rings, quills, crystal outlines and trails, with one sprite per trail.
Quality/distance gates remain in place; maximum submission is 16 sprites and 80
quads per visible hunter. Positions use the hunter/form envelope and shared preview
transform. No simulation state or RNG is consumed. Thumbnail cache is now v4.

Validation: desktop build, 14,745 cosmetics checks, endpoint catalog checks,
252 real-model catalog captures, 63 model-mode captures and preview quality/lifecycle
checks passed. Circuit, Inferno, Lightning and Orbital captures were visually inspected.
Device performance and live multiplayer acceptance remain unverified. The updated
online allowlist is checked in; deploying the hunter-cosmetics function is required
before the live service will accept the newly added keys.

## Material and motion polish (2026-09-30)

- Replaced per-segment armor quads with camera-facing continuous strips, each with
  a broad translucent halo and narrow bright core. Near High uses 12 subdivisions;
  other qualities use six. Maximum is 32 ribbon draws plus 16 sprite submissions,
  compared with the previous 80 quad draws. This is a submission-count improvement,
  not a measured frame-time claim.
- Orbit/Eclipse/Warp rings close at reduced particle budgets. Rising/falling effects
  fade through their cycle boundary; fire and storm trails no longer wrap halfway
  through a strip. Spike extension uses a smooth pulse.
- Skin finishes now have distinct metalness/roughness in the deferred pass, while
  optional authored specular maps retain priority. Forward rendering/previews use
  a restrained view-dependent sheen. Circuit traces carry moving energy; Nebula
  clouds and noise-based armor motion use continuous spatial noise.
- Death fall/collapse/ascension transforms use easing. These are presentation-only
  changes to existing poses, not new skeletal walk/run animation assets.
- Preview effects retain depth testing against the hunter but no longer write
  depth over subsequent transparent glow layers. Thumbnail cache is v5.
- Shared GLSL changes regenerated the World and DeferredPbr WGSL shaders.

Validation on this host: desktop Debug build (zero errors), 14,916 cosmetics checks,
Metal renderer window checks including six material-buffer readbacks, and 252
catalog captures / 63 model-mode captures / 600 looping death frames on each of
OpenGL and Metal. Orbital, Inferno and Circuit captures were inspected. Vulkan,
DX12, physical Android and gameplay performance were not measured for this change.


## Unified modern texture assets (2026-10-01)

Authored biped, first-person weapon, alternate-form and Halfturret skin channels
now use `ModernTextureAsset` and a scene-owned `TextureAssetManager` cache.
Existing material keys already identify authored map materials and
`effect/model/...` particle models, so HD world and FX replacements use the
same resolution policy without changing cosmetic, network or replay IDs. Quality
is Automatic, Low (1K), Medium (2K), High (4K) or Ultra (8K); transparent effect
assets deliberately top out one tier lower. Existing native assets remain the
fallback on missing, corrupt, unsupported or over-budget authored content.
