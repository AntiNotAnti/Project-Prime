> **Current status — October 19, 2026:** Implementation now includes expanded pose continuity, camera/cannon fences, particle and beam sampling, and specialized Hunter presentation. The latest implementation and evidence record is maintained in [CPP-03/CPP-04 presentation completion](../implementation/cpp03-cpp04-presentation-completion-2026-10-19.md). The final fresh macOS native matrix passes all 28 cells, and loaded headless/rendered replay checks pass; qualification limits are recorded there. The remainder of this document preserves the original narrow scope-FOV slice and its bounded checks; its scope and incomplete-acceptance statements describe that earlier slice.

# CPP-04 responsive first-person scope FOV

This slice fixes one source-evidenced coherence gap. It does not complete the
seven-hunter, effects, replay, platform or physical-device CPP-04 acceptance.

`PlayerEntity.ModPrepareFirstPersonRenderPose` already samples camera translation
and camera-local cannon offsets at the fractional draw time. Its responsive
camera branch previously returned canonical `CameraInfo.Fov`, so scope
magnification still changed only at 60 Hz. The legacy first-person path already
received sampled FOV. `Scene.TransformCamera` consumes the returned render FOV
for world and viewmodel projection, making this the smallest shared correction.

The responsive high-refresh branch now samples the existing camera FOV history
with the same supplied presentation fraction. Current/late-latched orientation
is unchanged. At a 60 Hz presentation rate it still returns current FOV. Scope
entry and reversal interpolate only between completed FOV endpoints.

The existing `CameraInfo` owner now stamps each FOV endpoint with
`FrameTiming.PresentationGeneration`. Only this new live responsive caller opts
into the generation fence. A focus/reset/discarded clock interval immediately
uses canonical FOV, and interpolation resumes only when both endpoints belong to
the new generation. This adds no second FOV history. Existing explicit
spawn/rejoin, teleport, and replay life/seek reset owners remain responsible for
their current boundaries. Replay callers omit the optional live fence and retain
their current sampling semantics.

The actual first-person fixture in `HitReactionPresentationCheck` exercises the
returned render FOV at 60/120/144/240 Hz for fixed-crosshair and Pro HUD Dynamic
camera policies. It checks scope entry/reversal, pending local pointer input,
explicit lifetime reset, teleport, focus recovery before/after capture, discarded
clock intervals, and unchanged default replay sampling. Repeated draws also
check canonical FOV, player/camera position, aim, and both RNG streams. Existing
hit-shake/cannon/camera assertions remain in the same fixture.

Validation command after a root-owned app build:

```sh
dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -frametimingcheck
```

The fixture is content-free and creates no GL window. Its canonical state
assertions are bounded draw-invariance evidence; they are not an actual gameplay
simulation/replay hash campaign or physical display validation. The root-owned
full build and deterministic fidelity regression checks must be recorded
separately. No simulation step, input stream, collision, weapon rule, network
protocol, replay serialization or saved-game format is changed by this slice.
