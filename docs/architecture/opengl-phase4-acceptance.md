# Phase 4 OpenGL acceptance matrix

## Synthetic automated GL pixels (Phase 4B)

Run `ProjectPrime -glvisualcheck /absolute/output` with a desktop OpenGL
context. This uses a **real GL2.1 FBO** (color + depth24/stencil8), draws four
asset-free scenes, asserts physical GPU-readback pixels, and emits bottom-up
RGBA8 captures with a SHA-256 evidence manifest:

- `opaque-wall`: green pickup fully behind an opaque red wall in center,
  with its unobstructed edge still visible
- `opaque-wall-repeat`: bitwise match after resetting draw state and depth
- `translucent-window`: underlying pickup visibly contributes through
  alpha-blended blue glass
- `stencil-mask`: stencil-covered center is not drawn through; visible
  neighboring area remains drawn

On Linux the GL/Mesa job runs through Xvfb; CI fails on pixel mismatches or GL
errors. `python -m unittest discover -s tools/opengl-visual` tests the
strict manifest/reference comparator. To check a candidate against a captured
same-driver reference, use:

```sh
python tools/opengl-visual/compare.py reference candidate --output gl-report.json
```

Reference/candidate must be same driver, resolution, pixel ordering, and the
exact four named raw frames. No missing capture, mismatched SHA or tolerance
override is silently accepted. The fixtures exercise GL behavior but are
**not a rendered native map or player/Studio acceptance result**.

## Native Ice Hive / Studio mandatory device matrix

With user-provided extracted game assets, make fixed-camera reference
screenshots and motion videos in Ice Hive at 60/120/144/240 Hz. Verify
opaque-wall pickup isolation, partial peeking, ramps, authored glass/windows,
forcefields, shadow persistence/distance, 4K map geometry, hunter/outlines,
weapon beams, HUD, high-refresh interpolation, and map rotation.

Also capture Map Studio, Replay Studio/theatre and Android GLES3 on a device
(after background/resume). Do not treat a missing or skipped GL context as
a pass; preserve skipped records and failure samples. An actual root-cause
claim requires reproduced before/after images on the same device/camera.

For hardware image evidence, the existing
`tools/render-parity/compare.py` supports per-filename limits and heatmaps.
Do not reuse archived Metal/Vulkan golden images for the OpenGL-only stack.
Capture a new reviewed OpenGL reference and establish image-specific tolerances.

Phase 4C performance rollout additionally requires paired hardware p95/p99
CPU world and GPU times, p99 presented frame intervals, stable 60 Hz
simulation, no GPU errors, and zero visual acceptance failures. Rollout flags
stay opt-in until explicitly accepted.
