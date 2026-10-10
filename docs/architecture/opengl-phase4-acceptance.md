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

## Phase 4C: same-executable optimization A/B and rollout thresholds

The `-glprofile` data stream now includes machine-readable
`[glframe-json]` and `[glprofile-json]` records for every 240 completed
frames, and `-glgpu` adds `[glgpu-json]` only on supported real GPU timer
drivers. Every record reports explicit sample count and numeric statistics;
the desktop frame record also reports whether VBO/binding optimization is
**actually enabled**, avoiding accidentally mislabeled A/B runs.

Collect at least 4 contiguous profiling windows (960 completed frames) for
each **same build, same map, same camera route, identical cap/quality** mode:

```text
baseline: -glprofile -glgpu
vbo:      -glprofile -glgpu -glvbo
binding:  -glprofile -glgpu -glbindcache
combined: -glprofile -glgpu -glvbo -glbindcache
```

Save console/debug output separately as `baseline.log`, `vbo.log`,
`binding.log`, `combined.log`. Provide an independently reviewed hardware
capture manifest (`source = "native-game-on-device"`, `passed = true`, and
`cases` for Ice Hive walls, Ice Hive shadows, glass/portals, Studio/HUD,
Android GLES). The manifest must cite actual evidence; do not manufacture it
from the synthetic GL fixture.

```sh
python tools/opengl-phase4/analyze.py \
  --baseline baseline.log --vbo vbo.log --binding binding.log \
  --combined combined.log --visual native-acceptance.json \
  --output optimization-decision.json
```

The analyzer rejects missing/invalid samples, simulation drift, stalls,
runtime flags that disagree with the declared arm, and GL errors. A mode
needs at least 3% lower median frame-interval p99, GPU world p99 within 3%,
CPU render p99 and frame p95 within 5%, 1% low within 2%, no dropped GPU
queries, and **independent real-game visual acceptance** to become
*eligible for review*. Default enablement is **not automatic**, and GL2.1
without timer query remains unverified until alternative GPU evidence is
provided. Run the asset-free gate's unit tests in CI.
