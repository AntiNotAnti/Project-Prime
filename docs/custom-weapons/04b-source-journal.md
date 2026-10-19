# CW-04b ordered source-time resources

This dormant journal is the second CW-04 slice. It does not enable multiplayer or
complete CW-04. It accepts trusted engine certificates through interfaces whose
historical pose, geometry, modifiers, bootstrap/readiness and canonical kill-trade
implementations remain mandatory integration work. Test certificates are stubs,
not proof of network authority.

## Ownership and ordering

One journal owns an immutable initial custom loadout for a complete owner life.
Its frozen registry must resolve the exact manifest. Selection starts empty;
registered definitions alone do not grant ownership. Mid-life grants are not yet
supported. The owning session must retain one `CustomSourceClock` per occupant,
including across deaths and reconnects of that occupant. Reconstructing a clock
must never become a same-life refill mechanism. Clock binding permits each strictly
newer life once and validates configuration before consuming that binding.

The fixed clock anchor admits accepted source observations at most 32 ticks ahead
of elapsed authority time. Claimed action timestamps cannot advance that clock or
the source frontier. Source observations require the journal's exact owner life;
the retained occupant clock can also validate old-life evidence independently.

All custom action kinds and owned weapons share one contiguous per-life sequence,
starting at 1. Reordered actions wait for the missing predecessor. A denial settles
and burns its sequence; a changed weapon, kind or timestamp cannot revive it.
Identical retries reuse immutable receipts. After receipt eviction, the settled
high-water still prevents execution. Sequences do not wrap within a life.

Each action requires an exact retained source permission and a verified certificate.
The certificate must reproduce the full action, agree with that permission, and
have bounded non-future observation/target world time. Fire also checks finite
body position, approximate unit aim, .99 aim agreement and a three-unit muzzle
radius around the historical body plus half-unit eye offset. These structural
checks supplement the future trusted geometry/history adapter; they cannot replace
it. Source frame, authority observation time and opponent target time remain distinct.

## Resources and presentation

Committed resources advance only when an ordered, admitted action executes. A
separate pure `WeaponRuntimeState.Preview` projects reload completion and bolt phase
without transferring committed ammunition, advancing admission time or changing
request identities. A future HUD preview therefore cannot authorize a delayed
fire during its historical reload.

Trusted form interruptions cancel selection's reload and scope at their original
source time before later actions. The earliest unapplied interruption survives
permission-ring eviction. Preview reflects it without mutating committed state.
The first permission for an exact source frame is immutable; contradictory trusted
repeats retire the life. A novel restriction predating already committed history retires the
life, even outside the retained permission window, rather than rewriting receipts.

Fire must match the ordered optic state and pass emitter capacity before its debit.
The immutable launch includes definition, exact certificate and before/after
snapshots. A definite `NoSpawn` refunds only that fire's ammunition and cooldown;
legitimate source-time reload completion remains committed. The failed request ID
stays burned. Uncertain or throwing emission retains the possible debit and retires
the life. The emitter must report `NoSpawn` only when it guarantees that no projectile,
collision, damage or other gameplay effect occurred.

## Bounded work and integration requirements

| Bound | Value |
| --- | --- |
| Initial owned custom weapons | At most 64 |
| Reordered pending actions | 16 |
| Offers per authority tick | 8 |
| Admission attempts per authority tick, including pending | 4 |
| Exact source permissions | 128 slots |
| Maximum action source age / certificate world age | 64 ticks each |
| Missing-action wait | Through 64 ticks; retire after |
| Immutable terminal receipts | 128, retained through 128 authority ticks |

Offer backpressure is transient and does not settle a sequence. Ingress must retain
and retry the action within its own bounded queue. Sequence-window overflow, gap
timeout and terminal saturation retire instead of skipping ordered work or evicting
live receipts. Retired journals reject new work and invalidate resources; retained
terminal facts remain readable. Pending work and state are private fixed arrays.
Engine callbacks cannot recursively process or enqueue journal work.

The remaining CW-04 adapter must supply actual accepted input/history and loss
recovery, exact manifest/bootstrap readiness, authenticated ingress, canonical trade
eligibility, indexed custom payment and explicit rewind/private-pool context. It
must preserve legitimate admitted flights across lifecycle transitions and bound
retained old-life journals. Native/custom keys must retain separate domains through
every shared join. Typed results, snapshots and real transport tests remain pending.
No live packet registration, protocol change or capability advertisement occurs here.
