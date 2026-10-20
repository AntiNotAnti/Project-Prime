# Offline Scout Drone laser and native bot combat

This increment extends [primary practice](second-hunt-scout-drone-primary-practice-2026-10-20.md)
with the SecondHunt cutting-laser timing and a separate explicit offline combat
practice choice. Native remains the default. Imported assets do not enable these
mechanics. The original seven Hunter identities, protocol, replay formats, native
UI action boundary and native canvas ABI remain unchanged.

The laser uses the existing native Zoom control in the mobile drone form. Its
134 subsequent simulation steps include windup, visible frames57 through122,
15 damage opportunities at57,61,...113, recovery and a separate600-step wait.
Releasing Zoom does not abort the clip; holding it can start another clip only
when its wait ends. A folded laser takes priority over simultaneous held burst
input. Neither attack interrupts the other, and each cooldown continues while
the other runs. Native merge/freeze cancels new laser damage with the full wait;
life reset or owner retirement clears the ray and state. Rendering consumes only
the current cached simulation ray.

Activation captures one of four donor sweep patterns and the first native
world/body target distance clamped to4..40. Each active step uses current native
aim and position, a60-unit ray and native current-player collision shapes. World
collision clamps both the offset origin and ray before choosing the closest
eligible opposing body. Hits call the existing damage owner with10 base damage,
normal native powerup/protection/modifier handling and no fabricated beam,
accepted-fire or weapon statistics. The private explicit attribution argument
cannot carry into nested public damage calls. The local laser label does not
change canonical native alt-attack classification.

Two crossed red native TrailSingle submissions use the player's existing trail
texture owner. Psycho Bit and native half-turret models remain visible fallbacks;
source drone/Trooper animations and materials are not integrated. The HUD shows
laser readiness/windup/sweep/recovery/cooldown and the existing Jump, Alt Attack
and Zoom inputs. Android already exposes Zoom through its existing touch mapping.

The four-step MECHANICS choice adds DRONE COMBAT PRACTICE after the existing
zero-bot Scout Drone practice. Entry captures Weavel, Battle and one native bot;
the user can select0..7 opponents and native difficulty. The existing local
population owner creates the human and native bots, with explicit FFA teams.
Only the exact pre-initialization human receives the private rule. Admission
checks the exact created/active population and leaves native bot AI and rules
unchanged. The older wall/drone choices retain their zero-bot restriction.
Invalid raw combat setup is refused before preference persistence. Network,
replay/live recording, campaign/Ship, automatic rematch and server career fences
remain in effect; exact Android handoff capture includes the combat discriminator.

Adaptation reference: SecondHunt41452f2d053fcfa679e33c00a73d120a53f59e61,
PlayerDroneLaser.cs and PlayerScoutDrone.cs, MIT (same retained repository license).
No donor global application flags, bot automation, simulation clock or packets
are imported.

## Qualification

Qualification is in progress; no frozen-source or final package claim yet.
Exploratory receipts are retained locally under
`artifacts/sh-integration/scout-laser-exploratory-01/`.

Rollback: leave MECHANICS at Native or revert this increment. Remaining work
includes Pirate Blaster/affinity, source Trooper/drone/cannon presentation and
animation, standing-body geometry/headshots, full Overcharged runtime integration,
network/replay identity and physical Android gameplay acceptance. These mechanics
are an explicit native-fallback practice mode, not completed imported characters.
