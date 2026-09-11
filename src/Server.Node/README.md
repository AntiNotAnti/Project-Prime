# Server Node

ASP.NET Core control authority for the public-lobby client path. Node owns
sessions and lobbies, starts authenticated Worker children, places frozen match
specifications, and delivers signed UDP admission handoffs. Node never
constructs a gameplay Scene.

`NodeApplication.Build` wires the Worker pool, lobby coordinator, session reaper,
and optional Backend directory reporter. Configure Kestrel HTTPS normally. Public
control connections require TLS; HTTP control upgrades are rejected. `/health`
and `/v1/status` expose only aggregate status and bounded catalog metadata;
authenticated WSS catalog pages carry the configured map identities.

Required configuration:

- `Node:Authentication:NodeId`: persistent Node UUID.
- `Node:Authentication:Issuer`: exact HTTPS Backend ticket issuer.
- `Node:Authentication:Keys`: 1..8 `{KeyId, PublicKeyPemPath}` SPKI P-256 public keys.
- `Node:Maps`: bounded `ContentIdentity` entries with map key, content hash/version,
  build version and gameplay protocol version, matching the Worker content profile.
- `Node:MaximumWaitlistPerLobby`: bounded queue capacity (default 64, maximum
  1024), and `Node:WaitlistOfferSeconds`: seat offer window from 10 to 20 seconds
  (default 15).
- `Node:Workers:Processes`: `WorkerLaunchOptions` entries. The manager owns private
  pipe/token/incarnation arguments. Set a real Worker executable, operator-owned
  content arguments, matching `Content`, absolute artifact root, and capacity.
  The lobby path requests recorded replays, so include `--replay-dir` with an
  operator-owned writable directory in the Worker arguments as well.

Empty map/worker lists support session/lobby service only; start fails explicitly
when content or compatible capacity is unavailable. They do not launch dummy
matches. Optional `Node:Directory` settings publish to Backend; see its reporter
options. Backend outages do not invalidate connected sessions.

The launcher connects to a compatible Node, lists public lobbies, and creates
`LobbyVisibility.Public` when the user chooses Host. Node list responses contain
public lobbies only. Private or unlisted local hosting has been retired from the
client flow; a client does not connect directly to a Worker or launch one on its
own.

The release package is one Node + Worker bundle: the renamed Node apphost
`ProjectPrimeServer` is at the bundle root, `ProjectPrime.Server.Worker` is below
`worker/`, and `server.example.json` is the configuration template. The Node
resolves and supervises the Worker from its configuration. There is no
`Worker --standalone` mode and no legacy standalone Server rollback path.

Connect `wss://host/v1/control` with `Authorization: Bearer <NodeAdmissionTicket>`.
Backend account bearer credentials and gameplay tickets are not Node admissions.
Admissions use ES256, `typ=pp-node-admission+jwt`, exact issuer, audience
`urn:project-prime:node:<NodeId>`, UUID `sub/jti`, display `name`, and `iat=nbf` with
expiry at most 120 seconds later. Replay IDs are consumed once. Verification is
local and key material is loaded at startup; rotation currently requires a Node
restart with the new public key set.

Guest access uses the anonymous Backend endpoint `POST
/v1/guest-node-admissions` in every environment. The request is
`{nodeId,displayName}`; the Backend trims the name and accepts only 1–16
printable ASCII characters. A successful response contains a short-lived ES256
Node ticket with `kind=guest` and a fresh ephemeral UUID in `sub`. The Node maps
that UUID to `GuestSessionId`, never to `PlayerId`. The display name is a label
for session, lobby, chat, roster, and handoff presentation; it is not an
account, authorization, or uniqueness claim.

`POST /v1/node-admissions` remains the separate confirmed-account path. An
account admission failure is returned to the caller; the Node and client flow
never reinterpret it as guest access or retry the guest endpoint. Guest access
still requires the normal configured Backend signer and an online Node, and the
ticket is single-use at the Node.

Guests use the same Node limits as accounts. They count toward
`Node:MaximumSessions` and the lobby player/observer limits. A disconnected
guest session retains its lobby membership and active match reservation during
the 45-second resume grace; expiry removes the membership and can cancel a
match whose lobby is empty. Resume tokens remain in memory only.

When a match is frozen, any guest in the roster, including an observer, forces
`MatchSpec.TrustClass=Practice`. The Worker may still emit a local
report-ready event, but the Node validates and discards that guest artifact
before Backend outbox ownership. Guest-containing matches therefore produce no
Backend account/career or ranked report, even if the other roster members are
registered.

Control v1 JSON uses `{version,type,requestId,payload}`; server events use
`{version,type,eventId,requestId,payload}`. Shared `NodeControlCodec` enforces 32 KiB,
strict fields, duplicate rejection, source-generated DTO serialization and enum
validation. Commands include lobby create/list/join/leave, queue join/leave/
accept/decline, configure, ready, hunter, team, chat, start, return and rematch.
Mutations carry `expectedRevision` (except creation); stale updates fail. Full
bounded snapshots carry new revisions and include waitlist state. See
`docs/LOBBY_WAITLIST.md` for queue ownership, reservation, and reconnect rules.
Chat retains 16 entries; player/observer limits are 8/16. Owner start freezes ready
seats, configured bots, and rules before placement. `lobby.configure` accepts
optional `botCount` within the player capacity and `timeLimitSeconds` from 1..3600.
It also accepts an optional `pointGoal` from 1..65535; omitted values use the
selected mode default, and Survival interprets the value as its lives setting.
Terminal Worker notification transitions the
same lobby to PostMatch once. Return/rematch reopens it with fresh readiness;
a subsequent start creates a new MatchId.

Developer-only lag-compensation visuals are requested through the Node process,
not the public player control socket. Set `Node:HostAdmin:TokenFile` to a file
containing one 32..256 character printable ASCII token to map this HTTPS-only
operator endpoint:

```text
POST /v1/host/matches/{matchId}/lagcomp-debug
Authorization: Bearer <host-token>

{"action":"enable","mode":"history","seat":0}
{"action":"refresh","mode":"dynamic","seat":0}
{"action":"clear","seat":0}
```

The request body is capped at 512 bytes and the route at 30 requests/minute per
source address. With no token file configured, the route is not mapped. The
Node resolves the active frozen human seat and routes
`LagCompHistory`/`LagCompDynamic`/`LagCompClear` over the authenticated Worker
pipe. The Worker sends bounded server-selected `NetMessageType.Debug` facts or
the canonical clear packet to that player; stale matches and non-player seats
fail closed. Clients cannot request a debug command or choose a rewind tick.

Send `node.ping` every 20 seconds; `node.pong` acknowledges it. Disconnected sessions
retain membership for 45 seconds. Reconnect with `Authorization: Resume <resumeToken>`;
only a hash is stored server-side, and the opaque token rotates on every resume.
A resumed session receives its current snapshot and a freshly signed handoff for
an active match. Keep this token in memory only. Expired reservations remove
membership, transfer lobby ownership, and cancel matches whose lobby is empty.
An authenticated member can request `match.rejoin` for its current MatchId to
obtain a fresh admission ticket, limited to one retry per five seconds. This
request cannot end another member's gameplay.

Control connections have bounded outbound channels, 30 requests/second, and bounded
request deduplication. Slow clients lose their connection and can use the grace
window; they cannot block the lobby writer. Lobby notifications coalesce to one
latest immutable snapshot per bounded lobby outside the authority lock.
