# Rendering draw ownership and CPU wall attribution

Implementation baseline is local lifecycle validation bundle
`749aa12dc89d41dc83823eafe0929c13b1ebfb99`, incorporating the proven resource
ownership changes in #492/#494/#495. Current upstream `main` is
`91b1c0c6c44e8c90effd250729709f791a84765e`.

This slice adds opt-in evidence. It makes no rendering optimization, FPS,
GPU-bottleneck, or native-memory boundedness claim. Historical roughly 450
immediate draws per frame do not identify the current bottleneck.

## Existing work preserved

`OpenGlCapabilities.cs` enables 25 validated policies: VAO, DYNAMIC_STREAM,
UNIFORM_REVISIONS, STATIC_CACHE, SHADOW_FAST, HUD_SAMPLERS, DEPTH_BATCH,
ATLAS_EVICTION, HUD_DISCARD, DEPTH_SHADER, GLES_UNIFORMS, HUD_GLYPHS,
HUD_SPRITES, DEPTH_PREP, PROCEDURAL_BATCH, ROOM_CACHE, DEPTH_KEEP,
SHADER_VARIANTS, ROOM_GROUPS, PLAIN_SHADER, SHADOW_PREP, ROOM_IDENTITY,
MESH_OBJECT, UNLIT_SHADER, and SHADOW_GROUPS. DEPTH_VERTEX and
PROCEDURAL_STREAM stay opt-in. Legacy VBO, binding cache, and batching defaults
also remain unchanged. No completed modernization work is repeated here.

Existing profilers separate inclusive CPU scopes, render wall, present wall,
completed-frame intervals, and asynchronous GPU query capability/status.
`presentWall` is not GPU execution time. Unsupported or absent GPU timers remain
UNVERIFIED.

## Source-backed diagnostic seam

- `GraphicsApi.End` counts executed facade Begin/End submissions, excluding
  commands stored in a compiled display list. Its fast procedural path counts
  its own executed Begin/End. The new bins partition this existing
  `ImmediateDraws` counter rather than guessing native driver draw counts.
- `RenderItem` separates setup/material/uniform work from geometry submission.
  Both are CPU wall scopes and can include synchronous driver/backpressure work.
- Explicit room ownership, palette presence, first-person flag, and primitive
  type classify RoomMesh, PaletteMesh, SingleTransformMesh, FirstPerson,
  Particle, TrailSingle/Multi/Stack, and ProceduralShape. These labels do not
  prove immutable geometry or animation. HUD and unclassified sources remain
  visible; missing ownership is never silently dropped.
- Current inclusive CPU pass plus owner produces a disjoint immediate-source
  matrix. Nested owner scopes restore the outer owner. Cancelled frames/context
  resets invalidate stale scopes and clear partial windows.
- Fixed storage holds 240-frame per-owner setup/geometry distributions. There
  are no retained Scene/RenderItem references. Absent owners have zero samples
  and null times. CPU samples include frames containing that owner only;
  inclusive scopes are not additive.
- The normal disabled path performs no new allocation, Stopwatch sampling,
  counters, GL queries, or synchronization. Optional summaries allocate outside
  timed scopes alongside the existing profiler summaries.

`[glpasses-json]` adds `drawAttribution` without changing existing schema/fields.
The analyzer preserves compatibility while the new review helper requires full
window coverage and conservation against the original executed counter.

## Reproduction

After the native slot is reserved, use the operator's prepared assets/user-data
and the same fixed flags, map, framebuffer, cap and fixture in every arm:

```bash
dotnet build tools/native-render-check -c Release
dotnet tools/native-render-check/bin/Release/net10.0/native-render-check.dll \
  -room 'MP3 PROVING GROUND' -players 8 -seconds 25 -hz -1 \
  -size 1920x1080 -shadows Off -glprofile -glgpu -output /absolute/run.json \
  > /absolute/run.log
python3 tools/opengl-modernization/draw_attribution.py /absolute/run.log \
  --warmup-windows 2 --output /absolute/run-attribution.json
```

The helper excludes all windows before the native harness's 240-simulation-step
measurement marker, preserves early GPU failure/capability status, and discards
two additional completed windows. At least four complete matching windows must
remain. It refuses overwrite, changing fixture contexts, unknown owners,
nonfinite/invalid statistics, partial windows, and lost/double-counted draws.
Medians of window percentiles are labeled as such, never pooled percentiles.

Separate solo, one-bot, and eight-player runs plus High/Ultra shadow attribution
are planned. Exact world/HUD/depth, shadow and occlusion captures are separate
from FPS runs. At least five matched runs per arm on a quiet host are required
before a major performance claim. Profiled throughput includes instrumentation
and cannot substitute for unprofiled measurements.

## Validation and open gates

| Check | Status | Evidence |
| --- | --- | --- |
| Desktop Release build | PASS | SDK10.0.401; 30 existing warnings, zero errors |
| Asset-free render graph characterization | PASS | Ownership precedence, nested restoration, count conservation, fixed bounds, cancellation |
| Python modernization analyzer suite | PASS | 19 tests, including malformed source partitions, missing timing/partial windows and simulation warmup exclusion |
| Dedicated server Release build | PASS | 22 existing warnings, zero errors |
| OpenGL-only guard / whitespace | PASS | Existing guard and `git diff --check` |
| Native profiler attribution | NOT_RUN | Waiting for context-control stress to release the native slot |
| Real color/depth/shadow/occlusion parity matrix | NOT_RUN | Native slot not yet available |
| Quiet-host unprofiled A/B performance | NOT_RUN | No optimization hypothesis supported yet |
| Physical Android and other GPU attribution | NOT_RUN | Desktop local work only so far |

No GC policy changes are included. Transition collection pause/residency remains
an independent matched-control investigation. Driver allocation growth remains
open; a quiet RSS plateau does not establish a bounded driver cache.
