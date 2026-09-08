# Server Node

ASP.NET Core control authority for the public-lobby client path. Node owns
sessions and lobbies, starts authenticated Worker children, places frozen match
specifications, and delivers signed UDP admission handoffs. Node never
constructs a gameplay Scene.

`NodeApplication.Build` wires the Worker pool, lobby coordinator, session reaper,
and optional Backend directory reporter. Configure Kestrel HTTPS normally. Public
control connections require TLS; HTTP control upgrades are rejected. `/health`
and `/v1/status` expose only aggregate status and configured map keys.

Required configuration:

- `Node:Authentication:NodeId`: persistent Node UUID.
- `Node:Authentication:Issuer`: exact HTTPS Backend ticket issuer.
- `Node:Authentication:Keys`: 1..8 `{KeyId, PublicKeyPemPath}` SPKI P-256 public keys.
- `Node:Maps`: bounded `ContentIdentity` entries with map key, content hash/version,
  build version and gameplay protocol version, matching the Worker content profile.
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
`FruityPrimeServer` is at the bundle root, `FruityPrime.Server.Worker` is below
`worker/`, and `server.example.json` is the configuration template. The Node
resolves and supervises the Worker from its configuration. There is no
`Worker --standalone` mode and no legacy standalone Server rollback path.

Connect `wss://host/v1/control` with `Authorization: Bearer <NodeAdmissionTicket>`.
Backend account bearer credentials and gameplay tickets are not Node admissions.
Admissions use ES256, `typ=ph-node-admission+jwt`, exact issuer, audience
`urn:prime-hunters:node:<NodeId>`, UUID `sub/jti`, display `name`, and `iat=nbf` with
expiry at most 120 seconds later. Replay IDs are consumed once. Verification is
local and key material is loaded at startup; rotation currently requires a Node
restart with the new public key set.

Control v1 JSON uses `{version,type,requestId,payload}`; server events use
`{version,type,eventId,requestId,payload}`. Shared `NodeControlCodec` enforces 32 KiB,
strict fields, duplicate rejection, source-generated DTO serialization and enum
validation. Commands include lobby create/list/join/leave, configure, ready,
hunter, team, chat, start, return and rematch. Mutations carry `expectedRevision`
(except creation); stale updates fail. Full bounded snapshots carry new revisions.
Chat retains 16 entries; player/observer limits are 8/16. Owner start freezes ready
seats, configured bots, and rules before placement. `lobby.configure` accepts
optional `botCount` within the player capacity and `timeLimitSeconds` from 1..3600.
Terminal Worker notification transitions the
same lobby to PostMatch once. Return/rematch reopens it with fresh readiness;
a subsequent start creates a new MatchId.

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
