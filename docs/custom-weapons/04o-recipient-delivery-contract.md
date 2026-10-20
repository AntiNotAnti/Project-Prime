# CW-04o: recipient protocol and bounded delivery owners

This slice defines protocol66 types94–96: reliable Background replica pages,
replica events and application ACKs. It implements pure recipient-owned delivery
and consumption. It does not yet register game-session handlers, activate
spectator readiness, mutate native snapshots or render effects. Those adapters
must satisfy this contract before CW-04 is complete.

## Identity and wire layout

The66-byte outer header contains version1, the actual RECIPIENT bootstrap,
frozen M7 manifest digest and recipient slot. A viewer may be occupied Life0;
no WeaponOwnerContext is manufactured for viewing. Every nested source launch
retains the ORIGINAL109-byte source action and bootstrap, its certificate,
authority launch world and actual muzzle/direction. Source and viewer must share
match/epoch/start and manifest, while bootstrap revision, generation, slot and
life remain independent. Old source lives can coexist with current replacement
life rows. Contacts retain the original source bootstrap, never a reconstruction
from the recipient or current source peer. Inner and outer event tokens match.

Baseline prefix88: outer66, baseline ID8, published-event frontier8, authority
world4, total flights1, page index1. Page0 contains exactly eight41-byte slot rows;
remaining pages contain up to four219-byte flight rows. Total flights0–64 implies
exactly1–17 pages; no empty trailing pages or alternative shapes exist. The largest
page is964 bytes, within the1200-byte datagram budget including the native header
and reliable event ID. Rows distinguish absent, occupied, spectator and bound
custom state, with no invented state for absent/unbound/Life0 viewers. A bound
row includes the25-byte canonical resource preview and settled/committed metadata.

Flight rows contain171-byte launch provenance, original payment deadline, actual
spawn/current position and per-step velocity, age and remaining seconds. Initial
and duplicated actual muzzle fields must agree. Active M7 age/remaining are bounded
by its six-tick lifetime and sum to that lifetime within float precision. Existing
flights retain their original inclusive payment deadline, including uint wrap.
Outer baseline/event/state worlds cannot precede recipient admission; inner
baseline source/launch times may predate the late join.

Event prefix79: outer66, kind1, event token8, authority world4. Launch and contact
payloads are171 and116 bytes. Current-state kind3 instead uses the eight-byte field
as a NONZERO independent state revision and carries eight41-byte rows. Revision
is separate from world time: roster/join/spectator changes can occur in one world.
Older revisions cannot roll back presentation; contradictory same revisions retire
the consumer. State batches never acknowledge or consume launch/contact tokens.

ACK115 contains outer66, baseline/event kind1, ID/token8, baseline frontier8 and
SHA25632. Event frontier is zero. The server separately verifies the actual endpoint
connection. Baseline ACK requires all pages to have been sent and the exact
ordered whole-baseline digest. Event ACK requires exact sent token and payload
digest. Transport delivery alone never consumes an application obligation.

## Ownership and bounds

A mailbox owns256 action groups. Before a NEW shot can affect gameplay, every
participating observer must have a reserved launch/contact group. A joining viewer
must reserve eventual contacts for every captured active flight before publishing
its baseline. Baseline transfer does not refresh those flights' deadlines.
Reservations prepay nonwrapping event-token headroom; tokens are minted only when
facts publish, so a baseline frontier never covers an unpublished future contact.
Outstanding token span stays below the receiver's1024-entry contradiction window.
Capacity does not evict obligations.

A catch-up contact can occur before the emitter returns. It is staged in its
preexisting group; launch publication creates its prerequisite, and contact sends
only after application ACK of launch. No-spawn/unattempted work releases unused
observer capacity. Uncertain/unknown emission or contradictory publication closes
only this observer mailbox; it cannot refund ammo, cancel an original-owner
payment, destroy another source's flight or change native history. The live adapter
must preserve mandatory original-owner private contact capacity independently.

Pages and facts retry every0.25 seconds with immutable bytes. Baseline and action
acknowledgement deadlines are15 seconds from original creation, never refreshed by
duplicates. Misses release unused contact capacity only after the original payment
deadline, inclusively retaining the exact boundary. Closing a mailbox has no
reference to authority health, source clocks or private launch payments.

The receiver stages all pages in fixed storage, accepts arbitrary order, and
publishes no baseline until the complete validated set agrees. It checks unique
full flight identities, actual source occupancy/generation, nonfuture source life,
and exact recipient occupancy/generation. Equal duplicates are inert;
contradictions invalidate the instance without modifying previously returned value
copies. Current state is kept separate from the local action publisher. No replica
fact changes native HP, grants resources, enters native shot history or serves as
a source proof. Authenticated host/connection dispatch and actual game integration
remain required in the following slice.

`ReceiveEvent` returns an ACK candidate and a typed `LastEvent`; it does not send
anything. The following live adapter must reserve durable consumer capacity before
calling it for a new fact, copy/apply that fact synchronously, and only then send
the ACK. Withholding an ACK after a failed sink is insufficient because the
receiver has already recorded dedupe. If consumer commit unexpectedly fails, retire
the stream. The adapter also checks mailbox expiry before admission and pumping,
and validates trusted canonical receipts before changing observer obligations.
