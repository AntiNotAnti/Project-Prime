# Project Prime renderer modernization baseline (R0)

Status: R0 evidence recorded before any SDL GPU resources or desktop cutover,
plus the current local R9.2 and bounded R10-R12 source checkpoints. SDL is now
the only desktop renderer. The historical sections below retain the original
legacy evidence; current source, build, and focused-test evidence remains distinct
from live visual acceptance.

## Target and host

| Item | Observed value |
|---|---|
| Repository | `/Users/jarrett/Documents/Development/Project-Prime` |
| Revision | `7a59474d0bc331b7309a8543bee193cf11b2ac0f` |
| Host | macOS 26.6.1 (25G76), Darwin 25.6.0, arm64 |
| GPU | Apple M4 Pro, 16 GPU cores, Metal 4 |
| Display | Built-in Liquid Retina XDR, 3024 x 1964 Retina; refresh rate is not recorded by the available system report |
| Runtime | .NET SDK 10.0.400, repository `global.json` |
| Legacy window | 1280 x 768 request, OpenGL 3.2 compatibility profile, hidden until load |
| R0 default backend | `legacy` |

The observed host's legacy OpenGL compatibility context is not available on
macOS. The existing native probe therefore aborts before gameplay; no baseline
screenshot is claimed. A core-profile substitution is not a valid baseline
because the current renderer still depends on immediate mode and display-list
semantics.

## Canonical visual coverage

The existing content inventory and rendering docs are the source for the scene
set; they do not imply that a scene was successfully captured on this host.
The candidate matrix is:

| Coverage | Candidate source scene/map | Existing evidence |
|---|---|---|
| Indoor room, opaque geometry, decals | `MP1 SANCTORUS` | `docs/MULTIPLAYER_CONTENT_BASELINE.json` |
| Large/outdoor room and animated movers | `MP3 PROVING GROUND` | `docs/MULTIPLAYER_CONTENT_BASELINE.json`, `docs/G1_RENDERING.md` |
| Ice/fog and translucent material paths | `MP9 CRYOCHASM` | room catalog/material parser and current `Renderer` passes |
| Effects, beams, particles, morph-ball trail | retail room plus a controlled beam/effect fixture | `Renderer.GetDrawItems`, `EffectPresentation`, `BeamProjectileEntityPresentation` |
| HUD, scan/visor overlays, cel outline | player camera in any retail room | `Renderer.OnRenderFrame`, HUD presentation, `RenderOptions` |
| Custom map geometry and thumbnail path | `DUST2` and `PARALLAX` | `docs/MULTIPLAYER_CONTENT_BASELINE.json`, `README.md` |
| Minimal deterministic harness | `TEST ARENA` | content inventory; it has no import source and is not a cooked bundle |

The full map catalog is intentionally not reduced to a single “golden map”:
the existing inventory records 27 retail rooms plus `TEST ARENA`, `DUST2`, and
`PARALLAX`, with known missing First Hunt data. A later visual gate must capture
fixed cameras for the matrix above on a machine with a compatible legacy
context, then repeat the same cameras through SDL.

## Metrics inventory

The fixed-step timing implementation already exposes the values needed for a
baseline, without changing simulation ownership:

| Metric | Existing source | Status |
|---|---|---|
| Simulation frequency | `Mods.Render.FrameTiming.MeasuredSimulationHz`, `SimulationHz = 60` | available |
| Render/draw frequency | `MeasuredFrameHz`, `ScenePresentation.FramesPerSecond` | available |
| Simulation steps | `TotalSteps`, `StepsThisFrame`, `StepHistogram` | available |
| Rendered frames | `TotalFrames` | available |
| Dropped steps | `DroppedSteps` | available |
| Stalls | `Stalls`, `Discontinuities` | available |
| Resolution/render scale | `ScenePresentation.Size`, `RenderSize`, `RenderOptions.ResolutionScale` | available |
| CPU frame time | not currently emitted; derive from a future backend-owned timer | missing from baseline output |
| Draw frequency/call count | no stable counter; GL call sites are inventoried below | missing from baseline output |

No performance target is set in R0. A future capture must log these values for
each canonical scene and preserve the 60 Hz simulation hash/command stream.

## OpenGL and windowing inventory

The repository-wide search covered `OpenTK.Graphics.OpenGL`, `GL.`,
`OpenTK.Windowing`, `GameWindow`, `ReadPixels`, and `SwapBuffers`. Every hit was
classified as follows; calls in shared client files are linked explicitly into
Android and therefore remain in scope.

| Classification | Files/call sites | R0 treatment |
|---|---|---|
| Desktop renderer, passes, resources, shaders | `src/Client/Rendering/Renderer.cs`, `src/Client/Rendering/RenderWindow.cs` | legacy compatibility path; R4+ SDL host/backend |
| Android GLES translator and lifecycle | `src/Client/Rendering/GlEs.cs`, `src/Android/GameView.cs`, `src/Android/AndroidMatch.cs`, `src/Android/PreviewRun.cs` | retained; R2 now shares `MeshCompiler` topology conversion |
| Android capture | `src/Android/AndroidScreenCapture.cs` | retained with SurfaceView/EGL lifecycle |
| Desktop capture/readback | `src/Client/Rendering/ScreenCapture.cs`, `src/Client/Runtime/ScreenCapture.cs` | final-composite target documented below; no SDL resource work in R0 |
| Thumbnail capture and offscreen harness | `src/Client/Runtime/ThumbnailCapture.cs`, `src/Client/Runtime/ThumbnailGenerator.cs` | legacy-only until capture migration |
| Headless/render test harnesses | `src/Client/Networking/AuthoritativeCheck.cs`, `ReplayPlaybackCheck.cs`, `MapAudit.cs`, `WeaponDps.cs` | source/test harnesses; no live SDL claim |
| Shader/binding compatibility adapters | `src/Client/Rendering/EsBindings.cs`, `EsShaders.cs` | Android-specific translation layer |
| Desktop window/input | `src/Client/Rendering/RenderWindow.cs`, `src/Client/Input/GamepadDesktop.cs`, `GamepadProbe.cs`, `src/Client/Runtime/WindowMode.cs`, UI/input files using OpenTK window types | remains OpenTK until the SDL platform-host slice |
| Non-render OpenTK math/domain references | `src/Game/Gameplay/Hunters/PlayerEntityNetAim.cs` and OpenTK.Mathematics usage across Game/Client | retained; OpenTK.Mathematics is a hard invariant |

The authoritative current frame ordering remains documented in
`docs/G1_RENDERING.md`: input/network, fixed simulation steps, presentation
submission, render, successful swap acknowledgement, then after-render work.
No selector or renderer change may move those boundaries.

For audit reproducibility, the `OpenTK.Graphics.OpenGL` hit set at this
revision is exactly:

```text
src/Android/AndroidScreenCapture.cs
src/Android/GameView.cs
src/Client/Networking/AuthoritativeCheck.cs
src/Client/Networking/ReplayPlaybackCheck.cs
src/Client/Networking/MapAudit.cs
src/Client/Networking/WeaponDps.cs
src/Client/Rendering/GlEs.cs
src/Client/Rendering/RenderWindow.cs
src/Client/Rendering/Renderer.cs
src/Client/Rendering/ScreenCapture.cs
src/Client/Runtime/ScreenCapture.cs
src/Client/Runtime/ThumbnailCapture.cs
```

The additional `GL.` adapter call sites are `src/Android/AndroidMatch.cs`,
`src/Android/PreviewRun.cs`, `src/Client/Rendering/EsBindings.cs`,
`src/Client/Rendering/EsShaders.cs`, and
`src/Client/Runtime/ThumbnailGenerator.cs`; they are covered by the Android,
shader, and thumbnail rows above. OpenTK windowing types occur in the desktop
host, input probes, UI pointer ownership, and the four network/content visual
harnesses; they are platform-host dependencies, not hidden GPU submission
paths. The complete file search used for this inventory was:

```text
rg -l "OpenTK.Graphics.OpenGL|\bGL\.|OpenTK.Windowing|GameWindow|ReadPixels|SwapBuffers" src tests tools
```

## Development selector

The client parses the internal switch `--renderer=legacy|sdl` through
`RenderBackendSelection`. At R0 the default was `legacy`; R9.1 changes the
shipping default to `sdl`, while retaining `--renderer=legacy` as a temporary
explicit fallback. Unknown values are rejected without changing the selected
backend. The switch is not exposed in normal launcher settings. Thumbnail child
processes always receive the parent's exact canonical selection.

## Final-composite capture decision

The owned visual acceptance target is the **final composited swapchain image**:
the scene render target has been composited, HUD/helmet/fade/visor/post-process
layers are complete, and capture occurs immediately before the successful
present/swap acknowledgement. Scene-only offscreen textures remain diagnostic
targets, not visual-parity screenshots. SDL resource work must implement this
same ownership and timing; it must not capture an intermediate scene target or
change `OnFramePresented` semantics.

## Baseline evidence and results

The following evidence was available at R0. Counts labelled “pre-change” are
the recorded baseline suite, not a claim that the dirty networking worktree is
clean.

| Check | Result |
|---|---|
| `dotnet build Game.sln -c Release --no-restore -m:1` at this checkout | **PASS**, 0 warnings, 0 errors |
| Pre-change main C# suite with `GAME_DATA_DIRECTORY=.../AMHE1` | **934/934 passed** |
| Pre-change content-free suite | **886/886 passed** |
| Pre-change Node suite | **109/109 passed** |
| Pre-change Backend suite | **192 passed**, 4 PostgreSQL skips |
| Pre-change Imaging suite | **18/18 passed** |
| Pre-change Python tooling | **67/67 passed** |
| `python3 tools/check-project-boundaries.py` pre-change | **0 violations** |
| Renderer modernization focused tests | **14/14 passed** |
| Android managed build in this host | **BLOCKED**: `NETSDK1147`, Android workload is not installed |
| Live legacy visual baseline on this host | **BLOCKED**: macOS OpenGL compatibility profile unavailable before gameplay |

The `ppy.SDL3-CS` candidate was inspected at exact package version
`2026.722.0` (repository commit
`7f836c9f21dad8ee68e70432e5b7d38ceae47eaa`); its package contains SDL3 native
assets for the current desktop RIDs, and a scratch net10.0 device probe on this
Apple M4 Pro reported `driver=metal`, `formats=MSL|METALLIB`, with Metal API
validation enabled. R0 deliberately does **not** add or pin the package because
no compiling SDL code or tests consume it yet. The exact version becomes a
repository dependency only in the first slice that compiles against it.

## Acceptance boundary

R0 is source/build/test characterization plus honest platform limitations. It
does not claim screenshots, rendered-client parity, Windows/Linux execution,
Android device acceptance, live SDL GPU submission, or a performance target.
Those are later gates after the CPU submission/compiler/material slices and the
SDL host/backend exist.

## Current implementation checkpoint (R0-R12 bounded local, 2026-09-09)

This checkpoint supplements the original R0 record above. It records the local
R9.1 shipping-default, R9.2 frame-pacing work, and the Android shared-frontend
and GLES world-backend migration. It is not full R9 platform or live Android
device acceptance.

| Roadmap slice | Current evidence |
|---|---|
| R0 | The baseline, selector contract, ownership decisions, and limitations above remain the historical reference. Legacy was the default renderer at this checkpoint. |
| R1-R3 | Client-owned sealed frame submissions, CPU mesh compilation/dynamic primitives, and backend-neutral material, pipeline, sampler, texture, and capture descriptions are implemented and covered by focused tests. |
| R4 | The SDL desktop host owns window, input, resize, and presentation lifecycle. It was introduced behind explicit `--renderer=sdl` selection before the R9.1 default change. |
| R5-R6 | SDL GPU device/resource ownership, fenced frame slots, caches, owned scene/final targets, and canonical HLSL are implemented. Checked-in SPIR-V, MSL, and DXIL artifacts carry pinned generation provenance; CI now rejects stale or missing generated shaders. |
| R7 | The SDL backend encodes the six ordered world passes, HUD scene items, cel surface/outline work, disruption/whiteout, composite, ordered overlays, and fade. The final composite remains backend-owned and the swapchain is presentation-only. |
| R8 | Backend-neutral capture requests/results, bounded asynchronous SDL GPU readback, final/scene/thumbnail targets, recording delivery, and the SDL offscreen tool-host path are implemented. Thumbnail, map-audit, replay-playback, authoritative, and weapon-DPS visual probes consume the neutral capture path. Presentable utility probes flush only an explicitly requested readback after successful submission, while gameplay readback remains asynchronous; returned probe captures are consumed independently of request cadence. A real AMHE1 thumbnail now completes through SDL/Metal and saves a non-black 640x360 PNG; the remaining utility outputs stay acceptance gates below. |
| R9.1 | SDL became the shipping default while the legacy selector remained temporarily available. The texture-upload boundary uses an exact `Marshal.Copy` path for both array-backed and general `ReadOnlyMemory<byte>` sources. The compatible `ppy.SDL3-CS` managed binding excludes its development-snapshot native assets; desktop packages instead pin the stable SDL 3.4.16 Windows, Linux, and macOS native families. A fresh Apple Silicon package using that runtime produced the accepted thumbnail hash on its first process, removing the black cold-start behavior reproduced with the prior SDL 3.5.0 development snapshot. R11 subsequently removed the fallback. |
| R9.2 local | Visible display-rate mode uses VSYNC acquisition without a software wait. Explicit caps select IMMEDIATE when supported and use an absolute-deadline software upper bound. When IMMEDIATE is unavailable, the one-time-logged `vsync-fallback` leaves software pacing disabled so Windows cannot wait once for the requested cap and again for VSync; in that fallback mode the requested explicit cap is display-rate limited. Minimized operation still paces at 60 Hz because no drawable swapchain is available to provide the bound. The FPS sample window begins at the first successful presentation, excluding scene construction and content loading. On this 120 Hz Apple Silicon/Metal host, bounded room runs stabilized at 120.0 Hz in display mode, 30.0 Hz at cap 30, 60.0 Hz at cap 60, and about 120 Hz at cap 240 (the requested cap remained an upper bound); the latter three runs held approximately 60 Hz simulation with no stalls or post-start dropped steps. This is local windowed evidence, not the cross-platform soak gate. |
| R10 local source checkpoint | Android now has an explicit `Gles` runtime identity and no longer inherits the desktop SDL default. Its existing custom SurfaceView/EGL render thread, context and surface ownership, pause/resume behavior, display-rate request, and successful-swap acknowledgement remain unchanged. Model, effect, world, and HUD model geometry is prepared through the shared `CpuMesh` frontend and drawn through direct indexed GLES uploads. Static buffers are cached by geometry identity plus mesh reference/revision and released on model unload. Dynamic geometry uses bounded streaming storage; converted fullscreen, cel, composite, fade, HUD, crosshair, radial, flat-box, and N-gon draws use a single-writer bounded scratch mesh after warmup. Android-active code no longer calls `GL.Begin`, `GL.End`, `GL.NewList`, or `GL.CallList`, and those compatibility APIs and their list/batch state have been removed from `GlEs`. The Android-only `GlesBackend` now executes the six ordered world passes from the sealed `RenderFrame`, frozen `RenderMaterial`, captured transforms/lights/options, frame texture averages, and captured `CpuMesh` resources. It does not call back into the scene or recompile dynamic primitives during repeated passes. GLES-native texture names remain in a sealed per-frame bridge while the existing uploader/cache stays authoritative. This completes the local R10.1/R10.2 source objective; live device visual/lifecycle acceptance and any future fully neutral HUD/post packet remain separate gates. |
| R11 local source checkpoint | Per the user's explicit override of the remaining external R9 acceptance gate, SDL is now the sole desktop host/backend. The legacy selector, `RenderWindow`, `GameWindow`/`NativeWindow` adapters, compatibility-context creation, desktop GL execution, display lists, immediate mode, and GLSL 1.20 sources are removed. The desktop Client references no `OpenTK.Graphics` or `OpenTK.Windowing.Desktop` assembly; it retains OpenTK mathematics, OpenAL, and the R4.4-compatible key/mouse/controller value types. GLFW remains only behind the existing controller compatibility/probe code and creates no window or graphics context. Android retains its permitted OpenTK ES/enum bindings and standalone GLES shaders. Compiled-assembly and publish-package tests reject reintroduction of the desktop GL/GameWindow dependencies. |
| R12 local source checkpoint | Per the user's explicit instruction to proceed despite the remaining external parity gate, the neutral graphics settings now expose Original, Enhanced, and Performance presets plus independent texture filtering, anisotropy, MSAA, selective bloom, and visual-light controls. Original remains the default until performance and visual evidence support changing it. The SDL backend builds full mip chains once per texture revision, negotiates anisotropy by deterministic sampler fallback, and negotiates a common color/depth sample count; cel-outline depth sampling forces single-sample rendering because the backend has no depth resolve path. A fixed eight-light render-only budget is captured in each sealed frame. Explicitly eligible energy geometry alone enters a separate depth-tested bloom pass; quarter-resolution separable blur is composited before disruption and whiteout, with fog visibility applied to emissive output. Generic particles and trails remain ineligible unless a known energy path opts in. The decoded RGBA/upload boundary accepts bounded high-resolution dimensions, mip generation, anisotropic sampling, and transparency without adding a replacement-file loader or PBR materials. Simulation, networking, authoritative state, and Android EGL/GLES lifecycle ownership remain unchanged. |

Current bounded R12 evidence includes a zero-warning Release solution build,
90/90 focused renderer and visual-enhancement tests, and an optimized Android 36 Release
build with zero errors and one unrelated malformed XML-comment warning in `NetLaunch.cs`.
The Android workload is installed in an isolated .NET SDK for this validation;
the build pins the existing Homebrew JDK 21 and a project-external Android SDK.
Generated-shader freshness and project boundaries both pass. The current Python
tooling run passes 80/80. The current content-backed solution run passes 1230/1230
main tests, 12/12 protocol-generator tests, 18/18 Imaging tests, 192 Backend
tests with four PostgreSQL skips, 53/53 Server.Shared tests, and 146/146 Node
tests. Self-contained publishes for `win-x64`,
`win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64` contain the
RID-appropriate SDL3 native library plus all hash-verified offline shaders;
`check-renderer-package.py` enforces that contract in CI. Bounded Metal room/model
launches report the Metal driver with MSL/METALLIB support without a renderer
exception. A fresh stable-runtime SDL package rendered `MP3 PROVING GROUND` on
its first process as a non-black 640x360 PNG with SHA-256
`5116a9bdca43f1653cbb8a89c0587ebec13b4fc29fefe248929509f2a90f718b`.
These are runtime and packaging smoke checks, not visual-parity screenshots or
performance acceptance. A later local Metal map-audit run exercised Enhanced
filtering, effective 16x anisotropy, effective 4x MSAA, bloom, dynamic visual
lights, and 100% render scale while saving twelve 3840x2160 frames. The first
attempt exposed SDL's requirement that mip-generated sampled textures also have
color-target usage; the corrected allocation then completed without that assertion.
This is a local visual example, not cross-platform parity or performance acceptance.
Android device execution is not claimed by the managed Release build.

Still external or unaccepted: live player HUD/spectator/replay visual comparison
against the historical legacy baseline; saved screenshot, recording, map-audit,
and replay-probe output on representative machines; Windows/Linux GPU execution;
Android device acceptance; resize/minimize/fullscreen/device-loss soak; leak and
frame-time measurements; and full renderer parity/performance acceptance. The user
explicitly authorized R11 and then R12 despite those outstanding gates; this local
source, build, test, and package checkpoint does not convert them into accepted
evidence. Performance testing is still required before changing the Original
default or assigning a lower automatic render scale to the Performance preset.
