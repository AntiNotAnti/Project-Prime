# Native Mac/Windows renderer diagnostic

This tool runs the normal `RenderWindow` loop, including its fixed 60 Hz
simulation, frame cap, render profiling and `SwapBuffers`. It requires the
operator's extracted assets and `paths.txt` in `PROJECT_PRIME_USER_DATA`.
It does not bundle game data or change renderer defaults.

```sh
dotnet build tools/native-render-check -c Release
dotnet tools/native-render-check/bin/Release/net10.0/native-render-check.dll \
  -room UNIT4_RM1 -seconds 24 -hz 60 -glprofile -gllegacylist -gllegacybindings \
  -output /absolute/path/baseline.json
```

Use caps 60/120/144/240 or `-hz -1` for uncapped. Desktop VBOs, binding caching
and opaque batching now default on. For a VBO-only arm use
`PROJECT_PRIME_GL_BATCH=0` with `-gllegacybindings`; for binding-only use
`-gllegacylist`. Set `PROJECT_PRIME_GL_BATCH=0` for combined VBO/binding without
batching, and omit overrides for the full default path. Explicit enable flags
override an environment zero; legacy flags always win. Request `-glgpu` only after
checking driver support. Apple GL2.1 on the tested M4 Pro lacks timer queries.

The workload has eight bots, frozen keyboard/mouse snapshots, fixed RNG
seeds and a 1920x1080 framebuffer by default (`-size 1280x720` changes it).
The first 240 simulation steps are warmup.
JSON statistics describe completed application frames, not panel scanout.
Low-FPS estimates are reciprocals of interval percentiles; the 0.1% estimate
requires at least 1,000 samples. CPU submission and swap distributions are
reported separately by the existing `-glprofile` stream. Exclude profiling
windows that overlap warmup, and never combine different binary MVIDs.

For visual comparison add `-shots /absolute/directory` and optionally
`-shadows Off|Low|High|Ultra`. Capture runs use exactly one simulation step
per picture and include extra readbacks/draws; **do not benchmark them**.
World, world+HUD, unchanged-frame repeat, camera/frame metadata and a float32
shadow-depth capture are emitted. Use the repository's parity comparator
without resizing. Native image differences require inspection and are not
automatically a wall-occlusion or shadow-persistence acceptance result.

The tool deliberately avoids client startup maintenance, which can spawn
thumbnail workers assuming the client's entry point. Its reflection accesses
are confined to test setup/diagnostics; the production renderer is unchanged.
`-p:MphReadProject=/absolute/baseline/MphRead.csproj` can build this same tool
against a baseline implementation into a separate output directory.

For a quiet offline scene use `-players 1 -idle`. Add `-windowcycle` for a
separate lifecycle run: fullscreen, restored window size, hide/show and focus
request. This uses native window APIs and records focus events. Actual focus
loss and recovery require observed callbacks, beyond a successful hide/show. It does not
prove OS keyboard Alt-Tab behavior and must not be included in benchmarks.

For the Ice Hive transform/occlusion regression, use the multiplayer room
`MP9 CRYOCHASM` (not its adventure counterpart `UNIT4_RM1`):

```sh
dotnet tools/native-render-check/bin/Release/net10.0/native-render-check.dll \
  -room 'MP9 CRYOCHASM' -players 2 -hunter Spire -idle -seconds 8 \
  -outlines Red -outlinecheck -size 1280x720 \
  -shots /absolute/path/ice-hive -output /absolute/path/ice-hive.json
```

Repeat with `-players 1` and omit `-outlinecheck` to check solo lighting and
jump-pad placement. `-outlinecheck` uses an actual animated hunter and the
production outline pass: a near depth wall must hide every outline pixel,
and far depth must expose some pixels as a positive control. Both counts
are recorded in the result; a missing body cannot silently pass the test.
`-camera x,y,z -facing x,y,z` sets a fixed free camera for paired captures.
Native mouse movement is ignored so activity outside the diagnostic cannot
change the camera between comparison runs.
