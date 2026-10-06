# Social presence

Slice 2 builds online-player presence on top of the Slice 1 social identity and
friendship foundation. It remains a launcher/control-plane feature and does not
add or change gameplay packet types.

## Presence model

Each running client receives an ephemeral process UUID and owns one row in
`prime.social_presence_sessions`.

A Hunter License can have several simultaneous sessions. The online directory
collapses them to the freshest heartbeat for that account. This avoids a second
device being knocked offline when another device closes.

Activities are:

- `menu`
- `online` for connected/transitional network state
- `lobby`
- `in_match`
- `spectating`

The client observes local state once per second but only sends a network update
when activity changes or a 15-second heartbeat is due. Directory reads ignore
sessions older than 45 seconds. A five-minute actor-local cleanup and four-session
per-account ceiling bound abandoned rows.

Shutdown sends a one-second best-effort leave request. Correctness does not
depend on it: crash, suspend, process kill and network loss all disappear through
the 45-second TTL.

Android suspends presence in `MainActivity.OnPause` and resumes it in
`OnResume`, so a backgrounded phone is not advertised as online.

## Privacy

`prime.social_settings` stores account-level policy:

- online visibility: `everyone`, `friends`, `hidden`
- activity visibility: `everyone`, `friends`, `private`
- invite policy: `everyone`, `friends`, `nobody`

Defaults are Everyone for online visibility, Friends for activity, and Friends
for invites.

Visibility is applied inside PostgreSQL before the online-player result leaves
the database. A hidden player is not emitted. A player whose activity is private
may still appear as generic `online`, but their lobby/match/spectator state,
room key and joinability are suppressed. Blocks suppress presence in both
directions.

The Profile settings page exposes all three choices. A fresh recovered device
may adopt the server-side policy before it has ever explicitly configured local
social privacy; once the player saves a choice on that device, the client treats
the local choice as intentional and synchronizes it back to the account.

## Security boundary

Presence uses a separate authenticated `presence` Edge Function.

- the Supabase Auth session is verified before identity is accepted
- the verified Auth UUID is the only actor identity
- request bodies are bounded to 4 KiB
- activity, UUID, room-key length and privacy enums are validated before SQL
- SQL inputs are parameterized
- presence/settings tables have RLS enabled
- direct `anon`/`authenticated` grants are revoked
- helper functions are not executable by client roles

No server IP or UDP endpoint is stored in presence. Joinability currently means
"this player is in a persistent lobby"; Slice 4 can resolve a permitted room to
an invite/join target without turning presence into an address directory.

## Client read model

`SocialPresenceClient.Current` contains:

- server privacy settings
- visible online players
- public Prime ID and display name
- activity after privacy reduction
- optional joinable room key
- friend flag
- last heartbeat timestamp
- server TTL

The client publishes a change event from its worker thread. Slice 3's UI must
marshal that event onto its UI dispatcher.

## Verification

The engineering contracts cover:

- frozen Deno typechecking for the `presence` Edge Function
- Edge-handler authentication, heartbeat, privacy, leave and body-validation tests
- disposable PostgreSQL checks for RLS/private grants, multi-user visibility,
  friend-only presence, private activity, leave behavior and block suppression
- normal client and dedicated-server builds through the existing workflow

## Deferred

Slice 2 intentionally does not add the social drawer, player cards, realtime
invite notifications, join-by-friend, recent players, party migration or
scoreboard friend badges. The online feed and privacy model are ready for Slice 3
to render.
