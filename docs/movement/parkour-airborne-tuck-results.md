# Parkour v1: airborne tuck

Classic remains the default. Practice's Movement choice and multiplayer host
rules offer Parkour as an explicit fifth selection. Existing Momentum v1,
Quake III v4 and Traversal v5 remain separately selectable and unchanged.
Parkour v1 inherits the frozen Traversal v5 planar tuning, slides, ramp handling,
surf training surfaces and native explosive-jump rules.

To test: select Parkour, jump in an open lane, then hold Ctrl while airborne.
The low biped pulls its feet up without changing airborne origin or velocity.
Release Ctrl to restore the standing hull when it fits. A low roof can deny
entry or hold the accepted low shape until standing clearance becomes available.
Holding crouch through landing returns to normal crouch; further jumps remain
native legal jumps. Native alternate forms and abilities retain their own rules.
New stance input respects the existing movement exclusions during freezes,
Prime state, protected pad/launch movement and form transitions. A retained low
body grows only when its prospective standing shape fits.

All supported native Hunters share the same fixed biped dimensions. The tucked
hull translates the ordinary crouched bounds upward by the native eye-to-low-eye
difference, about 0.49 world units. It remains inside the standing hull and keeps
the crouched capsule diameter. Eye, muzzle, shot proof and historical collision
use the same accepted stance. Airborne tuck does not grant a second jump, change
gravity, create lateral speed or move an airborne origin.

On physical landing, the origin rises by that offset while the accepted world
capsule and eye remain at exactly the same place. The body becomes ordinary
crouch. A cumulative four-bit pivot count survives missing reports, allowing the
existing reported-position velocity estimator to exclude that coordinate change.
Old/duplicate reports retain their existing sequencing guard; discontinuous
multiple-pivot reports yield zero estimated velocity. The owner velocity is never
changed by the pivot. Render-only eye smoothing suppresses an artificial dip
through airborne untuck fade; the submitted body uses the raised tuck origin.

Protocol 77 assigns model 4 / modifier bit 19 to Parkour v1. Intent and player
snapshot widths stay unchanged: accepted tuck uses reserved state bits and the
pivot count uses reserved intent bits. Historical poses retain tucked state and
both bounds. Interpolation does not blend across shape changes. Checkpoint,
continuation and replay pose identity preserve the accepted stance. Pre-77
archives cannot acquire Parkour rules or interpret formerly reserved state bits
as tuck; protocol 77 has explicit profile-v1 attribution rather than a current
profile fallback. Custom-weapon archives retain their existing unsupported
boundary; extending live source certificates does not add custom replay support.

Networking retains the project's existing life-fenced owner-position contract.
The authority checks accepted body/form eligibility and world clearance before
freezing the stance used by source facts and broadcasts that accepted shape.
It does not independently resimulate a remote owner's entire trajectory. The
movement-validation shadow remains diagnostic and never becomes an anti-cheat
verdict. Tuck does not weaken the existing stream, life or match fences.

Modern passive replicas consume accepted form states rather than replaying raw
Morph requests ahead of those states. Protocols before 77 retain their existing
reconstruction behavior. Live recording startup also commits the newest cached
recording frontier before taking its first checkpoint. Cached presentation
baselines can precede newer match/intent records; they cannot rewind the capture
clock. Authority and source clocks retain their original values, and strict
checkpoint identity and time validation remains enabled.

Validation used exact committed producer `26fba95d628e3168a6a0520fbf5e179e022f039f`,
integrated with main `cc8babc0ddb0a4ab0492589f74b1415a49dc8131`, on Windows x64
with private SDK 10.0.401. Both normal Release builds and the custom-weapon
contract tool pass; the only compiler warning is the existing CS0649.
[Selected evidence](evidence/parkour-airborne-tuck-selection.json) pins the complete
6,392-file source manifest, binary hashes, archive hashes and final process fences.
The subsequent publication commit changes documentation/evidence only.

- All 20 focused gates pass, including 33,971 assertions across 15 actual tuck
  scenarios, all seven native Hunters, raised-roof clearance, release, repeated
  jumps, legacy/form isolation and real moving-platform landing/carry. The
  platform trace has 189 carried ticks, 188 moving-carry ticks and no adapter bypass.
- The native golden baseline passes all 56 trajectories. Ramp, crouch, movement
  rules, shadow validation, replay metadata/cache, source/certificate contracts
  and the latest-main offline-character/Scout geometry checks pass. Custom-weapon
  checks pass 35,717 pure assertions; this is not a new custom replay claim.
- A clean 40-second authority session with two independent scripted owner clients
  and one native AI bot passes 31,061 / 31,878 owner checks. Its actual recording
  passes 124,319 replay checks across 2,400 frames, including 951 accepted tucked
  snapshots, 11 world checkpoints, continuation and 89 seeks.
- A separate 40-second session verifies 200 ms latency, 20 ms jitter, 2% loss,
  2% reorder and 1% duplication on both clients. Owner checks pass 31,850 / 32,724;
  its recording passes 126,177 checks across 2,404 frames, including 999 tucked
  snapshots, 11 checkpoints and 114 seeks. Both cohorts have zero hard shadow
  rejections and zero diagnostic WouldRefuse observations; the shadow remains advisory.
- The two preserved protocol-77 recording regressions pass without modifying
  their archives. Actual protocol 61, 62, 68, 69, 71 and 74 archives also pass
  linear/checkpoint/seek checks with byte-identical source files. Protocol-58
  compatibility passes 975 checks. No arbitrary-archive compatibility claim follows.

A bounded eight-native-bot benchmark ran one verification pair and a separate
60-simulated-second timing pair, each with 1,200 warmup and 3,600 measured steps.
Timing p50/p95/p99 is 0.1754/0.2804/0.3600 ms for Classic and
0.2055/0.3179/0.4186 ms for Parkour. Current-thread simulation allocations are
3,462.66 versus 3,502.39 bytes per step. Workloads differ: alive-player steps are
24,276 versus 25,020, shots 687 versus 1,099 and damage events 204 versus 282.
These are completed diagnostics, not a matched regression threshold, speedup,
worst-case tuck workload or sustained hardware-budget qualification. The earlier
accepted performance tradeoff remains documented; no timing noise rerun was used.

Earlier failures remain separate in the evidence history. Actual replay exposed
an unaccepted raw Morph transition; the preserved archive now verifies the
canonical-form fix. Faulted startup exposed recording frame 95 behind accepted
match/input frame 97; a private diagnostic captured that exact checkpoint, and
constructed plus fresh faulted-session tests now verify the frontier repair.
Malformed checkpoints are still rejected. No private checkpoint, game assets,
recordings or raw logs are published in this source change.

Static source-manifest tests, the protocol registry and the full publication
closure asset guard pass. Final Windows source/binary/archive fences pass with
zero owned runtime processes and no source, asset or evidence deletion.
Hosted CI is not awaited, per the operator's merge instruction. Mac builds,
simulation and UI automation were excluded. New GL raster/device presentation,
physical keyboard/controller/touch playtesting and a complete transport/platform
matrix remain unverified; CPU fixtures do not establish visual or physical-input
acceptance. The remaining ordered slices are wallstrafe, wallrun/kick, short
ledge scramble, and optional grapple traversal.

Latest-main integration subsequently merged main
`ee9da10f912f9175a092ed5d12d2a2e80743a263` at producer
`ce3d36e24a861d9639abbd905b8bf2f7a7eae1b8` after the upstream corpse-rendering
and menu changes. All 758 movement/network/player/nettest files are byte-identical
to the full functional producer. [Integration evidence](evidence/parkour-airborne-tuck-integration.json)
records fresh normal Windows builds with the new upstream dependency graph,
custom defaults, tuck/compatibility/bootstrap gates, and both retained recordings
replayed with exactly unchanged digests and key counts. Source, dependency and
archive fences pass with zero owned runtime processes. This targeted integration
did not rerun cohorts, historical batches, benchmarks or GL lifecycle checks;
those retain their original producer attribution above.
