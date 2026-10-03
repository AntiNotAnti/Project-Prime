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
has not met the performance gate. These stored captures predate the frame-pipeline
optimization below, so they remain the historical baseline rather than evidence
for the optimized path. Three-pass PBR at native scale remains a separate opportunity.

## Frame-pipeline optimization (2026-10-02)

The shared WebGPU compatibility path now attacks the CPU submission bottleneck
without changing simulation or graphics features:

- compatible core draws coalesce into one render pass until a real attachment or
  resource-usage boundary requires it to end;
- transient uniforms use aligned 4 MiB arena pages and transient geometry uses
  shared 2 MiB vertex / 512 KiB index pages instead of one GPU buffer per draw;
- arena CPU staging is flushed once per dirty page immediately before QueueSubmit;
- steady-state compatibility bind groups are cached by deterministic draw slot and
  resource fingerprint;
- dynamic texture updates up to 4 MiB stage through CopyBufferToTexture in command
  order, avoiding the previous forced QueueSubmit before every small UI/video update;
- the command batching ceiling is 1,024 operations, while readback, presentation,
  recovery and explicit hazards remain hard submission boundaries;
- benchmark samples now expose core draw count, core render-pass count and staged
  texture-upload count in addition to queue submissions, buffer writes and bind groups.

This section makes no FPS claim yet. Re-run the long Extreme benchmark on Apple
Metal, Windows DX12/Vulkan and physical Android Vulkan hardware before closing the
performance gate.

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


## Renderer bug audit (2026-10-01)

Audited on `main` at b152ffbf with local fixes. The scissor regression failed
before the fix: `(-1, -1, 2, 2)` colored adjacent pixels instead of only the
bottom-left pixel. The shared modern renderer now intersects both rectangle
endpoints with the target, preserves empty rectangles, and applies scissor to
framebuffer blits. Copy-to-texture and mip generation remain independent of
scissor state, as required by their GL contracts.

Copies between HDR/BGRA/RGBA formats now convert through a render pass instead
of submitting an incompatible raw texture copy. Copies and blits that sample a
window surface first stage its pixels into a sampleable texture: presentation
textures are only requested with render-attachment/copy-source usage. Regression
checks compare copied pixels and untouched neighbors, including HDR and window
sources, and check that blits respect scissor while copies ignore it.

The game window no longer reinstalls a GLFW callback that throws through native
frames for errors other than FeatureUnavailable. All desktop windows share a
rooted, non-throwing diagnostic callback. The full check deliberately invokes
SwapInterval on a NoAPI window and verifies that GLFW's NoContext error returns
normally. Native window creation still performs OpenTK's managed failure check.
This removes a native-abort path; it does not prove the cause of a reported
NVIDIA startup failure without that machine's failing-launch diagnostics.

The earlier diagnostic fixes are retained: flushed native-startup checkpoints,
wgpu logging, explicit `-debuglog` support for renderer probes, and undefined
(rather than zero) depth slices for 2D render attachments.

Validation of the final renderer code:

- Full policy/device/window/render/recovery checks pass on Metal and MoltenVK
  on an Apple M4 Pro.
- The expanded full check passes on Linux ARM64 with Mesa 25.2.8 llvmpipe,
  Xvfb/X11 and GLFW 3.4. The isolated test container built GLFW for ARM64;
  the shipped Linux x64 package uses its normal bundled GLFW.
- OpenGL texture-update/readback/HDR checks pass on macOS and Linux Mesa.
- Windows x64 and Linux x64 self-contained publishes pass; Windows GUI subsystem
  and both packages' no-game-assets checks pass.
- A supplied Windows Radeon R3 diagnostic log reports exit code 0 on the earlier
  diagnostic build. This is not runtime verification of these later fixes, the
  RX 6700 XT, or NVIDIA proprietary drivers/Wayland.

The full check deliberately forces device loss and OpenGL recovery. Its native
log can therefore contain "forced repeated device loss acceptance check" and,
with these new checks, an intentional GLFW NoContext error. Judge that test by
its final PASS and exit code; those injected failures are not game-launch crashes.


## Large authored texture / startup recovery hardening (2026-10-02)

A user report on Windows/RTX 4070 showed a 4K map replacement rendering normally
on OpenGL but becoming horizontally corrupted on Vulkan, while selecting DX12
could prevent the next launcher window from opening. Modern mip generation now
uses a transient render target followed by a texture-to-texture copy for each mip
level instead of sampling and rendering different subresources of the same native
texture in one pass. The asset-free window check now exercises a 4096-wide,
high-frequency texture through the full trilinear mip chain.

Preference-driven modern startup also writes a small crash fence before the NoAPI
window/device path. A managed failure still falls back immediately; if a native
driver abort prevents managed recovery, the next launch detects the unfinished
backend startup and opens with OpenGL instead of repeating the crash. Explicit
diagnostic/command-line renderer selection is not fenced.

Run `ProjectPrime -renderwindowcheck -renderer vulkan` or `-renderer dx12` to exercise the staged 4096-wide mip regression and backend startup path without game assets.


## HD companion residency optimization (2026-10-02)

Normal/specular/emissive replacement maps now load only while
`AdvancedMaterials` is enabled. The live toggle has a companion-only refresh:
turning it off releases companion GPU objects and removes queued companion work
without rebinding the resident HD albedo. Already-running decodes stay tracked
until completion so they cannot create hidden memory concurrency; if maps are
still off their result is discarded, while a rapid re-enable can reuse the same
deduplicated work. Turning the option back on rebuilds only missing companions. When advanced maps are disabled, a file-backed or
immutable package-backed albedo no longer has to wait for companion sources to
qualify for background decode.

Packaged `.ppmap` materials now reuse the package validation performed when the
map definition is loaded. Subsequent asset reads use a bounded single-entry ZIP
path while the archive length/timestamp remain unchanged; a changed package
invalidates the shortcut and falls back to full validation. Queued material
requests retain only dimensions and identity; encoded entry bytes are reopened
lazily on the decode worker instead of accumulating a compressed copy of the
whole texture pack in memory. Optional companion channels are not read at all
while Advanced Materials is disabled, and concurrent reads use independent
archive handles.

## Supersampled modern-graphics optimization (2026-10-02)

High-end custom settings exposed avoidable multiplicative costs above 100%
render scale. The world is still rendered at the requested supersampled size,
but the depth-driven post-process stack now resolves once at presentation size
when temporal history is not active. AO, contact shadows, reflections, bloom,
HDR tone mapping and the deferred material composite therefore no longer shade
every supersampled pixel only to be downsampled by the final composite.

Deferred PBR keeps the existing exact forward-depth contract at native scale.
When the world target is larger than presentation, its three compatibility
G-buffer replays use a presentation-sized color/depth target instead. The first
albedo replay builds local visibility depth and the normal/material passes use
exact depth equality against that local surface. This removes the supersampling
multiplier from the PBR raster workload without changing simulation or the
requested world render scale.

TAA history is now allocated and copied only when the temporal resolve can
actually consume it. HDR or an available deferred PBR buffer previously caused
the shader to reject TAA while the CPU/GPU still maintained its history texture;
those combinations now use the existing SMAA resolve and skip the dead history
copy. These are implementation optimizations, not a new measured performance
claim; hardware benchmark evidence should be refreshed before closing the
performance gate.
