# CW-12b — package preparation before loading

Protocol81 deliberately separates exact package preparation from both ordinary
lobby Ready and the existing bootstrap-bound CWM1 source-readiness handshake.
Native packet layouts, native weapon IDs and CWM1 encodings remain unchanged.
Live peers require an exact protocol match; protocol80 peers are refused by the
ordinary Hello boundary. Historical native replay bodies retain their reviewed
layouts and movement profile attribution. Package/custom replay still refuses.

An operator may call `DedicatedServer.ConfigureWeaponPackage(store, archive)`
before running the server. It requires an explicitly installed, fully validated
exact archive, retains a lease, and forces a persistent lobby. Lobby commands
cannot replace that selection. This foundation exposes no remote path, download
or automatic installation. The single-weapon runtime accepts one package only;
the detached CWPK contract still supports64 rows for future expansion.

The frozen availability flag uses bit20 and requires the existing custom pickup
availability bit18. IDs99/100/101 are bounded ordinary reliable background
offer/Ready/confirmation messages. Offers retry every0.5 seconds until the peer
acknowledges. A61-byte recipient key contains:

| Field | Bytes |
| --- | --- |
| Match ID | 2 little endian |
| Authority epoch | 8 little endian |
| Operator configuration nonce | 8 little endian |
| Slot | 1 |
| Slot generation | 2 little endian |
| Transport connection incarnation | 8 little endian |
| CWPK manifest digest | 32 |

Offers append the canonical186-byte single-row CWPK manifest, for247 bytes
total. Ready and confirmation echo exactly the61-byte key. Authenticated
transport, selected-host endpoint filtering and exact key comparison fence
stale/cross-slot/connection/epoch/configuration acknowledgements. A lobby Ready
revision change cannot invalidate or replace package identity. Replacement
occupants must prepare independently; spectators receive the same requirement.

The ordinary client verifies installed bytes on a worker, retains all leases and
acknowledges only after successful binding. Its default store is `weapons/`
inside the user data directory; an operator can explicitly set
`PROJECT_PRIME_WEAPON_STORE`. Missing/invalid archives produce a local error and
keep loading/gameplay frozen. No unfamiliar content is downloaded or installed.
Stopping, connection/match/epoch replacement or a cancelled worker retires
leases, including detached work that completes after cancellation. Contradictory
offers within the same match/incarnation are ignored; a retired parser must
finish before another can start, bounding validation concurrency to one. Server
shutdown/startup failure releases its binding. Uninstall cannot invalidate a
retained binding; pruning skips its archives until final release.

Actual Start, MatchLoaded, client ShouldLoadMatch/FreezeGameplay and exact-load
checks consult this barrier. **This slice still refuses package Start after all
peers prepare**, with an explicit online-execution-unavailable reason. That
prevents a verified package from accidentally running built-in M7 mechanics
before the next slice wires the exact definition, model and PCM into combat.
There is no gameplay grant, equip or custom replay activation. CLI/host allocator
selection, full resource prewarm, online package execution, real late-join world
entry, bots and replay/seek remain later CW-12 work. Android validation is deferred.
