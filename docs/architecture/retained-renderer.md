# Retained renderer and render graph migration

Project Prime's renderer is moving from the historical GL-style submission model to a
retained, backend-native world representation. This migration is deliberately staged:
OpenGL/OpenGL ES remain compatibility backends, while DX12/Vulkan/Metal continue to run
through the shared WebGPU renderer.

## Slice 1: retained geometry + explicit world graph

The first slice establishes the architecture without changing visual ordering.

### Persistent native geometry

Modern display lists already own immutable CPU vertex/index arrays after `EndList`.
Those arrays are now promoted immediately into persistent native WebGPU vertex/index
buffers when the list is finalized. The first visible frame therefore no longer owns
the upload/allocation cost for static model/room geometry.

Deletion still follows the existing display-list lifetime. Rebuilding or deleting a
list releases the corresponding persistent native buffers.

### Frame draw packets

`RetainedRenderWorld` captures the scene's opaque, decal and translucent submissions
into reusable `RetainedDrawPacket` arrays. Packet storage retains capacity between
frames and packet records are value types, so steady-state capture does not allocate
one object per draw.

Packets retain the original submission sequence. A material/state key is recorded for
instrumentation and later sorting, but this slice does not reorder any geometry.

### World render graph

The cartridge renderer's ordering-sensitive six world passes are now described by an
explicit `WorldRenderGraph`:

1. opaque color/depth/stencil
2. decals
3. translucent stencil marking
4. depth rebuild
5. translucent behind
6. translucent front

Each pass declares its color/depth/stencil reads and writes. The first slice keeps the
existing GL-compatible state transitions byte-for-byte in intent and executes the
same `RenderItem` draw routine. Shadows, deferred PBR, player outlines, preview,
HUD and post-processing remain outside this graph until their migration slices.

Run the content-free structural check with:

```sh
ProjectPrime -rendergraphcheck
```

## Slice 2: retained descriptors + order-preserving material batches

The second slice moves more submission state out of the pooled `RenderItem` bridge
without changing visible ordering.

### Immutable mesh descriptors

`RetainedRenderWorld` interns immutable mesh/raster descriptors across frames. A
descriptor owns the geometry list identity plus culling, billboard, wireframe and
viewmodel classification. The retained graph submits the descriptor's list identity
instead of reading geometry identity back from the mutable frame item.

### Immutable material snapshots

Each packet captures an exact value-type material descriptor containing the material
uniform/texture state that the current frame resolved after animation, texture
replacement and cosmetic selection. Transforms, matrix stacks and lighting remain
per-draw because they can differ even when two meshes share a material.

### Adjacent batching without sorting

Only adjacent, simple mesh packets with exactly equal material/raster state share one
material/texture/raster application. Their sequence is never changed. Overrides,
cosmetics, palette overrides, textured player skins, billboard geometry and viewmodels
remain one-state-application-per-draw.

This lets different mesh IDs using the same room material form a batch while preserving
the cartridge renderer's depth/stencil ordering.

### Persistent sampler-state cache

World and deferred-PBR texture sampling now cache the last applied
`TextureSamplerDescriptor + wrap mode` per texture binding. Identical filter,
mipmap, anisotropy and wrap requests stop reissuing texture parameters. Cache entries
are invalidated whenever a binding is released or re-uploaded, including progressive
HD replacement, so mip generation cannot be skipped after new image data arrives.

The renderer benchmark reports visible retained packets, adjacent batches, graph state
applications/reuses, retained descriptor count and sampler-state applications/cache
hits.

### Mainline compatibility

The retained graph is layered on top of the post-v0.1.44 renderer safety work.
Pipeline-keyed pass coalescing from #259 remains authoritative, progressive
large-texture promotion and pipeline prewarm from #262 remain intact, and #266's
run-based viewmodel isolation is preserved. Retained graph pass boundaries close
any open viewmodel run before changing pass-wide state.

## Slice 3: retained room submission templates

Room portal/frustum visibility remains dynamic and authoritative. Once a node is
accepted as visible, however, its immutable mesh topology no longer has to be
rediscovered every frame.

A weak per-node template retains:

- mesh reference and display-list identity;
- material reference and material index.

The live emission step still reads animated material values, mesh visibility,
texture-coordinate animation, selection state, transforms and polygon IDs. The
template therefore cannot freeze dynamic room behavior.

The emission loop also resolves room lighting, matrix-stack metadata and portal
alpha once per visible node instead of repeating those reads for every mesh in
the node. Weak keys ensure room rotations do not keep an old model graph alive.

Renderer benchmarks expose room-template builds and cache hits so large community
maps can quantify whether the retained submission path is being exercised.

## Slice 4: direct retained World submission

The first backend-direct path now targets the safest opaque retained meshes on
modern WebGPU backends. Eligible packets bypass Scene's GL-facing
`DoMaterial`/`DoTexture`/uniform replay and submit their already-retained
native geometry directly through the existing World WGSL, pipeline cache,
uniform arena and bind-group cache.

Eligibility is intentionally narrow:

- mesh geometry only;
- normal opaque render mode;
- no viewmodel or billboard transform;
- no matrix-stack skinning;
- no wireframe;
- no cosmetics, palette/colour overrides or textured-player skin;
- advanced-material and cel-shading modes remain on the compatibility executor.

The direct executor synchronizes the internal modern program/resource shadow
state while writing material, light, transform, sampler and texture state
without the public GL-style calls. This keeps a following fallback draw correct
without requiring a compatibility warm-up draw.

Texture sampling is also self-contained: retained direct draws resolve the same
native/modern sampling policy, update the WebGPU texture record, create mip
storage when required and reuse the progressive texture residency path.

Opaque and depth-rebuild graph passes attempt direct submission per packet.
A direct draw invalidates the adjacent compatibility-state reuse assumption, so
the next fallback packet reapplies full compatibility state before batching can
resume. Translucency, decals, PBR replay, viewmodels and special effects are
unchanged.

Benchmarks report direct retained World draws versus compatibility fallbacks.
`-rendergraphcheck` verifies the direct eligibility fence independently of a
GPU/device.

## Slice 5: frame-global World uniform templates

Direct retained World submission no longer writes packet state into
`ProgramRecord.Uniforms` and then asks the generated-shader bridge to read the
same dictionaries back out again.

At the start of each retained world graph:

- the current generated World shader's global scene state is resolved once;
- projection, view, fog, toon/global switches and viewport values are copied
  into a retained frame template;
- the generated layout is resolved to fixed word offsets once.

For each eligible direct packet:

- the frame template is copied into the generated uniform words;
- only packet-local fields are patched: lights, material values, texture matrix,
  model transform, alpha/material mode and immediate color/normal;
- unsupported feature gates are explicitly forced off;
- texture IDs are written directly into the generated sampler array;
- the completed block is written to the existing uniform arena.

This removes the per-packet string-keyed `ProgramRecord.Uniforms` update/read
round-trip while retaining the same generated World WGSL, pipeline cache,
bind-group cache and fallback behavior.

Benchmark output reports template builds and per-packet uniform patches. One
template build per rendered frame with many patches indicates the intended path.

Alpha-blended `RenderMode.Normal` items are excluded from the opaque direct
path because they are replayed in the translucent passes and would otherwise be
submitted only to fail the opaque alpha test.

## Slice 6: direct advanced material companion maps

The direct retained World path now remains active when Advanced Materials is
enabled.

Scene resolves a four-slot retained texture set for each packet:

1. albedo
2. normal
3. specular/roughness
4. emissive

Each slot carries its binding ID plus the resolved backend-independent sampler
policy. Albedo keeps the native fallback sampling behavior; authored companion
maps use their registered modern channel policy. The direct executor binds all
four WebGPU resources, patches the generated World flags, and validates each
binding against the active render target before draw encoding.

Cosmetics and explicit material overrides remain on the compatibility executor;
this slice only covers ordinary world/material replacement companion maps.

The generated fallback path also gates World companion samplers on the master
`advanced_materials` switch. This prevents stale `use_normal_map`,
`use_specular_map` or `use_emissive_map` values from causing unnecessary
resource binding when advanced materials are disabled.

Benchmarks report how many direct retained draws used companion maps separately
from the total direct World draw count.

## Slice 7: direct retained matrix-stack meshes

The direct World path now supports retained meshes that use the existing model
matrix-stack array.

For `MatrixStackCount > 0`, the packet's already-copied
`RenderItem.MatrixStack` is written directly into the generated World uniform
block. For `MatrixStackCount == 0`, the packet transform continues to populate
matrix slot zero.

Eligibility validates the authored matrix count against both the shader's
32-matrix contract and the packet storage bounds. Invalid/out-of-range stacks
fall back to the compatibility executor.

This expands direct submission to more animated/dynamic entity geometry without
changing skinning math, list geometry, draw order, projection handling or the
generated World shader. Viewmodels, billboards, cosmetics, overrides, decals,
translucency and cel shading remain outside the direct path.

Benchmarks report matrix-stack direct draws separately from total direct World
draws and direct Advanced Material draws.

## Slice 8: direct retained first-person viewmodels

Eligible first-person arm-cannon meshes can now use the direct retained World
executor while preserving the run-based viewmodel isolation introduced by #266.

The Scene still owns the projection transition:

- world -> viewmodel calls `SetViewModelRenderState(true)` and keeps the hard
  compatibility pass boundary;
- consecutive eligible viewmodel packets remain inside that run;
- the direct packet patches `proj_mtx` with the existing
  `_viewModelPerspectiveMatrix`;
- viewmodel -> world uses the same #266 transition back to the world projection;
- each graph pass still closes any open viewmodel run.

A failed direct attempt simply falls through to `RenderItem`; because the
viewmodel state was already selected, the fallback sees the same projection
without creating an extra transition.

Billboards, cosmetics, explicit overrides, decals, translucency and cel shading
remain compatibility-only. Viewmodel projection values and pass boundaries are
unchanged.

Benchmarks report direct retained viewmodel draws separately so the biped versus
alt-form CPU submission gap can be measured directly.

## Slice 9: direct retained billboard meshes

Spherical and cylindrical billboard meshes can now use the direct retained World
executor.

The Scene resolves the same view-inverse matrix used by the compatibility path:

- `BillboardMode.None` -> identity;
- `BillboardMode.Sphere` -> `_viewInvRotMatrix`;
- `BillboardMode.Cylinder` -> `_viewInvRotYMatrix`.

That matrix is patched directly into the generated World `view_inv_mtx` slot.
Geometry, matrix-stack data, materials, projection, draw order and pass state are
unchanged.

Eligibility accepts only the three defined billboard enum values; unknown values
fall back to the compatibility executor.

Benchmarks report direct retained billboard draws separately from total,
advanced-material, matrix-stack and viewmodel direct draws.

## Slice 10: direct retained color and palette overrides

Explicit color and palette overrides no longer force an otherwise eligible World
packet back through the compatibility executor.

The retained World layout now resolves the generated shader offsets for:

- `use_override` / `override_color`;
- `use_pal_override` / `pal_override_color`.

Per packet, the direct uniform patch writes the same flag/vector pairs used by
`DoTexture`. Player-outline replay is outside the world render graph, so this
path does not need the outline mask's temporary override suppression.

Textured-player-skin and cosmetic material paths remain compatibility-only in
this slice.

Benchmarks report direct retained override draws separately from the other
direct submission classes.

## Slice 11: direct retained textured bright skins

Textured player bright-skin meshes can now remain on the direct retained World
path.

The direct uniform patch mirrors the compatibility encoding:

- textured bright skin -> `textured_player_skin = 1`;
- high-contrast textured skin -> `textured_player_skin = 2`;
- ordinary packet -> `0`.

Player-outline replay still runs outside the world graph and continues to force
the textured-skin uniform off during its mask pass exactly as before.

This combines with direct color/palette overrides, matrix stacks, Advanced
Materials and viewmodel projection, allowing substantially more ordinary player
and arm-cannon geometry to avoid GL-style state replay.

Benchmarks report direct textured-skin draws separately.

## Slice 12: direct normal draw for outlined players

`PlayerOutlineColor` no longer forces the player's normal world/depth draw onto
the compatibility executor.

The colored outline itself is unchanged. `DrawPlayerOutlines()` still runs
after the world graph, binds the outline mask target, enables
`player_outline_mask`, supplies the per-player outline color, and replays the
same `RenderItem` through the compatibility path.

Only the earlier normal world/depth draw can now use retained direct WebGPU.

This keeps outline depth/culling/cutout behavior and exception cleanup intact
while removing unnecessary compatibility replay from the main player draw.

Benchmarks report these outlined normal-world direct draws separately.

## Next slices

The graph and packet seam is intended to support the remaining migration without
another scene-wide rewrite:

- retain static room packets across frames instead of rebuilding them from entities;
- retain static room visibility templates so only changing portal/material state is
  refreshed each frame;
- move the retained material descriptor directly into the WebGPU uniform/bind-group
  executor instead of replaying GL-compatible uniform calls;
- add partition/cluster visibility before packet emission;
- schedule shadow, PBR and post-processing as graph passes/resources;
- allow broader pipeline/material sorting only behind visual-parity gates;
- move modern backends to direct WebGPU packet execution, leaving the GL executor as
  the compatibility implementation.

The release gate remains visual parity plus measured frame-time improvement on Metal,
DX12/Vulkan and physical Android Vulkan hardware.
