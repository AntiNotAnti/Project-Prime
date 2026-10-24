# Replay Studio fidelity and editing implementation status

Date: 2026-10-22. Baseline: `984aa77dc70ffc274ae0629eb41b1d1d607a87f8`.

This change implements the principal capture, editor, seek, curve and export repairs.
Local Windows acceptance now includes real NVIDIA OpenGL rendering, extracted game
assets and FFmpeg encoding, using generated accepted-fact recordings. It does **not**
close the audit's complete acceptance gate: no representative human match,
captured-source image/audio comparison, physical display pacing, isolated GPU
timing or Windows/macOS/Linux/Android device matrix was recorded. Generated fixtures
and successful builds must not be described as that evidence.

## Implementation map

| Plan item | Implemented | Remaining acceptance or implementation |
|---|---|---|
| RPL-00 | Existing frame/GPU profiler retained; bounded update/seek percentile windows and separate read/rebuild/capture timings; provenance labels | Representative real recording baseline: CPU p50/p95/p99, display 1% lows, memory, GPU passes, readback/encode time, exact coverage |
| RPL-01 | Full renderer search proves producer already exists at `Renderer.cs`; selectable persisted high-fidelity recording profile; coverage/drop telemetry | Instrument an actual 10+ minute match and compare recorded final draws with replay |
| RPL-02 | Owner schema 4: 4096-entry producer queue, compressed 256-picture lifecycle chunks, SHA-256, indexed immutable read snapshot, legacy v1-v3 reads, no old lifetime sample cap; lossless offline crop backpressure | Cold owner chunk misses still read/decompress synchronously; audio/unrecorded FX/remote POV are not newly captured |
| RPL-03 | One verified owner source per playback/export; replacement scenes borrow its lifetime; four active loaders with cancellable admission | Real alternating-seek/cancellation soak and cold I/O timings |
| RPL-04 | Latest target wins; ordinary seek loop yields after 3 ms between steps; one leased immutable checkpoint read/decode worker; corrupt fallback is prefetched; bounded checkpoint LRU | Scene construction/restore and a simulation step remain atomic; canonical packet readers still reopen with replacement scenes; broader nearby-frame predecode not implemented |
| RPL-05 | Memory preview on frozen world; release commits once; explicit atomic Save; 100 Undo/Redo; dirty marker; guarded basic persistence; source-safe recovery journal and Discard; actual Windows native edit/drag/save/recovery checks | Representative pointer latency, real-recording qualification and crash/kill-process soak |
| RPL-06 | Camera v6/state v2 by default, opt-in v7/state v3; 1024 authored keys; bounded visible handles; previous formats migrate; cropped boundary tests; updated portable/import/export envelopes | Long-session native timeline performance measurement |
| RPL-07 | Binary interval/arc caches; independent orientation/FOV/roll modes and easing; safe time-aware SQUAD/spherical cubic; monotone lens curves; 78,279 checks; rendered preview/export equality at eight rates | Look-at overrides orientation and abrupt subject motion/switches can still turn abruptly; mixed modes/explicit easing are intentional velocity boundaries |
| RPL-08 | Three-slot PNG worker; exact backpressure/drain/cancel ownership; 240 FPS admission; actual repeat PNG/MP4 tests, 720p and 4K HUD targets | Readback remains synchronous; optional PBO/pooling/streaming and representative throughput/memory soak remain |
| RPL-09 | Primary composite reuse; three auxiliary submissions when qualified; under-100-ms gesture cache; settle refresh; rollback switch | Adaptive resolution not implemented; isolated GPU timings and other-platform qualification remain |
| RPL-10 | Coalesced seeks, rational FPS samples/report; actual local world, durable, cadence, rendered export and synthetic owner tests | Genuine sampled source captures, event/image/audio parity and desktop/Android device matrix remain mandatory |

Canonical replay protocols, accepted-fact simulation and authoritative gameplay
are unchanged. Camera and owner sidecars remain separate from replay authority.
Missing captured presentation stays explicitly reconstructed. See the detailed
[owner track](replay-owner-fidelity.md),
[camera editor](../architecture/replay-camera-editing-20261022.md), and
[export/four-view pipeline](replay-export-preview-pipeline.md) notes.

## Verification

The matching .NET 10.0.401 SDK was installed locally without changing `global.json`.
The replay candidate's desktop and dedicated-server Release builds passed. The Android workload was installed
locally, and the full Release `android-arm64` build passed, including AOT and signed
APK packaging (27 warnings, zero errors). It uses the existing local Android SDK 36
and JDK 17; this is compilation/package evidence, not device playback acceptance.
The opt-in Android managed experiment also compiles/packages in Release. Its APK
contains no RmlUi native bridge. Fresh Android ARM64/x64 bridge builds and the
separate native resource APK are NOT_RUN: this host has no installed/cached NDK.
The ordinary default APK is restored after the experiment; neither APK establishes
native UI or playback behavior on an Android device.
Read-only device checks found no connected ADB target and no configured AVD.
WHPX acceleration and an API 35 x86_64 image are available, but the current app
has no automated replay acceptance activity/intent. No emulator was created or
booted and no APK was installed on a device; a new CPU-only activity would repeat
math checks without establishing replay rendering acceptance.

An intermediate full working-tree desktop build failed on a concurrent unrelated
HUD edit referencing missing `HudProfile.AccentInk`. That work was preserved and
subsequently corrected by its concurrent author. The final ordinary desktop
Release build passes with two existing warnings and no source substitutions;
see `artifacts/replay-validation/desktop-final-current-tree-build.log`.
Fresh isolated-process format (3111 checks), controller and launch-UI checks also
pass against that final ordinary Release binary.
After the export working-directory fix, the shared-code server Release build
passes again (one existing warning), and default Android ARM64 passes again with
full AOT/signed packaging (27 existing warnings). The desktop replay diagnostic
also compiles with two existing warnings using a compile-only substitution of
the baseline HUD preview file; the concurrent source file is untouched. This
isolates fault-injection replay acceptance; the final ordinary full checkout
build is verified separately as described above.

```powershell
dotnet build src/MphRead.Android/MphRead.Android.csproj -c Release -r android-arm64 `
  -p:AndroidSdkDirectory=/absolute/Android/Sdk -p:JavaSdkDirectory=/absolute/jdk17
```

The final source-linked matrix passes all 15 projects, reporting 85,920 checks
(cumulative timeline output counted once). Separate native GL, archive, owner,
checkpoint, export and platform results are listed below.

- Production owner codec/identity/lifecycle/crop/loader checks, including ten
  **synthetic** minutes at 240 Hz (144,001 samples).
- Actual detached checkpoint byte-reader CRC, cursor isolation, native-handle
  lifetime after reader retirement and error cleanup checks.
- Source-linked player/transport ownership, soft-budget yielding, stalled read
  supersession and corrupt-latest/earlier-fallback checks.
- Camera controller, presenter and native source-linked migration/editor/recovery
  suites, including 100 Undo/Redo transactions and 1024-key crop equivalence.
- Export worker/controller/presenter checks, including real detached PNG encoding,
  cancellation/drain, buffer lifetime and rational FPS samples.
- Existing fidelity export and context analysis checks; frame-profiler checks.
- Actual NVIDIA OpenGL/FFmpeg exports and virtual-cadence acceptance on generated
  recordings; detailed scope and remaining real-source gates below.

| Focused suite | Result |
|---|---:|
| Native source-linked camera migration/editor/recovery | 1187 checks pass |
| Independent orientation/lens math, formats and crop | 78,279 checks pass |
| Camera controller / presenter | 412 / 754 pass |
| Native advanced camera adapter | 40 pass |
| Owner presentation, including full offline crop | 103 pass |
| Source-linked player/transport/seek worker boundaries | 54 pass |
| Production detached checkpoint reader | 7 pass |
| Export worker / controller / presenter | 61 / 28 / 62 pass |
| Timeline input tickets/density | 67 pass |
| Existing fidelity export / context analysis | 49 / 62 pass |
| Profiler and parity-report failure/source-protection checks | Pass |
| Encoder process/pipe/cancel and long working directories | 29 pass, including actual Windows process startup beyond MAX_PATH |
| Detached ticket file/JSON/runtime helpers | 23 pass, including private-path bounds, exact input/runtime hashes and atomic status publication; synthetic CPU fixture |

The optimized-JIT camera comparison uses the original `HEAD` evaluator and the
working-tree evaluator in isolated processes with the same 64-key Spline/Bezier
fixture, constant speed, warmup, batches and checksums. One local synthetic run
reported Spline p95 601.56 -> 99.22 ns and Bezier p95 1256.25 -> 101.56 ns per
sample, with matching checksums and zero measured sampling allocations. These are
batch-normalized CPU timings on a concurrently active Windows host, not whole
editor latency, cold-start or GPU measurements. Reproduce with:

```powershell
./tools/replay-camera-benchmark/compare.ps1 -Dotnet /absolute/path/to/dotnet
```

## Executed local rendering acceptance

Windows, .NET 10.0.401, NVIDIA GeForce RTX 5080, OpenGL 4.6.0 / NVIDIA 617.42.
No software renderer was used. An isolated profile referenced existing extracted
AMHE1 assets; generated recordings, images, movies, logs and FFmpeg stayed outside
Git under `%LOCALAPPDATA%/ProjectPrime/replay-validation-20261022`. Its
`acceptance-summary.json` records commands, individual binary hashes and scope.
FFmpeg 9.0.1 (Gyan essentials, SHA-256 verified) encoded actual libx264 MP4s.

The Windows native bridge was rebuilt and packaged with the complete 317-entry
replay action registry. Existing action indices, the 40-byte packet and protocol
version are unchanged. The loaded DLL was independently attested against its
canonical path and SHA-256
`02d2e9870c1b7cdbde92e51451442f1c70d81aad2dd5babb3b39e8175ed74646`.
Interactive fault-injection tests use a frozen diagnostic engine build with the
shipping default native policy; ordinary Release builds and launch diagnostics
are checked separately.

| Executed acceptance | Result and scope |
|---|---|
| World coverage | PASS: 20 selectable modes, 1801 generated frames each, detached restores/continuations and file/frozen-clip seeks |
| Determinism/report | PASS: 1801 hash frames, 14 cold/cached/coalesced seeks, five rates, 454 rational FPS state samples |
| Long replay | PASS: production-generated 18001-frame recording, indexed replay hashes/rates/seeks; five-minute indexed/unindexed current-engine benchmark also passed |
| Virtual host cadence on actual GL | PASS: 600 identical gameplay frames at 60/120/144/240/360/540 virtual Hz, no dropped/stalled simulation frames |
| Durable archive | PASS: six checkpoints, cold/backward seeks, corrupt optional checkpoint fallback and nested clips |
| Rendered export | PASS: repeated identical PNG sequences at all eight rates, actual 720p and 4K HUD targets, MP4, serialized queue/reel and cancellation |
| Synthetic owner presentation | PASS: generated 240 Hz camera/gun/animation/cosmetic samples, repeated PNG identity and actual MP4 at all eight rates |
| Independent curve export | PASS: v7 camera, live preview plus rendered view/projection equality, repeated PNG/MP4 at all eight rates, source/gameplay hashes unchanged; repeated on a 68-frame fixture |
| Theatre | PASS: 620 isolated frames, camera/player changes, four seeks, pause and foreground/transport sentinels |
| Replica isolation | PASS: 1801 hashes, 11186 animation restores, seven detached worlds, 1500 continuation and 793 projectile frames |
| Accepted live history | PASS: 1801 accepted-fact frames, eight checkpoints, 251 frozen frames, nested clips and reset |
| Materialized clip | PASS: 801 source/clip frames from source frame 800 |
| Killcam lifecycle | PASS: 2689 checks, 24 death/skip/respawn/disconnect cycles, 314 historical frames, resize/match/final/audio-owner handoff; startup fixture waits for the first budgeted visible picture |
| ROM-backed owner draw scope | PASS: 18 actual replica/viewmodel restoration assertions, including injected failure and unchanged authority checkpoint bytes |
| Legacy launch UI diagnostic | PASS: five fresh isolated processes plus the final default Release binary; fixture drains initial deferred search before injecting its synthetic selection |
| Native four-view stress | PASS for the scoped rendering gates in three configurations: compatible 1280x721 window/902x400 viewport uses three auxiliary renders; natural incompatible 1280x720 and explicit rollback at 1280x721 use four, without cache reuse. Each passes 100 toggles, injected tile failure/retry, seeks to 60 and 0, canonical-world equality and clean GL checks |
| Native independent channel editor | PASS: 16 channel transactions, exact metadata, Undo/Redo, recovery, Save/reload/export capture, actual graph drag/cancel/commit and unchanged canonical world; six styled controls pass strict visibility/size/nonoverlap checks and screenshot review |
| Native Studio workflow and independent workers | PASS: complete 1280x721 workflow exits cleanly; detached worker produces 577 1280x720 PNGs (368 distinct hashes) across Library teardown/reopen and foreground seek; exact clear-pending/cancel/private retry produces 121 PNGs with parent unchanged and children retired; three-frame MP4 has video/audio tracks and 2400 authored PCM frames |
| Windows long export paths | PASS: actual private render-worker retry after the original 292-character working-directory failure; 29 subprocess checks; actual FFmpeg/libx264 two-frame output into a 344-character directory |
| Native resource lifecycle | PASS: 11 framebuffer/attribute restoration assertions plus 120 shader ownership cycles / 1680 scenarios, zero failures |
| DS/render viewport scope | PASS: 584 linked checks, including ordinary viewport retention and DS restoration on success/failure |

The two fallback matrix variants were deliberately stopped after their completed
rendering/seek gates, before unrelated authoring/audio/export phases; their
reports say `SCOPED_PASS`, with the full workflow explicitly `NOT_RUN` for those
variants. This compares measured submission counts, not GPU throughput.
The full Studio run took 87.2 seconds on the instrumented replay candidate with
the compile-only baseline HUD substitution described above. Its source archive
and native-module pins stayed unchanged; callback observers were removed and no
owned worker processes remained. The generated archive has no recorded event in
the reviewed audio range, so recorded-event cue author/update/remove and cue
waveform qualification are explicitly `NOT_RUN`. Authored PCM mixing, device
selection, comparison, portable export/import and actual audio/video mux passed.

Acceptance found and repaired Windows process startup on long nested export
paths. Private worker inputs now use short per-user temporary roots, retries use
fixed-depth sibling output directories, and long-output encoder processes use a
short owned working directory. Failed/cancelled preparation retains the original
accepted ticket for another retry. Accepted private snapshots retain their prior
disk lifetime; automatic cleanup remains deferred. Details are in the
[export pipeline notes](replay-export-preview-pipeline.md).

The short curve export measured 164 frames across 16 runs: cumulative CPU/driver
readback 1134.17 ms and worker PNG validation/encode/write 11529.21 ms; the maximum
occupied queue was three frames. Work overlaps and the runs are tiny, so these
numbers do not establish sustained 240 FPS throughput. Detailed per-run timings
are retained in `curve-export-final/export-metrics.json`. A subsequent run after
the viewport/framebuffer cleanup repairs also passed all eight rates (164 frames;
readback 931.61 ms, worker PNG 10910.25 ms, queue maximum three). These remain
short diagnostic runs rather than throughput benchmarks.

The local determinism report measured seek p50/p95/p99 of 0.045/87.22/237.85 ms
and interactive update p50/p95/p99 of 3.07/6.17/19.50 ms on this generated fixture.
Maximum update was 46.08 ms. The 3 ms budget yields between indivisible steps and
restores; it is not a hard deadline. The synthetic five-minute benchmark compared
current-engine indexed/unindexed seeking (cold target 18000 median: about 21/1805 ms),
not before/after revisions or representative combat workloads.
The production-generated fixture retains 59.8 MB of timeline data with ten restore
points; its private clip measured 6.66 MB of managed memory. These are fixture
measurements, not bounds or real-match memory expectations.

These tests use real assets and rendering but generated authority facts. The owner
export generates its owner sidecar. Neither is a genuine captured-source oracle.
Physical refresh, display 1% lows, real match audio/image parity and other OS/device
qualification remain outside this local evidence.

## Real recording acceptance commands

Use an isolated profile with the operator's valid `paths.txt`, locally extracted
assets and a genuine source recording. Keep those inputs outside Git. Preserve
source SHA-256 and record platform/adapter/driver/settings for every run.

```text
ProjectPrime -replaydeterminism SOURCE -replayreport REPORT.json
ProjectPrime -replaybenchmark SOURCE -output OUTPUT
ProjectPrime -replaycadencecheck SOURCE
ProjectPrime -replayexportcheck SOURCE -output OUTPUT
ProjectPrime -replayownerexportcheck SOURCE -output OUTPUT
ProjectPrime -replaycurveexportcheck SOURCE -output OUTPUT
```

The determinism report's state samples exercise replay simulation clocks and
reconstructed presentation hashes. They are not GPU export captures.
`ImageParity`, `AudioParity` and `PhysicalDisplayPacing` are deliberately
`NOT_RUN`; a missing/invalid source must produce a failed result, never a pass.
The existing export checker creates synthetic owner poses when that mode is
selected; it is not a source-camera oracle for a genuine match.

Compare synchronized genuine source/replay captures with
`tools/render-parity/compare.py`, using explicitly reviewed per-capture thresholds.
Keep first-person shooting, timed impact, morph/third-person transitions, respawn,
killcam, pause/speed/seek, HUD and audio evidence distinct. See
`tools/render-parity/README.md` for its capture/threshold contract.

Disable high-fidelity recording in Settings to retain ordinary capture. Set
`PRIME_REPLAY_PRIMARY_REUSE=0` at startup for the original four auxiliary render
passes during hardware A/B qualification. Old camera formats remain readable;
saving an inherited document writes v6/state v2; independent channels write
v7/state v3, which older app revisions may not understand.
