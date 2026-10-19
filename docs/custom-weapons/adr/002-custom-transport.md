# CW-00 ADR B: explicit custom action lane

Status: design accepted; wire IDs/bytes deliberately deferred to CW-04 evidence.

The inspected base uses protocol **58** (`NetProtocol.cs:2543`), despite older architecture prose.
Preserve native FireEvent and historical demo layouts. Add a bounded, separately typed custom action
and capability; bump the live protocol when introducing the new contract. Capability negotiation
does not replace exact live-protocol matching.

Actions contain input intent, never damage: match, authority epoch, occupant generation/life,
shot sequence, original source frame/ack, frozen-manifest runtime ID, source pose and bounded optic/
reload/equip fields. Bind every action to the server-owned manifest and resource history.
Reject unknown identity, incompatible capability, unready package, stale life, duplicate/reordered
payment, implausible pose/cadence and excess work before allocation. All players, including bots,
submit through the same admission contract.

Reserve projectile capacity before payment; commit exactly once with accepted source/component
identity. Reuse canonical damage/claim/trade semantics only behind a typed identity adapter.
Native claim fallback cannot infer a custom weapon from a byte or accept client damage.

CW-04 must demonstrate native byte fixtures and malformed/latency tests before entry-point enablement.
CW-12 adds bounded manifest ack/readiness/reconnect and replay bootstrap. New custom recordings
must declare required schema and pinned definitions; absent required mechanics fails truthfully.
Rollback disables custom admission and rooms while preserving immutable packages and old readers.
