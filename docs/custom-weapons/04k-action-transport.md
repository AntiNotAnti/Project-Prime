# CW-04k developer action/result transport

Protocol63 assigns86 Action,87 Receipt,88 Contact,89 ResultAck and90 State.
The separate internal readiness/combat flags still default off and have no public
activation. Every new packet uses ordinary reliable capacity and the background
inbox lane. Native payloads, old IDs, checkpoint versions and historical60/61/62
bodies retain their widths. Live62 clients refuse normally; discovery accepts
reviewed60/61/62/63 widths. This is a built-in-M7 developer lane, not package loading.

The server derives the sender from its admitted endpoint, verifies actual
ConnectionId, native scene/bootstrap readiness, current stream/generation/life,
exact manifest and runtime10. Ingress is bounded64 custom action/ACK offers per
peer per wall second, before codec work. Current nonspectating human participants
must all have exact capability evidence; bots and spectator rooms are unsupported
and close the new-action gate. The handler only reserves/queues; it never emits,
spends ammo, moves a player or applies health. Foreign/stale requests allocate no
result record. A blocked room requires an exact retry after readiness recovers.

A16-entry per-peer application outbox reserves a209-byte receipt buffer and,
for Fire,116-byte contact buffer before the action enters the journal. Full
capacity refuses admission without consuming sequence or touching resources.
There is no eviction. Duplicate exact actions retain their original result;
conflicting same-identity actions cannot replace it. An ACK has the full
bootstrap/manifest/slot/life/runtime/sequence, result kind and nonzero monotonic
64-bit token. It can release only an actually published/sent matching result.
Receipt ACK does not free a pending or unacknowledged contact. Counter exhaustion
closes the owner instead of wrapping. Old-life obligations survive normal respawn;
occupant/bootstrap/connection retirement closes their owner.

The existing all-player source phase freezes placement, controls and canonical
grant facts before processing bounded journals and then draining accepted native
projectiles. A new action cannot process against a source whose first observed
world equals the current world: that sampled geometry does not exist yet. This
comparison uses ObservedWorldFrame, never the independent client SourceFrame, and
never publishes an early geometry snapshot. Current owner-self-death reconciliation
has already run. Existing exact pose/modifier/resource/rewind/trade/payment gates
remain mandatory. Each fire has contact capacity before TryFire and catch-up
emission; later flights must still own that reservation before canonical contact.

Canonical contact writes one reserved outcome after real before/after health,
including protected terminal zero and mandatory trade-denied zero. Receipt and
contact may arrive in either order. Application records retry at most every.25
wall seconds and remain until exact application ACK. Native transport ACK is not
a verdict. A15-second unconsumed obligation disconnects that peer explicitly;
retries never reset the deadline. A definitely unspawned/denied fire releases its
unused contact buffer. A spawned miss retains that buffer through the existing
lifetime+2 payment window inclusive, then releases it when no contact can pay.
No exception or uncertain emission manufactures an ammo refund.

The113-byte State is a revisioned presentation projection, distinct from committed
source-time admission. It includes full owner, frozen runtime mapping, settled
sequence, selected weapon, committed source and bounded magazine/reserve/optic/
reload/cooldown snapshot. It repeats at most4Hz to recover application-order races
with capability/native life confirmation. Receipt209 and Contact116 retain exact
immutable facts; ResultAck83 acknowledges them. Parsers check exact lengths,
reserved bits/enums, absent-zero fields, identity joins, mechanic bounds, actual
round debit, finite poses and canonical damage arithmetic. Wrapped deadlines and
completed-reload retained deadlines are valid. Readers allocate no hash strings.

The actual client cache accepts results only from its existing authenticated host
and exact confirmed bootstrap/connection/manifest. It retains up to16 contiguous
intent requests and retries exact bytes until an application receipt. Native
transport ACK alone changes nothing. Source state advances by monotonic revision;
a newer native life retires old local pending intent without refilling authority
resources. Old-life results can still be acknowledged without rolling the current
presentation back. A128-token fixed digest window deduplicates result callbacks;
an observed token/payload contradiction permanently invalidates custom capability.
Client receipt/contact handling never calls TakeDamage, writes native damage
history or advances a native receipt/replay frontier. Native snapshots own health.

This slice does not complete CW-04. Results/state currently target the firing
participant; full recipient-bootstrap broadcast/spectator/reconnect state is
pending. Source history still contains only actual consumed inputs, and the fire
proof still requires the exact preceding release. Missing/coalesced source
recovery, complete local/bot parity and final host/remote/loss acceptance remain
separate gates. No custom replay support, public loadout/input/HUD flow or physical
platform/rendered acceptance is claimed. Ordinary matches create none of these
custom owners; the rollback is both developer flags off.
