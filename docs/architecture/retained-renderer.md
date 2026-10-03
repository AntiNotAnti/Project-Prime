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

## Slice 13: stable retained uniform slots

The expanded direct World path now uses a dedicated retained uniform arena instead
of the generic frame arena. Direct draw slot N maps to the same WebGPU buffer and
aligned offset on every completed public frame.

Because WebGPU bind groups bake the uniform buffer and offset into the binding,
this removes offset churn caused by unrelated shadow/PBR/UI allocations.

The retained arena:

- uses fixed-size aligned World-uniform slots;
- never reuses a slot until the completed public-frame boundary;
- stages writes and flushes them immediately before the QueueSubmit that consumes
  those offsets;
- keeps pages alive across frames so buffer identity is stable;
- grows without relocating earlier slots.

Direct World bind groups use a separate stable-slot cache. A cached group survives
across frames while its slot, texture views and samplers are unchanged. Advanced
material companion maps, viewmodels, matrix stacks, billboards, overrides,
textured skins and outlined normal draws all share the same stable mechanism.

Progressive texture replacement or sampler changes replace only affected cached
groups. Benchmarks report retained bind-group hits/misses and uniform-slot
high-water; steady static scenes should trend strongly toward hits after warmup.

## Slice 14: direct retained deferred PBR MRT

Modern backends no longer have to replay every PBR-eligible opaque mesh through
`DrawDeferredPbrItem()` and GL-style uniform/texture calls after the forward
World pass.

When MRT PBR is active:

- Scene reuses the retained opaque packet list and immutable mesh descriptors;
- one `DeferredPbrMrt` frame template captures the global view/viewport state;
- per packet, the direct executor patches projection, billboard view inverse,
  matrix-stack/transform, texgen, material specular/emission and overrides;
- the existing retained four-texture set supplies albedo, normal, specular and
  emissive bindings with the same sampler/residency policy as the direct World
  path;
- the existing retained native geometry buffers are submitted directly into the
  three-target PBR render pass.

The first slice deliberately leaves cosmetic-surface/material effects on the
compatibility PBR replay. Viewmodels remain excluded exactly as before. OpenGL
and GLES still use the compatibility three-pass PBR path unchanged.

Direct and compatibility PBR draw counts, plus MRT template builds and uniform
patches, are included in renderer benchmark output. Mixed direct/fallback items
remain valid inside the same MRT pass because compatibility draws fully restore
their own per-item uniforms and texture state.

## Slice 15: native world-pass state + direct decals/translucency

Modern backends no longer replay the six world-pass state transitions through
the GL-style facade. `WorldRenderGraph` configures backend pipeline state
directly for opaque, decal, translucent-mask, depth rebuild, behind and front
passes. OpenGL/GLES keep the original switch unchanged.

Direct World eligibility is pass-aware: opaque/depth retain their opaque contract,
decals submit directly in the decal pass, and translucent/alpha-blended packets
submit directly through the stencil mask, behind and front passes. Stencil
comparison is fixed by pass while polygon ID remains a dynamic reference.

Depth/color masks, alpha test, stencil operations, blend state and decal depth
bias are therefore owned by the native graph state on modern backends.

## Slice 16: top-level frame render graph

The retained renderer now owns a top-level frame graph rather than relying on an
implicit call chain in `RenderFrameContent`.

The graph schedules, in the existing visual order:

1. shadow map;
2. world target clear/setup;
3. six-pass retained world graph;
4. player/world outlines;
5. scene-space preview and HUD models;
6. deferred PBR G-buffer;
7. enhanced post processing;
8. world-to-output composite.

Each node declares the resources it reads/writes: scene color/depth/stencil,
shadow depth, PBR albedo/normal/material, processed scene color and final output.

This slice intentionally preserves each pass implementation. The graph is the
ownership/scheduling boundary that lets backend-native passes replace individual
nodes without another Renderer.cs rewrite. Full-screen presentation HUD remains
outside the core frame graph because it is window/UI composition rather than the
retained 3D scene.

`-rendergraphcheck` validates both the six-pass world graph and the top-level
frame graph ordering/resource contract.

## Slice 17: persistent room render packets

Static room mesh topology now owns persistent `RenderItem` packet objects instead
of renting and returning one generic packet per visible mesh every picture.

A retained room mesh template owns:

- mesh/material/list identity;
- one persistent room render packet.

The packet is **not frozen**. Each visible submission overwrites every dynamic
field that can change:

- current material alpha/diffuse/ambient/specular;
- texture binding and texgen/repeat state;
- texture-coordinate animation matrix;
- node transform and matrix stack;
- billboard mode and lighting;
- portal alpha/polygon ID;
- editor selection override;
- mesh visibility remains checked before submission.

Persistent room packets bypass `_usedRenderItems`, so they can never be returned
to the generic player/effect pool. Dynamic entities, effects, trails, volumes and
viewmodels continue using the existing pool unchanged.

This removes steady-state room `RenderItem` rent/fill/recycle churn while keeping
portal/frustum and material animation fully live. Benchmarks expose persistent
room packet submissions alongside template builds/hits.

## Slice 18: direct retained shadow replay

The directional shadow pass now reuses the retained opaque packet list and native
mesh buffers instead of rebuilding each opaque mesh through `RenderItem`.

The existing shadow pass still owns its stabilized light camera, framebuffer,
depth target and alpha-cutout state. On modern backends it captures a temporary
World frame template after installing the shadow view/projection, then eligible
opaque packets submit directly through the retained World executor.

Viewmodels and translucent/alpha-blended packets remain excluded exactly as
before. Any ineligible packet falls back to the existing shadow `RenderItem`
replay in place. The normal world graph rebuilds its own frame template after
the shadow pass, so shadow matrices cannot leak into the main camera.

Benchmarks report direct versus compatibility shadow replay counts.

## Core retained renderer: complete

The core migration is complete at this point:

- immutable geometry is retained in native GPU buffers;
- room mesh packets persist across frames while dynamic fields stay live;
- the six-pass World graph owns native pass state on modern backends;
- opaque, decal and translucent mesh passes use direct retained WebGPU submission
  whenever their material features are supported;
- shadow geometry reuses retained packets and native buffers;
- deferred PBR MRT can replay retained opaque packets directly;
- stable World uniform slots and bind groups persist across frames;
- shadow, World, outlines, scene overlays, PBR, post processing and composite are
  scheduled by the top-level frame graph;
- OpenGL/GLES remain the compatibility implementation and special unsupported
  packet classes can still fall back per draw.

The remaining renderer ideas below are optimization/expansion work, not required
to finish the retained-renderer architecture.

## Post-core optimization pass

The first post-core pass builds on the completed retained architecture without
changing gameplay, simulation, networking, map data or translucent/decal ordering.

### 1. Portal-aware retained visibility clusters

Room sibling chains are cached in small spatial clusters with union AABBs. Each
cluster is tested against the active portal frusta before its member nodes reach
the original per-node visibility test. A cluster can only reject work; surviving
nodes still use the existing authoritative visibility path.

### 2. Parity-safe opaque sorting

Persistent room-owned opaque packets may be sorted by retained state within
contiguous safe runs. Dynamic entities, viewmodels, billboards, overrides,
outlines, textured skins, decals, translucency and other order-sensitive packets
remain barriers and retain cartridge submission order.

### 3. Stable retained PBR slots

Deferred PBR MRT owns a deterministic uniform arena and slot-keyed bind-group
cache, parallel to the retained World path. World and PBR use separate arenas so
their different generated uniform sizes cannot perturb one another's stable
buffer/offset identity.

### 4. Frame-graph transient color pool

Short-lived frame-graph color targets lease backing textures from a bounded
descriptor-matched pool. The player-outline mask releases its RGBA8 target before
post processing, allowing the later processed-scene target to reuse that storage
when dimensions/filtering match. TAA history remains persistent because it spans
frames.

### 5. Indexed-indirect retained draw foundation

Persistent room World/PBR draws can source their indexed draw arguments from a
GPU-visible WebGPU indirect arena. The current stage is CPU-populated indirect
arguments, deliberately structured so a later compute visibility/compaction pass
can own the same argument storage. It is enabled on desktop DX12/Vulkan and kept
off on Metal/Android until hardware benchmarks prove a win.

### 6. Native outline and fullscreen paths

Eligible player outline-mask geometry reuses retained World packets and native
mesh buffers, falling back per item when necessary. Fullscreen post-process,
tone-map, outline-composite and final-composite draws reuse one retained native
quad on modern backends instead of rebuilding legacy immediate geometry.

### 7. Backend-specific submission budgets

Modern command batching and staged texture-upload limits are selected per backend.
Desktop Metal/DX12/Vulkan receive longer command windows, while Android keeps a
smaller upload/encoder budget to limit burst memory and tiled-GPU pressure.
Indirect room draws remain backend-gated rather than assumed beneficial
everywhere.

The renderer benchmark reports cluster rejection, opaque sorting, World/PBR
bind-group reuse, transient-pool reuse, retained indirect/fullscreen/outline
draws, and the active submission/upload budgets so hardware acceptance can judge
each optimization independently.

The release gate remains visual parity plus measured frame-time improvement on
Metal, DX12/Vulkan and physical Android Vulkan hardware.
