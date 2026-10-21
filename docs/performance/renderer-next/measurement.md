# Measurement and acceptance lane

Prepared 2026-10-21. All commands below ran in native Windows PowerShell.
No macOS, Linux, Android device or WSL tests were executed.

## Implementation

- **R01:** Raw capture schema 3 retains timing-fence trigger intervals and the
  following intervals. Each row records its explicit interval start,
  generation before/after, stall/drop delta and reason. Timing fences keep the
  same measurement segment; focus, pause, cap, scene and lifecycle transitions
  have bounded boundary events and establish a fresh Present clock. Failed
  swaps and first/last boundaries are explicit. Overflow of either bounded
  record buffer prevents acceptance by the analyzer. Schema 2 remains readable.
- The desktop callback has a `finally` scope across gameplay and launcher
  branches. Raw rows include full callback and callback-to-Present durations,
  provider wait, existing input/simulation/preparation/submission/UI/upload/swap
  scopes, and explicit nested-scope semantics. Launcher callback CPU wall time
  is retained as an aggregate. Existing asynchronous GPU queries remain
  independent diagnostics; raw capture does not invent per-frame GPU duration.
- OpenTK owns event polling and numeric-cap software wakeup/deadline internals.
  These are not directly instrumented by this change. Full scheduler attribution
  and end-to-end capture overhead remain **UNVERIFIED**, so R01 is not full
  hardware acceptance and R02 cannot be closed from these checks.
- **R02:** Ordinary native harness now accepts Display (`-hz 0`), Unlimited and
  numeric caps, and admits Linux desktop execution. Platform-specific controls
  retain their restrictions. Windows tests of parsing/storage policy do not
  qualify Display behavior or establish Linux runtime support.
- **R13:** The AGX boundedness assessor requires exactly one candidate observation
  at the declared warmup anchor, in the same process as subsequent observations.
  A late plateau cannot hide growth from that anchor. Control-envelope and
  cumulative-span checks remain strict. Native AGX status is **UNRESOLVED_P0**;
  program reuse remains **NOT_ELIGIBLE / NO_ROLLOUT**.

## Executed focused validation

SDK: `C:\Users\Jarrett\.dotnet\dotnet.exe` (10.0.401), Python 3.14.7.

| Command | Actual result and scope |
|---|---|
| `dotnet run --project tools/frame-profiler-check/FrameProfilerCheck.csproj -c Release` | PASS. Links real `FrameTiming`, profiler and capture; drives 200 ms catch-up overflow and 500 ms stall, retains both trigger/following intervals and a 750 ms continuous hitch; checks focus/pause/cap/scene/cancel/failed-swap/first/final boundaries. Asset-free dependency seams; no native GL. |
| `dotnet run --project tools/frame-capture-check/FrameCaptureCheck.csproj -c Release` | PASS. Arithmetic, zero warmed Add allocations, frame/boundary overflow and schema-3 export. Synthetic storage test only. |
| `python -m unittest discover -s tools/native-fps -p test_analyze.py` | PASS, 13 tests. Schema migration, retained timing boundaries, missing-interval/reason/overflow rejection and existing strict schema-2/legacy checks. |
| `python -m unittest discover -s tools/native-render-check -p test_assess_agx_boundedness.py` | PASS, 17 tests. Includes missing-anchor late-plateau negative fixture, actual-anchor growth failure and cross-process anchor rejection. Synthetic evidence only. |
| `python -m unittest discover -s tools/native-render-check -p test_timing_samples.py` | PASS, 1 source-extracted C# test; valid/invalid Display/numeric/Unlimited caps and bounded duration/storage policy. |
| `python tools/native-fps/analyze.py docs/performance/native-fps/evidence/provenance-controls/pair-2-B/frames.json.gz -o <local-evidence>/historical-frame-report.json` | PASS. Historical schema-2 raw receipt remains readable; no current performance conclusion. |

Local evidence root:
`C:\Users\Jarrett\AppData\Local\ProjectPrime\renderer-next-20261021`.
An isolated asset fixture was prepared there under `assets` from the operator's
existing ROM using the existing client extraction path. `extraction.log` and
`assets/paths.txt` record successful AMHE1 extraction. Proprietary assets remain
outside the repository.

## Native qualification disposition

An immutable intermediate harness was copied to `native-candidate-1140` under
the local evidence root. Build PASS with one existing unrelated CS0649 warning
in `NetLobbyTest.CustomWeaponReadiness.cs`. The initial `--no-restore` attempt
failed because the tool had no assets file; the ordinary build restored and
succeeded without changing the SDK pin.

Hardware: NVIDIA GeForce RTX 5080, NVIDIA 617.42, OpenGL 4.6.0. Client MVID
`b6489b3e-4bbc-4763-a085-8b030f892efe`; client SHA256
`50e8b8e03fe92437041a8b81f6888e09b2c0c28de7c44d8fccdd435acae8e26e`;
harness SHA256
`1cc54e0fd1df15d1bfc7f4c09c904d05c3667de2561b2d218258e9be8386515a`.

All runs used `-room 'MP3 PROVING GROUND' -players 8 -size 1280x720 -seconds 8
-shadows Off`, Original preset/native scale, fixed input and existing bot AI.
`PROJECT_PRIME_GL_FRAME_CAPTURE` selected each fresh `smoke-<cap>-frames.json`.
Each raw receipt passed the strict schema-3 analyzer with zero dropped frame or
boundary records; each native result reports complete timing evidence.

| Cap | Measured seconds | Mean FPS | Interval p99 ms | Simulation Hz | Dropped steps / stalls |
|---|---:|---:|---:|---:|---:|
| 60 | 4.00 | 60.03 | 22.55 | 59.94 | 0 / 0 |
| 120 | 3.99 | 117.48 | 23.53 | 59.94 | 0 / 0 |
| 144 | 3.99 | 139.00 | 21.32 | 59.85 | 0 / 0 |
| 240 | 4.00 | 219.74 | 8.70 | 59.89 | 0 / 0 |
| Display | 4.00 | 59.49 | 33.35 | 59.97 | 0 / 0 |
| Unlimited | 3.99 | 469.58 | 12.65 | 59.97 | 0 / 0 |

Elapsed harness wall duration was 8.46–9.03 seconds including warmup. No focus
transitions were reported. Raw capture stores one fewer interval than the
harness, because its first measured Present establishes an explicit boundary;
the first interval is not presented as a dropped record. Evidence includes
`native-build-restored.log`, `frozen-binary-hashes.json`, `smoke-matrix.json`,
and per-mode result/log/raw-capture/frame-report files under the local root.

These are **functional diagnostic PASS**, with **cadence acceptance UNVERIFIED**:
other agents were developing/building concurrently, measurement windows are
short, and the binary is intermediate. Numeric-cap misses remain visible;
there is no attributed scheduler correction or claimed throughput gain. This
binary predates two subsequent telemetry-only repairs (warmup gameplay/launcher
aggregate classification and callback-exception boundary), so final integrated
qualification requires rebuilding. macOS/Linux/Android and AGX native
qualification are **NOT_RUN** under the user's Windows-only constraint. No
release defaults changed.

## R28 follow-up: startup ownership and frozen native callbacks

The later frozen integrated binary `4bad4ebc-6c44-4903-910a-f9dd80651633`
produced a failed 24-second 120-cap run: `final-120-result.json` reports
`complete=false`, 497 simulation ticks, 8.7585824 wall seconds, and zero
simulation drops/stalls. `final-120.log` records Avalonia wrong-thread setup.
The failed log/result/raw receipt are preserved; they are not valid complete
cadence evidence. An Escape event is inferred from the ordinary close path;
the pre-fix harness did not record its key source, so that trigger is not claimed
as recorded proof.

Source diagnosis found `Shell.AfterDraw` starting launcher maintenance/preview
workers for standalone render windows. Those workers access
`Dispatcher.UIThread` before lazy pause-menu setup can establish main-thread
ownership. The shared fixture's `assets/thumbnails.log` also records six native
thumbnail workers rejected by the harness at 12:19:02. Thus earlier standalone
timings may include launcher background work; retain their diagnostic status.
Automatic results UI is excluded by `EndScreen.PanelAvailable == false`.

The production repair narrowly gates first-frame startup on
`Active && ReferenceEquals(_window, window)`. Foreign/standalone windows do not
consume the one-shot flag. The native harness now suppresses and records live
keyboard/text/mouse callbacks before they enter the production window handlers;
frozen Scene snapshots alone had not suppressed Escape's event route. Focus and
window-close behavior remains active. Bounded value storage records counts,
first 128 discrete events, explicit detail overflow and the last key-down;
mouse motion cannot crowd out discrete event details. Physical gamepad polling
is unchanged and that isolation is not claimed. `launcherStartupScheduled=true`
now invalidates the harness's timing-evidence completeness.

Actual Windows focused validation:

```powershell
$env:DOTNET_EXE='C:\Users\Jarrett\.dotnet\dotnet.exe'
python -m unittest discover -s tools/native-render-check -p test_startup_ownership.py -v
```

PASS, two source-linked checks. The first compiles the actual `AfterDraw`
startup block with harmless scheduler seams and verifies inactive/foreign draws
cannot consume startup, an active owner starts once, and an explicit new session
can start again. The second links actual native input overrides and bounded
recorder, verifies Escape/text/mouse never reach the underlying handlers,
retains key provenance, and checks overflow plus zero warmed recording
allocations. No GL/hardware cadence claim follows from these checks.

Exact passing test names:

- `StartupOwnershipTests.test_actual_after_draw_startup_block_requires_active_owner`
- `StartupOwnershipTests.test_actual_native_overrides_suppress_and_record_escape_and_other_input`

The focused test output is retained as `startup-ownership-tests.log`.

The first integration rebuild exposed new BC7 code calling a missing
`GraphicsApi.CompressedTexImage2D`; this failed build is retained in
`startup-guard-native-build-bc7-negative.log`. The root owner corrected the
desktop storage upload call and arranged the subsequent integrated rebuild.
Updated native smoke and final qualification must use the rebuilt source;
pre-fix failures are never overwritten.

Integration build results retained separately:

- `startup-guard-native-build-bc7-negative.log`: FAIL, missing tracked-wrapper
  compressed upload entry point.
- `startup-guard-native-build-fixed.log`: FAIL, desktop upload enum/overload
  mismatch, corrected by the root owner.
- `startup-guard-native-build-final.log`: PASS for application/native harness,
  32.55 seconds, one existing unrelated CS0649 warning.
- `startup-guard-material-pack-build.log`: FAIL, missing `TextureSettingsGpuCheck`
  at `GpuCheck.cs:159` while the R21 helper was still being added; root informed.

No native timing or guard smoke was launched after these edits because other
shared-source packages were still changing. Final integrated qualification
belongs to a new frozen build; all previous failed and contaminated receipts
remain available.
