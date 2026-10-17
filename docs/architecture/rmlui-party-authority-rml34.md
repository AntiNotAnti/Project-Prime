# RML-34: server-authoritative Party contract (foundation)

Stacked on RML-33 / #696; not a live Party integration.

## What is in this slice
- Strongly typed Party membership, invitations, presence, command and server-revision contracts.
- UI-thread owner, bounded eight-member verification, cancellation and generation fences.
- No optimistic membership or presence; accepted commands require a fresh server read.
- Exact Prime ID and invitation validation; leader-only member removal and promotion.
- Discards previous private party data on account refresh, cancellation and errors.
- Transport exceptions never reach user-facing status.
- Asset-free fixture runs under the no-game-assets CI job.

## Not yet enabled
There is deliberately **no live authenticated Party transport or native invitation action wiring**. Do not enable Party controls or show members/online statuses until server permissions, invitation expiry, authoritative revision checking and account switching are verified end to end. Server must enforce every operation independently. Settings is last.

## Remaining acceptance
Backend implementation, privacy/presence service, invitation creation and revocation, lobby travel/reservations, native page presenter, real-device validation and full build matrix.
