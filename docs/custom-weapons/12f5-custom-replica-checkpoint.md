# CW-12f5 — explicit custom replica checkpoint inspection

This describes the source contract in `ReplayCustomWeaponDescriptor.cs`,
`ReplayReplicaState.CustomWeapons.cs`, `ReplayReplicaCheckpoint.cs` and
`ReplayReplicaCheckpointLayout.cs`. Local qualification is recorded in
[CW-12f5 evidence](evidence/CW-12f5.md). It extends the
[detached durable state contract](12f4-durable-replay-state.md), without opening
custom recording or playback gates.

## Inspection descriptor

`new ReplayCustomWeaponDescriptor(context)` still emits unchanged CWRD1.
`ForInspection(context)` explicitly emits CWRD2: the same historical bootstrap,
map identity and authored placement rows, followed by ushort name-byte length,
ushort mechanics-byte length, strict UTF-8 definition name and canonical
mechanics bytes. Both versions retain the 20 KiB descriptor bound and zero
reserved flags. Parsing requires canonical re-encoding; CWRD2 additionally
reconstructs its definition and checks the exact mechanics/layout context.

`InspectContext()` requires the CWRD2 metadata. It validates internal definition
and layout digest consistency without consulting a current stock table or
opening a package store. It does not establish archive provenance, prepare
resources, acquire a lease, install, download, grant or equip. Historical
package/resource ownership remains a separate binding contract.

## Explicit replica scope

`EnableCustomInspection(descriptor)` must run before Match or other accepted
replica facts. Custom facts then require an accepted matching native Match,
both Riftline M7 and packaged-weapon availability flags, exact match ID and
authority epoch, and matching room. Session configuration, when present, must
also match start generation and map identity. The descriptor alone does not
authorize arbitrary native/custom packets or synthesize a Match.

`CaptureCheckpoint()` emits replica version 15 only for an explicit custom
owner with a complete healthy custom lane and matching Match/configuration.
The header freezes protocol **83 exactly**. Required features are NativeTouch
(1), Pickups (2), Grants (4), Grapple (16) and CustomWeapons (32); TimedShots
(8) is optional. Unknown feature combinations and other protocol versions are
refused. Ordinary native writers continue emitting version 14.

The existing replica body is followed by signed int32 descriptor length and
CWRD2 bytes, then signed int32 custom-state length and CWRS1 bytes. Both lengths
must be positive and bounded by their component limits (20 KiB and 768 KiB).
The custom envelope cap is the native 512 KiB cap plus both component caps
and eight length bytes. Embedded custom recording frame cannot exceed the
outer recording frame. Complete pickup state and retained source/retry checks
remain those of CWRS1.

Only `RestoreCustomCheckpoint` and `InspectCustomCheckpointLayout` accept this
contract. Default native `RestoreCheckpoint` and checkpoint inspection always
refuse version 15. Explicit decoding validates a temporary owner before atomic
commit, including descriptor, CWRS state, Match/configuration and full body
consumption. Current protocol83 Match/Session payloads use scoped raw decoding
for this contract; native `ReplayIdentityCompatibility` conversion remains
strict and continues refusing custom availability. This adds no broad native
replay compatibility exception.

Rewind/reset clears custom context and state. Custom world time advances only
through its custom lane; an outer recording-frame update does not automatically
advance passive flights during a quiet interval. The checkpoint therefore
preserves its accepted anchors and clocks, not a claim of current quiet-frame
projectile presentation.

An actual native Match plus accepted custom facts can prove this lane's
canonical restore/suffix parity and unchanged RNG. It does not establish whole
Roster/snapshot coverage, Scene state, collision, damage payment, audio or
graphics ownership. There is no archive7 or world12 integration, resource lease
ownership, durable Demo record-close-reopen/seek, or playback gate opening in
this slice. Source, logs and assets remain independent retained evidence.
