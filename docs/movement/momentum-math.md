# PR-01 horizontal math contract

`Mods/Movement/MomentumMath.cs` contains internal immutable value contracts and a
pure velocity step. It has **no gameplay call site**. Classic remains the only
runtime movement selection in this slice, with no protocol/schema/dependency
change. The online movement model and immutable tuned v1 profile are later work.

`MomentumInput` supplies signed strafe/forward axes, a planar basis, full-deflection
desired speed, grounded state and simulation seconds. `MomentumTuning` supplies
ground acceleration, friction, stop speed, air acceleration and air projected
wish-speed cap. These are math coefficients, not a selected game profile. Caller
values use consistent spatial units/second; acceleration and friction rates are
per second. The adapter must convert the legacy native `Speed` representation
(30 times `Speed` is units/second) and pass `(float)FrameTiming.StepSeconds` from
the fixed 60-Hz update. The pure solver accepts other finite nonnegative dt values
for tests; it never reads a clock or render delta.

Axes clamp radially to magnitude one while preserving smaller magnitudes. The
forward/right vectors project onto X/Z and normalize independently. Their
normalized dot product must have absolute value at most `1e-4`; malformed/skew
bases are rejected rather than silently distorting analog movement. Projected
Y components are ignored after finiteness validation. The returned velocity's Y
is preserved exactly, including negative zero.

For normalized wish direction `w` and full input speed `s`, desired speed is
`s * min(1, length(axes))`. Ground friction first removes
`max(planarSpeed, stopSpeed) * friction * dt` without reversing direction.
Acceleration then adds along `w` by the smaller of available projected speed and
`acceleration * desiredSpeed * dt`. Ground target is desired speed; air target is
`min(desiredSpeed, airWishSpeedCap)`. The air cap does **not** replace desired
speed in the acceleration term or clamp the total velocity vector. This permits
air strafing while preserving perpendicular and existing above-target momentum.

Neutral ground input decays through friction. Neutral air input preserves planar
velocity. Opposing input gradually decelerates/reverses; it does not snap to a new
direction. Ground friction can decay external momentum; a future adapter must
classify special states/impulses before applying ordinary steering. There is no
arbitrary total velocity ceiling in this library.

Finite nonnegative scalar coefficients (including zero/default tuning) are valid.
Nonfinite vectors/axes/scalars, negative scalars, zero planar bases and
nonorthogonal bases throw an `ArgumentException` subtype before external mutation.
Finite extreme inputs use double intermediates to avoid squaring/product overflow;
if the returned X/Z cannot fit finite float components, the call rejects the
result. No unbounded loops, heap collections, LINQ, reflection, input polling,
Unity or scene/global state appear in the solver. Exceptions on invalid input are
outside its zero-allocation valid-step contract.

## Deliberately deferred behavior

Legal jumps, buffering, auto-hop, landing friction bypass, Space Jump, Hunter tuning,
slippery terrain, impulse classification, collision and bhop safety caps belong to
the adapter/later slices. The solver neither triggers jumps nor integrates position
or gravity. An adapter must select a single horizontal owner and preserve the
audited Native order, including newly merged private Trooper wall-cling fences.

## Verification scope

`--momentum-math` tests dimensionless hand-computed equations, counter-strafing,
air projection versus accumulated perpendicular velocity, 25/50/75/100% analog
input, radial/asymmetric diagonals, planar basis normalization, zero dt/rates,
invalid/extreme inputs, spatial-unit scaling and repeated fixed-step results at
30/60/90/120/144/240-Hz synthetic host cadences. Float equation tolerance is
absolute `2e-5`, relative zero; exact assertions cover vertical bits and identical
fixed-step sequences. A 1-nanosecond terminal boundary allowance compensates
floating-point summation of host-frame durations in the synthetic cadence fixture;
it does not alter production timing or feed a variable dt to the solver.

`--momentum-benchmark [json]` warms 122,880 player steps, then measures 819,200
steps in 200 batches of 512 ticks × eight scalar player values. Precomputed wish
reversals and ground/air phases exercise the same pure step. Arrays and output
serialization lie outside the measured interval. Thread allocation count must
remain zero. The CI benchmark disables tiered compilation to keep background
recompilation outside the timing comparison; normal equation tests use runtime
defaults. Average/p95/p99 are **normalized batch-time statistics**, not
individual-step tail latency or full-scene cost. CI repeats math/allocation checks
on Windows, Linux and macOS; machine-specific timings are artifacts, with no
absolute throughput threshold or claim of integrated performance acceptance.

See [PR-01 results](pr01-results.md) for actual runs and remaining gates, and
[reference attribution](reference-attribution.md) for the independent implementation policy.
