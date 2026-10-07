# Social lobby identity, invites and Join Friend

Slice 4 turns the Slice 1 relationship graph, Slice 2 presence feed and Slice 3
RmlUi social surface into an actual party entry path without turning Supabase
into the game server or treating a map name as a network address.

## Trust model

Three independent facts are required before Project Prime will join a social
target:

1. **The dedicated authority proves membership.** A connected client sends its
   existing short-lived Hunter License career ticket over the established
   `CareerIdentity` packet. The dedicated server posts that signed ticket,
   client ID and its current `AuthorityEpoch` to the
   `social-lobby-membership` Edge Function using the same opaque reporter
   credential used by authoritative career reporting. The Edge Function verifies
   both credentials and stores a 60-second membership proof.
2. **The authenticated player registers the lobby.** A client in a persistent
   lobby discovers the exact public directory row carrying its current
   `AuthorityEpoch`. The social service refuses registration unless the server
   membership proof for that player and epoch is live. The resulting social
   lobby UUID lasts 90 seconds and is refreshed while the player remains there.
3. **The joining client independently verifies the target.** A resolved social
   locator is treated as an untrusted hint. Before any UDP join, the recipient
   asks the public directory for the exact endpoint and then probes the live
   server. Address, port, protocol, lobby phase, active arena and
   `AuthorityEpoch` must still agree.

This prevents a player from publishing an arbitrary IP/port through Supabase,
prevents a stale invite from silently connecting to a different server that later
reused the same port, and keeps the existing dedicated server as gameplay
authority.

## Lobby identity

`prime.social_lobbies` stores short-lived social locator records:

- lobby UUID
- registering Hunter License UUID
- public IPv4 endpoint
- UDP port
- server `AuthorityEpoch`
- protocol version
- current room key
- display server name
- expiry

The endpoint never appears in the public presence feed. Slice 2 presence carries
only the lobby UUID and `joinable` flag after privacy reduction.

The social lobby is deliberately per player + authority epoch. Several friends
inside one server may therefore own separate social locator rows pointing at the
same verified authority. That makes invite ownership and account privacy simple
without changing the UDP server protocol.

## Server membership proof

`prime.social_lobby_memberships` is a short-lived backend proof, not a client
API. The dedicated server refreshes it every 20 seconds while the player is in a
persistent lobby and removes it when the peer leaves or the server leaves the
lobby state.

The `social-lobby-membership` Edge Function has the same intentional
`verify_jwt=false` relay exception as `career-report`: callers are dedicated
servers, not Supabase users. The handler authenticates the opaque
`PROJECT_PRIME_CAREER_SERVER_KEY` against
`public.project_prime_career_reporters`, then HMAC-verifies the player's
`pp1.` career ticket and its client-ID binding before writing membership.

A client cannot manufacture this proof.

## Invites

`prime.game_invites` stores durable game invites with:

- sender and recipient Hunter License UUIDs
- social lobby UUID
- pending / accepted / declined / cancelled / expired state
- five-minute invite expiry

Server-side enforcement covers:

- block lists in both directions
- recipient invite policy: Everyone / Friends / Nobody
- Friends-only validation against the canonical friendship table
- one live invite for one sender/recipient/lobby tuple
- five-second pair cooldown
- at most twenty live outgoing invites
- automatic cancellation when either player blocks the other

Accepting an invite returns a locator only while the referenced social lobby is
still live. An accepted invite remains retryable until its normal expiry, so a
transient client-side verification failure does not consume the invitation.

## Join Friend

Join Friend is stricter than Send Invite. It requires:

- an existing friendship
- no block in either direction
- target presence visible to the caller
- target activity not Private
- a fresh lobby presence session carrying a live social lobby UUID

The backend returns the locator, then the client runs the same directory + live
authority verification as an accepted invite.

There is no Join Friend from in-match state in this slice. The target must be in
the persistent lobby.

## Realtime notifications

Invite rows remain the durable source of truth. PostgreSQL emits a private
Supabase Realtime Broadcast invalidation to:

`social:user:<hunter-license-uuid>`

for sender and recipient whenever invite state changes.

The managed `SocialRealtimeClient` uses the documented Realtime WebSocket
protocol directly, joins only that private user topic with the current Supabase
Auth token, sends a heartbeat every 20 seconds, and reconnects when Auth refresh
is due. Broadcast data is never trusted as invite state; it only tells
`SocialInviteClient` to refresh the authenticated Edge snapshot.

A 15-second snapshot poll remains the fallback when Realtime is unavailable.

## RmlUi

Slice 4 adds an INVITES tab and expands row actions:

- JOIN FRIEND
- INVITE TO LOBBY
- JOIN GAME INVITE
- DECLINE GAME INVITE
- CANCEL GAME INVITE

Incoming request/invite counts share the top-bar badge. The newest incoming game
invite also appears as a compact notice in the drawer.

Rows carry the exact invite UUID as well as Prime ID, so multiple invitations
from one player cannot select or mutate the wrong record.

When a Join Friend or game-invite join passes verification, RmlUi hands the
already-verified endpoint to the existing Play workspace. The normal
`ServerBrowserService.JoinAsync`, lobby hydration, queue behavior and
`NetSession` path remain unchanged.

## Platform lifecycle

Desktop and Android start `SocialInviteClient` alongside presence. Android
suspends both services when the activity backgrounds and reconnects on resume.
Screenshot fixtures remain network-free.

## Verification

The slice extends the existing gates with:

- frozen Deno checks for `social-invites` and
  `social-lobby-membership`
- Edge-handler tests for authenticated invite operations, exact 64-bit epoch
  handling, server reporter authentication and HMAC-bound membership tickets
- disposable PostgreSQL checks for membership-gated lobby registration, RLS and
  direct-grant denial, presence lobby UUIDs, invite lifecycle, Join Friend,
  privacy suppression and block cancellation
- the existing desktop/server/Android compile gates and RmlUi native build

## Explicitly deferred

This slice does not implement party leadership, party migration across servers,
follow-the-leader matchmaking, voice/chat parties, invite-only server admission
at the UDP layer, or cross-match automatic regrouping. Those should build on the
authenticated social lobby identity rather than weakening it.
