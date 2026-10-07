# Social foundation

Slice 1 establishes durable social identity and relationship state without changing
the Project Prime gameplay protocol.

## Identity

Every player continues to use the Supabase Auth UUID already owned by Hunter
License. The social layer derives a stable public identifier from that UUID:

`PP-XXXX-XXXX-XXXX-XXXX-XXXX`

The public ID is stable across account linking/recovery because linking preserves
the underlying Supabase UUID. It is intended for exact player lookup; display
names remain presentation, not identity.

`prime.player_profiles` seeds `prime.social_profiles` through an insert trigger.
The migration also backfills every existing Hunter License profile.

## Data model

- `prime.social_profiles`: stable public Prime ID for each Hunter License.
- `prime.friend_requests`: directed pending requests.
- `prime.friendships`: one canonical unordered pair, stored as
  `player_a < player_b`.
- `prime.player_blocks`: directed blocks.

The database mutation function serializes changes for the two affected profiles.
Crossed friend requests become a friendship, duplicate sends are idempotent,
blocking removes friendship/pending state in both directions, and friendship
creation checks both players' limits.

Current limits are 200 friends, 50 outgoing requests, 100 incoming requests and
500 blocks.

## Security boundary

The social tables live in the existing `prime` schema but are not a direct
client API:

- RLS is enabled on every new social table.
- `anon` and `authenticated` table grants are explicitly revoked.
- helper/mutation functions are not executable by client roles.
- the Edge Function validates the caller with Supabase Auth and derives the actor
  UUID only from the verified Auth response.
- all SQL inputs are parameterized.
- blocked players are hidden from exact lookup in either direction.

The read policies are intentionally present as defense in depth for a future
explicit direct-read surface, but no direct table grants are part of Slice 1.

## Client API

`SocialClient` reuses Hunter License session ownership through
`HunterLicenseClient.InvokeAuthenticatedFunctionAsync`. The access token never
leaves the identity client.

Available operations:

- load social snapshot
- exact Prime ID lookup
- send / accept / decline / cancel friend requests
- remove friend
- block / unblock player

The Edge Function also invokes the existing Hunter License bridge before each
operation so a newly authenticated installation receives the same stable account
rows without creating a second identity system.

## Verification

Engineering contracts include:

- frozen Deno typechecking for the `social` function
- real Edge-handler Node tests for Auth rejection, profile provisioning, Prime ID
  validation, lookup, mutation routing and request-body ceilings
- disposable PostgreSQL acceptance for stable ID seeding, RLS/private grants,
  duplicate requests, crossed requests, friendship creation, blocking and lookup
  suppression
- the normal desktop and dedicated-server builds

## Explicitly deferred

Slice 1 does not implement Realtime Presence, online-player state, notifications,
invites, party UI, join-by-friend, recent players or gameplay/scoreboard social
badges. Those build on this foundation without changing its identity model.
