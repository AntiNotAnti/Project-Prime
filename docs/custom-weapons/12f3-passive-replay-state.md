# CW-12f3 — detached passive custom replay state

This describes the source contract for `ReplayCustomWeaponState` and typed
restore points. Local desktop/dedicated qualification and ordinary-client typed
restore observations are recorded in the [CW-12f3 receipt](evidence/CW-12f3.md).
It does not enable custom recording, a Demo archive, native byte checkpoints,
passive scene presentation or archive reopen/seek. Existing custom replay
refusal remains in force until those later contracts are complete.

The state consumes validated `ReplayCustomWeaponFact` values under one exact
`ReplayCustomWeaponContext`: recorded bootstrap, package/definition identity
and pickup layout. It has no Scene, NetSession, input, RNG, collision, inventory
funding, damage application, sound or graphics ownership. Accepted contact
outcomes are retained facts, never instructions to pay health again. Package
archive/resource leases belong to the surrounding historical binding, not this
detached value owner.

`Accept(fact, recordingFrame)` requires Bootstrap sequence 1 followed by the
complete Baseline at sequence 2, then contiguous fact sequences. Recording frames
cannot move backward. The state retains eight slot rows, up to 64 pickup rows,
256 flight anchors and 1,024 action entries. A 1,024-entry sequence/digest window
accepts byte-identical retries as no-ops; conflicting or out-of-window retries,
gaps and reordered sequences fail closed. Semantic/budget failures inside
`Accept` set `Invalidated`; a closed owner refuses subsequent acceptance.

State-table world ticks cannot regress. Tables preserve accepted magazine,
reserve, optic, phase and selection values without independently advancing the
weapon state machine. Pickup facts must preserve world order, incarnation,
revision and ready-time consistency. Advancing the clock alone does not grant
ownership or simulate a new pickup outcome.

The initial baseline can contain at most 64 validated flights. Each must belong
to an occupied, non-spectating matching generation with compatible life history;
duplicate action identities are refused. Its source certificate, launch world,
spawn position and direction seed an action anchor. A later launch must agree
with that baseline launch. Repeated launches/contacts must agree with their
retained canonical source and outcome; contact-before-launch remains terminal
and cannot resurrect a flight. State-table generation retirement removes old
generation flights without rewriting accepted source identity.

`Advance(world, recordingFrame)` updates passive clocks and expires flights.
World progression uses wrap-aware tick comparison. Flight velocity is distance
per fixed tick: position is original anchor position plus velocity × elapsed
ticks; age/lifetime use elapsed ticks divided by 60. Restores retain the original
anchor rather than repeatedly interpolating a previous sample, avoiding
seek-order rounding drift. No new collision or accepted shot is generated.
Action/contact tombstones survive flight removal. Entries can be reclaimed only
without an active flight after the configured projectile lifetime plus two ticks;
exhaustion refuses rather than silently dropping required history.

`CompleteForRestore` requires a healthy baseline and every authored pickup row.
`CaptureRestorePoint()` and `Copy()` reject incomplete pickup state. A
`ReplayCustomWeaponRestorePoint` owns a private snapshot; `Restore(context)`
requires the exact context digest and returns a fresh owner. Slot/pickup tables,
flight/action anchors, duplicate window, clocks and sequence are copied. An
equivalent fresh context is permitted; a different package/layout scope is not.
Mutating the returned owner cannot mutate the stored restore point.

`TrySlot`, `TryPickup` and `TryContact` expose passive values. `CopyFlights`
requires a 256-row destination and returns stable ordering by source slot,
generation, life and action sequence. A typed restore followed by the same
accepted suffix should reproduce exact tables and sorted flights at the same
final recording frame/world. This is a detached-state invariant, not evidence
that a persisted replay file can yet restore a whole scene. Native archive,
replica-checkpoint and world-checkpoint formats remain outside this slice.
