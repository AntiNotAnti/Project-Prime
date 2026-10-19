# CW-00 ADR C: accepted headshot facts own reactions

Status: accepted direction; all visual acceptance pending CW-07/08.

Use accepted damage/ReplayShotFact (headshot/lethal, damage event, match/epoch, both occupant/life
identities, tick and impact point). MatchSemanticEvent supplies accepted actor/target boundaries,
but lacks complete impact direction; do not invent direction or treat predicted damage as confirmed.
Inspect receiver parity before adding any compact presentation-only extension.

Head/spine overlays apply only while submitting geometry, restore canonical matrices in finally,
do not move hit volumes, muzzle, aim, simulation RNG or player velocity. Coalesce/deduplicate by
canonical fact identity; death supersedes flinch. Respawn, form, teleport, seek and epoch changes
cancel. Missing rigs use existing body reaction; vanished/dissolved bodies suppress corpse rendering.

CW-07/08 require seven-Hunter native/HD, replay/killcam/spectator, life and matrix-restore tests.
Rollback to existing reactions/cosmetics must be immediate and dispose presentation state.
