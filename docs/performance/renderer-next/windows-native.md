# Windows native qualification

All commands execute on Windows with the pinned .NET10.0.401 SDK. The GPU is
NVIDIA RTX5080, OpenGL4.6, driver617.42. The CIM display snapshot reports
5120x1440 at239Hz; this is not a measurement of scanout, VRR or latency.
Renderer timing uses its own recorded cap, framebuffer and presentation facts.
No other-platform runtime result is inferred.

## Intermediate integrated build

The first frozen source/binary campaign uses client MVID
`5d14b5fe-80be-41a1-838a-b3c59a02d46f`, SHA256
`f22d19de70681de8e7a1dfa48ec514e96969904a83a3e2cf4c99ba8c19d12e74`.
The native harness SHA256 is
`16972a839bd3a582368ee8d37cbfc0318ebe19ba3c601998737278072e6d4245`.
A clean Release Rebuild reproduces both DLL hashes. Source inventories and
explicit operator build metadata are retained externally. They establish
digest correspondence, not cryptographic compiler attestation.

| Check | Observed result | Scope |
|---|---|---|
| Preparation `--native` | PASS 14,822 | Portal capacity/parity, geometry ownership and shadow receiver synthetic fixtures |
| Portal clipping negative control | 100,000 legacy calls allocate 38,400,000 bytes; candidate 0 | Audited loop versus warmed bounded scratch; elapsed 31.920/32.276 ms in this run, so no CPU-speed gain claimed |
| Geometry reuse | Five meshes compile to four objects, 960 to 768 retained bytes; exact fixture pixels | Three load/unload cycles; a small synthetic resident-memory improvement, not total process/driver memory or gameplay FPS |
| Material `--gpu` | PASS default/BC7/BC7 plus `-gllegacy` | Real GL sampling, queue/toggle/ownership/failure checks; opaque KTX repair not yet in this DLL |
| BC7 four-channel fixture | 5,568 versus 21,840 GPU-format bytes; maximum channel error 0 | Four flat invented textures only; decode/upload observations are diagnostic, not pack quality or throughput acceptance |
| Resource/shader ownership | PASS 1,680 failure scenarios over 120 cycles | Zero failure count, clean recovery/disposal; does not establish AGX boundedness |
| Visual check | PASS 474 pixel assertions,17 captures | Includes79 production source-GX assertions; invented assets, no broad scene/motion acceptance |
| Detached shadows | PASS 215 | See [fixture and negative witness](dynamic-shadow-casters.md) |
| Spatial AA | Four functional policy modes PASS after fixture repair | Synthetic encoded-domain filter; NO_ROLLOUT; initial missing-owner fixture failure retained |
| Map resource arm | Three cycles PASS, owned resources zero after each unload | Map GL ownership without audio; separate from timing |
| Studio resource arm | Three cycles, three rebuilds each PASS; zero owners after unload | Renderer/cache reload, not full editor UI/four-view interaction |

Small logs are in this directory (`windows-*-final.log` and
`windows-spatial-aa-owner-repair-*.log`). Large images, raw data, frozen outputs,
host/source inventories and native map/Studio receipts remain under
`C:\Users\Jarrett\AppData\Local\ProjectPrime\renderer-qualification-20261022`.
The map/Studio receipts end in `resource-map-gl-final.json` and
`resource-studio-final.json`; despite those original filenames, they belong to
this intermediate MVID. Later source repairs require their own final receipts.

At that intermediate handoff, final timing and soak acceptance still required
the readback, cross-scene decode and OPAQUE KTX repairs to be rebuilt and
requalified together. The final v4/v5 results follow below.
Every failed or inconclusive attempt remains preserved and labeled.

## Final integrated v4/v5 identity

The rebuilt client includes the later cross-Scene decode lifetime, regional
thumbnail/calibration readback and OPAQUE KTX fallback repairs. Its MVID is
`53589a2c-2a2a-423c-94cc-24d50b7f6355`, SHA256
`26906006a69c2275eb385e58ad1c1007167c137ec43b3eb541c74c2b73cbe729`.
The native harness SHA256 is
`3eaf1d516939e49568d70af8cfb10a9d14555a2f883004960f5703e9e45eb1ba`.
The operator's complete 489-file frozen-bundle comparison reports v4 and v5
identical, including dependencies/maps/assets. Consequently the v4 native
functional checks and v5 timing/capture checks below exercise the same frozen
application bundle; matching only the primary DLL would have been insufficient.

Explicit before/after source snapshots, successful Windows Release Rebuild logs,
full frozen-file inventories and `source-v4-build-binding.json` /
`source-v5-build-binding.json` remain in the external evidence root above.
The v5 binding reports unchanged build inputs and all non-documentation inputs,
with source snapshot SHA256
`108e4e6904c817020bc987088e9ed0dab43fb6d2c8580d12337ccba1bd072936`
and end snapshot SHA256
`cce118d2eb3b8db172357376656a678ee74e2300c220805b9d6f31b79fec7cac`.
These are explicit operator digest/build records, not cryptographic compiler
attestation or acceptance on another device.

| Final functional check | Observed result | Limit |
|---|---|---|
| Real Scene decode lifetime | PASS in default, BC7 and BC7 plus `-gllegacy` material runs | Actual stale pump and unload retire blocked workers without waiting; shared reservations bound new-Scene admission; late prepared results do not publish. Does not bound codec-private scratch or prove RSS/frame time. |
| Regional capture/thumbnail | PASS native RGB/RGBA crop and actual thumbnail worker | Different read/draw FBOs, hostile pack PBO/row/skip/alignment, exact bottom-up bytes, bounded worker release and incomplete-FBO restoration. Calibration median/empty/failure behavior is separately source-linked, not a native cel visual qualification. |
| OPAQUE KTX fallback | PASS RGBA/Basis/fixed-block refusal and fallback identity checks | No optional albedo allocation/negative-image growth for refused OPAQUE KTX; accepted recolor base retained, nonopaque KTX transparency and OPAQUE PNG normalization preserved. MASK and full R22 color/alpha migration remain unqualified. |
| Material queue/settings/ownership | PASS all three policy runs; 56 pack assertions each | One settings job per picture, desired/applied lag and supersession, stable handles, direct/retained mip freeze, bounded prewarm and teardown; synthetic fixtures. |
| BC7 storage/pixels | PASS four flat channels: 5,568 versus 21,840 format bytes, maximum channel error 0 | Supported opt-in only; disabled by default and by strongest legacy policy. Individual decode/upload timings are diagnostics, not HD-pack quality or throughput acceptance. **NO_ROLLOUT**. |
| Preparation/receivers/geometry | PASS 14,822 | Current native synthetic parity/ownership/failure checks; no portal-heavy total-frame speedup claim. |
| Resource/shader ownership | PASS 1,680 failure scenarios over 120 cycles; zero failures | Separate native resource test, not AGX boundedness. |
| Map/Studio resource arms | Three cycles each PASS | v4 receipts show main resource ownership/rebuild cleanup; not full launcher/editor/four-view user-flow acceptance. |
| Visual source-GX/material fixtures | PASS 474 pixel checks, 17 captures, including 79 source-GX assertions | Invented fixtures, not full-scene motion or all color contracts. |
| Detached shadows | PASS 215 | Current Parkour parity repair; real-map motion/cost remains unqualified, candidate stays opt-in. |
| Spatial AA | Four policy modes PASS; enabled synthetic filter/copy GPU p50/p95 0.015488/0.015648 ms at 1280x720 | 30 samples, CPU p50/p95 0.0245/0.0658 ms, scratch 3,686,400 bytes. Test waits/uploads excluded; no gameplay/motion/device acceptance. Default off, legacy wins, **NO_ROLLOUT**. |
| Native harness Python regressions | PASS 134 tests, one skipped | Windows symlink privilege skip; all four explicit frozen-value pose cases execute. Offline correctness, not native timing. |

Receipts: [native composite](windows-composite-v4.log),
[default materials](windows-material-default-v4.log),
[BC7 materials](windows-material-bc7-v4.log),
[forced-legacy materials](windows-material-bc7-legacy-v4.log),
[preparation](windows-cpu-preparation-v4.log),
[native Python suite](windows-native-python-v4.log), and
[AA diagnostics](windows-spatial-aa-enabled-v5.log).
The [decode contract](texture-decode-lifetime.md),
[readback contract](capture-regions.md) and
[OPAQUE KTX fallback](opaque-ktx-fallback.md) explain the narrower repairs.
Their earlier pending handoff text is superseded only for the actual checks
listed here. Earlier build/fixture failures and inconclusive runs remain intact.

## Final v5 numeric/display matrix

The six cases in external `matrix-v5/summary.json` all record **PASS**. Settings
were MP3 PROVING GROUND, eight bots, fixed seeds/input snapshots, 1280x720,
Original/native appearance, scale 100 and shadows Off. Each requested 24 simulation
seconds excludes 240 warmup steps, leaving approximately 20 measured seconds.
No builds, other tests, periodic profiler stream or GPU timer query workload
ran during the declared timing window. Raw capture overhead is included and
remains a separate unqualified cost.

| Mode | Completed callback mean FPS | Interval p99 ms | Samples | Measured seconds | Gate |
|---|---:|---:|---:|---:|---|
| 60 | 59.975 | 17.5132 | 1,199 | 19.991750 | PASS |
| 120 | 119.872 | 9.1713 | 2,397 | 19.996268 | PASS |
| 144 | 143.860 | 7.8234 | 2,877 | 19.998576 | PASS |
| 240 | 239.692 | 4.9387 | 4,793 | 19.996476 | PASS |
| Display (`0`) | 239.770 | 4.9101 | 4,795 | 19.998307 | PASS functional/storage/conditions |
| Unlimited (`-1`) | 1,009.685 | 1.5809 | 20,193 | 19.999305 | PASS functional/storage/conditions |

All six record zero simulation drops/stalls, complete chronological storage,
matching immutable binaries/conditions, valid finite metrics and duration,
and no launcher startup. Raw Present interval coverage is approximately
99.923–99.996% of the callback measurement windows, within the declared fixed
100 ms endpoint tolerance. The numeric cases meet the predeclared mean ±2% and
p99 ≤ twice-period gates. Display/Unlimited do not receive an invented panel
cadence target. Their FPS values describe application returns, not physical
scanout, VRR, photon latency or a general gameplay guarantee. Unlimited supplies
headroom evidence for this workload/host; this is not a matched-baseline speedup.
The small run log is [windows-matrix-v5.log](windows-matrix-v5.log).

## Scale captures and sustained gate

External `scale-v5/summary.json` records **12 CAPTURE_PASS** cases: scale
50/100/150/200 crossed with shadows Off/Low/High. At the 1280x720 output, requested
and observed world sizes are 640x360, 1280x720, 1920x1080 and 2560x1440 respectively.
Each case records complete capture/storage, matching identity/conditions and
no launcher startup. These runs take screenshots and use capture stepping;
their callback rate and shortened wall windows are **not FPS acceptance**.
Readback stalls and capture work must not be mixed into the numeric matrix.
See [capture campaign log](windows-scale-v5.log); images remain outside Git.

The final v5 240-cap combat soak records **PASS** against the predeclared scoped
gate: 1,800.0138169 measured seconds, 431,761 native callback intervals, mean
239.865381 FPS and p99 4.8192 ms, with zero simulation drops/stalls. The native
interval sum agrees with the measurement window. Raw Present intervals cover
1,800.0113051 seconds, fraction 0.9999986046 of that callback window. Identity,
immutable binaries, window/settings conditions, finite metrics, duration and
complete storage all pass; no launcher startup is recorded. Total wall time was
1,806.3769295 seconds. The small receipt is external `soak-v5/summary.json`;
the retained run log is [windows-soak-v5.log](windows-soak-v5.log).

## Report-only interval tails

The [tail receipt](windows-timing-tails.json) derives the following from the
already saved native callback interval arrays. It binds each native receipt by
SHA256, uses nearest-rank p99.9 (`sorted[ceil(0.999 × N) - 1]`) and counts strict
threshold exceedances without a jitter tolerance. These calculations did not
rerun gameplay or change the acceptance gate.

| Mode | p99.9 ms | Maximum ms | > one numeric period | > 1.5 periods | > 2 periods |
|---|---:|---:|---:|---:|---:|
| 60 | 20.4564 | 40.2222 | 571 | 1 | 1 |
| 120 | 12.1256 | 33.3910 | 1,197 | 2 | 1 |
| 144 | 9.5286 | 31.2299 | 1,391 | 2 | 1 |
| 240 | 6.1145 | 28.4729 | 2,269 | 4 | 1 |
| Display | 6.3699 | 29.3872 | — | — | — |
| Unlimited | 2.2935 | 26.6647 | — | — | — |
| 30-minute 240 | 5.2564 | 32.3563 | 214,193 | 84 | 34 |

Numeric period means `1000 / selected cap` ms. Display/Unlimited have no assumed
display deadline. The soak contains eight intervals above 16.666667 ms and none
above 33.333333 ms. No interval in any of the seven saved native arrays exceeds
100 or 250 ms. The receipt retains these absolute-threshold counts for every
run. Every short numeric run nevertheless has one interval beyond twice its
period; the soak has 34. A passed p99 gate permits such rarer misses and must
not be described as every frame meeting its deadline.

Native intervals end at harness callback returns and include post-Present
bookkeeping. Raw capture intervals end at application Present returns, so their
first/last endpoints and total duration differ slightly. Neither measures GPU
completion, physical scanout or photon latency. The original mean ±2%, p99 ≤
twice-period, zero simulation drops/stalls and complete matching-condition gates
remain narrower than all-frame deadline, VRR, thermal or universal FPS acceptance.
The soak adds sustained evidence on this host; it is not a temperature/thermal
telemetry experiment or a matched-baseline speedup.

## Attribution of the saved long intervals

A single report-only streaming pass over the existing raw captures is retained
in [windows-timing-attribution.json](windows-timing-attribution.json). It did
not rerun gameplay or change source. The six short runs' worst intervals all
occur at simulation tick 314, approximately 1.24–1.26 seconds into the measured
window. They share segment 5, presentation generation 3 and `BoundaryReason=none`.
These are midrun intervals, not the first/warmup boundary or final receipt work.
The corresponding raw and callback intervals agree closely.

At that tick the six runs record base callbacks of 24.55–26.42 ms, simulation
10.52–11.42 ms and render 13.92–15.23 ms. Nested HUD time is 12.69–14.01 ms and
upload time 4.03–5.09 ms; swap is approximately 0.031 ms. Allocation in the
captured callback is approximately 4.86 MB. This locates substantial work inside
the actual measured callback, with a reproducible simulation-tick association;
it does not identify a particular HUD/asset operation. Inclusive pass scopes
must not be added together. No screenshot requests or timing-boundary tags
explain away these intervals, and they remain in the recorded tails.

The soak's worst raw interval is 32.3546 ms at simulation tick 34,228, about
566.477 seconds into raw capture. Its current base callback is only 2.876 ms
(simulation 0.7835, render 1.7494, swap 0.2852 ms). The gap from the preceding
base callback's end to the current callback's start is 29.4801 ms; instrumented
provider wait is 0.0001 ms. This interval is therefore dominated by time outside
the instrumented base callback. The saved data cannot separate event handling,
derived harness bookkeeping, ordinary loop work, OS scheduling or other causes
within that gap. It is not evidence of a GPU bottleneck or proven pacing sleep.

All 34 soak intervals above twice the numeric period have `BoundaryReason=none`,
stable generation/segment and zero recorded stall/drop flags. Twelve have render
time exceeding half the interval, twelve have an unattributed-gap majority,
two a simulation majority and three a swap majority; five are mixed. These are
descriptive scope buckets, not root-cause verdicts. No per-frame Gen0/Gen1/Gen2
increment is recorded on those 34 frames or the six short worst frames. The
soak nevertheless records 293/76/3 total collections; the instrumentation lacks
GC pause timestamps and cannot exclude collection work between captured scopes
or attribute a pause duration. GPU timing and OS scheduling traces are absent.
The final capture-stop boundary is separate from the saved intervals, and no
boundary classification removes the observed midrun misses from acceptance
evidence.

Required physical Android, macOS/Linux, AGX, broad lifecycle/multiview and
real-scene visual/cost gates remain separate. Spatial AA and BC7 stay opt-in
**NO_ROLLOUT**; no presets/defaults or other-platform runtime qualification are
promoted by these Windows results. Whole-plan completion is not established by
this scoped Windows timing pass.
