# Quake III advanced movement and missile jumps

Profile v2 extends the existing selectable Quake III rule. Classic remains the
default in normal Practice and multiplayer menus; Momentum v1 and Quake III v1
math are unchanged. See the [technique guide](quake3-movement.md).

## Runtime contract

Held Jump requests another native jump only on a new physical Standing contact.
It never uses the native Grounded grace period, adds a midair jump, or spends
Space Jump automatically. Fresh jump presses retain their original ability
handling. The accepted native jump still owns Y, sound, animation and flags;
the horizontal solver skips friction on that accepted tick. There is no new
persistent latch, queue, timer or replay appendix. Circle/strafe speed gain comes
from the existing projection solver and turning input, without a speed bonus.

An eligible Quake III biped's native self-Missile splash derives a bounded radial
impulse from finalized damage and explosion geometry before hit prediction.
The impulse adds to existing X/Y/Z velocity, including when already rising.
Self-damage, invulnerability, lethal outcomes, native weapon resources, contact
and line-of-sight handling remain in their original paths. Other weapons,
Classic, Momentum, alternate forms and special-state impulse ownership keep
their existing behavior. Full tuning units and bounds are in the technique guide.

Online movement still uses owner-reported positions. Authority combat validates
real emitted shot/component identity, witnessed explosion, life, geometry and
payment; the client cannot select its own blast center or authoritative impulse.
Only a finite bounded self-Missile candidate is admitted by the expanded claim
gate, then the authority replaces it with its independently derived vector.
The existing component payment ledger and exact self-event receipt settlement
remain responsible for duplicate prevention and authority-minus-prediction
impulse correction. No new trust in reported owner velocity is introduced.

## Protocol and recordings

Protocol 62 selects current Quake III profile v2 and refuses older live peers.
All packet IDs, widths and movement bits retain the protocol-61 values. Original
Quake III recordings identify profile v1 by protocol 60/61. The replay metadata
profile property derives from recorded protocol, without a new archive field.
Passive replay remains pose/damage presentation and never runs current movement
or blast physics. Checkpoint and timed-shot capability reviews explicitly
include 62 while retaining historical width handling.

## Verification

Windows validation is in progress on main
`2d18ac057` plus an independently hashed source overlay. Results and exact
source/binary/archive identities will be recorded here after the runs finish.
No Mac build, simulation, UI/focus interaction or performance run is performed.

The new automated routes are:

```text
--quake3-advanced-rules
--quake3-advanced-scene <assets> [json]
--quake3-rocket-scene <assets> [room]
```

The first route is asset-free and runs in the three-platform movement workflow.
Scene and rocket routes require the operator's extracted game data. Movement
proof uses actual keyboard/mouse input and native collisions. Combat seam tests
explicitly set incoming velocities and claim schedules; they do not masquerade
as physical input or live UDP blast tests. Real UDP and recordings have separate
source/binary leases.

## Limits

Human movement feel, physical devices, rendered UI/focus, broad high-speed map
qualification, surfing, crouch/hitbox changes and a replacement collision solver
are outside this slice's acceptance. Existing native slope, step and collision
responses still apply. No new performance-budget pass is claimed; optimization
remains deferred under the previously accepted tradeoff. Hosted CI status is
recorded separately from local Windows checks, and publication does not wait on
CI or review under the operator's instruction.
