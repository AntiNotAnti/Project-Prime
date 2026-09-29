# Project Prime 30 Hz → 60 Hz FPS Conversion Audit & Native Physics Implementation Plan

## Objective

Audit and correct Project Prime's inherited 30 Hz → 60 Hz gameplay conversions while preserving:

- 60 Hz simulation
- 60 Hz networking
- dedicated-server architecture
- prediction and lag compensation
- replay compatibility
- telemetry
- match clocks
- high-refresh presentation
- existing Project Prime gameplay enhancements

The goal is **not** to mechanically replace every `todo: FPS stuff` site.

The goal is to identify conversions where running the original logic twice as often produces mathematically or behaviorally different results.

Alt forms are intentionally deferred until the end of the project. Once the underlying shared physics systems are corrected and stable, **all seven hunter alt forms will be evaluated together in a dedicated native-parity pass**.

---

# Core Rules

## Rule 1: Project Prime remains 60 Hz

Do not return the game simulation to 30 Hz.

Networking, prediction, replay, telemetry, hit registration and other systems depend on the current 60 Hz simulation clock.

## Rule 2: Do not mass-edit FPS markers

Do not perform automated replacements such as:

```text
* 2 -> remove
/ 2 -> remove
coefficient -> coefficient / 2
```

without understanding the original behavior.

Every conversion must first be classified.

## Rule 3: Exact behavior matters more than superficial math

These are not generally equivalent:

```text
Native:
value *= F

Current approximation:
value += (value * F - value) / 2
```

Likewise:

```text
Native:
x += (target - x) * A
```

cannot always be converted using:

```text
A / 2
```

For exact half-step interpolation:

```text
A60 = 1 - sqrt(1 - A30)
```

For multiplicative damping:

```text
F60 = sqrt(F30)
```

## Rule 4: Update order is part of physics

Do not evaluate:

- acceleration
- damping
- gravity
- integration
- collision
- speed caps

in isolation.

Their ordering can materially change the result.

## Rule 5: Alt forms come last

Do not tune Samus, Spire or another hunter around physics bugs that may disappear after the shared movement engine is corrected.

Existing Samus/Spire improvements remain in place until the final alt-form pass.

---

# Phase 0: Establish Baseline

## Goal

Freeze a known-good behavioral baseline before changing FPS conversion math.

## Tasks

- Record current commit SHA.
- Record current protocol version.
- Record current physics-sensitive configuration.
- Verify normal builds:
  - Windows
  - Linux
  - Android where practical
- Run existing test/check commands.
- Save representative replay files.
- Save telemetry from representative matches.
- Record baseline movement measurements.

## Baseline movement scenarios

Capture at minimum:

### Biped

- standing idle
- forward acceleration
- backward acceleration
- strafing
- diagonal movement
- release-to-stop
- jump
- running jump
- falling
- landing
- slope ascent
- slope descent
- wall collision
- ceiling collision
- jump pad
- knockback
- moving platform

### Weapons/gameplay

- Power Beam autofire
- charge weapons
- missile cooldown
- Imperialist
- continuous weapons
- freeze
- burn
- damage invulnerability
- disruption
- pickups
- death/respawn

### Network

Repeat selected scenarios:

- offline
- server-local
- client
- moderate simulated latency
- replay playback

## Deliverable

```text
artifacts/fps-audit/baseline/
```

Include:

```text
movement.csv
timings.csv
combat.csv
network.csv
replays/
telemetry/
```

---

# Phase 1: Build `-fpsconvertaudit`

## Goal

Create a permanent auditing tool for all FPS-conversion sites.

## Command

Add:

```text
-fpsconvertaudit
```

## Scanner behavior

Search for:

```text
todo: FPS stuff
sktodo: FPS stuff
FPS stuff?
```

Also detect suspicious unmarked constructs near gameplay code.

Examples:

```csharp
value /= 2;
value *= 0.9f;
value += diff / 2;
Position += Speed / 2;
Speed += Acceleration / 2;
```

## Classification categories

Each site receives one primary category:

```text
TimerCounter
AnimationCadence
LinearIncrement
Velocity
Acceleration
PositionIntegration
Gravity
Damping
Interpolation
SpeedCap
Collision
Slope
Knockback
ProbabilityPerFrame
Camera
Effects
AI
Projectile
Unknown
```

## Risk levels

### SAFE

Obvious wall-clock conversion.

Example:

```csharp
timer = nativeFrames * 2;
```

### REVIEW

Could be correct but requires contextual verification.

Example:

```csharp
angle += nativeAngularVelocity / 2;
```

### HIGH

Likely mathematically non-equivalent.

Example:

```csharp
speed += (targetSpeed - speed) / 2;
```

### CRITICAL

Changes collision or integrated gameplay state.

Example:

```csharp
Speed += gravity / 2;
Position += Speed / 2;
CheckCollision();
```

## Report format

Generate:

```text
artifacts/fps-audit/fps-conversion-report.md
artifacts/fps-audit/fps-conversion-report.csv
artifacts/fps-audit/fps-conversion-report.json
```

Each entry should contain:

```text
file
line
method
expression
category
risk
native30Expression
current60Expression
expectedEquivalent
notes
status
```

## Important

The audit tool should **report**, not modify source.

---

# Phase 2: Add FPS Math Utilities

## Goal

Centralize mathematically correct frame-rate conversions.

Create something similar to:

```text
Mods/Physics/FrameRateMath.cs
```

## Helpers

Implement helpers such as:

```csharp
HalfStepDamping(float nativeFactor)
```

Equivalent:

```text
sqrt(nativeFactor)
```

---

```csharp
HalfStepInterpolation(float nativeAmount)
```

Equivalent:

```text
1 - sqrt(1 - nativeAmount)
```

---

```csharp
ScaleLinearPerTick(float nativeValue)
```

For truly linear per-tick quantities:

```text
nativeValue / 2
```

---

Potential generic forms:

```csharp
ConvertDamping(float nativeFactor, float nativeHz, float targetHz)

ConvertInterpolation(float nativeAmount, float nativeHz, float targetHz)

ConvertLinearRate(float nativeAmount, float nativeHz, float targetHz)
```

## Requirements

- deterministic
- no dependence on render FPS
- no allocations
- unit tested
- clearly distinguish rates from state
- do not use these helpers as an excuse to blindly convert every system

---

# Phase 3: Build a Physics Shadow Harness

## Goal

Compare the current 60 Hz implementation against expected/native-equivalent behavior before switching live gameplay.

## Command

Add:

```text
-fpsphysicscheck
```

## Per-tick logging

Record:

```text
frame
substep
hunter
form
positionBefore
velocityBefore
input
traction
acceleration
speedCap
speedFactor
gravity
positionPredicted
positionActual
velocityPredicted
velocityActual
standing
grounded
collisionFlags
collisionPlane
collisionDepth
collisionPushout
positionError
velocityError
```

## Comparison interval

Compare state at equivalent 30 Hz boundaries:

```text
60 Hz frame 0
60 Hz frame 2
60 Hz frame 4
60 Hz frame 6
...
```

Do not judge parity solely from the intermediate odd frame.

## Error metrics

Calculate:

```text
positionError
velocityError
headingError
collisionStateMismatch
groundedMismatch
standingMismatch
timerMismatch
```

## Output example

```text
[fpsphysics] frame=220
positionError=0.0014
velocityError=0.0002
grounded=OK
standing=OK
collision=OK
damping=FAIL
```

---

# Phase 4: Core Biped Movement

This is the first live gameplay physics conversion pass.

Alt forms remain untouched.

## Audit

Focus on:

```text
PlayerInput.cs
PlayerProcess.cs
PlayerEntity.cs
PlayerCollision.cs
```

## 4.1 Biped traction

Audit:

```text
WalkBipedTraction
StrafeBipedTraction
```

Current movement adds traction into `speedDelta`, which is then applied every simulation update.

Determine whether the original value represented:

- native per-30-Hz-tick impulse
- a continuous acceleration rate
- something already converted earlier

Do not assume.

## Required tests

Measure:

```text
0 -> 25% speed
0 -> 50% speed
0 -> 100% speed
180-degree reversal
diagonal acceleration
air steering
slippery terrain
```

---

## 4.2 Horizontal damping

Replace approximate half-step damping where native parity requires it.

Current pattern:

```csharp
Vector3 speedMul = Speed.WithX(Speed.X * speedFactor)
    .WithZ(Speed.Z * speedFactor);

Speed += (speedMul - Speed) / 2;
```

Evaluate against:

```text
sqrt(speedFactor)
```

Do not switch until shadow traces prove the replacement.

## Test environments

- standing
- walking
- strafing
- airborne
- slippery floor
- post-knockback
- post-jump-pad

---

## 4.3 Speed caps

Audit:

```text
WalkSpeedCap
StrafeSpeedCap
_hSpeedCap
speed-cap decay
```

Verify whether caps themselves are absolute and therefore frame-rate independent.

Audit any per-frame changes to those caps separately.

---

## 4.4 Jumping

Verify:

```text
JumpSpeed
jump state transitions
UsedJump
Grounded
Standing
_timeSinceGrounded
```

Initial jump velocity should usually remain an absolute state change, not be halved simply because the simulation runs twice as fast.

Test:

```text
jump apex
time to apex
maximum height
landing time
horizontal travel
running jump
```

---

# Phase 5: Gravity and Position Integration

## Goal

Correct one of the highest-risk FPS conversions.

Current behavior resembles:

```csharp
Speed += Gravity / 2;
Position += Speed / 2;
```

Two half steps do not reproduce the same position as one original native integration step.

## Do not simply tweak gravity

The solution must preserve:

```text
velocity
position
collision timing
ground transitions
```

together.

## Candidate architecture A

Exact equivalent 60 Hz integrator.

Derive a half-step scheme whose state after two 60 Hz ticks equals native state after one 30 Hz tick.

## Candidate architecture B

Native-cadence gameplay kernel.

Run authoritative physics at equivalent 30 Hz boundaries while deriving deterministic intermediate 60 Hz states.

For collision-sensitive movement, architecture B may ultimately be safer.

## Tests

### Vertical motion

- standing fall
- normal jump
- falling from fixed heights
- terminal/fall-speed behavior
- jump pads
- knockback
- ceiling impact

### Required measurements

```text
position
velocity
time-to-apex
apex-height
landing-frame
fall-damage-trigger
collision-plane
```

---

# Phase 6: Collision, Slopes and Pushout

## Goal

Remove historical compensating hacks where possible.

Priority file:

```text
PlayerCollision.cs
```

## Audit

### Lateral wall collision

Investigate:

```text
wall sliding
horizontal pushout
velocity projection
```

### Floor collision

Verify:

```text
standing transition
ground normal
landing response
fall damage
```

### Ceiling collision

Current code contains explicit 30 Hz compensation.

Reconstruct expected native response before replacing it.

### Slopes

Audit:

```text
vertical pushout
maximum climb angle
velocity along slope
gravity while grounded
```

### Edge transitions

Test:

```text
flat -> slope
slope -> flat
floor -> ledge
ledge -> air
air -> slope
```

## Acceptance

Collision corrections must not introduce:

- jitter
- wall sticking
- floor tunneling
- ceiling sticking
- incorrect slope climbing
- oscillating grounded state

---

# Phase 7: Jump Pads and Knockback

## Jump pads

Audit:

```text
_jumpPadAccel
jump-pad control lock
gravity compensation
jump-pad velocity restoration
```

The jump-pad calculations currently mix native-duration math with 60 Hz counters.

Verify entire trajectories rather than individual constants.

## Knockback

Audit:

```text
Acceleration
_accelerationTimer
damage knockback
alt-attack knockback
explosion impulse
```

Current pattern:

```csharp
Speed += Acceleration / 2;
```

may be correct as a linear rate, but interaction with corrected damping and collision must be tested.

## Acceptance

Two 60 Hz frames should reproduce expected:

```text
net impulse
position
velocity
collision result
```

at the equivalent native boundary.

---

# Phase 8: Weapons and Gameplay Timing

Once shared movement is stable, audit combat timing.

## Systems

### Weapon firing

- shot cooldown
- autofire
- repeat fire
- charged shot thresholds
- full charge
- partial charge
- reload-style cooldowns
- continuous weapons

### Status effects

- freeze
- burn
- disruption
- damage invulnerability
- spawn invulnerability

### Damage ticks

Verify ticks occur at the same real-world cadence.

### Attack startup/recovery

Audit:

```text
startup timers
active windows
recovery
cooldowns
```

## Priority principle

Most timer conversions such as:

```text
30 -> 60
15 -> 30
nativeTimer * 2
```

are expected to remain correct.

Only change them when tests show gameplay differences.

---

# Phase 9: Projectiles and Gameplay Entities

Audit physics-driven gameplay entities separately.

## High-value targets

### Bombs

Current patterns include:

```text
speed interpolation
Position += speed / 2
collision using half-step velocity
```

Create deterministic trajectory tests.

### Halfturret

Audit:

```text
vertical gravity
vertical integration
cooldown interpolation
movement
```

### Pickups

Audit attraction movement such as:

```text
Position += direction * amount / 2
```

Linear attraction may be fine, but distance-dependent attraction becomes nonlinear because position changes twice as frequently.

### Moving objects

Audit:

- platforms
- crushers
- moving hazards
- physics-triggered map objects

---

# Phase 10: Enemy and AI FPS Audit

## Goal

Audit AI only after player/combat physics are trustworthy.

## Main issue categories

### Linear motion

Usually safe:

```text
speed / 2
angleIncrement / 2
```

provided the rate is truly linear.

### Steering interpolation

High priority.

Examples similar to:

```csharp
direction += (target - direction) / 8 / 2;
```

should be tested against exact interpolation.

### Gravity

AI using:

```text
gravity / 2
position += speed / 2
```

requires the same scrutiny as player physics.

### AI RNG

Any probability checked once per frame must be investigated.

A native:

```text
1% chance every 30 Hz frame
```

executed unchanged at 60 Hz changes probability per second dramatically.

Use:

```text
P60 = 1 - sqrt(1 - P30)
```

when a true independent per-frame probability needs preservation.

### Attack timers

Simple timer doubling is usually safe.

## First AI candidates already identified

- Zoomer
- Geemer
- Petrasyl variants
- Voldrum variants
- Temroid
- Crash Pillar
- War Wasp variants

---

# Phase 11: Camera and Presentation

Gameplay physics should already be stable before this phase.

## Camera interpolation

Audit:

```csharp
CameraInfo.Position +=
    (target - CameraInfo.Position) * factor;
```

If `factor` originated at 30 Hz and now runs at 60 Hz, convert appropriately.

## View tilt

Audit multipliers such as:

```csharp
_viewTiltAngleH *= 0.9f;
```

At 60 Hz, applying `0.9` twice does not equal native `0.9`.

Use exact half-step damping where required.

## Morph camera

Audit:

```text
_field68C
_field690
camera distance interpolation
camera target interpolation
```

## Other presentation

Audit:

- view bob
- weapon bob
- sway
- smoke
- alpha fades
- model wobble
- animation cadence
- camera shake
- cosmetic spin

Visual-only discrepancies are lower priority unless they materially affect aiming.

---

# Phase 12: Replay / Network / Prediction Regression

After physics corrections, verify infrastructure has not diverged.

## Dedicated server

Confirm server remains authoritative where currently designed.

## Networking

Verify:

```text
movement snapshots
intent processing
combat reconciliation
prediction
correction
remote interpolation
```

## No host advantage

Run identical movement/combat scenarios as:

```text
server-local player
remote client
```

Compare results.

## Replay

Replay the same recorded inputs.

Verify:

```text
position
velocity
collision
shots
damage
death
match clock
```

remain deterministic.

## Lag compensation

Do not alter lag-comp frame numbering as part of this project.

The simulation remains 60 Hz externally.

---

# Phase 13: Full FPS Audit Cleanup

At this point rerun:

```text
-fpsconvertaudit
```

Each marker should have a disposition.

Possible statuses:

```text
VerifiedSafe
ConvertedExact
NativeCadence
IntentionalDifference
PresentationOnly
Deferred
NeedsResearch
```

Do not remove comments merely to reduce the marker count.

Replace vague markers with meaningful documentation where useful.

Example:

```csharp
// Native value is per 30 Hz tick.
// This is converted using exact 60 Hz exponential damping.
```

---

# Phase 14: ALT FORM AUDIT — LAST

This phase starts only after:

- core player physics is stable
- gravity/integration is stable
- collision is stable
- weapons are stable
- gameplay entities are stable
- AI conversion work is stable
- camera conversion work is stable
- network/replay regression passes

The purpose is to evaluate **all hunter alt forms together**.

Do not declare Samus or Spire complete before this phase.

---

# Phase 14A: Build `NativeAltInput`

Introduce a common representation such as:

```csharp
struct NativeAltInput
{
    float Forward;
    float Backward;
    float Left;
    float Right;

    float SteerDeltaX;
    float SteerDeltaY;

    bool Boost;
    bool Attack;
}
```

Possible simpler representation:

```csharp
struct NativeAltInput
{
    Vector2 Move;
    Vector2 SteerDelta;

    bool Boost;
    bool Attack;
}
```

## Input sources

Map:

```text
keyboard
mouse
controller
touch
stylus
network intent
bot input
replay input
```

into the same logical representation.

Input systems should not directly replace native physics.

---

# Phase 14B: Reconstruct Native `altSteerDelta`

Investigate original input fields:

```text
CPlayer + 0x464 + 0x2A
CPlayer + 0x464 + 0x2C
```

Mapped in Project Prime as approximately:

```text
PlayerInput.Field2A
PlayerInput.Field2C
```

Determine:

- units
- sign
- range
- accumulation
- reset behavior
- consumption order
- interaction with movement
- interaction with facing

This should become the authoritative native-style steering input representation where appropriate.

---

# Phase 14C: Build `-nativealtcheck`

Add:

```text
-nativealtcheck
```

Run reconstructed native alt behavior in shadow mode.

Do not initially control the real player.

## Log

```text
hunter
input
steerDelta
positionBefore
velocityBefore
traction
friction
gravity
speedCap
speedCapDecay
collisionPlane
collisionDepth
collisionPushout
standing
grounded
facing
altAttack
boost
predictedPosition
actualPosition
predictedVelocity
actualVelocity
positionError
velocityError
headingError
```

---

# Phase 14D: Evaluate All Seven Hunter Alt Forms

## 1. Samus — Morph Ball

Audit:

- traction
- momentum
- friction
- steering
- boost charge
- boost release
- speed cap
- airborne movement
- slope behavior
- collisions
- bomb interaction
- rolling transform

Preserve Project Prime enhancements:

- mouse/touch swipe sensitivity
- current-frame pointer capture
- aimed flick boost
- controller support

Modern input should feed native-style locomotion rather than replace it.

---

## 2. Kanden — Stinglarva

Audit:

- traction
- steering
- segmented-body movement
- segment positioning
- collision interaction
- bomb behavior
- surface movement
- speed damping
- rolling transform

Pay particular attention to existing per-frame Kanden segment movement.

---

## 3. Spire — Dialanche

Audit:

- traction
- rolling
- wall climbing
- wall collision
- ledge transition
- vertical assist
- speed cap
- gravity
- slam
- rolling transform

Preserve deliberate Project Prime control changes:

- fast swipe does not trigger slam
- regular attack remains slam
- swipe gives only slight mobility nudge

### Ledge crest assist

Do not remove immediately.

After native reconstruction:

1. test without crest assist
2. compare native behavior
3. determine whether corrected collision naturally solves the problem
4. retain only the minimum enhancement still needed

---

## 4. Noxus — Vhoscythe

Audit:

- movement
- spin
- wobble
- tilt
- attack startup
- attack movement
- collision response
- bounce response
- spin acceleration

Noxus currently contains several nonlinear interpolation conversions and is an important parity test for the FPS math utilities.

---

## 5. Trace — Triskelion

Audit:

- locomotion
- lunge
- lunge acceleration
- acceleration timer
- vertical impulse
- collision
- attack recovery
- facing
- transformation presentation

---

## 6. Sylux — Lockjaw

Audit:

- locomotion
- bombs
- bomb overuse
- bomb movement interactions
- grounded behavior
- air gravity
- collision
- attack state
- transform behavior

---

## 7. Weavel — Half-Turret

Audit:

- body locomotion
- attack lunge
- vertical impulse
- turret deployment
- turret gravity
- turret firing
- turret cooldown behavior
- body/turret synchronization
- transformation transitions

---

# Phase 14E: Cross-Hunter Alt Matrix

Create an automated comparison table:

```text
Hunter | Acceleration | Friction | Air | Collision | Attack | Transform | Native parity
Samus
Kanden
Spire
Noxus
Trace
Sylux
Weavel
```

Test all hunters through identical categories.

## Common test suite

### Movement

```text
forward 1 second
backward 1 second
left/right
diagonal
release to coast
180-degree reversal
```

### Air

```text
walk off ledge
fall
jump-pad launch
collision while airborne
```

### Collision

```text
flat wall
45-degree wall
corner
slope
ceiling
ledge
moving platform
```

### Special

Each hunter receives hunter-specific attack/mechanic tests.

---

# Phase 15: Final Native-Parity Evaluation

Once all alt forms have been audited, evaluate Project Prime behavior as a whole.

## Categories

Score differences descriptively rather than hiding intentional changes.

Use:

```text
NativeParity
ModernInputExtension
IntentionalProjectPrimeEnhancement
Bug
Unknown
```

## Project Prime enhancements that should survive

Unless testing demonstrates a serious conflict:

### General

- controller analogue input
- modern mouse controls
- touch/stylus controls
- collision inversion fixes
- dedicated servers
- no host advantage
- 60 Hz networking
- replay compatibility
- high-refresh presentation

### Samus

- swipe sensitivity
- low-latency pointer capture
- aimed flick boost

### Spire

- swipe mobility nudge
- swipe not triggering slam
- current attack control behavior

---

# Phase 16: Final Validation

## Build matrix

Run once after the implementation settles:

```text
Windows Release
Linux Release
Android Release
```

Avoid unnecessary CI runs during intermediate investigation.

## Automated checks

Add:

```text
-fpsconvertaudit
-fpsphysicscheck
-nativealtcheck
```

to the appropriate validation workflow.

## Regression checklist

Confirm:

- no movement regression
- no wall inversion regression
- no falling through maps
- no spawn regression
- no match-start regression
- no replay divergence
- no network desync
- no hit-registration regression
- no new host advantage
- no camera jitter
- no high-refresh ghosting regression

---

# Suggested PR Breakdown

## PR 1 — FPS Audit Infrastructure

Implement:

```text
-fpsconvertaudit
FPS classification
JSON/CSV/Markdown reports
FrameRateMath
math unit tests
```

No gameplay behavior changes.

---

## PR 2 — Physics Shadow Harness

Implement:

```text
-fpsphysicscheck
state capture
30 Hz boundary comparisons
movement trajectory fixtures
```

No live behavior changes.

---

## PR 3 — Biped Traction + Damping

Correct:

```text
traction
horizontal damping
speed-cap behavior
```

Only after shadow results validate changes.

---

## PR 4 — Gravity + Integration

Correct:

```text
gravity
velocity integration
position integration
```

Add jump/fall trajectory regression tests.

---

## PR 5 — Collision + Slopes

Correct:

```text
floor
wall
ceiling
slope
pushout
grounded/standing transitions
```

---

## PR 6 — Jump Pads + Knockback

Correct:

```text
jump-pad trajectory
control lock
knockback
acceleration spans
```

---

## PR 7 — Weapon/Gameplay Timing

Audit and correct:

```text
cooldowns
charge timing
status effects
damage ticks
attack windows
```

---

## PR 8 — Projectile/Gameplay Physics

Audit:

```text
BombEntity
HalfturretEntity
moving pickups
physics-driven objects
```

---

## PR 9 — AI FPS Conversion

Correct:

```text
AI steering
movement
gravity
probability
attack cadence
```

---

## PR 10 — Camera + Presentation

Correct:

```text
camera interpolation
view tilt
bob
sway
effects
visual easing
```

---

## PR 11 — Network/Replay Physics Regression

Validate:

```text
prediction
snapshots
reconciliation
replay
lag compensation
dedicated server
```

---

## PR 12 — Alt-Form Audit Infrastructure

Implement:

```text
NativeAltInput
altSteerDelta reconstruction
-nativealtcheck
all-hunter alt fixtures
```

No live alt replacement yet.

---

## PR 13 — Native Alt Movement

Apply corrected native-style architecture to:

```text
Samus
Kanden
Spire
Noxus
Trace
Sylux
Weavel
```

Prefer shared architecture with hunter-specific behavior rather than seven unrelated movement systems.

---

## PR 14 — Alt Enhancements Reconciliation

Reintroduce or retain intentional Project Prime extensions on top of verified native locomotion.

Evaluate:

```text
Samus swipe
Samus flick boost
Spire swipe nudge
Spire crest assist
controller analog behavior
touch/stylus behavior
```

---

## PR 15 — Final FPS Audit Closure

Rerun the full audit.

Every FPS site receives a documented disposition.

Generate final:

```text
FPS-CONVERSION-AUDIT.md
NATIVE-MOVEMENT-PARITY.md
ALT-FORM-PARITY.md
```

---

# Completion Criteria

The FPS conversion project is complete when:

- all FPS markers are classified
- critical nonlinear conversions are resolved or documented
- biped movement matches expected native behavior at equivalent 30 Hz boundaries
- gravity trajectories are verified
- collision state transitions are stable
- slopes behave consistently
- jump pads are deterministic
- knockback is deterministic
- combat timing preserves intended wall-clock behavior
- projectile physics is validated
- AI frame-rate dependencies are audited
- camera smoothing is mathematically consistent
- networking remains 60 Hz
- dedicated server remains authoritative
- replay remains deterministic
- no host advantage is introduced
- all seven alt forms have been evaluated together
- intentional Project Prime alt enhancements are explicitly documented
- no arbitrary correction layer is being used to hide a known FPS-conversion error

---

# Final Execution Order

```text
FPS Audit Infrastructure
        ↓
Physics Shadow Harness
        ↓
Biped Traction / Damping
        ↓
Gravity / Integration
        ↓
Collision / Slopes
        ↓
Jump Pads / Knockback
        ↓
Weapons / Gameplay Timing
        ↓
Projectiles / Gameplay Entities
        ↓
AI
        ↓
Camera / Presentation
        ↓
Network / Replay Regression
        ↓
Full FPS Audit Cleanup
        ↓
ALL ALT FORMS
        ↓
Project Prime Enhancements Reconciliation
        ↓
Final Native-Parity Validation
```

The key rule for this entire project is:

> **Fix the shared 30 Hz → 60 Hz simulation foundation first. Evaluate and tune every alt form only after that foundation is stable.**