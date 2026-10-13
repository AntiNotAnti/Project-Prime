# Bot AI: unified human-style locomotion and aim motor

Implemented as a first, independently reviewable phase of the competitive bot overhaul.

## Input ownership

The native MPH personality tree is retained for navigation and objectives. Each frame
the tactical layer evaluates movement choices, and native Hunter combat decisions may
add actions. The final `ApplyBotMotor` step resolves directional conflicts, rate-limits
yaw/pitch, and emits one locomotion command. Tactical movement temporarily owns the
four biped movement flags only if a safe move was found; otherwise the original
navigation move is restored. Random combat strafing no longer overrides an existing
navigation command.

## Locomotion

The existing `PlayerControls.SetAnalogMovement` path carries analog traction
alongside the normal digital direction flags. Inputs ramp between -1 and 1 with
asymmetric acceleration/braking; opposite movement requests stop at zero on a
simulation tick before accelerating in the opposite direction.
The full native walk/strafe caps and collisions remain unchanged. Tactical motor
momentum is rejected when existing floor/wall/hazard probes say the next move is
unsafe. Alt forms continue using their native controls pending a separate alt
locomotion implementation.

## Aiming

Yaw/pitch requests are shaped with bounded angular acceleration and velocity,
plus braking near the aim point, rather than applying the full per-frame legacy
turn amount immediately. A reversing aim request never outputs movement in the
wrong direction while angular momentum is still being dissipated. The deterministic per-tier profiles tune this rate.
This is command smoothing, **not** smoothing authoritative muzzle, hitbox,
projectile or target positions. LOS/firing constraints remain intact.

## Defensive aim-math fix

The legacy hearing/target-turn path previously sent raw, non-unit world-space
Y offsets directly into `acos()`, producing invalid pitch for ordinary vertical
separation. Pitch is now calculated from a normalized, clamped vector with
finite/zero guards. Horizontal dot products are also clamped before `acos()`.
These changes remove a separate source of abrupt/invalid bot aiming.

## Orbiting combat and balanced weapon roles

Tactical pursuit and retreat can now blend a safe lateral component with the
radial step, rather than running straight at the target whenever outside the
preferred engagement range. Each diagonal uses a full floor/wall/hazard probe.
Balanced Battlehammer weapon selection and preferred spacing follow the actual
mid-range three-round burst rules, including a small Weavel affinity preference.

## Defensive projectile anticipation

Normal/Hard/Insane bots can estimate closest approach of *visible*, hostile,
active projectiles relative to their own velocity. The horizon and reaction
delay vary by difficulty. When a shot threatens their collision area, the bot
chooses a floor/wall-safe lateral move and commits briefly, instead of flipping
its strafe direction every frame. A wall-blocked projectile is not an observable
cue, and unseen opponent positions are never used by this dodge path. This is
short-horizon, constant-velocity prediction; not perfect projectile foresight.

## Combat consistency

Charge preference is now chosen once per weapon/target action instead of being
randomized every frame. The original RNG still supplies deterministic choices.
Balanced Mode lead and vertical ballistic aim use its tuned projectile speed;
the three-round Battlehammer uses zero ballistic gravity as the actual shots do.

## Multiplayer and lifecycle

Server-owned bots use the same native controls and `NetBotInput` /
`PlayerReplicationBridge` `SlotIntent` as before. Analog axes are already
supported by the existing protocol. Clients and replay replicas still never
execute bot decisions. Motor state clears on `Reset`, `InitializeAtSpawn`,
death, and the transition into/out of an alt form.

## Validation

```sh
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bot-ai
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bots
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bots-scene /path/to/game-config 'MP1 SANCTORUS' 7
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bots-online /path/to/game-config 'MP1 SANCTORUS'
```

Headless checks cover acceleration, reversing without an input snap, angular
velocity limits, fine tracking and rejecting invalid values. Asset-backed tests
must still verify actual body displacement, client visual heading, projectile
alignment, weapon use, alt-form parity, and observed skill.

## Follow-up phases

* Measured angular/locomotion telemetry, plus client-side presentation
  analysis where remote facing may still snap.
* Perception/short-term memory and acoustics without hidden-position leakage.
* Tactical route and cover scoring; predictive dodge and map pickups.
* State-aware weapon timing, Hunter combat behavior, coordinated team tactics.
* Skill/personality profiles with full benchmark campaigns against humans.
