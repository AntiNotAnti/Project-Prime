# CW-00 ADR A: native isolation and custom identity

Status: accepted for CW-01 foundation; runtime admission remains disabled.

Keep `BeamType`, native weapon tables, inventory bits, slots, saves and packet bytes unchanged.
A custom identity consists of a nonempty content UUID and exact lowercase SHA-256 mechanics digest.
The tagged native identity accepts only player weapons 0–8; platform/enemy remain native entity details.
Default/uninitialized identities are invalid. Equality includes the tag and revision.

A disposable scene/session registry owns native read-only adapters and immutable custom definitions.
Freeze a bounded map before admission; sort custom identities deterministically and reserve runtime ID
zero as invalid. Runtime IDs never cross a save/replay/wire boundary without that exact manifest.
Custom per-player resources are separate from native availability, ammo and affinity arrays.
No static custom catalog, mutable native table append, fake Imperialist or enum cast.

CW-01 verifies equality, strict decoding, corruption, duplicate definitions, session isolation,
native values/table fixtures and disposal. CW-02 binds the canonical mechanics digest to immutable rules.
Rollback: remove the unused adapter; no migration or gameplay flag is needed before an entry point exists.
