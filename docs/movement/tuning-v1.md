# Momentum profile v1 — experimental opt-in

This immutable profile is selected by the authoritative Momentum match rule;
Classic/Native remains the default. Its identity is `MomentumProfileVersion = 1`.
Final acceptance still requires the PR-09 device, network and performance gates.
Because recordings can carry this profile,
coefficient changes require a new profile identity and historical decoding.

## Calibration and numeric settings

Seven separate stock Native fixtures support the same biped coefficients for
Samus, Kanden, Spire, Noxus, Trace, Sylux and Weavel. There is no evidence here for
different per-Hunter multipliers. Fixed-point table values are divided by 4096;
native `Speed` converts to world units/second by multiplying by 30.

| Measurement | Every stock Hunter |
| --- | --- |
| Walk / strafe cap | `983 / 4096 * 30` = 7.199707 units/s |
| Native speed after forward ticks 1 / 2 / 3 | 0.10986329 / 0.21312833 / 0.23999026 native units |
| First sample at ≥95% ground cap | tick 3, 50 ms |
| Neutral ground velocity retained per tick | 0.83996582 |
| Release below 10% initial speed | 14 ticks, 233 ms |
| Jump rise above settled floor | 2.4658203 units |
| Jump apex sample / first grounded landing | tick 32 / 65, 533 ms / 1.083 s |
| Maximum sampled vertical speed | 0.2998047 native units |

These measurements come from PR-00's committed seven-Hunter numeric fixtures.
PR-03 remeasures each Hunter's jump in genuine offline input/collision scenes.
They describe stock spawn behavior, not every ability or surface.

| V1 coefficient | Value / origin |
| --- | --- |
| Normal ground speed | `983 / 4096 * 30` |
| Ground acceleration rate | `450 * 60 / 983` = approximately 27.46694/s |
| Ground friction | `(1 - 2785 / 4096) * 30` = 9.60205078125/s |
| Stop-speed floor | 0; no additional unmeasured braking |
| Air acceleration rate | same calibrated rate as ground |
| Air wish projection cap | `groundSpeed * (1 - 3481 / 4096)` = approximately 1.081011 units/s |
| Regular retention ceiling | `groundSpeed * 1.5` = approximately 10.799561 units/s |

The first five movement rates preserve PR-02's native-derived scale. Ground
friction reproduces native neutral-ground decay. Partial analog magnitude scales
wish speed before acceleration; equal radial cardinal/diagonal magnitudes produce
equal ground targets. The air projection cap is separate from total speed: air
steering can add perpendicular momentum. With this acceleration rate the projected
cap can be reached in one tick from rest; that is explicit, not hidden by a total
velocity clamp.

The **1.5 multiplier is an initial private safety budget**, not a measurement that
combat or every map tolerates 50% faster movement. Sustained perpendicular air-wish
stress must activate it without NaN, allocation or cumulative rounding growth.
Ordinary solver-only motion cannot create velocity above it. Actual combat,
map traversal, maximum-speed sweeps, hit claims and balance remain release gates;
revise/version the candidate if those trials reject it.

## Jump and landing policy

Auto-hop is **off**. Jump buffering, added coyote/landing grace and queued jumps
are **off**. A held button never manufactures a fresh native press. The adapter
does not grant jumps, alter `UsedJump`, reset abilities, or set vertical velocity.
It receives a boolean only after the existing native biped jump branch accepts a
press. Native collision clears `UsedJump` on qualifying solid contact.

The contact model derives four phases from current native `Standing`,
`StandingPrevious`, vertical velocity and accepted jump, without persistent state:

| Phase | Meaning | Ground friction |
| --- | --- | --- |
| LegalJump | native jump branch accepted this tick | skip |
| Airborne | no standing contact | skip |
| Landing | standing, no previous standing contact | apply once |
| Grounded | continuing standing contact | apply once |

Native slope collision can produce positive vertical velocity while standing.
That tangent motion retains ground friction; it does not imply a jump. An
accepted native jump still takes precedence, and active external impulses bypass
the adapter. PR-07's authored ramp regression covers this distinction.

Horizontal processing precedes native movement collision. A press before landing
is an airborne press; holding it across landing does not retry it. A new press
after collision has rearmed the native jump can skip ground friction on takeoff.
A grounded tick without an accepted jump receives normal friction. No new state
needs resetting or adding to checkpoints for this policy.

The dormant native Space Jump ability is preserved: a grounded first jump leaves
`UsedJump` clear, one released/repressed airborne jump consumes it, and a third
airborne press is refused until native landing rearms it. Stock spawn does not
grant this ability; tests explicitly set only its ability bit and label the case
synthetic. They compare native and Momentum Y, flags and accepted impulse ticks.

## Impulses and numerical bounds

Active native pad, acceleration, launch, carrier, slippery/hazard, form and other
special owners bypass the adapter. For residual incoming speed already above the
regular ceiling, the solver may steer within that incoming magnitude, but cannot
add more magnitude. It does not instantly flatten an external trajectory. The
`IncomingOverCap` diagnostic is a kinematic observation, not proof of impulse
provenance or an anti-cheat decision. PR-04 must verify genuine impulse events.

The cap uses double intermediate squares and scales only planar velocity. Float
conversion rounds inward, preventing a tiny cap overshoot from becoming a larger
incoming budget on each subsequent air turn. Y bits are preserved. Stress checks
exercise both direct physical units and the native ×30 / ÷30 round trip without
widening tolerances.

## Evidence and limits

`--momentum-profile` tests the contact policy, normal landing versus takeoff,
analog magnitudes, gradual reversal, cumulative air gain, cap activation, residual
overspeed, fixed-step host schedules and warmed zero-allocation profile calls.
`--momentum-jump-scene` exercises all seven Hunters with actual offline keyboard
edges, native spawn, jump, collision and landing. CPU host schedules at
30/60/90/120/144/240 Hz still execute the same 60-Hz simulation; they are not a
real high-refresh rendering test. See [PR-03 results](pr03-results.md) for commands,
counts, numeric artifacts, CI state and remaining acceptance.

Physical mouse/controller/touch, bot navigation, competitive combat, network
impairments and real device/render parity are unverified in this slice. Neither
solver-only timing nor this calibration certifies the whole-scene +5% p95 budget.
