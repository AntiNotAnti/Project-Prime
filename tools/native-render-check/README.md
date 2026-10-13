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

For combat HUD parity, add `-stablehud` to both capture arms. The fixture runs
simulation faster than wall time, while the kill feed normally expires/fades
using wall time. This explicit capture-only option keeps existing feed entries
in their opaque lifetime, preserving their contents, order and capacity. It
requires `-shots`, records `captureKillFeedLifetimeStabilized` in the result,
and does not change the production clock or benchmark behavior. It cannot test
kill-feed lifetime/fading. Captures occur at simulation frames 240, 540, 840
and 1140; `-seconds 20` captures all four, while `-seconds 8` captures only one.

Results include observed window/focus/visibility transitions and actual render
and framebuffer dimensions. Scene-unload checks report retained geometry and
both immutable HUD image cache counts/format bytes before and after cleanup.

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


Short modernization comparisons are available through `tools/opengl-modernization/short_checks.py` (`same`, `profile`, `visual`, `cross`). The runner preserves binary hashes/MVIDs and separates readback from timing. See that directory’s README for paths and resume options. Cleanup now also requires the scene-owned depth snapshot’s recorded resident bytes to return to zero.

## Same-context resource stress

```sh
dotnet tools/native-render-check/bin/Release/net10.0/native-render-check.dll \
  -room 'MP3 PROVING GROUND' -players 8 -size 1920x1080 -hz -1 -seconds 8 \
  -shadows High -resourcestress -resourcecycles 30 -resourceframes 120 \
  -output /absolute/path/resource-stress.json
```

This separate lifecycle mode rotates `MP9 CRYOCHASM` and `MP3 PROVING GROUND` in the same context. `-resourcerooms 'ROOM A|ROOM B'` changes that list. `-resourcecycles` accepts 1..200 (default 4 for a pilot), and `-resourceframes` accepts 30..600 (default 120). Each reference/injected arm advances exactly one simulation step per presentation and compares world RGBA/depth at precisely the requested frame. The schedule's wall time includes room loading and GPU readback; it is not FPS evidence.

`-failure-stages` accepts a comma-separated diagnostic stage list or `none`. Defaults exercise `retained-promotion-after-vbo`, `retained-vao-after-create`, `retained-combined-after-vbo`, `depth-snapshot-after-framebuffer` and `shadow-after-framebuffer`; shadow injection requires enabled shadows. Requested stages must actually fire. Allocation failure must preserve ordinary fallback rendering and release partial owned objects. The depth allocation fixture directly exercises live scene D24S8 storage where a room is not eligible for cached replay; the shadow fallback reference disables shadows explicitly. No failure injection runs during normal client startup.

The fixture verifies exact repeated room pixels, owned-resource cleanup and retained generations per cycle, plus three actual Studio renderer/cache geometry/material reloads. Studio UI interactions are outside this test. RSS/managed memory are informational; unsupported Mac private-memory accounting is null, not a zero-memory claim. Thirty cycles supply longer-run evidence beyond the pilot. Mismatch files preserve reference/candidate RGBA, float depth, PNGs and exact frame metadata in `-resource-evidence` (default `<output>.resource-evidence`).

Independent timing probes accept `-render-scale 25..200` (percent), `-texturequality Automatic|Low|Medium|High|Ultra`, and `-lighting true|false`. The modernization resource runner separates unprofiled ABBA, profiled Low/High/Ultra shadows, and render-scale/texture-response proxies. Such proxies do not directly measure GPU bandwidth or overdraw.

## Controlled memory-attribution arms

`-resource-arm combined|map-gl|studio|audio` selects a lifecycle arm (default
`combined`). Use one immutable harness/client build for the whole campaign,
repeat in reverse order, and preserve the recorded identities. The modes are:

| Arm | Work per cycle | Audio | GL / Studio |
| --- | --- | --- | --- |
| `combined` | Two real map loads, exact fixed-frame fallback comparison, three Studio cache rebuilds | Ordinary scene audio | One window/context, ordinary renderer plus Studio |
| `map-gl` | The same two maps, players, RNG, simulation steps, rendering, failure schedule and readbacks | SFX stub; music device closed before measurement | Same window/context, no Studio |
| `studio` | Three geometry/material rebuilds of the existing 320x180 generated editor fixture | Music device closed before measurement | One window/context, no map scene or simulation |
| `audio` | Two SFX initialization/update/shutdown loads; exactly `-resourceframes` 1/60-second audio updates per load | Ordinary SFX banks, OpenAL owner and feedback warmup | No window/context, map scene or Studio |

All stress arms omit the historical harness's unused initial map load before
the first measured cycle. This changes the harness startup fixture; compare
these arms to a fresh `combined` run from **this same build**. Existing historical
reports remain unchanged. Non-stress renderer/performance modes retain their
ordinary startup path.

`map-gl` installs the existing `SfxInstanceBase` stub and wraps `ISceneServices`
only during `AddRoom`. Every policy is forwarded except
`AllowsPresentationSideEffects`; the two guarded statements skipped in
`SceneSetup.LoadGame` are exactly `Sfx.Load` and `Music.TryPlayRoomMusic`.
The original services are restored in `finally`, before `LoadScene` and every
simulation/render step. Headless and thumbnail switches are untouched. Music's
existing static constructor still performs one startup device probe; the tool
closes that device with its existing shutdown method before window construction.
`manifest.musicStartupProbeClosed` records this, so these arms **do not claim
zero startup audio allocation**. Repeated `Music.Init` still reads its managed
metadata but cannot start a closed device.

`audio` exercises SFX lifecycle and feedback initialization, including the
existing MiniAudio feedback device. It excludes room music, audible playback,
map resources, renderer and replay. It requests the same diagnostic unload GC
as `RenderWindow.EndScene`, without changing production policy. Shutdown may
overlap the next load by default; add `-resource-audio-wait` to wait up to 15
seconds after each unload for a separate serialized comparison. Native audio
fallback to a silent stub fails this arm, so unavailable devices cannot appear
to pass ownership validation. Context/source/buffer fields and owner-array
counts are observations of existing owners, not a complete OpenAL handle census.

Every map unload retains the existing release assertions and adds explicit
scene texture/framebuffer/program ownership release checks. A program still
bound after deletion may remain a valid driver name; it must have DeleteStatus
set and no remaining owner field. Such pending deletions are counted explicitly. Records include RSS,
private/virtual bytes when supported, managed live/committed/fragmented and LOH
metrics, allocations/collections, process thread/handle counts, thread-pool
workers/pending work, audio owner/pool counts and outstanding shutdown tasks.
Detailed task observations are emitted once in the final result, avoiding an
expanding task-detail array in every sample. GL counts cover the existing owned
resources; global driver object/residency accounting, VM categories and native
allocation stacks remain unavailable. Process accounting cannot distinguish
allocator retention from a driver cache or a native leak.

`-resource-drain` waits for **all** observed audio tasks, verifies completion,
then performs diagnostic full GC/LOH compaction/finalizers and `GL.Finish` where
there is a GL context. It records repeated post-drain samples at
`-resource-quiescent-samples 2..120` (default 6), separated by
`-resource-quiescent-ms 10..5000` (default 1000). Quiescence performs no more
loads, draws, audio updates or forced collections. These samples describe the
process while its context is still alive. They do not establish behavior after
context destruction. No lifecycle result is FPS evidence.

For a sequential campaign with immutable DLL hashes, SDK/runtime info, source
SHA/dirty state, environment flags, exact commands, logs and per-arm analyses:

```sh
PROJECT_PRIME_USER_DATA='/absolute/path/to/user-data' \
python3 tools/native-render-check/run_resource_arms.py \
  --dotnet /absolute/path/to/dotnet \
  --binary tools/native-render-check/bin/Release/net10.0/native-render-check.dll \
  --output /absolute/path/fresh-campaign-directory \
  --arms combined map-gl studio audio --cycles 60 --frames 120 \
  --quiescent-samples 30 --quiescent-ms 1000
```

The runner records source state from its own checkout, not an inferred commit
from a DLL. Actual client/harness SHA-256 and MVID are authoritative for build
identity; a baseline built via `MphReadProject` can have a different source SHA.
Use a second fresh directory and `--arms audio studio map-gl combined` for
reverse order. Extra shared harness flags may follow `--`, e.g.
`-- -failure-stages none`; separate the no-failure workload from the injected
workload and keep the choice identical in paired map arms. `--dry-run` prepares
the manifest/commands without opening a window or audio device. Do not rebuild
the DLLs or run another native diagnostic during a campaign.

Analyze an existing result independently:

```sh
python3 tools/native-render-check/analyze_resources.py /absolute/path/result.json \
  --warmup-cycles 5 --block-cycles 10 --output /absolute/path/analysis.json
python3 -m unittest discover -s tools/native-render-check -p 'test_*.py' -v
```

The analyzer excludes requested warmup cycles, reports whole-run and latter-half
OLS slopes per full cycle, block means/ranges and quiescent slopes per second.
Reference, candidate and Studio cleanup observations are separate series.
Unavailable/null counters stay unavailable; a short pilot with no post-warmup
samples yields no slope. R-squared is descriptive and does not supply confidence
intervals for autocorrelated reload samples. A flat/positive slope never
changes the report's `boundedness: UNVERIFIED` classification automatically.
Replay-only and full Studio UI isolation remain **NOT_RUN**.

## Audio managed-retention probe

The audio arm optionally accepts `-resource-retention-probe`. It runs the
same loads/updates/shutdowns in a `NoInlining` helper that returns before the
final drain. A separate non-inlined observer records weak references to every
actual SFX instance, candidate resource owner when present, bank data/list
fields, and every already-created decoded sample/stream byte payload. It never
forces an uncreated Lazy. The snapshots report per-load observations, live
load/item labels and observed/live payload bytes; shared objects can occur in
multiple observations, so these are not deduplicated bytes or retaining paths.
The PreparedAudio wrapper has already left Sfx.Load and is not observed here.
All record/telemetry values remain scalar-only; completed tasks are retained
as in the ordinary fixture.

Add `-resource-retention-second-gc` for a separately labeled second compacting
collection after the first full-GC/finalizer sample. This can reveal memory
released by finalizers or JIT frame lifetime; it does not change production GC
policy. The first collection/sample is always preserved, and the unchanged
ordinary audio fixture remains available when both flags are omitted. Result
schema stays at 2 with additional optional retention fields/phases. Compare the
**same new harness DLL** against both baseline and candidate clients, retaining
the original raw 60-cycle trials. Weak-observation instrumentation adds bounded
managed allocations and invalidates direct absolute comparison with the older
uninstrumented fixture.

```sh
# No assets, audio device or GL context needed; validates non-retaining
# observations, a retained completed task, and the non-inlined boundary.
dotnet tools/native-render-check/bin/Release/net10.0/native-render-check.dll \
  -resource-retention-selftest

# Two separately built/copied client directories can reuse one harness DLL.
python3 tools/native-render-check/run_resource_arms.py \
  --dotnet /absolute/path/to/dotnet --binary /absolute/baseline/native-render-check.dll \
  --output /absolute/fresh-baseline-directory --arms audio --cycles 60 --frames 120 \
  --quiescent-samples 30 --quiescent-ms 1000 -- \
  -resource-retention-probe -resource-retention-second-gc
```

Repeat against the candidate directory and reverse the order if ambiguous.
`vmmap <pid>` during quiescence can supplement process counters when the host
permits sampling; preserve unsupported/error output instead of treating it as
zero native memory. Neither low weak-reference counts nor a second collection
establishes that a specific native subsystem caused RSS growth.

For macOS native allocation-site attribution, use a separate profiling-only
process with `MallocStackLogging=1`, preserve the exact environment/binary
identities, and capture after the workload/drain while quiescence holds:

```sh
vmmap -summary PID > vmmap-summary.txt
malloc_history PID -allBySize -fullStacks > malloc-history.txt
python3 tools/native-render-check/analyze_vmmap.py base-vmmap.txt candidate-vmmap.txt \
  --output vmmap-comparison.json
python3 tools/native-render-check/analyze_malloc_history.py malloc-history.txt \
  --output malloc-stack-summary.json
```

The installed `malloc_history` manual defines `-allBySize` as currently live
allocation groups unless `-highWaterMark` is used. Avoid `-allEvents`, which
reports historical traffic instead. The parser separates mmap/VM reservations
from malloc-site groups and identifies OpenAL `BufferInfo` vector sites once per
group. Allocation origin is evidence about a subsystem; it does not isolate
which teardown operation failed or classify a library cache as a leak. Stack
logging changes allocator/CPU behavior, and this host warns that its own logging
pages count toward footprint. These runs provide no FPS evidence or direct
unprofiled-RSS comparison. Save unsupported/error output and stop escalating
when native sampling is unavailable.

A scoped `dotnet-gcdump` heap capture can supply object-type/root evidence in a
separate profiling trial. It requests another Gen-2 collection, so mark that
perturbation explicitly and retain the original first/second-GC samples and
unprofiled trials. Keep profiler binaries/dumps outside the repository. The
[official tool documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-gcdump)
describes the collection behavior and shared TMPDIR requirement. A cached
shared-pool byte array and a retained SFX bank are different roots; do not infer
one from total LOH bytes alone.
