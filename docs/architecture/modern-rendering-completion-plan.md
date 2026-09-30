# Project Prime: Modern Rendering Backends Completion Plan

## Target Branch

`feature/modern-rendering-backends`

Current audited head:

`e829c9b26980d1232436176cf05e50fc586b6120`

## Objective

Complete the modern renderer so Project Prime can run the full game and supporting tools through:

- Windows
  - DirectX 12
  - Vulkan
- macOS
  - Metal
  - Vulkan through MoltenVK
- Linux
  - Vulkan
- Android
  - Vulkan
- Compatibility fallback
  - OpenGL / OpenGL ES

The modern renderer should eventually become the default `Auto` path while OpenGL remains available as a compatibility fallback.

The migration must not alter:

- game simulation
- hit detection
- networking
- movement
- replay determinism
- authoritative game state

---

# Current State

The branch already contains most of the renderer foundation:

- `GraphicsApi` compatibility facade
- backend-neutral immediate-mode translation
- display-list translation
- WebGPU/wgpu-native device creation
- DX12/Vulkan/Metal backend policy
- Windows/X11/Wayland/Cocoa surfaces
- texture/resource compatibility state
- WebGPU buffers
- WebGPU shader modules
- uniform translation
- pipeline caching
- framebuffer emulation
- depth/stencil support
- basic readback
- framebuffer blitting
- core world rendering
- RTT rendering
- HUD disruption/shift rendering
- cel-outline shader
- player-outline shader
- HDR targets
- ACES tone mapping
- desktop swapchain presentation
- macOS MoltenVK packaging
- Android wgpu-native packaging
- focused backend CI checks

The remaining work is primarily **renderer correctness, advanced-rendering parity, Android presentation, and production activation**.

---

# Completion Order

## P0: Fix Current WebGPU Render-Pass Resource Hazard

### Problem

The latest modern renderer CI currently fails on:

- Linux Vulkan window presentation
- macOS Metal x64
- macOS Metal arm64

DX12 currently passes.

wgpu validation reports a texture being used simultaneously as:

- `RESOURCE`
- `COLOR_TARGET`

within the same usage scope.

This results in:

```text
Attempted to use a texture with conflicting usages.
Current usage TextureUses(RESOURCE)
and new usage TextureUses(COLOR_TARGET).
```

### Files

Primary:

```text
src/MphRead/Mods/Render/ModernGraphicsCompat.cs
src/MphRead/Mods/Render/ModernGraphicsCompat.World.cs
src/MphRead/Mods/Render/ModernGraphicsResourceState.cs
src/MphRead/Mods/Render/ModernGraphicsWindowCheck.cs
```

### Implementation

Add explicit WebGPU resource-usage tracking.

For every texture, track whether it is currently being used as:

```text
SampledTexture
ColorAttachment
DepthAttachment
CopySource
CopyDestination
```

Before beginning a render pass:

1. Determine all framebuffer attachments.
2. Determine all texture bindings required by the selected shader.
3. Reject or resolve any texture appearing in both sets.
4. Rebuild the bind group without conflicting bindings when appropriate.
5. End the current render pass whenever framebuffer/resource usage changes require a new scope.

Do not simply fix the failing HDR diagnostic by manually unbinding the texture.

The renderer should prevent this class of hazard globally.

### Add Debug Validation

Add something similar to:

```text
ValidateRenderPassResources()
```

It should report:

```text
texture ID
native texture
framebuffer
shader/program
texture unit
attachment type
```

when a feedback hazard occurs.

### Add WebGPU Error Handling

Register:

- uncaptured error callback
- device-lost callback

Do not allow wgpu-native's default uncaptured-error handler to abort the process without Project Prime logging useful information.

Failures should become controlled renderer errors whenever possible.

### Acceptance Criteria

All must pass:

```text
Windows DX12 -renderwindowcheck
Linux Vulkan -renderwindowcheck
macOS Metal -renderwindowcheck
```

MoltenVK should run wherever the CI/hardware exposes an adapter.

No:

```text
RESOURCE + COLOR_TARGET
```

validation failures.

---

# P0.1: Strengthen Render-Pass Lifetime Management

Framebuffer changes must correspond cleanly to WebGPU render-pass boundaries.

### Audit

Audit these operations:

```text
BindFramebuffer
FramebufferTexture2D
FramebufferRenderbuffer
Clear
BlitFramebuffer
CopyTexSubImage2D
ReadPixels
UseProgram
DrawBuffer
ReadBuffer
```

### Required Behavior

A render pass should end when:

- framebuffer changes
- framebuffer attachments change
- incompatible pipeline target format changes
- readback is requested
- framebuffer copy/blit is requested
- presentation begins
- a texture changes from attachment to sampled resource

Create a central helper:

```text
EndActiveRenderPassIfRequired(...)
```

Avoid scattering pass-ending logic throughout compatibility calls.

### Acceptance Criteria

Repeated sequences of:

```text
render
sample
render-to-same-resource-later
blit
readback
present
```

work without wgpu validation errors.

---

# P1: Complete Texture and Sampler Fidelity

The modern renderer currently stores mipmap state but native textures are generally created with:

```text
MipLevelCount = 1
```

and samplers currently use approximately:

```text
MipmapFilter = Nearest
MaxAnisotropy = 1
```

This means several existing Project Prime graphics settings do not yet have proper modern-backend behavior.

## Implement Real Mipmaps

### Files

```text
ModernGraphicsCompat.cs
ModernGraphicsResourceState.cs
```

### Changes

Calculate full mip count:

```text
floor(log2(max(width, height))) + 1
```

when mipmaps are requested.

Allocate the texture using the full mip chain.

Implement mip generation.

Preferred approach:

- reusable WGSL downsample pass
- one mip generated from the previous mip
- cached pipeline
- reused sampler/bind-group layouts

Fallback for unusual unsupported formats:

- no mipmap
- log fallback
- retain base level

### Texture State

Extend `TextureRecord` with fields resembling:

```text
RequestedMipmaps
NativeMipCount
MipmapsDirty
Anisotropy
```

### Acceptance Criteria

Existing:

```text
GenerateMipmap(...)
```

calls visibly produce usable mip levels.

---

# P1.1: Implement Trilinear Filtering

Map existing OpenGL filters correctly.

Examples:

```text
Nearest
Linear
NearestMipmapNearest
LinearMipmapNearest
NearestMipmapLinear
LinearMipmapLinear
```

Map these to WebGPU:

```text
MinFilter
MagFilter
MipmapFilter
```

### Acceptance Criteria

Project Prime's trilinear graphics setting behaves equivalently between GL and WebGPU.

---

# P1.2: Implement Anisotropic Filtering

Replace:

```text
MaxAnisotropy = 1
```

with the value selected through Project Prime graphics settings.

Support:

```text
1x
2x
4x
8x
16x
```

Clamp against backend/device capability if necessary.

### Acceptance Criteria

Graphics settings produce the expected WebGPU sampler.

---

# P1.3: Validate Texture Format Mapping

Audit translation for at least:

```text
RGB
RGBA
RGBA8
RGBA16F
Depth
Depth24
Depth24Stencil8
```

Verify:

- upload format
- native format
- sampling format
- attachment format
- readback format

Particularly test:

```text
Depth24Plus
Depth24PlusStencil8
RGBA16Float
```

---

# P2: Enable Modern Shadow Maps

Currently the production shadow path explicitly skips itself while:

```text
ModernGraphicsCompat.Active
```

### File

```text
src/MphRead/Mods/Render/ShadowMap.cs
```

### Goal

Remove the modern-renderer early return.

Reuse the compatibility renderer instead of creating a second shadow implementation where possible.

### Required Features

Support:

- shadow framebuffer
- depth target
- light projection
- stabilized texel snapping
- opaque geometry replay
- alpha-tested geometry
- depth comparison/sample
- PCF filtering
- Low / High / Ultra sizes

### Validate

```text
1024
2048
4096
```

shadow maps.

### Acceptance Criteria

Shadow rendering looks equivalent on:

- GL
- DX12
- Vulkan
- Metal

No high-refresh shadow crawling regression.

---

# P2.1: Enable Player Outlines

### File

```text
PlayerOutlines.cs
```

Remove:

```text
if (ModernGraphicsCompat.Active) return;
```

after the underlying path is validated.

### Test

Verify:

- friendly color
- enemy color
- custom colors
- width scaling
- render-scale scaling
- depth occlusion
- transparent geometry

### Acceptance Criteria

Modern renderer player outlines visually match the GL path.

---

# P2.2: Finish Cel Outline Integration

A WebGPU cel-outline shader already exists.

Finish production integration with:

- real scene depth
- target-size scaling
- render scale
- depth quantization
- edge width
- fallback when readable depth is unavailable

Remove remaining GL-only assumptions.

---

# P3: Port Deferred PBR to the Modern Renderer

### File

```text
DeferredPbr.cs
```

Currently:

```text
ModernGraphicsCompat.Active
```

forces the PBR path off.

### Goal

Run the existing deferred material system on WebGPU.

### Required G-buffer Targets

Maintain:

```text
Albedo
Normal
Material
Depth
```

Where practical, consider using MRT on WebGPU rather than repeating geometry three times.

OpenGL compatibility can continue using its existing multipass implementation.

### Preferred Modern Implementation

For WebGPU:

```text
single geometry pass
    -> albedo target
    -> normal target
    -> material target
    -> shared depth
```

This should reduce the modern renderer's cost versus the current compatibility GL approach.

### Materials

Support:

- base texture
- normal map
- specular map
- emissive map
- roughness data
- palette overrides
- cosmetic materials
- material emission
- material specular parameters

### Acceptance Criteria

Existing Advanced Materials and Deferred PBR settings work with modern renderers.

---

# P4: Port the Full Graphics/Post-Processing Pipeline

### File

```text
GraphicsPipeline.cs
```

Currently production post-processing is disabled when the modern renderer is active.

Remove the early return only after each feature is ported and individually validated.

---

## P4.1: Anti-Aliasing

Implement modern equivalents for:

```text
FXAA
FXAA High
SMAA-style 1x
TAA
```

### TAA Requirements

Port:

- history texture
- history invalidation
- previous view projection
- camera-cut rejection
- velocity/reprojection logic
- fast-history rejection

Prevent the previous ghosting problems from returning.

---

# P4.2: Sharpening

Port contrast-adaptive sharpening.

Ensure sharpening occurs:

```text
after AA
before final presentation
before HUD
```

as appropriate.

---

# P4.3: Bloom

Implement bloom against the modern scene texture.

Validate:

- threshold
- intensity
- HDR interaction

---

# P4.4: SSAO

Port depth-derived ambient occlusion.

Requires:

- sampled scene depth
- projection reconstruction
- normal reconstruction or PBR normals

---

# P4.5: Contact Shadows

Port depth-based contact shadows.

Verify stability during:

- camera movement
- high refresh
- render-scale changes

---

# P4.6: Screen-Space Reflections

Port SSR.

Pay special attention to the previous reflection artifact issues.

Add rejection for:

- invalid depth
- off-screen ray exits
- discontinuities
- extreme grazing angles

---

# P4.7: Enhanced Fog

Support:

```text
Enhanced Fog
Volumetric Fog
Volumetric Scattering
```

Use the same camera/depth reconstruction inputs as the GL path.

---

# P4.8: Dynamic Projectile/Energy Lighting

Port the existing dynamic-light candidate system.

Preserve:

```text
fixed scratch array
no per-frame allocation
maximum light count
```

Do not regress the allocation improvements already made.

---

# P4.9: HDR

Complete native WebGPU HDR flow:

```text
RGBA16F scene
    ↓
post processing
    ↓
ACES tone mapping
    ↓
swapchain
```

Verify:

- overbright colors
- bloom
- PBR
- emissives
- exposure
- tone mapping

---

# P5: Replay and Offscreen Rendering Parity

The low-level blit/readback pieces now exist.

Next prove all consumers.

## Required Paths

Test:

```text
Replay Studio playback
Replay Studio fullscreen
Replay export
killcams
final killcams
replay screenshots
clip playback
cinematic editor
thumbnail generation
```

### Verify

- framebuffer copies
- scaled framebuffer blits
- readback
- resize
- fullscreen transitions
- history invalidation
- old replay compatibility

### Acceptance Criteria

A replay should behave identically regardless of:

```text
OpenGL
DX12
Vulkan
Metal
```

---

# P5.1: Screenshot and Readback Parity

Audit every `ReadPixels` user.

Verify:

- world screenshots
- HUD screenshots
- replay screenshots
- automated regression captures
- map screenshots
- thumbnails

Check:

- row alignment
- vertical orientation
- pixel format
- alpha
- asynchronous GPU completion

Consider adding an asynchronous readback path later, but correctness comes first.

---

# P5.2: Map Studio / Editor Rendering

Run the modern renderer through:

- map viewport
- model preview
- imported OBJ preview
- material preview
- texture preview
- picking
- screenshots

Do not require Map Studio to maintain an independent renderer.

It should use the same compatibility abstraction.

---

# P5.3: Movie / Dynamic Texture Support

Audit dynamic texture upload users.

Examples:

- movie textures
- animated UI textures
- frequently updated render textures

Ensure `TexSubImage2D` correctly updates native WebGPU textures without reallocating unnecessarily.

---

# P6: Finish Launcher Hunter Preview

### File

```text
LauncherHunter.cs
```

Currently the modern renderer intentionally refuses to create the launcher side scene.

Remove this special case once offscreen scene rendering is stable.

### Requirements

Support:

```text
empty private Scene
hunter model
transparent UI hole
scene framebuffer
proper cleanup
retry handling
```

### Acceptance Criteria

Hunter preview works on:

- DX12
- Vulkan
- Metal

without falling back to placeholder blocks.

---

# P7: Android Vulkan Presentation

This is the largest remaining platform-specific implementation.

wgpu-native is already being built and packaged for Android.

The missing component is the presentation surface.

## Create Android Surface Adapter

Add something similar to:

```text
ModernGraphicsSurface.Android.cs
```

### Required Flow

```text
Android SurfaceView / Surface
        ↓
ANativeWindow
        ↓
WGPUSurfaceDescriptorFromAndroidNativeWindow
        ↓
WebGPU Surface
        ↓
SurfaceConfigure
        ↓
GetCurrentTexture
        ↓
Render
        ↓
Present
```

Use the wgpu/WebGPU Android-native-window surface descriptor supported by the pinned ABI.

---

# P7.1: Integrate Android Surface Lifecycle

Handle:

```text
SurfaceCreated
SurfaceChanged
SurfaceDestroyed

Activity pause
Activity resume

orientation changes
window resize
app backgrounding
app foregrounding
```

Never retain an invalid `ANativeWindow`.

On surface destruction:

```text
end render pass
release swapchain texture/view
unconfigure/release surface
```

On recreation:

```text
create surface
query capabilities
configure surface
resize render targets
resume rendering
```

---

# P7.2: Avoid Competing EGL/Vulkan Ownership

When Vulkan is selected, make sure Android does not create or retain an unnecessary GLES presentation context for the same surface.

Keep GLES as the fallback path.

Renderer selection must occur early enough in startup to choose the correct surface ownership strategy.

---

# P7.3: Android Vulkan Smoke Test

Add:

```text
-renderbackendprobe -renderer vulkan
```

for Android where possible.

Add an instrumented render test that proves:

```text
device created
surface created
frame acquired
triangle/frame rendered
frame presented
```

Prefer an emulator configured with Vulkan/SwiftShader for CI plus real-device validation before making Vulkan the default.

---

# P8: Device-Lost and Surface-Lost Recovery

Modern APIs require explicit recovery handling.

Implement recovery for:

```text
SurfaceLost
SurfaceOutdated
SurfaceTimeout
DeviceLost
OutOfMemory
```

### SurfaceLost / Outdated

Recreate or reconfigure:

```text
surface
surface format
surface view
dependent framebuffers
```

### DeviceLost

Attempt controlled renderer reconstruction.

If reconstruction fails:

```text
Auto mode -> fall back to OpenGL
explicit renderer -> report failure clearly
```

Do not silently leave the player on a black screen.

---

# P8.1: Fullscreen and Resize Stress

Test repeated:

```text
window resize
maximize
restore
fullscreen
windowed
monitor switch
resolution change
render scale change
```

Verify no:

- stale texture
- stale framebuffer
- invalid surface
- leaked view
- invalid pipeline target

---

# P9: Frame Pacing and Presentation

Implement modern renderer presentation behavior for:

```text
VSync
uncapped
frame-rate cap
high-refresh monitors
```

Select appropriate WebGPU present mode where supported.

Ensure modern rendering does not reintroduce:

- input lag
- camera late-latch mismatch
- weapon/world timing mismatch
- high-refresh ghosting

Keep explicit FPS limiting outside the GPU swap mechanism where appropriate.

---

# P9.1: Pipeline Cache Hardening

Audit the pipeline cache key.

It should include every state affecting the WebGPU pipeline:

```text
shader
topology
color format
depth format
blend mode
blend equation
depth test
depth function
depth write
stencil state
culling
color write mask
alpha behavior
sample count
polygon behavior where applicable
```

Incorrectly sharing pipelines can create backend-specific rendering corruption that is difficult to diagnose.

---

# P9.2: Pipeline Prewarming

Avoid first-use shader/pipeline hitches.

Prewarm common pipelines during existing match prewarm:

```text
world opaque
world alpha-test
world translucent
HUD
RTT
cel
outline
tone map
shadow
PBR
post process
```

Do not compile every theoretical combination.

Prewarm observed/common combinations.

---

# P10: Graphics Settings Renderer Selection

Do this only after renderer parity is stable.

Add to:

```text
Settings
  → Graphics
  → Renderer
```

Options should be platform-specific.

### Windows

```text
Auto
DirectX 12
Vulkan
OpenGL
```

### macOS

```text
Auto
Metal
Vulkan (MoltenVK)
OpenGL
```

### Linux

```text
Auto
Vulkan
OpenGL
```

### Android

```text
Auto
Vulkan
OpenGL ES
```

---

# P10.1: Persist Renderer Selection

Add renderer preference to the normal settings schema.

Migration default:

```text
OpenGL
```

until the final activation phase.

After release validation, change new/default installs to:

```text
Auto
```

Existing users should not be forcibly migrated to a modern backend during the experimental period.

---

# P10.2: Startup Fallback Policy

Recommended behavior:

## Auto

Try:

```text
platform preferred backend
    ↓ failure
OpenGL/OpenGL ES
```

Log why fallback occurred.

## Explicit Modern Backend

Try requested backend.

If initialization fails:

- clearly report failure
- allow fallback to OpenGL
- remember the failure for the current startup
- do not repeatedly retry every frame

---

# P11: Backend Capability Reporting

Add useful diagnostics to logs:

```text
selected backend
adapter name
driver
wgpu-native version
surface format
maximum texture size
maximum anisotropy
supported present modes
HDR support
depth formats
timestamp/query support
```

Example:

```text
[render] backend=DX12
[render] adapter=NVIDIA GeForce RTX ...
[render] surface=BGRA8UnormSrgb
[render] depth=Depth24PlusStencil8
[render] anisotropy=16
```

This will make player renderer bug reports dramatically easier to diagnose.

---

# P12: Visual Parity Test Suite

Create deterministic scenes/captures for:

```text
world geometry
alpha-test geometry
transparent objects
HUD
cel shading
player outlines
shadow maps
PBR
bloom
SSAO
SSR
fog
HDR
replay
map preview
hunter preview
```

Reference:

```text
OpenGL
```

Compare against:

```text
DX12
Vulkan
Metal
Android Vulkan
```

Do not require byte-identical images.

Use reasonable tolerance for:

- filtering
- floating-point precision
- vendor differences

Flag structural differences.

---

# P12.1: Resource Lifetime Stress Tests

Repeatedly:

```text
load map
start match
end match
return lobby
start another match
open Replay Studio
close Replay Studio
open Map Studio
close Map Studio
change resolution
change render scale
toggle fullscreen
```

Track:

- textures
- texture views
- buffers
- samplers
- pipelines
- bind groups
- shader modules
- surfaces

Object counts should return near baseline after teardown.

---

# P12.2: Performance Validation

Benchmark each backend with identical settings.

Capture:

```text
average frame time
1% low
0.1% low
CPU render submission time
GPU frame time where available
pipeline creation stalls
texture upload time
VRAM/resource usage
```

Test at:

```text
1080p
1440p
4K
```

and several render scales.

The modern path should not merely render correctly. It should justify the migration.

---

# P13: Dedicated Modern Renderer CI

Keep renderer validation separate from unrelated Project Prime CI failures.

Recommended jobs:

## Windows

```text
DX12 backend probe
DX12 window check
Vulkan backend probe
Vulkan window check
```

## Linux

```text
Vulkan device
Vulkan surface
Vulkan rendered compatibility suite
```

Use Mesa/llvmpipe or Lavapipe as the deterministic software fallback.

## macOS arm64

```text
Metal
MoltenVK when adapter available
```

## macOS x64

```text
Metal
MoltenVK when adapter available
```

## Android

```text
Vulkan wgpu-native build
APK packaging
Android Vulkan surface test
```

---

# P13.1: Modern Renderer Regression Command

Create a single command such as:

```text
-renderfullcheck
```

Internally run:

```text
backend policy
device creation
surface creation
texture upload
mipmap
FBO
depth
stencil
world shader
RTT
blit
outline
HDR
readback
presentation
resource cleanup
```

This provides one canonical renderer acceptance command for local development and CI.

---

# P14: Enable Modern Renderer by Default

Only perform this phase after all previous production gates pass.

Change:

```text
GraphicsBackendPolicy.ModernGameplayRequested
```

semantics so `Auto` activates modern rendering.

Final defaults:

```text
Windows -> DirectX 12
macOS   -> Metal
Linux   -> Vulkan
Android -> Vulkan
```

Fallback:

```text
OpenGL / OpenGL ES
```

---

# Final Activation Gate

Do not flip `Auto` to modern until all of these are true:

- [ ] DX12 window/render test green
- [ ] Windows Vulkan green
- [ ] Linux Vulkan green
- [ ] macOS Metal arm64 green
- [ ] macOS Metal x64 green
- [ ] MoltenVK verified on real compatible hardware
- [ ] Android Vulkan surface implemented
- [ ] Android Vulkan match verified
- [ ] mipmaps implemented
- [ ] trilinear filtering implemented
- [ ] anisotropic filtering implemented
- [ ] shadow maps enabled
- [ ] cel outlines enabled
- [ ] player outlines enabled
- [ ] deferred PBR enabled
- [ ] post-processing enabled
- [ ] HDR enabled
- [ ] TAA/FXAA parity verified
- [ ] SSAO verified
- [ ] SSR verified
- [ ] fog/volumetrics verified
- [ ] Replay Studio verified
- [ ] replay export verified
- [ ] screenshots verified
- [ ] Map Studio verified
- [ ] launcher hunter preview verified
- [ ] fullscreen/resizing stress passes
- [ ] device/surface lost recovery works
- [ ] no meaningful resource leaks
- [ ] high-refresh presentation verified
- [ ] performance benchmark accepted
- [ ] OpenGL fallback verified

---

# Recommended Commit / PR Sequence

Keep this work split into reviewable pieces.

## Commit 1

```text
render: fix WebGPU texture attachment/sample hazards
```

Includes:

- resource hazard tracking
- proper render-pass boundaries
- uncaptured error callback
- Vulkan/Metal window-check fix

## Commit 2

```text
render: complete modern mipmap and sampler support
```

Includes:

- mip chains
- trilinear filtering
- anisotropy
- texture-format validation

## Commit 3

```text
render: enable shadows and outline passes on WebGPU
```

Includes:

- shadow map
- cel outline
- player outline

## Commit 4

```text
render: port deferred PBR to WebGPU
```

Prefer a WebGPU MRT G-buffer.

## Commit 5

```text
render: port graphics post-processing pipeline to WebGPU
```

Includes:

- AA
- sharpening
- bloom
- SSAO
- contact shadows
- SSR
- fog
- dynamic lights
- HDR

## Commit 6

```text
render: complete replay and offscreen WebGPU rendering
```

Includes:

- replay
- killcams
- screenshots
- export
- thumbnails
- movie textures

## Commit 7

```text
render: enable launcher and editor side scenes on WebGPU
```

Includes:

- hunter preview
- Map Studio
- preview scenes

## Commit 8

```text
android: add WebGPU Vulkan presentation surface
```

Includes:

- ANativeWindow
- surface lifecycle
- present/recreate logic

## Commit 9

```text
render: add device-loss and presentation recovery
```

Includes:

- device lost
- surface lost
- resize
- fullscreen
- present modes

## Commit 10

```text
render: add renderer selection and fallback policy
```

Includes Graphics UI and persistence.

## Commit 11

```text
render: add cross-backend parity and stress validation
```

Includes CI and capture tests.

## Commit 12

```text
render: enable modern backends for Auto renderer selection
```

Final production activation.

---

# Priority Summary

## P0: Blocking

1. Fix WebGPU resource conflict
2. Fix Vulkan window check
3. Fix Metal window check
4. Add controlled WebGPU error/device-lost handling
5. Harden render-pass boundaries

## P1: Renderer Fidelity

6. Mipmaps
7. Trilinear filtering
8. Anisotropic filtering
9. Texture-format parity

## P2: Core Graphics

10. Shadows
11. Cel outlines
12. Player outlines
13. Deferred PBR

## P3: Advanced Graphics

14. AA/TAA
15. bloom
16. sharpening
17. SSAO
18. contact shadows
19. SSR
20. volumetric fog
21. dynamic lights
22. HDR

## P4: Supporting Renderers

23. Replay Studio
24. replay export
25. screenshots/readback
26. Map Studio
27. movie textures
28. launcher hunter preview

## P5: Platforms

29. Android Vulkan surface
30. Android lifecycle
31. fullscreen/resize recovery
32. device/surface-lost recovery

## P6: Production

33. renderer UI
34. automatic fallback
35. parity suite
36. stress suite
37. performance validation
38. CI gate
39. modern renderer becomes `Auto`

---

# Definition of Done

`feature/modern-rendering-backends` is complete when a player can select or automatically receive the appropriate modern backend, play a complete Project Prime session, use Replay Studio and Map Studio, switch resolutions/fullscreen, and return to the lobby without rendering differences, crashes, significant leaks, or gameplay behavior changes.

At that point the renderer architecture becomes:

```text
                Project Prime renderer
                         │
                    GraphicsApi
                         │
              Modern compatibility layer
                         │
                     WebGPU
          ┌──────────────┼──────────────┐
          │              │              │
        DX12           Vulkan          Metal
          │              │              │
       Windows      Win/Linux/Android   macOS
                         │
                      MoltenVK
                         │
                        macOS
```

with:

```text
OpenGL / OpenGL ES
```

remaining as the compatibility escape hatch rather than the primary rendering path.