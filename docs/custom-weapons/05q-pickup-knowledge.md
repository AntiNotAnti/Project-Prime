# CW-05q — private pickup knowledge and source fence

This foundation prevents a delayed input from spending resources that were acquired
after the input was authored. It does not yet change live packets, activate online
pickups or remove the remaining online spawn grant. Offline public acquisition
continues through world contact; menus cannot grant the rifle.

A numeric next-source boundary alone is insufficient. An input carrying source102
can arrive after source101 collected an item at world120 while still having been
authored before the pickup. The authority must require private knowledge of the
funded inventory and retain the stamp captured with the first accepted native
input. A duplicate source cannot replace its stamp or original ingress time.

`CustomResourceKnowledge` contains a resource revision and a private128-bit
cryptographic nonce, with an exact24-byte little-endian codec. Empty knowledge is
all zero; partial values refuse decoding/writing. Nonce creation does not consume
the gameplay RNG, and diagnostics redact it. The authority creates knowledge only
from a complete, matching ledger outcome and funded state. Exact publication
retries preserve the nonce and established boundary. A newer resource grant
rotates knowledge; pending actions with older knowledge refuse conservatively.

`CustomPickupKnowledgeFence` belongs to one owner life, authored weapon and pickup
world. Accepted-input capture, control echo and action inspection also require the
authenticated connection and complete native bootstrap. The bounded128-entry
history records every accepted source, including empty knowledge. The first
matching echo freezes the prior source frontier before inserting the new input.
Admission requires a strictly newer retained source carrying that exact knowledge,
source/world funding floors, immutable ingress-world evidence and a Fire ACK at or
after both acquisition and the current native bootstrap. Existing clock, age,
pose, resource, payment and trade gates remain mandatory.

A trusted native readiness transition can rebind the same life to a new bootstrap
or authenticated connection. It preserves funded revision, original grant world
and effective source while rotating private knowledge and clearing source/echo
history. Source/world anchors cannot regress. An exact same-context retry does
not rotate, rewind or clear history. A retired fence cannot be revived. Rebind is
an authority API, never a client-requested mutation, and does not recreate or
invalidate native bootstrap state to process a pickup.

Before activation, ingress must capture only genuinely accepted native inputs
before custom consumption; client stamps must latch at original input/control
creation rather than send/retry time. Exact publication payloads must be reserved
before funding and retained across rotations. Unknown or obsolete action knowledge
must settle terminally rather than block the FIFO. Private nonces must never enter
public item baselines or spectator replicas. These integration requirements, empty
private inventory state and reliable world item delivery remain the next slice.
No live protocol or replay shape changes here; live protocol remains71.
