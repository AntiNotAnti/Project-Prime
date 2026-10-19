# CW-04e bounded custom launch and payment contract

`CustomLaunchPayments` is a dormant authority-thread state machine for one complete
owner life. It has no scene hook, live transport, health mutation or native ledger
caller. CW-04d's live custom Spawn/damage refusal remains intact. Supplying internally
consistent accepted-launch metadata does not prove source admission or trade safety.

A future scene authority adapter owns these stores, keeps old-life stores while
admitted flights/outcomes remain valid, and retires them on occupant/epoch replacement.
Respawn alone does not invalidate an admitted flight. That scene-level lifecycle
container and its total budget are still required; this module does not claim them.

The constructor pins full owner match/epoch/slot/generation/life and the complete
frozen manifest/registry mapping. Launch registration requires the exact registry
definition object, paired source/action/resource transition, bounded immutable source
observation/target age, and strictly increasing universal custom action sequence.
A permanent high-water sequence fence prevents recreating an expired, no-spawn or
capacity-refused action. The store does not infer permission from source timestamps.

Storage is fixed at construction: 128 entries by default, explicitly bounded 1–1,024.
Each entry reserves its inline terminal outcome before contact. Unexpired entries
are never evicted. Capacity preflight allocates nothing. The eventual caller must
reserve this capacity and reliable typed delivery capacity before canonical effects;
no reliable transport queue is implemented here. Source allocation occurs only after
metadata validation and a free entry are established.

A returned immutable `CustomShotSource` is an entry capability by object identity.
Equal metadata or a record clone cannot acquire its lease. Opaque leases bind store,
entry and monotonically increasing tokens, preventing reuse after expiry/replacement.
Default, foreign, retired and stale leases cannot mutate another entry.

The contact key carries complete `CustomActionIdentity`, victim slot/generation/life,
component 1 and explicit body/halfturret channel. Headshot and impact point are outcome
classification, not another payment key. The schema-1 precision component has one
terminal contact across all targets/channels. Protected/friendly/invulnerable contacts
with zero actual removal still consume it. Exact duplicates retrieve the immutable
first outcome; changing point/headshot cannot replace that outcome. A changed target
or channel cannot revive a consumed component.

Catch-up can contact during emission. A later emitted confirmation preserves an
already reserved/terminal contact. No-spawn completion is allowed only before any
contact; it cannot authorize a refund after effects. Contact abandonment or invalid
completion faults the entry, rather than releasing a retryable reservation. An
uncertain emitter return closes admission for new launches while independently
admitted flights remain payable. A pending emitter deadline is checked even after a
terminal catch-up contact, preserving a paid or zero-effect outcome during quarantine.
If outcome retention expires before a long-lived emitter deadline, eviction first
quarantines the still-pending emission; an expired receipt cannot silently reopen
launch capacity.

A contact result supplies canonical body/turret health before and after; the adapter
must capture the before values **before** turret splitting. The store computes actual
removal, forbids increases/negative health, requires a nonzero canonical damage event
for positive removal, and requires event zero for no removal. Lethal is derived from
actual body removal ending at zero. This module neither allocates canonical damage
IDs nor verifies an engine actor; the future canonical adapter must do both and must
not route custom results through native ACK/replay/history producers.

The authority adapter advances the explicit clock each tick. Flight time is bounded
by mechanics lifetime plus two ticks from emission admission. A completed/no-spawn/
faulted entry is retained for 256 ticks inclusive, then reusable. Expired flights
cannot contact; incomplete emitter/contact execution becomes uncertain. All time
checks use serial arithmetic across uint wrap. Permanent per-life sequence fencing
survives storage expiry. Retiring/disposal clears retained references and invalidates
escaped leases.

Validation exercises the real state-machine and immutable metadata, with explicit
fixture certificates and health observations. It does not establish collision,
canonical HP mutation, authenticated ingress, death/trade ordering, reliable outcome
presentation, reconnect or multiplayer fairness. Those remain CW-04 gates before
CW-05.
