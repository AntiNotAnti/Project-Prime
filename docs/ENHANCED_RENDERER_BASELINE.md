# Project Prime Enhanced Renderer VE0 Baseline

## Status and evidence boundary

VE0 now has a backend-neutral, bounded metadata format in
`RenderBaselineMeasurement`. It records the quality state and geometry visible
in a sealed `RenderFrame`, plus CPU frame-time samples collected around a render
tool frame. The output is deterministic, machine-readable JSON suitable for a
sidecar next to each screenshot.

No representative scene captures or performance results are recorded here.
This checkout does not expose backend GPU timer queries, GPU memory budgeting,
or a complete backend draw counter. Those values remain explicit `null` values
with an unavailable reason; planned frontend stream draws and triangles are
reported separately and must not be presented as GPU totals.

## Canonical capture matrix

Use a fixed camera and unchanged content for every row. Assign each camera a
stable identifier and retain that identifier in the JSON `scenario` field.

| Scenario family | Required visible evidence |
| --- | --- |
| `indoor-geometry` | large nearby floor and wall triangles, hard corners |
| `outdoor-room` | longest practical view distance and visible background |
| `fog-heavy-room` | geometry both before and within the room fog range |
| `hunter-closeup` | armor, visor, and weapon at close range |
| `weapon-fire` | muzzle, projectile or beam, and a nearby lit surface |
| `bomb-explosion` | bomb core, explosion, and surrounding room geometry |
| `transparent-geometry` | overlapping transparent and opaque geometry |
| `force-field` | force field with readable geometry behind it |
| `teleporter` | active teleporter and nearby opaque surfaces |
| `particles` | representative particle-heavy combat frame |
| `custom-map` | one shipped custom map using its normal asset pipeline |

Capture each scenario with this configuration matrix:

| Output | Preset | Cel |
| --- | --- | --- |
| 1920x1080 | Original | off, on |
| 1920x1080 | Enhanced | off, on |
| 2560x1440 | Original | off, on |
| 2560x1440 | Enhanced | off, on |
| 3840x2160 where practical | Original | off, on |
| 3840x2160 where practical | Enhanced | off, on |

Do not substitute the Performance preset for an Original or Enhanced baseline.
Performance can be captured as an additional comparison after the required
matrix is complete.

## Reproducible tool workflow

1. Start from the same build and extracted content set. Record their hashes in
   the enclosing run manifest; do not put machine-local absolute content paths
   in the per-frame JSON.
2. Load the named room, select the fixed camera, configure output resolution,
   render scale, preset, MSAA, anisotropy, bloom, dynamic lights, and cel state.
3. Warm all scene resources before measurement. A baseline run uses 120
   unrecorded submitted frames followed by exactly 600 measured submitted
   frames. Restart the sample window after a resize or device event.
4. Wrap each measured tool render call with
   `cpuWindow.Measure(() => host.Render(...), result => result.Submitted)`.
   Skipped/acquire-failed frames do not belong in the 600 submitted-frame set.
5. Capture the final presented frame through the existing neutral capture path.
   Use a stable pair such as
   `hunter-closeup-sanctorus-1080p-enhanced-cel.png` and the same basename with
   `.json`.
6. Seal the corresponding `RenderFrame`, then call
   `RenderBaselineMeasurement.Capture`. Pass the configured render-scale value
   and the backend's `RenderBackendInfo`; serialize with `ToJson()`.
7. Validate that the JSON contains 600 CPU samples, the expected drawable and
   scene sizes, the selected quality state, and a screenshot path. Preserve
   unavailable device metrics as unavailable rather than converting them to
   zero.

The CPU statistic is the elapsed host-side presentation/render/submit duration
seen by the tool. The 99th percentile uses nearest-rank selection. The sample
window has a fixed capacity and evicts oldest samples if a caller exceeds it;
the sidecar reports the eviction count.

## Metric definitions

| JSON field | Meaning |
| --- | --- |
| `output.drawableWidth/Height` | requested final drawable pixels captured in the sealed frame |
| `output.sceneWidth/Height` | internal scene target pixels captured in the sealed frame |
| `output.configuredRenderScalePercent` | configured render scale, kept separately from rounded target dimensions |
| `quality.requested*` | quality values requested by the frontend frame |
| `quality.effective*` | backend-resolved values when exposed; otherwise `null` |
| `quality.visualLightCount` | bounded render-only light count in the sealed frame |
| `cpuFrameTime` | bounded host-side sample count, average, nearest-rank p99, and eviction count |
| `geometry.logicalTriangleCount` | one copy of each resolvable world/HUD scene submission's indexed triangles |
| `geometry.plannedWorld*` | visits through the explicit six world passes; excludes HUD, bloom, overlays, and post processing |
| `geometry.gpuDrawCallCount` | actual complete backend draw count; `null` until instrumented |
| `device.gpuFrameMilliseconds` | GPU timer-query result; `null` until instrumented |
| `device.gpuMemoryBytes` | renderer GPU memory/budget result; `null` until instrumented |

## VE0 completion gate

VE0 evidence is complete only after every required matrix row has a non-black
PNG and validated JSON sidecar on the target platform. A source/test pass for
the measurement code does not satisfy the live capture or performance gate.
