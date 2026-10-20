# Native FPS decision — October 19, 2026

The program adds bounded diagnostic capture, retains long hitches, corrects stale Display fallback
measurements, fixes diagnostic sampling boundaries and wires asset-free regression
checks into CI. No new renderer optimization or graphics-quality downgrade is
enabled. The simulation remains fixed at 60 Hz. No physical latency improvement
or 10–20% game FPS gain is claimed.

The user's request explicitly authorizes committing and merging completed slices
without waiting for review/CI. Hosted exact-head/final-main results are therefore
not represented as green acceptance. Device measurements are bound to the frozen
source and loaded binary in each receipt, even while other work advances main.

| Slice | Source work / disposition | Evidence status |
|---|---|---|
| FPS-00 | Audited current source/flags, froze workload identities, retained raw Windows A/A and attribution | Windows diagnostic baseline with recorded contention limits; Mac timing excluded |
| FPS-01 | Opt-in bounded chronological capture and true tail-mean 1% low; legacy schema retained | Storage/arithmetic/allocation tests and native lifecycle controls pass; enabled overhead not claimed negligible |
| FPS-02 | Fresh source-owned Display fallback measurements; existing explicit-cap scheduler preserved | 53 policy assertions pass; 120/144 native cap gate FAIL, mechanism unproven |
| FPS-03 | Removed measured render-thread DLL-hashing artifact from diagnostics | Same-binary late/early control receipt; no ordinary gameplay renderer speedup claim |
| FPS-04 | Inclusive World/Preparation/ItemSetup owners measured; no isolated safe work-removal candidate proved | NO_ROLLOUT; renderer implementation not attempted on counters alone |
| FPS-05 | Existing HUD caches hit, steady upload misses negligible; GPU timers supported on RTX 5080 | NO_ROLLOUT; no upload/UI bottleneck proved, no duplicate caches/rings/scaling introduced |
| FPS-06 | Existing pose/input timeline retained; fixture-only focus isolation repaired in PR797 | Windows 28/28 pixel/canonical/particle checks pass; physical photon/VRR/device matrix UNVERIFIED |
| FPS-07 | Bounded 30-minute diagnostics, allocation/GC receipts frozen before analysis/cleanup | 30-minute receipt complete, stability gate FAIL (26 drops); external build overlap; unavailable platforms/matrices NOT_RUN |
| FPS-08 | Offline diagnostics report, CI contracts, default matrix and rollback receipts | Local checks pass; hosted CI not awaited per user instruction |

Implementation history: [FPS-00 #774](https://github.com/AntiNotAnti/Project-Prime/pull/774),
[FPS-01 #775](https://github.com/AntiNotAnti/Project-Prime/pull/775),
[FPS-02 #778](https://github.com/AntiNotAnti/Project-Prime/pull/778),
[FPS-07 #779](https://github.com/AntiNotAnti/Project-Prime/pull/779),
[FPS-03 #782](https://github.com/AntiNotAnti/Project-Prime/pull/782),
[raw-hitch follow-up #784](https://github.com/AntiNotAnti/Project-Prime/pull/784),
[historical fixture repair #785](https://github.com/AntiNotAnti/Project-Prime/pull/785),
and [FPS-06 fixture isolation #797](https://github.com/AntiNotAnti/Project-Prime/pull/797).
FPS-04/05 are evidence-gated no-rollout decisions, with no speculative renderer patch.

The fresh replay-format gate exposed two malformed historical fixtures after
protocol-60 match-rule widening. [PR785](https://github.com/AntiNotAnti/Project-Prime/pull/785)
repairs only those fixtures; the historical decoder and protocol are unchanged.
The fresh suite now passes 3,050 checks. See [local validation](validation.json)
and [frozen Windows evidence](windows.md).

The full Windows soak completed with 26 dropped simulation steps and an external
build overlap near the end. The stability gate fails; no uncontended 30-minute
acceptance or renderer gain is claimed. Every interval remains in the
[sustained report](windows-sustained.json).

The final [Windows native pose matrix](windows-functional.json) passes all 28
seven-Hunter × four-rate cells. All 21 cross-rate comparisons match 1,440
canonical/input steps exactly, with positive isolated native gun/charge pixels
and particle gates. The disk-full interrupted attempt remains preserved; only
passing cells with identical frozen runtime/asset hashes enter the final matrix.
This establishes bounded scheduled-draw correctness, with no physical latency,
OS focus, full multiplayer/replay event parity or FPS acceptance.

The final repository asset guard fails on twelve UI PNGs already tracked in the
refreshed baseline `521aff029`, outside this slice. See the preserved
[guard log](asset-guard.log) and [validation](validation.json). The FPS slice adds
compressed JSON/log receipts and text only, with every receipt below 2 MiB;
no image, extracted game asset or settings-path file is included. The existing
repository failure is reported rather than removed or reclassified as green.

## Interpretation

True 1% low is `1000 / mean(slowest ceil(N * 0.01) frame intervals in ms)`.
`onePercentLowEstimateFps` remains the reciprocal-p99 proxy. Neither is panel
scanout. CPU swap wall time is not GPU time; nested CPU/GPU passes must not be
summed. Comparisons never combine loaded MVIDs, profiled/unprofiled arms or
screenshot/pose readbacks. All valid raw short runs are retained, including bad
initial caps and the proven diagnostic artifact. Failed/stopped protocol attempts
are exclusions, not performance wins.

## Platform policy

| Platform | Renderer optimization defaults | Capture default | Device acceptance |
|---|---|---|---|
| Windows NVIDIA RTX 5080 | Existing capability-gated defaults retained | Off | Cap/stability gates FAIL in recorded runs; reported 239 Hz panel, no photon/VRR claim |
| Windows AMD / other NVIDIA | Existing capability-gated defaults retained | Off | NOT_RUN for these devices |
| macOS Apple Silicon | Existing capability-gated defaults retained | Off | Native functional smoke/compositor passed; uncontended timing NOT_RUN |
| macOS Intel | Existing capability-gated defaults retained | Off | NOT_RUN |
| Linux Mesa/NVIDIA | Existing capability-gated defaults retained | Off | Native device NOT_RUN; hosted build results not substituted |
| Android GLES | Existing capability-gated defaults retained | Unsupported desktop raw capture | Release build passed; physical 60/90/120 and thermal runs NOT_RUN |
| Unknown/invalid capabilities | Existing legacy fallback policy | Off | UNVERIFIED |

PALETTE_PREP stays off and PROCEDURAL_STREAM stays opt-in. No GL flag is promoted
from raw call-count reduction, a synthetic timing result or one device's FPS.

## Rollback and repeatability

Unset PROJECT_PRIME_GL_FRAME_CAPTURE/PROJECT_PRIME_GL_FRAME_CAPACITY to disable
capture. Revert the pacing PR if a source-transition regression is found; numeric
caps/Unlimited remain available under the original OpenTK policy. The late DLL
hashing path exists only as `-late-provenance-control`, is labelled diagnostic,
and cannot qualify ordinary timing. Revert individual PRs in reverse order for a
full code rollback; no settings/schema/asset/replay migration needs undoing.

Use fresh asset-only user data per run, freeze source plus loaded SHA/MVID and
asset tree hashes, exclude 240 warmup simulation ticks and partial profiler
windows, preserve chronological samples and every exit status. Build/copy only
between native timing runs. See the native tool README for 604/1804-second runs,
capacity/overflow rules and the separate capture/readback modes. Do not reclassify
missing physical/network/overlay/Studio/multi-device validation as PASS.
