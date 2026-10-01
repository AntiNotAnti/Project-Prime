# Material pack authoring

The headless commands require no extracted game assets:

```
ProjectPrime -materials inspect texture-packs/default
ProjectPrime -materials validate texture-packs/default
ProjectPrime -materials inventory inventory.json
ProjectPrime -materials starter inventory.json texture-packs/MyPack
```

The runtime currently loads `texture-packs/default` beside the desktop application, or beneath Android’s initialized writable launcher directory. Other folders can be authored/validated and selected by placing their contents there. Existing five legacy filename variants remain supported when a manifest entry is absent. An explicit empty entry uses the original game texture. Missing/corrupt optional maps produce warnings and disable only that channel; unsafe references, duplicate identities and unknown formats reject the manifest. Invalid manifests are ignored at runtime while the safe legacy filename path remains available; validation still reports the error. PNG is the supported image format.

Inventory exports bounded observations retained during scene texture uploads (or native material inspection), saved when a scene closes normally to the user-local `ProjectPrime/material-inventory.json`. It contains keys, original dimensions and model names, never image content or guessed usage. It is a record of models observed in the process that last saved it, not a complete cartridge or map catalog. If no inventory exists the command reports that fact. Starter generation refuses to overwrite an existing manifest and leaves every assignment empty.

Stable keys use actual loader metadata:

- `model/<encoded-name>/texture/<id>/palette/<id>/recolor/<id>` for ordinary models.
- `room/<encoded-RoomMetadata.Name>/texture/...` for native rooms and legacy custom rooms without a MapId.
- `map/<existing-MapId-N-format>/texture/...` for community/generated rooms with a persistent MapId.
- `map/<MapId-N-format>/material/<MapMaterial.Id-N-format>/recolor/<id>` for authored materials, including portable `.tex` images and borrowed source textures.
- `effect/model/<encoded-particle-model-name>/texture/...` for the actual shared model loaded by particle definitions. This intentionally identifies a shared asset, not individual effect instances that share its texture binding.

Names are invariant lowercase, and punctuation is encoded without collisions. Scene model copies retain their presentation identity. Runtime metadata carries the existing community MapId without changing serialized map definitions, compiled outputs, or content/package hash rules. Texture/palette slots are the actual observed slots in that asset version. Room/community scoped entries win over generic model entries. Shared effect/model aliases follow an explicit generic model entry, and are resolved regardless of effect-load order so reuse of an existing texture binding stays deterministic. Actual particle loading records the effect alias in inventory. Older model manifests and all five legacy filename candidates remain fallback paths.

Map Studio's material browser exposes stable authored map/material keys, Browse/Clear controls, and downsampled channel images with channel semantics. Selected images are validated and copied into the local default pack under content-addressed names; manifest writes are atomic. Edited thumbnails are disposed promptly. These are local presentation pack changes, separate from map document undo/save and community packages. The viewport feeds normal/specular/roughness/emissive maps into the same material shader as gameplay, under fixed studio lighting. It computes face normals and preserves the original UV coordinate dimensions when albedo resolution changes. Material edits refresh textures and release old companion textures while retaining mesh geometry.

Compiled/community metadata carries the existing authored material GUIDs in memory,
without changing package bytes or content hashes. The runtime matches the compiler's
material slot order (including imported texture-pack offsets) and verifies names
before applying provenance; stale/mismatched metadata retains legacy lookup. Reordering
materials does not change their keys. Authored surfaces receive separate renderer
bindings even when compilation deduplicates their texture/palette pair, so clearing
one assignment cannot override another surface. Binding reload and scene teardown
retain/release these resources alongside ordinary textures. An explicit authored assignment costs one extra
texture binding per textured authored material/recolor; disabled/absent authored assignments
keep the original allocation. Pack reload adds newly assigned bindings. The original pair bindings
remain available for legacy consumers and animation fallbacks.

The editor resolves authored assignments directly, without guessing compiled texture
slots. Existing native source previews remain fallback when no authored assignment
exists. Imported architecture outside the authored material table and per-effect-instance
overrides continue to use observed asset keys; shared effect assets intentionally
remain shared. Packs remain local presentation overrides separate from community packages.

Rendering uses one resolved map contract before the existing `GraphicsApi` backend dispatch: normal XYZ, specular in red and roughness in green, emissive RGB. Android uses the same PNG header/chunk/dimension validation, the OS BitmapFactory decoder (unscaled, unpremultiplied), and explicit ARGB-to-RGBA conversion before the shared upload path. The editor albedo decoder uses that same platform-safe byte decoder. It uses the writable pack root and does not load the desktop-only Stb native dependency; physical Android acceptance remains pending. Pack reload changes the renderer material revision and retains main texture bindings while replacing their companion resources. Partial companion allocation failures release all earlier allocations.

The local synthetic pixel suite now verifies the shared material shader on actual OpenGL, Metal, and Vulkan. Broad scene appearance and unavailable hardware still require acceptance; this bounded suite does not prove every lighting/material combination.

Run synthetic content-free checks:

```
dotnet run --project tools/material-pack/material-pack.csproj -c Release
# Optional real OpenGL context; no cartridge data required:
dotnet run --project tools/material-pack/material-pack.csproj -c Release -- --gpu
```

The GPU check runs 12 success/failure upload cycles plus a partial-allocation fault (38 allocated / 38 released texture handles), and four lit editor refreshes. It asserts all material shader channels are enabled, the editor scope restores, only one mesh is uploaded, old map textures retire, and scene teardown releases the final set. A synthetic runtime model then proves two authored materials sharing texels retain one original binding when disabled, acquire independent red/original-white textures after a manifest reload, and release every owned texture at teardown.

Limits: 2 MiB manifest, 8,192 materials, 32 MiB per image, 8,192 maximum dimension, 16 million pixels per image, 256 MiB pack, 32,768 filesystem entries. PNG chunk checksums and full decode are validated. Absolute/traversal/backslash paths and linked pack assets are rejected. The local pack should not be mutated concurrently while validation or upload is in progress.


Synthetic cross-backend pixel checks (no game assets):

```
dotnet run --project tools/material-pack -c Release -- --pixels opengl /tmp/material-opengl.json
dotnet run --project tools/material-pack -c Release -- --pixels metal /tmp/material-metal.json
dotnet run --project tools/material-pack -c Release -- --compare /tmp/material-opengl.json /tmp/material-metal.json
```

The Metal run requires the existing `libwgpu_native.dylib` runtime beside the tool's
output DLL (or in its normal native library search path). The test asserts that the
requested backend is active; a fallback is a failure. It renders the production
Scene editor material path into a synthetic 96×96 RGBA/depth target and reads real
GPU pixels. Alpha is checked in that RGBA target because normal scene capture uses
RGB and cannot preserve alpha evidence. The editor disables alpha blending/testing,
so the alpha checks prove channel preservation, not gameplay cutout/blending policy.

Measured on 2026-10-01, Apple M4 Pro: OpenGL reports `2.1 Metal - 91.7`; the modern
path reports `WebGPU 1.0 / Metal`. All 12 fixtures pass their semantic assertions:
flat normal, tilted normal, glossy/rough specular, green emission, zero/half alpha,
nearest/linear UV sampling, repeated UVs, and minified checker with mipmaps off/on.
Representative RGB samples were flat `(61,61,61)`, tilted normal `(67,67,67)`,
rough specular `(73,73,73)`, and green emission `(61,157,61)` on both APIs.
Alpha readback was exactly 0 and 128. The mipmap sample changed from 184 to 134.

The comparison checks a 56×56 interior containing texel transitions and minified
samples, with a maximum allowed error of 1/255 per channel. **Measured interior
error was zero for every fixture**, and alpha matched exactly. Full-frame differences
were 156 RGB components (52 pixels), maximum 28/255, at background grid coverage
edges outside the tested material surface; these are reported rather than hidden.
JSON outputs retain complete RGB pixels and backend metadata for independent review.
The same 12 fixtures also passed on actual `WebGPU 1.0 / Vulkan (MoltenVK)`
with zero interior error against OpenGL and exact alpha. Vulkan used the packaged
MoltenVK loader/ICD and native runtime SHA-256
`5bfdbb661cca97d80a85b854d394d43c530a5b18bf49d4184e2555f3bf39ef60`.
Use `--pixels vulkan /tmp/material-vulkan.json` and the same comparison command;
install the native runtime in both the output root and `runtimes/osx-arm64/native`
after building, with the MoltenVK loader and ICD beside the output DLL.
Windows DirectX/Vulkan and physical Android pixel acceptance remain unverified.

Observed inventories retain up to eight distinct model and map labels per material
(256 characters per label), and persist a deterministic bounded subset below 2 MiB.
They describe observed usage, not a complete installed-content census: omitted or
unobserved materials are never labeled unused. Legacy single-model inventories
remain readable. Map Studio counts effective mesh-face assignments and imported
model sources; Select usages frames those actual objects.

The extended suite contains 22 fixtures. Ten additional fixtures replay the actual
shared `Scene.RenderItem` used by gameplay, with explicit production sampler,
`SrcAlpha / OneMinusSrcAlpha` blend, and `AlphaFunction.Equal(1)` opaque-pass state.
They verify repeat, mirror and clamp at out-of-range positive/negative UVs, including
expected red/green texel selection. Transparent red over opaque blue produces
`(0,0,255)`, `(128,0,127)`, and `(255,0,0)` at alpha 0, 128, and 255;
the destination alpha also matches the source-alpha blend equation. Half-alpha
fragments fail the opaque pass and full-alpha fragments pass it.

On the same local OpenGL, Metal and Vulkan runtimes, all ten new fixtures matched
exactly across the **entire frame**, while all 22 material interiors matched exactly.
The original editor alpha fixtures continue to prove unblended channel preservation.
The replay tests exercise shared gameplay draw/blend state, not complete scene
sorting, stencil polygon ordering, or multi-surface transparency interactions.
No shipping renderer hooks or alternate shader implementations are introduced.
