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
