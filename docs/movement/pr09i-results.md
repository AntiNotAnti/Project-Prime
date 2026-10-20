# PR-09i — ordinary-input cap envelope

One predeclared Windows capture passes all 28 prescribed mirrored pairs across
all seven Hunters and both rooms: **358,935 assertions and 31,920 body samples**,
including 14 stationary calibration pairs. Every Momentum pair first reaches
the regular air cap at tick 104. Classic replays the exact recorded raw commands.
Production movement, the profile, collision and the Classic default are unchanged.

## Input and scope

The optional route is
`--momentum-balance-adaptive-scene <paths-only-config> <fresh-json>`.
The existing four-scenario balance route and PR-09g fixed-turn implementation
retain their original bodies. Each new arm runs 480 fixed-60-Hz ticks with the
unchanged seeded native spawn and authored collision. W runs for ticks 1–48;
subsequent mirrored A/D input uses one-tick Space pulses at the same stationary
Native landing period. No landing-adaptive jump buffer is introduced.

A bounded input controller observes actual planar velocity and aims the strafe
wish perpendicular to it. In the tested air-acceleration regime, perpendicular
wish maximizes the ideal squared-speed gain; it predicts approximately 56
eligible air steps from run-up speed to the unchanged 1.5-times-ground cap.
The controller accounts for ordinary default dynamic-camera easing with a
small read-only yaw predictor, 20 bounded inverse iterations, a ±256-pixel
command limit and 1/256-pixel quantization. It writes ordinary keyboard and raw
cumulative mouse samples. It never writes body position, velocity, facing,
health, life, grounding, abilities or camera preferences, and never invokes
private aim methods.

Momentum generates the command trace first; a fresh Native arm replays it
exactly. This is matched offered input, not independently optimized Native
play. All cases use the same law and fixed seed; no controller/profile/geometry
tuning, favorable case selection or capture reruns follow observed outcomes.
Pre/post native impulse and exclusion state is retained. Any external-path or
collision-counter observation requires review rather than an unqualified pass.

## Measured results

- All 28 paired raw-command hashes agree; actual spawn position/facing
  differences are zero. Every envelope arm completes all 480 samples.
- There are 5,390 regular incoming air-cap witnesses and 5,446 actual
  speed-limited steps. The other 56 decisions carry the existing incoming
  overspeed exemption and remain explicitly retained.
- The exact float32 profile cap is 10.799560546875. Reported norms for those
  56 incoming decisions reconstruct to that cap at float32 precision. Maximum
  reported body/solver speed is 10.799561500549316, one float32 ULP above it;
  the uncapped candidate reaches 10.85353. These are observed numerical
  boundaries, not proof of their cause or a globally exact speed ceiling.
- All 15,960 horizontal seam checks preserve native Y bits. Every Momentum
  envelope arm has at least six actual native-accepted jumps. Native never
  invokes the Momentum solver.
- Actual collision overflow/recovery counters, observed external-path state
  and Momentum bypass ticks are zero. This is scoped collision evidence;
  zero counters alone do not establish universal non-penetration.
- The bounded controller clamps 35 commands. Its original float error metric
  reports zero; independent recomputation from serialized predicted/actual
  angles finds at most 0.00004 degrees, below the unchanged 0.02-degree check.
- Whole-trial median speed ranges from 6.880286 to 10.79956 for Momentum and
  approximately 7.199707 for Native. All 28 pairs reach radial distance 3;
  distance 6 is reached by 7 Native / 28 Momentum arms, and distance 10 by
  7 / 7. Remaining objectives are censored at eight seconds. Turning paths
  are not straight-line speedrun or competitive-balance comparisons.

The measured chains support retaining the existing conservative v1 cap in
these rooms. The result extends PR-09g's fixed-turn observations with actual
ordinary-input cap engagement; it does not prove optimal strafe play, every
map, maximum long-run speed or competitive balance.

## Provenance and remaining checks

The actual capture uses production source
`9c13d4409517410c995282645873c6a9a0c42112`, verified normal Core
`e9a1c554bef375b19c206e93c2acd9035e4534792e01ec797e2fd9c77133bea7`,
and the three recorded test-file overlays. The isolated tool-only build passes
with zero warnings/errors and leaves Core unchanged. Setup attempt 01 stops
before checkout/build/capture because the dependency inventory exceeds its
1 GiB copy budget; setup 02 declares a 2 GiB bound with the same 8 GiB reserve.
Both receipts remain preserved. There is exactly one actual capture.

[Numeric evidence](evidence/pr09-cap-envelope.json) retains every Hunter/map/
direction distribution, objective outcome, input hash, counter, source lease,
setup history and bound. An independent 27,612-check review recomputes command
equality, quantiles, objective arrivals, cap events and yaw errors from the
saved report. Raw bounded traces remain ignored. The four input/movement
dependency hashes remain unchanged at the later publication base; executed
binaries retain their original source label. No physical-device, render-Hz,
network or performance result is inferred from this headless functional run.
The separately accepted performance tradeoff is documented in [PR-09h](pr09h-results.md).
Hosted CI remains a separate verification step and is not waited on for merge.

## Current-main integration

After concurrent NCX04 and the receipt-ordering repair merge, a separate fresh
normal Core/nettest build at `e5009324427eefcd826c373a3e51a1dd77c44dd9` passes
with zero warnings/errors. Lifecycle, claim receipts, combat fast receipts and
the actual paid-receipt scene all pass. Normal execution leaves the new fast
path Off; the focused fixture exercises its declared Paid mode internally.

The original normal binaries/PDB/runtime/dependency files are preserved before
applying these three test files. The optional adaptive route then compiles
against the new Core with zero warnings/errors; all five current fast/Paid
family commands and the new adaptive command remain in help. Core hash
`603d5592520a5d0a149525a14c539c7f67d6d0d7671408ab15f3cbaaf8aa6cbd`
is unchanged by that tool-only build. The controller/helper and four
movement/input dependency hashes remain identical to the actual capture.
This is a fresh integration compile plus focused receipt checks, not a
relabelled cap replay or another timing experiment.
[Current integration evidence](evidence/pr09-current-paid-compatibility.json)
records both separate tool hashes, preservation manifests and all four exits.
