# Modern texture assets

Project Prime keeps the native cartridge/FPTX texture path as the compatibility
fallback and layers authored HD material data above it.

`ModernTextureAsset` is the backend-independent decoded representation. It
accepts bounded RGBA source images up to 8192×8192, preserves aspect ratio while
downsampling, renormalizes tangent-space normal maps, and provides deterministic
GPU-memory estimates.

`TextureAssetManager` owns authored cosmetic residency and the shared quality
policy. The same decode/upload policy is also used by material-pack world
textures and effect-model replacements.

## Quality policy

| Tier | World / hunter / weapon / alt / turret | Effects |
| --- | ---: | ---: |
| Low | 1024 | 512 |
| Medium | 2048 | 1024 |
| High | 4096 | 2048 |
| Ultra | 8192 | 4096 |

Automatic selects Medium on Android and High on desktop. Every result is
additionally clamped to the graphics-device maximum. UI assets cap at 2048.

The manager uses RGBA8 uploads and accounts for the extra one-third memory of a
complete mip chain. Cosmetic residency budgets are conservative by platform and
quality. Existing world texture bindings retain their engine-owned IDs during
quality changes, so animation and material references remain valid.

## Identity and compatibility

No new network or replay texture payload exists. Existing stable cosmetic IDs,
authored map material GUIDs and `effect/model/...` material identities resolve
the local best-quality representation. Authored map channels and Q3 PNG/JPEG
source art can travel inside `.ppmap`; the package still carries FPTX/native
fallback data, so clients without HD art or with HD replacements disabled retain
the original material path.

The palette FPTX format is intentionally still accepted unchanged. This system
is the modern authored layer, not a destructive rewrite of native MPH texture
data. A future KTX2/Basis source codec can be added behind
`ModernTextureAsset` without changing renderer, map, cosmetic or FX call sites.
