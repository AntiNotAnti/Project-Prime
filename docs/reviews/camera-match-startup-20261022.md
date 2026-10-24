# First-frame camera startup crash

The installed ctf1 build loaded Alinos Perch and Fault Line successfully, then
threw `Matrix is singular and cannot be inverted` in `SetMaterialCamera`.
The user's linear-color renderer setting requires the inverse of the camera
view. Normal Shell match loading can render before the first fixed simulation
step, while `CameraInfo.ViewMatrix` was still the default zero matrix.

The earlier CTF bot initialization repair remains necessary and unchanged.
Earlier map diagnostics advanced simulation before drawing, so they did not
cover this first-frame interval.

`CameraInfo.Reset` and the prepared pose in `PlayerEntity.Initialize` now build
the camera basis/view and rebase presentation history immediately. The shared
view-building helper does not consume random numbers or advance camera shake.
The simulation's existing shake/RNG/decay order is preserved.

Validation evidence is under `artifacts/camera-startup-crash-20261022`:

- The native `tools/match-startup-check` harness reproduced the exact old crash
  on Alinos Perch through `MatchStart.Begin` and the production render callback
  at frame zero, before any simulation update or camera inversion in the test.
- The fixed client passed two matches in one persistent window on each map:
  zero simulation steps on each first draw with linear color enabled, followed
  by 120 native simulation steps, balanced teams and seven ready CTF bots.
- Asset-free camera checks cover fresh/reset/reused/prepared poses, inverse-eye
  agreement, draw history, invalid pose handling and seeded shake/RNG behavior.
- The corrected ZIP's actual game and Studio executables passed smoke/window
  checks. Its game passed frame-timing and all 213 CTF checks, plus 600-frame
  live CTF map runs on both reported maps with seven objective-ready bots.

The native tool exercises the production match-start/render boundary directly;
it does not automate menu clicks or qualify Internet multiplayer. Private test
profiles reuse cloned locally owned assets and are excluded from the ZIP.
