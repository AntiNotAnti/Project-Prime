# RML-36: authenticated Party Edge transport candidate

This is a **staged, unconnected adapter** stacked on RML-35 / #698.

The candidate `AuthenticatedNativePartyAuthority` sends typed JSON through `HunterLicenseClient.InvokeAuthenticatedFunctionAsync`, retaining the existing guest/linked identity, serialized auth gate and token ownership. It maps snapshot, invite, accept/decline/cancel, leave, kick, and promote actions to a proposed first-party `party` Edge endpoint, using expected server revision on writes.

Reads reject malformed Prime IDs, missing lists, duplicate members, out-of-range list sizes, invalid leaders and invitation identities. Unknown presence values become Unknown; no optimistic party/presence claims are made.

**Contract proposal, NOT deployed or enabled:** The corresponding server Edge function has not been verified as present. Before wiring native UI controls, backend must enforce JWT ownership, privacy, blocked relationships, invite expiry, leader roles, maximum roster, idempotency, per-account rate limits, and atomic revision checks. Add integration tests with multiple real accounts and race/reconnect cases. If deployed API response schema differs, revise the DTOs and tests rather than assuming compatibility.

Full Settings stays last. Avalonia remains default.