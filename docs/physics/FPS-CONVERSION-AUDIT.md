# FPS conversion audit — implementation status

The full [conversion plan](FPS-CONVERSION-PLAN.md) is **unfinished**. Historical replay restoration and Android restore are fixed; the native recorder and all 17 biped fixtures now run. An offline native-cadence kernel experiment matches movement state, measured stages and contacts across the entire corpus. A second experiment now runs that operator inside the actual 60 Hz scene, with identical boundary results for commands on either half. The default 60 Hz gameplay path remains unchanged and still fails native parity.

Runtime acceptance is currently **macOS only**, per the user’s instruction on September 29, 2026. Windows, Linux and Android runtime checks are deferred; no cross-platform pass is claimed.

## Current evidence

| Gate | Result |
|---|---|
| Authentic v0.1.34 checkpoint restoration | PASS; immutable historical fields, value layouts and 97 type IDs; all eight players and scores checked, then ten continuation frames |
| Replay control / format | PASS; format suite has 2,738 checks |
| Android Release restore | PASS; host publish-RID inference disabled in Android head |
| Android compilation/runtime | Unverified; Android SDK missing and installed Java 27 is incompatible with workload version detection |
| Original-ROM capture | All 17 fixtures complete with game-counter, health, input, ROM/state/profile/script/trace provenance checks |
| Prime capture and consumed input | All 17 fixtures complete |
| Geometry/impulse coverage | All eight named geometry/impulse fixtures pass independent checks in both implementations |
| Offline 30 Hz kernel, movement and observed stages/contacts | All 17 agree within unchanged strict tolerances |
| Offline 30 Hz kernel, complete comparison including heading | 15 PASS; idle and knockback fail idle-sway heading only |
| Native kernel in the actual 60 Hz scene | 17/17 movement/stage/contact matches for both input phases; 15/17 complete; intermediate authority held |
| Native cadence checkpoint restoration | PASS from both halves; buffered edge consumed; 20 continuation frames; scene isolation; v3/v0.1.34 retained |
| Intermediate presentation candidate | 17,800 body/camera render samples checked; draw-only midpoint/endpoint; one-frame translation delay; visual playtest pending |
| Default 60 Hz native parity | FAIL; no live kernel conversion has been enabled |
| Six-profile synthetic client regression | Native release, jump, wall and pad fixtures pass with corrected spawn setup and endpoint draining; native authority preserves reported velocity through its animation/contact step |
| Power Beam timing and projectiles | Single, repeat and charge fixtures pass strict shot/charge, movement, projectile flight/contact/expiry comparisons; moving freeze-release exposed coupled camera/fire ordering, now under correction |
| Alt forms | Last; remain unverified / exit 2 without complete evidence |

The reproducible follow-up evidence is in ignored `artifacts/fps-audit/followup/native-kernel-final/` and `baseline-verified/`. Two independent complete runs produced byte-identical native captures for all 17 fixtures. Each fixture has native/Prime captures, comparison output, logs, manifests and geometry coverage where applicable. `summary.json` separates capture completion, coverage and parity. The corpus command correctly returns failure for any unmatched fixture, including the two heading-only mismatches in the experiment.

The first strict full-stage experiment is also retained in `paired-kernel8/`. Both idle and knockback first fail heading at boundary 68; maximum heading error is approximately 0.489696 degrees. Their position, velocity, state, timer and measured stages/contacts match. The ROM's ordinary idle sway is earlier than Prime's intentional delayed-sway setting. That policy difference has not been hidden by relaxing heading tolerance.

## What changed

- **Replay compatibility:** the recorded contract selects an immutable v0.1.34 decoder, including nested value layouts and historical type ordering. Substituting an old producer version into the current schema is no longer used. Unknown contracts fail closed. An actual old-source fixture was produced twice from tag `v0.1.34` (`f0e01e09`), restored against its producer's oracle, and continued. See [fixture generator](../../tools/replay-v134/README.md).
- **Android restore:** SDK 10.0.401 inferred host RID `osx-arm64` for trimmed Release publishing and appended it to Android ABIs. `UseDefaultPublishRuntimeIdentifier=false` prevents the nonexistent desktop Mono request. NcsfPlay was not the source. No SDK downgrade or nonexistent package reference was added.
- **Native capture:** the pinned melonPrimeDS interpreter runs the user's original AMHE1 ROM. Reproducible menus create Data Shrine and Combat Hall starting states. Native instruction probes observe acceleration, damping, gravity, integration, collision, and ordered contact planes/depth/pushout. Probe noninterference was verified. ROM and states remain private local artifacts. See [recorder](../../tools/native-recorder/README.md).
- **Matched setup:** both runners start with the same representable position and movement state. Native setup now resets its saved input-idle timer and initial grounded flags to match Prime Spawn. Previously retained idle time rotated the native movement basis during settling. Earlier references in `paired-reproduced` are superseded, not deleted.
- **Scenarios:** all 17 are executable. Six static geometry fixtures have verified placements, the pad uses Combat Hall's real trigger, and knockback prescribes an initial velocity plus six native ticks of acceleration after settling. The latter validates impulse response, not weapon-hit generation. Both runners reject contaminated captures. See [corpus and runner](../../tools/fps-scenarios/README.md).
- **Kernel experiment:** `-fpsnativekernel` reconstructs the complete 30 Hz biped operation with the original order, signed truncation versus rounded fixed-point arithmetic, speed-cap normalization, contact projection, edge normalization and slope slowdown. It is selected explicitly by diagnostic scenario/network workers. It does not enable a gameplay preference or change network cadence; native modes are persisted by the v4 world-capsule appendix. See [movement findings](NATIVE-MOVEMENT-PARITY.md).
- **Stage comparison:** trace stages declare their cadence. Whole native stages can be compared with the 30 Hz experiment; they remain UNAVAILABLE against individual live 60 Hz substeps. Missing measurements are never fabricated zeroes. Stage/contact disagreement fails the comparison even if final position matches.

Normal gameplay trajectories for the original nine fixtures were compared with the pre-experiment captures and remain unchanged. Native diagnostic arithmetic is not enabled by headless mode alone, so dedicated servers and replay tools retain their ordinary simulation.

## Reproduce

Use .NET 10 (`~/.dotnet/dotnet` on this machine) and absolute scenario/data/output paths. Startup can change the working directory.

```sh
dotnet run --project tools/fps-check -c Release
dotnet run --project tools/fps-check -c Release -- -fpsconvertaudit /absolute/checkout /absolute/report
dotnet run --project src/MphRead -c Release -- \
  -fpsscenario /absolute/scenario.json -fpsoutput /absolute/new.jsonl -fpsnativekernel
dotnet run --project src/MphRead -c Release -- -fpsphysicscheck \
  -fpsreference /absolute/native-reference.jsonl -fpsactual /absolute/prime.jsonl -fpsoutput /absolute/comparison
```

The [corpus runner](../../tools/fps-scenarios/run_corpus.py) captures and compares every fixture in one command; its README documents both room states and the normal 60 Hz baseline mode. Output directories must be new. Position/velocity/stage tolerance is 0.0001 and heading tolerance is 0.001 degrees. Required state must be finite; frame/player keys, hunter/form, schema and stage cadence are validated. Exit 0 is agreement, 1 is mismatch/error, and 2 is unavailable evidence. Passing supplied traces does not replace provenance or coverage review.

## Remaining critical path

1. Validate presentation latency and moving-world integration. The native operator runs on even boundaries, with tested input phases, a numerically verified draw-only midpoint/endpoint path and checkpointed render history. Intermediate authority is held; presentation adds one 60 Hz frame of translation delay. Moving platforms/player contacts and dynamic teleports/impulses still need native coverage. Derived intermediate states must not feed back into native authority accidentally. The original plan explicitly permits this architecture.
2. Complete live correction/prediction ownership. Scene policy and pending input now restore through a v4 world-capsule appendix, with both-half continuation and legacy-policy tests passing. This detached replay result does not certify local-owner network rollback.
3. Resolve the idle-sway policy/heading difference in the appropriate camera cohort, and extend native coverage beyond the current static Samus corpus to moving-world interactions.
4. After the first accepted live correction, rerun synthetic clients, replay, combat fairness, render-rate tests, and macOS runtime determinism. The other runtime platforms are deferred by the user; cross-compilation would not establish their determinism.
5. Apply the same audit and reference method to combat, projectiles/entities, AI, camera/HUD, then all seven alt forms together. The original 1,108-site inventory remains a baseline, not 1,108 resolved findings.

The latest cadence evidence is in `artifacts/fps-audit/followup/cadence60-verified/`. Every fixture includes both input phases and an every-frame cadence companion. Boundary traces are identical between phases. Both the original native-30 experiment and the normal-60 baseline remain exactly unchanged across all 17 full traces. The follow-on `cadence-state-corpus/` checks preserve all four modes exactly after moving cadence state into scenes/players. Native-mode capsules now include the explicit v4 appendix; ordinary worlds keep v3. Networking policy remains unchanged. The later `cadence-presentation-corpus/` preserves all four movement traces while validating 17,800 first-person render samples, body/camera endpoints and draw noninterference. Frame-timing checks and restored presentation-hash continuation pass. No live-network or visual-playtest acceptance is claimed.

## Latest verification

Desktop/nettest Release builds pass. The arithmetic/scanner/comparator/input suite passes **3,045 assertions**, including native rounding observations, full-stage cadence separation, malformed impulses and integer square-root bounds. All 17 native and Prime captures complete, and all eight geometry/impulse coverage checks pass. Replay control, 2,738 replay-format checks and authentic v0.1.34 restoration were rerun successfully after the state refactor, alongside both-phase native-cadence checkpoint continuation and malformed-state checks. Android restore remains fixed; no post-correction cross-platform or live-network acceptance is claimed. Protocol remains 33 and default simulation/network clocks remain 60 Hz.


## Network and combat continuation

Corrected network setup gives the authority responsibility for the scenario life/spawn and waits for replication before client capture. Earlier default-network evidence only compared latency profiles against one another and missed a rejected client spawn; it did not establish the requested initial coordinates. Every native network profile now also requires the original-ROM boundary comparison. A holds the measured endpoint while packets drain, allowing a meaningful convergence check for repeatedly bouncing pads. Protocol 33 derives puppet velocity from reported positions, so this is compared with the observer independently from the owner’s native integration velocity.

The new gate found and fixed native-mode carry-over of the airborne timer on respawn, and a second gravity/traction update contaminating accepted remote velocity. Evidence is retained in `native-client-final-biped-release`, `native-client-final-flat-wall`, `native-client-speed-fixed-jump-forward`, and `native-client-speed-fixed-jump-pad`; all six profiles pass in each. Corrected default release transport also passes in `default-client-final-release`. These tests cover owner-authored movement acceptance/replication, not server resimulation or combat fairness.

The recorder and runner now share fire input and measure actual firing, charge, freeze and projectile timelines. `projectile-corpus-final` passes the initial three Power Beam cases, including static-wall impact and expiry, with 1,635 draw-pose samples. Projectile correction remains scoped to the diagnostic native-cadence Power Beam. The moving freeze-release fixture now also passes, with buffered fire edges, native bob timing and camera-order corrections. All four fixtures pass both input phases in `native-fire-final-phase0` and `native-fire-final-phase1`; see [combat evidence](NATIVE-COMBAT-PARITY.md). Alt forms remain gated.
