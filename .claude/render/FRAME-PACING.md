# Frame pacing: fixed simulation, display-paced presentation

Status: current implementation summary, last reviewed 2026-09-12. The old
OpenTK `RenderWindow` measurements are historical and are retained as such in
`testing/TEST-METRICS.md`; SDL is the current desktop host.

## Why simulation stays at 60 Hz

Gameplay timers, commands, replay frames, and protocol cadence use simulation
ticks. Changing the simulation rate would change gameplay and network behavior,
not just presentation. Project Prime therefore keeps one fixed 60 Hz simulation
and permits presentation to run at the display or configured frame cap.

## Current desktop split

`src/Client/Rendering/Platform/SdlGameHost.cs` owns the SDL window, native
events, GPU surface, and host lifetime. It drives
`src/Client.Presentation/Rendering/Platform/GameWindowFrameLoop.cs`:

```text
translate input
  -> FrameTiming.Advance
  -> N ScenePresentation.OnSimulationFrame calls
  -> one OnDrawFrame
  -> acquire/encode/submit
  -> OnFramePresented and AfterRenderFrame after successful submission
```

`src/Renderer/FrameTiming.cs` retains fractional debt, caps catch-up at five
steps, and treats invalid or greater-than-250-ms intervals as discontinuities.
A machine below 60 drawn frames per second can perform multiple simulation
steps per picture; a high-refresh display can draw pictures with no new
simulation step. Neither changes the fixed tick count.

Frame advance resets wall-clock debt and requests exactly one logical step.
Minimized, occluded, failed-acquire, and failed-submit frames keep pumping
events/UI but do not advance presentation acknowledgement.

## Android

`src/Android/GameView.cs` owns its EGL surface and render thread. It uses the
same fixed clock and shared Client.Presentation scene code, but keeps Android
input inside the fixed-step loop so a touch edge is consumed exactly once.
`eglSwapBuffers` provides display pacing in display mode; API 30+
`Surface.SetFrameRate` is best-effort. `OnFramePresented` runs only after a
successful swap.

The API 30 x86_64/SwiftShader emulator has loaded an offline match. That is
lifecycle evidence, not physical-device frame pacing or visual evidence.

## Presentation interpolation

Interpolation is currently enabled for an explicit presentation-only allowlist.
`FrameTiming.RenderAlpha` resolves between completed fixed-step samples without
writing interpolated state into Game. Local camera translation/FOV, viewmodel
submission, local/offline roots, copied platform/door node poses, selected
projectile/bomb/item submissions, and independent frame-local effects are
covered. Remote players remain owned by snapshot interpolation.

History resets across timing discontinuities, room/scene changes, pooled
identity changes, spawn/life changes, teleports, form changes, spectator modes,
and hard prediction correction. Player skeletal animation, owner-relative or
mesh effects, and complex attachments remain outside the allowlist.

Desktop relative mouse motion has one producer and one fixed-step consume.
Presentation may peek without consuming; simulation uses the same pending batch
and does not also subtract an absolute snapshot. Android touch keeps its own
destructive consume in `ApplyInput` and does not use the desktop accumulator.

## Draw-owned clocks

Fade, effects, and pause-map animation retain their established ordering but
consume the number of simulation steps owed. Drawing more often must not advance
gameplay clocks. Effect parity uses its own clock so splitting simulation from
presentation does not remove first-frame bursts.

## Settings

Launcher **FPS limit** selects Display (VSync), a numeric cap, or Unlimited and
saves `FrameRateCap`. Display is the default. Numeric caps disable VSync and use
the SDL frame pacer; this avoids double pacing. The on-screen FPS counter is enabled
by default and reports successful presentations over a rolling wall-clock window;
the settings toggle can still disable it. Long idle gaps start a fresh sample so a
minimize/resume interval is not reported as active rendering. Simulation and phase
telemetry are written to diagnostics.

## Evidence boundary

Synthetic schedules cover 40/60/120/144/165/240 Hz, stalls, catch-up, alpha,
and draw-without-simulation behavior. Focused tests cover submission ordering,
failed presents, telemetry bounds, interpolation barriers, and input
conservation. `docs/testing/PERFORMANCE_BASELINES.md` defines the repeatable
protocol.

On 2026-09-12, a content-free SDL GPU/Metal smoke submitted one hidden frame on
local Apple Silicon macOS. Windows and Linux runtime-smoke jobs are scheduled;
their results are not claimed here. Real high-refresh appearance,
mouse-to-photon latency, and physical Android behavior still need device
validation. See `docs/G1_RENDERING.md` and `.claude/KNOWN-GAPS.md`.
