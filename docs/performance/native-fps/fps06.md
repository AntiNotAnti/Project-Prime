# FPS-06: native pose fixture focus isolation

The Windows V2 campaign passed all four Samus schedules, then Kanden60 failed
its positive charge-pixel gate. Main-player consumed controls became empty at
tick245 despite held scripted movement/fire, while the second player kept firing
and had a charge effect at420. The main player never charged. The original OS
event causing suppression was not captured, so the failure alone cannot identify
its trigger or establish a rendering defect.

The fixture already uses frozen synthetic keyboard/mouse snapshots and explicitly
excludes OS focus acceptance. Its native focus callback nevertheless forwarded
to RenderWindow's production hardware focus latch/context reset. A loss while
scripted inputs stayed held could require neutral hardware input forever, even
though the next fixture frame restored logical focus. This mixes two input owners.

Pose mode now records OS callbacks separately without forwarding those hardware
side effects. Every non-pose native timing, resource and lifecycle mode retains
its existing base callback. Ordinary client code, hardware focus safety, simulation,
networking, pose interpolation and renderer state are unchanged. Charge input
consumption fails early after the phase-entry boundary, including release460.
PowerBeam, tick420, positive isolated native pixels, exact owner restoration,
canonical hashes and all existing beam/particle gates remain intact.

The harness-only `-pose-focus-control none|isolated|legacy` requires pose mode.
At tick245, isolated routes labelled loss/gain callbacks through the observer;
legacy deliberately invokes the original base side effects and cannot qualify.
It is a negative diagnostic control, never a player setting or timing candidate.

## Same-binary Windows proof

[Raw receipts](evidence/focus-controls/index.json) bind source
`9c1aabc1b59fba5b34451304093503f28dba5c24`, client MVID
`dc8876fa-2eea-4f83-81f7-1b9575cb0e8e`, client SHA
`7ac9893a145e4b1127ea2a044aa0c9aafa73de43a2ff5ae888e8b412aad7df98`
and harness SHA
`ec9e55f7cd9634b396ed0714f88f40e287b9ae3c598ebe3c587db060efca10e8`.
All runs use the interactive Windows NVIDIA GL desktop and unchanged extracted
assets. Original failed evidence remains in the final program receipts.

Legacy60 exits1 at the expected consumption gate, with361 recorded steps and
qualification disabled. Isolated60 and none60 both exit0, pass every native
pixel/beam/particle/owner gate, and match all **1,440 canonical steps exactly**,
with identical input timeline SHA. None60/144 passes the existing cross-rate
analyzer's exact canonical and pixel checks. The analyzer's historical status
name contains MAC; actual receipt vendor/context/MVID identify these Windows runs.
See the [comparison](focus-control-summary.json). No panel/OS-focus/physical input
latency or performance acceptance is claimed. The full seven-Hunter matrix is
reported independently by the final program decision.

Release build passes with zero warnings/errors;115 native-tooling tests pass
(four existing skips), including source-linked parser/schedule cases and CLI
rejection outside pose mode. No pixel threshold or canonical tolerance changed.

Rollback: revert this harness-only PR. No game settings, assets, replay schema,
protocol or player-input behavior need migration or rollback.
