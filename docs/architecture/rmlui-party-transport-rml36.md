# RML-36: authenticated Party Edge transport candidate

This is a **staged, unconnected adapter** stacked on RML-35 / #698.

The candidate `AuthenticatedNativePartyAuthority` sends typed JSON through `HunterLicenseClient.InvokeAuthenticatedFunctionAsync`, retaining the existing guest/linked identity, serialized auth gate and token ownership. It maps snapshot, invite, accept/decline/cancel, leave, kick, and promote actions to a proposed first-party `party` Edge endpoint, using expected server revision on writes.

Reads reject malformed Prime IDs, missing lists, duplicate members, out-of-range list sizes, invalid leaders and invitation identities. Unknown presence values become Unknown; no optimistic party/presence claims are made.

**Contract proposal, NOT deployed or enabled:** The corresponding server Edge function has not been verified as present. Before wiring native UI controls, backend must enforce JWT ownership, privacy, blocked relationships, invite expiry, leader roles, maximum roster, idempotency, per-account rate limits, and atomic revision checks. Add integration tests with multiple real accounts and race/reconnect cases. If deployed API response schema differs, revise the DTOs and tests rather than assuming compatibility.

Full Settings stays last. Avalonia remains default.

## Account lease hardening, 2026-10-17

The current adapter uses `InvokeNativeFunctionWithLeaseAsync` and preserves the
auth owner's opaque session witness on the native Party snapshot. The witness
is internal and ignored by JSON. Production `IsCurrent` checks reference
identity and expiry without HTTP; the default interface implementation preserves
injected fixture compatibility. Expected-self and server-revision fields remain
mandatory for mutations.

The existing controller checks cached authority at every owner entry, including
while a command hides its visible snapshot. Account changes clear private party
state, cancel active work and retire the completion generation. Late reads must
carry a current lease before adoption; cancelled commands cannot start readback.
No travel, reservation packet or protocol change is part of this hardening.

Source-only verification passed 100 existing Party assertions and 11 new lease
assertions (`/tmp/prime-party-lease-source.log`). The production adapter and lease
helper also passed 8 source-linked checks with a closed auth-owner facade that
rejects any authentication or HTTP call (`/tmp/prime-party-adapter-source.log`).
Those 8 checks are registered in the compiled Party adapter fixture for the next
coordinated application/tool build; actual real-owner reflection results remain
pending that run. No live account or backend acceptance is claimed here.
