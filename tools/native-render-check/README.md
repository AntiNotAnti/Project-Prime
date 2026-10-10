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

## Solo Ice Hive flicker and missing world/outline occlusion diagnosis

You do **not** need to remember the last good release. Start with the exact
current binary and compare one idle human against the same human with one AI
opponent. The game's asset key for the Ice Hive screenshot is `UNIT4_RM1`,
not the similarly named `MP9 CRYOCHASM` arena. Use 60 FPS and native shadows
off to remove two confounding variables. Run each experiment in a **fresh
process** against the same installed game data:

```sh
mkdir -p /tmp/prime-solo/one /tmp/prime-solo/two
dotnet build tools/native-render-check -c Release
dotnet tools/native-render-check/bin/Release/net10.0/native-render-check.dll \
  -room UNIT4_RM1 -players 1 -idle -seconds 24 -hz 60 -shadows Off \
  -shots /tmp/prime-solo/one -diagnose-solo \
  -output /tmp/prime-solo/solo.json
dotnet tools/native-render-check/bin/Release/net10.0/native-render-check.dll \
  -room UNIT4_RM1 -players 2 -idle -seconds 24 -hz 60 -shadows Off \
  -shots /tmp/prime-solo/two -diagnose-solo \
  -output /tmp/prime-solo/bot.json
python tools/native-render-check/compare_solo.py \
  /tmp/prime-solo/solo.json /tmp/prime-solo/bot.json \
  --output /tmp/prime-solo/analysis.json
```

When running against the current desktop defaults, independently repeat with
`-gllegacylist -gllegacybindings -gllegacystate -gllegacydepth` to prove or
exclude *additional* optimization contributions. The original solo issue
predates these optimizations; do **not** assume they created it. To narrow
history, use **published** `v0.1.47`, `v0.1.43`, `v0.1.38`, and earlier
releases as candidates, but never label any tag good without native acceptance.
Install historical packages into isolated folders and prevent auto-update from
changing which binary actually ran.

With `-diagnose-solo` each of the four capture records adds `soloDiagnostic`
without changing production rendering: active player/bot counts, phase,
entity and node layers, camera node reference, portal fallback vs visible part
count, room-only opaque packet counts, decals/translucency, lighting vectors,
physical world depth coverage at a bounded 3x3 sample grid, and the final
GL state. All reflection/readback executes only at capture time. The tool
fails if diagnostic depth attachment or pixel samples are invalid.

The comparator **does not require identical images with a second visible bot**.
It first rejects different builds, settings, mode and sample frames. If
camera positions differ by more than 0.1 world unit, it explicitly refuses
depth/room geometry parity claims. A solo scene falling back to drawing
all room parts points toward the camera-node/portal path; stable room
submission but diverging depth coverage points toward depth/stencil
reconstruction; different light inputs point toward initialization.
These are directions for targeted debugging, not verified root causes.

Use `python -m unittest discover -s tools/native-render-check -p 'test_*.py' -v`
for a content-free comparator contract. Native GPU verification requires
the user's installed cartridge assets and a Mac/Windows graphics context.
Do not run capture mode as a steady-state performance benchmark.
