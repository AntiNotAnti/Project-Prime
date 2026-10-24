# Native desktop renderer diagnostic

`-authoredmaterials` enables HD texture replacements and advanced material channels
for this diagnostic process. Combine it with `-linearcolor` to exercise the linear
world renderer. The result records actual modern texture bindings, source material
draws and resident material bytes. This option fails acceptance if neither a modern
binding nor a source draw was observed, or a requested linear scene did not produce
its encoded output. It does not save graphics preferences.

Windows campaign tooling is `qualify_windows_renderer.ps1`; it requires a fresh
output folder, preserves failed cases, exits nonzero when any gate fails, and
checks frozen binary identity/conditions as well as the declared cadence gates.
`-SourceSnapshot` and `-BuildBinding` accept explicit build provenance digests;
posthoc working-tree hashes alone do not attest binaries. The manifest writer's
`--snapshot-source` mode inventories full tracked inputs before/after a build;
prefer outputs outside the checkout. Optional `--end-source-snapshot` validates
the end digest and build-input equality. See
`docs/performance/renderer-next/windows-tooling.md` for exact scope and checks.

Frozen-value display-pose regressions require an explicit
`PROJECT_PRIME_POSE_REFERENCE_DLL` and use `DOTNET_EXE` or native PATH discovery,
including Windows `dotnet.exe`; they do not select old historical builds.

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

Use caps 60/120/144/240, `-hz 0` for Display/VSync, or `-hz -1` for uncapped.
The ordinary harness accepts Windows, macOS and Linux; this support is distinct
from platform qualification. The 2026-10-21 implementation checks run on Windows
only. Platform-specific resource controls keep their existing restrictions.
Desktop VBOs, binding caching
and opaque batching now default on. For a VBO-only arm use
`PROJECT_PRIME_GL_BATCH=0` with `-gllegacybindings`; for binding-only use
`-gllegacylist`. Set `PROJECT_PRIME_GL_BATCH=0` for combined VBO/binding without
batching, and omit overrides for the full default path. Explicit enable flags
override an environment zero; legacy flags always win. Request `-glgpu` only after
checking driver support. Apple GL2.1 on the tested M4 Pro lacks timer queries.

The workload has eight bots, frozen keyboard/mouse snapshots, fixed RNG
seeds and a 1920x1080 framebuffer by default (`-size 1280x720` changes it).
The first 240 simulation steps are warmup.
Native keyboard/text/mouse event callbacks are also suppressed, so live Escape
cannot bypass the frozen Scene snapshots and open/close UI during a run.
`nativeInputIsolation` records bounded discrete-event provenance, complete event
counts and explicit detail overflow; motion only contributes an aggregate.
Window-close and focus callbacks remain active, so interrupted runs still fail.
Physical gamepad polling is unchanged and controller isolation is not claimed.
`launcherStartupScheduled` must be false: standalone diagnostics do not own
launcher maintenance/preview workers, and unexpected scheduling invalidates
`timingEvidenceComplete`. Source-linked regressions run with
`python -m unittest discover -s tools/native-render-check -p test_startup_ownership.py`.
Normal timing JSON now includes `observedWorkload` schema1: actual start/end
slot/hunter/bot roster, RNG assignment readback and start/end RNG states,
offline authority at both boundaries, exact measurement ticks, entry-only phase,
and SHA/MVID bindings to the loaded client/harness files. The ordinary `-hunter`
changes slot zero; other bot slots retain their existing rotating hunters. The
receipt explicitly records frozen-device snapshots with bot AI and
`replayExecuted: false`. Attaching a replay/Oracle artifact does not make this a
replay-driven workload. Binary observations occur before the measurement
baseline; final receipt observations occur after the final interval and their
allocation overhead is excluded. This is provenance evidence, not source/build
attestation or a device/performance qualification.

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

### Windows crouch rendering capture

`-crouchcheck` selects an actual offline Traversal match and drives one local
Hunter through synthetic Enter-to-spawn, Ctrl hold, and Ctrl release. It uses
native simulation and presentation; actor position, stance and model matrices
are never written by the diagnostic. A collision-clear external camera stays
fixed across all three captures. OS focus callbacks are recorded as observations;
this capture mode isolates its frozen synthetic keyboard from the production
focus-loss handler. It does not qualify OS focus or physical keyboard behavior.

```powershell
dotnet build tools/native-render-check -c Release
dotnet tools/native-render-check/bin/Release/net10.0/native-render-check.dll `
  -crouchcheck -room "MP3 PROVING GROUND" -players 1 -hunter Samus `
  -hz 60 -seconds 8 -size 1280x720 -shadows Off -outlines Off `
  -shots C:\evidence\crouch-samus -output C:\evidence\crouch-samus.json
```

Choose a fresh PNG directory. The check saves standing, crouched and released
images, each with a frozen render-only body-hidden negative control and an
unchanged-frame repeat. It requires visible native body pixels, stable repeated
readbacks, a shorter crouched silhouette, and restored standing height after
release. JSON includes PNG/RGB hashes, actual input/stance/position, silhouette
bounds, camera and GL context metadata. Use `-hunter` to run another Hunter in a
separate process. This is a narrow Windows image check; it does not report frame
timing, physical-device, online or replay acceptance.

### Current presentation acceptance — October 19, 2026

The fresh CPP-03/04 candidate passes all **28 seven-Hunter/four-rate cells** in
`native-pose-charge-retreat-v2`, including strict canonical/input comparisons,
camera transactions, positive native gun/charge controls, and beam/particle
consumed-value sampling witnesses. Its loaded headless and native rendered
replay checks also pass. See [the completion record](../../docs/implementation/cpp03-cpp04-presentation-completion-2026-10-19.md)
and `artifacts/cpp03-cpp04-completion-20261019/native-completion-summary.json`.
The campaign history below preserves earlier failures; it does not describe the
latest acceptance. Readback correctness is separate from performance and panel
scanout.

The serial runner creates fresh per-cell directories, pins the runtime and
read-only extracted assets, runs the Samus 60/144 pilot first, checks every arm
and within-Hunter comparison before proceeding, then rechecks runtime/assets:

```sh
python3 tools/native-render-check/run_display_pose.py \
  --dotnet /absolute/dotnet --binary /absolute/immutable/native-render-check.dll \
  --user-data-template '/absolute/asset-paths-template' \
  --output /absolute/new/matrix
```

The current harness admits steady schedules 60/120/144/240/360/500 on Windows
and macOS. `-pose-schedule jitter144 -hz 144` instead supplies the repeating
elapsed sequence `(0.5, 1.5, 0.75, 1.25) / 144` seconds. Its mean is 144 outputs
per simulated second and every interval is below one 60 Hz simulation step.
Both the input preview and ordinary `RenderWindow` callback use the same
declared elapsed value; the production frame clock remains the only scheduler.
The sequence uses no simulation RNG. A requested schedule does not measure
physical display refresh, scanout, input-device polling or performance.

The bounded NCX-06 recovery plan is thirteen serial arms: Samus at all six
steady rates plus jitter144, then Kanden/Trace/Sylux/Noxus/Spire/Weavel at 500.
Select it with `--ncx06-matrix` on the command above. Other Hunters have
single-rate coverage in that plan; only Samus has cross-schedule qualification.
Use `--jitter` to add a jitter144 arm per requested Hunter in an ordinary matrix.

New native reports retain the exact established gameplay/presentation hashes
and additionally record independent, fixture-local combat, canonical source
input/shot pose, and scene/global RNG witnesses per simulation tick. The runner
requires exact agreement for these witnesses across requested schedules,
including input edge states, and requires unchanged witnesses on zero-step
draws, repeated draws and temporary external-camera captures. These observations
do not change replay hash schemas, stored recordings or scheduling. The analyzer
can require them explicitly with `--require-supplemental`; `--require-jitter`
requires a jitter144 result per requested Hunter. Reports identify the actual
Windows or Mac platform, and cross-schedule comparisons reject mixed platforms.
The fixture scopes a disconnected view over connected controllers and blocks
physical polling, then restores cached input and focus state on completion or failure. Native OS focus and physical
mouse/controller behavior remain separate acceptance work.

`--require-particle-sampling` additionally requires both particle and beam
consumed-value counters (current at 60, positive history samples above 60).
Those counters do not substitute for isolated beam/free-particle pixels.

### Mac display-pose capture

`-posecheck` is an explicit mode in this same window. Unlike ordinary `-shots`,
it uses the normal `RenderWindow`/`FrameTiming` loop, supplies elapsed `1/hz`,
and prepares input only when the existing accumulator will take a simulation
step. It never forces one simulation step per output frame. The one declared
clock-reset checkpoint tests the existing presentation-generation fence.

```sh
PROJECT_PRIME_USER_DATA='/absolute/fresh/asset-only-user-data' \
dotnet /absolute/immutable/native-render-check.dll \
  -posecheck -room 'MP3 PROVING GROUND' -players 2 -hunter Samus \
  -seconds 24 -hz 60 -size 1280x720 -shadows Off \
  -shots /absolute/fresh/samus-60-shots -output /absolute/fresh/samus-60.json
```

Run a Samus 60/144 pilot first. The default matrix is Samus, Kanden, Trace,
Sylux, Noxus, Spire and Weavel at each steady output schedule
60/120/144/240/360/500. Every
process needs fresh asset-only user data, working directory, JSON and capture
directory. The existing `campaign_identity.isolate_user_data` copies only a
normalized `paths.txt` referencing the operator's read-only assets. Keep the
same immutable client/harness, asset identity, room, dimensions and options
throughout the matrix. No canonical settings, saves or credentials are needed.

The 1,440 simulation steps include 240 warmup steps, biped motion, power-beam
charge/release, the existing fidelity morph/attack/death/respawn script,
scope transitions, two uncaptured teleports and the explicit clock reset.
Thirteen checkpoints save first-person world/HUD/depth and an external-camera
native body image. A same-frame `HideModel` control must remove actual body
pixels while preserving the shadow path, and restoring it must restore exact
pixels. Repeated unchanged-view draws must preserve the established gameplay
and `ReplayPresentationHash` projections exactly. Each first-person → external
camera → first-person transaction must also preserve both entry/exit hashes;
named canonical differences are retained and fail the run. Charge/effect attachment
and scope FOV transitions are native-state witnesses; they do not independently
prove isolated effect or arm-cannon pixels.

After the strict normal pilot passes, `-posecheck -pose-pixels` explicitly adds
separate native arm-cannon and charge-effect controls at ticks 300 and 420.
The gun control requires the exact first-person gun branch, no smoke and no
attached effects, then suppresses only that branch. The charge control saves
the exact attached entry/element references and flags, temporarily omits that
entry from submission, then restores them. Both require a completed effect
pass, positive changed pixels, exact restored pixels, unchanged simulation
tick/step count and both canonical hashes. They never unlink/spawn effects or
restore canonical emitter/particle snapshots. A failure is retained.

Use `--require-pixel-controls` with the analyzer for that stronger stage; it
refuses ordinary pose captures as isolated gun/effect proof. Keep the immutable
engine/tool and stage identical across every rate in a comparison. Qualify the
Samus 60/144 pixel pilot before the seven-hunter/four-rate pixel matrix.

The historical RML64 Mac attempt was partial: Samus/Kanden/Trace pass all
four schedules, while Sylux at 60 fails the tick-420 isolated charge-pixel gate;
15 later arms were not run. A private natural-age probe also fails at 421/450
with nonzero native quads, so no checkpoint substitution is accepted. See
[the evidence record](DISPLAY-POSE-EVIDENCE.md) and its immutable receipts.

The original input scenario remains the default `native-pose-v1`. Explicitly
select `-pose-scenario native-pose-charge-retreat-v2` to add only normal backward
movement during charge ticks 361–390. The fire hold/release timeline and tick-420
pixel gate stay fixed. Room-collision and projected-depth diagnostics explain
occluded controls; they never substitute for positive pixels or exact restoration.
That historical revision required a fresh Sylux 60/144 strict pixel pilot and then a new
complete seven-hunter/four-rate matrix. Historical passing original arms cannot
fill its matrix. Every report records `scenarioId`; the analyzer rejects mixed or
unknown scenarios. Missing historical identities mean `native-pose-v1` only.
Use `--scenario native-pose-charge-retreat-v2 --require-pixel-controls` when
analyzing the new stage. RML66 Sylux passes all four schedules. The subsequent
Samus 60 arm fails the strict normal frame-step gate at output 604, after 603
accepted steps; its cause remains unverified. The chunk stops there, leaving
the new matrix at four PASS, one FAIL and 23 NOT_RUN. The failed receipt and
all earlier records remain preserved in `DISPLAY-POSE-EVIDENCE.md`.

Each output records the actual simulation-step count and interpolation alpha;
each step records prepared/consumed input and both established canonical hashes.
Extra model, ice, Kanden, effect and camera anchors are separate raw diagnostics.
Their differences neither extend the replay contract nor fail canonical
comparison: different interpolation alpha may legitimately change draw state.

```sh
python3 tools/native-render-check/analyze_display_pose.py \
  /absolute/fresh/samus-60.json /absolute/fresh/samus-144.json \
  --hunters Samus --rates 60 144 --output /absolute/fresh/pilot-analysis.json
python3 -m unittest discover -s tools/native-render-check -p 'test_*display_pose*.py' -v
```

The analyzer requires the complete requested matrix and reports the first
canonical/input divergence without tolerances. It rejects older camera reports
that recorded entry/exit differences only as observations. Its unit reports are
synthetic; source-linked tests exercise the actual frame clock, control aliases,
float-rounded actor/animation boundary and effect override ownership without GL.
Neither is a world-render acceptance run. Native captures include repeated
draws/readbacks, so this mode supplies no performance comparison. Requested
output schedules are distinct from panel refresh or scanout, and the reported
GLFW primary-monitor mode does not measure panel scanout. Replay playback,
OS focus behavior, network behavior and physical-device acceptance remain separate gates.

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
Rendered replay and full Studio UI isolation remain **NOT_RUN**. Windowless
reader/replica/seek isolation is provided by
[replay-memory-check](../replay-memory-check/README.md).

## Context teardown and recreation controls

`-resource-context-cycles 120,180` runs those cycle counts sequentially in two
actual unshared windows/contexts, using the existing map-gl or Studio stress
fixture. It requires `-resource-drain`. Each ordinary fixture produces its own
JSON and exact pixel/depth/ownership checks. After `EndScene` and actual window
`Dispose`, the process stays alive for `-resource-context-post-samples` samples
at `-resource-context-post-ms` intervals. No additional GC occurs after context
teardown. The workload helper returns before those observations; the managed
window wrapper can remain weak-alive until a later collection.

```sh
PROJECT_PRIME_USER_DATA='/absolute/operator-owned/user-data' \
PROJECT_PRIME_GL_FORCE_LEGACY=0 \
python3 tools/native-render-check/run_context_controls.py \
  --dotnet /absolute/dotnet --binary /absolute/immutable/native-render-check.dll \
  --output /absolute/fresh-context-campaign --cycles 120 180 --frames 120 \
  --samples 60 --capture-native-stacks
```

The optional native sampler adds `MallocStackLogging=1` and preserves live
`vmmap -summary` and `malloc_history -allBySize -fullStacks` captures before and
after every context disposal, together with commands, times, statuses and
hashes. Missing tools/captures are preserved, not reported as zero bytes. Stack
logging perturbs memory/CPU. Mapped platform resource records can carry AGX
compiler frames; they are not automatically live malloc bytes. The streaming
`analyze_graphics_stacks.py` separates explicit VM groups and reports
`mallocPrefixBytes` only when libsystem_malloc appears in the first three
frames. That prefix is an observed mechanism, not a complete heap census.

Scene cleanup now corroborates owner counters with actual `IsList`,
`IsBuffer`, APPLE/core `IsVertexArray`, `IsTexture`, `IsFramebuffer`,
`IsRenderbuffer`, `IsProgram` and `IsShader` queries against captured names.
Retail model leases, current character/editor lists, retained/combined geometry,
all named scene programs/world variants and attached shaders are covered.
Recognized deletion-pending programs/shaders remain separate from destroyed
names. This does not enumerate every object in the context or prove driver
residency release. Historical artifacts preceding this extension contain a
narrower ownership set and no native `IsList` witness.

Add `-resource-shader-source-ledger` to observe the existing canonical/world
variant shader-owner hook. The tool hashes GLSL after `ShaderSource` and before
compilation, retaining at most 64 stage/hash keys and scalar callback counts.
It restores the previous hook when the fixture closes. This requires a client
with the existing shader-owner hook; unsupported clients fail explicitly.
Compile/link callback counts are attempts, not successful operations. The
ledger excludes graphics pipeline, outline and HUD/UI paths without that hook;
equal source hashes do not prove identical driver pipeline keys or safe program
sharing. Source bodies and native owner objects are not retained.

GC telemetry includes cumulative pause milliseconds and the latest collection's
pause durations/generation/concurrent/compacted flags; transition observations
include context disposal wall time and GC pause delta. These do not isolate every
engine transition GC call or justify changing production GC policy.

For a final process-lifetime control, add `--driver-drain` to the runner:

```sh
PROJECT_PRIME_USER_DATA='/absolute/operator-owned/user-data' \
PROJECT_PRIME_GL_FORCE_LEGACY=0 \
python3 tools/native-render-check/run_context_controls.py \
  --dotnet /absolute/dotnet --binary /absolute/immutable/native-render-check.dll \
  --output /absolute/fresh-driver-control --cycles 30 --frames 120 --samples 10 \
  --driver-drain --shader-source-ledger --capture-native-stacks
```

After every window/context is disposed, the owner thread polls GLFW events,
observes that phase, terminates GLFW, and observes the still-live process. No
GLFW/GL/window API or window recreation follows termination. OpenTK's cached
initialized flag is not reset, so this is a final diagnostic phase, not a
reusable runtime teardown policy. No extra GC is requested. Default behavior
is unchanged unless the flag is supplied.

With native capture enabled, the runner supplies
`-resource-context-phase-acks <fresh-directory>` and acknowledges each capture
before the fixture advances. Manual reuse of any existing acknowledgement
directory/file, missing/empty/duplicate arguments, and option-like values are
rejected. A missing acknowledgement times out after 90 seconds; requested
captures must all exist and exit successfully for the runner to pass. Failure
evidence remains in the fresh output directory. Equal idle populations or
successful teardown do not establish retaining roots, driver cache/leak
classification, or repeated-workload boundedness.

Device-free argument/NoInlining validation:

```sh
dotnet run --project tools/native-render-check -c Release -- -resource-context-selftest
```

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

## AGX-01 immutable evidence gate

Context campaigns require `--user-data-template /absolute/operator-user-data`.
Only paths.txt is read; a fresh user-data root is created in the campaign. Asset
contents, binaries, capture files and per-context receipts have separate hashes.
All requested native phases are held until external sampling is acknowledged;
`--capture-native-stacks` no longer requires final GLFW termination. Source-dirty
or unsupported profiler evidence remains UNVERIFIED. The runner defaults to no
allocation failure injection; use a separate `--failure-stages ...` campaign.
Run `validate_context_campaign.py /absolute/campaign` to check existing evidence.
See [AGX-01](../../docs/engineering-next/agx01.md) for limits and rollback.

### Minimal shader compiler controls (AGX-02)

`--minimal-gl` enters before user-data, assets, graphics preferences, Scene or audio initialization. It creates only serial 32×32 unshared desktop compatibility contexts and uses exact `Shaders` source strings (legacy depth-vertex source by default). `--minimal-gl-depth-vertex` explicitly selects the other source. No production renderer/cache/default changes are made by these controls.

| Mode | Contexts | Compile/link operations | Draw operations |
|---|---:|---:|---:|
| `no-shader` | iterations | zero | zero |
| `compile-only` | one | iterations × selected variants | zero; draw-time driver allocation NOT_RUN |
| `compile-delete` | one | iterations × selected variants | once per newly linked program |
| `fresh-contexts` | iterations | selected variants per fresh context | once per newly linked program |
| `rebind-draw` | one | selected variants once | iterations × selected variants |

Variants: `tiny` (literal GLSL 1.20 triangle), `canonical`, `native`, `plain`, `unlit`, and `four-prewarm` (canonical/native/plain/unlit live together until retirement). `--minimal-gl-define NAME=scalar` and `--minimal-gl-bind attribute:location` are repeatable explicit inputs. `--minimal-gl-sampler-unit 0..15` identifies synthetic sampler state. An inactive define/attribute/sampler does not prove a changed driver pipeline key.

The source/stage SHA-256 and link-input SHA-256 are separate from draw-state hashes. Drawing initializes identity matrices/32 palette entries, uses an untextured synthetic triangle and changes override color. Every draw requires nonblack pixel coverage; subsequent draws on the same program require changed pixels. Raw RGBA hashes and coverage/change counts are recorded. This is a non-vacuous diagnostic control, not gameplay fidelity validation. Sampler units are set explicitly but textures/material permutations are not exercised. True shared contexts, GLES, full game state, driver leak classification and boundedness remain NOT_RUN/UNVERIFIED.

Build and device-free checks:

```sh
dotnet build tools/native-render-check/native-render-check.csproj -c Release
# A stable external client can be referenced for minimal-control-only builds:
# add -p:MphReadProject=/absolute/path/to/prime/src/MphRead/MphRead.csproj
# To exercise this branch's new source/status hooks, build its own client project.
dotnet tools/native-render-check/bin/Release/net10.0/native-render-check.dll --minimal-gl-selftest
dotnet tools/native-render-check/bin/Release/net10.0/native-render-check.dll --minimal-gl-describe --minimal-gl-variant four-prewarm
python3 -m unittest discover -s tools/native-render-check -p 'test_*.py'
```

The self-test drives the same program-owner rollback through a fake GL facade: allocation/compile/link/bind/draw injected failures, failed compiler/link statuses, detach-before-shader-delete and unbind-before-program-delete. It verifies no recognized owned names or attachments survive. Real-driver lifecycle closure and positive pixel coverage require a native run and are not implied by this self-test.

For a reviewed native run, the serial runner records immutable source/binary/MVID/OS/OpenTK identities, exact arguments, retained scalar event receipts and optional outstanding native-stack/VM captures:

```sh
python3 tools/native-render-check/run_minimal_gl_controls.py \
  --binary tools/native-render-check/bin/Release/net10.0/native-render-check.dll \
  --output /fresh/path/minimal-tiny-compile-delete \
  --mode compile-delete --variant tiny --iterations 30 \
  --capture-native-stacks --compress-stacks
```

Native capture acknowledgements hold `minimal-NNN-before-workload`, `after-workload`, and `after-disposal`; each phase waits until profiling completes. There are no context recreations after a GLFW termination control: this small runner does not perform GLFW termination. `--max-rss-mib`, `--min-free-disk-mib` and timeout stop an unsafe campaign while preserving partial evidence. `--dry-run` starts no native process. Gzip preserves complete stdout and both compressed/uncompressed hashes. Profiling changes allocator/CPU behavior, so these results cannot be used as FPS evidence.

`--minimal-gl-fail-at` supports `vertex-allocated`, `fragment-allocated`, `vertex-compiled`, `fragment-compiled`, `program-allocated`, `attached`, `linked`, `bound`, and `drawn`. Injected native failures intentionally produce unsuccessful receipts with completed rollback observations. Bind/draw stages require a drawing mode.

Desktop map/source accounting also now observes existing opt-in thread-local callbacks in canonical scene/world variants, GraphicsPipeline, PlayerOutlines, launcher photo, launcher/thumbnail diagnostics, and desktop GraphicsApi wrappers (including OpenGlVisualCheck). Normal startup leaves callbacks null. Facade CompileShader/LinkProgram events count actual attempts and immediate status exactly once; owner hooks are source attribution only. Current sources are hashed at each compile, including repeated compilation and reused names. `--shader-source-ledger-selftest` verifies nested facade/owner hooks, source reassignment, recompilation/relinking, reused names, generic routes and disabled no-query behavior. Compile/link attempt counts are distinct from immediate GL success/failure status counts; incomplete status coverage is UNVERIFIED. Launcher/preview/diagnostic paths require separate positive workloads and are NOT_RUN when a map campaign does not execute them. GLES and third-party native UI shader creation are outside this ledger's scope. GL-name counts do not equal compilation attempts or native allocation groups.


## Project Prime Next GL comparison controls

`gl_comparison.py` verifies the committed path-sanitized historical GL-02 derivative receipts and
recomputes per-run arrays/medians. Integrity PASS retains the measured performance
REJECTED/default-off decision. Its native `run` defaults to a dry schedule;
`--execute` is explicit. `--kind regression` performs only one same-binary
PALETTE_PREP OFF/ON eight-player High capture pair with exact decoded RGBA and
float32 depth comparison plus owned-name retirement. Timing, broader captures
and profiles use separate kinds and fresh output directories. The required
expected loaded MVID, physical harness/client files, before/after hashes and
isolated asset-only settings prevent the earlier wrong-target launch problem.

See [GL completion evidence and native recipes](../../docs/engineering-next/gl-completion.md).
Unsupported Windows/Linux/GLES/device gates remain NOT_RUN; historical M4 Pro
receipts do not certify a newly built integration binary.

`sanitize_gl_evidence.py verify` checks derivative hashes/lengths and numeric projections. With `--original-root`, it also checks the external originals against their SHA256/lengths; neither mode depends on private host paths embedded in command labels.

### Sustained frame timing

Ordinary timing accepts `-seconds 8..1804`; the first 240 simulation ticks remain
warmup, so 604 requests ten minutes and 1804 requests thirty minutes of measured
simulation time. Existing capture modes remain bounded to 120 seconds and pose
mode remains exactly 24 seconds. Slow runs may have different elapsed wall time;
the existing timeout and dropped-simulation counters remain visible.

Raw interval storage is allocated before loading/warmup, bounded to 4096 returned
frames per requested measured second (at most about 59 MiB). It never grows in
the measured loop. Overflow records `droppedIntervalRecords`, stops with nonzero
exit, and sets `timingEvidenceComplete` false. Do not accept a partial run.
Allocation, collection and measurement-end counters freeze at the final measured
interval, excluding later receipt generation, percentile sorting, JSON and scene
cleanup. `managedBytes`/`workingSetBytes` remain post-run observations, not peak
residency. A callback interval includes the harness's post-Present bookkeeping;
the profiler's raw capture timestamps the actual swap-return boundary separately.

### Frozen pose input and native focus controls

Pose readback mode observes OS focus separately from its synthetic keyboard/mouse
owner. Native focus callbacks do not latch that synthetic device off; ordinary
native timing, resource and lifecycle modes retain the production focus path.
OS focus and physical input acceptance remain NOT_CLAIMED. A pose-only scoped
disconnected gamepad view blocks fixed and render-time polling plus hotplug
callbacks, while retaining registered physical devices and preferences. The
fixture restores cached input/edge/action/context state on exit, including
exceptions. Reports retain whether a physical controller was connected before
isolation and whether fixed/presentation input was isolated. Run the bounded
content-free `--pose-input-isolation-selftest` to exercise a connected input
negative control, isolated polls, nested scopes and exception cleanup.
Required charge fire
and release are checked at consumption, so missing input fails before pixel
qualification; PowerBeam/tick420 and all positive pixel/canonical gates remain.

`-pose-focus-control isolated` injects a labelled loss/gain callback at tick245.
Compare its exact canonical timeline with `none` on the same frozen binary.
`legacy` explicitly routes that injection through the old production hardware
path to reproduce suppression. It cannot complete/qualify normal pose acceptance.
All three values require `-posecheck`; these are diagnostic controls, not player
settings or hardware latency tests.

The analyzer explicitly rejects `legacy`, including a forged completed/PASS
report. `isolated` requires the labelled loss/gain pair at simulation frame244,
immediately before tick245. Compare a baseline `none` arm and an `isolated` arm
with the same binary, schedule and FOV; the exact canonical/input/combat/RNG
timelines remain the acceptance gates. The negative control must remain failed
or incomplete and must never fill a positive matrix cell.

`-pose-fov 60|78|120` selects a world-FOV setting for that opt-in native pose arm.
Omitting it preserves the existing setting. Reports distinguish the requested
and selected setting, camera-authored degrees, and actual world/viewmodel
projection radians. Scope changes preserve the authored magnification policy;
the world angle during a scope is not asserted to equal the selected setting.
The fixture restores the prior FOV when it releases its input isolation. Use
the same explicit setting for all rates in a cross-schedule comparison.

For a bounded native cartridge-gun FOV sweep without an HD replacement pack,
the existing client diagnostic has a separate explicit mode:

```sh
PROJECT_PRIME_USER_DATA='/absolute/fresh/asset-only-user-data' \
dotnet /absolute/immutable/ProjectPrime.dll \
  -viewmodelacceptancecheck 'MP3 PROVING GROUND' -hunter Samus \
  -nativeprojectiononly -output /absolute/fresh/native-projection
```

That mode raises the gun through the existing movement-input helper, freezes
simulation, requires actual native weapon-mesh packets at each selected FOV
60/78/120, and captures three PNGs. It requires an unchanged weapon projection,
three distinct actual world projections, the camera-authored scaling policy,
and unchanged established canonical hashes. It writes
`native-projection-acceptance.json`. This supplies fixed-pose OpenGL evidence;
scope/high-refresh/physical-input/latency acceptance remains separate. Existing
HD `-projectiononly`, animation and material checks retain their pack requirement
and behavior.

# Renderer audit qualification controls

`-scale 25..800` selects the existing render scale (default 100). Results now
distinguish requested target size from the actually allocated `renderSize` when
capability/byte budgets force fallback. Use 50/100/150/200 for capture checks;
image/readback runs remain separate from timing. `-posecheck` requires 100%.
Final renderer audit evidence and the Windows-only platform constraint are
recorded in `docs/performance/renderer-next/status.md`.
