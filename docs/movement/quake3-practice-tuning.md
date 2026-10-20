# Quake III strafe-jump tuning draft

This playable offline preset targets Quake III / Quake Live-style strafe-jump
speed building. Classic remains the default. Existing Momentum profile v1,
match rules, protocol 59 and recorded behavior are unchanged. This draft is not
an exact Quake physics port: Hunter jump timing, gravity, collision, alternate
forms, abilities and the fixed 60 Hz simulation retain their native ownership.

## Launch and tune

Launch the desktop game with:

```text
-momentumq3dev "MP3 PROVING GROUND" -hunter Samus
```

Optionally add `-seconds 1..600` for a bounded session.

Use a multiplayer room name already installed in your game. Omitting `-seconds`
keeps practice running until you exit. This starts a new private one-player
battle scene with an unlimited match timer. The older `-momentumdev <room>`
still runs unchanged v1 coefficients. Normal practice/lobby Momentum still uses
v1; the draft does not change your saved movement preference.

Run forward, jump, then hold forward plus a strafe key while smoothly turning
in that strafe direction. Alternate the strafe direction on subsequent jumps.
Release/repress jump for legal native jumps; no automatic hop, extra jump,
jump buffer or coyote timer is added. Staying on the ground applies friction.
Use a large room: unbounded steering-generated speed can reach speeds for which
existing maps and collision were not designed. Human feel and high-speed map
qualification remain follow-up work.

To adjust coefficients, copy [the starter JSON](quake3-practice-default.json)
and launch:

```text
-momentumq3dev <room> -tuning /absolute/path/practice.json
```

The file loads and validates once before creating the scene. Restart practice
to apply edits. Unknown properties, invalid numbers and unsafe parameter ranges
are refused; files are never polled during simulation. Values are decimal JSON
numbers, with case-sensitive names. Missing properties use the draft defaults.

| Property | Default | Meaning |
| --- | --- | --- |
| `groundSpeed` | 7.199707 | Native-calibrated run speed in world units/second |
| `groundAcceleration` | 10 | Acceleration coefficient per second |
| `groundFriction` | 6 | Ground friction coefficient per second |
| `stopSpeedFraction` | 0.3125 | Friction floor relative to ground speed |
| `airAcceleration` | 1 | Air acceleration coefficient per second |
| `airWishSpeedFraction` | 1 | Air projection limit relative to ground speed |

The original v1 air projection limit is about 1.081 world units/second. At
running speed, forward+strafe already projects above that limit and cannot add
velocity. The draft limit is the full 7.199707, allowing that input to build
speed as the player turns. It also removes the v1 1.5× retention ceiling for this
private experiment. Turning technique still matters: a fixed view/diagonal will
ultimately reach its projection limit.

## Compatibility boundary

Selection happens once before simulation frame 0 and belongs to the original
local human owner. Network admission, replication, replay/side scenes, owner
replacement, unsupported Hunter variants, cleanup and other existing policy
losses permanently retire it. Abilities and special impulses use the existing
per-player native bypasses. The retained selection blocks live demo, clip and
server-replay recording for that scene, including after retirement; start a
fresh supported scene to record. Existing immutable archive export is unchanged.

Promoting the final coefficients into multiplayer or recordings requires a
separately identified profile and its compatibility validation. Do not edit
`MomentumProfileV1` to promote this draft. Performance optimization remains
deferred under the [v1 closeout](pr09-closeout.md); this tuning slice makes no
new performance-budget or physical-device acceptance claim.

Automated Windows results, exact source/binary identities and remaining limits
are in the [tuning results](quake3-practice-results.md).

## Design reference

The draft uses the behavior and public parameter facts in id Software's
[Quake III movement source](https://github.com/id-Software/Quake-III-Arena/blob/master/code/game/bg_pmove.c)
(acceleration 10/1, friction 6, stop speed 100) and
[default speed setting](https://github.com/id-Software/Quake-III-Arena/blob/master/code/game/g_main.c)
(320), scaled to native world units. Existing independently implemented
`MomentumMath` supplies the projected-acceleration equations. No Quake function
bodies, collision code or translated implementation are copied, and no new
runtime dependency is added. This is a Quake III-inspired draft, not a claim of
bitwise Quake Live equivalence.
