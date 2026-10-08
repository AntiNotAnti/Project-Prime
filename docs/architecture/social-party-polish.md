# Social party polish

Slice 5 adds party coordination, Recent Players and Do Not Disturb on top of the
authenticated social/lobby pipeline from Slices 1-4.

A party is **social state**, not a second game session. Project Prime's dedicated
server remains the only gameplay/lobby authority. Party actions eventually resolve
through the same authenticated game-invite and Join Friend path introduced in
Slice 4.

## Party model

The backend stores:

- `prime.social_parties`
  - party UUID
  - leader Hunter License UUID
  - timestamps
- `prime.social_party_members`
  - at most one party per Hunter License
  - ordered join time
- `prime.social_party_invites`
  - sender / recipient / party
  - ten-minute pending lifetime
  - accepted / declined / cancelled / expired states

The party ceiling is eight members, matching Project Prime's multiplayer slot
capacity. Capacity checks serialize on the party row before accepting a member so
two simultaneous accepts cannot both consume the final seat.

Only the leader may invite, remove a member, promote another member or disband the
party. Inviting a friend while not already in a party creates a party with the
sender as leader.

If a leader leaves, the longest-standing remaining member becomes leader. Pending
party invitations are cancelled on every leader handoff instead of silently
attributing somebody else's invitations to the new leader. A one-member leader
leaving disbands the party.

Blocking another Hunter is stronger than party membership. Pending game/party
invites between the pair are cancelled immediately. If both Hunters are in the
same party, they are separated immediately as well.

## Party-to-game handoff

Party membership does not imply that all members are connected to one server.

When a leader is already in a verified persistent lobby, **Invite Party to Lobby**
sends ordinary Slice 4 game invitations to each other party member. Every member's
game-invite privacy and Do Not Disturb policy is still evaluated independently.

A non-leader may use **Join Leader** only when the leader is:

- still a friend,
- visible as joinable through the privacy-reduced presence feed, and
- in a server that passes Slice 4's public-directory and live
  `AuthorityEpoch` verification.

There is intentionally no automatic server migration, matchmaking teleport or
hidden reconnect. Party coordination never bypasses the normal join path.

## Do Not Disturb

`prime.social_settings.do_not_disturb` is account-level policy.

When enabled:

- new game invites are refused server-side;
- new party invites are refused server-side;
- online presence remains visible according to the existing visibility policy;
- friendship requests are unaffected.

The setting is exposed in Profile > Social privacy and as a quick DND toggle in
the RmlUi social drawer. It synchronizes through the same authenticated privacy
service as online/activity/invite visibility.

## Recent Players

Recent Players never guesses identity from a display name.

The dedicated authority already verifies each client's signed Hunter License
career ticket. Slice 5 keeps that verified membership alive while the server is
in lobby, match or post-match state and records co-presence between verified
Hunter License UUIDs.

The membership now carries a separate `lobby_eligible` bit:

- `true` only while the authority is a persistent lobby the player currently
  occupies;
- `false` during matches/post-match where the same proof is useful only for
  encounter history.

Social lobby registration requires a fresh `lobby_eligible=true` proof, so
tracking Recent Players during gameplay does not make an in-match server
Join-Friend capable.

`prime.social_recent_players` stores directed actor/other-player rows with:

- last verified co-presence time;
- encounter count.

Repeated 20-second membership heartbeats update `last_seen` but do not inflate
the encounter count. A new encounter is counted only after at least a ten-minute
gap. Each account retains its 200 most recent rows, while the social read model
returns the newest 30.

Blocked players are suppressed from the Recent Players read model.

## Realtime and polling

Party invite/member/leadership changes emit the same private per-user
`social_changed` invalidation used by game invites.

Changes are fanned out to all current party members, plus the removed member when
somebody leaves or is removed. Deleted pending invitations also invalidate their
sender and recipient.

Realtime remains an invalidation hint only. `SocialPartyClient` refreshes the
authenticated Edge snapshot and maintains a 20-second polling fallback.

## RmlUi

The existing SOCIAL drawer gains:

- **PARTY**
  - member roster
  - leader/member labels
  - pending incoming/outgoing party invitations
  - Invite to Party
  - Join Party / decline / cancel
  - Leave Party
  - leader: remove member, promote leader, disband
  - leader in a lobby: Invite Party to Lobby
  - member with joinable leader: Join Leader
- **RECENT**
  - verified recent players
  - last encounter age and encounter count
  - live presence merged when visible
  - Add Friend, game invite, Join Friend and party invite actions when applicable
- **DND**
  - fast account-level Do Not Disturb toggle

The Home session card also shows the current party state at a glance.

## Platform lifecycle

`SocialPartyClient` runs beside presence/invites on desktop and Android.
Android suspends it while the activity is backgrounded and resumes it with the
other social clients. RmlUi screenshot fixture runs remain network-free.

## Security

All new `prime` tables have RLS enabled and direct `anon` /
`authenticated` grants revoked. Edge Functions derive the actor only from a
verified Supabase Auth session.

Recent-player writes originate only from the dedicated-server membership
endpoint, whose reporter credential and signed Hunter License ticket are verified
before SQL.

No service-role key is exposed to game clients.

## Verification

Slice 5 extends the contracts with:

- frozen Deno dependency/type checks for the new `social-party` function;
- authenticated party Edge tests;
- membership tests that assert the lobby-eligibility bit and trusted
  Recent-Player touch;
- disposable PostgreSQL acceptance covering:
  - private grants/RLS,
  - DND refusal,
  - verified Recent Players,
  - party creation/invite/accept,
  - capacity-safe party state,
  - block-driven party separation.

The ordinary client, dedicated-server, Android and RmlUi native build gates remain
the final compile/runtime boundary.

## Deferred

This slice deliberately does not add:

- automatic party migration to another server;
- leader-driven matchmaking that moves the whole party without confirmation;
- reserved UDP party slots;
- party voice or text chat;
- automatic post-match regrouping;
- scoreboard friend/party badges.

The scoreboard does not currently receive stable Hunter License UUIDs for other
players, so adding those badges accurately would require an explicit,
privacy-reviewed network identity mapping rather than guessing from player names.
