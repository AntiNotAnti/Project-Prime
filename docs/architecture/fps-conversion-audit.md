# FPS Conversion Audit

Snapshot: Project Prime `main` at `f9aa9bb6702aef06d873f0a3609fffa994805a46`.

This audit treats the original MPH movement state as a 30 Hz discrete simulation and Project Prime as a 60 Hz external simulation. The goal is not to remove the 60 Hz architecture. The goal is to find places where two 60 Hz updates do not reproduce one native 30 Hz state transition.

## Tooling

Run:

```sh
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -fpsconvertaudit
```

Optional TSV output:

```sh
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -fpsconvertaudit -fpsconvertauditout artifacts/fps-conversion-audit.tsv
```

The command scans every C# `todo: FPS stuff` / `sktodo: FPS stuff` marker, assigns a category, risk, and priority, and never rewrites source.


### Movement shadow foundation

The audit now runs the content-free movement shadow contract checks before scanning source. These checks keep the conversion math executable instead of leaving it only in this document:

- `F60 = sqrt(F30)` composes to the native 30 Hz multiplier after two 60 Hz substeps.
- `A60 = 1 - sqrt(1 - A30)` composes to the native interpolation coefficient.
- Coupled impulse+damping reference math composes exactly for the simplified `(v + T) * F` recurrence; production traction still requires the live shadow harness before adoption.
- The current gravity pair is checked to preserve boundary velocity while exposing the known quarter-gravity position error.
- `MovementBoundarySnapshot` and `MovementShadowComparer` define the stable 30 Hz comparison contract for position, velocity, facing, gravity, standing/contact state and `SpireClimbing`.
- `MovementShadowAccumulator` retains only the first meaningful divergence and maxima so long runs stay bounded.

This foundation intentionally changes no production movement. The next slice supplies Samus/Spire reference states and live 30 Hz-boundary observations to this contract.


The live sampler is now opt-in with:

```sh
ProjectPrime -movementshadow
ProjectPrime -movementshadow -movementshadowout artifacts/movement-shadow.tsv
```

It observes only Samus and Spire in this first stage, after the production movement/collision step, and records only even 60 Hz simulation frames (the equivalent native 30 Hz boundaries). With no output path it prints a low-rate summary; with `-movementshadowout` it writes buffered TSV evidence. The sampler does not alter movement state. Contact normal/pushout columns are reserved until the collision-shadow slice populates them.

`NativeMovementReference` now provides the first collision-free 30 Hz reference step for horizontal impulse/cap/damping, gravity, semi-implicit position integration and facing convergence. It is not yet authoritative and is not wired into production movement.


### Samus/Spire live reference comparison

The next shadow layer now feeds the actual native-scale parameters observed during each production 60 Hz substep into the 30 Hz reference:

- horizontal `speedDelta` before production applies it;
- the current horizontal speed cap;
- the selected native damping multiplier before Project Prime's current half-step approximation;
- gravity before the current `g / 2` update.

A comparison is emitted only when both 60 Hz substeps used the same movement parameters. Windows are skipped when form state changes, alt form/Spire climbing is active, jump-pad/biped-lock behavior is involved, lateral collision occurs, or standing/contact identity changes. Those skips are counted by reason rather than being treated as parity failures.

Two initial comparison domains are reported:

- `ground-horizontal`: compares native-vs-production X/Z traction, speed-cap and damping while retaining production vertical/facing state until collision shadowing exists.
- `air-kinematic`: compares full position/velocity including gravity and semi-implicit integration when no contact transition is observed.

When `-movementshadowout` is used, raw production boundaries remain in the requested TSV and comparison deltas are written to `<path>.compare.tsv`. Shutdown prints bounded per-slot/domain maxima, first-divergence counts, and skip totals.


### Collision evidence capture

The shadow now records the strongest collision correction observed across each two-substep native window:

- normalized winning contact plane;
- requested pushout depth;
- bounded initial-overlap recovery corrections;
- ordinary biped/alt contact corrections;
- Spire wall-climb pushout corrections.

This is still evidence, not a reconstructed native collision solver. `ground-horizontal` masks the recorded contact fields when evaluating traction/damping so known collision behavior cannot create a false movement failure. `air-kinematic` skips a window when a meaningful contact correction occurred. Raw production TSV output retains the contact normal and pushout for the upcoming slope/corner/Spire collision-reference slice.

## P0 inventory

The five player files currently contain **179** marked sites:

| File | Markers |
|---|---:|
| `PlayerInput.cs` | 64 |
| `PlayerCollision.cs` | 10 |
| `PlayerProcess.cs` | 54 |
| `PlayerCamera.cs` | 24 |
| `PlayerEntity.cs` | 27 |

The older rough count of 178 is now stale because `PlayerProcess.cs` contains 54 marked sites.

## Confirmed mathematical mismatches

### 1. Horizontal damping uses a linear half-step instead of the exact half-step multiplier

Current:

```csharp
Vector3 speedMul = Speed.WithX(Speed.X * speedFactor).WithZ(Speed.Z * speedFactor);
Speed += (speedMul - Speed) / 2;
```

If the native 30 Hz multiplier is `F`, the current 60 Hz multiplier is `(1 + F) / 2`. Two updates therefore apply:

```text
((1 + F) / 2)^2
```

instead of `F`.

The exact half-step multiplier is:

```text
F60 = sqrt(F30)
```

Examples using current player constants:

| State | Native F30 | Current pair | Relative extra retention |
|---|---:|---:|---:|
| Samus alt ground | 0.964844 | 0.965153 | +0.032% |
| Spire alt ground | 0.979980 | 0.980081 | +0.010% |
| Air | 0.849854 | 0.855490 | +0.663% |
| Walk | 0.879883 | 0.883490 | +0.410% |
| Stand | 0.679932 | 0.705543 | +3.767% |

The alt-ground difference is small, but air and stand damping are large enough to alter transitions and stopping feel.

### 2. Gravity velocity matches, but gravity position integration does not

Current 60 Hz pair:

```csharp
Speed = Speed.AddY(_gravity / 2);
Position = Position + Speed / 2;
```

For native semi-implicit 30 Hz integration:

```text
v1 = v0 + g
p1 = p0 + v1
```

two current half-steps produce:

```text
v2 = v0 + g
p2 = p0 + v0 + 0.75g
```

Velocity is correct at the 30 Hz boundary. Position is not.

Samus and Spire use alt-air gravity `-245 / 4096 = -0.05981445`. From rest, each equivalent 30 Hz interval is about **0.0149536 world units too high**. With no collision correction, the offset accumulates to about **0.4486 units after one second**.

This is a prime candidate for altered ledge contact, slope entry, wall contact lifetime, jump arcs, and Spire climb transitions.

### 3. View-tilt damping runs too fast

Current:

```csharp
_viewTiltAngleH *= 0.9f;
_viewTiltAngleV *= 0.9f;
```

At 60 Hz, two updates produce `0.81` of the previous value. If native behavior is `0.9` per 30 Hz tick, the exact 60 Hz multiplier is:

```text
sqrt(0.9) = 0.9486833
```

### 4. Facing interpolation is slower than native

Current:

```csharp
_facingVector += diff * 0.3f / 2;
```

Two 0.15 lerps produce an effective 30 Hz coefficient of `0.2775`, not `0.3`.

The exact half-step coefficient is:

```text
A60 = 1 - sqrt(1 - A30)
A60(0.3) = 0.16333997
```

This is a small per-frame difference but directly affects facing convergence.

## High-risk conversions requiring shadow validation

### 5. Player traction is applied at full strength on every 60 Hz simulation tick

Both biped and rolling-alt input accumulate native traction into `speedDelta`, then execute:

```csharp
Speed += speedDelta;
```

There is no general half-rate conversion on the traction term.

Because the movement function runs every 60 Hz simulation frame, a held input can inject roughly two native traction impulses per original 30 Hz interval before damping/capping. Speed caps hide part of the effect, but acceleration time and collision approach velocity can still differ significantly.

Do **not** blindly divide traction by two. Traction and damping are coupled. If the native step is approximately `(v + T) * F`, the exact two-half-step input coefficient depends on `F`. This should be validated in the native movement shadow harness.

### 6. Collision response is not decomposable with simple /2 constants

`PlayerCollision.cs` contains explicit 30 Hz emulation hacks for slope/ceiling response and a wall-climb stickiness TODO.

Spire climbing also performs order-dependent operations such as:

```csharp
Position += result.Plane.Xyz * dot;
Speed += vec / 2;
Speed = Speed.AddY(4 * dot * yFactor / 2);
```

The penetration depth `dot` changes after the first 60 Hz collision solve, so applying a half-strength velocity response twice is not equivalent to one 30 Hz response. Full penetration pushout also occurs twice as often.

This is why the Spire ledge assist should not be removed until native-cadence collision behavior is measured.

### 7. Third-person camera speed input is probably over-divided

`PlayerCamera.cs` currently calculates:

```csharp
float speedMagSqr = (Speed / 2).LengthSquared;
```

and later also halves the camera response increment.

If `Speed` remains stored in native 30 Hz units, dividing before squaring reduces the magnitude to one quarter, then the response increment is halved again. That can reduce the native-equivalent camera collision/orbit response to roughly one eighth before clamping effects.

This should be checked against the native camera update before correction.

### 8. Camera smoothing coefficients need exact half-step conversion

Several camera paths apply a native-looking interpolation coefficient every 60 Hz frame without conversion, while others use a naive `/ 2`. Both forms can be wrong for exponential convergence.

Use:

```text
A60 = 1 - sqrt(1 - A30)
```

when the native operation is `x += (target - x) * A30`.

## Conversions that are probably already correct

Do not churn these without evidence:

- frame timers converted from `N` to `N * 2`
- cooldown/startup/invulnerability durations converted to twice the frame count
- animation logic deliberately advanced every other 60 Hz simulation frame
- constant linear per-frame increments divided by two
- constant linear cap decay divided by two
- absolute speed caps and jump impulse values that keep the velocity state in native 30 Hz units

The important distinction is **state unit** versus **per-frame operation**. A native velocity value does not automatically need to be halved merely because position is integrated twice as often.

## Recommended implementation order

1. Keep the 60 Hz simulation, networking, replay frame numbers, lag compensation, and protocol unchanged.
2. Use `-fpsconvertaudit` to inventory all marked sites and maintain the P0/P1 queue.
3. Add deterministic math checks for exact half-step damping and interpolation.
4. Build the Samus/Spire native movement shadow harness before changing traction or collision.
5. Log native-vs-current position, velocity, damping, gravity, collision plane/pushout, standing, facing, and `SpireClimbing` at equivalent 30 Hz boundaries.
6. Correct isolated proven conversions first, then move traction/collision to a reconstructed native movement kernel once parity is measurable.
7. Keep the recent Samus modern swipe capture and Spire flick/ledge quality-of-life behavior as input/presentation layers over the native kernel.
