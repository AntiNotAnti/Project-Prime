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

Validation results and the selected source/binary evidence will be recorded here
once the Windows candidate gates finish. Mac builds and simulation are excluded
for this task. The remaining Parkour techniques are separate ordered slices:
wallstrafe, wallrun/kick, short ledge scramble, and optional grapple traversal.
