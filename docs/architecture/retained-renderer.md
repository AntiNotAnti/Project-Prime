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
