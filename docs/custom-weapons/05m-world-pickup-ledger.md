# CW-05m — world pickup layout and arbitration

This dependency slice adds immutable custom pickup layouts and a scene-owned
world ledger. The engine adapters, actual authored placement catalog, rendering,
public spawn-grant replacement and normal selection remain subsequent work. No
client packet or native item type activates this ledger.

Each layout holds at most 64 sorted, stable spawn IDs with custom runtime IDs,
fixed-point positions, initial delay, respawn interval and reserve refill. Its
SHA256 binds every field, the frozen manifest and the complete map identity
(room key, custom map ID, full content and package hashes). Construction copies
the input under a hard enumeration cap, validates mechanics/registry identity
and rejects malformed names rather than replacing invalid Unicode bytes.

Contact has one custom owner identity and engine-captured source/authority tick,
body/sphere geometry and pickup permissions. The biped lateral/height and alt-form
sphere tests use the native strict radius boundary. The engine producer and
funding adapter must bind these facts to the exact living current actor; the
ledger is not independent movement proof or a validator of client claims.

Collection requires the exact world/spawn/incarnation, current authority tick,
availability and valid overlap. It reserves one of 128 immutable outcome slots
before entering the funding adapter. A definite funding refusal leaves the item,
revision and queue unchanged. Success advances the global revision and item
incarnation and schedules the next availability tick. Serial contact processing
therefore admits exactly one collector for an incarnation. Delay/respawn use
simulation time, including uint wrap; rendering never advances them.

Outcome publication is FIFO. The owner releases an outcome only after reliable
publication succeeds (or synchronous offline consumption); a peer's ACK cannot
directly call this method. A full queue refuses collection before funding, leaving
the item available for retry. Baseline capture must copy item states together
with inventory on the scene owner; item reads refuse during the funding callback
to prevent a mixed transaction snapshot. Late-join transport/baseline application
is not implemented here.

An exception or malformed successful funding result is uncertain. The ledger
quarantines all further collection instead of reoffering a potentially funded
incarnation. The engine adapter must also retire the affected runtime/life when
it receives Fault. It must not refund an uncertain grant. Previously committed
outcomes remain readable for publication after retirement.
