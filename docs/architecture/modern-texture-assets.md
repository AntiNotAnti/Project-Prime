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


## Runtime loading performance

Material-pack discovery validates path containment, byte/dimension limits and the
PNG header without decoding every referenced image. Strict authoring/import
validation still checks PNG chunks/CRC and performs a real decode. Runtime pixel
decode is therefore paid once, when the asset is actually prepared for GPU
residency, instead of once during manifest discovery and again during upload.

On the modern WebGPU backends, a generated mip chain reuses one staging texture
for all levels of a source image. This preserves the staged copy path used to
avoid Vulkan subresource corruption while removing per-mip native texture
allocation/destruction churn, which is especially costly for 4K/8K assets.


### Progressive desktop promotion

File-backed desktop HD replacements no longer block `InitTextures`. The native
cartridge texture is uploaded first and remains valid at the same binding ID.
At most two ordinary HD channels decode concurrently on worker threads, and the
draw thread promotes at most one prepared GPU image per frame. Source images
whose raw RGBA decode exceeds 96 MiB are serialized because desktop STB owns its
native decode while the managed copy/downscale is produced; this prevents two
large 5K–8K authoring images from overlapping their peak transient allocations.
Albedo is queued first; normal/material/emissive companions follow only after
albedo succeeds.
Quality, sampling, material-revision and binding-version checks discard stale
work after a setting change, pack reload or texture release. Android keeps the
existing synchronous path for now.

This is intentionally a bounded streaming path rather than an eager whole-pack
predecode: an Ultra 8192x8192 RGBA channel can occupy 256 MiB before mipmaps, so
decoding an entire material pack concurrently would trade startup time for a
large transient-memory spike.
