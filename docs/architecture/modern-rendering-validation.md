# Modern rendering validation — 2026-09-30

Host: Apple M4 Pro, macOS arm64, .NET SDK 10.0.401, pinned wgpu-native
33133da4ec5a0174cb21539ef2d3346f75200411 plus the AppKit-view surface extension. Results apply to this working tree;
CI and real-device results must be recorded separately.

| Check | Observed result |
| --- | --- |
| Desktop Release build | Pass |
| Android Debug build and embedded ARM64 APK | Pass; final APK installed and tested on API 36 ARM64 emulator |
| Metal asset-free full check | Pass: policy, native probe, surface, mipmaps, world/RTT, depth/stencil, outline, HDR, readback, forced reconstruction, repeated allocation/resize cleanup, failed recovery to fresh GL |
| Linux ARM64 Vulkan full check | Pass on Mesa 25.2.8 llvmpipe, including failed recovery to fresh GL |
| MoltenVK full check | Pass on M4 Pro with bundled driver; AppKit surface and loader alias fixes verified without environment overrides |
| Room / G-buffer parity | 24/24 on Metal and 24/24 on MoltenVK against OpenGL; [Metal metrics](render-validation/room-opengl-metal.json), [MoltenVK metrics](render-validation/room-opengl-moltenvk.json) |
| Map Studio | 48 checks pass on OpenGL and Metal |
| Map screenshots | 11/11 tolerance comparisons pass; [metrics](render-validation/map-opengl-metal.json) |
| All-effects combat/respawn | 9/9 cases, 4,935 frames, 3,106 checked, 4,916 advanced frames, 10 deaths/respawns; zero black frames/errors/invariant failures; 14 boundary-poison comparisons pass; 18 resize/scale/cel transitions each |
| Metal replay cadence | 60/120/144/240/360/540 Hz, 600 identical gameplay frames per cadence, no drops/stalls |
| Metal hunter/cosmetic previews | 252 skin/armor/death previews, 63 model-mode captures, 600 death frames, all seven hunters’ real-player paths pass |
| Metal launcher transitions | Pass: full shell capture sequence, UI map selection, match, bot rematch, next map, fullscreen/borderless/windowed and return-to-lobby; zero missed controls after updating the stale map-picker selector |
| Shader generation | Fresh shared-source export regenerates all eight files deterministically |
| Dedicated server build | Pass |
| Metal replay export | Pass: 720p/4K, seven output rates, repeated samples, fractional frames, gameplay invariance, queue/reel/cancellation |
| Android Vulkan emulator | Pass: device/surface/acquire/draw/readback/present; background/foreground recreates surface and renders frame 2 |
| Windows DX12/Vulkan, macOS x64 | CI configured; not executed locally |
| Android physical device | Not tested |

The all-effects check explicitly enables HDR, deferred PBR, TAA, high SSAO,
contact shadows, reflections, fog/volumetrics and dynamic glow and rejects silent
feature refusal. The frozen-room captures cover individual effects and all three
shadow sizes. They do not replace moving/combat visual review across multiple maps.
Comparison thresholds also bound the fraction of significantly changed pixels;
this caught PBR errors that mean-color error alone could hide.

## Performance gate remains open

The frozen-room benchmark uses the same Extreme preset on each backend, at
1920×1080, 2560×1440 and 3840×2160 with 75% and 100% render scale. It records
120 samples after 20 warmups. Completed time includes a synchronous GPU wait;
submission time excludes that wait. Present is excluded. GPU timestamp queries
are reported as unavailable, not approximated by CPU time.

[Metal results](render-validation/metal-m4pro-extreme.json) are approximately
18–26 ms completed per frame after batching (previously 24–27 ms); [OpenGL results](render-validation/opengl-m4pro-extreme.json)
are approximately 7–12 ms. These are development measurements, not release-quality
percentile estimates (especially 0.1% with only 120 samples). Modern rendering
has not met the performance gate. Native command encoders submit in batches of
64 operations with distinct pooled uniforms/geometry per draw. Three-pass PBR
and further CPU submission work remain opportunities for improvement.

## Unverified release requirements

- Cross-platform visual/combat parity and physical Android lifecycle stress.
- Full feature-by-feature visual coverage, old replay corpus, monitor switching,
  sustained match/lobby/editor/replay transitions and detailed native live-object accounting.
- Auto failed reconstruction now closes the failed desktop session and starts a
  fresh OpenGL launcher process. The forced-loss test verifies fresh GL context
  rendering; automatic launcher process restart still needs end-user fault-injection
  acceptance. Android returns to its error flow and selects GLES for the next session.
- GPU timestamp profiling, longer benchmarks, and performance acceptance.
- Default activation: intentionally withheld pending the original plan’s gates.

Generated images and game assets remain outside the repository.

## Shared texture updates and sampler reuse (2026-09-30)

A manual Apple Silicon playtest using Metal was reported to run well by the
user. This is additional gameplay evidence, not Windows or Android acceptance.

Partial texture uploads now preserve pixels previously written by the GPU.
The modern compatibility layer uploads only the addressed rectangle, preserving
queue order and framebuffer orientation, including RGBA16F conversion. Previously
it re-uploaded a stale full CPU image, potentially erasing the rest of a render
target. Invalid rectangles and short sources are rejected before CPU mutation.
Repeated effective filter/wrap/anisotropy settings no longer invalidate native
samplers. Both fixes apply to the shared Metal/Vulkan/DX12 implementation.

`-textureupdatecheck -renderer opengl|metal|vulkan|dx12` exercises queued clears,
two distinct partial updates, row orientation, untouched GPU pixels and HDR.
The pixel assertions pass locally on OpenGL, Metal and Vulkan through MoltenVK.
The modern full check includes these assertions; the macOS CI job also runs the
OpenGL reference. Backend-state checks cover atomic rejection and sampler reuse.
These changes have no measured frame-rate claim and do not close the performance
gate or substitute for native Windows/Android gameplay testing.

The full Metal and MoltenVK suites passed after the texture fix, including
resource lifetime and forced device-loss recovery. Windows x64 self-contained
publish and Android ARM64 Debug build also passed (Android: 88 warnings,
0 errors). Those are compile/package checks, not Windows or Android runtime
validation. Full-image video/UI uploads reuse CPU storage without an additional
full-frame staging allocation; the texture diagnostic covers this path too.

## PR implementation audit

The final audit added cached triangle-edge wireframe, bind-group lifetime counts,
and opt-in benchmark counters for pipeline creation, texture upload submission,
and tracked resource storage. Wireframe/fill switching, upload/counter checks,
resource teardown (including zero retained bind groups), and forced recovery pass
on Metal and MoltenVK. OpenGL texture-update reference checks also pass.

The benchmark instrument smoke covered all six resolution/scale combinations
with 30 measured frames each. Startup and warmup pipeline events, upload bytes,
and storage estimates were populated. This run overlapped compilation and is
not a comparative performance result. The default benchmark now measures 1,200
frames; the older 120-frame JSONs above remain historical development evidence.

The Android timing policy has 21 passing deterministic checks. The implementation
scope audit and explicit limitations are in modern-rendering-backends.md. The PR
remains experimental with hardware/release acceptance pending; it does not
change the default renderer.
