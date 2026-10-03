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

## Next slices

The graph and packet seam is intended to support the remaining migration without
another scene-wide rewrite:

- retain static room packets across frames instead of rebuilding them from entities;
- split immutable mesh/material descriptors from per-frame transforms;
- move material/pipeline state from GL compatibility calls into packet descriptors;
- add visibility/partition inputs before packet emission;
- schedule shadow, PBR and post-processing as graph passes/resources;
- batch compatible packets by pipeline/material only after parity captures prove that
  ordering is safe;
- move modern backends to direct WebGPU packet execution, leaving the GL executor as
  the compatibility implementation.

The release gate remains visual parity plus measured frame-time improvement on Metal,
DX12/Vulkan and physical Android Vulkan hardware.
