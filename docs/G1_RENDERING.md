# G1 rendering and frame-timing boundary

Status: **CURRENT** implementation summary, last reviewed 2026-09-12. The
pre-SDL/OpenTK call paths in `G1_BASELINE.md` and
`RENDERER_MODERNIZATION_BASELINE.md` remain historical evidence. They are not
the current host design. No source or synthetic result in this document is a
physical-device, visual-parity, or mouse-to-photon claim.

## CURRENT: frame paths

Desktop uses `src/Client/Rendering/Platform/SdlGameHost.cs` and the shared
`src/Client.Presentation/Rendering/Platform/GameWindowFrameLoop.cs`:

```text
SDL event poll and translation
  -> FrameTiming.Advance / ManualStep
  -> zero to five fixed simulation steps
  -> ScenePresentation.OnDrawFrame
  -> acquire and encode SDL GPU frame
  -> submit
  -> OnFramePresented only after successful submission
  -> AfterRenderFrame only after successful submission
```

Minimized, occluded, failed-acquire, and failed-submit frames do not advance the
presentation acknowledgement boundary. Auxiliary UI/event pumping remains
responsive on both success and failure paths. `SdlGameHost` owns the SDL window,
input hubs, GPU backend, and native lifetime; Client.Presentation owns the
portable loop and scene-facing frame contract.

Android retains its Android-owned loop in `src/Android/GameView.cs`. It advances
the same fixed clock and shared scene presentation, renders through the guarded
GLES path, and calls `OnFramePresented` only after `EglSwapBuffers` succeeds.
Its surface/lifecycle behavior is not inferred from the desktop host.

`src/Renderer/FrameTiming.cs` is the clock shared by these paths. Simulation is
fixed at 60 Hz, catch-up is capped at five steps, invalid or greater-than-250-ms
wall intervals are discontinuities, and excess debt is dropped. Fractional debt
is retained as `RenderAlpha`; runtime phase telemetry uses bounded samplers and
does not change scheduling.

## CURRENT: interpolation boundary

`src/Client.Presentation/Rendering/SimulationPoseHistory.cs` and
`RenderInterpolation.cs` retain completed-tick presentation samples per scene.
History is keyed by stable entity/player identity and reset across scene,
identity, life, room, teleport, form, timing, and hard-correction barriers.
Matrix interpolation uses translation/scale lerp and quaternion slerp with
finite and singular-value guards.

The central invariant is that rendering never writes an interpolated pose back
to Game. Interpolation affects copied draw submissions, copied matrix stacks,
or frame-local presentation values. It does not modify authoritative entity
position, player aim, collision volume, node identity, muzzle/spread origin,
protocol state, or persistent model/effect caches.

| Path | Current behavior |
|---|---|
| Local biped camera | Completed-step translation/FOV interpolation; current simulation orientation plus eligible pending desktop look |
| First-person viewmodel | Authored animation/bob interpolated in camera-local space, then attached once to the resolved render camera |
| Local/offline bodies | Root transform interpolation; remote network slots are excluded independently of main-player selection |
| Platforms and doors | Rigid roots plus copied authored node poses; normal draw/collision caches and `WasDrawn` behavior remain untouched |
| Selected projectiles/bombs/items | Copied roots, trails, and frame-local particles interpolate behind identity/lifetime barriers |
| Independent effect sprites | World-space positions sampled after existing effect ticks; no render-frequency effect advancement |
| Remote/replay/spectator | Existing snapshot/replay presentation remains the only pose owner; local look prediction is disabled |
| Android | Shares pose interpolation; desktop SDL look production is not enabled there |

Owner-relative effects, mesh effects, player skeletal animation, complex
attachments, and other paths without a proven copied-presentation contract
remain outside the interpolation allowlist.

## CURRENT: look ownership

Desktop SDL relative-motion events are translated once at the host boundary.
The local `RenderLookAccumulator` allows a non-consuming presentation read and
one fixed-step consume; simulation does not also subtract an absolute mouse
snapshot. Absolute position remains available for pointer UI, buttons, and
wheel input. Pause, chat, cursor-ownership changes, focus changes, scene
handoff, and frame advance discard pending relative look and rebaseline the
compatibility snapshot.

Pending movement uses the current simulation orientation. Rendering never
invokes simulation aim methods. Existing sensitivity, inversion, zoom scaling,
pitch limits, block-input rules, and command generation remain owned by the
fixed-step input path. Android touch retains its destructive consume exactly
once in `ApplyInput`; it does not borrow the desktop accumulator.

## TARGET: invariants

- Fixed simulation results, commands, clocks, collision, and protocol output
  must be identical regardless of draw cadence.
- A copied render submission may outlive source scratch state; Game-owned
  mutable arrays or model nodes may not escape into it.
- A successful native submit/swap is the only presentation acknowledgement.
- Remote snapshot interpolation and local completed-tick interpolation must
  never both own the same player pose.
- Timing telemetry is bounded, allocation-free after warm-up, and observational.

## TEMPORARY EXCEPTIONS

Android still uses the explicit Client.Presentation Android target and two
guarded GLES implementation source links described in `PROJECT_LAYOUT.md`.
The GLES and SDL backends are intentionally different native implementations
behind shared frame/render contracts. This is not a claim of device parity.

OpenTK-compatible key/mouse value types remain at a few presentation seams
after platform translation. OpenTK desktop windowing and graphics-context
ownership are retired; SDL is the sole desktop native host/backend.

## Evidence and remaining acceptance

Pure and focused tests cover fixed-clock arithmetic, discontinuities, bounded
phase telemetry, frame ordering, failed-submit acknowledgement, interpolation
math, copied-source independence, reset barriers, and look conservation at
multiple render schedules. See `docs/testing/PERFORMANCE_BASELINES.md` for the
repeatable synthetic timing protocol. Those tests do not establish rendered
visual quality or input latency.

On 2026-09-12, the content-free runtime smoke locally created the real SDL
GPU/Metal path on Apple Silicon macOS, submitted one hidden frame, and exited
successfully (`driver=metal`, `surface=32x32`). Windows and Linux native smoke
jobs are scheduled but remain unclaimed until their results are recorded.

Still requiring device or rendered validation: high-refresh camera/viewmodel
appearance, native mouse-to-photon behavior, Windows/Linux input semantics,
physical Android lifecycle/rendering/input, player skeletal animation, complex
attached effects, and live projectile/cross-portal visual alignment. These are
tracked in `.claude/KNOWN-GAPS.md` and `CURRENT_RELEASE_GATES.md`.
