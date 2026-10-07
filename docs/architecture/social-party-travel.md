# Social party travel and regrouping

Slice 6 turns the Slice 5 party into a durable coordination layer that can move
through Project Prime's existing lobby system without creating a second gameplay
authority.

The central rule remains unchanged: **party state may coordinate a move, but only
the normal dedicated-server join path may perform it**.

## Party travel intent

A leader in a verified persistent lobby automatically publishes one short-lived
travel intent for the party.

The backend stores:

- party UUID
- stable travel UUID
- revision
- leader Hunter License UUID
- verified social lobby UUID
- reason:
  - `leader_lobby`
  - `quick_play`
  - `regroup`
- 90-second expiry

The travel UUID stays stable while a leader keeps refreshing the same live
destination. A destination change increments the revision. An expired intent is
also treated as a new revision, so old decline/follow state cannot silently leak
into a later trip to the same server.

If the revision counter ever reaches its defensive ceiling, the travel UUID is
rotated and the revision returns to one.

## Server proof

A travel intent is valid only while all of these are true:

1. the publisher is still the party leader;
2. the referenced social lobby still belongs to that leader;
3. the social lobby has not expired;
4. the dedicated authority still has a fresh server-verified membership proof
   for that leader and authority epoch;
5. that membership proof is still `lobby_eligible=true`.

The same membership check is repeated when another party member resolves
**Follow Party**.

A stale social-lobby row by itself is therefore insufficient to move a party.

## Member consent

Each non-leader party member receives response state for the current travel
revision:

- `pending`
- `following`
- `joined`
- `declined`

Travel is never automatic.

The Social drawer shows **FOLLOW PARTY** and **DECLINE** when a live travel intent
is available. Declining suppresses the prominent travel notice for that revision,
but Follow Party remains available if the player changes their mind.

Clicking Follow Party:

1. marks the member as following;
2. resolves the current travel revision;
3. retrieves the opaque social lobby locator;
4. independently verifies the destination against the public server directory;
5. probes the live server;
6. requires matching protocol, lobby phase, arena and `AuthorityEpoch`;
7. hands the already-verified endpoint into the existing Play workspace join path.

When the client actually reaches the destination authority, it reports
`joined` automatically.

Blocking still takes precedence over all party state. Slice 5's block separation
removes the blocked player from the party, which also removes their travel state.

## Leader lifecycle

The leader client refreshes travel every 30 seconds while all of these are true:

- the leader still owns the party;
- the party has at least two members;
- the client is in a persistent lobby;
- Slice 4 has a current verified social lobby.

Leaving the session, entering a match, or otherwise leaving the lobby clears the
active travel intent immediately.

If the party roster falls below two members, the backend clears the travel intent
as well.

Changing party leader clears travel. A new leader must establish their own
verified lobby before another travel intent can exist.

## Post-match regrouping

The leader client remembers that the party entered match/post-match state.

When that same party returns to a verified persistent lobby, the next travel
publication uses the `regroup` reason. Party members who ended up back at the
launcher or were disconnected receive a **POST-MATCH REGROUP** prompt and may
explicitly follow the leader back into the lobby.

Members who remained attached to the same authority are automatically marked
joined and do not receive a redundant follow prompt.

## Party-aware Quick Play

Quick Play is party-aware when invoked by a party leader.

For a party of N members it selects only servers that:

- are live and protocol-compatible;
- are currently in a lobby, rather than already in-match;
- report at least N currently open player slots.

After the leader successfully joins, the client tags the next travel publication
as `quick_play`. Other party members then receive **PARTY QUICK PLAY READY** and
may follow.

### Capacity is not a reservation

Slice 6 deliberately does **not** reserve slots.

Quick Play chooses a lobby that has enough capacity at discovery time, but other
players may still consume those slots before the whole party follows. If that
happens, the existing normal join/queue behavior applies.

True reserved party admission belongs in a later server-capacity slice because
it needs authoritative reservation tokens at the UDP admission boundary, not a
social-database promise.

## Roster presentation

The PARTY view merges travel state into each member row:

- TRAVEL PENDING
- FOLLOWING PARTY
- REGROUPED
- TRAVEL DECLINED

The party header also displays the current travel destination and reason.

The existing **Invite Party to Lobby** remains available to a leader who wants to
send ordinary five-minute game invites in addition to the short-lived party
travel prompt.

## Realtime and polling

Travel-row and response changes emit the existing private per-user
`social_changed` invalidation to every current party member.

Realtime remains only an invalidation signal. `SocialPartyClient` refreshes the
authenticated snapshot and keeps its polling fallback.

## Security

The new `prime.social_party_travel` and
`prime.social_party_travel_responses` tables:

- have RLS enabled;
- revoke direct `anon` and `authenticated` grants;
- expose no client-executable helper function;
- derive every actor from the authenticated Edge session.

Travel resolution never accepts an endpoint from the caller.

The only endpoint returned is the one attached to the leader's verified social
lobby, and the joining client still treats it as untrusted until public-directory
and live-authority verification succeeds.

## Verification

Slice 6 extends the existing acceptance surface with:

- Edge tests for travel publish/follow/decline/joined actions;
- malformed/stale travel revision validation;
- PostgreSQL acceptance for:
  - leader-only publication;
  - follower state;
  - destination revision changes;
  - stale revision rejection;
  - live locator resolution;
  - joined acknowledgement;
  - travel cleanup after block/roster collapse;
  - RLS and direct-grant denial;
- the existing disposable migration execution gate;
- normal desktop, dedicated-server, Android and native RmlUi build gates.

## Deferred

This slice intentionally leaves these for later:

- authoritative party slot reservations;
- simultaneous atomic admission of the whole party;
- auto-follow without player consent;
- cross-server transfer while a member is still actively inside another match;
- party leader matchmaking that starts a new dedicated server on demand;
- party voice/chat.

Those require either server admission tokens or a broader matchmaking service,
not more social UI.
