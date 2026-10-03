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
quality. World/material replacements use a separate scene-wide admission budget:
desktop Low/Medium/High/Ultra allow 256/512/1024/3072 MiB, while Android allows
128/256/384/768 MiB. Automatic resolves to High on desktop and Medium on Android.
The budget applies only to incremental authored HD residency; native cartridge
textures remain the stable fallback. The scene never evicts an already-admitted
world material mid-match: once the budget is full, later HD channels remain on
their native representation until a quality/replacement refresh releases and
re-evaluates residency. Existing world texture bindings retain their engine-owned
IDs during quality changes, so animation and material references remain valid.

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


### Progressive authored texture promotion

File-backed and immutable package-backed HD replacements no longer block
`InitTextures` on desktop or Android. The native cartridge texture is uploaded
first and remains valid at the same binding ID. Package-backed requests retain only dimensions and
identity while queued; their encoded entry bytes are read lazily by the decode
worker, so a large `.ppmap` does not become an in-memory compressed-texture
cache just because its materials were discovered.
Desktop runs at most two ordinary HD decode workers; Android uses one worker to
leave CPU/memory headroom for gameplay. The draw thread promotes at most one
prepared GPU image per frame on either platform. Source images whose raw RGBA
decode exceeds 96 MiB are serialized because desktop STB owns its
native decode while the managed copy/downscale is produced; this prevents two
large 5K–8K authoring images from overlapping their peak transient allocations.
Albedo is queued first; normal/material/emissive companions follow only after
albedo succeeds.
Quality, sampling, material-revision and binding-version checks discard stale
work after a setting change, pack reload or texture release. Android uses its
platform BitmapFactory decoder on the worker and still performs every GL/WebGPU
upload on the render thread.

This is intentionally a bounded streaming path rather than an eager whole-pack
predecode. Desktop PNG/JPEG sources that exceed the selected runtime cap now
resample directly from STB's native decoded surface into the final managed
texture, avoiding the previous second full-resolution RGBA copy. Large source
decodes above the scheduler threshold are also serialized. An Ultra 8192x8192
RGBA channel can still occupy 256 MiB for its final pixels before mipmaps, so
whole-pack eager decode remains deliberately avoided.


### World/material residency admission

The scene accounts albedo, normal, material and emissive authored replacements by
their actual post-quality RGBA8+mipmap footprint. Admission is tied to texture
ownership, so disabling Advanced Materials, disabling HD replacements, changing
quality, reloading material packs or unloading a scene returns the corresponding
bytes immediately. Progressive worker decodes do not reserve VRAM until their
prepared image reaches the render thread; if the budget is already full, the
prepared image is discarded and the binding keeps its native cartridge texture.

This is deliberately admission control rather than LRU eviction. Stable bindings
and stable presentation take priority over maximizing HD coverage by swapping
materials in and out during gameplay.
## KTX2 / Basis Universal GPU compression

Authored HD textures may also use `.ktx2`. Basis Universal ETC1S/UASTC payloads
are transcoded once on the existing background decode workers and promoted through
the same bounded streaming and VRAM-admission path as PNG/JPEG/TGA assets.

When the modern renderer is active, Project Prime requests the adapter's WebGPU
texture-compression features and chooses a portable native target:

- desktop DX12/Vulkan/Metal prefers BC7 when available;
- Android Vulkan prefers ASTC 4x4, then ETC2 RGBA8;
- other supported adapters fall through BC7 / ASTC / ETC2 capability order;
- legacy OpenGL/OpenGL ES or a device without a usable compressed feature
  transcodes Basis to RGBA8 and uses the existing upload path.

The complete KTX2 mip chain is retained and uploaded directly. Project Prime does
not generate block-compressed mips at runtime. If the authored image exceeds the
active texture-quality dimension cap, Basis is instead transcoded to RGBA8 and
downscaled by the existing quality policy, so KTX2 never bypasses user/device
resolution limits.

The managed bindings are provided by Ktx2.NET 1.0.5. Windows x64 and Linux x64
use its runtime assets. macOS and Android package a repository-built libktx
pinned to KTX-Software v4.4.2 through `tools/ktx/build-native.sh`.
