# Parkour v3: wallrun and wall kick

Classic remains the default. Select **Movement: Parkour** in normal practice
Match Rules or multiplayer host Match Rules. The host freezes the accepted
ruleset at match start. Protocol 79 selects v3; protocol 78 recordings retain
v2 and protocol 77 recordings retain v1. Other movement profiles retain their
existing rules. No dependency or game asset is added by this slice.

An upright eligible native biped earns a wallrun from actual safe static vertical
wall contact while airborne, holding Forward and strafe along the wall above
half ordinary ground speed. Current full-body probes and standing clearance
validate the retained contact before reducing gravity. Reduced gravity is 35%
of native gravity for at most 45 fixed 60 Hz ticks per airborne period. It never
resets or adds upward velocity. Release, crouch/tuck, loss of contact, a corner,
geometry ambiguity or native ability ownership ends the run. Remaining budget
survives cancellation; only new life or real safe static nonseparating standing
support refills it. Grounded grace, teleport, form changes and stance cleanup
cannot mint another budget.

Release Jump and press again during a valid run to kick. The kick supplies at
least 8 world units/second outward and 9 upward, preserving stronger incoming
components and wall tangent velocity. It spends the entire remaining budget.
Holding Jump never automatically kicks. The native jump/SpaceJump eligibility
bits remain unchanged; native pads, teleports and external acceleration retain
priority. Gravity reduction applies to a local integration value: the persistent
native gravity field and scripted override tick retain native ownership.
See the [Parkour guide](parkour-movement.md).

Windows qualification uses exact committed producer
`6eec103001a5e44e0efb850ea0216eedd9fec4bd`, integrated with main
`d0867e7f03a7d21e9643b1f9d8649435486bc478` after wallstrafe PR #930. The
publication updates add documentation, selected evidence, asset-free CI wiring
and acceptance-fixture compatibility;
runtime code remains byte-identical to that producer. Runtime
results below are completed before publication.

The pure helper passed 1,102 assertions, including mirrored/rotated normals,
stronger incoming impulses, invalid/nonfinite input refusal and zero managed
allocation over 20,000 warmed measured calls. The actual keyboard/native-wall
scene passed 6,149 assertions across eight cases: 45 earned run ticks, gravity
reduction without changing the stored native field, release/crouch/corner loss,
held versus fresh Jump, kick velocity floors, real landing refill and four
legacy-model isolation controls. Its native Teleport API lifecycle case spends
three ticks before invoking Teleport once at the unchanged current origin;
42 remaining ticks are preserved and the active run cancels. This is an API
regression check, not a teleport-map-trigger qualification.

[Selected evidence](evidence/parkour-wallrun-selection.json) pins all 6,457
committed source files, the empty overlay, binary/dependency hashes, raw evidence
hashes and final fences. Fresh normal Windows Release nettest/custom builds
passed using SDK 10.0.401, with the existing CS0649 warning and no errors.

- All 16 focused gates and the supplemental 99-assertion Parkour stance gate
  passed. Inherited wallstrafe passed 11,820 scene assertions; tuck passed
  33,971; Classic retained all 56 native golden trajectories. Architecture,
  protocol-17, stance-network, historical metadata/replay and character-policy
  checks passed. Custom defaults passed 36,962 pure contract checks; the compiled
  party tool passed 138 actual-DLL checks.
- Clean 40-second UDP testing used an authority, two independent owners and one
  native authority bot. Owners passed 32,442 / 33,351 checks. Its recording passed
  126,721 checks across 2,399 frames, including 1,018 tucked snapshots, 10 captured
  world checkpoints and 87 seeks.
- A separate 40-second cohort verified 200 ms round trip, 20 ms jitter, 2% loss,
  2% reorder and 1% duplication on both owners. They passed 31,428 / 32,214 checks.
  Its recording passed 124,522 checks across 2,405 frames, including 1,039 tucked
  snapshots, 11 checkpoints and 93 seeks. Both cohorts had zero hard shadow
  rejections and zero advisory WouldRefuse observations; the shadow remains
  diagnostic rather than an anti-cheat verdict.
- Both retained protocol-77/v1 recordings and both protocol-78/v2 recordings
  remained byte-identical. All four kept their exact gameplay/presentation
  digests, selected counts, checkpoint arrays and seek arrays. New recordings
  explicitly report protocol 79 and profile v3.
- One bounded eight-native-AI Classic/Parkour verification pair and one separate
  timing pair completed, each with 1,200 warmup and 3,600 measured fixed-60-Hz
  simulation steps. No timing-noise reruns or comparative acceptance threshold
  were applied.

| Timing run | p50 / p95 / p99 (ms per step) | Worst (ms) | Current-thread bytes per step |
| --- | --- | --- | --- |
| Classic | 0.1740 / 0.2651 / 0.3362 | 3.3194 | 3,462.66 |
| Parkour v3 | 0.1748 / 0.2564 / 0.3663 | 12.0260 | 3,498.91 |

These workloads differ: Classic has 24,276 alive-player steps, 687 shots and 204
damage events; Parkour has 23,171, 508 and 210 respectively. Timings are completed
diagnostics, not an equal-work comparison, an overall speedup or a new pass
against the previously deferred performance regression target. The native-bot
workload is not a worst-case sustained wall-probe workload. The earlier accepted
performance tradeoff remains documented; optimization can continue separately.

All test-owned runtime processes closed. Source, production binaries, custom/party
dependencies and all retained archives passed final readback fences. The public
selection contains no private paths, nicknames, recordings or imported assets.
The CI workflow now includes asset-free Parkour stance, compatibility, wallstrafe
and wallkick gates, with Parkour source/test path filters. Its YAML and command
wiring were checked; all four commands passed on this Windows producer.

These are CPU physics, UDP and accepted-fact replay checks. The authored scene
proves wallrun forces; multiplayer sessions prove the selected v3 ruleset,
owner/authority/remote stance and recording paths. The existing life-fenced
owner-position contract remains in effect; the authority does not independently
resimulate remote trajectories. Passive replay restores accepted positions and
shapes with physics/input disabled, preserving historical profile attribution.
The stable passive checkpoint schema is unchanged and is not an active-controller
save format; a future active save must persist budget and controller clocks.

No new GL, physical-device, rendered-smoothness or worst-case wall-probe performance
acceptance is claimed. Raw assets, recordings and logs remain private. CI status
is recorded in the PR; merging does not wait for CI or review, as requested.
A post-merge snapshot of PR #930 exposed stale live-profile and four-model menu
assertions plus a content-free adapter stub missing the upstream private-weapon
type. These fixtures are updated without changing production movement. Targeted
Windows revalidation of the affected fixtures is pending before publication.
Ledge scramble and grapple remain pending. Work stops after this PR at the user's
requested break.
