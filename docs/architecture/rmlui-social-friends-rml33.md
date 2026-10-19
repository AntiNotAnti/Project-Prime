# RML-33: authenticated native Friends/Requests foundation

Date: 2026-10-17
Parent: RML-32 / #695 merged into main.
Source audited: backup/main-2026-10-09.

## Historical parity gap

The original Social UI survived as pages/social/social.rml, but SocialClient,
the friends/party/presence controllers, privacy and reservation services were
absent from current main. RML-32 correctly disabled Social rather than invent
friends, parties or online activity.

This slice restores the authenticated Friends, Requests and Block foundation.
Invites, presence, party, travel and reservation require separate
server-authoritative work. Those actions stay disabled with an explanation.

## Implemented

- Restore first-party authenticated Social Edge client through the current
  Hunter License guest/linked account. The bridge never exposes bearer or
  refresh tokens to the UI/controller.
- Server-verified friends, incoming/outgoing requests and blocked players.
  8-row paging, bounded search, Prime ID lookup and exact typed permissions.
- Explicit removal/block confirmation. Mutations use an exact verified Prime
  ID and reread the server relationship after successful acceptance, rather
  than making optimistic local roster changes.
- Cancel network work on route change and reject stale worker completion
  generations. Clear cached private social data after authentication errors.
  Untrusted HTTP response messages are not displayed in error banners.
- Home/Lobby Social route becomes functional in the opt-in desktop launcher,
  with real cached Friends and pending-request counts. Presence stays unknown,
  and no friend is falsely reported as online.
- Reuse native foreground-document modal admission and the existing page manager.
  Avoid publishing unchanged bindings on every rendering frame.

## Automated acceptance

The new tools/rmlui-social-check exercises malformed IDs, pagination, lookup,
request permissions, destructive confirmation, server reread, cancellation,
stale completions, owner-thread checks and error redaction. The no-game-assets
CI job runs it. Live menu tests verify authenticated Home counts and the
disabled Social route behavior when the service is absent.

## Account lease hardening, 2026-10-17

The current production service preserves the auth owner's opaque account lease
when projecting a native social directory. The witness is internal and ignored
by JSON serialization. Every controller owner entry checks the cached lease;
account replacement or expiry immediately clears private friends, requests,
blocked rows, lookup selection, self ID and pending confirmations, cancels active
work and retires its completion generation. A late initial/refresh read also
requires a current lease before adoption. HTTPS remains on the existing worker.

A source-only build of the actual controller and the Social acceptance fixtures
passed 40 checks: 20 existing Social checks, 8 expected-self mutation checks and
12 new lease retirement checks. The new cases include cached blocked/request
rows, confirmation admission, a cancellation-ignoring late lookup/mutation,
retired read completion and witness serialization. Log:
`/tmp/prime-social-lease-source.log`. The coordinated production build and real
account/device acceptance remain separate validation.

## Outstanding

Real backend/account/device authentication, rate limits, privacy and server
authorization must be verified against an available Social service. This is
a staged opt-in foundation, not complete Social/Party parity or default cutover.

Next priorities: Party/invites/presence/privacy; native Community installer;
full License and rich Replay/Studio; game HUD/pause and Android validation.
Full Settings remains LAST.
