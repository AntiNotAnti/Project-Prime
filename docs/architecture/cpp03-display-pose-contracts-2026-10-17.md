> **Current status — October 19, 2026:** Implementation now includes expanded pose continuity, camera/cannon fences, particle and beam sampling, and specialized Hunter presentation. The latest implementation and evidence record is maintained in [CPP-03/CPP-04 presentation completion](../implementation/cpp03-cpp04-presentation-completion-2026-10-19.md). The final fresh macOS native matrix passes all 28 cells, and loaded headless/rendered replay checks pass; qualification limits are recorded there. The remainder of this document preserves the original narrow historical slice and its bounded checks; its scope and incomplete-acceptance statements describe that earlier slice.

# CPP-03 display-time pose continuity — October 17, 2026

## Scope and source lock

This change closes a narrow continuity gap in the existing C# entity draw history. It does not establish full CPP-03 or CPP-04 visual, performance, device, network impairment, or authoritative simulation acceptance.

Project baseline: `dc7f097067d755ec76d145fc7cc333068566863d`, with the parent task's existing uncommitted RmlUi continuation preserved. The implementation uses Project Prime's existing capture and interpolation path rather than introducing a second pose controller.

The donor comparison was rechecked against Fruity master `62b128af60e9bb69faf868a00e0fac2017636109`, not the older audit revision. The exact primary source is [ScenePresentation.cpp](https://github.com/liveteklol/Fruity-Prime/blob/62b128af60e9bb69faf868a00e0fac2017636109/src/MphRead.Native/Mods/Render/ScenePresentation.cpp), raw file SHA-256 `1a62a6615ed044bb2b33a3e427dd76ea24e851a56590fe935f0e0bb579416b5d`. The useful behaviors are consecutive-step validity, rejecting a canonical transform edited since capture, and keeping disappeared entities out of the render owner. The C# matrix interpolation remains the existing Project implementation. Fruity's global pointer-keyed pose map, motion prediction, and temporary particle mutation were not imported.

## Existing coverage audited

| Owner | Existing behavior retained |
| --- | --- |
| `EntityBase.ModCaptureDrawState` | One canonical transform capture after completed simulation/reconciliation; a greater-than-four-unit jump rebases history. |
| `EntityBase.SimulationDrawTransform` | Draw-only scale/rotation/translation sampling. The local first-person and live network-player policy remains in `PlayerEntity.InterpolateDrawTransform`. |
| `PlayerEntity.Draw` | Its body/alternate-form/shadow position now uses the existing pose owner for ordinary offline non-main actors, through the small `ModGetDrawActorPose` selector. Live network playout and recorded-pose overrides retain their existing branches. |
| `FrameTiming` | Fixed 60 Hz simulation, independent display clock, current-state alpha at 60 Hz, bounded catch-up. |
| `AnimationSamples` / `ModelInstance.PresentationSamples` | Read-only frame prediction with animation group/index/flags/cadence witnesses; node/material/UV sampling stays with existing callers. |
| `NetSmoothing` | Remote network playout, received-frame sampling, current lifecycle validation, form/gap/teleport holding, and matching input acknowledgements. |
| `ReplayPoseStream` / `ReplaySceneServices` | Recorded pose timeline, viewer-specific sampling, recorded lives, and existing replay presentation reset hooks. |
| Camera / first-person helpers | Existing camera, FOV, aim, and cannon histories; their broader coherence remains CPP-04 work. |

No renderer, network, replay-capture, collision, combat, shader, GL-owner, resource-cache, or product UI file is changed by this item.

## Missing contract implemented

`DisplayPoseHistory` replaces the render use of the private previous/current transform pair inside `EntityBase`; it is a value field on the existing entity. Only `ModResetDrawState` and the existing `ModCaptureDrawState` hook write it. It holds no entity registry and cannot schedule an entity for drawing.

The three preexisting `_drawPrevious`, `_drawCurrent`, and `_drawStateValid` fields are frozen slots in the historical replay-world schema and generated accessors. An integrated architecture check caught that removing those fields would break class initialization. They therefore retain their exact original names, types, and canonical reset/capture values solely for archival compatibility. Rendering never reads these compatibility slots, and the new `_drawPose` state, scene/room references, and continuity witnesses are absent from checkpoint traversal. The schema and generated accessor contract remain `A4512223EC0CC49516DB00DD3733168DA6969DEA059E9DDE84B31062ADEA675A`.

The carry witness is the current scene's `LifetimeGeneration`, the exact room object, current authority epoch/match, current slot generation/life, actual form flags and Hunter, replica/live mode, and presentation timeline generation. Replica identity comes from that scene's `ReplaySceneServices.State`, with `Transport.SeekGeneration`; it never falls through to live session state. Live identity uses the existing session/lifecycle owners. Player health and active/spawned flags only determine carry eligibility; they do not change whether the renderer draws the canonical entity.

Capture remains at the existing point before `Scene.FrameCount` increments. A sample is usable only when:

- Both poses were captured on consecutive simulation ticks with the same eligible witness.
- The capture belongs to the immediately completed scene tick. Duplicate, backward, skipped, and overflowed ticks rebase history.
- The live canonical transform still exactly equals the current captured value.
- The supplied fraction is finite. Finite out-of-range fractions clamp to the known interval; there is no extrapolation.

Form/life/scene/room/match/seek changes fence sampling immediately, even before the next capture. Repeated captures on a paused scene tick collapse carry. A missing capture expires the previous sample. Teleports and nonfinite captures reset it. Each failure returns the current canonical transform; it never draws a retained prior-life pose as a fallback.

`FrameTiming.PresentationGeneration` is a read-only presentation fence incremented when `Reset` discards accumulated time, a stall recovers, or excess catch-up time is dropped. Scheduling, timestep arithmetic, frame counts, and simulation inputs are unchanged. Replica carry uses its seek generation instead of the host display clock.

`SimulationDrawPosition` uses the same validity guard and timestamp as the full transform, while preserving its existing translation-only role for camera/radar consumers. Local first-person draw bypass and the network/replay owners' later overrides remain intact.

The consumer audit found that ordinary offline actors previously bypassed `ReplayDrawTransform` inside `PlayerEntity.Draw`, despite being eligible in `InterpolateDrawTransform`. `Draw` now obtains its base actor position/facing from `ModGetDrawActorPose`, which consults that exact existing eligibility policy and samples once. The shared `drawPosition` feeds the biped, alternate form and shadow. The biped basis also receives the sampled finite facing. Kanden applies only the same base translation to local render-node copies; its simulation-owned segment matrices/positions and chain orientation are unchanged. No new alternate-form joint/effect controller is introduced; broader CPP-04 animation coherence remains open.

The newly enabled ordinary offline path restores canonical biped/alternate-form nodes in `finally`. A factorized body-matrix helper preserves the exact former canonical arithmetic; canonical `_modelTransform`, muzzle/charge anchors, and frozen-ice matrices/archive remain on that completed pose. The displayed biped nodes are restored after temporary canonical muzzle-anchor calculation so later ice/armor submissions can still use the selected picture. Kanden segment nodes and Spire attack nodes return to canonical values after submission. These scopes avoid introducing sampled actor state into simulation or archival owners; the preexisting live-network/replay/spectator branches keep their existing policy.

## Verification

Run from the repository root with the pinned .NET SDK:

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/display-pose-check/display-pose-check.csproj -c Release
```

The fixture links the production `DisplayPoseHistory.cs` and `FrameTiming.cs`; only logging is stubbed. It passed **77,268 checks** on .NET SDK **10.0.401**. The captured console output is [source-fixture.txt](../evidence/cpp03-display-pose-2026-10-17/source-fixture.txt). A warmed loop of 20,000 production transform/position reads allocated **0 managed bytes**; this is a core allocation observation, not a CPU/frame-time or whole-renderer performance result.

| Display schedule | Draw calls | Canonical ticks | Fractional poses |
| --- | ---: | ---: | ---: |
| 60 Hz | 600 | 600 | 0 |
| 120 Hz | 1,200 | 600 | 4,611 |
| 144 Hz | 1,441 | 600 | 10,159 |
| 240 Hz | 2,400 | 600 | 13,833 |

All four schedules produce identical canonical matrix-stream SHA-256:

```text
afbd59895f88a7328f960198b41b6752a5f501556396e3703d8e5441881c1071
```

The synthetic eight-entity simulation includes direction reversal, form changes, new lives, timeline resets, and entity removal. It verifies real fixed-step fractional timestamps, current-state sampling at 60 Hz, read-only canonical/history values across repeated draws, immediate identity fencing, new-lifetime recovery, paused/same-tick draws, capture gaps/rewinds, teleports, nonfinite input, shortest-arc orientation, and removal/pool reset behavior.

The additional content-free actual-scene fixture, `tools/nettest --display-pose-integration`, passed **69 checks** against the parent's coordinated Release application build; the Release nettest build passed with zero warnings/errors. It exercises the real `EntityBase` capture/draw hookup, actual scene removal and scene-generation transfer, actual `PlayerEntity` first-person bypass, the selector used by `Draw`, exact original biped-transform arithmetic, and morph/death/despawn boundaries. It uses test-only reflection to set the existing scene tick and form flags without loading a map. It also checks the frozen world fingerprint, exclusion of the new pose types from traversal, production generated/reflection player-block byte parity, and an exact production-codec player-block restore/recapture roundtrip. The independently frozen historical canonical snapshot stream SHA-256 is `64a16aac610f36c686ae6055fdfde3d2c94819fd1e1cddd7457cabf9138ae4f1`. Captured output: [integration-fixture.txt](../evidence/cpp03-display-pose-2026-10-17/integration-fixture.txt). These selector/field-block checks do not execute full `Draw` or certify an asset-loaded playback seek.

The existing replay [architecture gate passed](../evidence/cpp03-display-pose-2026-10-17/replay-architecture.txt), and the existing pickup/replay suite passed **83 checks**, including historical world-capsule envelope and byte-preservation checks: [replay-compatibility.txt](../evidence/cpp03-display-pose-2026-10-17/replay-compatibility.txt). An independent source review covered canonical cleanup, ice/effect ownership, Kanden segment isolation, Spire node convention, Weavel's separate turret graph, and copied render palettes. These are source/CPU contract checks, not a seven-Hunter rendered acceptance tour.

This hash is a source-only fixture's canonical matrix stream, **not** a full game simulation, replay file, or authoritative combat hash. The fixture's removed-entity check demonstrates the contract's lack of a secondary render registry; actual scene draw enumeration still needs the parent's integrated checks. No 3D screenshot, physical-device behavior, actual frame capture, relative Fruity performance, CPU/GC budget, seven-Hunter visual tour, impaired-network run, or replay parity is claimed here. The parent task coordinates full application/server/GL builds and broader acceptance.
