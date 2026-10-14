# AGX-04: native allocation release evidence

**Release disposition: UNRESOLVED_P0. Program acceptance: NOT_ACCEPTED.** No runtime memory mitigation, program lease/cache, GC-policy change or optimization default has been accepted. The diagnostic gates and compiler attribution are useful evidence; they do not close persistent AGX allocation growth.

The current matched macOS campaign has only two cumulative increasing-load observations in one process: 60 loads after the first context and 300 after the second unshared context. Outstanding direct malloc-prefix AGX fragment groups after disposal grow from **44.830734 MiB to 224.644974 MiB**, an increase of **179.814240 MiB**. An independent fresh-process 60-load repetition ends at **44.717972 MiB**. The two matched 60-load controls define an observed maximum-minus-minimum noise envelope of **118,240 bytes / 0.112762 MiB**, before interpreting the later workload. The observed growth greatly exceeds that envelope. Four final idle observations and GLFW poll/terminate do not substitute for increasing-load windows.

`assess_agx_boundedness.py` revalidates immutable raw campaign/capture receipts before extracting cumulative process loads and named direct malloc-origin bytes. Noise controls must be independent repetitions at the explicit warm-up anchor, with matching source/binary/MVID, extracted-asset fixture, workload/graphics, profiling, OS/GPU/driver, context strategy and observation phase. It rejects duplicates, altered/missing/failed captures, unsupported or null origin measurements, non-increasing workload targets and mixed contracts. Fresh-process windows cannot establish within-process boundedness. RSS, RSS minus GC, GL-name deletion, mapped/reserved lengths and a single cleanup are not substitute metrics.

The gate requires at least **three successive increasing-load observations strictly after the declared warm-up target**. Both individual increases and the cumulative span including the warm-up anchor must stay within the predeclared observed-control envelope. This catches small continuing increments whose total exceeds control variation. PASS means scoped stabilization evidence only, not mathematical boundedness, a proven cause, a corrective patch, visual/platform acceptance or exact-head CI. Synthetic positive plateau fixtures exist only in tests and are marked `SYNTHETIC_TEST_ONLY`; the actual sanitized result receipt contains no invented plateau.

```sh
python3 tools/native-render-check/assess_agx_boundedness.py \
  --campaign /path/to/agx01-30-120 \
  --noise-campaign /path/to/agx01-30-120 \
  --noise-campaign /path/to/agx01-repeat30 \
  --control-loads 60 --warmup-loads 60 --minimum-windows 3 \
  --output /fresh/path/agx04-assessment.json
python3 -m unittest discover -s tools/native-render-check -p 'test_*.py'
```

The CLI exits nonzero for FAIL, UNVERIFIED or INVALID evidence. The actual dataset is VALID for data quality but fails the stabilization acceptance gate and lacks sufficient post-warm-up windows. The 60-load first-context observation calibrates warm-up/control noise; it is not counted as a post-warm-up increasing-load window. Controls are not enlarged using candidate values. The noise envelope is observed variation from two repetitions, not a statistical confidence interval or a guarantee about later workloads.

## Compiler and lifecycle attribution

All six GL-only controls at source `cdbaa4bc` independently validate. They run without assets/Scene/audio, check compile/link success, detach/delete shaders, unbind/delete programs, recognized-name retirement and context disposal. Drawing controls produce 128 nonblack pixels; repeated same-program draws change those pixels. Their direct AGX fragment observations after final disposal are:

| Control | Links / draws | MiB |
|---|---:|---:|
| Tiny compile-only, 120 iterations | 120 / 0 | 0.018051 |
| Tiny compile/draw/delete, 120 iterations | 120 / 120 | 3.353470 |
| Tiny compile once/rebind/draw, 120 draws | 1 / 120 | 0.024246 |
| Four prewarm variants, 120 iterations | 480 / 480 | 48.143539 |
| No shaders, two contexts | 0 / 0 | 0.018021 |
| Tiny in three fresh unshared contexts | 3 / 3 | 0.101822 |

This supports **reproducible driver-origin behavior associated with drawing newly linked programs despite complete observed application GL-name release**, not a definitive driver leak or retaining-owner proof. Compile-only versus draw controls indicate post-link materialization; reuse is a plausible reduction in churn, not an accepted production mitigation. These controls have different binary/source/workload identities and are not pooled into the full-scene noise envelope.

AGX-03 remains **NOT_ELIGIBLE** for a production program lease/cache. The tiny keys cover exact expanded sources, explicit attribute bindings/default link options and limited synthetic draw state. Full material/vertex-layout/texture/sampler/framebuffer/depth/stencil/blend/context-capability inputs remain unproven. Full-scene uniform/layout/cache generation reset, foreground/replay/Studio/launcher/side-scene ownership, bounded live-context leases, destroyed-context invalidation, partial initialization, fallback and failure injection under reuse still require evidence. True shared contexts are NOT_RUN. No global cache should be inferred from identical GLSL hashes.

The first expanded map ledger receipt at `cdbaa4bc` double-counted nested facade/owner hooks (64/36 events across two loads); it is retained as negative attribution evidence. Correction `1866fc2b` uses `facade-operation-v2`: the separate 120-frame map fixture reports **32 shader compilations / 18 program links**, all statuses successful, complete coverage, zero unexpected callbacks/overflow, 14 stage/source keys and 12 source hashes. Both loads retain exact RGBA/depth parity and zero recognized owned names/current program after unload. This scoped fixture does not certify unexecuted launcher, replay, GLES or external UI paths. See [counter correction](agx02-counter-correction.md).

## Hardware scope and operational disposition

Measured scope: **Apple M4 Pro; macOS 27.0 build 26A428; OpenGL 2.1 Metal - 91.7; OpenTK 4.9.4; .NET 10.0.12**. Fresh processes reset the observed fragment baseline to approximately 0.0185 MiB. A process restart is an observed operational mitigation only; it does not fix long-lived runtime allocation behavior or establish an acceptable session length.

600/900 same-context load windows, a second macOS/GPU/OS combination, true shared-context controls, reversed-order baseline/candidate acceptance, and Windows/Linux/GLES native lifecycle regression are **NOT_RUN**. No memory-runtime candidate is accepted. The unresolved P0 and restart-only operational option require an explicit maintainer release decision; successful diagnostic PR CI alone does not accept the entire engineering program.
