# Alimbic turret draw-only pose ownership

The turret's native pose is a special case: its animated rotation node yields `_rotNodePos` which is later used to spawn physical projectiles. This PR introduces a scoped display-rate animation sample **only while queuing render items**, then reconstructs the authoritative integer pose in `finally` and reads `_rotNodePos` from that canonical pose.

No angle, collision, target selection, cooldown, spawn frame, network packet, replay or hit fact changes. Existing scene-specific aim transform (patrol vs target) is applied identically to canonical and sampled passes. Rendered palettes are snapshotted before canonical restoration.

Guard: `PROJECT_PRIME_BOSS_ANIMATION_BLEND=0` or `PROJECT_PRIME_ANIMATION_BLEND=0`. This does not cover other specialized boss appendages or damage-node material scripts, which require separate pose ownership audits.

Required acceptance: GL frame captures 60/120/144/240, turret attack/head position and shot rays in 2–8 player matches, replay/seek and no host advantage. No physical device testing was performed during this code slice.
