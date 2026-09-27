# Cosmetics implementation

Implemented September 27, 2026. Entry point: **Hunter License → Customization**.
Choose a hunter, select Skin / Armor Effect / Death Presentation, and press Equip.
The real hunter preview rotates, zooms, and can play/reset the selected death.

## Delivered

- Stable string catalog and per-hunter local loadouts. Default, Obsidian, and Alimbic for all seven hunters; 16 armor effects plus None; eight death choices including Classic. Built-in skins are procedural material treatments, with optional authored texture channels supported separately.
- Local-first atomic persistence, pending synchronization, stale-response protection, and authenticated Hunter License writes. The `hunter-cosmetics` Supabase function is deployed; its server-owned catalog rejects unknown and wrong-hunter selections. All initial items are unlocked.
- Shared forward, GLES, and deferred shader source, separate skin/material overrides, first-person reduction, alt forms, Weavel turret, live model previews, and normal player paths used by spectator/replay/killcam.
- Gameplay status and team visibility take priority. Team games retain native skin palettes. Show Custom Cosmetics hides presentation without changing replicated selections; quality Off retains skins and uses native deaths.
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
- Cosmetics checks: 64 passed, including persistence, stale synchronization, compact IDs, occupant fencing, late-join state, replay decoder restoration, status priority, quality fallback, death appendix round-trip, and respawn cancellation.
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
