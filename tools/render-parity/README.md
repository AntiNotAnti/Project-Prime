# Renderer parity evidence

`ProjectPrime -renderparitycheck ROOM -renderer metal -output OUTPUT` requires
local extracted game data and a working display/device. It writes PNG captures
and `evidence.json`. The manifest records the original backend request separately
from the actual created device, platform, adapter/driver, settings, frames, and
successful device reconstructions. Device-loss callback totals are unavailable
and recorded as null, not zero. For OpenGL the driver field is its GL version
string.

These captures include the scene's world, postprocessing and HUD/visor/fade.
They exclude application shell overlays, launcher hunter and Shell.AfterDraw.
The three `pbr-*.png` images are intermediate G-buffer diagnostics. This command
is not evidence of full application composite parity or hardware acceptance.

Compare two capture directories with Python, Pillow and NumPy:

```
python tools/render-parity/compare.py reference candidate --output report.json
```

The report retains mean/structural/changed-pixel gates and adds normalized RGB
RMSE and maximum error. Heatmaps in `report-diffs/` encode each pixel's maximum
channel error as red intensity (0–255); no amplification or resizing is applied.
Missing or extra captures, corrupt images, dimension mismatches, and empty sets
fail. Coarse structural comparison alone downsamples both images to 64×64.

Use `--thresholds limits.json` to override limits for exact capture filenames:

```json
{"world-hud.png": {"mean": 0, "rmse": 0, "maximum": 0}}
```

Available keys are `mean`, `structural`, `fractionOver10Percent`, `rmse`, and
`maximum`. Limits must be finite within [0,1]; unknown metrics or capture names
fail. Existing defaults are preserved. RMSE and maximum default to 1 (reported
but not restrictive); select reviewed per-capture tolerances before using them
as acceptance gates. Avoid relaxing a whole run to accommodate one effect.

Content-free comparator checks:

```
python -m unittest discover -s tools/render-parity -v
```

Cross-platform hardware, long-session lifecycle, full shell composite coverage,
and performance acceptance remain separate gates.

## Application final-composite contract

`FinalCompositeCapture.Read` reads the default backbuffer on the render owner
before presentation, after the caller's last pass. It uses the shared backend
API, returns bottom-up packed RGB, bounds allocation at 64 MiB, and restores the
read framebuffer and pixel alignment. Replay export keeps its explicit export
target; scene thumbnails keep their intentional pre-HUD target.

The existing `ProjectPrime -shellshot ABSOLUTE_OUTPUT` sequence captures the
actual launcher/match/pause/settings/results composite from `Shell.AfterDraw`,
after shell UI and the launcher hunter. Every successful PNG now has a sibling
`.png.evidence.json` identifying scope, requested/actual backend, adapter, driver,
resolution, render scale and available live resources. Failed image readback
fails the scripted run instead of only printing a warning. Use isolated
`PROJECT_PRIME_USER_DATA` when running this UI sequence. Its animated frames are
not frozen cross-backend golden references; it provides lifecycle/scope evidence.

```sh
dotnet run --project tools/final-composite-check -c Release -- opengl
dotnet run --project tools/final-composite-check -c Release -- metal
```

This content-free GPU check draws a synthetic backdrop and final overlay, then
asserts overlay inclusion, orientation, packed RGB, screenshot delegation and
state restoration. It requires a real supported graphics session. The backend
printed in the result is the created device; a missing Metal runtime must not be
counted as successful Metal evidence. This is a readback contract test, not the
full scene/material/hardware matrix.

The parity harness uses the window's actual framebuffer dimensions (including
HiDPI scaling), not its requested logical size. The benchmark resizes the native
window for each requested pixel size, then records both the requested size and
actual framebuffer size; actual adapter/backend metadata accompanies timings.
Do not compare old benchmark results that changed scene dimensions while leaving
the native window at its startup size. Render scale remains a separate setting.

## Separating host submission from presentation pacing

Benchmark evidence now records the requested VSync state and actual modern
present mode, aggregate surface-acquire calls/time, and separate per-frame present
time. VSync is explicitly requested off for OpenGL too. A modern backend may
still select FIFO when Immediate/Mailbox is unavailable. Surface acquisition is
inside the completed-frame and submission measurements, so inspect its time
before attributing a difference to rendering throughput.

Modern samples also report native queue-submission, buffer-write and bind-group
creation counts and elapsed host-call time. These counters run only during an
explicit performance sample; divide their totals by the sample count for a
per-frame value. Driver calls may block, and their timings are not GPU execution
or transfer timestamps. Do not add these overlapping diagnostics to the measured
completed-frame time. The pinned ABI still lacks timestamp-period conversion.

The Vulkan/MoltenVK evidence file records the actual local device, runtime hashes,
scene comparison, native lifetime checks and diagnostic timing runs. Native Vulkan
window tests require the AppKit-enabled pinned wgpu build and packaged loader/ICD;
a missing runtime or a renderer fallback must never count as Vulkan acceptance.
A framework-dependent build may resolve `runtimes/osx-arm64/native` ahead of the
output root, so merely replacing the root library does not prove which native
library the process loaded. Use the packaged application or verify both paths.

## Content-backed scene lifecycle checks

`-shellshot OUTPUT -shelllifecycle REPLAY` appends two match → spectator → rejoin
→ launcher → Forge → replay → paused seek → launcher cycles to the existing
fullscreen/rematch shell sequence. It requires installed game content and a
playable replay. Metadata rejection, replay failure, incomplete seek, missing
Forge workspace, retained scene-resource growth, or failed image capture fails
the run. The Forge library modal is explicitly dismissed before leaving the
workspace, as a real launch must do. Replay readiness, actor/camera state and
warnings are logged; inspect the PNGs as well as the state assertions. The
regression also switches Free → FirstPerson after deliberately clearing the
global camera notification, proving each replica observes its own applied mode.

On a modern backend, the first added match destroys the actual device and
requires reconstruction without fallback, a generation increment, and nonblack
final-composite readback. Add `-renderadvanced` to request Extreme sampling plus
HDR, deferred PBR and TAA; target/history readiness is checked before and after
reconstruction. `-shelllifecycleonly` runs the startup steps and added cycles for
focused reruns; it does **not** cover the omitted fullscreen/rematch sequence.

The repeated boundary assertion covers textures, renderbuffers, geometry,
programs, lists, views, samplers, shader modules, surfaces and no retained bind
groups. Buffer pools and pipelines are logged high-water caches and may grow
when bot draws differ. These counts do not establish bounded native VRAM or
replace the exact-workload 120-cycle native resource check. This check opens
Forge, but does not author a map or enter an editor playtest.

`-respawnrendercheck 'MP3 PROVING GROUND' -cycles 9 -timeout 300` exercises the
existing damage/respawn, overlay, resize and poisoned-render-state controls.
Requested/actual renderer identity and whiteout state now accompany diagnostics
for unexpected black frames. Preserve any failed run even when a rerun passes;
a later pass cannot explain an intermittent failure. The local lifecycle evidence
records synthetic replay provenance separately from rejected historical inputs.
