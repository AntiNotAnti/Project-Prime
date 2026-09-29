# Local aim trainer

Offline → Aim Trainer launches PRIME AIM LAB using an ordinary local Battle scene.
The scene owns the session, target controllers, fixed-step clock and shot ledger.
The nine launch drills use actual hunter entities and the normal weapon collision
and controller aim-assist paths. No training game mode, packet, match-rule field,
protocol version or Hunter License submission is introduced.

## Speed drills and settings

- Timed Flick uses one target with a 90-frame (1.5-second) lifetime and an
  18-frame acquisition gap after a hit or timeout. Expired targets are counted.
- Multi Target Flick presents four or five distinct targets at once, including
  two elevated targets (at least one on a high platform). Each hit target returns
  in a different available position after the acquisition gap. This drill does
  not time targets out, so score and throughput reward switching speed.
- The arena has platforms at heights 4, 8, 12 and 16. Moving targets use ground
  lanes outside Multi Target Flick; elevated movers have tighter bounds.
- Every explicit movement choice is honored. Tracking presets visibly select
  strafe or jump-strafe; ordinary precision/flick drills preserve target count.
- Imperialist ignores its shot cooldown only on a fresh fire press in training,
  by default. Holding fire retains normal timing. Advanced options can restore
  ordinary reload timing. “Refill ammo on hit” is separate from shot cooldown.
- Multi-target hits score and hide each contacted target independently while
  shot accuracy counts each shot once. Personal-best keys are versioned so scores
  using the new timing/scoring rules do not compete with old records.

## Runtime boundaries

- `AimTrainerLaunch` sanitizes configuration and persists it separately from bot
  rules in the launcher preferences. Existing LaunchKind values retain their IDs.
- `MatchStart` and `AndroidMatch` attach training only to local scenes. Online and
  replay scenes cannot attach a session. Scene cleanup drops the session.
- Training intercepts bot input before normal AI, blocks target weapon firing and
  pickups, and consumes damage before deaths, invulnerability, scoring and career
  accounting. Briefly hidden targets leave the normal assist candidate set.
- Firing opens a local shot identity before projectile creation, including
  synchronous collision. Projectiles inherit that identity; dry fire rolls back.
  Hits are attributed to the target appearance present at launch. Pending shots
  resolve after a ten-second simulation timeout or at the end of the run.
- Timing is in 60 Hz simulation frames. Rendering cannot advance trainer stats.
  Completion stops simulation and presents dedicated desktop/Android results.
- Retry preserves configuration and hunter, with a new seed unless fixed-seed is
  selected. Change Drill returns to the training controls; Exit returns to Offline.
- `aim-trainer.json` holds local best scores and their summary metrics, separated
  by input source and configuration. Pen input has a separate Stylus category.

## Measurements

Accuracy counts shots with a collision, not pellets or render frames. Headshot-only
body contacts record body hits but award no points. Reaction is launch minus target
appearance; acquisition includes projectile travel. Misses resolve only after the
pending window. Direct, splash, charged, freeze, scoped and unscoped contacts are
recorded from weapon/collision state. Headshots use the game's DamageFlags.Headshot.

Tracking is the fraction of simulation frames with a confirmed damage connection.
Shock Coil uses tracking and connection statistics rather than conventional shot
accuracy. Continuous lock samples count uninterrupted connected frames. This is a
connection measurement, not an inferred screen-space crosshair overlap metric.

The built-in arena recipe ships with desktop and Android builds. It includes clear
flick positions, long-distance lanes, elevated platforms and a separate jump-pad
lane. Motion uses normal movement/jump controls, including difficulty speed and
cadence; tracking targets are not teleported during active segments.

## Audit regression coverage

The trainer suite checks every movement pattern in real scene simulation,
seven-target respawn exhaustion, exact target lifetime boundaries, multi-target
platform support and distinct positions, splash scoring, held versus fresh-click
Imperialist fire, normal reload timing, and launcher preset visibility. The real
OpenGL startup check accepts `-trainingdrill TimedFlick` or
`-trainingdrill MultiTargetFlick`, with optional `-trainingcapture /path/image.png`.

## Verification

Run from a build configured with the user's extracted game files:

```
ProjectPrime -trainingcheck
ProjectPrime -trainingcheck -simulation -shots /path/to/captures
ProjectPrime -windowcheck -trainingwindowcheck
ProjectPrime -primeuicheck -shots /path/to/ui-captures
ProjectPrime -gamepadcheck
```

The simulation check exercises real projectile collision, headshot damage flags,
target transitions, AI/fire suppression, timing, pending misses, completed-score
immutability, match-counter isolation and cleanup. UI captures cover desktop,
phone landscape and phone portrait configuration and results.

Physical controller/touch/stylus playtesting, display-rate comparison and Android
on-device lifecycle acceptance still require manual testing. Authored map metadata
anchors, replay run export and the expanded post-V1 drill catalogue remain the
plan's explicitly deferred follow-up work.

Verification on 2026-09-28: desktop and Android builds succeeded. The trainer's
28 configuration/simulation assertions passed, as did 18,744 launcher UI checks
and 909 controller checks. Desktop and both phone orientations were captured and
inspected. The signed Android APK contains `assets/maps/aimlab.json`. Existing
compiler/platform warnings remain; no new build errors were accepted.

Startup regression: headless map generation previously omitted source texture
pixels, then cached an arena that simulation could load but rendering could not.
Map export now explicitly decodes textures outside the simulation model cache,
and compiler fingerprint 7 rebuilds older output. The simulation check decodes
the generated arena's textures; the window check loads it through `MatchStart`,
verifies rendered frames and an advancing trainer timer, and closes cleanly.
Both checks passed on Apple M4 Pro OpenGL on 2026-09-28 (29 trainer assertions).

Audit on 2026-09-29: removed silent movement/count overrides, added timed and
multi-target flick drills, corrected multi-target projectile scoring, bounded
elevated movement and circular lanes, handled exhausted seven-target lanes, and
added trainer-only fresh-press Imperialist cooldown bypass. Compact HUD placement
keeps the central and elevated target field clear. The expanded trainer suite
passed 80 simulation/configuration/UI checks; launcher UI passed 113 checks.
Both new drills passed real OpenGL startup/readback and timer checks on macOS.
Desktop and Android builds passed with existing warnings; Android device testing
and physical controller/touch verification were not performed in this audit.
