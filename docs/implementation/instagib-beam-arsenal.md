# Project Prime Insta-Gib beam arsenal (implementation slice)

Branch: `feature/beam-fx-20261019`. All cosmetic behavior is limited to multiplayer Insta-Gib Imperialist presentation. Damage, shot origins, hit claims, server authority, collision, recoil and respawn are unchanged.

## Catalog and visual grammar

| Wire ID | Style | Readable geometry |
| --- | --- | --- |
| 0 | Ion Fracture 2.0 | Razor white core, saturated slot sheath, traveling X knot, shear shards, hex impact |
| 1 | Void Rift | Violet-and-dark split channels, retracting angular fragments, square impact |
| 2 | Quantum Split | Cyan main rail, magenta divergent echoes, pixel facets |
| 3 | Alimbic Resonance | Gold-white lance, traveling hex containment glyphs |
| 4 | Omega Pulse | Crimson rail, sequential compression rings |
| 5 | Arc Disruptor | White-violet straight rail with short decorative side arcs |
| 6 | Solar Spear | Orange-gold plasma, branching fins, radiant impact |
| 7 | Cryo Shatter | Ice-blue rail, staggered crystalline facets |
| 8 | Phantom Trace | Thin teal rail, tapered low-opacity holographic afterimages |

Each style has an authored palette and eight optional color presets plus Original. The new default replaces only Insta-Gib beam presentation: other Imperialist modes retain their native effect.

## Wire/replay compatibility

`CosmeticStatePacket.Size` remains **24**. The existing `Death` 16-bit cosmetic-only field is reinterpreted as:

- bits 0–7: death presentation ID (currently 0–7)
- bits 8–11: beam style ID (0–8)
- bits 12–15: optional color preset (0–8)

The all-zero upper byte exactly preserves existing cosmetic packets and historical replays. Unknown style, death and dye IDs sanitize before use. New clients display their own selected styles and the corresponding accepted style for other slots, late joins, spectators and replay packets. Older clients at the same protocol number degrade an unrecognized combined death ID to Classic while retaining skins/armor. Any future expansion past 15 beam styles, 15 tints or 255 death styles **requires a new packet layout and protocol revision**, not reusing these bits.

The authenticated Hunter License HTTP function currently accepts only three fields (skin, armor, death). Beam selection is intentionally **local-first**: stored with the existing atomic cosmetics file, carried live via the existing CosmeticState packet, and retained when the three synced fields are refreshed. Beam-only changes do not mark unrelated cloud fields pending and do not require account authentication. Syncing beams across devices requires a separate allowlisted backend migration.

## Renderer lifetime and performance

`InstagibIonFracture` is scene-owned and allocated lazily only when needed. At most 24 shot trails live for 0.82 seconds. Drawing is driven by scene time and shot IDs; duplicate replicated fire records and replay seeks cannot create persistent trails. No render-time RNG, new texture, new render backend or shader is introduced. All geometry ends at the actual collision point in the existing depth-tested transparent world pass. Reduced motion preserves the core while disabling accents; reduced transparency removes atmospheric halo. Cosmetic quality Off draws the precise core only.

All added ribbons use pooled `TrailMulti` vertices. The busiest effects are capped at 3 shafts plus up to 18 small accent facets/rings at High; lower quality omits detail. The scene also prioritizes recently fired nearby shots and caps fully decorated trails at 12 (High), 8 (Medium) or 4 (Low) each frame. Remaining trails keep their canonical center beam; accents degrade with view distance, never changing collision endpoints.

## UI paths

- Avalonia Hunter License → Customization: nine selectable beam cards with generated local swatches, and a color selector.
- Native RmlUi Hunter picker: cycle beam style/color using the shared, explicit Equip draft controller.
- Existing skins, armor and death selection remain unchanged.

## Acceptance

- `dotnet run --project tools/nettest/nettest.csproj -c Release -- --instagib-ion-fracture`
- `dotnet run --project src/MphRead/MphRead.csproj -c Release -- -cosmeticscheck` (or use the assembled app executable)
- `dotnet run --project tools/rmlui-hunter-check -c Release`
- `dotnet build src/MphRead.sln -c Release`

Device work outstanding: GPU screenshots for all nine effects from muzzle/third person, 8-player spawn spam, wall/corridor occlusion, 30/60/144/240Hz frame pacing, Android GLES/context loss and thermal limits. Do not claim these without measurements. Original spatial Imperialist sound remains in use. Each beam now has original, distance-attenuated firing and impact accents in the game's existing SoundFlow output, with at most two bounded cached voices per style. Wall and player impacts use the same per-style impact timbre with different volume, always layered quietly underneath native spatial audio. Those generated accents are not authored sample-bank replacements or fully spatialized 3D sounds; real 3D spatial audio remains provided by the original weapon. Actual transient dynamic lights still require separate GPU acceptance. The launcher preview uses a schematic swatch, not a live OpenGL shot. World impacts include a short depth-tested prism flash and shallow surface sparks; these are cosmetic geometry, **not** physically illuminating lights.

## Reference-quality rail upgrade (2026-10-19)

The original hairline effect is replaced by a deliberately thicker,
soft-edged *textured* rail based on broad visual elements of the current
Fruity-Prime `InstagibRails` design (reference
`src/MphRead.Native/Mods/Render/InstagibRails.cpp`, master
`12ff677b9700bc8dabde37eb9a3fdd10c62b87b6`).
This is a distinct managed implementation using Project Prime's renderer.

- A persistent 0.9-second central lance uses the **already-loaded native
  `trail` material**, depth tested and scene-owned. No standalone image/shader.
- Three layers with authored maximum halfwidths: 0.24 softened halo,
  0.11 saturated band and 0.06 luminous pale core. Each tapers to 12% width at
  the muzzle to preserve first-person visibility.
- The standard Ion Fracture 2.0 adds **two phase-offset luminous filaments**
  around the central beam, with predictable temporal rotation and a 36-unit
  reach, 73 samples on High or 43 on Medium. Existing crystalline X-knots and
  impact presentation remain Project Prime-specific.
- All other styles also benefit from improved textured layering without losing
  their distinct silhouettes, color selection or impact sounds.
- Accessibility settings preserve a legible core while turning off broad
  transparency and animated accents. Headless and missing texture situations
  fall back to prior untextured ribbons.
- Texture binds are resolved using the scene's original material cache each
  frame. The effect does not own, allocate, release or share a GL texture ID.

Validation: `dotnet run --project tools/nettest/nettest.csproj -c Release -- --instagib-ion-fracture`
and the focused `Insta-Gib beam FX` workflow cover finite, bounded look
curves, phase opposition, deterministic offsets and muzzle taper. These
asset-free checks cannot prove native texture blending, HDR/color behavior,
wall occlusion or real-device 1v1 and 8-player performance; capture GPU
screenshots and benchmarks before considering the visual acceptance complete.

## Distinct animated signatures (2026-10-19)

The colored three-layer textured rail remains the aim-readable baseline, but
eight other styles now render **persistent, visually different** motion instead
of depending only on small first-frame accents:

| Style | Persistent geometry/movement |
| --- | --- |
| Ion Fracture 2.0 | Existing intertwined twin energy filaments and crystalline X-knot |
| Void Rift | Two continuously folding violet dimensional seams |
| Quantum Split | A pair of magenta phase echoes that split and converge |
| Alimbic Resonance | Faceted six-sided energy cage advancing in steps |
| Omega Pulse | Three traveling compression bulges along the rail |
| Arc Disruptor | Chopped opposite-polarity electric zig-zags |
| Solar Spear | Twin rotating flame tongues with pulsing billows |
| Cryo Shatter | Sharp triangular crystal lattice advancing in discrete facets |
| Phantom Trace | Two drifting, fading holographic afterimages |

Animation is scene-time driven and geometry is clamped within 0.6 world units
of the canonical trace. High/Medium draw two textured strands with 57/37
samples; Low still draws one 21-sample signature; Off and reduced-motion do
not animate decorative strands. The shared shot core, occlusion, hit endpoint,
network packet and replay identity remain unchanged.

Asset-free `--instagib-ion-fracture` acceptance checks all eight non-default
styles for bounded, deterministic, time-varying and pairwise distinct
geometry. Actual GPU screenshots and visual legibility under OpenGL/GLES still
require hardware acceptance.
