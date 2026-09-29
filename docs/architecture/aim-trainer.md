# Local aim trainer

Offline → Aim Trainer launches PRIME AIM LAB using an ordinary local Battle scene.
The scene owns the session, target controllers, fixed-step clock and shot ledger.
The seven launch drills use actual hunter entities and the normal weapon collision
and controller aim-assist paths. No training game mode, packet, match-rule field,
protocol version or Hunter License submission is introduced.

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

## Verification

Run from a build configured with the user's extracted game files:

```
ProjectPrime -trainingcheck
ProjectPrime -trainingcheck -simulation -shots /path/to/captures
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
