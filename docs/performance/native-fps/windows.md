# Native Windows evidence — October 19, 2026

The user requested the Windows machine after the initial Mac timing exclusion.
Runs use the interactive Windows desktop (session 1), RTX 5080, Ryzen 7 9850X3D,
NVIDIA GL 4.6 driver 617.42. Windows reports a 5120×1440 display at 239 Hz;
the normal test framebuffer is 1920×1080. Driver VSync/VRR overrides and physical
scanout are UNVERIFIED. Each receipt records requested/effective cap, software
frequency, VSync, focus, dimensions, loaded MVID/SHA and actual workload.

The [short summary](windows-short.json) and [raw index](evidence/windows-short/index.json)
retain all 18 runs. They are diagnostic: measured windows are only 8/20 seconds,
and progress queries read completed result JSON during the campaign. Early cap
receipts also observe other session-0 dotnet processes; their activity/cause is
not proved. These short runs cannot establish uncontended qualification. Fresh
asset-only user data, 240 fixed warmup ticks, Original preset, 100% resolution,
MP3 PROVING GROUND and eight offline bots were used. Source was
`ff76552e811710989bca332598053be9012a7b52`; client MVID was
`26f3ff93-8305-42e4-aff6-7f5e46d50c3d`. No comparisons pool this binary with later
pacing/provenance/sustained binaries.

## Baseline and overhead controls

Five profiler-off Unlimited runs have median p99 **1.4168 ms**, range
1.3591–1.6260 ms, and median true 1% low **576.91 FPS**. The full p99 range is
18.84% of the median. Five bounded-capture runs, interleaved ABBA, have median
p99 **1.4701 ms**, range 1.3714–1.6247 ms, and median true low **558.78 FPS**.
These diagnostic controls do not establish negligible capture overhead. No
renderer candidate is eligible from these numbers, and no 10–20% game FPS
improvement is claimed. Normal release capture remains off.

| Initial requested cap | Mean callback FPS | p99 interval ms | Dropped simulation steps |
|---:|---:|---:|---:|
| 60 | 60.109 | 17.6174 | 0 |
| 120 | 78.535 | 116.2821 | 28 |
| 144 | 99.484 | 46.6987 | 1 |
| 165 | 155.542 | 21.5951 | 0 |
| 240 | 239.699 | 5.1921 | 0 |
| Unlimited | 1208.040 | 1.5224 | 0 |

Bad initial cap results remain in the report. Later corrected-binary 120/144
traces are separate receipts, not replacements or a claimed explanation of
these transients. One reported 239 Hz monitor does not establish the plan's
multi-panel refresh/VRR/forced-driver matrix. No new scheduler is enabled.

## Measured owners

After the measurement marker, discard the first partial CPU window and first
GPU window per pass. Values below are median inclusive CPU p50 divided by median
frame interval p50. Parent and child scopes overlap: **never sum these shares**.

| Owner | Shadows Off ms / share | Shadows High ms / share |
|---|---:|---:|
| World | 0.2604 / 34.4% | 0.3893 / 40.9% |
| Preparation | 0.1840 / 24.3% | 0.2022 / 21.3% |
| Opaque | 0.1477 / 19.5% | 0.2733 / 28.7% |
| DrawItems | 0.1421 / 18.8% | 0.1604 / 16.9% |
| ItemSetup | 0.0909 / 12.0% | 0.1222 / 12.9% |
| HUD | 0.0576 / 7.6% | 0.0588 / 6.2% |
| GeometrySubmission | 0.0186 / 2.5% | 0.0286 / 3.0% |

Asynchronous native GPU queries are supported with zero dropped queries. Median
World GPU p99 is 0.562064 ms Off / 0.547552 ms High; High shadow p99 is about
0.097408 ms. These are separate pass windows. CPU swap p99 is 0.0463 / 0.0449 ms,
which is not GPU execution. HUD glyph/sprite caches hit in the steady windows,
with median upload bytes zero. No upload, UI or shadow bottleneck justifies a
new cache, ring, quality reduction or default flag. FPS-04/05 are NO_ROLLOUT.

Raw captures show distinct repeatable simulation tails around ticks 314 and 344:
8.16–8.67 ms with ~39 KB allocation and a later 2.8–3 KB HUD upload, versus
2.63–3.16 ms with ~920 KB allocation and no upload. Neither has a coincident GC.
Existing scene initialization already loads common/active Hunter models,
weapon effects, shader variants and decoded sound banks. The exact causes remain
unknown/mixed; allocation or low tick numbers alone do not prove JIT or loading.

## Diagnostic correction and sustained run

The five initial raw captures contain a ~40 ms gap at tick 242. Same-binary
late/early controls prove the cause is diagnostic DLL hashing after warmup.
Moving it before warmup reduces the median boundary gap from 40.3277 to 1.148 ms.
See [FPS-03](fps03.md) and its complete [raw index](evidence/provenance-controls/index.json).
This does not establish a gameplay renderer speedup.

The later raw-profiler fix retains continuous hitches above 500 ms. Checking all
adjacent timestamps in the earlier same-segment short/control captures finds no
hidden >500 ms boundary (maximum 11.379 ms between stored records); their largest
40–45 ms diagnostic interval is the first retained interval. Explicit lifecycle
segments still exclude interrupted boundaries. The unprofiled sustained harness
uses its own bounded callback sample array and is unaffected by this filter.

The [earlier protocol attempt](evidence/windows-excluded/index.json) retains a
real nonzero 144 Hz pose exit: its original scenario did not exercise the native
beam presentation policy. That attempt was subsequently closed before sustained
qualification for the hashing correction. The corrected campaign explicitly uses
`native-pose-charge-retreat-v2`; it cannot erase the original failure.

Sustained results and native pose pilot analysis are recorded separately under
their frozen source/loaded binary. Screenshot/pose readbacks do not qualify FPS.
Unknown physical devices, impaired networking, Studio/UI/overlay and full
hunter/map matrices remain NOT_RUN unless their own receipt says otherwise.

## Corrected sustained result

The [full sustained receipt](windows-sustained.json) and [raw index](evidence/windows-sustained/index.json)
record 1,800.761 measured seconds after warmup, 429,116 callback intervals, zero
interval-record loss, zero focus/window-state transitions, unchanged asset trees
and unchanged loaded client binary. Mean FPS is **238.297**, interval p50/p95/p99/
p99.9 is **4.1634 / 4.6045 / 4.7792 / 10.7553 ms**, true 1% low is **135.906 FPS**,
and the separate reciprocal-p99 proxy is **209.240 FPS**. Late rates are 0.2976%
over 1.5× budget and 0.2051% over 2×.

**The stability gate fails: 26 dropped simulation steps.** Process exit zero and
`timingEvidenceComplete` mean the bounded receipt completed; they do not mean
performance or simulation acceptance. A dotnet-build/MSBuild session external to
this campaign began at 03:03:57.856 UTC, roughly 28 seconds before the native
process ended. All >100 ms intervals cluster near measured elapsed 1782–1789
seconds. Correlation is not causal attribution: there is no per-frame simulation-
drop/GC witness in this unprofiled array. No samples are deleted. Five-minute
trend blocks in the receipt show p99 around 4.71–4.75 ms in the first 25 minutes
and 8.93 ms in the last full block; those blocks are descriptive, not separate
A/B qualifications. An uncontended 30-minute acceptance remains NOT_RUN.

The measured allocation counter is 14,522,184,768 bytes, GC collections are
291/28/2, and reported end managed/working-set sizes are 246,205,192 / 186,028,032
bytes. The allocation/collection counters freeze before analysis/cleanup; managed and
working-set snapshots are read after percentile analysis, before cleanup, and
are not a heap residency proof. Lightweight process snapshots stayed bounded.
Scene cleanup retired all enumerated model lists, buffers, VAOs, programs,
textures, FBO/RBOs, HUD caches and existing retained/procedural/depth counters;
this is owner retirement, not complete driver-residency enumeration. A post-exit
NVIDIA reading was 37°C/P8/15.35 W; it does not prove thermal behavior in the run.

The corrected 120/144 captured traces also fail the ±2% cap gate: **98.399 / 124.524
FPS**, p99 **55.3877 / 27.7579 ms**. The 120 trace has 22 simulation drops and ten
explicit capture segments, so strict analysis rejects a whole continuous raw
Present comparison. The 144 trace has no simulation drops. Earlier pre-correction
protocol traces happened to meet those caps, but cannot replace these negative
results or establish their mechanism. No alternate scheduler is promoted.

The explicit V2 Samus 60/144 native pilots pass exact canonical/camera, native
beam/particle and isolated gun/charge pixel controls. The analyzer retains its
historical `PASS_BOUNDED_MAC_DRAW_QUALIFICATION` label, but these receipts report
Windows NVIDIA GL and their actual loaded MVID. They validate scheduled native
draw correctness, not panel scanout, FPS, hardware input or OS focus acceptance.

## Functional witnesses and fixture isolation

Native MP3 eight-player map sweeps at draw rates 1/3/4 each completed 2,377
simulation steps with identical summary counters. Rates 3/4 report zero draws
advancing simulation, zero Lockjaw RNG changes and zero trail mismatches, but
zero active native Lockjaw trail checks: that path was not exercised. Freeze,
burn and disrupt probe coverage was also incomplete. These counters are bounded
smoke witnesses, not complete combat, replay or multiplayer event parity.
See [functional evidence](windows-functional.json) and the
[initial raw receipts](evidence/windows-functional-initial/index.json).

The initial full pose matrix passed four Samus rates, then failed Kanden/60
because synthetic held input stopped reaching the main player. [PR797](https://github.com/AntiNotAnti/Project-Prime/pull/797)
isolates the pose fixture from OS hardware focus callbacks. Its same-binary
legacy/isolated controls reproduce the old input suppression and retain identical
1,440-step canonical/input streams for the corrected arm. Production input,
focus behavior, rendering and protocol are unchanged. See [FPS-06](fps06.md).

The corrected matrix initially passed 16 cases (Samus, Kanden, Trace and Sylux
at 60/120/144/240) before Noxus/60 exhausted the Windows disk during screenshot
and result serialization. Its zero-byte screenshot, absent result and nonzero
exit are preserved in the [interrupted attempt](evidence/windows-pose-interrupted/index.json).
Only this task's three obsolete source/build copies were removed, freeing 5 GB;
the frozen active binary and all original result receipts were retained. The
remaining twelve cases use a fresh output directory and the exact same runtime
and asset identities. No failed attempt is counted as a passing matrix cell.

The [final combined analysis](windows-pose-analysis.json) passes **28/28 cells**:
all seven Hunters at 60/120/144/240 scheduled draws, with all isolated native
gun/charge, camera/canonical and particle gates unchanged. All **21 cross-rate
comparisons** match **1,440 canonical/input steps exactly**. The recovery's
[raw index](evidence/windows-pose-recovery/index.json) contains the remaining
12 passing cells. Both plans have identical runtime-file and asset-tree hashes;
both final receipts confirm unchanged runtime/assets. The one failed disk-full
attempt is hash-bound in the combined analysis and never fills a matrix cell.
Actual Windows NVIDIA context and client MVID identify the run despite the
analyzer's historical MAC status name. This is bounded scheduled-draw correctness,
not OS focus, physical scanout, photon latency, full network/replay parity or FPS.

To reproduce the combined correctness analysis, restore `result.json.gz` for the
16 passing cells under `windows-pose-interrupted` and all 12 under
`windows-pose-recovery`; exclude the absent failed17 result. Pass the restored
28 results to `tools/native-render-check/analyze_display_pose.py` with
`--require-pixel-controls --require-particle-sampling --scenario native-pose-charge-retreat-v2`
and a fresh `--output`. The committed comparison lists every selected receipt
and result SHA; the raw indexes verify restored bytes and both plans' identities.

## Reproduction

Each gzip restores original raw bytes, with SHA-256 in its index. JSON is UTF-8
(optional BOM); PowerShell logs are UTF-16. For example:

```sh
python3 tools/native-fps/analyze.py docs/performance/native-fps/evidence/windows-short/paired-1-A/result.json.gz -o report.json
python3 tools/native-fps/analyze.py docs/performance/native-fps/evidence/provenance-controls/pair-2-B/frames.json.gz -o capture-report.json
```

The CLI reports chronological intervals, nearest-rank tails, true tail-mean 1%
low, the separately labelled reciprocal-p99 proxy, late rates, fixed simulation
Hz, cap/VSync and profiling/override evidence. It never automatically qualifies
hardware or declares a rollout winner. Native callback-return intervals include
post-Present bookkeeping; raw profiler intervals use actual Present returns.
Neither measures panel scanout, photon latency or Reflex (the provider is null).
