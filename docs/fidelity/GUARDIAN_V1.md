# Guardian / Psycho Bit v1

Guardian is a normal Project Prime Hunter extension, not a claim of retail AMHE1
player fidelity. The authoritative playable catalog contains Samus through
Guardian (enum 0–7); `Hunter.Random` remains selector sentinel 8. Guardian's
affinity is Power Beam.

## Asset-backed presentation

The alt model is `PsychoBit`, with nodes `ROOT`, `L_Gill`, and `R_Gill`, five
authored recolors, and four animation groups with lengths 30/30/20/40. Project
Prime loads the supplied Psycho Bit sounds `PSYCHOBIT_CHARGE`,
`PSYCHOBIT_BEAM`, `PSYCHOBIT_FLY`, `PSYCHOBIT_DAMAGE`, and `PSYCHOBIT_DIE`, plus
the `psychoCharge` and `ineffectivePsycho` effects. The biped keeps Guardian's
canonical six recolors. Alt-model team mapping is explicit: canonical slots 4
and 5 map to Psycho Bit recolors 3 and 4; invalid indices are rejected.

The four group lengths are asset facts. Project Prime v1 deliberately uses only
authored group 0 as a stable paused player pose (frame 0) for idle and
locomotion. The remaining enemy groups are not assigned retail player
semantics; charge/release feedback is represented by authoritative effects and
audio instead of restarting an inferred skeletal clip.

AMHE1 does not provide a Guardian first-person arm-cannon model. Guardian keeps
the existing gunless first-person presentation instead of rendering the Samus
placeholder gun. Charge and muzzle feedback is attached to a finite anchor
derived from the final render camera and authoritative shot origin/direction;
third-person feedback remains presentation-only. Biped Power Beam shots still
use the normal authoritative aim and projectile pipeline. The HUD likewise
uses the documented coloured Samus portrait fallback until dedicated Guardian
HUD art is authored.

## Mechanics and compatibility

Psycho Bit uses the normal alternate-form transition, collision volume, grounded
movement, camera, freeze/cloak/damage/death cancellation, doors/forcefields,
and reset paths. It is hover-styled only within the normal authored alt traversal
envelope; it has no unrestricted flight. Unlike the retail rolling forms,
Guardian's ranged alt aim uses the ordinary biped pitch envelope (-85 to 85
degrees), so ordinary targets below the hover volume remain reachable. Holding
Alt Attack charges using
Guardian's existing authored `PlayerValues.AltAttackStartup`, then release spawns
a Power Beam projectile through the authoritative combat pipeline. The projectile
retains normal lag compensation and records generic `FromAlt` damage attribution.

The active alt attack is replicated with the generic snapshot bit in protocol 22.
Protocol-21 replay snapshots remain frozen at 104 bytes and use a dedicated replay
decoder; their Guardian value remains biped-only, and alt-form Guardian records are
rejected instead of inheriting protocol-22 Psycho Bit semantics. Protocol 17–20
replay snapshots remain on their 98-byte codec.

Exact retail Psycho Bit enemy behavior, frame-event semantics, and missing player
animation references are intentionally not guessed. Any future fidelity claim
requires version-correct AMHE1 binary/runtime evidence.
