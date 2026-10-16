# PR-06: Material/texture-coordinate blending, preview pilot

**Rollout:** OFF by default. Independent diagnostic toggles:
`PROJECT_PRIME_MATERIAL_ANIMATION_BLEND=1` and `PROJECT_PRIME_TEXCOORD_ANIMATION_BLEND=1`.

## Implemented

- `Model.AnimateMaterials` has an internal sampling overload that reads the native current and next frame from the same original animation lookup; RGB/ambient/specular/alpha channels interpolate linearly in existing units, without altering frame counters. The original public API remains unchanged.
- `Model.AnimateTexcoords` has an equivalent internal overload. Scroll and scale are linear; rotation follows the shortest angular arc and preserves the existing UV transform order.
- The HunterPreview draw pass checks each channel's actual animation slot independently, samples from its owned preview clock, temporarily applies material colors/alpha, submits draw items and restores the canonical materials and material bindings in `finally`.
- `EntityBase.GetTexcoordMatrix` receives a fractional UV sample only if the draw scope explicitly binds the model-instance identity. The previous context is restored after submission.
- `AnimateTextures` is intentionally untouched. Frame swaps change discrete texture IDs; blending IDs would create invalid textures.

## Safety / deferrals

- This pilot does not yet enable fractional material/UV curves for regular world entities, because shared alpha and boss scripts may be gameplay-significant.
- No change to protocol 56, map formats, replay format, animation ticks, palette ownership, OpenGL shader implementation, or default settings.
- Android GLES and physical HUD/depth/transparent/glass captures are not available from connector-driven implementation; do **not** treat asset-free tests as full native acceptance.
- The new internal overloads leave all original callsites byte-for-byte semantically identical when no interpolation context is active.

## Native acceptance before enabling

1. Run `nettest --animation-sampling` and the full workflow; check no protocol, map, or campaign regressions.
2. Compare all seven Hunters' previews at 60/120/144/240 Hz (including custom LODs, paletted skins, particle overlays, alpha/death), with the flag OFF and ON.
3. Capture translucent room materials, fog, portals, weapon energy and toon outlines with original OpenGL and Android GLES at fixed matching camera positions.
4. Assert material.CurrentAlpha/CurrentDiffuse and model texture bindings after each draw exactly match the canonical no-blend state.
5. Record p95/p99, CPU/GPU, allocations and GL error A/B in 4x240-frame windows, not synthetic claims.
