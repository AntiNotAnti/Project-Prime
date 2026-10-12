# Balanced Mode Battlehammer: three-round burst rifle

This design replaces the **player-fired** Battlehammer in multiplayer Balanced Mode
only. Ordinary multiplayer, Adventure, non-Balanced matches and Weavel's automated
Halfturret keep their stock projectile behavior.

| Rule | Normal Battlehammer | Weavel affinity |
| --- | --- | --- |
| Burst | 3 bullets, one trigger event | Same |
| Bullet timing | 0 / 3 / 6 simulation frames | Same |
| Burst repeat | 27 simulation frames (450 ms) | Same |
| Cost | 12 UA per burst | Same |
| Speed | Stock Judicator raw 8192 (1 game unit / 60-Hz step) | Same |
| Gravity / random spread | 0 / 0 | Same |
| Direct / headshot damage | 12 / 12 per bullet | 14 / 14 per bullet |
| Maximum splash | 4 per bullet | 5 per bullet |
| Splash radius | 1.50 game units | 1.65 game units |
| Falloff | Linear, LOS checked | Linear, LOS checked |
| Knockback magnitude | 0.20 | 0.24 for rounds 1-2; **0.42 concussion** on round 3 |
| Shooter splash | Ordinary self-splash | **Immune** |
| Flight lifetime | 1.20 seconds *after each bullet activates* | Same |

One fire event produces three independent physical projectiles and three distinct
shot-proof component identities. Damage, splash and knockout impulse remain
authority-resolved; a direct hit excludes that victim from the same bullet's
splash. The third-round concussion is a stronger physical knockback vector,
not a freeze, stun, or lingering damage effect. Its stronger force applies to
direct and splash impacts, with normal wall line-of-sight restrictions.

## Transport, deterministic replay and resource policy

- Burst cadence and full 12-UA payment are shared by native input and
  NetShotProof's historical-source validator; mouse, controller, touch and
  bots use the same ceiling.
- One trigger fires three projectiles; holding fire repeats bursts after
  27 simulation ticks. The bullet ray is fixed at trigger time. This deliberately
  avoids synthesizing unattested intermediate aiming inputs on remote machines.
- Round 2 and round 3 use **negative projectile Age** as a 3/6-tick activation
  delay. Age and DamageDirMag already exist in the replay checkpoint schema,
  so no new checkpoint fields or wire packet shape are required. Delayed
  projectiles do not collide, expire their active flight lifetime, or draw
  a trail until activation.
- The native projectile pool preflights three free slots before spending
  ammo, and all three share the single fired shot's proof identity with
  distinct component ordinals.
- The read-only, single-projectile shadow/counterfactual estimator returns
  `HistoricalDataUnavailable` for this revised weapon until it supports
  multi-round ballistic timelines. Native authoritative collision remains active.
- Balanced Mode telemetry revision **3** renames the six Battlehammer diagnostic
  counters to round direct/splash and concussion direct/splash/damage/kill.
  Historical telemetry remains partitioned by revision.

## Important scope notes

- No extra headshot multiplier is introduced.
- Double damage and Prime Hunter's existing modifiers still apply to bullet
  damage; the concussion strength and blast radius do not scale with them.
- Weavel's self-splash immunity is a gameplay rule only while Balanced Mode
  is active. The standard Battlehammer retains its existing self-splash behavior.
- The existing Halfturret remains a single autonomous shell rather than an
  automatic three-round turret.
- The earlier terrain-impact cluster generation is removed from the Balanced
  player-fired Battlehammer. Its legacy replay field is retained for backwards
  checkpoint compatibility.
- One standard muzzle sound/flash occurs at the start of the burst. Delayed
  projectile trails become visible independently at their actual activation.

## Validation

Run the existing advanced rules scene checks, especially the revised checks in
`src/MphRead/Mods/Network/AdvancedMatchRulesCheck.cs` for damage, ammo,
spaced activation, third-round concussion magnitude, no gravity, own blast
immunity, normal/affinity parity and stock non-Balanced behavior.

Also exercise authority/remote projectile acceptance, replay seek during the
0-100 ms burst, 60/120/240 Hz visual presentation, server-side ammo/cadence
proof, and moderate/high-latency hit reconciliation before shipping.
