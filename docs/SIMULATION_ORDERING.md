# Simulation ordering

The reference for this characterization is `b31bc57`. Classic keeps its existing
order; new competitive policies must be explicit. One authoritative tick is 1/60
second, independent of display refresh. This is a staged, ordered simulation,
not an unordered collection of simultaneous events.

## Tick boundary

`ServerSimulation.Step` reconciles membership, evaluates quorum/lifecycle and
resets inputs on a phase change before selecting input. Waiting and Countdown
never step the world. Losing quorum on the exact countdown deadline cancels the
start; the next eligible countdown performs another pristine reset.

In Playing, it selects at most one command per participant and enters the combat
context. `Scene.StepHeadlessFrame` evaluates the existing match completion rules
before processing entities. A completed score from the previous world step is
therefore considered before new damage or movement. A zero regulation clock does
not grant an additional world step in Classic. If a completed score goal and zero
clock coincide, the existing mode evaluation identifies the score-goal reason.

Entities run in ascending `EntityType`, with Room first and stable insertion order
within a type. Objective entities precede Player; Player precedes BeamProjectile.
Items are picked up during Player processing. Thus a health pickup may protect
against a later projectile-stage hit; it cannot resurrect a player who was
already dead before pickup processing. Valid lethal events from both participants
retain attribution even if the first event killed the second event's attacker.

After entity processing, AI visibility/decisions and standings are updated.
Queued messages are processed after the entity loop. Frame count and clocks are
updated at the final fence. Projectile catch-up drains after the ordinary world
step only while Playing; phase completion clears pending catch-up. Server
snapshots and historical colliders are captured from that completed state.

## Objectives and ties

A completed flag capture is not undone by a later death in the same step. The
capture releases its carrier before death, so death cannot drop the same flag
again. A flag still carried when the carrier dies follows the existing drop/reset
path. Prime changes and all accepted score/death mutations are incorporated in
the next terminal result. Exact tied standings remain tied; list ordering is not
reinterpreted as an extra kill or objective point.

Classic regulation expiry ends a contested objective without awarding an extra
frame. G3's explicit overtime policy will extend the period while staying in
Playing; it must not silently change Classic's boundary.

## Result and network fences

The authority captures one immutable `MatchResult`. Readiness, disconnects and
empty-server periods during Ending/Intermission do not rewrite it or reset the
phase deadlines. Input bundles from previous match/phase identities are rejected.
World assembly validates the complete identity/revision before applying state.
Reliable events keep their match identity across rotation and cannot act as new
match events.

## Verification

`nettest --simulation-order DATA` exercises actual AMHE1 multiplayer mutations
for mutual kills, last-two Survival deaths, health/damage ordering, simultaneous
scores at expiry, capture/death, Prime transfer, contested expiry, countdown loss
and single result capture. Fixture-only tail actions invoke real gameplay damage
and objective methods inside the world step; they do not model projectile flight.
Existing combat/catch-up harnesses cover that separate path.

`nettest --match-phases DATA` covers pristine countdown resets, team quorum, stale
input epochs, terminal readiness/departures (including all players leaving),
replicated rules and deadline-driven rotation. The socket lifecycle and world
validation tests cover old-match state and event ordering. These checks do not
constitute rendered-client or external Internet acceptance.
