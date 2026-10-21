# Parkour v2: wallstrafe

Classic remains the default. **Movement: Parkour** is an explicit normal practice
and multiplayer host choice. Protocol 78 selects Parkour v2; protocol 77 archives
retain v1. Native Hunter abilities, alternate forms and the existing owner-position
network contract keep their ownership. No new dependency or game asset is added.

Hold Forward and strafe while aiming into and along a safe static vertical wall.
Each native positive-penetration contact can add 0.5 world units/second along the
wall, capped at 1.5 times ordinary ground speed. Incoming overspeed and native
vertical velocity are preserved. A second face within native collision skin
vetoes the impulse, including a resting corner face. Ramps, ceilings, movers,
hazards, grounded crouch and slides refuse it. Airborne tuck remains eligible.
Leaving contact or releasing the required input ends the bonus immediately.
See the [Parkour guide](parkour-movement.md) for controls and comparisons.

Windows x64 qualification used private SDK 10.0.401 and exact committed producer
`1cea2db5590826bab08a81cb371a4ab6691f93ec`, integrated with main `9885b151c`.
[Selected evidence](evidence/parkour-wallstrafe-selection.json) pins all 6,447 source
files, the empty overlay, binary hashes, recordings and final readback. Fresh normal
Release nettest/custom builds passed with the existing CS0649 warning and no errors.

- All 11 focused gates passed. The actual keyboard/static-wall scene passed
  11,820 assertions: mirrored walls earned 75 impulses each; final travel exceeded
  identical-input Traversal by 0.153849 world units over 180 ticks. The original
  greater-than-0.1 travel threshold and greater-than-0.01 native tangent-velocity
  threshold both passed. All 144 two-wall corner samples had zero impulses;
  airborne tucked contact earned 20 impulses; all four legacy models had none.
- Tuck passed 33,971 scene assertions; Classic retained all 56 native golden
  trajectories. Profile, metadata, replay, timed-shot, protocol-58 and character
  policy gates passed. Custom defaults passed 36,962 pure contract checks and
  the actual-DLL party tool passed 138 checks.
- Clean 40-second UDP testing used an authority, two independent owner clients
  and one native authority bot. Owners passed 32,633 / 33,416 checks. Its actual
  recording passed 126,905 replay checks across 2,399 frames, including 1,068
  tucked snapshots, 10 captured world checkpoints and 97 seeks.
- A separate 40-second cohort verified 200 ms round trip, 20 ms jitter, 2% loss,
  2% reorder and 1% duplication on both clients. Owners passed 31,397 / 32,194
  checks. The recording passed 124,201 checks across 2,405 frames, including
  949 tucked snapshots, 11 checkpoints and 85 seeks. Both cohorts had zero hard
  shadow rejections and zero advisory WouldRefuse observations.
- Both preserved protocol-77/v1 actual recordings remained byte-identical and
  retained their exact gameplay/presentation digests and selected replay counts:
  clean 124,319 checks / 2,400 frames, faulted 126,177 / 2,404.
- The warmed helper allocated zero managed bytes across 10,000 measured calls.
  This is a helper allocation gate, not an overall performance threshold. The
  earlier bounded full-simulation diagnostics remain documented in the
  [tuck results](parkour-airborne-tuck-results.md).

Candidate 03 remains a preserved failure: its short contact window missed the
travel threshold, and a resting second face allowed one corner impulse. The
fix observes near resting faces only for refusal; activation still requires
native positive penetration. The longer clear lane keeps the original travel
threshold. Preparation-only candidates are not reported as executed passes.

These are CPU scene, UDP and passive replay checks. Multiplayer cohorts exercise
the accepted movement ruleset and stance; the separate authored scene proves
wall contact and acceleration. Passive replay consumes accepted facts instead
of running current wall forces. The authority retains the life-fenced owner-position
contract and does not independently resimulate remote trajectories. Stale-call
checks prove the frame fence, not a direct same-tick duplicate-call experiment.
No new GL, physical-device, rendered-smoothness or aggregate performance pass
is claimed. All test-owned runtime processes closed; source, binaries and archives
passed final fences. Raw logs, recordings and game assets remain private.

Latest-main integration producer `587f8972d984f782e83c63927d6875b742d86977`
includes the subsequent lobby-exit fix on main `b920a13d4`. A fresh Windows
nettest/custom build, custom defaults, wall helper/scene, architecture and
protocol-17 gates all passed. All 765 player/movement/network/nettest files were
byte-identical to the full producer. Both v1 and both v2 recordings retained
identical archive hashes, gameplay/presentation digests, counts, checkpoint
arrays and seek arrays. [Integration evidence](evidence/parkour-wallstrafe-integration.json)
pins the new source and binaries. It adds no new cohort, GL or performance claim.
The publication commit changes only documentation and selected evidence.
CI status is recorded in the PR; merge does not wait for CI or review, as requested.
Wallrun/kick is the next separate slice. Ledge scramble and grapple remain pending.
