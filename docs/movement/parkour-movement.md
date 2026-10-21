# Parkour movement

Classic remains the default. Choose **Movement: Parkour** in normal practice
Match Rules or multiplayer host Match Rules to enable the separate profile.
Campaign and the other movement models keep their existing rules. Parkour
requires native Hunter tuning; Balanced/Enhanced and alternate character
variants keep their existing exclusions. A host freezes the choice at match start.

Parkour inherits Traversal v5 planar tuning, ramp handling, crouch/power sliding,
tagged training-map surfing, held landing hops and native explosive-jump rules.
Native alternate forms and abilities retain their ownership.

**Airborne tuck:** jump, then hold Ctrl. Your feet pull into the short capsule
without an added jump impulse or airborne-origin translation. Release Ctrl to
stand when the taller hull fits. A physical landing returns the same world hull
to ordinary crouch. See [tuck validation](parkour-airborne-tuck-results.md).

**Wallstrafe:** approach a safe static vertical wall while holding Forward and
Left/Right strafe. Aim so the combined movement pushes into the wall and along
it. A straight head-on push gives no bonus. Each actual contact tick can add
0.5 world units/second along the wall, up to 1.5 times ordinary Parkour ground
speed; incoming overspeed is preserved. The boost leaves native vertical
velocity untouched. It works on foot or airborne, including an eligible tuck.
Grounded crouching, slides, ramps, ceilings, corners, movers and damaging faces
refuse the bonus. Leaving the wall ends it immediately; there is no grace boost.

Use a long flat training-wall lane to compare the same Forward+strafe input
with Traversal. Parkour should build more tangent speed while touching the wall.
Try the mirrored wall, then release strafe, turn away, push head-on and reach a
corner: each should stop the bonus. Keep the movement HUD visible to compare
speed without relying solely on camera motion.

**Wallrun and wall kick:** jump into a safe vertical wall while holding Forward
and strafe along it. Above half ordinary ground speed along the wall, an upright
biped can use up to 45 fixed ticks of reduced gravity per airborne period.
Gravity is 35% of native gravity; the run never resets upward velocity. Release
Forward/strafe, crouch, leave the face, reach a corner, or enter a native ability
and the run ends. Remaining time is preserved until a real safe static landing;
changing form or releasing input cannot refill it.

While running, release Jump and press it again to kick away from the wall. A
fresh kick supplies at least 8 units/second outward and 9 upward, preserving
larger incoming impulses and tangent speed. It spends the entire remaining
wallrun budget. Holding the original Jump press never kicks automatically.
Native jump/SpaceJump flags remain owned by their existing controller. Native
pads, teleports and external acceleration retain priority over a pending kick.

**Short ledge scramble:** walk into a safe static ledge, hold Forward and press
Jump freshly. An upright native biped can mantle a broad, standing-clear top
within 1.25 units of total rise and 2.5 units of horizontal origin travel.
Assistance uses native velocity and collision for at most 24 fixed ticks;
it never snaps the body. The entire lift/across/settle route must fit the standing
body, and one safe flat face must support the full landing footprint. Narrow,
roof-blocked, excessive, moving and hazardous ledges refuse admission, leaving
native Jump available. Release Forward, enter a form/ability, encounter an
obstruction or stop progressing to cancel. Active wallrun/kick, tuck and slide
exclude a mantle. Training lanes at z=17 include a broad ledge plus blocked,
height, width and thin-rail controls.

Current live peers use protocol 80 / Parkour v4. Protocol 79 recordings retain
Parkour v3, protocol 78 retains v2 and protocol 77 retains v1. Earlier protocols cannot
acquire Parkour. Passive replay worlds
consume accepted positions and shapes rather than rerunning current wall forces.
The existing life-fenced owner-position multiplayer contract remains in effect;
the authority does not independently resimulate remote trajectories.

See [wallstrafe validation](parkour-wallstrafe-results.md) for completed Windows
scene, multiplayer and replay results. [Wallrun validation](parkour-wallrun-results.md) records the v3 checks and
performance diagnostics. The [v4 version contract](../network/parkour-v4-version-contract.md)
describes mantle admission and historical boundaries. Optional grapple follows
as a separate slice.
