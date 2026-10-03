# Modern rendering backends

Project Prime's modern renderer is based on the WebGPU C API through Silk.NET 2.23 and
wgpu-native. The backend policy is deliberately explicit:

| Platform | Modern backends | Default once the renderer migration is enabled |
| --- | --- | --- |
| Windows | DirectX 12, Vulkan | DirectX 12 |
| macOS | Metal, Vulkan through MoltenVK | Metal |
| Linux | Vulkan | Vulkan |
| Android | Vulkan | Vulkan |

OpenGL/OpenGL ES remains the compatibility fallback while the shared renderer is moved
off legacy GL state.

## Why one WebGPU renderer

The current renderer is heavily OpenGL-oriented and Android already has a compatibility
facade that converts legacy immediate-mode/display-list calls into buffers. Reusing that
shape gives Project Prime one renderer-facing API while wgpu-native handles DirectX 12,
Metal and Vulkan underneath. It avoids four independent material, post-process, replay,
map-editor and UI renderers drifting apart.

Silk.NET.WebGPU 2.23.0 is generated against the WebGPU ABI used by wgpu-native commit:

`33133da4ec5a0174cb21539ef2d3346f75200411`

Do not update one side without the other. Newer wgpu-native releases changed callback,
surface and device layouts.

## Native builds

Windows and Linux use `Silk.NET.WebGPU.Native.WGPU 2.23.0`.

macOS uses a repository-built wgpu-native library so the same binary contains Metal and
the `vulkan-portability` backend:

```bash
tools/wgpu/build-native.sh osx-arm64
# or
tools/wgpu/build-native.sh osx-x64
```

The macOS package also carries the Vulkan Loader and MoltenVK. At runtime the Vulkan
probe points the loader at `MoltenVK_icd.json`.

Android uses the same pinned ABI and builds an NDK shared library. API 24 is the floor,
matching the Android project:

```bash
export ANDROID_NDK_ROOT=/path/to/android-sdk/ndk/<version>
tools/wgpu/build-native.sh android-arm64
# optional emulator:
tools/wgpu/build-native.sh android-x64
```

The Android project includes a built `libwgpu_native.so` automatically when it exists
under `artifacts/wgpu-native-android/lib/<abi>/`.

## Diagnostics

The policy check needs no display or GPU:

```bash
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -renderbackendcheck
```

The native probe creates a real wgpu-native instance, adapter and device:

```bash
ProjectPrime -renderbackendprobe -renderer dx12
ProjectPrime -renderbackendprobe -renderer vulkan
ProjectPrime -renderbackendprobe -renderer metal
```

Aliases include `d3d12`, `directx12`, `vk` and `moltenvk`.

## Renderer migration gates

The modern backend is intentionally not made the normal gameplay renderer until these
gates are complete:

1. Generalize Android's `GlEs` facade into a platform-neutral legacy-render command
   layer and retain its immediate-mode/display-list batching.
2. Implement WebGPU textures, samplers, buffers, uniform state and shader modules.
3. Map framebuffer changes and clears to render-pass boundaries, with a pipeline cache
   keyed by shader/topology/blend/depth/stencil/cull/write-mask/target format.
4. Port shadow maps, deferred PBR, outlines, replay targets, editor previews, UI overlay,
   screenshots/readback and movie textures.
5. Create native swapchain surfaces from the existing OpenTK window on desktop and the
   existing Android SurfaceView/ANativeWindow lifecycle.
6. Run rendered parity captures and resource-lifetime checks on DX12, Vulkan, Metal and
   Android Vulkan before changing the default away from OpenGL.

Explicit modern renderer selection is experimental. The ordinary startup path
without an explicit renderer option remains on OpenGL. Explicit `-renderer auto`
already resolves to the platform modern backend in the audited branch; this work
does not change that policy.


## Completion work: 2026-09-30

The requested scope and activation gates are retained in
[modern-rendering-completion-plan.md](modern-rendering-completion-plan.md).
The migration remains experimental; see [validation evidence](modern-rendering-validation.md).

Implemented:

- Shader-aware feedback checks reject sampled color/depth attachment aliases and
  self-copies before native submission. Compatible core draws now remain inside a
  coalesced render pass; framebuffer/attachment changes, clears, copies, readback,
  texture mutation and presentation close the pass before changing resource usage.
- Rooted error/loss callbacks, bounded surface reacquisition, device reconstruction
  from retained logical resources, fresh-window OpenGL startup fallback, and
  fresh-process OpenGL launcher restart after failed Auto reconstruction. A failed
  session is closed; live simulation is never reinitialized to rebuild rendering.
- GPU mip chains with disjoint subresource views; trilinear and anisotropic sampler
  mapping; sampler changes preserve images; dynamic uploads reuse native storage.
- World, deferred PBR and post-process WGSL generated from the shared GLSL formulas.
  Production shadows, cel/player outlines, material maps, AA/TAA, bloom, SSAO,
  reflections, fog, contact shadows, dynamic lights and HDR use the shared paths.
  PBR currently retains the existing three geometry passes; MRT is not implemented.
- RGBA16F scene and history targets, ACES resolve, framebuffer-origin tracking,
  bottom-up GL-compatible readback, clipped clears, default-framebuffer depth,
  attribute restoration and viewport transformation for oversized GL viewports.
- Display-list GPU geometry caching with draw-time normal/color inheritance,
  frame-arena uniform/transient-geometry storage, cached steady-state bind groups,
  ordered staged texture updates, batched command encoding/submission, and common
  pipeline prewarming on the rendering thread.
- Auxiliary thumbnail, replay and editor windows own the selected renderer through
  `DesktopGraphicsSession`. The launcher hunter side scene uses the same renderer.
- macOS Vulkan uses an AppKit-view native extension to avoid the pinned library
  treating a Vulkan surface as Metal. The packaged loader alias has its own Mach-O
  identity, allowing Vulkan startup without DYLD environment overrides.
- Android ANativeWindow/WebGPU surface ownership and recreation, without an EGL
  presentation context on the Vulkan path. GLES remains the startup fallback.
- Renderer preference in settings (migration default OpenGL), platform choices,
  restart notice, supported present-mode selection and capability logging.

Commands:

```sh
ProjectPrime -renderfullcheck -renderer metal
ProjectPrime -mapviewportcheck /tmp/map-metal -renderer metal
ProjectPrime -respawnrendercheck 'MP3 PROVING GROUND' -renderadvanced -cycles 1 -timeout 120 -renderer metal
ProjectPrime -replayexportcheck synthetic -output /tmp/export-metal -renderer metal
ProjectPrime -replaycadencecheck synthetic -renderer metal
ProjectPrime -renderparitycheck 'MP3 PROVING GROUND' -output /tmp/room-metal -renderer metal
ProjectPrime -renderbenchmark 'MP3 PROVING GROUND' -output /tmp/metal.json -renderer metal
python3 tools/render-parity/compare.py /tmp/map-gl /tmp/map-metal --output /tmp/parity.json
```

Game/replay/editor checks require local game assets. Export also requires FFmpeg.
The full renderer check and Android `RendererAcceptanceActivity` require no assets.
Generated shaders are checked using `tools/modern-shaders/check.sh`.

Remaining release gates include cross-platform/hardware acceptance, broad combat
and effect visual parity, sustained lifetime/monitor/fullscreen stress, physical
Android match acceptance, and performance acceptance. Current Metal
submission cost exceeds OpenGL in the frozen Extreme-preset benchmark. Do not
change the default to Auto until these gates are actually satisfied.

## PR scope audit (2026-09-30)

Remaining hardware testing does not block opening the implementation PR;
it still blocks default activation.

| Plan phases | Implementation | Remaining acceptance |
| --- | --- | --- |
| P0–P1 | Feedback validation, pass boundaries, callbacks, texture formats, mipmaps and samplers implemented | Native Windows matrix and broader GPU coverage |
| P2–P4 | Shadows, cel/player outlines, deferred PBR, AA/TAA, post effects and HDR enabled | Moving-scene and cross-vendor visual acceptance |
| P5–P6 | Replay/export/readback, editor/thumbnail targets and hunter previews integrated | Old replay corpus and long tool/session transitions |
| P7 | Android Vulkan surface and lifecycle implemented; GLES fallback preserved | Physical Android matches and lifecycle stress |
| P8–P9 | Surface/device recovery, present modes, pipeline keys/prewarm and reusable buffers implemented | Long recovery/fullscreen/monitor stress and end-to-end launcher restart |
| P10–P11 | Settings persistence, platform choices, fallback and capability reporting implemented | User-facing platform acceptance |
| P12–P13 | Full checks, parity captures, lifetime diagnostics, benchmark and independent CI jobs implemented | Full hardware matrix, sustained runs and performance acceptance |
| P14 | Explicit Auto already selects platform modern backends | New/default installs remain OpenGL until release gates pass |

The preferred single-pass MRT optimization is not a required feature gate: PBR
uses the existing three-pass material path with validated buffers. Wireframe
triangles now emit cached line indices instead of silently drawing filled faces.
This is portable one-pixel triangle-edge wireframe; native wide lines and
polygon-rasterization edge/culling rules are not emulated. Normal gameplay uses
filled triangles.

Benchmarking now defaults to 1,200 measured frames per resolution/scale, with
`-samples N` for shorter smoke runs or longer acceptance runs. It records startup,
warmup and measured pipeline creation count/CPU duration/maximum stall, texture
upload bytes/CPU submission duration, tracked texture storage and pooled buffer
storage. Upload duration includes flushing preceding draws. Storage estimates
are not driver VRAM measurements. Instrumentation is enabled only while explicitly
benchmarking. GPU timestamp milliseconds remain unavailable through the pinned
native C ABI, which lacks the queue timestamp-period conversion function; adapter
support is logged separately rather than treating CPU wait time as GPU timing.
