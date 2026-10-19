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

The authenticated Hunter License HTTP function currently accepts only three fields (skin, armor, death). Beam selection is intentionally **local-first**: stored with the existing atomic cosmetics file, carried live via the existing CosmeticState packet, and retained when the three synced fields are refreshed. Syncing beams across devices requires a separate allowlisted backend migration.

## Renderer lifetime and performance

`InstagibIonFracture` is scene-owned and allocated lazily only when needed. At most 24 shot trails live for 0.82 seconds. Drawing is driven by scene time and shot IDs; duplicate replicated fire records and replay seeks cannot create persistent trails. No render-time RNG, new texture, new render backend or shader is introduced. All geometry ends at the actual collision point in the existing depth-tested transparent world pass. Reduced motion preserves the core while disabling accents; reduced transparency removes atmospheric halo. Cosmetic quality Off draws the precise core only.

All added ribbons use pooled `TrailMulti` vertices. The busiest effects are capped at 3 shafts plus up to 18 small accent facets/rings at High; lower quality omits detail.

## UI paths

- Avalonia Hunter License → Customization: nine selectable beam cards with generated local swatches, and a color selector.
- Native RmlUi Hunter picker: cycle beam style/color using the shared, explicit Equip draft controller.
- Existing skins, armor and death selection remain unchanged.

## Acceptance

- `dotnet run --project tools/nettest/nettest.csproj -c Release -- --instagib-ion-fracture`
- `dotnet run --project src/MphRead/MphRead.csproj -c Release -- -cosmeticscheck` (or use the assembled app executable)
- `dotnet run --project tools/rmlui-hunter-check -c Release`
- `dotnet build src/MphRead.sln -c Release`

Device work outstanding: GPU screenshots for all nine effects from muzzle/third person, 8-player spawn spam, wall/corridor occlusion, 30/60/144/240Hz frame pacing, Android GLES/context loss and thermal limits. Do not claim these without measurements. Original recorded sounds remain in use; authored per-style sound assets and actual transient dynamic lights require separate audio/GPU asset acceptance. The launcher preview uses a schematic swatch, not a live OpenGL shot.
