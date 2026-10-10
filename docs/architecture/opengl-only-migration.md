# OpenGL-only architecture migration

**Baseline:** published `v0.1.47`, commit `0351ef6f707ab0369c95d8d4185869f6e02622f8`.
**Status:** Phase 1 OpenGL-only runtime policy plus Phase 2 physical source/package cleanup are implemented on separate stacked pull requests. Full CI and real hardware acceptance are not yet confirmed.

## Phase 1: OpenGL is the only launchable game renderer

- Windows, Linux and macOS use desktop compatibility OpenGL. Android uses the
  shared engine's OpenGL ES 3.0 adapter.
- `Auto` and absent preferences resolve to OpenGL everywhere. Persisted
  DirectX 12, Vulkan and Metal selections are migrated to OpenGL on settings
  load. The retired renderer startup-recovery marker is cleared.
- Explicit `-renderer dx12`, `vulkan`, `metal` and their aliases fail with a
  descriptive error. The old enum values remain solely to diagnose historic
  requests for compatibility with saved settings and clear CLI errors.
- The renderer selector is removed from the settings view. There is no longer a
  modern-device startup or NoAPI window path for desktop auxiliary windows.
- The mandatory renderer-policy test now rejects modern selections on every
  supported platform, and CI runs an actual Linux Mesa/OpenGL framebuffer
  smoke instead of modern-native smoke jobs. The macOS build/release jobs
  retain the content-free policy check and an Intel OpenGL texture fixture.

## Not removed in phase 1

This change **does not yet delete** `ModernGraphicsCompat*`, WebGPU shader
generation, WebGPU bindings, the pinned native libraries or legacy advanced
post-processing. Those symbols are still referenced throughout the client,
Android head, launcher/Studio tooling and older acceptance checks. Deleting
them in the first cut would obscure compile/runtime failures in a very large
changeset. Release packaging may still contain unused native renderer
dependencies until phase 2. The runtime policy refuses to select them.

## Phase 2: physical removal (PR #434, stacked on #433)

1. Replace all `ModernGraphicsCompat.Active` dual-path call sites with the
   direct desktop OpenGL / Android GLES implementation.
2. Delete modern-only renderer classes, WGSL shader generation, device/surface
   adapters, GPU-indirect/visibility machinery and modern-only diagnostics;
   preserve the shared `LegacyGeometryBatch`, room culling, retained packet
   extraction and state/material caches for GL.
3. Remove Silk.NET WebGPU and MoltenVK package references, bundled native
   `wgpu_native`/`MoltenVK` libraries, Android Vulkan packaging, modern-only
   CI and macOS notarization/signing assumptions.
4. Update `tools/check-macos-build.sh`, `tools/test-macos-tools.sh`, and release
   APK/runtime validation to expect only necessary OpenGL and KTX assets.
5. Validate main-window, launcher, thumbnails, Map Studio, Replay Studio,
   replay export, fullscreen/focus/resize and Android pause/resume. Only then
   remove the temporary policy shims.

## Phase 3: graphics cleanup and performance engineering

### Slice A: retire experimental postprocessing (PR staged on main)

- The UI now exposes native lighting/fog, render scale, texture sampling,
  authored material maps, player/cel outlines and separately selectable
  directional shadows. Retired TAA/HDR/bloom/SSAO/reflections/volumetric
  postprocessing controls are no longer displayed.
- Settings schema 10 migrates every retired effect to a neutral value, even for
  same-schema JSON, while preserving actual native graphics preferences and
  authored texture/character options.
- Quality presets change render scale and texture sampling, not fullscreen
  effects. They no longer implicitly activate shadows.
- The GL frame graph no longer executes the deferred-PBR buffer build. The
  remaining directional-shadow composition keeps the gameplay scene and
  HUD compositor intact while no longer filling temporal history or binding
  deferred PBR textures. HDR tone-map shaders are not compiled for that path.
- The shadow-only composite skips GPU work when the map/depth is unavailable.
  Turning shadows off deletes the shadow framebuffer and up-to-4K depth/color
  attachments rather than keeping their allocations for the rest of the match.
- Existing retired effect storage/code remains only as compatibility scaffolding
  for a later physical-delete slice; its runtime processing is disabled.
- This is a correctness and pass-count reduction, **not** a measured FPS gain.
  Validate map shadows, player/cel outlines, HUD, beams and focus/fullscreen,
  and compare p95/p99 frametimes before claiming an improvement.

### Next slices


- Retire optional post effects and remove their framebuffers and user-facing
  controls only after proving native world/depth/stencil, player outlines,
  beam/glow, HUD and compositor remain visually correct.
- Adapt retained geometry packet submission to GL VBO/IBO + compatible draw
  batching, preserve order-sensitive translucent/stencil passes, and profile
  persistent geometry and texture residency.
- Measure actual frame wall time, p95/p99/1% lows, CPU submissions, GL state
  changes and present costs at 60/120/144/240+ FPS on real hardware. Keep the
  fixed 60 Hz gameplay/network/replay simulation clock unchanged.
- Verify transparent walls and pickup occlusion, shadow persistence, custom
  maps, viewmodels, scene transition and high-refresh camera/HUD coherence.

## Release gates

An OpenGL-only release requires compiled Windows/Linux/macOS/Android targets,
native GL/GLES smoke or clearly documented unavailable hardware gates, a
device-level high-refresh and Android lifecycle pass, and no accidental WebGPU
native dependencies in shipped artifacts after phase 2. Do not infer a speedup
from code removal alone. Preserve the original v0.1.47 rendering baseline for
before/after metrics.
