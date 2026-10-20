# Movement balance observations — PR-09 draft

These are measured input/collision trials, not competitive balance acceptance. The straight chains do not exercise an exhaustive strafe or maximum-speed envelope. No profile tuning changed.

[PR-09i](pr09i-results.md) adds one predeclared ordinary-input cap trial: all
28 mirrored Hunter/map pairs reach the regular cap at tick 104, and Native
replays identical raw commands. There are 5,390 regular air-cap witnesses,
with all 56 incoming-overspeed decisions retained separately. Observed native
impulse/exclusion state and overflow/recovery counters are zero. The report
retains the one-ULP speed boundary, all distributions, controller clamps and
censored traversal objectives. These measured chains support retaining the
unchanged v1 cap for the sampled rooms; no global or competitive claim follows.

The later [PR-09g fixed air-turn observations](pr09g-results.md) add all seven
Hunters on both maps with mirrored ordinary mouse/strafe input: 28 envelope pairs,
14 calibration pairs and 31,920 actual body samples. Every pair reaches its radial
objectives with zero collision overflow/recovery. The maximum observed solver
speed is 8.887898, below the unchanged 10.799561 cap; cap engagement in that fixed-turn experiment remains zero.
All distributions and per-pair outcomes are retained in
[the new evidence](evidence/pr09-air-turn-envelope.json). This adds a fixed turning
observation without claiming optimal-strafe or competitive calibration.

`--momentum-balance-scene <paths-only-config> [output.json]` runs seven Hunters on MP3 PROVING GROUND and MP1 SANCTORUS with the production Native/Momentum match rule. Each pair starts at the same seeded native spawn position and facing; all 56 macOS pair comparisons reported zero differences. Native movement, jump/gravity, abilities, life and authored collision remain active. The fixture never forces position, velocity, grounding, health or jump flags.

Fresh macOS Release execution at source `3de8887a8b04bd85db8b60ea14da9add15c1972a` plus the uncommitted PR-09 changes passed **215,756 assertions across 112 trials and 38,640 bounded per-tick samples**. Host: Apple M4 Pro, macOS 27.0.1, arm64, .NET 10; one offline player, fixed 60 Hz simulation, headless without render cadence or network impairment. The full build and subsequent tool-only build each passed with zero warnings/errors. [Sanitized per-Hunter/map results](evidence/pr09-balance-runup-draft.json) include helper/binary hashes, runtime configuration, conditions, percentiles and every objective result. Independent Python recomputation of the four reported percentiles, every body-coordinate radial displacement, total planar path distance and first-arrival objectives passed 39,536 checks over all 112 arms. Local raw body samples, manifest and log remain under `artifacts/momentum-pr09/balance-scene-02-macos/`.

The earlier fresh Windows x64 run remains separately preserved: **141,570 assertions, 84 trials and 25,200 samples**, without the subsequently added run-up scenario. [Its original results](evidence/pr09-balance-draft.json) retain the original helper/binary hashes and every observed outcome. Windows used the verified current source checkout plus PR-09 overlay; macOS ran its actual compiled C# engine. No cross-platform bit-perfect claim is made.

Each Hunter/map first performs a stationary native jump and landing. Native/Momentum Y-position bits, Y-velocity bits and jump/ground flags agree on all 180 sampled steps. Every actual Momentum horizontal-solver sample preserves native Y-velocity bits. Its legal-jump evidence comes from the same-frame native `nativeJumpAccepted` contact seam and verifies the exact Hunter native jump impulse before gravity/collision. Native moving jump counts are explicitly inferred from fresh UsedJump plus an upward jump-press sample, not claimed as directly instrumented accepted jumps.

## Running manual chains

W is held for ticks 1–48. Space pulses on tick 49 and every stationary Native first-landing period afterward; W is held on the other ticks. Both models receive exactly those fixed inputs for eight seconds. Each run acquires approximately 7.200 world units/second before its first jump request. Proving Ground is grounded on tick 48; Sanctorus is naturally airborne on tick 48 in both models, and its normal native jump allowance is preserved. The fixture does not manufacture ground contact.

Speed distributions below use every simulation step, including contact and blocked motion. Both models have p50/p95/p99 approximately **7.200 world units/second** in every running-chain Hunter/map cell. The full JSON retains p05, means, extrema, moving-air distributions and sample counts. Path rate is actual cumulative planar body travel divided by eight seconds; it is distinct from velocity and radial objective progress.

| Hunter | Room | Mean speed Native / Momentum | 10-unit time Native / Momentum | Path rate Native / Momentum | Actual Momentum legal jumps |
|---|---|---:|---:|---:|---:|
| Samus | Proving Ground | 6.401 / 5.716 | 1.533s / 1.417s | 5.416 / 5.749 | 7 |
| Kanden | Proving Ground | 6.401 / 5.716 | 1.533s / 1.417s | 5.416 / 5.749 | 7 |
| Spire | Proving Ground | 6.401 / 5.716 | 1.533s / 1.417s | 5.416 / 5.749 | 7 |
| Noxus | Proving Ground | 6.401 / 5.716 | 1.533s / 1.417s | 5.416 / 5.749 | 7 |
| Trace | Proving Ground | 6.401 / 5.716 | 1.533s / 1.417s | 5.416 / 5.749 | 7 |
| Sylux | Proving Ground | 6.401 / 5.716 | 1.533s / 1.417s | 5.416 / 5.749 | 7 |
| Weavel | Proving Ground | 6.401 / 5.716 | 1.533s / 1.417s | 5.416 / 5.749 | 7 |
| Samus | Sanctorus | 5.906 / 4.489 | 1.533s / 1.417s | 4.563 / 4.504 | 6 |
| Kanden | Sanctorus | 5.906 / 4.489 | 1.533s / 1.417s | 4.563 / 4.504 | 6 |
| Spire | Sanctorus | 5.906 / 4.489 | 1.533s / 1.417s | 4.563 / 4.504 | 6 |
| Noxus | Sanctorus | 5.906 / 4.489 | 1.533s / 1.417s | 4.563 / 4.504 | 6 |
| Trace | Sanctorus | 5.906 / 4.489 | 1.533s / 1.417s | 4.563 / 4.504 | 6 |
| Sylux | Sanctorus | 5.906 / 4.489 | 1.533s / 1.417s | 4.563 / 4.504 | 6 |
| Weavel | Sanctorus | 5.906 / 4.489 | 1.533s / 1.417s | 4.563 / 4.504 | 6 |

The predeclared objectives are the first tick reaching 3, 6 or 10 units of planar radial displacement from the actual spawn, not cumulative distance or a route-completion guarantee. For the run-up chains, both rooms and all Hunters reach 3 units at Native 0.483s / Momentum 0.433s, before the first jump. The 10-unit objective shown above includes the first jump. Later repeated jumps, contact and wall effects contribute to the full eight-second speed/path distributions.

## Cold starts and release

The original cold chain pulses Space immediately from rest, then uses the same native-calibrated period while holding W on non-jump ticks. It remains part of both captured runs. All seven Hunters have the following room-level results; every individual cell is retained in the JSON.

| Room | Speed p50 Native / Momentum | 3-unit time Native / Momentum | 10-unit time Native / Momentum |
|---|---:|---:|---:|
| Proving Ground | 7.200 / 1.081 | 0.500s / 2.800s | 1.567s / not reached in 8s |
| Sanctorus | 7.200 / 7.200 | 0.500s / 1.767s | 1.550s / 2.800s |

The Proving Ground cold-start result must not be presented as the running-speed envelope: beginning the jump from rest leaves the straight Momentum air input at approximately 1.081 units/second. The running case acquires its speed through actual prior movement.

The release control holds W for 80 ticks and releases all movement for ticks 81–240. All Hunters on both maps reach 3 units at Native 0.483s / Momentum 0.433s; Native does not reach 10 units in the four-second window, while Momentum reaches it at 1.483s. Momentum records 38 real neutral-ground braking steps per release trial. These are measured acceleration/coast outcomes, not a claim that a particular difference is competitively acceptable.

No speed-cap intervention occurred in these controls. The tested chains therefore supply distributions and traversal observations while leaving exhaustive non-impulse envelope selection, optimal strafe trials, combat balance, broader maps, physical devices and the remaining PR-09 release gates open. Timing from these functional runs is not performance evidence.
